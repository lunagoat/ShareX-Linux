// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
namespace ShareX.NativeOverlay.Wayland;

public static class WaylandOverlay
{
    /// <summary>Read compositor geometry without creating or mapping any surfaces.</summary>
    public static IReadOnlyList<OverlayOutput> GetOutputs()
    {
        using var probe = new WaylandOverlaySession(true);
        return probe.Outputs.ToArray();
    }

    public static void Register()
    {
        OverlaySessionFactory.IsSupported = () =>
        {
            if (!OperatingSystem.IsLinux()) return false;
            try { using var probe = new WaylandOverlaySession(true); return true; }
            catch { return false; }
        };
        OverlaySessionFactory.Open = () => new WaylandOverlaySession();
    }
}
