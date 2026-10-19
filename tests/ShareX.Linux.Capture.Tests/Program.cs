using ShareX.AvaloniaUI.Linux;
using ShareX.ScreenCaptureLib;
using SkiaSharp;
using System.Drawing;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

var monitors = LinuxScreenCapture.ParseHyprlandMonitors("""
[{"name":"left","x":-1280,"y":200,"width":1920,"height":1080,"scale":1.5,"transform":0,"focused":false},
 {"name":"rotated","x":0,"y":0,"width":2560,"height":1440,"scale":1.25,"transform":1,"focused":true},
 {"name":"disabled","x":0,"y":0,"width":800,"height":600,"scale":1,"disabled":true}]
""");
Assert(monitors.Length == 2, "disabled outputs excluded");
Assert(monitors[0].Bounds == new Rectangle(-1280, 200, 1280, 720), "negative origin and 1.5x scale");
Assert(monitors[1].Bounds == new Rectangle(0, 0, 1152, 2048), "rotated output and fractional scale");
using (SKBitmap source = new(300, 150))
{
    source.Erase(SKColors.Red);
    source.SetPixel(30, 45, SKColors.Blue);
    using SKBitmap crop = LinuxScreenCapture.CropDesktop(source, new Rectangle(-100, 20, 200, 100), new Rectangle(-80, 50, 40, 20));
    Assert(crop.Width == 60 && crop.Height == 30 && crop.GetPixel(0, 0) == SKColors.Blue, "portal crop accounts for scale and desktop origin");
}
if (args.Contains("--portal")) { await PortalChecks.RunAsync(); return; }
if (args.Contains("--ui")) { Environment.ExitCode = UiChecks.Run(); return; }
if (args.Contains("--e2e")) { await E2EChecks.RunAsync(); return; }
if (!args.Contains("--live")) return;
Directory.CreateDirectory("/tmp/sx-wp3/evidence");
foreach (var monitor in LinuxScreenCapture.GetMonitors())
{
    Console.WriteLine($"MONITOR: {monitor.Name} Bounds={monitor.Bounds} Scale={monitor.Scale} Primary={monitor.Primary}");
    using SKBitmap capture = new Screenshot().CaptureRectangle(monitor.Bounds);
    Assert(capture.Width == (int)Math.Round(monitor.Bounds.Width * monitor.Scale) && capture.Height == (int)Math.Round(monitor.Bounds.Height * monitor.Scale), "native pixel dimensions for " + monitor.Name);
    using var data = capture.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes("/tmp/sx-wp3/evidence/" + monitor.Name + ".png", data.ToArray());
}
using (var full = new Screenshot().CaptureFullscreen())
{
    double scale = LinuxScreenCapture.GetMonitors().Max(m => m.Scale);
    Assert(full.Width == (int)Math.Round(LinuxScreenCapture.DesktopBounds.Width * scale), "mixed-scale fullscreen width");
    using var data = full.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes("/tmp/sx-wp3/evidence/full.png", data.ToArray());
}
var cursor = LinuxScreenCapture.CursorPosition;
using (var region = new Screenshot().CaptureRectangle(new Rectangle(cursor.X, cursor.Y, 100, 80)))
    Assert(region.Width > 0 && region.Height > 0, "custom region under cursor");
var windows = LinuxCaptureWindows.GetWindows();
Assert(windows.Length > 0 && LinuxCaptureWindows.ActiveHandle != 0, "compositor window enumeration and active window");
using (var window = new Screenshot().CaptureActiveWindow()) Assert(window != null && window.Width > 0, "active window capture");
using (var cursorImage = new Screenshot { CaptureCursor = true }.CaptureActiveMonitor()) Assert(cursorImage.Width > 0, "cursor-inclusive monitor capture");
