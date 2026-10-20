// GPL-3.0-or-later.
#nullable enable

using Avalonia.Styling;
using ShareX.ImageEditor.Integration;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ScreenCaptureLib;

namespace ShareX.OverlayAvaloniaLayer;

public sealed class AnnotationLayerOptions
{
    public ImageEditorOptions ImageEditorOptions { get; set; } = new();
    public RegionCaptureOptions RegionCaptureOptions { get; set; } = new();
    public bool EnableAnnotations { get; set; } = true;
    public double RenderScaling { get; set; } = 1.0;
    public ThemeVariant? Theme { get; set; }
}
