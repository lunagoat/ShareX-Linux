# ShareX.OverlayHost

.NET 10 interaction/hosting library referencing only OverlayRender and NativeOverlay
(no resolved Avalonia dependencies). No display is opened by constructing a controller.

`OverlayController.Handle` consumes native events on one owner thread. `Scene`,
`Cursor`, `Result` and `CancellationReason` expose its state; `Publish` calls back
once per monitor with global logical scene snapshots. Supply frozen monitor layout,
a monotonic `IOverlayClock`, capture/visual option snapshots, initial pointer position,
an optional front-to-back `IWindowRectProvider`, and `IOverlayExtension`.
The caller supplies window discovery; this library never enumerates platform windows.

`OverlayHostLoop` owns its session and borrows matching MonitorRenderer instances.
`RunAsync` ingests input separately from the single controller/render worker, paces
frames to at most one per 8 ms, and presents only damage regions. The first frame
and unchanged-layout notifications force full presents from retained screenshots.
Changed output identity, order, origin, size, scale or pixel size cancels with a
reason instead of applying frozen pixels to a different layout. `Pump` is a manual
single-thread step for fake-clock tests, and must not run concurrently with RunAsync.
`Post` marshals inputs and `ExtensionChanged` notifications through the same queue.

Continuous motion occupies one queue slot. Discrete key/button/wheel events retain
order and are never dropped; a motion immediately before a discrete event is an
ordering barrier, preserving where a key or click acted. A frame processes a bounded
batch of 4096 queue items so a stream of discrete events cannot starve presentation.
No renderer or controller lock is held by ingestion or the independent watchdog.

The native Wayland dispatch path sets the lock-free `IOverlaySession.CancelRequested`
flag and tears surfaces down on Esc before going back to rendering work. The host
also checks that flag before rendering and consumes Esc on its ingestion path.
Watchdog timeouts are configurable frame completion (default 5 s after an outstanding
event), no first frame after 30 s, and no input for 45 s. Input arriving during a
frame stays outstanding for the next frame. Independent cancellation completes the
host result and disposes the session even if its worker is blocked. `Cancelled`
is raised with the reason. The native backend requests keyboard focus only after
all outputs have received their first compositor frame callback.

On completion, `OverlayHostResult` supplies logical crop/metadata/committed annotation
snapshots and caller-owned per-output BGRA screenshot tiles. Each tile has its global
logical intersection, scale and exact floor/ceil output-local physical crop. Tiles
come from original screenshots, never the dimmed/chrome framebuffer. Dispose tile
bitmaps after use. Native-resolution tiles preserve mixed-DPI source pixels without
choosing an implicit resampling/composition policy. Final image stitching, annotation
flattening, and application after-capture actions belong to integration.

The extension owns toolbar hit testing, tool editing, bitmap composition and revisions,
and committed annotation snapshots. Pointer capture is retained after a toolbar press
until release, including release outside toolbar bounds. Keys and annotation mode are
forwarded; Tab restores region mode. A null extension provides region-only capture.
Extensions execute on the controller thread; publish immutable bitmap snapshots and
post ExtensionChanged when background work completes. Do not mutate controller state
or a bitmap being rendered from a different thread.

`RunAsync` can return while a renderer/extension is stuck. Keep borrowed renderers and
extension bitmaps alive until `WorkerCompletion`; never dispose them concurrently.
The application must still run the overlay in a killable process and enforce its
hard timeout/fallback. This step does not install application capture hotkeys or
open production overlays.

Windows source parity implemented: drag/pending-hover threshold, move, eight handles,
5-unit minimum selection (1-unit interactive resize minimum, then release validation),
Shift-square creation, Ctrl moving during creation, arrows moving the logical pointer
by 1 / Shift 10, Enter/double-click confirmation, Space fullscreen, tilde active monitor,
Delete, Esc, right-click creation cancellation/release action suppression, every
RegionCaptureAction on right/middle/X1/X2, last-region clipping, injected window/control
hover and click selection, QuickCapture, active-monitor capture bounds, 500 ms click/key
delay (Esc always works), pointer HUD/magnifier/custom text via OverlayRender, wheel
magnifier density, cursors, global mixed-scale/multi-monitor and off-output drag input.
Double-click recognition in the Wayland backend uses a 500 ms / 4 logical unit default;
platform-configured Avalonia double-click preferences are not queried.

Remaining differences/integration: physical cursor warping and confinement (logical
nudging and active-monitor bounds are implemented), platform window/control discovery,
Windows native metadata lookup, toolbar visibility/tool state/editing/undo/dialogs and
annotation flattening (extension worker), final single-image composition/after-capture
flow, application fallback/process hard timeout, and real/nested compositor validation.
This is not a claim of full RegionCaptureWindow UI parity or compositor safety validation.

Headless checks: `tests/ShareX.OverlayHost.Tests/run.sh` (fake sessions/clock, no windows).
The separate `tests/ShareX.OverlayHost.Smoke` executable uses synthetic screenshots and
refuses to open a session unless WAYLAND_DISPLAY starts with sx-. Do not run it against
a real display; no compositor smoke execution is included in the headless suite.
