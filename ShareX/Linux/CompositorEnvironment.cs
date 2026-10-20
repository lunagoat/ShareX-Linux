// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.IO;
using System.Linq;

namespace ShareX;

/// <summary>
/// ShareX finds out which compositor it runs under from environment variables (NIRI_SOCKET, SWAYSOCK, HYPRLAND_INSTANCE_SIGNATURE). A process
/// started by systemd, an XDG autostart entry or a display-manager script often does not get them, and then every compositor-specific code path
/// (overlay placement, monitor layout, bars) is silently skipped. When none is set but a compositor's IPC socket exists, use it.
/// </summary>
internal static class CompositorEnvironment
{
    internal static string RuntimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/run/user/" + Environment.UserName;

    private static bool Set(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));

    private static bool ProcessAlive(string pidText) => int.TryParse(pidText, out int pid) && Directory.Exists("/proc/" + pid);

    /// <summary>Returns what was discovered (for the log), or null when nothing needed to be done.</summary>
    public static string Ensure()
    {
        if (!OperatingSystem.IsLinux() || Set("NIRI_SOCKET") || Set("SWAYSOCK") || Set("HYPRLAND_INSTANCE_SIGNATURE") || !Directory.Exists(RuntimeDirectory))
        {
            return null;
        }

        try
        {
            string wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";

            // niri.<wayland display>.<pid>.sock
            string niri = Directory.GetFiles(RuntimeDirectory, "niri.*.sock")
                .Select(path => (Path: path, Parts: Path.GetFileName(path).Split('.')))
                .Where(x => x.Parts.Length >= 4 && ProcessAlive(x.Parts[^2]) && (wayland.Length == 0 || Path.GetFileName(x.Path).Contains("." + wayland + ".")))
                .OrderByDescending(x => File.GetLastWriteTimeUtc(x.Path)).Select(x => x.Path).FirstOrDefault();
            if (niri != null)
            {
                Environment.SetEnvironmentVariable("NIRI_SOCKET", niri);
                return "NIRI_SOCKET=" + niri;
            }

            // sway-ipc.<uid>.<pid>.sock
            string sway = Directory.GetFiles(RuntimeDirectory, "sway-ipc.*.sock")
                .Where(path => Path.GetFileName(path).Split('.') is { Length: >= 4 } p && ProcessAlive(p[^2]))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (sway != null)
            {
                Environment.SetEnvironmentVariable("SWAYSOCK", sway);
                return "SWAYSOCK=" + sway;
            }

            // hypr/<signature>/.socket.sock
            string hyprDir = Path.Combine(RuntimeDirectory, "hypr");
            string hypr = Directory.Exists(hyprDir)
                ? Directory.GetDirectories(hyprDir).Where(d => File.Exists(Path.Combine(d, ".socket.sock"))).OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, ".socket.sock"))).FirstOrDefault()
                : null;
            if (hypr != null)
            {
                Environment.SetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE", Path.GetFileName(hypr));
                return "HYPRLAND_INSTANCE_SIGNATURE=" + Path.GetFileName(hypr);
            }
        }
        catch (Exception)
        {
        }

        return null;
    }
}
