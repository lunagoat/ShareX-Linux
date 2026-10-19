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
using Avalonia.Threading;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ShareX.Tools;

public sealed record VideoPlaybackInfo(double Duration, int Width, int Height);

/// <summary>libmpv decodes audio/video; its software render API presents inside Avalonia on both Wayland and X11.</summary>
public sealed class MediaPlayer : Control, IDisposable
{
    private nint _mpv, _renderer;
    private WriteableBitmap? _frame;
    private readonly DispatcherTimer _timer;
    private double _volume = 1, _duration;
    private bool _muted, _playing, _disposed;
    private int _generation;
    private double _aspect = 16d / 9;

    public event Action<double>? PositionChanged;
    public event Action<bool>? IsPlayingChanged;
    public event Action<Exception>? PlaybackFailed;
    public event Action? VideoClicked;
    public bool IsPlaybackRequested => _playing;
    internal void SaveFrame(string path) => _frame?.Save(path, PngBitmapEncoderOptions.Default);
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); Set("volume", (_volume * 100).ToString(CultureInfo.InvariantCulture)); }
    }
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; Set("mute", value ? "yes" : "no"); }
    }

    public MediaPlayer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
        _timer.Tick += (_, _) => Tick();
        PointerReleased += (_, e) => { if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left) VideoClicked?.Invoke(); };
    }

    private unsafe void Initialize()
    {
        if (_mpv != 0) return;
        try
        {
            _mpv = mpv_create();
            if (_mpv == 0) throw new InvalidOperationException("Cannot initialize libmpv.");
            foreach (var (name, value) in new[] { ("vo", "libmpv"), ("hwdec", "no"), ("config", "no"),
                ("terminal", "no"), ("input-default-bindings", "no"), ("keep-open", "yes"), ("pause", "yes") })
                Check(mpv_set_option_string(_mpv, name, value));
            Check(mpv_initialize(_mpv));
            byte[] api = "sw\0"u8.ToArray();
            fixed (byte* pointer = api)
            {
                RenderParameter[] parameters = [new(1, (nint)pointer), default];
                Check(mpv_render_context_create(out _renderer, _mpv, parameters));
            }
            Volume = _volume;
            IsMuted = _muted;
        }
        catch (DllNotFoundException ex)
        {
            DisposeNative();
            throw new InvalidOperationException("Video playback requires libmpv (install the mpv package).", ex);
        }
        catch { DisposeNative(); throw; }
    }

    public async Task<VideoPlaybackInfo> LoadAsync(string path, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unload();
        int generation = _generation;
        Initialize();
        Command("loadfile", Path.GetFullPath(path), "replace");
        try
        {
            // file-loaded is authoritative: duration alone can still belong to the preceding file.
            for (int i = 0; i < 600; i++)
            {
                token.ThrowIfCancellationRequested();
                if (generation != _generation) throw new OperationCanceledException();
                while (true)
                {
                    MpvEvent current = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_mpv, 0));
                    if (current.Id == 0) break;
                    if (current.Id == 7 && current.Data != 0 && Marshal.ReadInt32(current.Data, 4) < 0)
                        throw new InvalidDataException("libmpv could not decode this video.");
                    if (current.Id != 8) continue;
                    _duration = Number("duration");
                    int width = (int)Number("video-params/w"), height = (int)Number("video-params/h");
                    if (!double.IsFinite(_duration) || _duration <= 0 || width <= 0 || height <= 0)
                        throw new InvalidDataException("This file does not contain a seekable video stream.");
                    _aspect = Number("video-params/aspect");
                    if (_aspect <= 0) _aspect = (double)width / height;
                    InvalidateMeasure();
                    _timer.Start();
                    return new(_duration, width, height);
                }
                await Task.Delay(25, token);
            }
            throw new TimeoutException("libmpv did not finish loading the video.");
        }
        catch { if (generation == _generation) Unload(); throw; }
    }

    public void Unload()
    {
        _generation++;
        _timer.Stop();
        _duration = 0;
        _playing = false;
        if (_mpv != 0) { Set("pause", "yes"); Command("stop"); }
        _frame?.Dispose();
        _frame = null;
        IsPlayingChanged?.Invoke(false);
        InvalidateVisual();
    }
    public void Seek(double seconds)
    {
        if (_duration > 0) Command("seek", Math.Clamp(seconds, 0, _duration).ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
    }
    public void Play()
    {
        if (_duration <= 0) return;
        if (Number("time-pos") >= _duration - 0.03) Seek(0);
        _playing = true;
        Set("pause", "no");
        IsPlayingChanged?.Invoke(true);
    }
    public void Pause()
    {
        _playing = false;
        Set("pause", "yes");
        IsPlayingChanged?.Invoke(false);
    }
    public void StepFrame(bool forward)
    {
        if (_duration <= 0) return;
        Pause();
        Command(forward ? "frame-step" : "frame-back-step");
    }

    private unsafe void Tick()
    {
        if (_renderer == 0 || _duration <= 0) return;
        try
        {
            while (Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_mpv, 0)).Id is int id && id != 0)
                if (id == 7) Pause();
            if (Get("eof-reached") == "yes" && _playing) Pause();
            PositionChanged?.Invoke(Math.Clamp(Number("time-pos"), 0, _duration));
            bool resize = _frame == null;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = Math.Clamp((int)Math.Ceiling(Bounds.Width * scale), 2, 3840);
            int height = Math.Clamp((int)Math.Ceiling(Bounds.Height * scale), 2, 2160);
            if (_frame?.PixelSize != new PixelSize(width, height)) resize = true;
            ulong update = mpv_render_context_update(_renderer);
            if (!resize && (update & 1) == 0) return;
            if (resize)
            {
                _frame?.Dispose();
                _frame = new(new(width, height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using ILockedFramebuffer target = _frame!.Lock();
            int* size = stackalloc int[] { width, height };
            nuint stride = (nuint)target.RowBytes;
            int block = 0;
            byte[] format = "bgra\0"u8.ToArray();
            fixed (byte* formatPointer = format)
            {
                RenderParameter[] parameters = [new(17, (nint)size), new(18, (nint)formatPointer),
                    new(19, (nint)(&stride)), new(20, target.Address), new(12, (nint)(&block)), default];
                Check(mpv_render_context_render(_renderer, parameters));
            }
            InvalidateVisual();
        }
        catch (Exception ex) { _timer.Stop(); PlaybackFailed?.Invoke(ex); }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : 360;
        return width / height > _aspect ? new(height * _aspect, height) : new(width, width / _aspect);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_frame != null) context.DrawImage(_frame, new Rect(_frame.Size), new Rect(Bounds.Size));
    }

    private string Get(string name)
    {
        nint value = mpv_get_property_string(_mpv, name);
        if (value == 0) return "";
        try { return Marshal.PtrToStringUTF8(value) ?? ""; }
        finally { mpv_free(value); }
    }
    private double Number(string name) => double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : 0;
    private void Set(string name, string value) { if (_mpv != 0) Check(mpv_set_property_string(_mpv, name, value)); }
    private void Command(params string[] args)
    {
        nint[] pointers = new nint[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            Check(mpv_command(_mpv, pointers));
        }
        finally { foreach (nint pointer in pointers) if (pointer != 0) Marshal.FreeCoTaskMem(pointer); }
    }
    private static void Check(int code)
    {
        if (code < 0) throw new InvalidOperationException("libmpv: " + Marshal.PtrToStringUTF8(mpv_error_string(code)));
    }
    private void DisposeNative()
    {
        if (_renderer != 0) mpv_render_context_free(_renderer);
        _renderer = 0;
        if (_mpv != 0) mpv_terminate_destroy(_mpv);
        _mpv = 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Unload();
        DisposeNative();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct RenderParameter(int type, nint data)
    {
        public readonly int Type = type;
        public readonly nint Data = data;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MpvEvent
    {
        public int Id, Error;
        public ulong Reply;
        public nint Data;
    }
    private const string Library = "libmpv.so.2";
    [DllImport(Library)] private static extern nint mpv_create();
    [DllImport(Library)] private static extern int mpv_initialize(nint handle);
    [DllImport(Library)] private static extern void mpv_terminate_destroy(nint handle);
    [DllImport(Library)] private static extern int mpv_set_option_string(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library)] private static extern int mpv_set_property_string(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library)] private static extern nint mpv_get_property_string(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library)] private static extern int mpv_command(nint handle, nint[] args);
    [DllImport(Library)] private static extern nint mpv_wait_event(nint handle, double timeout);
    [DllImport(Library)] private static extern nint mpv_error_string(int error);
    [DllImport(Library)] private static extern void mpv_free(nint data);
    [DllImport(Library)] private static extern int mpv_render_context_create(out nint context, nint handle, RenderParameter[] parameters);
    [DllImport(Library)] private static extern ulong mpv_render_context_update(nint context);
    [DllImport(Library)] private static extern int mpv_render_context_render(nint context, RenderParameter[] parameters);
    [DllImport(Library)] private static extern void mpv_render_context_free(nint context);
}
