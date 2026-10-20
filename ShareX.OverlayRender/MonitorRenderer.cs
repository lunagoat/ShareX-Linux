// ShareX overlay scene renderer. GPL-3.0-or-later.
using System.Diagnostics;
using SkiaSharp;

namespace ShareX.OverlayRender;

/// <summary>
/// Retained CPU BGRA renderer for one monitor. No display or UI dependencies. Call serially per
/// instance; independent instances may run concurrently. Framebuffer is borrowed read-only and
/// remains valid until disposal. Update finishes all drawing before returning damage.
/// </summary>
public sealed class MonitorRenderer : IDisposable
{
    private readonly SKBitmap _original;
    private readonly SKImage _originalImage;
    private readonly SKBitmap _dimmed;
    private readonly SKBitmap _frame;
    private readonly SKCanvas _canvas;
    private readonly SKTypeface _typeface;
    private readonly SKFont _font;
    private readonly bool _ownsTypeface;
    private readonly SKPaint _paint = new() { IsAntialias = false };
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([5, 5], 0);
    private OverlayScene? _previous;
    private int _dimStrength = -1;
    private bool _disposed;

    public string MonitorId { get; }
    public LogicalRect LogicalBounds { get; }
    public double Scale { get; }
    public SKBitmap Framebuffer => _frame;
    public long RetainedBytes => (long)_frame.RowBytes * _frame.Height * 3;
    public TimeSpan DimCacheBuildTime { get; private set; }
    private SKRectI Surface => new(0, 0, _frame.Width, _frame.Height);

    /// <param name="screenshot">Frozen BGRA8888 premultiplied/opaque screenshot.</param>
    /// <param name="takeScreenshotOwnership">Transfer ownership to avoid a startup copy; otherwise cloned.</param>
    /// <param name="typeface">Optional Segoe UI/Inter font supplied by host; must outlive the renderer.</param>
    public MonitorRenderer(string monitorId, LogicalRect logicalBounds, double scale, SKBitmap screenshot, SKTypeface? typeface = null, bool takeScreenshotOwnership = false)
    {
        if (!double.IsFinite(scale) || scale <= 0 || logicalBounds.IsEmpty ||
            !double.IsFinite(logicalBounds.X) || !double.IsFinite(logicalBounds.Y) ||
            !double.IsFinite(logicalBounds.Width) || !double.IsFinite(logicalBounds.Height))
            throw new ArgumentException("Monitor bounds and scale must be finite and positive.");
        if (screenshot.ColorType != SKColorType.Bgra8888 ||
            screenshot.AlphaType is not (SKAlphaType.Premul or SKAlphaType.Opaque) ||
            screenshot.Width != (int)Math.Ceiling(logicalBounds.Width * scale) ||
            screenshot.Height != (int)Math.Ceiling(logicalBounds.Height * scale))
            throw new ArgumentException("Screenshot must be BGRA and ceil(logical size * scale).");
        MonitorId = monitorId;
        LogicalBounds = logicalBounds;
        Scale = scale;
        _original = takeScreenshotOwnership ? screenshot : screenshot.Copy();
        // An immutable bitmap lets SKImage share its pixels. Reuse the image so Update never
        // converts or snapshots an entire mutable screenshot just to draw a clipped interior.
        _original.SetImmutable();
        _originalImage = SKImage.FromBitmap(_original);
        _dimmed = new SKBitmap(_original.Info);
        _frame = new SKBitmap(_original.Info);
        _canvas = new SKCanvas(_frame);
        _ownsTypeface = typeface == null;
        _typeface = typeface ?? SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
        _font = new SKFont(_typeface, (float)(12 * scale));
        BuildDimCache(20);
        Copy(_dimmed, _frame, Surface);
    }

    /// <summary>Caller-owned native-resolution crop of the frozen screenshot, without overlay chrome.
    /// Coordinates are output-local physical pixels. Call on the renderer owner thread.</summary>
    public SKBitmap CapturePixels(SKRectI pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Left < 0 || pixels.Top < 0 ||
            pixels.Right > _original.Width || pixels.Bottom > _original.Height)
            throw new ArgumentOutOfRangeException(nameof(pixels));
        var result = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(_original, new SKRect(pixels.Left, pixels.Top, pixels.Right, pixels.Bottom),
            new SKRect(0, 0, pixels.Width, pixels.Height));
        return result;
    }

    /// <summary>Full cache rebuild only at startup or when dim strength changes.</summary>
    private void BuildDimCache(int strength)
    {
        long start = Stopwatch.GetTimestamp();
        Copy(_original, _dimmed, Surface);
        using var canvas = new SKCanvas(_dimmed);
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Clamp((int)Math.Round(strength / 100d * 255), 0, 255)) };
        canvas.DrawRect(Surface, paint);
        _dimStrength = strength;
        DimCacheBuildTime = Stopwatch.GetElapsedTime(start);
    }

    public MonitorDamage Update(OverlayScene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        var damage = new DamageSet(Surface);
        if (_dimStrength != scene.Options.BackgroundDimStrength)
        {
            BuildDimCache(scene.Options.BackgroundDimStrength);
            damage.Add(Surface);
        }
        if (_previous == null) damage.Add(Surface);
        else ComputeDamage(_previous, scene, damage);
        if (damage.Regions.Count > 0)
        {
            using var clip = new SKRegion();
            foreach (SKRectI rect in damage.Regions)
            {
                Copy(_dimmed, _frame, rect);
                clip.Op(rect, SKRegionOperation.Union);
            }
            _canvas.Save();
            _canvas.ClipRegion(clip);
            DrawScene(scene);
            _canvas.Restore();
        }
        _canvas.Flush();
        _previous = scene;
        return new MonitorDamage(MonitorId, damage.Bounds, damage.Regions.ToArray());
    }

    private SKRect Map(LogicalRect r) => new((float)((r.X - LogicalBounds.X) * Scale),
        (float)((r.Y - LogicalBounds.Y) * Scale), (float)((r.Right - LogicalBounds.X) * Scale), (float)((r.Bottom - LogicalBounds.Y) * Scale));
    private SKPoint Map(LogicalPoint p) => new((float)((p.X - LogicalBounds.X) * Scale), (float)((p.Y - LogicalBounds.Y) * Scale));
    private static SKRectI Round(SKRect r, float pad = 0) => (r.Width <= 0 || r.Height <= 0) ? default : new(
        (int)Math.Floor(r.Left - pad), (int)Math.Floor(r.Top - pad),
        (int)Math.Ceiling(r.Right + pad), (int)Math.Ceiling(r.Bottom + pad));
    private static LogicalRect Active(OverlayScene s) => !s.Selection.IsEmpty ? s.Selection : s.Hover;
    private bool HasPointer(OverlayScene s) => s.Pointer.X >= LogicalBounds.X && s.Pointer.X < LogicalBounds.Right &&
        s.Pointer.Y >= LogicalBounds.Y && s.Pointer.Y < LogicalBounds.Bottom;

    private void ComputeDamage(OverlayScene old, OverlayScene next, DamageSet d)
    {
        SKRectI a = Round(Map(Active(old))), b = Round(Map(Active(next)));
        if (a != b)
        {
            // Only pixels entering/leaving the undimmed interior need restoring.
            foreach (var r in DamageSet.Subtract(a, b)) d.Add(r);
            foreach (var r in DamageSet.Subtract(b, a)) d.Add(r);
        }
        if (Active(old) != Active(next) || old.Selection != next.Selection || old.ShowHandles != next.ShowHandles || old.Options != next.Options)
        {
            ChromeDamage(old, d);
            ChromeDamage(next, d);
        }
        if (old.Pointer != next.Pointer || old.Options != next.Options)
        {
            CrosshairDamage(old, d);
            CrosshairDamage(next, d);
        }
        if (old.Shape != next.Shape)
        {
            ShapeDamage(old.Shape, d);
            ShapeDamage(next.Shape, d);
        }
        if (old.Pointer != next.Pointer || old.Options != next.Options || old.InfoText != next.InfoText)
        {
            d.Add(Round(HudBounds(old), 2));
            d.Add(Round(HudBounds(next), 2));
        }
        if (old.Selection != next.Selection || old.Options != next.Options)
        {
            d.Add(Round(SelectionInfoBounds(old), 2));
            d.Add(Round(SelectionInfoBounds(next), 2));
        }
        if (old.ExternalOverlay != next.ExternalOverlay)
        {
            if (old.ExternalOverlay is { } x) d.Add(Round(Map(x.Bounds), 1));
            if (next.ExternalOverlay is { } y) d.Add(Round(Map(y.Bounds), 1));
        }
    }

    private void ChromeDamage(OverlayScene s, DamageSet d)
    {
        if (Active(s).IsEmpty) return;
        SKRect active = Map(Active(s));
        // A border inset into an adjacent monitor cannot paint this surface. Do not let
        // conservative stroke padding wake an otherwise untouched neighbour at a seam.
        if (active.Right > 0 && active.Bottom > 0 && active.Left < _frame.Width && active.Top < _frame.Height)
            AddOutline(d, active, 2);
        if (s.Selection.IsEmpty) return;
        if (s.ShowHandles)
            foreach (SKPoint p in Handles(Map(s.Selection))) d.Add(Round(SKRect.Create(p.X - 20, p.Y - 20, 40, 40)));
        if (s.Options.ShowCenterCrosshair)
        {
            SKRect r = Map(s.Selection);
            d.Add(Round(SKRect.Create(r.MidX - 12, r.MidY - 12, 24, 24)));
        }
    }

    private void CrosshairDamage(OverlayScene s, DamageSet d)
    {
        if (!s.Options.ShowScreenCrosshair) return;
        SKPoint p = Map(s.Pointer);
        if (p.Y >= 0 && p.Y < _frame.Height) d.Add(new(0, (int)Math.Floor(p.Y) - 1, _frame.Width, (int)Math.Floor(p.Y) + 3));
        if (p.X >= 0 && p.X < _frame.Width) d.Add(new((int)Math.Floor(p.X) - 1, 0, (int)Math.Floor(p.X) + 3, _frame.Height));
    }

    private void ShapeDamage(OverlayShape? shape, DamageSet d)
    {
        if (shape == null) return;
        SKRect r = Map(shape.Bounds);
        float pad = (float)(shape.StrokeWidth * Scale / 2 + 2);
        if (shape.Kind == OverlayShapeKind.Rectangle && shape.Fill.Alpha == 0) AddOutline(d, r, pad);
        else d.Add(Round(new SKRect(r.Left - pad, r.Top - pad, r.Right + pad, r.Bottom + pad)));
    }

    private static void AddOutline(DamageSet d, SKRect r, float pad)
    {
        if (r.IsEmpty) return;
        d.Add(Round(new(r.Left, r.Top, r.Right, r.Top + 1), pad));
        d.Add(Round(new(r.Left, r.Bottom - 1, r.Right, r.Bottom), pad));
        d.Add(Round(new(r.Left, r.Top, r.Left + 1, r.Bottom), pad));
        d.Add(Round(new(r.Right - 1, r.Top, r.Right, r.Bottom), pad));
    }

    private void DrawScene(OverlayScene s)
    {
        if (!Active(s).IsEmpty)
        {
            SKRect active = Map(Active(s));
            // Integer aligned copy of original interior; clipping bounds all work to current damage.
            _canvas.Save();
            _canvas.ClipRect(active, SKClipOperation.Intersect, false);
            SetPaint(SKColors.White, SKPaintStyle.Fill);
            _paint.BlendMode = SKBlendMode.Src;
            _canvas.DrawImage(_originalImage, 0, 0, _paint);
            _paint.BlendMode = SKBlendMode.SrcOver;
            _canvas.Restore();
            SKRect border = new(active.Left + .5f, active.Top + .5f, active.Right - .5f, active.Bottom - .5f);
            SetPaint(s.Options.Accent, SKPaintStyle.Stroke);
            _canvas.DrawRect(border, _paint);
            _paint.Color = new SKColor(0, 0, 0, 230);
            _paint.PathEffect = _dash;
            // Reset phase on each edge, as RegionSelectionOverlay does.
            _canvas.DrawLine(border.Left, border.Top, border.Right, border.Top, _paint);
            _canvas.DrawLine(border.Left, border.Top, border.Left, border.Bottom, _paint);
            _canvas.DrawLine(border.Right, border.Top, border.Right, border.Bottom, _paint);
            _canvas.DrawLine(border.Left, border.Bottom, border.Right, border.Bottom, _paint);
        }
        if (!s.Selection.IsEmpty)
        {
            SKRect r = Map(s.Selection);
            if (s.Options.ShowCenterCrosshair)
            {
                PixelCross((int)Math.Floor(r.MidX) - 1, (int)Math.Floor(r.MidY) - 1, SKColors.Black);
                PixelCross((int)Math.Floor(r.MidX), (int)Math.Floor(r.MidY), s.Options.Accent);
            }
            if (s.ShowHandles)
            {
                foreach (SKPoint p in Handles(r))
                {
                    SetPaint(new SKColor(0, 0, 0, 100), SKPaintStyle.Fill);
                    using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
                    _paint.MaskFilter = blur;
                    _canvas.DrawCircle(p, 7.5f, _paint);
                    _paint.MaskFilter = null;
                    _paint.Color = SKColors.White;
                    _paint.IsAntialias = true;
                    _canvas.DrawCircle(p, 7.5f, _paint);
                }
            }
        }
        if (s.Options.ShowScreenCrosshair) DrawScreenCrosshair(s);
        DrawHud(s);
        DrawInfoPanel(SelectionInfoBounds(s), SelectionText(s), s.Options);
        DrawShape(s.Shape);
        if (s.ExternalOverlay is { } overlay)
        {
            SetPaint(SKColors.White, SKPaintStyle.Fill);
            _canvas.DrawBitmap(overlay.Bitmap, Map(overlay.Bounds), _paint);
        }
    }

    private void SetPaint(SKColor color, SKPaintStyle style, float width = 1)
    {
        _paint.Color = color;
        _paint.Style = style;
        _paint.StrokeWidth = width;
        _paint.PathEffect = null;
        _paint.IsAntialias = false;
    }

    private static IEnumerable<SKPoint> Handles(SKRect r)
    {
        yield return new(r.Left, r.Top); yield return new(r.MidX, r.Top); yield return new(r.Right, r.Top);
        yield return new(r.Right, r.MidY); yield return new(r.Right, r.Bottom); yield return new(r.MidX, r.Bottom);
        yield return new(r.Left, r.Bottom); yield return new(r.Left, r.MidY);
    }

    private void PixelCross(int x, int y, SKColor color)
    {
        SetPaint(color, SKPaintStyle.Fill);
        _canvas.DrawRect(x - 10, y, 21, 1, _paint);
        _canvas.DrawRect(x, y - 10, 1, 21, _paint);
    }

    private void DrawScreenCrosshair(OverlayScene s)
    {
        SKPoint p = Map(s.Pointer);
        float x = MathF.Floor(p.X) + .5f, y = MathF.Floor(p.Y) + .5f;
        void Line(float x1, float y1, float x2, float y2)
        {
            if (x2 <= x1 && y2 <= y1) return;
            SetPaint(s.Options.Accent, SKPaintStyle.Stroke);
            _canvas.DrawLine(x1, y1, x2, y2, _paint);
            _paint.Color = new SKColor(0, 0, 0, 230); _paint.PathEffect = _dash;
            _canvas.DrawLine(x1, y1, x2, y2, _paint);
        }
        if (y >= 0 && y < _frame.Height) { Line(0, y, x - 5, y); Line(x + 5, y, _frame.Width, y); }
        if (x >= 0 && x < _frame.Width) { Line(x, 0, x, y - 5); Line(x, y + 5, x, _frame.Height); }
    }

    private void DrawShape(OverlayShape? s)
    {
        if (s == null) return;
        SKRect r = Map(s.Bounds);
        void Draw()
        {
            switch (s.Kind)
            {
                case OverlayShapeKind.Rectangle: _canvas.DrawRect(r, _paint); break;
                case OverlayShapeKind.Ellipse: _canvas.DrawOval(r, _paint); break;
                case OverlayShapeKind.Line: _canvas.DrawLine(r.Left, r.Top, r.Right, r.Bottom, _paint); break;
            }
        }
        if (s.Fill.Alpha > 0 && s.Kind != OverlayShapeKind.Line) { SetPaint(s.Fill, SKPaintStyle.Fill); _paint.IsAntialias = true; Draw(); }
        if (s.StrokeWidth > 0 && s.Stroke.Alpha > 0)
        {
            SetPaint(s.Stroke, SKPaintStyle.Stroke, (float)(s.StrokeWidth * Scale));
            _paint.IsAntialias = true;
            Draw();
        }
    }

    private string PointerText(OverlayScene s)
    {
        if (s.InfoText != null) return s.InfoText;
        int x = (int)Math.Round(s.Pointer.X), y = (int)Math.Round(s.Pointer.Y);
        if (!s.Options.UseCustomInfoText) return $"X: {x} Y: {y}";
        SKPoint p = Map(s.Pointer);
        SKColor c = _original.GetPixel(Math.Clamp((int)Math.Round(p.X), 0, _original.Width - 1), Math.Clamp((int)Math.Round(p.Y), 0, _original.Height - 1));
        string hex = $"{c.Red:x2}{c.Green:x2}{c.Blue:x2}";
        return s.Options.CustomInfoText.Replace("$HEX", hex.ToUpperInvariant()).Replace("$hex", hex)
            .Replace("$x", x.ToString()).Replace("$y", y.ToString()).Replace("$r", c.Red.ToString())
            .Replace("$g", c.Green.ToString()).Replace("$b", c.Blue.ToString()).Replace("$n", "\n");
    }

    private SKSize TextSize(string text)
    {
        string[] lines = text.Split('\n');
        return new(lines.Max(x => _font.MeasureText(x)) + (float)(16 * Scale), (float)((lines.Length * 16 + 10) * Scale));
    }

    private SKRect HudBounds(OverlayScene s)
    {
        if (!HasPointer(s) || (!s.Options.ShowMagnifier && !s.Options.ShowInfo)) return default;
        float size = (float)((Math.Clamp(s.Options.MagnifierSize, 50, 500) + 2) * Scale);
        SKSize text = s.Options.ShowInfo ? TextSize(PointerText(s)) : default;
        float w = Math.Max(s.Options.ShowMagnifier ? size : 0, text.Width);
        float h = (s.Options.ShowMagnifier ? size : 0) + (s.Options.ShowInfo ? text.Height + (s.Options.ShowMagnifier ? (float)(10 * Scale) : 0) : 0);
        SKPoint p = Map(s.Pointer);
        float gap = (float)(18 * Scale);
        float x = p.X + gap, y = p.Y + gap;
        if (x + w > _frame.Width) x = p.X - w - gap;
        if (y + h > _frame.Height) y = p.Y - h - gap;
        return SKRect.Create(Math.Clamp(x, 0, Math.Max(0, _frame.Width - w)), Math.Clamp(y, 0, Math.Max(0, _frame.Height - h)), w, h);
    }

    private string SelectionText(OverlayScene s) => $"{Math.Round(s.Selection.Width * Scale)} × {Math.Round(s.Selection.Height * Scale)}";
    private SKRect SelectionInfoBounds(OverlayScene s)
    {
        if (!s.Options.ShowInfo || s.Selection.IsEmpty || s.Selection.X < LogicalBounds.X ||
            s.Selection.X >= LogicalBounds.Right || s.Selection.Bottom <= LogicalBounds.Y ||
            s.Selection.Bottom > LogicalBounds.Bottom) return default;
        SKRect r = Map(s.Selection);
        SKSize size = TextSize(SelectionText(s));
        float y = r.Bottom + (float)(8 * Scale);
        if (y + size.Height > _frame.Height) y = Math.Max(0, r.Top - (float)(36 * Scale));
        return SKRect.Create(Math.Clamp(r.Left, 0, Math.Max(0, _frame.Width - size.Width)), y, size.Width, size.Height);
    }

    private void DrawHud(OverlayScene s)
    {
        SKRect hud = HudBounds(s);
        if (hud.IsEmpty) return;
        float top = hud.Top;
        if (s.Options.ShowMagnifier)
        {
            float size = (float)(Math.Clamp(s.Options.MagnifierSize, 50, 500) * Scale);
            SKRect outer = SKRect.Create(hud.MidX - size / 2 - (float)Scale, top, size + (float)(2 * Scale), size + (float)(2 * Scale));
            SKRect content = SKRect.Create(outer.Left + (float)Scale, outer.Top + (float)Scale, size, size);
            SetPaint(SKColors.White, SKPaintStyle.Fill);
            _paint.IsAntialias = true;
            if (s.Options.UseSquareMagnifier) _canvas.DrawRect(outer, _paint); else _canvas.DrawOval(outer, _paint);
            _canvas.Save();
            using var clip = new SKPath();
            if (s.Options.UseSquareMagnifier) clip.AddRect(content); else clip.AddOval(content);
            _canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            int maximum = Math.Clamp((int)(size / 3), 3, 35);
            if ((maximum & 1) == 0) maximum--;
            int count = Math.Clamp(s.Options.MagnifierPixelCount, 3, maximum);
            if ((count & 1) == 0) count = count < maximum ? count + 1 : count - 1;
            SKPoint pointer = Map(s.Pointer);
            int cx = (int)Math.Round(pointer.X), cy = (int)Math.Round(pointer.Y);
            // Tiny nearest-neighbour pixel image, edge clamped to frozen screenshot.
            using var pixels = new SKBitmap(count, count, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (int y = 0; y < count; y++)
            for (int x = 0; x < count; x++)
                pixels.SetPixel(x, y, _original.GetPixel(Math.Clamp(cx + x - count / 2, 0, _original.Width - 1), Math.Clamp(cy + y - count / 2, 0, _original.Height - 1)));
            _canvas.DrawBitmap(pixels, content);
            int Snap(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
            int left = Snap(content.Left + count / 2 * size / count), right = Snap(content.Left + (count / 2 + 1) * size / count);
            int upper = Snap(content.Top + count / 2 * size / count), lower = Snap(content.Top + (count / 2 + 1) * size / count);
            SetPaint(s.Options.Accent.WithAlpha(75), SKPaintStyle.Fill);
            _canvas.DrawRect(content.Left, upper, left - content.Left, lower - upper - 1, _paint);
            _canvas.DrawRect(right, upper, content.Right - right, lower - upper - 1, _paint);
            _canvas.DrawRect(left, content.Top, right - left - 1, upper - content.Top, _paint);
            _canvas.DrawRect(left, lower, right - left - 1, content.Bottom - lower, _paint);
            SetPaint(new SKColor(0, 0, 0, 75), SKPaintStyle.Fill);
            for (int i = 1; i < count; i++)
            {
                _canvas.DrawRect(Snap(content.Left + i * size / count) - 1, content.Top, 1, size, _paint);
                _canvas.DrawRect(content.Left, Snap(content.Top + i * size / count) - 1, size, 1, _paint);
            }
            SetPaint(SKColors.Black, SKPaintStyle.Stroke);
            _canvas.DrawRect(new SKRect(left - .5f, upper - .5f, right - .5f, lower - .5f), _paint);
            _paint.Color = SKColors.White;
            _canvas.DrawRect(new SKRect(left + .5f, upper + .5f, right - 1.5f, lower - 1.5f), _paint);
            _canvas.Restore();
            SetPaint(SKColors.Black, SKPaintStyle.Stroke, (float)Scale);
            _paint.IsAntialias = true;
            if (s.Options.UseSquareMagnifier) _canvas.DrawRect(content, _paint); else _canvas.DrawOval(content, _paint);
            top = outer.Bottom + (float)(10 * Scale);
        }
        if (s.Options.ShowInfo)
        {
            string text = PointerText(s);
            SKSize size = TextSize(text);
            DrawInfoPanel(SKRect.Create(hud.MidX - size.Width / 2, top, size.Width, size.Height), text, s.Options);
        }
    }

    private void DrawInfoPanel(SKRect rect, string text, OverlayOptions options)
    {
        if (rect.IsEmpty) return;
        SetPaint(options.InfoBackground, SKPaintStyle.Fill);
        _paint.IsAntialias = true;
        _canvas.DrawRoundRect(rect, (float)(5 * Scale), (float)(5 * Scale), _paint);
        SetPaint(options.InfoBorder, SKPaintStyle.Stroke, (float)Scale);
        _paint.IsAntialias = true;
        _canvas.DrawRoundRect(rect, (float)(5 * Scale), (float)(5 * Scale), _paint);
        SetPaint(options.InfoForeground, SKPaintStyle.Fill);
        _paint.IsAntialias = true;
        float y = rect.Top + (float)(17 * Scale);
        foreach (string line in text.Split('\n'))
        {
            _canvas.DrawText(line, rect.MidX, y, SKTextAlign.Center, _font, _paint);
            y += (float)(16 * Scale);
        }
    }

    /// <summary>Copies only the supplied physical region, for presentation by the future host.</summary>
    public static unsafe void Copy(SKBitmap source, SKBitmap destination, SKRectI rect)
    {
        if (source.ColorType != SKColorType.Bgra8888 || destination.ColorType != SKColorType.Bgra8888 ||
            rect.Left < 0 || rect.Top < 0 || rect.Right > Math.Min(source.Width, destination.Width) ||
            rect.Bottom > Math.Min(source.Height, destination.Height)) throw new ArgumentOutOfRangeException(nameof(rect));
        if (rect.IsEmpty) return;
        byte* src = (byte*)source.GetPixels(), dst = (byte*)destination.GetPixels();
        for (int y = rect.Top; y < rect.Bottom; y++)
            new ReadOnlySpan<byte>(src + y * source.RowBytes + rect.Left * 4, rect.Width * 4)
                .CopyTo(new Span<byte>(dst + y * destination.RowBytes + rect.Left * 4, rect.Width * 4));
        destination.NotifyPixelsChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.Dispose(); _originalImage.Dispose(); _original.Dispose(); _dimmed.Dispose(); _frame.Dispose();
        _font.Dispose();
        if (_ownsTypeface) _typeface.Dispose();
        _paint.Dispose(); _dash.Dispose();
    }
}
