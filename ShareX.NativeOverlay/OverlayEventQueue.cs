// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Threading.Channels;

namespace ShareX.NativeOverlay;

/// <summary>Coalesces consecutive motion without losing button/key/wheel ordering.
/// Motion uses one slot between discrete events, even while the consumer is rendering.</summary>
public sealed class OverlayEventQueue : ChannelReader<OverlayEvent>
{
    private readonly object gate = new();
    private readonly LinkedList<OverlayEvent> pending = new();
    private readonly Channel<bool> signal = Channel.CreateBounded<bool>(1);
    private bool completed;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override Task Completion => completion.Task;
    public bool TryWrite(OverlayEvent input)
    {
        lock (gate)
        {
            if (completed) return false;
            if (input is PointerMoved && pending.Last?.Value is PointerMoved) pending.Last.Value = input;
            else pending.AddLast(input);
            signal.Writer.TryWrite(true);
            return true;
        }
    }
    public void TryComplete()
    {
        lock (gate) { completed = true; signal.Writer.TryComplete(); if (pending.Count == 0) completion.TrySetResult(); }
    }
    public override bool TryRead(out OverlayEvent item)
    {
        lock (gate)
        {
            if (pending.First is not { } first) { item = null!; return false; }
            item = first.Value; pending.RemoveFirst();
            if (completed && pending.Count == 0) completion.TrySetResult();
            return true;
        }
    }
    public override bool TryPeek(out OverlayEvent item)
    {
        lock (gate)
        {
            if (pending.First is { } first) { item = first.Value; return true; }
            item = null!; return false;
        }
    }
    public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lock (gate) { if (pending.Count > 0) return true; if (completed) return false; }
            if (!await signal.Reader.WaitToReadAsync(cancellationToken)) return false;
            signal.Reader.TryRead(out _);
        }
    }
}
