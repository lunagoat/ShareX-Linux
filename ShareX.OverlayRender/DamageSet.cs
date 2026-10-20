// ShareX overlay scene renderer. GPL-3.0-or-later.
using SkiaSharp;

namespace ShareX.OverlayRender;

// A small nonoverlapping geometric region set. No framebuffer scans or pixel comparisons.
internal sealed class DamageSet(SKRectI surface)
{
    public List<SKRectI> Regions { get; } = [];
    public SKRectI Bounds => Regions.Count == 0 ? default : new(Regions.Min(r => r.Left), Regions.Min(r => r.Top), Regions.Max(r => r.Right), Regions.Max(r => r.Bottom));

    public void Add(SKRectI rect)
    {
        rect = Intersection(rect, surface);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        List<SKRectI> pending = [rect];
        foreach (SKRectI existing in Regions)
        {
            pending = pending.SelectMany(r => Subtract(r, existing)).ToList();
            if (pending.Count == 0) return;
        }
        Regions.AddRange(pending);
    }

    private static SKRectI Intersection(SKRectI a, SKRectI b)
    {
        SKRectI r = new(Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top), Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));
        return (r.Width <= 0 || r.Height <= 0) ? default : r;
    }

    public static IEnumerable<SKRectI> Subtract(SKRectI a, SKRectI b)
    {
        if (a.Width <= 0 || a.Height <= 0) yield break;
        SKRectI i = Intersection(a, b);
        if (i.Width <= 0 || i.Height <= 0) { yield return a; yield break; }
        if (a.Top < i.Top) yield return new(a.Left, a.Top, a.Right, i.Top);
        if (i.Bottom < a.Bottom) yield return new(a.Left, i.Bottom, a.Right, a.Bottom);
        if (a.Left < i.Left) yield return new(a.Left, i.Top, i.Left, i.Bottom);
        if (i.Right < a.Right) yield return new(i.Right, i.Top, a.Right, i.Bottom);
    }
}
