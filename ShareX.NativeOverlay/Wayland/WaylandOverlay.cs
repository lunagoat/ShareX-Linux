// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
namespace ShareX.NativeOverlay.Wayland;

public static class WaylandOverlay
{
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
