using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Integration;
using ShareX.AvaloniaUI.Linux;
using ShareX.AvaloniaUI.Windows;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using ShareX.ImageEditor.Integration;
using ShareX.ImageEditor.Presentation.Views;
using SkiaSharp;
using System.Drawing;
using System.Reflection;

internal static class UiChecks
{
    private static object? Invoke(object target, string method, params object?[] parameters) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, parameters);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    public static int Run()
    {
        int result = 0;
        AvaloniaBootstrapper.Initialize([], async () =>
        {
            try { await CheckAsync(); }
            catch (Exception e) { Console.Error.WriteLine(e); result = 1; }
            finally { AvaloniaBootstrapper.Shutdown(); }
        }, () => { });
        AvaloniaBootstrapper.Run();
        return result;
    }

    private static async Task CheckAsync()
    {
        ImageEditorIntegration.Initialize();
        var bounds = LinuxScreenCapture.DesktopBounds;
        double scale = LinuxScreenCapture.GetMonitors().Max(m => m.Scale);
        SKBitmap fixture = new((int)(bounds.Width * scale), (int)(bounds.Height * scale));
        fixture.Erase(new SKColor(30, 60, 90));
        using (var canvas = new SKCanvas(fixture))
        using (var paint = new SKPaint { Color = SKColors.Lime })
            canvas.DrawRect(SKRect.Create(150, 150, 150, 150), paint);
        var window = new RegionCaptureWindow(new AvaloniaRegionCaptureRequest
        {
            Screenshot = fixture,
            ScreenBounds = bounds,
            RegionCaptureOptions = new() { DetectWindows = true },
            ImageEditorOptions = new(),
            EnableAnnotations = true
        });
        Task<AvaloniaRegionCaptureResult?> capture = window.CaptureAsync();
        try
        {
            await Task.Delay(3500);
            if (capture.IsCompleted) await capture; // Surface startup exceptions.
            var geometry = LinuxCaptureWindows.GetWindows().FirstOrDefault(w => w.Title == window.Title);
            Console.WriteLine($"Overlay: requested={bounds}; actual={geometry?.Rectangle}; renderScale={window.RenderScaling}; DIP={window.Bounds}");
            Assert(geometry != null && geometry.Rectangle.Location == bounds.Location && Math.Abs(geometry.Rectangle.Width - bounds.Width) <= 1 && Math.Abs(geometry.Rectangle.Height - bounds.Height) <= 1, "overlay spans compositor desktop without tiling");
            using (var image = LinuxScreenCapture.Capture(bounds))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes("/tmp/sx-wp3/evidence/overlay.png", data.ToArray());
            var editor = window.FindControl<EditorView>("EditorWorkspace")!;
            using SKBitmap stamp = new(20, 20);
            stamp.Erase(SKColors.Red);
            editor.InsertImageAnnotation(stamp, new Avalonia.Point(170, 170));
            await Task.Delay(150);
            Invoke(window, "SetSelection", new Rect(150, 150, 150, 150), null);
            Invoke(window, "OnKeyDown", new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            var result = await capture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(result != null && result.Image.Width == 150 && result.Image.Height == 150, "region selection returns pixel-exact crop");
            Assert(result!.ScreenRectangle == new Rectangle(bounds.X + 100, bounds.Y + 100, 100, 100), "last region stores logical coordinates at fractional scale");
            Assert(result.Image.GetPixel(0, 0) == SKColors.Lime, "region screenshot alignment");
            Console.WriteLine($"Annotation pixel={result.Image.GetPixel(25,25)}; modified={result.ImageModified}");
            Assert(result.Image.GetPixel(25, 25) == SKColors.Red, "annotation rendered into captured image");
            result.Image.Dispose();
        }
        finally { if (window.IsVisible) window.Close(); }

        var cancelFixture = new SKBitmap(200, 100);
        cancelFixture.Erase(SKColors.Blue);
        var cancelWindow = new RegionCaptureWindow(new AvaloniaRegionCaptureRequest
        {
            Screenshot = cancelFixture, ScreenBounds = new Rectangle(2600, 100, 200, 100),
            RegionCaptureOptions = new(), ImageEditorOptions = new()
        });
        Task<AvaloniaRegionCaptureResult?> cancelled = cancelWindow.CaptureAsync();
        try
        {
            await Task.Delay(500);
            Invoke(cancelWindow, "OnKeyUp", new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Escape });
            Assert(await cancelled.WaitAsync(TimeSpan.FromSeconds(5)) == null, "Esc cancels and closes region overlay");
        }
        finally { if (cancelWindow.IsVisible) cancelWindow.Close(); }

        var picker = new ScreenColorPickerWindow(new() { ShowMagnifier = true });
        Task<ScreenColorPickerResult?> pick = picker.PickAsync();
        try
        {
            await Task.Delay(500);
            Invoke(picker, "UpdatePicker", new Avalonia.Point(100, 100));
            var preview = picker.FindControl<TextBlock>("InfoText");
            Assert(preview != null && preview.Text != "#000000", "screen colour picker reads frozen pixels and formats colour");
            Invoke(picker, "OnKeyDown", new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert(await pick.WaitAsync(TimeSpan.FromSeconds(5)) == null, "Esc cancels screen colour picker");
        }
        finally { if (picker.IsVisible) picker.Close(); }
    }
}
