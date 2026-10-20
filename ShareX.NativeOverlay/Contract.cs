// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
namespace ShareX.NativeOverlay;

/// <summary>Rectangle in compositor logical coordinates (what xdg-output reports; the same space Hyprland/niri/sway use for monitor layout).</summary>
public readonly record struct OverlayRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public static OverlayRect Union(OverlayRect a, OverlayRect b)
    {
        int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }
}

/// <summary>One monitor as the compositor describes it.</summary>
public sealed record OverlayOutput(string Name, OverlayRect Logical, double Scale);

public enum OverlayCursor { Default, Crosshair, Grab, Grabbing, Move, ResizeNS, ResizeEW, ResizeNWSE, ResizeNESW, Text, Pointer, Hidden }

[Flags]
public enum OverlayModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Super = 8, CapsLock = 16, NumLock = 32 }

/// <summary>Button numbers follow Avalonia: 1 left, 2 middle, 3 right, 4 back (X1), 5 forward (X2).</summary>
public abstract record OverlayEvent;

/// <summary>Coordinates are GLOBAL logical coordinates (output origin + surface-local position), doubles, can lie outside every output during a drag.</summary>
public sealed record PointerMoved(double X, double Y, OverlayModifiers Modifiers) : OverlayEvent;
public sealed record PointerButton(int Button, bool Pressed, double X, double Y, OverlayModifiers Modifiers, int ClickCount = 1) : OverlayEvent;
/// <summary>Wheel deltas in "notches" (1.0 = one click); positive Y = scroll up like Avalonia's Delta.</summary>
public sealed record PointerWheel(double DeltaX, double DeltaY, double X, double Y, OverlayModifiers Modifiers) : OverlayEvent;
public sealed record PointerLeft : OverlayEvent;
/// <summary>EvdevCode is the Linux input keycode (KEY_ESC = 1); Keysym is the xkb keysym after modifiers/layout; Text is the produced UTF-8 text (null for non-printing keys).
/// Held keys auto-repeat as extra Pressed events with IsRepeat = true.</summary>
public sealed record KeyEvent(uint EvdevCode, uint Keysym, bool Pressed, bool IsRepeat, string? Text, OverlayModifiers Modifiers) : OverlayEvent;
/// <summary>Raised once if the compositor closes the overlay (e.g. output removed) or the session is disposed.</summary>
public sealed record OverlayClosed : OverlayEvent;

/// <summary>Raised when the output list or any output's logical rect / scale changed (including the fractional scale arriving after the surfaces were created).
/// Pixel sizes from <see cref="IOverlaySession.PixelSize"/> may have changed: the consumer must re-render every output completely.</summary>
public sealed record LayoutChanged : OverlayEvent;

/// <summary>
/// A full-desktop overlay made of one wlr-layer-shell surface (layer overlay, anchored to all edges, exclusive zone -1, exclusive keyboard focus after the first frame callbacks) per output.
/// Input events are global-desktop based so the consumer can treat the desktop as ONE space of size <see cref="Desktop"/>.
/// Pixels are per output: each output has its own retained frame of <see cref="PixelSize"/> physical pixels, so a monitor that is not touched by an event costs nothing.
/// </summary>
public interface IOverlaySession : IDisposable
{
    IReadOnlyList<OverlayOutput> Outputs { get; }

    /// <summary>Lock-free emergency flag set by dispatch before host rendering. Escape also unmaps on dispatch.</summary>
    bool CancelRequested => false;

    /// <summary>Union of all output logical rectangles.</summary>
    OverlayRect Desktop { get; }

    /// <summary>Events in arrival order. Completes (OverlayClosed is the last item) when the session ends. Safe to read from any one thread.</summary>
    System.Threading.Channels.ChannelReader<OverlayEvent> Events { get; }

    /// <summary>Physical pixel size of the frame for output <paramref name="output"/>: logical size times its (possibly fractional) scale, rounded up (matching the scene renderer).</summary>
    (int Width, int Height) PixelSize(int output);

    /// <summary>
    /// Show a frame of one output: premultiplied BGRA (ARGB8888 little endian), <paramref name="stride"/> bytes per row, size <see cref="PixelSize"/>.
    /// Only <paramref name="dirty"/> (output-local PIXEL rect, null = everything) is read and re-copied to the compositor buffers. Thread-safe; copies the dirty pixels before returning,
    /// so the caller may reuse the buffer immediately. Frames of the wrong size (the layout changed meanwhile) are dropped; a <see cref="LayoutChanged"/> event follows.
    /// </summary>
    void Present(int output, ReadOnlySpan<byte> bgra, int stride, OverlayRect? dirty);

    void SetCursor(OverlayCursor cursor);
}

public static class OverlaySessionFactory
{
    /// <summary>True when a Wayland compositor with wlr-layer-shell, wl_shm and a seat is reachable (checked by actually connecting).</summary>
    public static Func<bool> IsSupported = () => false;

    /// <summary>Opens the overlay on every output. Throws <see cref="NotSupportedException"/> when layer-shell is missing.</summary>
    public static Func<IOverlaySession> Open = () => throw new NotSupportedException("Wayland implementation not linked");
}
