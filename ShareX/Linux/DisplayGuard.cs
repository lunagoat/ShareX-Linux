// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.IO;
using System.Linq;

namespace ShareX;

/// <summary>
/// ShareX's UI toolkit (Avalonia) talks X11, so a Wayland session needs an X server for it: XWayland on GNOME, KDE, Hyprland and
/// sway, or xwayland-satellite on niri. Find a running one, or explain how to get one instead of crashing with "XOpenDisplay failed".
/// </summary>
internal static class DisplayGuard
{
    /// <summary>Overridable for tests.</summary>
    internal static string SocketDirectory = Environment.GetEnvironmentVariable("SX_X11_SOCKET_DIR") ?? "/tmp/.X11-unix";

    private static bool SocketExists(string display)
    {
        // ":0", ":0.0", "localhost:0" -> display number
        int colon = display.LastIndexOf(':');
        if (colon < 0) return false;
        string number = new string(display[(colon + 1)..].TakeWhile(char.IsDigit).ToArray());
        return number.Length > 0 && File.Exists(Path.Combine(SocketDirectory, "X" + number));
    }

    /// <summary>Returns null when an X server is reachable (setting DISPLAY if it had to be discovered), otherwise a help message.</summary>
    public static string Check()
    {
        string display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display) && (display.StartsWith(":") ? SocketExists(display) : true))
        {
            return null;
        }

        // No usable DISPLAY (common when started from a compositor key bind or a service): use any running X server of ours.
        try
        {
            string found = Directory.Exists(SocketDirectory)
                ? Directory.GetFiles(SocketDirectory, "X*").Select(Path.GetFileName).Where(x => x.Length > 1 && x[1..].All(char.IsDigit)).OrderBy(x => int.Parse(x[1..])).FirstOrDefault()
                : null;
            if (found != null)
            {
                Environment.SetEnvironmentVariable("DISPLAY", ":" + found[1..]);
                return null;
            }
        }
        catch (Exception)
        {
        }

        string desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToLowerInvariant();
        bool niri = desktop.Contains("niri") || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET"));
        bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        if (niri)
        {
            return "ShareX needs an X server, and niri does not include one.\n" +
                   "Install xwayland-satellite (niri 25.08 and newer starts it automatically once it is installed;\n" +
                   "on older niri add  spawn-at-startup \"xwayland-satellite\"  to your config and restart niri), then start ShareX again.";
        }

        if (wayland)
        {
            return "ShareX needs an X server (XWayland), but none is running in this session.\n" +
                   "Install XWayland (Arch: xorg-xwayland, Debian/Ubuntu: xwayland, Fedora: xorg-x11-server-Xwayland) and make sure your compositor starts it.";
        }

        return "No display found: neither a Wayland nor an X11 session is available (DISPLAY and WAYLAND_DISPLAY are empty).";
    }
}
