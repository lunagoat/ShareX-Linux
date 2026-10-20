# Performance report: perf

Code-reading audit and display-free micro-measurements, 2026-10-20. No application UI, compositor connection, live display, upload service, release script, or user configuration was used. Read `docs/LINUX_PORT.md` and `docs/COMPATIBILITY.md` first. Existing behavior and UI are preserved.

## Changes

1. **Avoid the encoded-image copy** — `ShareX.HelpersLib/Helpers/SkiaImageHelpers.cs`, `ImageToBase64`: convert only the populated region of the owned `MemoryStream` buffer, rather than allocating and copying it with `ToArray()`. Before → after: **28.595 → 27.494 ms/op**, **15,054,292 → 12,694,540 managed bytes/op** for a 1024×768 BMP (3.9% less time, 15.7% fewer allocated bytes). Encoding and Base64 output are identical; spare stream capacity is excluded.
2. **Reuse SQLite batch insert commands** — `ShareX.HistoryLib/Managers/HistoryManagerSQLite.cs`, `Append`: allocate ten typed parameters once and reuse the command/prepared statements for every row, retaining the existing lock, transaction, SQL, JSON serialization and `last_insert_rowid()` assignment. Before → after: **43.135 → 22.426 ms per 5,000-row batch**, **21,653,264 → 13,414,912 managed bytes/batch** (48.0% less time, 38.0% fewer allocated bytes). Single-row uploads are not claimed to gain the batch speedup.
3. **Avoid the managed animation-file snapshot** — `ShareX.HelpersLib/SkiaAnimatedImage.cs`: read the file stream directly into retained `SKData` instead of `ReadAllBytes` followed by `CreateCopy`. The native encoded snapshot remains independently owned, preserving decoding after source replacement. Before → after opening a 2.25 MiB BMP: **0.487 → 0.224 ms/op**, **2,359,736 → 701 managed bytes/op** (54.0% less time, over 99.9% fewer managed allocated bytes). This removes the transient managed file-sized array; native storage is still required.
4. **Add reproducible headless checks and report** — `tests/ShareX.Performance.Tests/Program.cs`, `ShareX.Performance.Tests.csproj`, and `docs/status/perf-perf.md`: record timing/allocation results and verify unchanged image/history/snapshot behavior without application startup. No production runtime cost.

## Findings left unchanged

1. **Compositor information and pointer processes** (`ShareX.Avalonia/Linux/LinuxScreenCapture.cs`, `ShareX.HelpersLib/Linux/LinuxWindowSystem.cs`, `ShareX.Tools/Linux/DesktopTools.cs`, `ShareX.Avalonia/Linux/LinuxCaptureWindows.cs`): monitor discovery already caches for 1 second; helper window enumeration caches for 100 ms and supports forced refresh. Cursor position, active window and placement verification are time-sensitive. A mouse halo can request cursor position about 60 times/second and launch `hyprctl` on Hyprland. Caching pointer positions would change motion latency; reliable event-driven IPC and session/hotplug invalidation need their own implementation and desktop validation. No additional cache added. No real subprocess/display timings claimed.
2. **UI blocking and synchronous compatibility APIs** (`ShareX.Avalonia/Linux/LinuxScreenCapture.cs`, `ShareX.HelpersLib/Linux/LinuxSettingsProtector.cs`, `LinuxGlobalShortcutsPortal.cs`, `LinuxX11Hotkeys.cs`, `PrintHelper.cs`, `LinuxDesktop.cs`): synchronous portal capture/cursor bridges, secret-tool lookup/store, printing/opening and shutdown waits remain. Secret-tool operations have 2-second deadlines and portal closing has an 800 ms wait. Converting callers throughout the UI requires lifecycle/error-ordering work beyond a local optimization; removing waits could change observable completion and failure semantics. These are findings, not measured fixes.
3. **History reads** (`ShareX.HistoryLib/Managers/HistoryManagerSQLite.cs`, history views): load still materializes all rows and deserializes tags; baseline 5,000-row load was 23.300 ms / 15,965,472 managed bytes. History windows already load asynchronously and debounce filtering at 220 ms. Paging, persistent result caches or skipping tags could change filtering/sorting and freshness; no such changes made. SQLite delete already reuses its command inside a transaction.
4. **Timers and idle wake-ups** (inventory below): TaskManager updates progress from task events; its 50 ms delay is only the explicit tray-icon test. Uploader polling exists only during active uploads. Remaining notable open-tool costs are paused media playback (~60 callbacks/s), video controls (5/s while open), recording pause monitoring (50/s while a recording is paused), and notification hover tracking (25/s while visible). Changing those safely needs mpv/input wake-up integration or changes to timing; retained to preserve seeks, frame stepping, resize, hover and stop responsiveness. No application-wide fast polling timer was found in the inspected paths.
5. **Startup and settings** (`ShareX/Program.cs`, `ShareX/SettingManager.cs`, `ShareX/Infrastructure/ApplicationSettingsRuntime.cs`): uploaders/hotkey JSON already loads on a worker; application config must precede language, paths and UI. Hotkeys, tray and notification setup have ordering dependencies. Update checks use an immediate initial callback then a one-hour timer when enabled. Completion-triggered saves are asynchronous, gated by the existing setting and the task queue becoming idle. Deferral or coalescing can change readiness/durability; unchanged without a safe independent boundary and display-free end-to-end startup measurement.
6. **Other large-file consumers** (`ShareX.Tools/Tools/Metadata/TiffMetadataReader.cs`, `ShareX.Tools/Tools/AnalyzeImage/AnalyzeImageViewModel.cs`, `ShareX.HelpersLib/Helpers/SkiaImageHelpers.Files.cs`, legacy JSON history): TIFF metadata and analysis use byte-oriented/random-access parsers; file Base64 must return the full string. Streaming these requires parser/API changes. Existing `GetImageFileDimensions` already uses a file stream and codec header probe. Native animation data still retains an independent encoded snapshot so frame decoding survives source replacement.
7. **Image/effect hot paths and complexity** (`ShareX.Avalonia/Imaging/BitmapConversionHelpers.cs`, `ShareX.ImageEffectsLib/Presentation/ImageEffectsViewModel.cs`, `SkiaImageHelpers.Operations.cs`): bitmap transfer already copies raw pixels rather than PNG round-trips; previews resize first and effects debounce at 100 ms. Pixel scans and effect work remain proportional to image size. No demonstrated quadratic loop justified a behavior-preserving rewrite in this audit. Full-screen color-picker PNG conversion remains a potential startup cost; replacing its image ownership path needs separate rendering parity checks.

Timing for each change above compares the baseline with the first measurement after that isolated change. A final combined run also measured image conversion at 27.147 ms / 12,694,540 bytes, animation open at 0.224 ms / 701 bytes, history append at 23.679 ms / 13,414,912 bytes, and unchanged history load at 22.674 ms / 15,965,472 bytes. Runtime variation across runs is expected; allocation reductions are consistent.

## Measurement method and reproduction

`tests/ShareX.Performance.Tests` is a standalone console harness; it initializes no UI and uses only files under its own temporary directory, which it removes. It checks Base64 byte parity for all five image formats; unique SQLite-generated IDs; nullable columns, UTC timestamps and JSON tags; animation metadata and decoding after the source file is overwritten. It measures current-thread managed allocations (not native memory or peak RSS), warms each operation, runs five samples and reports the median per operation. Image/animation cases use 20 iterations per sample; history append uses one 5,000-row batch and history load uses five loads per sample. Append includes creating/opening/schema initialization/disposal and ID validation, equally before/after. Filesystem cache is warm; results are local microbenchmarks, not desktop or startup timings.

```bash
source scripts/linux/env.sh
unset DISPLAY WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE SWAYSOCK NIRI_SOCKET DBUS_SESSION_BUS_ADDRESS
dotnet run --project tests/ShareX.Performance.Tests -c Release -p:NuGetAudit=false
```

## Timer / delay audit

All production C# `Timer`, `DispatcherTimer` construction sites (including target-typed construction) and `Task.Delay` sites were searched; test programs and excluded `LegacyWindows` code are not shipped idle loops. `DebugTimer`, `Stopwatch`, commented code and `TimerWidth` are not scheduling timers. Rows group related sites with the same lifecycle; all are unchanged for the stated reason.

| File / feature | Interval / delay | Lifetime, idle assessment and reason retained |
| --- | --- | --- |
| `ShareX/TaskManager.cs`, TestTrayIcon | 50 ms × 101 | Explicit test animation only; no production idle loop. |
| `ShareX/Linux/WatchFolder.cs` | 250 ms, max 120 polls; four stable checks | Only after file events; waits for writes to finish, cancellation supported. |
| `ShareX/Presentation/AutoCapture/AutoCaptureWindow.axaml.cs` | Capture 1 s initially/while uploads busy, then configured delay; status 250 ms | Both start on capture start and stop on stop/close. |
| `ShareX/Presentation/Notification/NotificationWindow.cs` | Configured duration; fade 16 ms; hover 40 ms | Duration/fade/hover only for displayed notification; all stopped on close; hover polling preserves pointer/popup semantics. |
| `ShareX/Presentation/AfterUpload/AfterUploadWindow.axaml.cs` | 60 s | Optional auto-close while the result window is open; stopped on close. |
| `ShareX/Presentation/MainWindow/TrayIconService.cs` | Platform double-tap time (500 ms fallback) | Started only for pending single click; stops after tick, double click or disposal. |
| `ShareX/EasterEggs/ShareXClicker/ShareXClickerControl.cs` | 33 ms | Starts on game interaction; stops on disposal. Supports continuing passive game progress and animations. |
| `ShareX/CaptureHelpers/CaptureBase.cs` | Configured screenshot delay; 250 ms | One-shot capture delay / wait for hidden UI before capture; not background polling. |
| `ShareX.HistoryLib/Views/HistoryWindow.axaml.cs`, `ImageHistoryWindow.axaml.cs` | 220 ms each | One-shot filter debounce; stops before work and on close. |
| `ShareX.ImageEffectsLib/Presentation/ImageEffectsViewModel.cs` | 100 ms | One-shot preview debounce; stops before processing and disposal. |
| `ShareX.UploadersLib/FileUploaders/Streamable.cs` | 1 s | Processing-status polling for current upload only, cancellable. |
| `ShareX.UploadersLib/FileUploaders/MediaFire.cs` | 1 s | Current upload completion only, cancellable. |
| `ShareX.UploadersLib/FileUploaders/SendSpace.cs` | Initial 1 s, then remainder of 1 s interval | Current transfer progress only; cancellation on dispose. |
| `ShareX.UploadersLib/FileUploaders/BackblazeB2.cs` | Exponential seconds per retry | Failed transfer backoff only, cancellable. |
| `ShareX.HelpersLib/UpdateChecker/GitHubUpdateManager.cs` | Immediate then 1 h | Exists only when auto-update is allowed; disabled/disposed via configuration. Hourly callback still occurs if AutoUpdateEnabled becomes false; negligible cadence, preserved. |
| `ShareX.HelpersLib/Linux/SingleInstanceManager.cs`, `LinuxInstanceConnection.cs` | 50 ms retry | Startup argument forwarding until deadline, no idle polling. |
| `ShareX.HelpersLib/Linux/LinuxHotkeyHost.cs` | 250 ms | Cancellable one-shot portal registration debounce. |
| `ShareX.HelpersLib/Linux/LinuxGlobalShortcutsPortal.cs` | 800 ms | One-shot disposal timeout race. |
| `ShareX.HelpersLib/Presentation/ColorPicker/ColorPickerWindow.axaml.cs` | Two 250 ms delays | One-shot hide-before-capture actions. |
| `ShareX.ImageEditor/Presentation/Controls/EffectBrowserPanel.axaml.cs` | 50 ms | One-shot focus after layout. |
| `ShareX.ImageEditor/Presentation/ViewModels/MainViewModel.Notifications.cs` | Configured duration, hide 220 ms, hover 50 ms | Only while a notification is active/hovered; generation checks prevent old notifications continuing. |
| `ShareX.Tools/Tools/BackgroundRemover/BackgroundRemoverViewModel.Notifications.cs` | Configured duration, hover 50 ms, hide 220 ms | Same notification lifecycle; no app-wide idle loop. |
| `ShareX.Tools/Controls/RulerOverlayControl.cs` | 1,400 ms | One-shot status-message expiry. |
| `ShareX.Tools/Controls/MonitorTestPatternControl.cs` | 16 ms | Enabled only for animated test patterns, stopped otherwise. |
| `ShareX.Tools/Tools/AnimatedGifMaker/AnimatedGifMakerViewModel.cs` | Configured frame delay, clamped 10–655,350 ms; 100 ms preview debounce | Playback only when enabled; debounce cancelled on new changes; stopped on disposal. |
| `ShareX.Tools/Tools/AnimatedGifTrimmer/AnimatedGifTrimmerViewModel.cs` | Time to next frame end | One-shot playback rescheduled after each decoded frame; stopped on pause/disposal. |
| `ShareX.Tools/Linux/MediaPlayer.cs` | 60 Hz; loading 25 ms, max 600 attempts | Render/event poll starts on file-loaded; stops on unload/error/dispose. Remains active when paused for seek/step/resize; event-driven replacement deferred. |
| `ShareX.Tools/Tools/VideoPlayer/VideoPlayerWindow.axaml.cs` | 200 ms | Controls visibility updates while open; stops on close. Could become one-shot, but focus/hover behavior needs UI tests. |
| `ShareX.Tools/Tools/MouseHighlighter/MouseHighlighterService.cs` | 60 Hz | Only while the user enables the tool; updates global cursor/animation, stops on dispose. |
| `ShareX.Tools/Tools/NetworkMonitor/NetworkMonitorViewModel.cs` | Selected 1/2/5/10/30 s; disconnect confirmation 1 s; 600 ms notification | Monitoring only while started, cancellation on stop; confirmation and notifications are one-shots. No network probes run in this audit. |
| `ShareX.Tools/Tools/ImageWatermark/ImageWatermarkViewModel.cs`, `ImageResizer/ImageResizerViewModel.cs`, `ImageConverter/ImageConverterViewModel.cs` | 100 ms each | Cancellable one-shot preview debounces. |
| `ShareX.Tools/Tools/AnalyzeImage/AnalyzeImageWindow.axaml.cs`, `QRCode/QRCodeWindow.axaml.cs`, `OCR/OCRWindow.axaml.cs` | 250 ms each | One-shot hide-before-capture actions. |
| `ShareX.Tools/Tools/PinToScreen/PinToScreenStartupViewModel.cs` | 200 ms | One-shot initial capture delay. |
| `ShareX.Avalonia/Linux/LinuxCaptureOverlay.cs` | 40/120/100 ms niri; 40/150 ms sway; 40 ms KDE; 40/180/30 ms Hyprland | Bounded placement/verification retries only while showing a capture overlay; not application idle timers. |
| `ShareX.Avalonia/Linux/KdeCapture.cs` | Two 150 ms waits per placement attempt | Let KWin process configure before verifying geometry/stacking; required correctness boundary. |
| `ShareX.ScreenCaptureLib/Linux/RecordingWindowPlatform.cs` | First 80 ms, subsequent 100 ms | Bounded visible-window placement retries. |
| `ShareX.ScreenCaptureLib/ScrollingCaptureManager.cs` | User start/scroll delays and computed remaining capture delay | Only during scrolling capture; timings are user-visible capture behavior. |
| `ShareX.ScreenCaptureLib/Presentation/ScrollingCapture/ScrollingCaptureWindow.axaml.cs` | 250 ms | One-shot hide-before-region-selection delay. |
| `ShareX.ScreenCaptureLib/Presentation/RegionCapture/RegionCaptureWindow.axaml.cs` | InputDelay setting | One-shot input delay, only for active capture. |
| `ShareX.ScreenCaptureLib/Presentation/ScreenRecording/ScreenRecordWindow.axaml.cs` | 50 ms | Countdown/recording elapsed UI; starts with timer, stops on pause/stop/close. |
| `ShareX.ScreenRecordingLib/Linux/ScreenRecorder.cs` | Paused 20 ms; frame-rate budget; encoder start 350 ms; external encoder end 20 ms; portal recording 50 ms | Only within an active recording. Pause/stop could use async signals, but requires recording-process lifecycle tests; unchanged. |
| `ShareX.OverlayHost/OverlayHostLoop.cs` | 10 ms watchdog; 1 ms synchronous pump sleep | Only during overlay session; emergency cancellation latency intentionally bounded. |
| `ShareX/NativeOverlay/OverlayHelperServer.cs` | 10 ms emergency timer; 2 ms readiness sleep; 8 ms worker pump sleep | Helper/session lifetime only, disposed on session end; cancellation must work even if worker stalls. Injection-only infinite/100 ms sleeps are test hooks. |

Additional synchronous sleeps found: `CaptureWindow.cs` (100/250/100 ms focus/hide sequencing), `WorkerTask.cs` (1 s configured retry), `PingHelper.cs` (configured inter-ping wait), `FPSManager.cs` (remaining frame budget), `Helpers.cs` (explicit wait helpers and bounded process wait), `DownloaderWindow.axaml.cs` (configured progress delay), and `HistoryManager.Test` (two 1 s benchmark separators). They run within explicit operations, not recurring idle timers. The Steam launcher is outside Linux app runtime. No timing changes made because doing so could alter capture/retry behavior.

## Validation and commits

All checks passed after the final production change:

- Release solution build: **0 errors**, 55 existing warnings. The initial default parallel build hit MSBuild's internal `MSB0001: Invalid node id specified: 4`; retrying with one worker and build servers disabled succeeded. Each production change was followed by a successful full solution build (first: 60 warnings / 0 errors; second and third: 55 warnings / 0 errors).
- `tests/ShareX.OverlayHost.Tests/run.sh`: exit 0, **121 checks passed**, no display/window APIs invoked.
- `tests/ShareX.OverlayRender.Tests/run.sh`: exit 0, retained/full-redraw equivalence, damage containment, mixed scales and rendering parity passed.
- `tests/ShareX.OverlayIntegration.Tests/run.sh --headless`: exit 0, helper startup, frozen-pixel fallback, crash/stop/heartbeat timeout handling and diagnostics passed.
- `dotnet run --project tests/ShareX.Compositor.Tests -c Release`: exit 0, **0 failures**, including private KWin D-Bus fakes.
- Performance harness: exit 0; encoding, history and retained animation-snapshot checks passed.
- `git diff --check`: passed.

Successful build command (after `source scripts/linux/env.sh`):

```bash
dotnet build ShareX.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
```

The first successful build also restored packages; subsequent builds used `--no-restore`. Required test scripts were invoked directly, sequentially, with display/compositor/session-bus variables unset, `XDG_SESSION_TYPE=headless`, `NuGetAudit=false` and `MSBUILDDISABLENODEREUSE=1`. Compositor tests create their own private fake bus. No real desktop validation was attempted.

Staging/commit command failed: `.git/index.lock: Read-only file system`. The requested `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` trailer was supplied; no commits were created and no push was attempted. Changes remain uncommitted.
