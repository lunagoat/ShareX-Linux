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
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShareX.AvaloniaUI.Windows;
using ShareX.HelpersLib;
using SkiaSharp;

namespace ShareX.Tools.Linux;

/// <summary>Temporary WP5 seam for the WP3 screen picker: sample a frozen compositor screenshot, one overlay per monitor.</summary>
internal sealed class ScreenColorPicker : Window
{
    private readonly SKBitmap _pixels;
    private readonly Bitmap _background;
    private readonly ScreenColorPickerOptions _options;
    private readonly TextBlock _label;
    private readonly Border _preview;
    private readonly Canvas _canvas;
    private readonly PixelRect _bounds;
    private readonly Action<ScreenColorPickerResult?> _complete;
    private readonly Image _magnifier;

    internal ScreenColorPicker(SKBitmap pixels, PixelRect bounds, double scaling, ScreenColorPickerOptions options,
        Action<ScreenColorPickerResult?> complete)
    {
        _pixels = pixels;
        _bounds = bounds;
        _options = options;
        _complete = complete;
        using var stream = new MemoryStream();
        pixels.Save(stream, SKEncodedImageFormat.Png);
        stream.Position = 0;
        _background = new Bitmap(stream);
        Title = "ShareX screen color picker";
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Position = bounds.Position;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        Cursor = new Cursor(StandardCursorType.Cross);
        _canvas = new Canvas();
        _label = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(8), FontSize = 16 };
        _magnifier = new Image { Width = 150, Height = 150, IsVisible = options.ShowMagnifier };
        RenderOptions.SetBitmapInterpolationMode(_magnifier, BitmapInterpolationMode.None);
        _preview = new Border { Background = Brushes.Black, Child = new StackPanel { Children = { _magnifier, _label } }, IsHitTestVisible = false };
        _canvas.Children.Add(_preview);
        Content = new Grid { Children = { new Image { Source = _background, Stretch = Stretch.Fill }, _canvas } };
        PointerMoved += (_, e) => Update(e.GetPosition(this));
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right) complete(null);
            else if (e.InitialPressMouseButton == MouseButton.Left)
            {
                var point = e.GetPosition(this);
                var sample = Sample(point);
                complete(new(sample.Color, sample.Position, e.KeyModifiers.HasFlag(KeyModifiers.Control)));
            }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; complete(null); } };
        Opened += (_, _) => { Activate(); Focus(); };
        Closed += (_, _) => { _background.Dispose(); _pixels.Dispose(); };
    }

    internal (Color Color, PixelPoint Position) Sample(Point point)
    {
        int x = Math.Clamp((int)(point.X / Math.Max(1, Bounds.Width) * _pixels.Width), 0, _pixels.Width - 1);
        int y = Math.Clamp((int)(point.Y / Math.Max(1, Bounds.Height) * _pixels.Height), 0, _pixels.Height - 1);
        SKColor color = _pixels.GetPixel(x, y);
        return (Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue), new(_bounds.X + x, _bounds.Y + y));
    }
    private void Update(Point point)
    {
        var sample = Sample(point);
        _label.Text = ScreenColorPickerTextFormatter.Format(_options.InfoText, sample.Color, sample.Position);
        int x = sample.Position.X - _bounds.X, y = sample.Position.Y - _bounds.Y;
        // CroppedBitmap retains the screenshot; no screen capture or native calls in pointer events.
        _magnifier.Source = new CroppedBitmap(_background, new PixelRect(Math.Clamp(x - 7, 0, Math.Max(0, _pixels.Width - 15)),
            Math.Clamp(y - 7, 0, Math.Max(0, _pixels.Height - 15)), Math.Min(15, _pixels.Width), Math.Min(15, _pixels.Height)));
        Canvas.SetLeft(_preview, Math.Clamp(point.X + 18, 0, Math.Max(0, Bounds.Width - 190)));
        Canvas.SetTop(_preview, Math.Clamp(point.Y + 18, 0, Math.Max(0, Bounds.Height - 200)));
    }

    public static Task<ScreenColorPickerResult?> PickAsync(ScreenColorPickerOptions options)
    {
        var probe = new Window();
        var screens = probe.Screens.All;
        TaskCompletionSource<ScreenColorPickerResult?> completion = new();
        List<ScreenColorPicker> overlays = [];
        bool finished = false;
        void Complete(ScreenColorPickerResult? result)
        {
            if (finished) return;
            finished = true;
            foreach (var overlay in overlays) overlay.Close();
            completion.TrySetResult(result);
        }
        try
        {
            // Capture every monitor before showing any overlay, so the magnifier cannot sample itself.
            foreach (var screen in screens)
                overlays.Add(new(CaptureScreen(screen.Bounds), screen.Bounds, screen.Scaling, options, Complete));
            foreach (var overlay in overlays) { overlay.Closed += (_, _) => Complete(null); overlay.Show(); }
            if (overlays.Count == 0) Complete(null);
            else
            {
                overlays[0].Activate();
                overlays[0].Focus();
            }
        }
        catch { Complete(null); throw; }
        finally { probe.Close(); }
        return completion.Task;
    }
    private static SKBitmap CaptureScreen(PixelRect bounds) => DesktopTools.Capture(bounds);
}
