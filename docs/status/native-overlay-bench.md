# Native overlay render benchmark

Step 1 of `native-overlay-plan.md`; measured 2026-10-20 against application source
`4085bb4071dbebf836091633faf1dc9aa3847427`. No production overlay changes.

Machine: Intel Core Ultra 7 265KF, 20 physical cores / 20 logical CPUs (all allowed),
31.1 GiB RAM; Linux 7.2.11-1-cachyos x86_64; .NET SDK 10.0.402, Release net10.0;
Avalonia/Headless/Skia 12.1.4, SkiaSharp 3.119.4. Shared host, no CPU pinning,
frequency lock, or claim of a hard real-time bound. Maxima include scheduling/GC outliers.

Reproduce from the repository root (only NuGet restore may use the network):

```bash
tests/ShareX.OverlayBench/run.sh /tmp/sharex-overlay-full.json
python3 tests/ShareX.OverlayBench/report.py /tmp/sharex-overlay-full.json
```

The runner sources `scripts/linux/env.sh` (uses `~/.dotnet`), builds Release, unsets
DISPLAY/WAYLAND_DISPLAY, and selects only Avalonia.Headless + Skia. It never calls
`UseDesktop`. The benchmark removes the production `OnOpened` handler before showing
any **headless** window: no placement, cursor query, window enumeration, DBus, X11,
Wayland, layer-shell, screen capture, or native window creation is used. A deterministic
striped SKBitmap stands in for the frozen desktop; input uses Headless mouse injection.
Production workspace loading, annotation tool, HUD/magnifier, selection rendering,
controls and themes are used. Native startup is replaced with explicit workspace setup.

Raw per-event evidence is retained in [native-overlay-bench.samples.json.gz](native-overlay-bench.samples.json.gz).
The report generator accepts that archive directly. Startup-to-visible time and
steady-state overlay memory are outside this render benchmark; the plan’s 150 ms
startup and 300 MB memory targets are not validated here. The harness-wide process
peak was 894.8 MiB across all 32 sequential cases, including the largest A surface,
multiple B workspaces and framework caches; this is not an isolated steady-state B
measurement and does not demonstrate the 300 MB requirement.

## Layouts

| Case | Monitor logical sizes, left to right | Scale | Raster surface(s), physical pixels |
| --- | --- | --- | --- |
| 1440p | 2560×1440 | 1.0 | 2560×1440 |
| 4K | 2560×1440 | 1.5 | 3840×2160 |
| Dual (lag case) | 1920×1080 + 2560×1440 | 1.5 | A: 6720×2160; B: 2880×1620 + 3840×2160 |
| Triple | 2560×1440 + 2560×1440 + 1920×1080 = 7040×1440 bounding box | 1.5 | A: 10560×2160; B: 3840×2160 + 3840×2160 + 2880×1620 |

The supplied dual-monitor dimensions are treated as **logical**, matching the lag-case
headless design in the plan. The 4K case is explicitly 3840×2160 physical. A includes
black space below the shorter monitor; B allocates only each monitor's actual extent.

## What A and B mean

**A:** one desktop-sized, retained CPU framebuffer, rendering the current unmodified
`RegionCaptureWindow`. Every event copies the entire framebuffer to a presentation
buffer, then diffs it. This uses the archived retained-frame idea rather than the
original allocate-and-clear-every-frame implementation, so it is a stronger baseline.

**B (simulation):** one independent headless `RegionCaptureWindow` and retained CPU
framebuffer per monitor, cropped synthetic screenshot and local coordinates. Mouse,
selection drag and annotation run on the first largest monitor. Only that monitor
receives those events. Hover switches across monitors each event: both the old highlight
and new highlight are damaged and rendered; other surfaces must remain idle. In the
single-monitor cases hover alternates a full-monitor fake window and no window.
Each touched framebuffer is diffed against its previous image: vectorized row equality,
then first/last changed pixel on changed rows, yielding one conservative bounding box.
Only that box is copied to the presentation buffer and previous-image cache. There is
no full-frame temporary readback in B. Diff still scans the whole **touched** monitor.
A/B share the same diff algorithm and retained-surface implementation; execution is serial,
with no parallel-monitor speedup assumed. No GPU, native presentation or compositor is simulated.

B is a local-workload architecture experiment, **not** a ready multi-monitor host:
independent toolbar/HUD layout, no shared annotation document, no cross-monitor drag,
no mixed-DPI translation, no global toolbar placement, and no pixel-equivalence claim
between stitched B and A. Those integration costs are not measured. B does preserve and
verify each local raster frame; the old/new hover monitor work is included in event cost.
The archived `NativeOverlayHost`, `RetainedHeadlessFrames`, `RegionDimLayer` (inside
`RegionSelectionOverlay.cs`) and host tests (j)/(k)/(l) were inspected with `git show`.
Only the retained-frame adapter idea/code was reused in this test project. The static
dim/interior-blit optimization is **not** ported: these numbers measure today's renderer.
No branch was merged.

## Timing and correctness

Each of 32 cases has **40 warmup events + 240 measured events**; setup and 12 initial
settling ticks are excluded. Every event synchronously completes CPU rendering with
`ShouldRenderOnUIThread=true` before diff/copy. No input coalescing, event dropping,
frame pacing, sleeps or display refresh time are included. Percentiles are nearest-rank.

Mouse motion has no selection. Selection/rectangle motion uses a fixed (220,220) logical
anchor and moves the endpoint through x=400..1831, y=400..953, including periodic jumps;
only the first largest monitor is touched. Rectangle uses the actual Rectangle toolbar
tool and a full-desktop selection (clipped to the monitor for B); it is one continuous
in-progress annotation, not hundreds of accumulated shapes. Hover injects synthetic
window candidates through the existing private window list, then runs actual hover input.
A highlight covers the entire target monitor, not a small sample window.

Fail-fast checks verify fractional-scale raster dimensions, actual nonempty selection,
completed rectangle annotation, full-monitor hover dimensions, at least one rasterized
surface per event, and zero raster activity on untouched B monitors. After **each** event,
the copied presentation buffer is compared byte-for-byte with its complete retained
raster, outside the measured interval. Thus a missed dirty region fails the run rather
than making it look fast. The private framebuffer ABI is pinned and fails closed.

Total = injected input + Avalonia jobs/layout/render recording + CPU raster/composition
+ diff + framebuffer copy/cache update + small harness bookkeeping. CPU raster is the
wall time from framebuffer lock to unlock, including Skia scene replay, blending and
raster composition (not a function-level Skia profiler). Avalonia is render/input elapsed
minus that lock duration; editor backing-store work before the lock is included there.
Copy includes maintaining the previous-frame cache. There is **no GPU readback**; direct
CPU framebuffer access costs no separate readback phase. Assertions and final full-buffer
verification are excluded except the small input-state/surface-count checks.

## Per-event frame cost

All entries are **p50 / p95 / max milliseconds**, 240 events per entry. This is
the complete CPU frame pipeline through copy-out, not just the Skia draw call.

| Layout | Event | A: whole desktop | B: independent monitors + dirty copy |
| --- | --- | ---: | ---: |
| 1440p | Mouse | 3.51 / 4.90 / 26.53 | 2.04 / 2.24 / 25.33 |
| 1440p | Selection | 76.22 / 77.32 / 79.13 | 75.32 / 76.47 / 77.68 |
| 1440p | Rectangle | 74.44 / 75.90 / 76.99 | 74.28 / 75.39 / 76.74 |
| 1440p | Hover | 75.38 / 76.82 / 78.15 | 75.35 / 76.39 / 77.76 |
| 4K | Mouse | 5.60 / 6.11 / 62.76 | 4.10 / 4.73 / 63.73 |
| 4K | Selection | 189.06 / 191.05 / 193.29 | 186.00 / 188.40 / 190.45 |
| 4K | Rectangle | 183.76 / 185.51 / 186.08 | 183.44 / 185.27 / 188.55 |
| 4K | Hover | 185.97 / 188.45 / 190.16 | 192.75 / 198.27 / 202.91 |
| Dual | Mouse | 9.17 / 10.27 / 69.31 | 4.10 / 4.58 / 60.45 |
| Dual | Selection | 336.98 / 349.72 / 354.64 | 185.94 / 191.17 / 193.95 |
| Dual | Rectangle | 329.25 / 331.60 / 334.44 | 182.84 / 184.91 / 187.55 |
| Dual | Hover | 330.32 / 333.03 / 336.79 | 291.20 / 294.89 / 296.79 |
| Triple | Mouse | 11.15 / 13.49 / 69.33 | 4.08 / 4.53 / 60.37 |
| Triple | Selection | 500.42 / 505.80 / 509.31 | 188.39 / 190.92 / 193.64 |
| Triple | Rectangle | 489.33 / 492.91 / 499.67 | 184.34 / 186.38 / 188.20 |
| Triple | Hover | 498.92 / 504.03 / 508.27 | 292.52 / 375.72 / 380.40 |

## Where rectangle and hover time goes

Arithmetic **mean milliseconds per event** (means add; percentiles do not). Raster is
summed across both touched surfaces on B hover transitions. Copy volume counts bytes
copied to the presentation buffer, excluding the previous-frame cache update.

| Layout | Mode/event | Avalonia input/layout/record | Skia raster/composition | Diff | CPU copy/cache | Copy MiB/event | Dominant |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1440p | A Rectangle | 0.17 | 72.23 | 1.14 | 0.97 | 14.06 | Raster |
| 1440p | A Hover | 0.28 | 73.26 | 0.05 | 1.84 | 14.06 | Raster |
| 1440p | B Rectangle | 0.14 | 72.93 | 1.03 | 0.26 | 1.66 | Raster |
| 1440p | B Hover | 0.23 | 73.11 | 0.04 | 1.80 | 14.06 | Raster |
| 4K | A Rectangle | 0.19 | 179.48 | 2.47 | 1.59 | 31.64 | Raster |
| 4K | A Hover | 0.21 | 181.68 | 0.10 | 3.89 | 31.64 | Raster |
| 4K | B Rectangle | 0.17 | 180.31 | 2.36 | 0.59 | 3.74 | Raster |
| 4K | B Hover | 0.22 | 187.10 | 0.09 | 5.48 | 31.64 | Raster |
| Dual | A Rectangle | 0.16 | 321.95 | 4.44 | 2.60 | 55.37 | Raster |
| Dual | A Hover | 0.26 | 322.44 | 0.92 | 6.70 | 55.37 | Raster |
| Dual | B Rectangle | 0.19 | 179.65 | 2.38 | 0.59 | 3.74 | Raster |
| Dual | B Hover | 0.37 | 283.44 | 0.18 | 7.12 | 49.44 | Raster |
| Triple | A Rectangle | 0.17 | 478.73 | 6.57 | 3.90 | 87.01 | Raster |
| Triple | A Hover | 0.26 | 486.05 | 3.50 | 9.45 | 87.01 | Raster |
| Triple | B Rectangle | 0.16 | 181.35 | 2.31 | 0.58 | 3.74 | Raster |
| Triple | B Hover | 0.39 | 310.67 | 0.18 | 7.89 | 54.05 | Raster |

## Validation

Release builds passed for `tests/ShareX.OverlayBench/ShareX.OverlayBench.csproj`
and `ShareX/ShareX.csproj` using `scripts/linux/env.sh` and the local .NET SDK.
The benchmark build had zero warnings/errors; the application build had existing
platform analyzer warnings and zero errors. All 7,680 measured events completed,
and the archived samples were checked for exactly one rasterized surface/event,
except B multi-monitor hover, which correctly rasterized exactly two.
The initial smoke suite also passed. No real display was used. The requested
checkpoint commit could not be created because `.git` is read-only (Git could not
create `index.lock`).

## GPU feasibility (inspection only)

The installed Avalonia.Skia API includes `ISkiaGpu`, `TryGetGrContext`, GPU render
sessions and GL contexts, but `.UseSkia()` alone does not turn a headless framebuffer
into a GPU surface. This adapter supplies only `IFramebufferPlatformSurface`, whose Skia
`FramebufferRenderTarget` rasterizes into an `ILockedFramebuffer`. Local package XML
(`~/.nuget/packages/avalonia.skia/12.1.4/lib/net8.0/Avalonia.Skia.xml`) documents both
paths. The Headless package exposes framebuffer format/scaling, not a ready offscreen
GPU window/surface option.

EGL/GL/GBM driver libraries and NVIDIA/Mesa EGL vendor manifests exist. Host driver
metadata identifies an NVIDIA GeForce RTX 5070 Ti, but **both `/dev/dri` and
`/dev/nvidia*` are absent in this execution environment**. Hardware headless GL is
therefore not available to this run.
A separate surfaceless EGL/device context, platform graphics/surface integration and
GPU synchronization/readback (or a zero-copy presentation path) would be required on a
machine exposing a GPU. Software EGL/llvmpipe would not demonstrate GPU acceleration.
No GL context, display connection, GPU implementation or GPU performance claim was made.
These CPU results cannot establish whether the full GPU option B meets the targets.

## Five-line conclusion

1. B dual-monitor mouse: p95 4.58 ms, max 60.45 ms; does not meet the strict 16 ms target.
2. B rectangle: worst sampled frame 188.55 ms across layouts; fails the 33 ms no-frame-over target.
3. B full-monitor hover: worst sampled frame 380.40 ms across layouts; fails the 33 ms no-frame-over target.
4. Per-monitor dirty copy reduces transfer work, but CPU Skia raster/composition dominates the slow cases; GPU B remains untested.
5. Use the plan’s C fallback for dim/selection/hover with a static frozen layer and retained, dirty-region annotation rendering; benchmark that change before native hosting.
