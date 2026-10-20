// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
namespace ShareX.OverlayHost;

public sealed class FramePacer
{
    private readonly IOverlayClock clock;
    private TimeSpan next;
    public TimeSpan Interval { get; }
    public FramePacer(IOverlayClock clock, TimeSpan? interval = null)
    { this.clock = clock; Interval = interval ?? TimeSpan.FromMilliseconds(8); }
    public bool TryBegin(bool dirty)
    {
        if (!dirty || clock.Now < next) return false;
        next = clock.Now + Interval; return true;
    }
}

/// <summary>Independent observer. Atomic timestamps only; never calls controller/render code.
/// Event timeout measures from the first outstanding event, so continuous input cannot postpone it.</summary>
public sealed class OverlayWatchdog
{
    private readonly IOverlayClock clock;
    private readonly long opened;
    private long lastInput, lastFrame = -1, outstanding = -1;
    private long generation, frameGeneration;
    private readonly object gate = new();
    public TimeSpan FrameTimeout { get; }
    public OverlayWatchdog(IOverlayClock clock, TimeSpan? frameTimeout = null)
    { this.clock = clock; opened = lastInput = clock.Now.Ticks; FrameTimeout = frameTimeout ?? TimeSpan.FromSeconds(5); }
    public void Input()
    {
        lock (gate)
        {
            long now = clock.Now.Ticks;
            Interlocked.Exchange(ref lastInput, now);
            Interlocked.CompareExchange(ref outstanding, now, -1);
            generation++;
        }
    }
    public void BeginFrame() { lock (gate) frameGeneration = generation; }
    public void FrameCompleted(bool eventsRemaining = false)
    {
        lock (gate)
        {
            Interlocked.Exchange(ref lastFrame, clock.Now.Ticks);
            if (!eventsRemaining && generation == frameGeneration) Interlocked.Exchange(ref outstanding, -1);
        }
    }
    public string? Check()
    {
        long now = clock.Now.Ticks, frame = Interlocked.Read(ref lastFrame), pending = Interlocked.Read(ref outstanding);
        if (frame < 0 && now - opened >= TimeSpan.FromSeconds(30).Ticks) return "No first frame within 30 seconds.";
        if (now - Interlocked.Read(ref lastInput) >= TimeSpan.FromSeconds(45).Ticks) return OverlayCancelReasons.Idle;
        if (pending >= 0 && now - Math.Max(frame, pending) >= FrameTimeout.Ticks) return "Frame completion watchdog expired.";
        return null;
    }
}

/// <summary>Cancel reasons that mean the user is done (or gone), as opposed to a failure that should fall back to the window overlay.</summary>
public static class OverlayCancelReasons
{
    public const string Escape = "Escape";
    public const string MouseAction = "Mouse action";
    public const string Idle = "No input for 45 seconds.";
    public static bool IsUserCancel(string? reason) => reason is Escape or MouseAction or Idle;
}
