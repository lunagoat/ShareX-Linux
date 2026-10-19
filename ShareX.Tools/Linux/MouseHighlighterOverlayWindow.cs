#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ShareX.Tools.Linux;
using SkiaSharp;
using System.Runtime.InteropServices;
using Color = System.Drawing.Color;

namespace ShareX.Tools;

// A transparent Avalonia X11/XWayland window with an empty XShape input region.
// Only our own overlay receives compositor commands; no global rules are installed.
internal sealed class MouseHighlighterOverlayWindow : Window, IDisposable
{
    private readonly MouseHighlighterService _service;
    private readonly PixelRect _screenBounds;
    private readonly WriteableBitmap _bitmap;
    private readonly Image _image;
    private nint _display;

    public MouseHighlighterOverlayWindow(MouseHighlighterService service, PixelRect bounds)
    {
        _service = service;
        _screenBounds = bounds;
        Title = "ShareX mouse highlighter overlay";
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Position = bounds.Position;
        Width = bounds.Width;
        Height = bounds.Height;
        _bitmap = new(new(bounds.Width, bounds.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        _image = new Image { Source = _bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Content = _image;
        try
        {
            Show();
            var handle = TryGetPlatformHandle();
            if (handle?.HandleDescriptor != "XID")
                throw new NotSupportedException("Mouse highlighting currently needs the X11/XWayland Avalonia backend. Use your compositor's cursor highlight effect on native Wayland.");
            _display = DesktopTools.XOpenDisplay(0);
            if (_display == 0) throw new InvalidOperationException("Cannot connect to X11 for a click-through overlay.");
            XShapeCombineRectangles(_display, handle.Handle, 2, 0, 0, 0, 0, 0, 0); // ShapeInput, ShapeSet
            XFlush(_display);
            if (DesktopTools.Hyprland)
            {
                string address = FindOwnAddress(handle.Handle);
                if (address.Length > 0)
                {
                    bool evalSuccess = false;
                    try
                    {
                        string lua = $"local win = hl.get_window('address:{address}') if win then hl.dispatch(hl.dsp.window.float({{ window = win }})) hl.dispatch(hl.dsp.window.resize({{ window = win, x = {bounds.Width}, y = {bounds.Height} }})) hl.dispatch(hl.dsp.window.move({{ window = win, x = {bounds.X}, y = {bounds.Y} }})) hl.dispatch(hl.dsp.window.pin({{ window = win }})) end return 'ok'";
                        string eval = ToolProcess.Read("hyprctl", "eval", lua).Trim();
                        if (eval.Contains("ok")) evalSuccess = true;
                    }
                    catch { }

                    if (!evalSuccess)
                    {
                        try
                        {
                            ToolProcess.Read("hyprctl", "dispatch", "setfloating", "address:" + address);
                            ToolProcess.Read("hyprctl", "dispatch", "resizewindowpixel", $"exact {bounds.Width} {bounds.Height},address:{address}");
                            ToolProcess.Read("hyprctl", "dispatch", "movewindowpixel", $"exact {bounds.X} {bounds.Y},address:{address}");
                            ToolProcess.Read("hyprctl", "dispatch", "pin", "address:" + address);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { Dispose(); throw; }
    }

    private static string FindOwnAddress(nint xid)
    {
        // PID and title constrain this to an overlay created by this process.
        return DesktopTools.GetWindows().LastOrDefault(w => w.ProcessId == Environment.ProcessId && w.Text == "ShareX mouse highlighter overlay")
            is { } window ? $"0x{window.Handle.ToInt64():x}" : "";
    }

    public void Refresh()
    {
        using (ILockedFramebuffer target = _bitmap.Lock())
        using (SKSurface surface = SKSurface.Create(new SKImageInfo(_screenBounds.Width, _screenBounds.Height, SKColorType.Bgra8888,
            SKAlphaType.Premul), target.Address, target.RowBytes))
        {
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Translate(-_screenBounds.X, -_screenBounds.Y);
            Draw(surface.Canvas, _service.Options);
            surface.Canvas.Flush();
        }
        _image.InvalidateVisual();
    }

    private void Draw(SKCanvas canvas, MouseHighlighterOptions options)
    {
        double time = _service.Time;
        SKPoint cursor = new(_service.CursorPosition.X, _service.CursorPosition.Y);
        if (options.Mode == MouseHighlightMode.Circle)
        {
            using SKPaint always = Paint(options.AlwaysColor);
            canvas.DrawCircle(cursor, options.Radius, always);
        }
        foreach (MouseHighlight highlight in _service.Highlights)
        {
            Color color = options.GetColor(highlight.Button);
            SKPoint center = new(highlight.Position.X, highlight.Position.Y);
            if (options.Mode == MouseHighlightMode.Circle)
            {
                using SKPaint fill = Paint(color, FadeOpacity(highlight, options, time));
                canvas.DrawCircle(center, options.Radius, fill);
            }
            else
            {
                double progress = Math.Clamp((time - highlight.Started) / options.RippleDuration, 0, 1);
                double fade = highlight.Released.HasValue
                    ? 1 - Math.Clamp((time - highlight.Released.Value) / options.RippleDuration, 0, 1) : 1;
                float radius = (float)(options.RippleSize / 2d * (0.35 + 0.65 * progress));
                using SKPaint ring = Paint(color, fade * options.RippleIntensity);
                ring.Style = SKPaintStyle.Stroke;
                ring.StrokeWidth = (float)(3 * options.RippleIntensity);
                if (highlight.Crosshairs)
                {
                    float gap = radius * 0.4f;
                    canvas.DrawLine(center.X - radius, center.Y, center.X - gap, center.Y, ring);
                    canvas.DrawLine(center.X + gap, center.Y, center.X + radius, center.Y, ring);
                    canvas.DrawLine(center.X, center.Y - radius, center.X, center.Y - gap, ring);
                    canvas.DrawLine(center.X, center.Y + gap, center.X, center.Y + radius, ring);
                }
                else
                {
                    using SKPaint glow = Paint(color, fade * options.RippleIntensity * 0.3);
                    using SKMaskFilter blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
                    glow.MaskFilter = blur;
                    canvas.DrawCircle(center, radius, glow);
                    canvas.DrawCircle(center, radius, ring);
                }
            }
        }
    }

    private static double FadeOpacity(MouseHighlight highlight, MouseHighlighterOptions options, double time)
    {
        if (!highlight.Released.HasValue) return 1;
        double age = time - highlight.Released.Value - options.FadeDelay;
        if (age < 0) return 1;
        return options.FadeDuration == 0 ? 0 : Math.Clamp(1 - age / options.FadeDuration, 0, 1);
    }

    private static SKPaint Paint(Color color, double opacity = 1) => new()
    {
        IsAntialias = true,
        Color = new SKColor(color.R, color.G, color.B, (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255))
    };


    public void Dispose()
    {
        Close();
        _bitmap.Dispose();
        if (_display != 0) DesktopTools.XCloseDisplay(_display);
        _display = 0;
    }

    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(nint display, nint window, int kind,
        int x, int y, nint rectangles, int count, int operation, int ordering);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
}
