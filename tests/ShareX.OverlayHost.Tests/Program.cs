// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Threading.Channels;
using ShareX.NativeOverlay;
using ShareX.OverlayHost;
using ShareX.OverlayRender;
using SkiaSharp;

int checks = 0;
void Check(bool value, string message)
{ if (!value) throw new Exception(message); checks++; }
var layout = new[] { new OverlayOutput("left", new(-200, 0, 200, 200), 1), new OverlayOutput("right", new(0, 0, 301, 200), 1.25) };
CaptureOptions normal = new() { QuickCapture = false };
OverlayController Logic(CaptureOptions? options = null, IWindowRectProvider? windows = null, IOverlayExtension? extension = null) =>
    new(layout, new FakeClock(), (options ?? normal) with { InputDelay = TimeSpan.Zero }, windows, extension);
void Down(OverlayController c, double x, double y, OverlayModifiers m = 0) => c.Handle(new PointerButton(1, true, x, y, m));
void Move(OverlayController c, double x, double y, OverlayModifiers m = 0) => c.Handle(new PointerMoved(x, y, m));
void Up(OverlayController c, double x, double y, OverlayModifiers m = 0) => c.Handle(new PointerButton(1, false, x, y, m));
void Drag(OverlayController c, double x, double y, double x2, double y2, OverlayModifiers m = 0)
{ Down(c, x, y, m); Move(c, x2, y2, m); Up(c, x2, y2, m); }
KeyEvent Key(uint code, OverlayModifiers modifiers = 0, bool pressed = true) => new(code, 0, pressed, false, null, modifiers);

var c = Logic();
Drag(c, -100, 20, 100, 100);
Check(c.Selection == new LogicalRect(-100, 20, 200, 80), "cross-monitor create");
Check(c.Scene.ShowHandles, "handles visible");
Drag(c, -50, 60, -40, 70);
Check(c.Selection == new LogicalRect(-90, 30, 200, 80), "move");
Move(c, 110, 110);
Check(c.Cursor == OverlayCursor.ResizeNWSE, "resize cursor");
Drag(c, 110, 110, 130, 130);
Check(c.Selection == new LogicalRect(-90, 30, 220, 100), "resize");
c.Handle(Key(111)); Check(c.Selection.IsEmpty, "delete");
Drag(c, 0, 0, 2, 2); Check(c.Selection.IsEmpty, "minimum size");
Down(c, -100, 30); Move(c, -50, 50, OverlayModifiers.Shift);
Check(c.Selection == new LogicalRect(-100, 30, 50, 50), "square");
Move(c, -40, 60, OverlayModifiers.Control);
Check(c.Selection == new LogicalRect(-90, 40, 50, 50), "ctrl moves creation");
Move(c, -20, 80);
Check(c.Selection == new LogicalRect(-90, 40, 70, 40), "ctrl release resumes creation");
c.Handle(Key(106, OverlayModifiers.Shift)); Check(c.Selection.Width == 80, "shift arrow pointer nudge during creation");
c.Handle(Key(108, OverlayModifiers.Control)); Check(c.Selection.Y == 41, "ctrl arrow moves creation");
Up(c, -10, 81); c.Handle(Key(28)); Check(c.Result != null, "enter confirms");

foreach (var (hx, hy) in new[] { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) })
{
    c = Logic(); Drag(c, 20, 40, 120, 140);
    double x = hx < 0 ? 20 : hx > 0 ? 120 : 70, y = hy < 0 ? 40 : hy > 0 ? 140 : 90;
    Drag(c, x, y, x + 10, y + 10);
    Check(c.Selection == new LogicalRect(hx < 0 ? 30 : 20, hy < 0 ? 50 : 40, hx < 0 ? 90 : hx > 0 ? 110 : 100, hy < 0 ? 90 : hy > 0 ? 110 : 100), $"handle {hx},{hy}");
}
c = Logic(); Down(c, -100, 30); Move(c, 900, 900); Up(c, 900, 900);
Check(c.Selection.Right == 301 && c.Selection.Bottom == 200 && c.Pointer.X == 900, "outside all outputs pointer retained and selection clipped");
c = Logic(); Move(c, 20, 30); c.Handle(Key(106)); c.Handle(Key(108, OverlayModifiers.Shift | OverlayModifiers.Control));
Check(c.Pointer == new LogicalPoint(21, 40) && c.Selection.IsEmpty, "arrows move pointer, not completed selection");

var provider = new Windows();
c = Logic(windows: provider); Move(c, 30, 30);
Check(c.Hover == new LogicalRect(20, 20, 40, 40), "control snap");
Down(c, 30, 30); Up(c, 30, 30); c.Handle(Key(28));
Check(c.Result?.Window?.IsControl == true, "click captures candidate and metadata");
c = Logic(normal with { DetectControls = false }, provider); Move(c, 30, 30);
Check(c.Hover == new LogicalRect(0, 0, 150, 150), "disable controls");
c = Logic(normal with { DetectWindows = false }, provider); Move(c, 30, 30); Check(c.Hover.IsEmpty, "disable windows");
c = Logic(windows: provider); Move(c, 30, 30); Down(c, 30, 30); Move(c, 90, 80); Up(c, 90, 80);
Check(c.Selection == new LogicalRect(30, 30, 60, 50), "snap drag switches to create at four pixels");
c = Logic(normal with { QuickCapture = true }, provider); Move(c, 30, 30); Down(c, 30, 30); Up(c, 30, 30); Check(c.Result != null, "quick snap");
c = Logic(normal with { QuickCapture = true }); Drag(c, 0, 0, 50, 50); Check(c.Result != null, "quick capture");

foreach (int button in new[] { 2, 3, 4, 5 })
foreach (RegionCaptureAction action in Enum.GetValues<RegionCaptureAction>())
{
    var extension = new Extension();
    c = Logic(normal with { RightClick = action, MiddleClick = action, X1Click = action, X2Click = action, LastRegion = new(-20, 10, 40, 40) }, extension: extension);
    Drag(c, 20, 20, 120, 120); Move(c, 70, 70);
    c.Handle(new PointerButton(button, true, 70, 70, 0));
    if (button == 3) { Check(!c.Finished, "right waits for release"); c.Handle(new PointerButton(button, false, 70, 70, 0)); }
    Check(action switch
    {
        RegionCaptureAction.None => !c.Finished && !c.Selection.IsEmpty,
        RegionCaptureAction.CancelCapture => c.CancellationReason != null,
        RegionCaptureAction.RemoveShapeCancelCapture or RegionCaptureAction.RemoveShape => c.Selection.IsEmpty && !c.Finished,
        RegionCaptureAction.SwapToolType => extension.AnnotationMode,
        RegionCaptureAction.CaptureFullscreen => c.Result?.Bounds == new LogicalRect(-200, 0, 501, 200),
        RegionCaptureAction.CaptureActiveMonitor => c.Result?.Bounds == new LogicalRect(0, 0, 301, 200),
        RegionCaptureAction.CaptureLastRegion => c.Result?.Bounds == new LogicalRect(-20, 10, 40, 40),
        _ => false
    }, $"button {button} action {action}");
}
c = Logic(); Down(c, 0, 0); Move(c, 50, 50); c.Handle(new PointerButton(3, true, 50, 50, 0)); c.Handle(new PointerButton(3, false, 50, 50, 0));
Check(c.Selection.IsEmpty && !c.Finished, "right cancels creation without cancelling session");
c.Handle(new PointerButton(3, false, 50, 50, 0)); Check(c.CancellationReason != null, "right outside cancels");
foreach (uint code in new uint[] { 1, 57, 41 })
{
    c = Logic(); Move(c, -100, 50); c.Handle(Key(code));
    Check(code == 1 ? c.CancellationReason == "Escape" : c.Result?.Bounds == (code == 57 ? new LogicalRect(-200, 0, 501, 200) : new LogicalRect(-200, 0, 200, 200)), $"key {code}");
}
c = new(layout, new FakeClock(), normal with { ActiveMonitorMode = true, InputDelay = TimeSpan.Zero }, initialPointer: new(-100, 10));
Drag(c, -100, 20, 200, 100); Check(c.Selection.Right == 0, "active monitor mode bounds");
var clock = new FakeClock(); c = new(layout, clock, normal);
Down(c, 0, 0); Up(c, 50, 50); Check(c.Selection.IsEmpty, "input delay");
clock.Advance(500); Drag(c, 0, 0, 50, 50); Check(!c.Selection.IsEmpty, "input enabled at 500 ms");
c.Handle(new PointerWheel(0, 1, 40, 40, 0)); Check(c.Scene.Options.MagnifierPixelCount == 13 && c.Scene.Pointer == new LogicalPoint(40, 40), "magnifier wheel and pointer HUD");

clock = new(); var watchdog = new OverlayWatchdog(clock, TimeSpan.FromSeconds(2));
clock.Advance(29999); Check(watchdog.Check() == null, "before first-frame deadline"); clock.Advance(1); Check(watchdog.Check()!.Contains("first frame"), "first frame timeout");
clock = new(); watchdog = new(clock, TimeSpan.FromSeconds(2)); watchdog.FrameCompleted(); watchdog.Input();
clock.Advance(1999); watchdog.Input(); Check(watchdog.Check() == null, "frame watchdog pending"); clock.Advance(1); Check(watchdog.Check()!.Contains("Frame completion"), "continuous input cannot postpone watchdog");
clock = new(); watchdog = new(clock); watchdog.FrameCompleted(); clock.Advance(45000); Check(watchdog.Check()!.Contains("No input"), "idle timeout");
clock = new(); var pacer = new FramePacer(clock); Check(pacer.TryBegin(true) && !pacer.TryBegin(true), "pacer first"); clock.Advance(7); Check(!pacer.TryBegin(true), "pacer early"); clock.Advance(1); Check(pacer.TryBegin(true) && !pacer.TryBegin(false), "pacer deadline and clean");

using (var fixture = new Fixture(layout))
{
    var host = fixture.Host;
    host.Pump(); Check(fixture.Session.Presents.Count == 2, "initial present all outputs");
    Check(fixture.Session.Presents.All(p => p.Dirty == null), "initial full frames");
    int count = fixture.Session.Presents.Count;
    for (int i = 0; i < 1000; i++) fixture.Session.Send(new PointerMoved(10 + i % 100, 60, 0));
    host.Pump(); Check(fixture.Session.Presents.Count == count, "pacer suppresses early render");
    fixture.Clock.Advance(8); host.Pump();
    Check(host.Controller.Pointer == new LogicalPoint(109, 60), "latest move");
    Check(fixture.Session.Presents.Count - count < 20, "1000 motions few damage presents");
    Check(fixture.Session.Presents.Skip(count).All(p => p.Output == 1 && p.Dirty != null), "untouched monitor remains idle");
    fixture.Session.Send(new PointerWheel(0, 1, 40, 60, 0)); fixture.Session.Send(new PointerWheel(0, 1, 40, 60, 0));
    fixture.Session.Send(Key(106)); fixture.Session.Send(Key(106)); fixture.Clock.Advance(8); host.Pump();
    Check(host.Controller.Scene.Options.MagnifierPixelCount == 11 && host.Controller.Pointer.X == 42, "wheel and keys never dropped");
    fixture.Session.Send(new LayoutChanged()); fixture.Clock.Advance(8); host.Pump();
    Check(fixture.Session.Presents.TakeLast(2).All(p => p.Dirty == null), "same layout full repaint");
    fixture.Session.Outputs = new[] { layout[0], layout[1] with { Scale = 2 } };
    fixture.Session.Send(new LayoutChanged()); fixture.Clock.Advance(8); host.Pump();
    Check(host.Controller.CancellationReason!.Contains("layout or scale"), "scale change clear cancellation");
}
using (var fixture = new Fixture(layout))
{
    fixture.Session.Send(new PointerButton(1, true, -20, 20, 0)); fixture.Session.Send(new PointerMoved(40, 60, 0));
    fixture.Session.Send(new PointerButton(1, false, 40, 60, 0)); fixture.Session.Send(Key(28)); fixture.Host.Pump();
    var result = await fixture.Host.Completion;
    Check(result.Outputs?.Count == 2, "capture tiles on both outputs");
    Check(result.Outputs![0].Pixels.GetPixel(0, 0) == SKColors.CornflowerBlue && result.Outputs[1].Pixels.GetPixel(0, 0) == SKColors.CornflowerBlue, "capture uses frozen pixels without chrome");
    Check(result.Outputs[1].Pixels.Width == 50, "fractional scale crop");
    foreach (var tile in result.Outputs) tile.Pixels.Dispose();
}
foreach (bool changed in new[] { false, true })
using (var fixture = new Fixture(layout))
{
    fixture.Host.Pump();
    fixture.Session.Send(changed ? new OverlayClosed() : new KeyEvent(1, 0, true, false, null, 0));
    fixture.Clock.Advance(8); fixture.Host.Pump(); Check(fixture.Host.Controller.CancellationReason != null, "close and escape paths");
}
using (var fixture = new Fixture(layout))
{
    fixture.Host.Pump(); fixture.Clock.Advance(45000); fixture.Host.Pump();
    Check(fixture.Host.Controller.CancellationReason!.Contains("No input"), "host watchdog");
}
// Block the controller thread in extension code; input ingestion/Escape must still terminate the session.
var blocked = new Extension { Block = true };
using (var fixture = new Fixture(layout, blocked))
{
    fixture.Session.Send(new PointerButton(1, true, 250, 50, 0));
    Task<OverlayHostResult> run = fixture.Host.RunAsync();
    Check(blocked.Entered.Wait(2000), "worker deliberately blocked");
    fixture.Session.Send(Key(1));
    var result = await run.WaitAsync(TimeSpan.FromSeconds(2));
    Check(result.CancellationReason == "Escape", "escape independent of stuck controller/render worker");
    blocked.Release.Set(); await fixture.Host.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(2));
}
var frozen = new Extension { Block = true };
using (var fixture = new Fixture(layout, frozen))
{
    fixture.Session.Send(new PointerButton(1, true, 250, 50, 0));
    var run = fixture.Host.RunAsync();
    Check(frozen.Entered.Wait(2000), "watchdog worker deliberately blocked before first frame");
    fixture.Clock.Advance(5000);
    var result = await run.WaitAsync(TimeSpan.FromSeconds(2));
    Check(result.CancellationReason!.Contains("Frame completion"), "independent watchdog terminates blocked worker");
    frozen.Release.Set(); await fixture.Host.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(2));
}
clock = new(); watchdog = new(clock); watchdog.BeginFrame(); watchdog.Input(); watchdog.FrameCompleted();
clock.Advance(5000); Check(watchdog.Check()!.Contains("Frame completion"), "input arriving during frame remains outstanding");
c = Logic(); Drag(c, 10, 10, 80, 80); c.Handle(new PointerButton(1, true, 40, 40, 0, 2)); Check(c.Result != null, "double click confirm");
using (var fixture = new Fixture(layout))
{
    fixture.Session.CancelRequested = true; fixture.Host.Pump();
    Check(fixture.Session.Presents.Count == 0 && fixture.Host.Controller.CancellationReason == "Escape", "cancel flag checked before first render");
}
using (var fixture = new Fixture(layout))
{
    fixture.Session.Outputs = new[] { layout[0] with { Logical = new(-201, 0, 200, 200) }, layout[1] };
    fixture.Session.Send(new LayoutChanged()); fixture.Host.Pump();
    Check(fixture.Host.Controller.CancellationReason!.Contains("layout or scale"), "origin change cancels frozen screenshot mapping");
}
using (var toolbar = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Bgra8888, SKAlphaType.Premul)))
{
    toolbar.Erase(SKColors.Red);
    var extension = new Extension { Surface = new(toolbar, new(210, 20, 20, 20), 7), Shapes = new[] { new OverlayShape(OverlayShapeKind.Rectangle, new(10, 10, 10, 10), SKColors.Red, SKColors.Transparent) } };
    c = Logic(extension: extension);
    Down(c, 220, 25); Move(c, 30, 30); Up(c, 30, 30);
    Check(extension.Received.Count == 3 && c.Selection.IsEmpty, "extension hit and pointer capture forwarding");
    int scenes = 0; c.Publish((output, scene) => { scenes++; Check(scene.ExternalOverlay?.Revision == 7, "extension revision exposed"); });
    Check(scenes == 2, "scene callback per monitor");
    c.RunAction(RegionCaptureAction.SwapToolType); c.Handle(Key(30));
    Check(extension.Received.Last() is KeyEvent, "annotation key forwarding");
    c.Handle(Key(15)); Check(!extension.AnnotationMode, "tab activates region tool");
    Drag(c, 10, 10, 80, 80); c.Handle(Key(28)); Check(c.Result?.Annotations.Count == 1, "committed annotations snapshot in capture result");
}
c = Logic(); Drag(c, 10, 10, 80, 80); c.RunAction(RegionCaptureAction.SwapToolType); c.Handle(Key(28));
Check(c.Result != null, "enter confirms selection in annotation mode");
var eventQueue = new OverlayEventQueue();
for (int i = 0; i < 1000; i++) eventQueue.TryWrite(new PointerMoved(i, 0, 0));
eventQueue.TryWrite(Key(106)); eventQueue.TryWrite(new PointerMoved(2000, 0, 0)); eventQueue.TryWrite(new PointerWheel(0, 1, 0, 0, 0));
var events = new List<OverlayEvent>(); while (eventQueue.TryRead(out var input)) events.Add(input);
Check(events.Count == 4 && events[0] is PointerMoved { X: 999 } && events[1] is KeyEvent && events[2] is PointerMoved { X: 2000 } && events[3] is PointerWheel, "coalescing preserves discrete event barriers");
Console.WriteLine($"PASS: {checks} checks; no display/window APIs invoked.");

sealed class FakeClock : IOverlayClock
{
    private long ticks;
    public TimeSpan Now => TimeSpan.FromTicks(Interlocked.Read(ref ticks));
    public void Advance(int ms) => Interlocked.Add(ref ticks, TimeSpan.FromMilliseconds(ms).Ticks);
}
sealed class Windows : IWindowRectProvider
{
    public IEnumerable<WindowRect> GetRects(bool includeControls)
    {
        if (includeControls) yield return new(new(20, 20, 40, 40), true);
        yield return new(new(0, 0, 150, 150));
    }
}
sealed class Extension : IOverlayExtension
{
    public bool AnnotationMode, Block;
    public List<OverlayEvent> Received { get; } = new();
    public OverlayBitmap? Surface { get; set; }
    public IReadOnlyList<OverlayShape> Shapes { get; set; } = Array.Empty<OverlayShape>();
    public ManualResetEventSlim Entered = new(), Release = new();
    public bool HandlesPointer(LogicalPoint p) => p.X > 200;
    public void OnPointer(OverlayEvent input) { Received.Add(input); if (Block) { Entered.Set(); Release.Wait(); } }
    public void OnKey(KeyEvent input) => Received.Add(input);
    public OverlayBitmap? Bitmap => Surface;
    public IReadOnlyList<OverlayShape> CommittedAnnotations => Shapes;
    public void SetAnnotationMode(bool active) => AnnotationMode = active;
}
sealed record PresentCall(int Output, OverlayRect? Dirty, byte[] Bytes);
sealed class FakeSession : IOverlaySession
{
    private readonly OverlayEventQueue events = new();
    public IReadOnlyList<OverlayOutput> Outputs { get; set; }
    public OverlayRect Desktop => Outputs.Select(o => o.Logical).Aggregate(OverlayRect.Union);
    public ChannelReader<OverlayEvent> Events => events;
    public List<PresentCall> Presents { get; } = new();
    public bool CancelRequested { get; set; }
    public FakeSession(IReadOnlyList<OverlayOutput> outputs) => Outputs = outputs;
    public (int Width, int Height) PixelSize(int output) => ((int)Math.Ceiling(Outputs[output].Logical.Width * Outputs[output].Scale), (int)Math.Ceiling(Outputs[output].Logical.Height * Outputs[output].Scale));
    public void Present(int output, ReadOnlySpan<byte> bgra, int stride, OverlayRect? dirty) => Presents.Add(new(output, dirty, bgra.ToArray()));
    public void SetCursor(OverlayCursor cursor) { }
    public void Send(OverlayEvent input) => events.TryWrite(input);
    public void Dispose() => events.TryComplete();
}
sealed class Fixture : IDisposable
{
    public FakeClock Clock { get; } = new();
    public FakeSession Session { get; }
    public OverlayHostLoop Host { get; }
    private readonly MonitorRenderer[] renderers;
    public Fixture(IReadOnlyList<OverlayOutput> layout, IOverlayExtension? extension = null)
    {
        Session = new(layout);
        renderers = layout.Select(o =>
        {
            var (w, h) = Session.PixelSize(Array.IndexOf(layout.ToArray(), o));
            var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)); bitmap.Erase(SKColors.CornflowerBlue);
            return new MonitorRenderer(o.Name, new(o.Logical.X, o.Logical.Y, o.Logical.Width, o.Logical.Height), o.Scale, bitmap, takeScreenshotOwnership: true);
        }).ToArray();
        Host = new(Session, renderers, Clock, new CaptureOptions { QuickCapture = false, InputDelay = TimeSpan.Zero }, extension: extension);
    }
    public void Dispose() { Session.Dispose(); foreach (var renderer in renderers) renderer.Dispose(); }
}
