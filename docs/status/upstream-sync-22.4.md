# Upstream sync: ShareX 22.4 (develop ca3f01f94)

Branch `upstream-sync-22.4` merges upstream `develop` at `ca3f01f94` (ShareX 22.4.0 plus 349 later
commits, 643 commits since the previous merge base `54fc70341`) into `linux-port`, then adapts the new
upstream code to Linux and fixes bugs found while reviewing it.

Commits:

| Commit | What |
| --- | --- |
| `eb1dda49d` | The merge: 44 conflicts resolved, new projects retargeted, compile fixes |
| `79b62a460` | Linux follow-ups: classic ruler capture, warm-up, monitor fallback, native overlay toolbar, OCR models in packages, docs |
| `d19d5467f` | Fixes in merged features: HiDPI last-region mask, hidden region-tool shortcuts, transparent region window placement, HDR option, editor path comparison, package versions |
| `f2a7c4834` | Bugs found in review (several predate the merge): CLI file arguments, IPC fallback folder, Escape in the overlay, `$output$` splitting, process timeouts |
| (this commit) | Playback check waits for progress; this status page |

## Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Target framework | Stay on `net10.0` (upstream moved to `net11.0` on 2026-11-16) | Only the .NET 10 SDK is installed; .NET 10 is the LTS; upstream's change was the TFM only and no .NET 11 API is used. Revisit when the build machine and the PKGBUILD/Flatpak move to SDK 11. |
| OCR | Upstream's PaddleOCR (`ShareX.OCR`, ONNX Runtime CPU) replaces the tesseract implementation | The redesigned OCR window needs text geometry; PaddleOCR runs offline on Linux. The models (30 MB) ship in `Models/` next to the executable. tesseract is no longer a dependency. |
| ReadyToRun | Upstream's "do not precompile third-party assemblies" target only runs for Windows runtimes | It exists for Authenticode signing; the Linux publish precompiles Avalonia/Skia for startup speed. |
| Region capture window | Upstream's rewrite plus the port's coordinate model (image pixels / Wayland logical desktop units / DIPs) on every new conversion | HiDPI Wayland screenshots have more pixels than their desktop area. |
| Native overlay | Kept for the common case; multi-region mode and ellipse/freehand region tools (visible or with a shortcut) use the window overlay | The native renderer draws single rectangle selections. |
| Photo editor TIFF | ffmpeg instead of GDI+ (`ShareX.PhotoEditor/Linux/FFmpegImageCodec.cs`); also the fallback decoder for AVIF/HEIC | No System.Drawing on Linux. |
| Windows-only additions | Display affinity (capture exclusion), HDR tone mapping, transparent window re-capture, DWM/subclassing: guarded or hidden | No Linux equivalent. |

## Verification

All test harnesses were run headless with no display variables, a private D-Bus (`dbus-run-session`) and
throw-away `HOME`/`XDG_*` folders under `/tmp/sx-*`, before the merge (baseline) and after each step.

| Harness | Baseline | Final | Notes |
| --- | --- | --- | --- |
| Core, Editor, Effects, Compositor, Linux.Bugs, OverlayRender, OverlayHost, Linux.Capture, Input.Linux | pass | pass | Effects: 22 instead of 14 effects are now unchanged at default settings because upstream stopped resampling same-size draws (they used to blur slightly). |
| Integration | pass | pass | New check: editor commands take their file argument (fails on the pre-fix CLI with the files listed as uploads). Caught the task settings crash below. |
| Linux.Tools | 2 display-only failures | 3 display-only failures | OCR check now runs PaddleOCR and passes. Clipboard viewer, ruler and the new classic ruler need a display. The playback check waited a fixed 450 ms and failed 3 of 6 runs while OCR from the previous check still used the CPU; it now waits up to 3 s for playback to advance (0 of 2 failures after). |
| Platform, Startup | fail without a display | same | `xdg-open` and X11 are needed. |
| OverlayIntegration, OverlayHost, OverlayRender (`run.sh`, `--headless`) | pass | pass | OverlayIntegration: 27 checks. Running it with `dotnet run` instead of `run.sh` starts the test binary as the helper and fails the supervision check; use `run.sh`. Its annotation check caught the toolbar jump below. |

Also verified:

* Self-contained single-file publish (`scripts/linux/publish.sh --out <dir>`): builds; `Models/` with both
  PaddleOCR models is next to `ShareX` (`AppContext.BaseDirectory`); binary 262 MB (was 243 MB).
* `install-user.sh` and the PKGBUILD now copy directories from the publish output (they only copied files,
  so OCR models would have been missing). `bash -n` passes; the install itself was not run.

Bugs found and fixed (with how they were found):

* Task settings crashed when compositor/xrandr monitor discovery failed (Integration harness): upstream's new
  toast-monitor list enumerates monitors; `DesktopScreen` now falls back to Avalonia's screens.
* Classic ruler (new tool) did not open: GDI capture, `DllNotFoundException: user32` (Linux.Tools harness).
* Native overlay annotation toolbar jumped out from under the pointer on monitors narrower than the 22.x
  toolbar (OverlayIntegration harness); it now stays inside the desktop and stops following the pointer once
  the user interacts, as upstream's window overlay does.
* `sharex -PhotoEditor/-PixelEditor/-VideoTrimmer/-AnimatedGifTrimmer <file>` uploaded the file instead of
  opening it (code review; regression check added).
* Multi-region last-region capture masked the wrong area on HiDPI Wayland (code review).
* Escape that cancelled an annotation also closed the capture on key-up (code review).
* Single-instance socket in a predictable shared `/tmp` folder without `XDG_RUNTIME_DIR` (code review).
* `$output$` in custom FFmpeg recording commands split on spaces in the output folder (code review).
* `--doctor` and the compositor reload after `--install-binds` could hang; timed-out helpers were not reaped.

## Not verified / known gaps

* Nothing was run on the live desktop: region capture, the native overlay, recording, the Photo and Pixel
  editors, the transparent region capture and notifications were checked by code review and headless
  harnesses only. These need a manual pass on Hyprland (and ideally KDE/GNOME) before a release.
* Photo/Pixel editors: fonts default to "Segoe UI" (falls back to the system font on Linux); AltGr may act as
  Alt for the eyedropper spring shortcut; saved window positions are ignored by Wayland compositors.
  Cosmetic; reported, not changed.
* Photo editor TIFF open/save runs ffmpeg on the UI thread (bounded to 60 s); fine for normal images.
* GIF/WebP/APNG recordings no longer capture audio; the recorder still captures H.264 segments and converts
  at the end, so very long GIF recordings take a while to finish (abort now skips that step).
* The second instance does not pass a Wayland activation token to the running instance (upstream added the
  Windows equivalent, `AllowSetForegroundWindow`).
* `README.linux.md` still says the personal folder is not redirected by `$HOME`/`XDG_*`; the code uses XDG
  folders now.
