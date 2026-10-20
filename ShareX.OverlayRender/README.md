# ShareX.OverlayRender

Display-independent .NET 10 CPU scene renderer. Its only package reference is
SkiaSharp. The executable tests supply Skia's Linux native assets. It does not
reference Avalonia, platform interop, input APIs, or a compositor.

Create one `MonitorRenderer` per frozen monitor screenshot. Supply its global
logical rectangle, scale, and a BGRA8888 premultiplied/opaque screenshot whose
physical dimensions are `ceil(logical dimension * scale)`. Negative desktop
origins and mixed scales are supported. All scene coordinates are global logical
coordinates; conversion to physical pixels happens inside the monitor renderer.
Border, handle and centre-crosshair sizes follow the existing image-pixel chrome;
HUD sizes and annotation stroke widths scale with the monitor.

```csharp
using var monitor = new MonitorRenderer("DP-1", bounds, scale, screenshot,
    typeface: captureFont, takeScreenshotOwnership: true);
MonitorDamage damage = monitor.Update(scene);
foreach (SKRectI region in damage.Regions)
    MonitorRenderer.Copy(monitor.Framebuffer, presentationBitmap, region);
```

With ownership transfer, do not mutate/dispose the screenshot: the renderer owns
it until disposal. By default the screenshot is cloned, so the caller can dispose
its input immediately. The optional typeface is borrowed and must outlive the
renderer. Supply Segoe UI or the application's Inter fallback for font parity;
the default uses installed Segoe UI (Skia's system fallback if absent). No fonts
are downloaded or bundled. Theme colors default to ShareX dark; the host can copy
its current palette into `OverlayOptions`.

`Framebuffer` is a borrowed, read-only premultiplied BGRA bitmap. Update synchronously
finishes CPU writes. Do not read concurrently with Update. Monitor instances can
be used independently on separate threads, but the benchmark uses one thread.
The framebuffer is never replaced or cleared during steady-state updates.
`Regions` are nonoverlapping, clipped physical damage rectangles; `Bounds` is their
smallest enclosing rectangle. Prefer Regions for copying/presenting sparse chrome.
Geometry includes conservative padding for strokes, antialiasing and shadows; it
is not a byte-exact minimal pixel diff. Identical snapshots return empty damage.

The frozen screenshot is marked immutable and exposed to Skia through one retained
SKImage sharing its pixels, avoiding per-update bitmap snapshots.
The renderer caches the dimmed background once, rebuilding only when dim strength
changes. Selection/hover interiors use the original screenshot; changed interior
areas use old/new rectangle subtraction. Borders, handles, screen crosshair,
centre crosshair, HUD, current annotation and external bitmap each contribute
their own damage. The renderer restores cache pixels only inside that union,
then replays the layers once through a Skia region clip. Full-monitor hover
necessarily touches the monitor interior. No pixel scan/diff, full-frame clear,
full-sized temporary surface or Avalonia render occurs in an update.

The current annotation slot supports rectangle/ellipse outline and fill and line
strokes (including horizontal/vertical lines). It is one changing shape, not an
annotation document or an input controller. Clearing/replacing it removes the old
preview. The next integration step must retain committed annotations and connect
the remaining editor tools; they are not implicitly accumulated here.

The external bitmap is caller-owned and composited last. Keep it alive through
Update, increment its Revision whenever its pixels change, and call Skia's
`NotifyPixelsChanged` after raw pixel writes. Its logical bounds determine size
and placement. A small toolbar surface can span monitor boundaries. There is no
input hit testing in this library.

The Avalonia/application side still needs to provide:

- Toolbar bitmap, revision and global bounds, plus toolbar hit testing/actions.
- Text tool editing, shaping/committing text annotations, dialogs, menus and pickers.
- Capture options/theme/font snapshots and input/selection/resize/snapping logic.
- Annotation document, undo/delete, committed shapes and remaining annotation tools.
- Screenshot acquisition, native surface presentation, coalesced input, Esc,
  watchdog/timeouts, fallback and result cropping (outside this rendering step).

Run `tests/ShareX.OverlayRender.Tests/run.sh` for synthetic correctness checks,
add `--bench output.json` for timings, or `--memory Dual` / `--memory Triple` for
an isolated retained-scene memory reading. Run
`tests/ShareX.OverlayBench/run.sh --native-parity` for direct bitmap comparisons
with the existing Avalonia region control, without constructing a window.

A retained desktop-sized annotation layer can supply `OverlayBitmap.DirtyRegions`
(global logical coordinates). When bitmap identity and bounds are unchanged, a new
revision damages only those regions. Clearing pixels must include the cleared area.
Replacing/moving the bitmap still damages both complete bounds. Omitting the list
retains the original conservative behavior.
