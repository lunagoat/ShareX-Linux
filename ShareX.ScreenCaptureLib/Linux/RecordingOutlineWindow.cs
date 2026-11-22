// ShareX - GPL v3. See LICENSE.txt.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SystemRectangle = System.Drawing.Rectangle;

namespace ShareX.ScreenCaptureLib;

public sealed class RecordingOutlineWindow : Window
{
    private readonly RecordingRegionBorder border = new() { IsHitTestVisible = false };
    public IBrush AccentBrush { get => border.AccentBrush; set => border.AccentBrush = value; }
    public RecordingOutlineWindow(SystemRectangle region)
    {
        Title = "ShareX - Recording Region Outline";
        WindowDecorations = WindowDecorations.None;
        CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Position = new PixelPoint(region.X - 1, region.Y - 1);
        Width = region.Width + 2; Height = region.Height + 2;
        Content = border;
        Opened += (_, _) =>
        {
            // The compositor places the window in logical pixels; its content and input shape are in X pixels, which
            // differ on a scaled monitor when Hyprland gives XWayland native pixels (force_zero_scaling).
            double windowPixels = RecordingWindowPlatform.WindowPixelsPerLogicalPixel(region);
            double renderScaling = double.IsFinite(RenderScaling) && RenderScaling > 0 ? RenderScaling : 1;
            int w = region.Width + 2, h = region.Height + 2;
            Width = w * windowPixels / renderScaling; Height = h * windowPixels / renderScaling;
            _ = RecordingWindowPlatform.Place(this, region.X - 1, region.Y - 1, w, h, seeThrough: true);
            int pw = (int)System.Math.Round(w * windowPixels), ph = (int)System.Math.Round(h * windowPixels);
            RecordingWindowPlatform.Shape(this, [new(0, 0, pw, 1), new(0, ph - 1, pw, 1), new(0, 1, 1, ph - 2), new(pw - 1, 1, 1, ph - 2)], true);
        };
    }
}
