// ShareX - GPL v3. See LICENSE.txt.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using SystemRectangle = System.Drawing.Rectangle;

namespace ShareX.ScreenCaptureLib;

public sealed class RecordingOutlineWindow : Window
{
    private readonly RecordingRegionBorder border = new() { IsHitTestVisible = false };
    public IBrush AccentBrush { get => border.AccentBrush; set => border.AccentBrush = value; }
    public RecordingOutlineWindow(SystemRectangle region)
    {
        WindowDecorations = WindowDecorations.None;
        CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Position = new PixelPoint(region.X - 1, region.Y - 1);
        Width = region.Width + 2; Height = region.Height + 2;
        Content = border;
        Opened += (_, _) =>
        {
            double scale = Screens.ScreenFromPoint(Position)?.Scaling ?? 1;
            Width = (region.Width + 2) / scale; Height = (region.Height + 2) / scale;
            int w = region.Width + 2, h = region.Height + 2;
            RecordingWindowPlatform.Place(this, region.X - 1, region.Y - 1, w, h);
            RecordingWindowPlatform.Shape(this, [new(0, 0, w, 1), new(0, h - 1, w, 1), new(0, 1, 1, h - 2), new(w - 1, 1, 1, h - 2)], true);
        };
    }
}
