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
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ShareX.AvaloniaUI.Input;
using ShareX.AvaloniaUI.Theming;
using ShareX.AvaloniaUI.Linux;
using SkiaSharp;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShareX.AvaloniaUI.Windows;

public partial class ScreenColorPickerWindow : Window
{
    private const int MagnifierPixelCount = 15;
    private readonly ScreenColorPickerOptions _options;
    private readonly TaskCompletionSource<ScreenColorPickerResult?> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Border _pickerPreview, _colorPreview;
    private readonly Image _magnifierImage;
    private readonly TextBlock _infoText;
    private SKBitmap? _screenshot;
    private Bitmap? _background;
    private WriteableBitmap? _magnifierBitmap;
    private DrawingRectangle _screenBounds;
    private Color _currentColor, _pressedColor;
    private PixelPoint _currentPosition, _pressedPosition;
    private bool _selecting, _cancelling;

    public ScreenColorPickerWindow() : this(new ScreenColorPickerOptions()) { }

    public ScreenColorPickerWindow(ScreenColorPickerOptions options)
    {
        _options = options ?? new();
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        AvaloniaXamlLoader.Load(this);
        _pickerPreview = this.FindControl<Border>("PickerPreview")!;
        _colorPreview = this.FindControl<Border>("ColorPreview")!;
        _magnifierImage = this.FindControl<Image>("MagnifierImage")!;
        _infoText = this.FindControl<TextBlock>("InfoText")!;
        this.FindControl<Grid>("MagnifierPanel")!.IsVisible = _options.ShowMagnifier;
        if (_options.ShowMagnifier)
        {
            _magnifierBitmap = new(new PixelSize(MagnifierPixelCount, MagnifierPixelCount), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _magnifierImage.Source = _magnifierBitmap;
        }
        Opened += async (_, _) =>
        {
            try
            {
                await LinuxCaptureOverlay.PlaceAsync(this, _screenBounds);
                Activate();
                Focus();
            }
            catch (Exception e) { _completionSource.TrySetException(e); Close(); }
        };
    }

    public Task<ScreenColorPickerResult?> PickAsync(Window? owner = null)
    {
        // Freeze before showing any picker pixels, so neither the preview nor dimming contaminate samples.
        // niri windows cannot span monitors: pick on the focused one.
        _screenBounds = LinuxScreenCapture.TryGetFocusedMonitor(out DrawingRectangle focusedMonitor) ? focusedMonitor : LinuxScreenCapture.DesktopBounds;
        _screenshot = LinuxScreenCapture.Capture(_screenBounds);
        using SKData data = _screenshot.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(data.ToArray());
        _background = new Bitmap(stream);
        Image image = new() { Source = _background, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Grid root = new();
        var canvas = (Canvas)Content!;
        Content = null;
        root.Children.Add(image);
        root.Children.Add(canvas);
        Content = root;
        Title = "ShareX screen color picker [" + Guid.NewGuid().ToString("N") + "]";
        Position = new PixelPoint(_screenBounds.X, _screenBounds.Y);
        Width = _screenBounds.Width;
        Height = _screenBounds.Height;
        Cursor = CursorAssetLoader.GetCrosshairCursor(RenderScaling);
        if (owner != null) Show(owner); else Show();
        return _completionSource.Task;
    }

    protected override void OnClosed(EventArgs e)
    {
        _completionSource.TrySetResult(null);
        _screenshot?.Dispose();
        _background?.Dispose();
        _magnifierBitmap?.Dispose();
        base.OnClosed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdatePicker(e.GetPosition(this));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        UpdatePicker(point.Position);
        _selecting = point.Properties.IsLeftButtonPressed;
        _cancelling = point.Properties.IsRightButtonPressed;
        _pressedColor = _currentColor;
        _pressedPosition = _currentPosition;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_selecting || _cancelling)
            Complete(_selecting ? new ScreenColorPickerResult(_pressedColor, _pressedPosition, e.KeyModifiers.HasFlag(KeyModifiers.Control)) : null);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Complete(null); e.Handled = true; }
    }

    private unsafe void UpdatePicker(Point client)
    {
        if (_screenshot == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        int x = Math.Clamp((int)(client.X * _screenshot.Width / Bounds.Width), 0, _screenshot.Width - 1);
        int y = Math.Clamp((int)(client.Y * _screenshot.Height / Bounds.Height), 0, _screenshot.Height - 1);
        SKColor color = _screenshot.GetPixel(x, y);
        _currentColor = Color.FromRgb(color.Red, color.Green, color.Blue);
        _currentPosition = new PixelPoint(_screenBounds.X + (int)(client.X * _screenBounds.Width / Bounds.Width),
            _screenBounds.Y + (int)(client.Y * _screenBounds.Height / Bounds.Height));
        _colorPreview.Background = new SolidColorBrush(_currentColor);
        _infoText.Text = ScreenColorPickerTextFormatter.Format(_options.InfoText, _currentColor, _currentPosition);
        if (_magnifierBitmap != null)
        {
            using var buffer = _magnifierBitmap.Lock();
            for (int row = 0; row < MagnifierPixelCount; row++)
            {
                uint* pixels = (uint*)((byte*)buffer.Address + row * buffer.RowBytes);
                for (int column = 0; column < MagnifierPixelCount; column++)
                {
                    SKColor pixel = _screenshot.GetPixel(Math.Clamp(x + column - 7, 0, _screenshot.Width - 1), Math.Clamp(y + row - 7, 0, _screenshot.Height - 1));
                    pixels[column] = 0xff000000 | (uint)pixel.Red << 16 | (uint)pixel.Green << 8 | pixel.Blue;
                }
            }
            _magnifierImage.InvalidateVisual();
        }
        double width = Math.Max(240, _pickerPreview.Bounds.Width), height = Math.Max(200, _pickerPreview.Bounds.Height);
        double left = client.X + 18, top = client.Y + 18;
        if (left + width > Bounds.Width) left = client.X - width - 18;
        if (top + height > Bounds.Height) top = client.Y - height - 18;
        Canvas.SetLeft(_pickerPreview, Math.Clamp(left, 0, Math.Max(0, Bounds.Width - width)));
        Canvas.SetTop(_pickerPreview, Math.Clamp(top, 0, Math.Max(0, Bounds.Height - height)));
        _pickerPreview.Opacity = 1;
    }

    private void Complete(ScreenColorPickerResult? result)
    {
        if (_completionSource.TrySetResult(result)) Close();
    }
}
