// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using ShareX.NativeOverlay;
using ShareX.OverlayRender;

namespace ShareX.OverlayHost;

/// <summary>Single-consumer logic; call Handle and Publish only from the host queue thread.</summary>
public sealed class OverlayController
{
    private enum Interaction { None, Creating, PendingHover, Moving, Resizing }
    private readonly CaptureOptions options;
    private readonly IOverlayClock clock;
    private readonly TimeSpan opened;
    private readonly IWindowRectProvider? windows;
    private readonly IOverlayExtension extension;
    private Interaction interaction;
    private LogicalPoint press, lastCreation;
    private LogicalRect start;
    private (int X, int Y) resize;
    private bool wasControl, movingCreation, suppressRight, extensionPointer, annotationMode;
    private WindowRect? candidate, selected;
    private OverlayOptions visuals;
    private IReadOnlyList<OverlayOutput> outputs;
    private readonly LogicalRect bounds;
    public LogicalPoint Pointer { get; private set; }
    public LogicalRect Selection { get; private set; }
    public LogicalRect Hover { get; private set; }
    public CaptureResult? Result { get; private set; }
    public string? CancellationReason { get; private set; }
    public bool Finished => Result != null || CancellationReason != null;
    public bool Dirty { get; private set; } = true;
    public event Action<string>? Cancelled;
    public event Action<CaptureResult>? Captured;
    public OverlayCursor Cursor => interaction is Interaction.Moving or Interaction.Resizing ? OverlayCursor.Grabbing :
        extension.HandlesPointer(Pointer) ? OverlayCursor.Pointer : HandleCursor(HitHandle(Pointer));
    public OverlayScene Scene => new()
    {
        Selection = Selection, Hover = Hover, Pointer = Pointer,
        ShowHandles = Valid && interaction == Interaction.None && !annotationMode,
        Options = visuals, ExternalOverlay = extension.Bitmap
    };
    private bool Valid => Selection.Width >= CaptureOptions.MinimumSize && Selection.Height >= CaptureOptions.MinimumSize;

    public OverlayController(IReadOnlyList<OverlayOutput> outputs, IOverlayClock clock, CaptureOptions? options = null,
        IWindowRectProvider? windows = null, IOverlayExtension? extension = null, LogicalPoint initialPointer = default)
    {
        if (outputs.Count == 0) throw new ArgumentException("At least one output is required.", nameof(outputs));
        this.outputs = outputs.ToArray(); this.clock = clock; this.options = options ?? new();
        this.windows = windows; this.extension = extension ?? new NullOverlayExtension();
        visuals = this.options.Visuals; opened = clock.Now; Pointer = initialPointer;
        bounds = this.options.ActiveMonitorMode ? Rect(outputs.FirstOrDefault(o => Contains(Rect(o), Pointer)) ?? outputs[0]) :
            new(outputs.Min(o => o.Logical.X), outputs.Min(o => o.Logical.Y),
                outputs.Max(o => o.Logical.Right) - outputs.Min(o => o.Logical.X), outputs.Max(o => o.Logical.Bottom) - outputs.Min(o => o.Logical.Y));
        UpdateHover();
    }

    public void Publish(Action<OverlayOutput, OverlayScene> callback)
    {
        var scene = Scene;
        foreach (var output in outputs) callback(output, scene);
        Dirty = false;
    }
    public void Invalidate() => Dirty = true;
    public void Cancel(string reason)
    {
        if (Finished) return;
        CancellationReason = reason; Cancelled?.Invoke(reason);
    }
    public void Handle(OverlayEvent input)
    {
        if (Finished) return;
        Dirty = true;
        switch (input)
        {
            case OverlayClosed: Cancel("Overlay session closed."); break;
            case PointerMoved m:
                Pointer = new(m.X, m.Y);
                if (extensionPointer || annotationMode || (interaction == Interaction.None && extension.HandlesPointer(Pointer))) extension.OnPointer(input);
                else Move(m.Modifiers);
                break;
            case PointerButton b:
                Pointer = new(b.X, b.Y);
                if (clock.Now - opened < options.InputDelay) break;
                if (extensionPointer || annotationMode || (interaction == Interaction.None && extension.HandlesPointer(Pointer)))
                {
                    extension.OnPointer(input); extensionPointer = b.Pressed; break;
                }
                Button(b); break;
            case PointerWheel w:
                Pointer = new(w.X, w.Y);
                if (annotationMode || extension.HandlesPointer(Pointer)) extension.OnPointer(w);
                else visuals = visuals with { ShowMagnifier = true, MagnifierPixelCount = Math.Clamp(visuals.MagnifierPixelCount + (w.DeltaY > 0 ? -2 : 2), 3, 35) | 1 };
                break;
            case KeyEvent k: Key(k); break;
            case PointerLeft: extension.OnPointer(input); break;
        }
    }
    private void Button(PointerButton b)
    {
        if (b.Button == 3)
        {
            if (interaction is Interaction.Creating or Interaction.PendingHover)
            { Clear(); suppressRight = b.Pressed; }
            else if (!b.Pressed)
            { if (suppressRight) suppressRight = false; else RunAction(options.RightClick); }
            return;
        }
        if (b.Button != 1)
        {
            if (b.Pressed) RunAction(b.Button switch { 2 => options.MiddleClick, 4 => options.X1Click, 5 => options.X2Click, _ => RegionCaptureAction.None });
            return;
        }
        if (b.Pressed)
        {
            if (b.ClickCount >= 2 && Valid && Contains(Selection, Pointer)) { Complete(Selection, selected); return; }
            press = Clamp(Pointer); start = Selection; resize = HitHandle(Pointer);
            if (resize != default) interaction = Interaction.Resizing;
            else if (Valid && Contains(Selection, Pointer)) interaction = Interaction.Moving;
            else if (!Hover.IsEmpty) { Selection = Hover; selected = candidate; interaction = Interaction.PendingHover; }
            else { Selection = default; selected = null; interaction = Interaction.Creating; }
            lastCreation = press; movingCreation = false; wasControl = b.Modifiers.HasFlag(OverlayModifiers.Control);
        }
        else if (interaction != Interaction.None)
        {
            Move(b.Modifiers); interaction = Interaction.None; movingCreation = wasControl = false;
            if (!Valid) { Selection = Hover; selected = candidate; if (!Valid) Clear(); }
            if (Valid && options.QuickCapture) Complete(Selection, selected);
        }
    }
    private void Move(OverlayModifiers modifiers)
    {
        LogicalPoint p = Clamp(Pointer);
        switch (interaction)
        {
            case Interaction.None: UpdateHover(); break;
            case Interaction.PendingHover:
                if (Math.Sqrt(Math.Pow(p.X - press.X, 2) + Math.Pow(p.Y - press.Y, 2)) < 4) break;
                interaction = Interaction.Creating; selected = null; Selection = default;
                goto case Interaction.Creating;
            case Interaction.Creating:
                bool control = modifiers.HasFlag(OverlayModifiers.Control);
                if (!control) movingCreation = false;
                if (control && !Selection.IsEmpty && (movingCreation || !wasControl))
                {
                    var before = Selection;
                    Selection = Moved(Selection, p.X - lastCreation.X, p.Y - lastCreation.Y);
                    press = Clamp(new(press.X + Selection.X - before.X, press.Y + Selection.Y - before.Y));
                    movingCreation = true;
                }
                else
                {
                    double dx = p.X - press.X, dy = p.Y - press.Y;
                    if (modifiers.HasFlag(OverlayModifiers.Shift))
                    {
                        double side = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Min(dx < 0 ? press.X - bounds.X : bounds.Right - press.X, dy < 0 ? press.Y - bounds.Y : bounds.Bottom - press.Y));
                        p = new(press.X + (dx < 0 ? -side : side), press.Y + (dy < 0 ? -side : side));
                    }
                    Selection = new(Math.Min(press.X, p.X), Math.Min(press.Y, p.Y), Math.Abs(p.X - press.X), Math.Abs(p.Y - press.Y));
                }
                lastCreation = Clamp(Pointer); wasControl = control; break;
            case Interaction.Moving: Selection = Moved(start, p.X - press.X, p.Y - press.Y); selected = null; break;
            case Interaction.Resizing:
                double x = Selection.X, y = Selection.Y, width = Selection.Width, height = Selection.Height;
                double rx = p.X - press.X, ry = p.Y - press.Y;
                if (resize.X > 0) width = Math.Max(1, width + rx);
                if (resize.X < 0) { double d = Math.Min(width - 1, rx); x += d; width -= d; }
                if (resize.Y > 0) height = Math.Max(1, height + ry);
                if (resize.Y < 0) { double d = Math.Min(height - 1, ry); y += d; height -= d; }
                Selection = Intersect(new(x, y, width, height), bounds); press = p; selected = null; break;
        }
    }
    private void Key(KeyEvent k)
    {
        if (k.EvdevCode == 1) { Cancel("Escape"); return; }
        if (clock.Now - opened < options.InputDelay) return;
        if (k.EvdevCode == 15 && k.Pressed && k.Modifiers == OverlayModifiers.None) { annotationMode = false; extension.SetAnnotationMode(false); return; }
        if (k.Pressed && k.EvdevCode is 28 or 96 && Valid) { Complete(Selection, selected); return; }
        if (annotationMode) { extension.OnKey(k); return; }
        if (k.EvdevCode is 29 or 97 && !k.Pressed) movingCreation = wasControl = false;
        if (k.EvdevCode is 42 or 54 && interaction == Interaction.Creating) { Move(k.Pressed ? k.Modifiers | OverlayModifiers.Shift : k.Modifiers & ~OverlayModifiers.Shift); return; }
        if (!k.Pressed) return;
        switch (k.EvdevCode)
        {
            case 28: case 96: if (Valid) Complete(Selection, selected); break;
            case 57: Complete(bounds); break;
            case 41: CompleteMonitor(); break;
            case 111: if (Valid) Clear(); break;
            case 103: case 105: case 106: case 108:
                int d = k.Modifiers.HasFlag(OverlayModifiers.Shift) ? 10 : 1;
                Pointer = Clamp(new(Pointer.X + (k.EvdevCode == 105 ? -d : k.EvdevCode == 106 ? d : 0), Pointer.Y + (k.EvdevCode == 103 ? -d : k.EvdevCode == 108 ? d : 0)));
                Move(k.Modifiers); break;
            default: extension.OnKey(k); break;
        }
    }
    public void RunAction(RegionCaptureAction action)
    {
        switch (action)
        {
            case RegionCaptureAction.CancelCapture: Cancel("Mouse action"); break;
            case RegionCaptureAction.RemoveShapeCancelCapture:
                if (Valid && Contains(Selection, Pointer)) Clear(); else Cancel("Mouse action"); break;
            case RegionCaptureAction.RemoveShape: if (Valid && Contains(Selection, Pointer)) Clear(); break;
            case RegionCaptureAction.SwapToolType: annotationMode = !annotationMode; extension.SetAnnotationMode(annotationMode); break;
            case RegionCaptureAction.CaptureFullscreen: Complete(bounds); break;
            case RegionCaptureAction.CaptureActiveMonitor: CompleteMonitor(); break;
            case RegionCaptureAction.CaptureLastRegion: Complete(Intersect(options.LastRegion, bounds)); break;
        }
    }
    private void CompleteMonitor()
    {
        var output = outputs.FirstOrDefault(o => Contains(Rect(o), Pointer));
        if (output != null) Complete(Intersect(Rect(output), bounds));
    }
    private void Complete(LogicalRect rect, WindowRect? window = null)
    {
        if (rect.IsEmpty || Finished) return;
        Result = new(rect, window, extension.CommittedAnnotations.ToArray()); Captured?.Invoke(Result);
    }
    private void Clear() { interaction = Interaction.None; Selection = default; selected = null; movingCreation = wasControl = false; UpdateHover(); }
    private void UpdateHover()
    {
        candidate = !Valid && options.DetectWindows ? windows?.GetRects(options.DetectControls).FirstOrDefault(w => (!w.IsControl || options.DetectControls) && Contains(w.Bounds, Pointer)) : null;
        Hover = candidate == null ? default : Intersect(candidate.Bounds, bounds);
    }
    private (int X, int Y) HitHandle(LogicalPoint p)
    {
        if (!Valid || options.QuickCapture || interaction != Interaction.None) return default;
        double radius = 7.5 / (outputs.FirstOrDefault(o => Contains(Rect(o), p))?.Scale ?? 1);
        foreach (var h in new[] { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) })
        {
            double x = h.Item1 < 0 ? Selection.X : h.Item1 > 0 ? Selection.Right : Selection.X + Selection.Width / 2;
            double y = h.Item2 < 0 ? Selection.Y : h.Item2 > 0 ? Selection.Bottom : Selection.Y + Selection.Height / 2;
            if (Math.Abs(p.X - x) <= radius && Math.Abs(p.Y - y) <= radius) return h;
        }
        return default;
    }
    private OverlayCursor HandleCursor((int X, int Y) h) => h == default ? Valid && Contains(Selection, Pointer) ? OverlayCursor.Grab : OverlayCursor.Crosshair : h.X == 0 ? OverlayCursor.ResizeNS : h.Y == 0 ? OverlayCursor.ResizeEW : h.X == h.Y ? OverlayCursor.ResizeNWSE : OverlayCursor.ResizeNESW;
    private LogicalPoint Clamp(LogicalPoint p) => new(Math.Clamp(p.X, bounds.X, bounds.Right), Math.Clamp(p.Y, bounds.Y, bounds.Bottom));
    private LogicalRect Moved(LogicalRect r, double dx, double dy) => new(Math.Clamp(r.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - r.Width)), Math.Clamp(r.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - r.Height)), r.Width, r.Height);
    internal static LogicalRect Rect(OverlayOutput o) => new(o.Logical.X, o.Logical.Y, o.Logical.Width, o.Logical.Height);
    internal static bool Contains(LogicalRect r, LogicalPoint p) => !r.IsEmpty && p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom;
    internal static LogicalRect Intersect(LogicalRect a, LogicalRect b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y)));
}
