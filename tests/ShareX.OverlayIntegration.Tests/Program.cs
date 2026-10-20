// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Diagnostics;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Headless;
using ShareX;
using ShareX.NativeOverlay;
using ShareX.NativeOverlay.Wayland;
using ShareX.OverlayAvaloniaLayer;
using ShareX.OverlayHost;
using ShareX.OverlayRender;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;

// Native mode and its helper entry both refuse all non-sx sockets before probing.
if (!args.Contains("--headless") && (!OverlayHelperServer.IsTestDisplay || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SX_SANDBOX"))))
{ Console.Error.WriteLine("Refusing: tests require an sx- nested compositor and SX_SANDBOX."); return 9; }
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SX_SANDBOX")))
{ Console.Error.WriteLine("Refusing: SX_SANDBOX required even for headless tests."); return 9; }
ShareX.HelpersLib.LinuxPaths.InitializeSandbox();
if (args.Contains("--headless"))
{
    Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
    Environment.SetEnvironmentVariable("DISPLAY", null);
    AppBuilder.Configure<OverlayHelperApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true })
        .UseSkia().WithInterFont().SetupWithoutStarting();
    SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
    Headless();
    SynchronizationContext.SetSynchronizationContext(null);
    await Supervision();
    return 0;
}
await Nested();
return 0;

static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS: " + text); }
static SKBitmap Background(int width, int height)
{
    var image = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
    image.Erase(SKColors.DarkSlateBlue);
    using var c = new SKCanvas(image);
    using var p = new SKPaint { Color = SKColors.Lime };
    c.DrawRect(0, 0, width, 100, p); // conspicuous outside-selection sentinel
    return image;
}
static Button RectangleButton(AnnotationLayerHost layer) => layer.Window.GetVisualDescendants().OfType<Button>()
    .First(b => b.IsVisible && b.DataContext is ToolbarCustomizationItemViewModel { Tool: EditorTool.Rectangle } && b.Tag != null);
static void AssertImage(SKBitmap image, int width, int height)
{
    Check(image.Width == width && image.Height == height, $"crop has exact pixel dimensions (expected {width}x{height}, got {image.Width}x{image.Height})");
    int red = 0, sentinel = 0, background = 0;
    foreach (var p in image.Pixels)
    {
        if (p.Red > 160 && p.Green < 120 && p.Blue < 120) red++;
        if (p == SKColors.Lime) sentinel++;
        if (p == SKColors.DarkSlateBlue) background++;
    }
    Check(red > 20, "committed annotation pixels survive flattening");
    Check(sentinel == 0 && background > 1000, "outside-selection background excluded; inside pixels retained");
}
static void Headless()
{
    using (var frozen = Background(1300, 875))
    {
        using var tile = NativeRegionCapture.SplitScreenshot(frozen, new(-400,-50,1040,700), new("rotated", new(-400,-50,301,700),1.25));
        Check(tile.Width == 377 && tile.Height == 875, "parent split uses ceil at fractional scale and rotated/negative-origin geometry");
        Check(tile.GetPixel(0,0) == frozen.GetPixel(0,0), "parent split preserves output orientation");
    }
    var outputs = new[] { new OverlayOutput("portrait", new(-400, -50, 400, 700), 1), new OverlayOutput("fractional", new(0, -50, 640, 700), 1.25) };
    using var session = new FakeSession(outputs);
    var renderers = outputs.Select(o => { using var image = Background((int)Math.Ceiling(o.Logical.Width * o.Scale), (int)Math.Ceiling(o.Logical.Height * o.Scale));
        return new MonitorRenderer(o.Name, new(o.Logical.X, o.Logical.Y, o.Logical.Width, o.Logical.Height), o.Scale, image); }).ToArray();
    using var extension = new AnnotationOverlayExtension(session.Desktop, outputs, new());
    var clock = new Clock();
    var host = new OverlayHostLoop(session, renderers, clock, new() { QuickCapture = false, DetectWindows = false, InputDelay = TimeSpan.Zero }, extension: extension);
    void Pump(OverlayEvent? input = null) { if (input != null) session.Input.Writer.TryWrite(input); clock.Now += TimeSpan.FromMilliseconds(10); host.Pump(); }
    void Drag(double x, double y, double x2, double y2)
    { Pump(new PointerMoved(x, y, 0)); Pump(new PointerButton(1, true, x, y, 0)); Pump(new PointerMoved(x2, y2, 0)); Pump(new PointerButton(1, false, x2, y2, 0)); }
    Pump();
    Drag(-300, 150, 300, 450);
    Check(host.Controller.Selection == new LogicalRect(-300, 150, 600, 300), "selection spans negative-origin mixed-scale outputs");
    var button = RectangleButton(extension.Layer);
    var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), extension.Layer.Window)!.Value;
    Drag(center.X + session.Desktop.X, center.Y + session.Desktop.Y, center.X + session.Desktop.X, center.Y + session.Desktop.Y);
    Check(!extension.Layer.IsRegionToolActive, "actual toolbar button selects rectangle tool");
    Drag(-230, 220, 200, 390);
    Check(extension.Layer.HasAnnotations && extension.CommittedAnnotations.Count == 1, "extension retains committed annotation document");
    Pump(new KeyEvent(44, 0x7a, true, false, null, OverlayModifiers.Control));
    Check(extension.CommittedDocument.Count == 0, "Ctrl+Z forwarded from toolbar annotation mode");
    Pump(new KeyEvent(44, 0x7a, true, false, null, OverlayModifiers.Control | OverlayModifiers.Shift));
    Check(extension.CommittedDocument.Count == 1, "Ctrl+Shift+Z restores annotation document");
    extension.SetAnnotationMode(false);
    var popup = new Avalonia.Controls.Primitives.Popup { PlacementTarget = button, ShouldUseOverlayLayer = true,
        Placement = Avalonia.Controls.PlacementMode.Bottom, VerticalOffset = 100,
        Child = new Button { Width = 100, Height = 50, Background = Avalonia.Media.Brushes.Magenta } };
    ((Grid)extension.Layer.Window.Content!).Children.Add(popup);
    popup.IsOpen = true;
    Pump();
    Check(extension.HandlesPointer(new(-350, 600)), "open popup captures pointer outside toolbar in region mode");
    Check(TopLevel.GetTopLevel(popup.Child!) == extension.Layer.Window, "popup is composited inside retained headless layer");
    popup.IsOpen = false; Pump();

    using var desktop = Background(1040, 700);
    using var result = extension.Layer.RenderResult(desktop, new(100, 200, 600, 300));
    AssertImage(result!, 600, 300);
    using var high = Background(1300, 875);
    high.SetPixel(150, 270, SKColors.Yellow);
    using var dense = extension.Layer.RenderResult(high, new(100, 200, 600, 300), 1.25);
    AssertImage(dense!, 750, 375);
    Check(dense!.GetPixel(25,20) == SKColors.Yellow, "native-density background pixel survives annotation flattening without resampling");
    Pump(new KeyEvent(28, 0xff0d, true, false, null, 0));
    Check(host.Completion.Result.Capture != null, "Enter confirms through production controller");
    foreach (var tile in host.Completion.Result.Outputs!) tile.Pixels.Dispose();
    foreach (var renderer in renderers) renderer.Dispose();
    Console.WriteLine("Headless integration: no compositor connections or desktop capture.");
}
static async Task Supervision()
{
    // No socket is opened by these pre-initialization helper fault hooks. The helper guard
    // still requires a private sx- name and sandbox before accepting any test request.
    Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "sx-no-server-self-test");
    var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
    ShareX.AvaloniaUI.Linux.LinuxCaptureOverlay.Diagnostic = diagnostics.Enqueue;
    try
    {
        foreach (string mode in new[] { "stop", "crash", "heartbeat-only" })
        {
            string directory = OverlayPixels.CreateDirectory();
            try
            {
                using (var file = OverlayPixels.Create(Path.Combine(directory, "request.json")))
                using (var writer = new StreamWriter(file)) writer.Write(Newtonsoft.Json.JsonConvert.SerializeObject(new OverlayHelperRequest { Test = mode }));
                Check((File.GetUnixFileMode(Path.Combine(directory, "request.json")) & (UnixFileMode)0x3f) == 0, "shared request has mode 0600");
                using var screenshot = Background(400, 300);
                var request = new AvaloniaRegionCaptureRequest { Screenshot = screenshot, ScreenBounds = new(0,0,400,300), RegionCaptureOptions = new(), ImageEditorOptions = new() };
                bool fallback = false;
                var watch = Stopwatch.StartNew();
                await NativeRegionCapture.CaptureWithFallbackAsync(request, true, "fault test",
                    () => NativeRegionCapture.SuperviseAsync(directory, mode, mode == "heartbeat-only" ? TimeSpan.FromSeconds(1) : null),
                    () => { fallback = true; Check(request.Screenshot.Handle != IntPtr.Zero, "fallback retains original frozen pixels"); return Task.FromResult<AvaloniaRegionCaptureResult?>(null); });
                Check(fallback && watch.Elapsed < TimeSpan.FromSeconds(8), mode + ": child terminated and window fallback invoked within timeout");
            }
            finally { Directory.Delete(directory, true); }
        }
        Check(diagnostics.Any(s => s.Contains("heartbeat/startup timeout")) && diagnostics.Any(s => s.Contains("Injected helper failure")) && diagnostics.Any(s => s.Contains("hard timeout")), "fallback diagnostics distinguish heartbeat, crash and hard timeout");
    }
    finally { Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null); ShareX.AvaloniaUI.Linux.LinuxCaptureOverlay.Diagnostic = null; }
}
static async Task Nested()
{
    WaylandOverlay.Register();
    var outputs = WaylandOverlay.GetOutputs();
    Check(outputs.Count == 2, "nested compositor has two outputs");
    using var injection = new Injection();
    var desktop = outputs.Select(o => o.Logical).Aggregate(OverlayRect.Union);
    using var image = Background(desktop.Width, desktop.Height);
    var request = new AvaloniaRegionCaptureRequest { Screenshot = image, ScreenBounds = new(desktop.X, desktop.Y, desktop.Width, desktop.Height), EnableAnnotations = true,
        ImageEditorOptions = new(), RegionCaptureOptions = new() { QuickCapture = false, DetectWindows = false } };
    // Helper reports the real toolbar button's location via a private test-only control file.
    string directory = OverlayPixels.CreateDirectory();
    try
    {
        foreach (var (output, i) in outputs.Select((o, i) => (o, i)))
        { using var tile = Background((int)Math.Ceiling(output.Logical.Width * output.Scale), (int)Math.Ceiling(output.Logical.Height * output.Scale)); OverlayPixels.Write(Path.Combine(directory, i + ".shm"), tile); }
        void Write(string test) { File.Delete(Path.Combine(directory, "request.json")); using var file = OverlayPixels.Create(Path.Combine(directory, "request.json")); using var writer = new StreamWriter(file);
            writer.Write(Newtonsoft.Json.JsonConvert.SerializeObject(new OverlayHelperRequest { Outputs = outputs.Select((o, i) => new FrozenOutput(o, i + ".shm")).ToArray(), EnableAnnotations = true,
                Options = new() { QuickCapture = false, DetectWindows = false }, Test = test })); }
        void Move(int x, int y) => injection.Move(x, y, desktop.Width, desktop.Height);
        async Task Drag(int x, int y, int x2, int y2) { Move(x,y); injection.Button(true); await Task.Delay(30); Move(x2,y2); await Task.Delay(30); injection.Button(false); await Task.Delay(100); }
        // First scenario: the production flow. The helper is started BEFORE the screenshot exists (Prewarm), RunAsync then supplies the tiles.
        var prewarmed = NativeRegionCapture.Prewarm(request.RegionCaptureOptions, request.ImageEditorOptions, true, "toolbar") ?? throw new Exception("Prewarm unavailable in the nested compositor.");
        await Task.Delay(400); // the helper must wait for the screenshot, not fail or hang
        Task<AvaloniaRegionCaptureResult?> capture = NativeRegionCapture.RunAsync(request, "toolbar", prewarmed);
        string toolbar = Path.Combine(prewarmed.Directory, "toolbar.json");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(toolbar) && deadline.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(50);
        Check(File.Exists(toolbar), "prewarmed helper mapped native scene and actual toolbar");
        string firstToolbar = toolbar;
        toolbar = Path.Combine(directory, "toolbar.json");
        await Task.Delay(700);
        await Drag(100, 120, 500, 370); // must fit the smallest logical output (445 px tall at 1.6x)
        int[] point = Newtonsoft.Json.JsonConvert.DeserializeObject<int[]>(File.ReadAllText(firstToolbar))!;
        await Drag(point[0], point[1], point[0], point[1]);
        await Drag(140, 160, 450, 330);
        injection.Key(28, true); injection.Key(28, false);
        var result = await capture ?? throw new Exception("Capture cancelled.");
        using (result.Image) AssertImage(result.Image, (int)Math.Ceiling(400 * outputs[0].Scale), (int)Math.Ceiling(250 * outputs[0].Scale));
        Check(result.ImageModified, "annotation metadata returned to after-capture flow");
        foreach (string mode in new[] { "toolbar", "stall" })
        {
            File.Delete(toolbar); Write(mode);
            var cancel = NativeRegionCapture.SuperviseAsync(directory, mode);
            await Task.Delay(1500);
            var elapsed = Stopwatch.StartNew();
            injection.Key(1, true); injection.Key(1, false);
            Check(await cancel == null && elapsed.Elapsed < TimeSpan.FromSeconds(3), mode + ": Esc cancels, including stalled render worker");
        }
        {   // An aborted capture (screenshot failed) must not leave a prewarmed helper or its directory behind.
            var abandoned = NativeRegionCapture.Prewarm(request.RegionCaptureOptions, request.ImageEditorOptions, true) ?? throw new Exception("Prewarm unavailable.");
            int pid = abandoned.Child.Id; string abandonedDirectory = abandoned.Directory;
            await Task.Delay(500);
            abandoned.Dispose();
            await Task.Delay(300);
            Check(!Directory.Exists(abandonedDirectory) && !Directory.Exists("/proc/" + pid), "abandoned prewarm: helper killed and directory removed");
        }
        Write("stall");
        var timer = Stopwatch.StartNew();
        try { await NativeRegionCapture.SuperviseAsync(directory, "stall"); throw new Exception("Stalled helper accepted."); }
        catch (TimeoutException) { Check(timer.Elapsed < TimeSpan.FromSeconds(10), "stalled helper killed within heartbeat timeout; fallback boundary reached"); }
    }
    finally { Directory.Delete(directory, true); }
}
sealed class Clock : IOverlayClock { public TimeSpan Now { get; set; } }
sealed class FakeSession(OverlayOutput[] outputs) : IOverlaySession
{
    public Channel<OverlayEvent> Input { get; } = Channel.CreateUnbounded<OverlayEvent>();
    public IReadOnlyList<OverlayOutput> Outputs => outputs;
    public OverlayRect Desktop => outputs.Select(o => o.Logical).Aggregate(OverlayRect.Union);
    public ChannelReader<OverlayEvent> Events => Input.Reader;
    public (int Width, int Height) PixelSize(int i) => ((int)Math.Ceiling(outputs[i].Logical.Width * outputs[i].Scale), (int)Math.Ceiling(outputs[i].Logical.Height * outputs[i].Scale));
    public void Present(int output, ReadOnlySpan<byte> pixels, int stride, OverlayRect? dirty) { }
    public void SetCursor(OverlayCursor cursor) { }
    public void Dispose() { }
}
internal static class TestTools
{
    public static string Run(string command, params string[] args)
    { var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, UseShellExecute = false }; foreach (string arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!; string output = child.StandardOutput.ReadToEnd(); child.WaitForExit(); if (child.ExitCode != 0) throw new Exception(command + " failed"); return output; }
}
