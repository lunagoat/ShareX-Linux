// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using ShareX.NativeOverlay;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.OverlayHost;
using ShareX.OverlayRender;
using SkiaSharp;

namespace ShareX.OverlayAvaloniaLayer;

/// <summary>Single UI-thread adapter. The layer owns the complete editor document, including tools
/// that cannot be represented by OverlayShape; final export always uses RenderResult.</summary>
public sealed class AnnotationOverlayExtension : IOverlayExtension, IDisposable
{
    public AnnotationLayerHost Layer { get; }
    private readonly OverlayRect desktop;
    private readonly IReadOnlyList<OverlayOutput> outputs;
    private SKBitmap? retained;
    private long revision;
    private LogicalRect? lastSelection;
    public bool AnnotationMode => !Layer.IsRegionToolActive;
    public OverlayBitmap? Bitmap { get; private set; }
    public IReadOnlyList<Annotation> CommittedDocument => Layer.Workspace.WorkspaceAnnotations;
    public IReadOnlyList<OverlayShape> CommittedAnnotations => CommittedDocument
        .Where(a => a is RectangleAnnotation or EllipseAnnotation or LineAnnotation).Select(a =>
    {
        var b = a.GetBounds();
        return new OverlayShape(a.GetType().Name == "EllipseAnnotation" ? OverlayShapeKind.Ellipse :
            a.GetType().Name == "LineAnnotation" ? OverlayShapeKind.Line : OverlayShapeKind.Rectangle,
            new(b.Left + desktop.X, b.Top + desktop.Y, b.Width, b.Height),
            SKColor.TryParse(a.StrokeColor, out var stroke) ? stroke : SKColors.Red,
            SKColor.TryParse(a.FillColor, out var fill) ? fill : SKColors.Transparent, a.StrokeWidth);
    }).ToArray();

    public AnnotationOverlayExtension(OverlayRect desktop, IReadOnlyList<OverlayOutput> outputs, AnnotationLayerOptions options)
    {
        this.desktop = desktop; this.outputs = outputs;
        Layer = AnnotationLayerHost.Create(new(0, 0, desktop.Width, desktop.Height), options);
        Prepare(default, default);
    }
    private Point Local(double x, double y) => new(x - desktop.X, y - desktop.Y);
    public bool HandlesPointer(LogicalPoint point) => Layer.HitTest(Local(point.X, point.Y));
    public void SetAnnotationMode(bool active)
    {
        if (active) Layer.ActivateAnnotationTool(); else Layer.ActivateRegionTool();
    }
    public void OnPointer(OverlayEvent input)
    {
        switch (input)
        {
            case PointerMoved m: Layer.SendPointerMove(Local(m.X, m.Y), Modifiers(m.Modifiers)); break;
            case PointerButton b:
                Layer.SendPointer(b.Pressed ? PointerAction.Down : PointerAction.Up, Local(b.X, b.Y),
                    b.Button switch { 1 => MouseButton.Left, 2 => MouseButton.Middle, 3 => MouseButton.Right, _ => MouseButton.None }, Modifiers(b.Modifiers));
                break;
            case PointerWheel w: Layer.SendPointerWheel(Local(w.X, w.Y), new(w.DeltaX, w.DeltaY), Modifiers(w.Modifiers)); break;
        }
    }
    public void OnKey(KeyEvent input)
    {
        var key = input.Keysym switch
        {
            >= 0xffbe and <= 0xffc9 => Key.F1 + (int)(input.Keysym - 0xffbe),
            0xff50 => Key.Home, 0xff57 => Key.End, 0xff55 => Key.PageUp, 0xff56 => Key.PageDown, 0xff63 => Key.Insert,
            0xffe1 => Key.LeftShift, 0xffe2 => Key.RightShift, 0xffe3 => Key.LeftCtrl, 0xffe4 => Key.RightCtrl,
            0xffe9 => Key.LeftAlt, 0xffea => Key.RightAlt,
            0x2b or 0x3d => Key.OemPlus, 0x2d or 0x5f => Key.OemMinus,
            0x5b or 0x7b => Key.OemOpenBrackets, 0x5d or 0x7d => Key.OemCloseBrackets,
            0x2c or 0x3c => Key.OemComma, 0x2e or 0x3e => Key.OemPeriod,
            0x2f or 0x3f => Key.OemQuestion, 0x3b or 0x3a => Key.OemSemicolon,
            0x27 or 0x22 => Key.OemQuotes, 0x5c or 0x7c => Key.OemPipe, 0x60 or 0x7e => Key.OemTilde,
            0xff08 => Key.Back, 0xffff => Key.Delete, 0xff0d => Key.Enter, 0xff09 => Key.Tab,
            0xff51 => Key.Left, 0xff52 => Key.Up, 0xff53 => Key.Right, 0xff54 => Key.Down,
            >= 0x61 and <= 0x7a => Key.A + (int)(input.Keysym - 0x61),
            >= 0x41 and <= 0x5a => Key.A + (int)(input.Keysym - 0x41),
            >= 0x30 and <= 0x39 => Key.D0 + (int)(input.Keysym - 0x30),
            0x20 => Key.Space, _ => Key.None
        };
        Layer.SendKey(key, Modifiers(input.Modifiers), input.Pressed);
        if (input.Pressed && !string.IsNullOrEmpty(input.Text) &&
            (input.Modifiers & (OverlayModifiers.Control | OverlayModifiers.Alt | OverlayModifiers.Super)) == 0)
            Layer.SendTextInput(input.Text);
    }
    public void Prepare(LogicalRect selection, LogicalPoint pointer)
    {
        if (selection != lastSelection)
        {
            Layer.SetSelection(new(selection.X - desktop.X, selection.Y - desktop.Y, Math.Max(0, selection.Width), Math.Max(0, selection.Height)));
            lastSelection = selection;
        }
        var output = outputs.FirstOrDefault(o => pointer.X >= o.Logical.X && pointer.X < o.Logical.Right && pointer.Y >= o.Logical.Y && pointer.Y < o.Logical.Bottom) ?? outputs[0];
        var r = output.Logical;
        Layer.SetActiveMonitor(new(r.X - desktop.X, r.Y - desktop.Y, r.Width, r.Height));
        var rendered = Layer.Render();
        if (!rendered.HasChanges) return;
        var frame = rendered.Frame;
        retained ??= new SKBitmap(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var source = new SKBitmap();
        source.InstallPixels(new(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul), frame.Address, frame.Stride);
        foreach (var region in rendered.DirtyRegions)
            MonitorRenderer.Copy(source, retained, new(region.X, region.Y, region.Right, region.Bottom));
        retained.NotifyPixelsChanged();
        double scale = (double)frame.Width / desktop.Width;
        Bitmap = new(retained, new(desktop.X, desktop.Y, desktop.Width, desktop.Height), ++revision,
            rendered.DirtyRegions.Select(r => new LogicalRect(desktop.X + r.X / scale, desktop.Y + r.Y / scale, r.Width / scale, r.Height / scale)).ToArray());
    }
    private static RawInputModifiers Modifiers(OverlayModifiers modifiers)
    {
        RawInputModifiers result = RawInputModifiers.None;
        if (modifiers.HasFlag(OverlayModifiers.Shift)) result |= RawInputModifiers.Shift;
        if (modifiers.HasFlag(OverlayModifiers.Control)) result |= RawInputModifiers.Control;
        if (modifiers.HasFlag(OverlayModifiers.Alt)) result |= RawInputModifiers.Alt;
        if (modifiers.HasFlag(OverlayModifiers.Super)) result |= RawInputModifiers.Meta;
        return result;
    }
    public void Dispose() { Layer.Dispose(); retained?.Dispose(); }
}
