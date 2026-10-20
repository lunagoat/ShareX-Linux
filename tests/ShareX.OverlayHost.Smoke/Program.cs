// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using ShareX.NativeOverlay;
using ShareX.NativeOverlay.Wayland;
using ShareX.OverlayHost;
using ShareX.OverlayRender;
using SkiaSharp;

// Check before registration, probing, opening the session, or any display API.
string display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
if (!display.StartsWith("sx-", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"refusing: WAYLAND_DISPLAY='{display}' is not a nested test compositor (socket must start with sx-)");
    return 9;
}
WaylandOverlay.Register();
using var session = OverlaySessionFactory.Open();
var renderers = session.Outputs.Select((output, index) =>
{
    var (width, height) = session.PixelSize(index);
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    bitmap.Erase(SKColors.SteelBlue);
    using (var canvas = new SKCanvas(bitmap))
    using (var paint = new SKPaint { Color = SKColors.Gold })
        for (int x = 0; x < width; x += 80) canvas.DrawRect(x, 0, 20, height, paint);
    return new MonitorRenderer(output.Name, new(output.Logical.X, output.Logical.Y, output.Logical.Width, output.Logical.Height), output.Scale, bitmap, takeScreenshotOwnership: true);
}).ToArray();
var host = new OverlayHostLoop(session, renderers, options: new CaptureOptions { QuickCapture = false });
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(args.Length > 0 ? int.Parse(args[0]) : 30));
var result = await host.RunAsync(timeout.Token);
Console.WriteLine(result.CancellationReason ?? $"Captured {result.Capture!.Bounds}, {result.Outputs!.Count} native-resolution tiles.");
foreach (var tile in result.Outputs ?? Array.Empty<OutputCapture>()) tile.Pixels.Dispose();
// A stuck worker must never have its framebuffer disposed concurrently. Process exit is the final backstop.
if (await Task.WhenAny(host.WorkerCompletion, Task.Delay(1000)) == host.WorkerCompletion)
    foreach (var renderer in renderers) renderer.Dispose();
return 0;
