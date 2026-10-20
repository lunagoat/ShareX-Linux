// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using ShareX.NativeOverlay;
using ShareX.OverlayRender;

namespace ShareX.OverlayHost;

/// <summary>Post after publishing a new extension bitmap snapshot/revision.</summary>
public sealed record ExtensionChanged : OverlayEvent;

public enum RegionCaptureAction { None, CancelCapture, RemoveShapeCancelCapture, RemoveShape, SwapToolType, CaptureFullscreen, CaptureActiveMonitor, CaptureLastRegion }

/// <summary>UI-independent snapshot. Values and defaults match RegionCaptureOptions.</summary>
public sealed record CaptureOptions
{
    public const int MinimumSize = 5;
    public bool QuickCapture { get; init; } = true;
    public bool DetectWindows { get; init; } = true;
    public bool DetectControls { get; init; } = true;
    public bool ActiveMonitorMode { get; init; }
    public TimeSpan InputDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public RegionCaptureAction RightClick { get; init; } = RegionCaptureAction.RemoveShapeCancelCapture;
    public RegionCaptureAction MiddleClick { get; init; } = RegionCaptureAction.SwapToolType;
    public RegionCaptureAction X1Click { get; init; } = RegionCaptureAction.CaptureFullscreen;
    public RegionCaptureAction X2Click { get; init; } = RegionCaptureAction.CaptureActiveMonitor;
    public LogicalRect LastRegion { get; init; }
    public OverlayOptions Visuals { get; init; } = new();
}

public sealed record WindowRect(LogicalRect Bounds, bool IsControl = false, object? Metadata = null);
/// <summary>Returns candidates in front-to-back order, controls before their parent window.</summary>
public interface IWindowRectProvider
{
    IEnumerable<WindowRect> GetRects(bool includeControls);
}

/// <summary>Owned by the controller thread. Bitmap lifetime follows OverlayBitmap's contract.
/// Extension implementations own tool editing, undo/delete, toolbar state and annotation composition.</summary>
public interface IOverlayExtension
{
    bool AnnotationMode => false;
    void Prepare(LogicalRect selection, LogicalPoint pointer) { }
    bool HandlesPointer(LogicalPoint point);
    void OnPointer(OverlayEvent input);
    void OnKey(KeyEvent input);
    OverlayBitmap? Bitmap { get; }
    IReadOnlyList<OverlayShape> CommittedAnnotations { get; }
    void SetAnnotationMode(bool active);
}

public sealed class NullOverlayExtension : IOverlayExtension
{
    public bool HandlesPointer(LogicalPoint point) => false;
    public void OnPointer(OverlayEvent input) { }
    public void OnKey(KeyEvent input) { }
    public OverlayBitmap? Bitmap => null;
    public IReadOnlyList<OverlayShape> CommittedAnnotations => Array.Empty<OverlayShape>();
    public void SetAnnotationMode(bool active) { }
}

/// <summary>Global logical crop plus per-output pixel crops supplied by the host; caller owns final composition.</summary>
public sealed record CaptureResult(LogicalRect Bounds, WindowRect? Window, IReadOnlyList<OverlayShape> Annotations);
public interface IOverlayClock { TimeSpan Now { get; } }
public sealed class OverlayClock : IOverlayClock
{
    private readonly System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
    public TimeSpan Now => timer.Elapsed;
}
