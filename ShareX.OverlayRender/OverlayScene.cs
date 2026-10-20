// ShareX overlay scene renderer. GPL-3.0-or-later.
using SkiaSharp;

namespace ShareX.OverlayRender;

/// <summary>Global logical coordinates. Screenshots and returned damage use physical pixels.</summary>
public readonly record struct LogicalRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct LogicalPoint(double X, double Y);

/// <summary>Value snapshot of RegionCaptureOptions; intentionally independent of the UI assembly.</summary>
public sealed record OverlayOptions
{
    public int BackgroundDimStrength { get; init; } = 20;
    public bool ShowCenterCrosshair { get; init; } = true;
    public bool ShowScreenCrosshair { get; init; }
    public bool ShowMagnifier { get; init; } = true;
    public int MagnifierSize { get; init; } = 150;
    public int MagnifierPixelCount { get; init; } = 15;
    public bool UseSquareMagnifier { get; init; }
    public bool ShowInfo { get; init; } = true;
    public bool UseCustomInfoText { get; init; }
    public string CustomInfoText { get; init; } = "X: $x, Y: $y$nR: $r, G: $g, B: $b$nHex: $hex";
    // Defaults from ShareXTheme.axaml. The host can supply its active theme and font.
    public SKColor Accent { get; init; } = new(0x3E, 0x83, 0xF2);
    public SKColor InfoBackground { get; init; } = new(0x27, 0x27, 0x27);
    public SKColor InfoBorder { get; init; } = new(0x1F, 0x1F, 0x1F);
    public SKColor InfoForeground { get; init; } = new(0xD8, 0xDA, 0xDB);
}

public enum OverlayShapeKind { Rectangle, Ellipse, Line }

/// <summary>One in-progress annotation, in global logical coordinates. Fill may be transparent.</summary>
public sealed record OverlayShape(OverlayShapeKind Kind, LogicalRect Bounds, SKColor Stroke, SKColor Fill, float StrokeWidth = 2);

/// <summary>
/// Caller-owned immutable premultiplied BGRA bitmap, composited last (e.g. toolbar).
/// Increment Revision after changing its pixels; retain the old bitmap until Update returns.
/// </summary>
public sealed record OverlayBitmap(SKBitmap Bitmap, LogicalRect Bounds, long Revision = 0, IReadOnlyList<LogicalRect>? DirtyRegions = null);

public sealed record OverlayScene
{
    public LogicalRect Selection { get; init; }
    public LogicalRect Hover { get; init; }
    public LogicalPoint Pointer { get; init; }
    public bool ShowHandles { get; init; }
    public OverlayOptions Options { get; init; } = new();
    public OverlayShape? Shape { get; init; }
    public OverlayBitmap? ExternalOverlay { get; init; }
    /// <summary>Optional already formatted HUD text. Null uses pointer/custom pixel information.</summary>
    public string? InfoText { get; init; }
}

/// <summary>
/// Conservative geometry damage, clipped to a monitor. Regions do not overlap; Bounds is their
/// smallest enclosing rectangle, not a pixel-diff claim. Empty means no framebuffer writes.
/// </summary>
public sealed record MonitorDamage(string MonitorId, SKRectI Bounds, IReadOnlyList<SKRectI> Regions);
