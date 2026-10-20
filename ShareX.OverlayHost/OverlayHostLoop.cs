// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using SkiaSharp;
using ShareX.NativeOverlay;
using ShareX.OverlayRender;

namespace ShareX.OverlayHost;

/// <summary>Owned native-resolution pixel tile. Dispose Pixels after composing/saving the capture.</summary>
public sealed record OutputCapture(string Output, LogicalRect LogicalBounds, double Scale, SKRectI PixelBounds, SKBitmap Pixels);
public sealed record OverlayHostResult(CaptureResult? Capture, string? CancellationReason, IReadOnlyList<OutputCapture>? Outputs = null);

/// <summary>Owns the session, borrows renderers. One thread consumes all controller changes.
/// Input ingestion and watchdog do not depend on rendering. Await RunAsync before disposing renderers.
/// On watchdog cancellation a stuck render worker may remain; keep borrowed renderers alive until WorkerCompletion.
/// The enclosing application must still enforce its process hard timeout.</summary>
public sealed class OverlayHostLoop
{
    private readonly IOverlaySession session;
    private readonly IReadOnlyList<MonitorRenderer> renderers;
    private readonly OverlayOutput[] layout;
    private readonly OverlayEventQueue queue = new();
    private readonly IOverlayClock clock;
    private readonly FramePacer pacer;
    private readonly OverlayWatchdog watchdog;
    private readonly TaskCompletionSource<OverlayHostResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource workerCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int started;
    public OverlayController Controller { get; }
    public Task WorkerCompletion => workerCompletion.Task;
    public Task<OverlayHostResult> Completion => completion.Task;
    public event Action<string>? Cancelled;

    public OverlayHostLoop(IOverlaySession session, IReadOnlyList<MonitorRenderer> renderers, IOverlayClock? clock = null,
        CaptureOptions? options = null, IWindowRectProvider? windows = null, IOverlayExtension? extension = null,
        LogicalPoint initialPointer = default, TimeSpan? frameTimeout = null)
    {
        this.session = session; this.renderers = renderers; this.clock = clock ?? new OverlayClock();
        layout = session.Outputs.ToArray();
        if (layout.Length != renderers.Count || layout.Where((o, i) => o.Name != renderers[i].MonitorId || OverlayController.Rect(o) != renderers[i].LogicalBounds || o.Scale != renderers[i].Scale).Any())
            throw new ArgumentException("Renderers must match the frozen output layout.", nameof(renderers));
        Controller = new(layout, this.clock, options, windows, extension, initialPointer);
        pacer = new(this.clock); watchdog = new(this.clock, frameTimeout);
        Controller.Cancelled += reason => Finish(new(null, reason));
        Controller.Captured += capture =>
        {
            var tiles = new List<OutputCapture>();
            try
            {
                foreach (var renderer in renderers)
                {
                    var rect = OverlayController.Intersect(capture.Bounds, renderer.LogicalBounds);
                    if (rect.IsEmpty) continue;
                    var origin = renderer.LogicalBounds;
                    var pixels = new SKRectI((int)Math.Floor((rect.X - origin.X) * renderer.Scale),
                        (int)Math.Floor((rect.Y - origin.Y) * renderer.Scale),
                        Math.Min(renderer.Framebuffer.Width, (int)Math.Ceiling((rect.Right - origin.X) * renderer.Scale)),
                        Math.Min(renderer.Framebuffer.Height, (int)Math.Ceiling((rect.Bottom - origin.Y) * renderer.Scale)));
                    tiles.Add(new(renderer.MonitorId, rect, renderer.Scale, pixels, renderer.CapturePixels(pixels)));
                }
                if (!Finish(new(capture, null, tiles))) foreach (var tile in tiles) tile.Pixels.Dispose();
            }
            catch { foreach (var tile in tiles) tile.Pixels.Dispose(); throw; }
        };
    }
    private bool Finish(OverlayHostResult result)
    {
        if (!completion.TrySetResult(result)) return false;
        ThreadPool.QueueUserWorkItem(_ => session.Dispose());
        if (result.CancellationReason is { } reason) Cancelled?.Invoke(reason);
        return true;
    }
    public Task<OverlayHostResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Host already started.");
        _ = Ingest();
        new Thread(() =>
        {
            try
            {
                while (!completion.Task.IsCompleted)
                {
                    if (cancellationToken.IsCancellationRequested) { Finish(new(null, "Host cancellation requested.")); break; }
                    Pump(); Thread.Sleep(1);
                }
            }
            catch (Exception e) { Finish(new(null, "Overlay host failed: " + e.Message)); }
            finally { workerCompletion.TrySetResult(); }
        }) { IsBackground = true, Name = "ShareX overlay controller" }.Start();
        _ = Observe();
        return completion.Task;
    }
    /// <summary>Thread-safe input/extension wakeup. Mutations are applied by the queue consumer.</summary>
    public void Post(OverlayEvent input)
    {
        watchdog.Input();
        if (input is KeyEvent { EvdevCode: 1, Pressed: true }) Finish(new(null, "Escape"));
        else queue.TryWrite(input);
    }
    private async Task Ingest()
    {
        try
        {
            while (!completion.Task.IsCompleted && await session.Events.WaitToReadAsync())
            {
                while (session.Events.TryRead(out var input))
                {
                    Post(input);
                    if (completion.Task.IsCompleted) return;
                }
            }
            if (!completion.Task.IsCompleted) queue.TryWrite(new OverlayClosed());
        }
        catch (Exception e) { Finish(new(null, "Input failed: " + e.Message)); }
    }
    private async Task Observe()
    {
        while (!completion.Task.IsCompleted)
        {
            if (session.CancelRequested) Finish(new(null, "Escape"));
            else if (watchdog.Check() is { } reason) Finish(new(null, reason));
            await Task.Delay(10);
        }
    }
    /// <summary>Deterministic single-thread step for scripted/fake-clock hosts. Do not call alongside RunAsync.</summary>
    public void Pump()
    {
        if (completion.Task.IsCompleted) return;
        if (session.CancelRequested) { Controller.Cancel("Escape"); return; }
        if (watchdog.Check() is { } reason) { Controller.Cancel(reason); return; }
        // Without RunAsync, the fake session is the queue source.
        if (Volatile.Read(ref started) == 0)
            while (session.Events.TryRead(out var input)) { watchdog.Input(); queue.TryWrite(input); }
        if (!pacer.TryBegin(true)) return;
        watchdog.BeginFrame();
        // Discrete events retain order; consecutive moves have already collapsed to one.
        int budget = 4096;
        while (budget-- > 0 && queue.TryRead(out var input))
        {
            if (input is LayoutChanged)
            {
                if (!session.Outputs.SequenceEqual(layout) || layout.Where((o, i) => session.PixelSize(i) != (renderers[i].Framebuffer.Width, renderers[i].Framebuffer.Height)).Any())
                { Controller.Cancel("Output layout or scale changed; frozen screenshots no longer match."); return; }
                full = true; Controller.Invalidate();
            }
            else Controller.Handle(input);
            if (Controller.Finished) return;
        }
        if (!Controller.Dirty) return;
        if (session.CancelRequested) { Controller.Cancel("Escape"); return; }
        session.SetCursor(Controller.Cursor);
        Controller.Publish((output, scene) => Render(Array.IndexOf(layout, output), scene));
        full = false; watchdog.FrameCompleted(queue.TryPeek(out _));
    }
    private bool full = true;
    private unsafe void Render(int index, OverlayScene scene)
    {
        if (completion.Task.IsCompleted || session.CancelRequested) return;
        var renderer = renderers[index];
        var damage = renderer.Update(scene);
        var bitmap = renderer.Framebuffer;
        var bytes = new ReadOnlySpan<byte>((void*)bitmap.GetPixels(), bitmap.RowBytes * bitmap.Height);
        if (full) session.Present(index, bytes, bitmap.RowBytes, null);
        else foreach (var rect in damage.Regions)
            session.Present(index, bytes, bitmap.RowBytes, new(rect.Left, rect.Top, rect.Width, rect.Height));
    }
}
