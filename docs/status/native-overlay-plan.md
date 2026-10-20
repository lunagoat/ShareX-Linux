# Native overlay: plan (DRAFT, edit me)

Status: approved by the user (Option B, benchmark first). Step 1 completed 2026-10-20: [headless CPU benchmark results](native-overlay-bench.md). Step 2 renderer-only C prototype implemented in `ShareX.OverlayRender` (see the C results in the benchmark report); Step 4b headless Avalonia annotation layer implemented in `ShareX.OverlayAvaloniaLayer` and benchmarked across all 4 layouts (all 20 targets PASS; see benchmark report); no native surface host, input/Esc path or compositor testing is included. The first attempt is archived at tag `backup/native-overlay-2026-10-19` (never pushed); we are starting over with this spec.

## Goal

Pressing the region-capture key on Linux Wayland must feel like Windows ShareX:

- The screen freezes and dims across **all monitors at once**, in one continuous selection space.
- The same UI as Windows: crosshair, magnifier, info text, window/control snapping, the top toolbar, all annotation tools, the same keys and mouse actions.
- It appears instantly and tracks the mouse without lag.
- It works on Hyprland, niri, sway, KDE **and GNOME**, with the full toolbar on every one of them.
- Fullscreen games: the region selector does not need to cover them (Windows ShareX can't either). The "capture entire screen/window" hotkeys, which show no overlay, must keep working over games.

"Same as Windows" is the acceptance test. When in doubt, copy the Windows behaviour. Don't invent new Linux behaviour.

## Windows parity checklist (what "done" means)

Grouped. Each item gets a manual check on at least Hyprland and one other compositor.

1. **Frozen background.** Screenshot of the whole desktop taken first, shown unchanged and dimmed (`BackgroundDimStrength`). The selection area shows undimmed.
2. **Selection.** Drag to select, move and resize handles, min size, Shift/Ctrl modifiers, arrow keys nudge, Enter confirms, Space/tilde as in the current `RegionCaptureWindow`.
3. **Window snapping.** Hover highlights window or control under the cursor (`DetectWindows`, `DetectControls`), click captures it.
4. **Crosshair and magnifier.** Centre crosshair, optional screen-wide crosshair, magnifier with pixel grid, info text (`ShowInfo`, `CustomInfoText`).
5. **Toolbar and tools.** Top toolbar, every annotation tool the Windows build has (rectangle, ellipse, line, arrow, text, blur, pixelate, highlight, step, freehand, cursor, speech balloon, image), undo/delete.
6. **Mouse actions.** Right-click, middle-click, X1, X2 do what `RegionCaptureAction*` options say (including "capture fullscreen" / "active monitor").
7. **Esc cancels** and always works, including when the overlay is stuck.
8. **Multi-monitor.** Selection can span monitors; mixed scales (e.g. 1.0 and 1.5) are correct; monitor order and offsets follow the compositor, not XWayland.
9. **Result.** The crop matches the pixels under the selection on every monitor, then the normal after-capture flow runs (clipboard, save, upload, editor).

Reuse: `RegionCaptureWindow` (Avalonia) already implements items 2–6 for the X11 path. Do not rewrite UI logic unless an approach below forces it.

## Hard requirements (safety)

These are what went wrong last time.

1. **Never lock the user out.** Evidence: Print on the real desktop made the overlay unresponsive and the user had to power off.
   - Watchdog in a separate process or thread: if the overlay stops producing frames or handling input, it unmaps itself and exits.
   - Esc handled at the lowest level so it works even if the UI thread is stuck.
   - The main ShareX process kills the overlay process after a hard timeout.
2. **Never grab keyboard focus until the first frame is actually on screen.**
3. **No input backlog.** Coalesce mouse moves; never queue unbounded events.
4. **Fallback.** If the overlay can't start, fall back to the plain Avalonia overlay window and log why (no silent failure).
5. **Testing never touches the user's real compositor** (`wayland-1`) without explicit say-so. Nested sway/niri/Hyprland only. Every test script refuses to run when `WAYLAND_DISPLAY` is the real one.

## Lessons from the first attempt

- Rendering the whole multi-monitor area as one big headless Avalonia surface was too slow (a 7040×1440 logical area at 1.5× scale). Full-frame repaints on every mouse move caused the lockout.
- What fixed most of it: retained framebuffer (about 100 ms to 9 ms per frame), a static dim layer plus an undimmed interior blit, dirty rects in logical pixels, parallel diff, a coalescing input queue.
- **Still slow:** drawing with an annotation tool, and window-hover highlights over huge areas.
- Bug class to avoid: frame sizes at fractional scale (the frame timer died on a wrong-size frame at 1.5×). All coordinates logical, convert to physical in one place only.

## First decision: which architecture? (decide before any code)

| Option | Idea | Pros | Cons |
| --- | --- | --- | --- |
| **A. Headless Avalonia into layer-shell buffers** | Previous design, one surface per monitor this time | UI logic reused as is | CPU render cost; the still-slow cases |
| **B. Render per monitor, only changed regions, GPU** | Same as A but each monitor renders and damages independently, GPU or Skia-GPU if available | Lag scales with the monitor you are on, not the whole desktop | More plumbing (EGL/dmabuf) |
| **C. Native drawing, no Avalonia in the overlay** | Small Skia scene (dim, selection, crosshair, magnifier) drawn directly; toolbar and tools as light native widgets | Fastest, most robust | Two UIs to keep in sync with Windows; large effort |
| **D. Keep the X11 window, fix placement per compositor** | One overlay window per monitor positioned through compositor rules/IPC | Smallest change | Fullscreen games stay uncovered; it's what we have and Hyprland multi-monitor misaligns |

Recommendation: **B**, prototyped on one monitor first. Measure frame cost with a fixed script before building anything else. If B can't hit the targets, C for the dim/selection layer only.

## Targets (measurable)

- Overlay visible within 150 ms of the frozen screenshot being ready.
- Mouse-move to updated frame under 16 ms at 1.5× scale on a 2-monitor 4K-ish desktop.
- Drawing a rectangle annotation: no frame over 33 ms.
- Window-hover highlight over a full-monitor window: no frame over 33 ms.
- Memory under 300 MB for the overlay process.

## Work plan

Each step ends with a commit and a working build. Steps 1–2 are research with no UI.

1. **Benchmarks first (about 1 hour).** A headless test that renders `RegionCaptureWindow` at the target sizes and reports per-event frame cost for: mouse move, drawing a rectangle, hover highlight. This is the number every option must beat.
2. **Prototype the chosen option (about 1 day).** One monitor, dim plus selection plus Esc, nested compositor only.
3. **All monitors, mixed scales.** Layer surfaces per output, global logical space, coordinates test with fake monitor layouts.
4. **Safety layer.** Watchdog, Esc path, timeouts, fallback. Test deliberately by freezing the UI thread.
5. **Parity pass.** Walk the checklist above, fix gaps.
6. **Real-desktop test.** Only when the user says go. One short run, then report.
7. **Release** as a normal CI release after the user approves.

## Who does what

- **codex:** Wayland session code (layer-shell, buffers, seats), benchmarks.
- **agy:** hosting and glue (input injection, process plumbing), tests.
- **Claude:** spec, review, integration, the safety review, all git/release steps.

## Decisions (answered by the user)

1. Covering fullscreen games with the region selector: not required. Same as Windows; the no-overlay hotkeys (entire screen / window) are the answer there.
2. KDE and GNOME get the toolbar too, "as close to Windows as possible". The plain overlay is not an acceptable permanent path anywhere.
3. Ship as fast as possible; no staging requirement.

## Consequence of decision 2: a backend per compositor family

GNOME's Mutter does not implement wlr-layer-shell, so layer surfaces are not available there. The overlay needs an `IOverlaySession` with more than one backend:

| Backend | Compositors | How |
| --- | --- | --- |
| Layer-shell | Hyprland, niri, sway, KDE (checked: nested KWin 6.8 advertises `zwlr_layer_shell_v1` v5 with 2 outputs; still to verify it stacks above Plasma panels) | one overlay-layer surface per output |
| Fullscreen windows | GNOME (mutter is not installed on this machine, so untested), and KDE only if layer-shell stacking fails | one borderless fullscreen toplevel per output, placed by the compositor (KWin script on KDE; Mutter has no scripting, so test what fullscreen-per-monitor gives) |

All backends will share the C scene renderer, frame cache and input path (the benchmark-selected fallback; small Avalonia toolbar/text/dialog surfaces remain separate). Only surface creation and input plumbing differ. Step 3 of the work plan must verify the KDE and GNOME rows by experiment before relying on them.


## Step 4a: controller and host logic (2026-10-20)

Implemented in [ShareX.OverlayHost](../../ShareX.OverlayHost/README.md): pure controller,
extension seam, paced/coalesced session host, independent watchdog, lock-free dispatch
Esc cancellation, and native-resolution capture tiles. Native keyboard focus waits for
first-frame callbacks on all outputs. Scripted fake-session/fake-clock tests and a guarded
synthetic-screenshot smoke executable accompany the library. See the README for the
explicit RegionCaptureWindow behavior list, ownership/threading contracts and differences.

This is library work, not application activation or approval to test a real compositor.
Application fallback, a parent-enforced process hard timeout, annotation/toolbar worker,
final capture composition and compositor validation remain separate integration work.

Validation: 119 scripted host checks and the existing retained-renderer correctness
suite pass headlessly. Release `dotnet build ShareX.sln` passes; the final incremental
build reports zero warnings/errors (the earlier clean compilation reported existing
application warnings). The smoke executable's refusal path was checked with both an
unset display and a non-sx socket, returning 9 before opening a session. No real or
nested display connection/window was attempted. Git checkpointing failed because
`.git/index.lock` cannot be created on the read-only Git directory; changes are uncommitted.
