# Native scene correctness and CPU benchmark

Run from the repository root:

```bash
tests/ShareX.OverlayRender.Tests/run.sh
tests/ShareX.OverlayRender.Tests/run.sh --bench /tmp/native-scene.json
tests/ShareX.OverlayRender.Tests/run.sh --memory Dual
tests/ShareX.OverlayRender.Tests/run.sh --memory Triple
tests/ShareX.OverlayBench/run.sh --native-parity
```

These are fail-fast executable tests, matching the repository's Linux test style.
No test connects to a display or creates a window. The native suite depends only
on the renderer and Skia native assets. The separate parity mode uses existing
Avalonia Headless initialization, then renders the production region control
directly to a bitmap without creating a Window.

Correctness checks compare presentation bytes with retained bytes exactly after
every update. Randomized scenes at four scales are also compared with a fresh
full redraw (up to 3 channel values allowed for Skia's antialiased clipping/blend
rounding). Missing layers or stale regions still fail. Scenarios include negative
origins, mixed-DPI seams, ceil raster dimensions, handles, dim-strength changes,
HUDs and custom info, screen/centre crosshairs, rectangle/ellipse/line outline and
fill, zero-height/width lines, external bitmap movement/removal, nonoverlapping
damage and identical snapshots. The production-control comparison checks dim and
interior pixels to one value and loosely checks accent perimeter coverage.

Benchmark layouts, endpoint sweep, scenarios and warmup/sample counts match
OverlayBench B. Selection and annotation use the first largest monitor. Hover
switches full-monitor rectangles and moves the pointer/HUD to that monitor;
single-monitor hover alternates on/off. Default HUD/options are enabled. Render
includes Update on every monitor; Copy includes only reported regions into
presentation buffers. Total includes both, without input dispatch or native
presentation. Untouched monitors must return no damage. All presentation buffers
are checked after every event, outside timing; fresh redraw checks also run at
the end of warmup and measurement. Cache-build samples exclude screenshot
creation, original cloning and initial framebuffer population. Percentiles use
nearest rank; maxima include scheduler and GC stalls. No pacing, dropped events,
parallel monitors, GPU, or full-frame diff is used.

Memory mode transfers screenshot ownership, runs a selection, rectangle preview and default HUD on each
monitor, disposes no live source bitmap, and reports retained bytes and process
RSS. It has no presentation copies, reference renderer or Avalonia. The full
benchmark keeps extra screenshots and presentation/reference frames for correctness
and is not an overlay-process memory measurement.
