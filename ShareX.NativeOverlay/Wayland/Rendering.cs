// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using NWayland.Protocols.Wayland;
using NWayland.Protocols.Wlr.WlrLayerShellUnstableV1;
using NWayland.Protocols.Viewporter;
using NWayland.Protocols.FractionalScaleV1;

namespace ShareX.NativeOverlay.Wayland;

public sealed partial class WaylandOverlaySession
{
    private sealed class Surface
    {
        public Output Output = null!;
        public WlSurface Proxy = null!;
        public ZwlrLayerSurfaceV1 Layer = null!;
        public WpViewport? Viewport;
        public WpFractionalScaleV1? FractionalScale;
        public int Width, Height;
        public byte[]? Frame;
        public int FrameStride, PixelWidth, PixelHeight;
        public readonly List<Buffer> Buffers = new();
        /// <summary>Pixel rect not yet committed to the compositor.</summary>
        public OverlayRect? Pending;
        public void Destroy()
        {
            foreach (var b in Buffers) b.Dispose();
            Buffers.Clear();
            FractionalScale?.Destroy(); Viewport?.Destroy(); Layer?.Destroy(); Proxy.Destroy();
        }
    }

    private sealed class Buffer : IDisposable
    {
        public WlBuffer Proxy = null!;
        public nint Map;
        public int Width, Height, Stride, Size;
        public bool Busy;
        public OverlayRect? Stale;
        public void Dispose()
        {
            Proxy?.Destroy();
            if (Map != 0 && Map != -1) Native.munmap(Map, (nuint)Size);
            Map = 0;
        }
    }

    private void CreateSurface(Output output)
    {
        var s = new Surface { Output = output, Proxy = compositor.CreateSurface(null, null) };
        surfaces.Add(s);
        s.Viewport = viewporter?.GetViewport(s.Proxy, null, null);
        s.FractionalScale = fractionalManager?.GetFractionalScale(s.Proxy, new WpFractionalScaleV1.Listener.Relay
        {
            OnPreferredScale = (_, scale) =>
            {
                output.FractionalScale = scale / 120.0;
                PublishLayout();
            }
        }, null);
        s.Layer = layerShell.GetLayerSurface(s.Proxy, output.Proxy, ZwlrLayerShellV1.LayerEnum.Overlay, "sharex-region",
            new ZwlrLayerSurfaceV1.Listener.Relay
            {
                OnConfigure = (layer, serial, width, height) =>
                {
                    layer.AckConfigure(serial);
                    if (width == 0 || height == 0) throw new IOException("Layer-shell configured an empty surface.");
                    if (s.Width != width || s.Height != height)
                    {
                        foreach (var b in s.Buffers) b.Dispose();
                        s.Buffers.Clear();
                        s.Width = checked((int)width); s.Height = checked((int)height);
                        s.Viewport?.SetDestination(s.Width, s.Height);
                        var region = compositor.CreateRegion(null, null);
                        region.Add(0, 0, s.Width, s.Height);
                        s.Proxy.SetInputRegion(region); s.Proxy.SetOpaqueRegion(region); region.Destroy();
                        s.Pending = null; // A frame at the new size follows via Present.
                    }
                    s.Proxy.Commit();
                },
                OnClosed = _ => stopping = true
            }, null);
        s.Proxy.SetBufferScale(1);
        s.Layer.SetSize(0, 0);
        s.Layer.SetAnchor(ZwlrLayerSurfaceV1.AnchorEnum.Top | ZwlrLayerSurfaceV1.AnchorEnum.Bottom | ZwlrLayerSurfaceV1.AnchorEnum.Left | ZwlrLayerSurfaceV1.AnchorEnum.Right);
        s.Layer.SetExclusiveZone(-1);
        s.Layer.SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivityEnum.Exclusive);
        s.Proxy.Commit();
    }

    private static OverlayRect Intersect(OverlayRect a, OverlayRect b)
    {
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }

    private static OverlayRect? Merge(OverlayRect? a, OverlayRect b) => a.HasValue ? OverlayRect.Union(a.Value, b) : b;
    private static int Pixels(int logical, double scale) => Math.Max(1, checked((int)Math.Round(logical * scale, MidpointRounding.AwayFromZero)));

    public (int Width, int Height) PixelSize(int output)
    {
        var o = Volatile.Read(ref layout).Outputs[output];
        return (Pixels(o.Logical.Width, o.Scale), Pixels(o.Logical.Height, o.Scale));
    }

    public void Present(int output, ReadOnlySpan<byte> bgra, int stride, OverlayRect? dirty)
    {
        var current = Volatile.Read(ref layout);
        if ((uint)output >= (uint)current.Outputs.Count) throw new ArgumentOutOfRangeException(nameof(output));
        var info = current.Outputs[output];
        var (width, height) = PixelSize(output);
        if (stride < checked(width * 4) || bgra.Length < checked((height - 1) * stride + width * 4)) return; // Frame of a stale layout: LayoutChanged is on its way.
        var full = new OverlayRect(0, 0, width, height);
        var rect = Intersect(dirty ?? full, full);
        if (rect.Width == 0 || rect.Height == 0) return;
        var copy = new byte[checked(rect.Width * rect.Height * 4)];
        for (int row = 0; row < rect.Height; row++) bgra.Slice((rect.Y + row) * stride + rect.X * 4, rect.Width * 4).CopyTo(copy.AsSpan(row * rect.Width * 4));
        Enqueue(() =>
        {
            if (output >= layout.Outputs.Count || layout.Outputs[output] != info) return; // Layout changed since the frame was rendered.
            var s = surfaces[output];
            if (s.Frame == null)
            {
                if (rect != full) return; // Retained frame was dropped with the layout; wait for a full frame.
                s.PixelWidth = width; s.PixelHeight = height; s.FrameStride = width * 4;
                s.Frame = new byte[checked(s.FrameStride * height)];
                rect = full;
            }
            for (int row = 0; row < rect.Height; row++)
                copy.AsSpan(row * rect.Width * 4, rect.Width * 4).CopyTo(s.Frame.AsSpan((rect.Y + row) * s.FrameStride + rect.X * 4));
            s.Pending = Merge(s.Pending, rect);
            foreach (var b in s.Buffers) b.Stale = Merge(b.Stale, rect);
            Submit(s);
        });
    }

    private Buffer CreateBuffer(Surface s)
    {
        var b = new Buffer { Width = s.PixelWidth, Height = s.PixelHeight, Stale = new(0, 0, s.PixelWidth, s.PixelHeight) };
        b.Stride = checked(b.Width * 4); b.Size = checked(b.Stride * b.Height);
        int fd = Native.memfd_create("sharex-overlay", 1);
        if (fd < 0) throw new IOException("memfd_create failed for overlay buffer.");
        try
        {
            if (Native.ftruncate(fd, b.Size) != 0) throw new IOException("ftruncate failed for overlay buffer.");
            b.Map = Native.mmap(0, (nuint)b.Size, 3, 1, fd, 0);
            if (b.Map == -1) throw new IOException("mmap failed for overlay buffer.");
            var pool = shm.CreatePool(fd, b.Size, null, null);
            b.Proxy = pool.CreateBuffer(0, b.Width, b.Height, b.Stride, WlShm.FormatEnum.Argb8888,
                new WlBuffer.Listener.Relay { OnRelease = _ => { b.Busy = false; if (s.Buffers.Contains(b)) Submit(s); } }, null);
            pool.Destroy();
            s.Buffers.Add(b);
            return b;
        }
        catch { b.Dispose(); throw; }
        finally { Native.close(fd); }
    }

    private unsafe void Submit(Surface s)
    {
        if (s.Frame == null || s.Pending == null || s.Width == 0) return;
        var b = s.Buffers.FirstOrDefault(b => !b.Busy);
        if (b == null)
        {
            if (s.Buffers.Count >= 3) return; // Release will submit the accumulated changes.
            b = CreateBuffer(s);
        }
        if (b.Stale is { } stale)
        {
            for (int y = stale.Y; y < stale.Bottom; y++)
                s.Frame.AsSpan(y * s.FrameStride + stale.X * 4, stale.Width * 4).CopyTo(new Span<byte>((void*)(b.Map + y * b.Stride + stale.X * 4), stale.Width * 4));
            b.Stale = null;
        }
        var damage = s.Pending.Value;
        s.Pending = null; b.Busy = true;
        s.Proxy.Attach(b.Proxy, 0, 0);
        s.Proxy.DamageBuffer(damage.X, damage.Y, damage.Width, damage.Height);
        s.Proxy.Commit();
    }
}
