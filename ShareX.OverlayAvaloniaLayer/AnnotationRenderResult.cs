// GPL-3.0-or-later.
#nullable enable

using System;
using System.Collections.Generic;
using Avalonia;

namespace ShareX.OverlayAvaloniaLayer;

public sealed class AnnotationRenderResult
{
    public bool HasChanges { get; init; }
    public IReadOnlyList<PixelRect> DirtyRegions { get; init; } = Array.Empty<PixelRect>();
    public PixelRect EnclosingBounds { get; init; }
    public LayerFrame Frame { get; init; }
    public double TotalMilliseconds { get; init; }
    public double AvaloniaMilliseconds { get; init; }
    public double RasterMilliseconds { get; init; }
    public double DiffMilliseconds { get; init; }
    public long ChangedBytes { get; init; }
}
