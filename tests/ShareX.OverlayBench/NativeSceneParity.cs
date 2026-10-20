// ShareX native overlay pixel checks against the existing control. GPL-3.0-or-later.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ShareX.OverlayRender;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;

namespace ShareX.OverlayBench;

internal static class NativeSceneParity
{
    // Render controls directly to a CPU bitmap. No Window is constructed or shown.
    public static int Run()
    {
        foreach (double scale in new[] { 1, 1.5 })
        foreach (bool hover in new[] { false, true })
        {
            const int width = 320, height = 240;
            int physicalWidth = (int)(width * scale), physicalHeight = (int)(height * scale);
            var active = new Rect(40, 40, 180, 130);
            var overlay = new RegionSelectionOverlay
            {
                Width = width, Height = height,
                AccentBrush = new SolidColorBrush(Color.FromRgb(62, 131, 242)),
                SelectionRectangle = hover ? default : active,
                HoverRectangle = hover ? active : default,
                ShowCenterCrosshair = true, DimAlpha = 51
            };
            var root = new Border { Width = width, Height = height, Background = Brushes.White, Child = overlay };
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            using var reference = new RenderTargetBitmap(new PixelSize(physicalWidth, physicalHeight), new Vector(96 * scale, 96 * scale));
            reference.Render(root);
            using var referencePixels = new SKBitmap(physicalWidth, physicalHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            reference.CopyPixels(new PixelRect(0, 0, physicalWidth, physicalHeight), referencePixels.GetPixels(), referencePixels.ByteCount, referencePixels.RowBytes);
            using var shot = new SKBitmap(physicalWidth, physicalHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            shot.Erase(SKColors.White);
            using var native = new MonitorRenderer("parity", new(0, 0, width, height), scale, shot);
            var logical = new LogicalRect(40, 40, 180, 130);
            native.Update(new OverlayScene
            {
                Selection = hover ? default : logical, Hover = hover ? logical : default,
                Options = new OverlayOptions { ShowMagnifier = false, ShowInfo = false }
            });
            // Dim strength and undimmed interior must agree to one channel value.
            foreach (var point in new[] { (10, 10), (80, 80), (260, 200) })
            {
                int x = (int)(point.Item1 * scale), y = (int)(point.Item2 * scale);
                SKColor a = native.Framebuffer.GetPixel(x, y), b = referencePixels.GetPixel(x, y);
                if (Math.Abs(a.Red - b.Red) > 1 || Math.Abs(a.Green - b.Green) > 1 || Math.Abs(a.Blue - b.Blue) > 1)
                    throw new InvalidOperationException($"Dim/interior mismatch at {point}: {a} vs {b}");
            }
            // Border geometry is intentionally physical-pixel aligned in the native renderer.
            // Loose perimeter check tolerates Avalonia's scaled ant phase and antialiasing.
            int nativeCount = 0, referenceCount = 0;
            for (int y = (int)(38 * scale); y < 173 * scale; y++)
            for (int x = (int)(38 * scale); x < 223 * scale; x++)
            {
                if (x > 43 * scale && x < 217 * scale && y > 43 * scale && y < 167 * scale) continue;
                static bool IsAccent(SKColor c) => c.Blue > c.Red + 25 && c.Blue > c.Green + 25;
                if (IsAccent(native.Framebuffer.GetPixel(x, y))) nativeCount++;
                if (IsAccent(referencePixels.GetPixel(x, y))) referenceCount++;
            }
            if (nativeCount < 100 || referenceCount < 100 || (double)nativeCount / referenceCount is < .3 or > 2)
                throw new InvalidOperationException("Accent border geometry/color mismatch.");
            Console.WriteLine($"PASS: RegionSelectionOverlay dim/interior/accent, scale {scale}, hover={hover}");
        }
        return 0;
    }
}
