// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NWayland;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.Wlr.WlrLayerShellUnstableV1;
using NWayland.Protocols.XdgOutputUnstableV1;
using NWayland.Protocols.Viewporter;
using NWayland.Protocols.CursorShapeV1;
using NWayland.Protocols.FractionalScaleV1;

namespace ShareX.NativeOverlay.Wayland;

/// <summary>All protocol objects and mutable rendering/input state belong to the dispatch thread.</summary>
public sealed partial class WaylandOverlaySession : IOverlaySession
{
    private sealed record Layout(IReadOnlyList<OverlayOutput> Outputs, OverlayRect Desktop);
    private Layout layout = new(Array.Empty<OverlayOutput>(), default);
    public IReadOnlyList<OverlayOutput> Outputs => Volatile.Read(ref layout).Outputs;
    public OverlayRect Desktop => Volatile.Read(ref layout).Desktop;
    private readonly OverlayEventQueue events = new();
    public ChannelReader<OverlayEvent> Events => events;
    private readonly ConcurrentQueue<Action> work = new();
    private readonly object lifetime = new();
    private readonly Thread thread;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int wake;
    private volatile bool stopping;
    private int cancelRequested;
    public bool CancelRequested => Volatile.Read(ref cancelRequested) != 0;
    private bool initialized;
    private WlDisplay display = null!;
    private WlRegistry registry = null!;
    private WlCompositor compositor = null!;
    private WlShm shm = null!;
    private WlSeat seat = null!;
    private ZwlrLayerShellV1 layerShell = null!;
    private ZxdgOutputManagerV1? xdgManager;
    private uint xdgVersion;
    private WpViewporter? viewporter;
    private WpFractionalScaleManagerV1? fractionalManager;
    private WpCursorShapeManagerV1? cursorManager;
    private readonly List<Output> outputs = new();
    private readonly List<Surface> surfaces = new();

    private sealed class Output
    {
        public uint Id;
        public WlOutput Proxy = null!;
        public ZxdgOutputV1? Xdg;
        public string Name = "";
        public int X, Y, Width, Height, Scale = 1, Transform;
        public int? LogicalX, LogicalY, LogicalWidth, LogicalHeight;
        public bool Done, XdgDone;
        public double? FractionalScale;
        public OverlayRect Rect => new(LogicalX ?? X, LogicalY ?? Y,
            LogicalWidth ?? ((Transform % 2 == 1 ? Height : Width) / Scale),
            LogicalHeight ?? ((Transform % 2 == 1 ? Width : Height) / Scale));
    }

    public WaylandOverlaySession() : this(false) { }

    internal WaylandOverlaySession(bool probe)
    {
        if (!OperatingSystem.IsLinux()) throw new NotSupportedException("Wayland overlays require Linux.");
        wake = Native.eventfd(0, 0x800 | 0x80000); // NONBLOCK | CLOEXEC
        if (wake < 0) throw new IOException("Could not create Wayland dispatch eventfd.");
        thread = new Thread(() => Run(probe)) { IsBackground = true, Name = "ShareX Wayland overlay" };
        thread.Start();
        try
        {
            if (!ready.Task.Wait(TimeSpan.FromSeconds(3))) throw new NotSupportedException("Wayland overlay initialization exceeded the 3 second deadline.");
            ready.Task.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Stop();
            if (e is AggregateException aggregate) e = aggregate.InnerException!;
            throw e is NotSupportedException ? e : new NotSupportedException("Could not initialize the Wayland overlay: " + e.Message, e);
        }
    }

    private void Run(bool probe)
    {
        var deadline = Stopwatch.StartNew();
        try
        {
            display = WlDisplay.Connect(null, new WlDisplayOptions());
            registry = display.GetRegistry(new WlRegistry.Listener.Relay { OnGlobal = Global, OnGlobalRemove = (_, id) =>
            {
                if (outputs.Any(o => o.Id == id)) stopping = true;
            } }, null);
            Sync(deadline);
            if (layerShell == null) throw new NotSupportedException("The Wayland compositor does not advertise zwlr_layer_shell_v1 (wlr layer-shell).");
            if (compositor == null || shm == null || seat == null || outputs.Count == 0)
                throw new NotSupportedException("Wayland overlay requires wl_compositor, wl_shm, wl_seat and at least one wl_output.");
            foreach (var output in outputs) AddXdg(output);
            Sync(deadline);
            while (outputs.Any(o => !o.Done || (o.Xdg != null && !o.XdgDone))) Pump(TimeoutLeft(deadline));
            PublishLayout();
            if (!probe)
            {
                if (viewporter == null && outputs.Any(o => o.FractionalScale.HasValue || o.Rect.Width != 0 && Math.Abs(Scale(o) - 1) > 1e-6))
                    throw new NotSupportedException("Scaled outputs require wp_viewporter.");
                foreach (var output in outputs) CreateSurface(output);
                Sync(deadline);
                while (surfaces.Any(s => s.Width == 0 || s.Height == 0)) Pump(TimeoutLeft(deadline));
            }
            initialized = true;
            ready.TrySetResult();
            while (!stopping)
            {
                while (!stopping && work.TryDequeue(out var action)) action();
                Repeat();
                Pump(RepeatTimeout());
            }
        }
        catch (Exception e) { ready.TrySetException(e); }
        finally
        {
            lock (lifetime) stopping = true;
            try
            {
                foreach (var s in surfaces) s.Destroy();
                DestroyInput();
                foreach (var o in outputs) { o.Xdg?.Destroy(); if (o.Proxy.IsReleaseAvailable) o.Proxy.Release(); }
                cursorManager?.Destroy(); fractionalManager?.Destroy(); viewporter?.Destroy(); xdgManager?.Destroy();
                layerShell?.Destroy();
                display?.Flush();
            }
            catch { /* A disconnected compositor may reject destruction requests. */ }
            finally
            {
                display?.Dispose();
                lock (lifetime) { Native.close(wake); wake = -1; }
                while (work.TryDequeue(out _)) { }
                events.TryWrite(new OverlayClosed());
                events.TryComplete();
            }
        }
    }

    private static int TimeoutLeft(Stopwatch timer)
    {
        int left = 2900 - (int)timer.ElapsedMilliseconds;
        if (left <= 0) throw new NotSupportedException("Wayland compositor did not finish overlay initialization within 3 seconds.");
        return left;
    }

    private void Sync(Stopwatch timer)
    {
        bool done = false;
        display.Sync(new WlCallback.Listener.Relay { OnDone = (_, _) => done = true }, null);
        while (!done && !stopping) Pump(TimeoutLeft(timer));
        if (stopping) throw new NotSupportedException("Wayland overlay initialization was cancelled.");
    }

    private void Pump(int timeout)
    {
        while (display.PrepareRead() != 0)
            if (display.DispatchPending() < 0) throw new IOException("Wayland display disconnected.");
        int flush = display.Flush();
        int error = Marshal.GetLastPInvokeError();
        if (flush < 0 && error != 11) { display.CancelRead(); throw new IOException("Wayland flush failed."); }
        var fds = new[] { new Native.PollFd { Fd = display.GetFd(), Events = (short)(1 | (flush < 0 ? 4 : 0)) }, new Native.PollFd { Fd = wake, Events = 1 } };
        int result = Native.poll(fds, 2, timeout);
        if (result > 0 && (fds[0].Revents & 1) != 0)
        {
            if (display.ReadEvents() < 0) throw new IOException("Wayland read failed.");
        }
        else display.CancelRead();
        if (result < 0 && Marshal.GetLastPInvokeError() != 4) throw new IOException("Wayland poll failed.");
        if ((fds[0].Revents & (8 | 16 | 32)) != 0) throw new IOException("Wayland connection closed.");
        if ((fds[1].Revents & 1) != 0) Native.read(wake, out _, 8);
        if (display.DispatchPending() < 0) throw new IOException("Wayland dispatch failed.");
    }

    private void Global(WlRegistry r, uint id, string iface, uint version)
    {
        switch (iface)
        {
            case "wl_compositor":
                if (version < 4) throw new NotSupportedException("wl_compositor v4 (damage_buffer) is required.");
                compositor = r.Bind<WlCompositor>(id, 4, null, null); break;
            case "wl_shm": shm = r.Bind<WlShm>(id, 1, null, null); break;
            case "zwlr_layer_shell_v1": layerShell = r.Bind<ZwlrLayerShellV1>(id, Math.Min(version, 4), null, null); break;
            case "zxdg_output_manager_v1":
                xdgVersion = Math.Min(version, 3); xdgManager = r.Bind<ZxdgOutputManagerV1>(id, xdgVersion, null, null); break;
            case "wp_fractional_scale_manager_v1": fractionalManager = r.Bind<WpFractionalScaleManagerV1>(id, 1, null, null); break;
            case "wp_viewporter": viewporter = r.Bind<WpViewporter>(id, 1, null, null); break;
            case "wp_cursor_shape_manager_v1": cursorManager = r.Bind<WpCursorShapeManagerV1>(id, 1, null, null); break;
            case "wl_seat":
                if (seat == null) seat = r.Bind<WlSeat>(id, Math.Min(version, 8), new WlSeat.Listener.Relay { OnCapabilities = SeatCapabilities }, null);
                break;
            case "wl_output":
                if (initialized) { stopping = true; break; } // A new desktop needs a new frame extent/session.
                if (version < 2) throw new NotSupportedException("wl_output v2 (done events) is required.");
                var o = new Output { Id = id, Name = "output-" + id };
                o.Proxy = r.Bind<WlOutput>(id, Math.Min(version, 4), new WlOutput.Listener.Relay
                {
                    OnGeometry = (_, x, y, _, _, _, _, _, transform) => { o.X = x; o.Y = y; o.Transform = (int)transform; },
                    OnMode = (_, flags, w, h, _) => { if ((flags & WlOutput.ModeEnum.Current) != 0) { o.Width = w; o.Height = h; } },
                    OnScale = (_, scale) => o.Scale = Math.Max(1, scale),
                    OnName = (_, name) => o.Name = name,
                    OnDone = _ => { o.Done = true; if (xdgVersion >= 3) o.XdgDone = true; if (initialized) PublishLayout(); }
                }, null);
                outputs.Add(o); break;
        }
    }

    private void AddXdg(Output o)
    {
        if (xdgManager == null) return;
        o.XdgDone = false;
        o.Xdg = xdgManager.GetXdgOutput(o.Proxy, new ZxdgOutputV1.Listener.Relay
        {
            OnLogicalPosition = (_, x, y) => { o.LogicalX = x; o.LogicalY = y; },
            OnLogicalSize = (_, w, h) => { o.LogicalWidth = w; o.LogicalHeight = h; },
            OnName = (_, name) => o.Name = name,
            OnDone = _ => { o.XdgDone = true; if (initialized) PublishLayout(); }
        }, null);
    }

    private static double Scale(Output o) =>
        o.FractionalScale ?? (o.LogicalWidth > 0 ? (double)(o.Transform % 2 == 1 ? o.Height : o.Width) / o.LogicalWidth.Value : o.Scale);

    private void PublishLayout()
    {
        var list = outputs.Select(o => new OverlayOutput(o.Name, o.Rect, Scale(o))).ToArray();
        if (list.Any(o => o.Logical.Width <= 0 || o.Logical.Height <= 0)) throw new NotSupportedException("Wayland output has no usable logical size.");
        var desktop = list.Select(o => o.Logical).Aggregate(OverlayRect.Union);
        var next = new Layout(Array.AsReadOnly(list), desktop);
        if (!layout.Outputs.SequenceEqual(list))
        {
            foreach (var s in surfaces)
            {
                foreach (var b in s.Buffers) b.Dispose();
                s.Buffers.Clear();
                s.Frame = null;
                s.Pending = null;
            }
            Volatile.Write(ref layout, next);
            if (initialized) events.TryWrite(new LayoutChanged());
        }
    }

    private void Enqueue(Action action)
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            work.Enqueue(action);
            ulong one = 1; Native.write(wake, ref one, 8);
        }
    }

    private void Stop()
    {
        lock (lifetime)
        {
            stopping = true;
            if (wake >= 0) { ulong one = 1; Native.write(wake, ref one, 8); }
        }
    }

    public void Dispose()
    {
        Stop();
        if (Thread.CurrentThread != thread) thread.Join();
    }
}
