// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace ShareX.HelpersLib;

/// <summary>Compositor coordinates are logical desktop pixels, including negative monitor origins.
/// Handles are opaque: Hyprland addresses, sway container IDs, or X11 XIDs. Never truncate to Int32.</summary>
public static class LinuxWindowSystem
{
    public sealed record Window(IntPtr Handle, string Title, string ClassName, int Pid, Rectangle Bounds,
        Rectangle ClientBounds, bool Visible, bool Active, bool Maximized = false, bool Minimized = false);

    private static readonly object sync = new();
    private static Window[] cached = [];
    private static long updated;
    public static string Backend => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) ? "Hyprland" :
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) ? "sway" :
        !IsWayland ? "X11" : "Wayland (window enumeration unavailable)";
    public static bool IsWayland => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public static IReadOnlyList<Window> GetWindows(bool refresh = false)
    {
        lock (sync)
        {
            if (!refresh && Environment.TickCount64 - updated < 100) return cached;
            try
            {
                cached = Backend switch
                {
                    "Hyprland" => ReadHyprland(),
                    "sway" => ParseSway(LinuxCommand.Run("swaymsg", "-r", "-t", "get_tree") ?? "{}", LinuxCommand.Run("swaymsg", "-r", "-t", "get_workspaces")),
                    "X11" => LinuxX11.GetWindows(),
                    _ => []
                };
            }
            catch (Exception exception)
            {
                DebugHelper.WriteException(exception, "Unable to enumerate " + Backend + " windows.");
                cached = [];
            }
            updated = Environment.TickCount64;
            return cached;
        }
    }

    public static Window Find(IntPtr handle) => GetWindows().FirstOrDefault(x => x.Handle == handle);
    public static IntPtr ActiveWindow => GetWindows().FirstOrDefault(x => x.Active)?.Handle ?? IntPtr.Zero;
    public static IntPtr WindowAt(Point point) => GetWindows().FirstOrDefault(x => x.Visible && x.Bounds.Contains(point))?.Handle ?? IntPtr.Zero;

    private static Window[] ReadHyprland()
    {
        string clients = LinuxCommand.Run("hyprctl", "-j", "clients");
        string monitors = LinuxCommand.Run("hyprctl", "-j", "monitors");
        string active = LinuxCommand.Run("hyprctl", "-j", "activewindow");
        return ParseHyprland(clients ?? "[]", monitors ?? "[]", active ?? "{}");
    }

    // Public parsers also permit offline compositor fixture tests without a running desktop.
    public static Window[] ParseHyprland(string clients, string monitors, string active)
    {
        HashSet<int> workspaces = JArray.Parse(monitors).SelectMany(m => new[] {
            (int?)m["activeWorkspace"]?["id"] ?? 0, (int?)m["specialWorkspace"]?["id"] ?? 0 }).Where(x => x != 0).ToHashSet();
        string activeAddress = (string)JObject.Parse(active)["address"];
        return JArray.Parse(clients).OrderBy(c => (int?)c["focusHistoryID"] ?? int.MaxValue).OrderByDescending(c => (bool?)c["floating"] == true).Select(c =>
        {
            Rectangle bounds = new((int?)c["at"]?[0] ?? 0, (int?)c["at"]?[1] ?? 0,
                (int?)c["size"]?[0] ?? 0, (int?)c["size"]?[1] ?? 0);
            string address = (string)c["address"] ?? "0x0";
            bool visible = (bool?)c["mapped"] == true && (bool?)c["hidden"] != true &&
                ((bool?)c["pinned"] == true || workspaces.Contains((int?)c["workspace"]?["id"] ?? 0));
            return new Window(new IntPtr(long.Parse(address.Replace("0x", ""), NumberStyles.HexNumber)),
                (string)c["title"] ?? "", (string)c["class"] ?? "", (int?)c["pid"] ?? 0, bounds, bounds,
                visible, address == activeAddress, (int?)c["fullscreen"] > 0);
        }).OrderByDescending(x => x.Active).ToArray();
    }

    public static Window[] ParseSway(string json, string workspacesJson = null)
    {
        HashSet<string> visibleWorkspaces = workspacesJson == null ? null : JArray.Parse(workspacesJson).Where(x => (bool?)x["visible"] == true).Select(x => (string)x["name"]).ToHashSet();
        List<Window> windows = new();
        void Visit(JToken node, bool workspaceVisible)
        {
            if ((string)node["type"] == "workspace") workspaceVisible = (bool?)node["visible"] ?? visibleWorkspaces?.Contains((string)node["name"]) ?? false;
            if (node["app_id"]?.Type == JTokenType.String || node["window"]?.Type == JTokenType.Integer)
            {
                JToken rect = node["rect"];
                Rectangle bounds = new((int?)rect?["x"] ?? 0, (int?)rect?["y"] ?? 0, (int?)rect?["width"] ?? 0, (int?)rect?["height"] ?? 0);
                JToken client = node["window_rect"];
                Rectangle clientBounds = new(bounds.X + ((int?)client?["x"] ?? 0), bounds.Y + ((int?)client?["y"] ?? 0),
                    (int?)client?["width"] ?? bounds.Width, (int?)client?["height"] ?? bounds.Height);
                windows.Add(new Window(new IntPtr((long?)node["id"] ?? 0), (string)node["name"] ?? "",
                    (string)node["app_id"] ?? (string)node["window_properties"]?["class"] ?? "", (int?)node["pid"] ?? 0,
                    bounds, clientBounds, (bool?)node["visible"] ?? workspaceVisible, (bool?)node["focused"] == true,
                    (int?)node["fullscreen_mode"] > 0));
            }
            foreach (string key in new[] { "nodes", "floating_nodes" })
                foreach (JToken child in node[key] as JArray ?? new JArray()) Visit(child, workspaceVisible);
        }
        Visit(JObject.Parse(json), true);
        return windows.OrderByDescending(x => x.Active).ToArray();
    }

    public static bool TryGetCursorPosition(out Point point)
    {
        point = Point.Empty;
        if (Backend == "Hyprland")
        {
            try
            {
                string json = LinuxCommand.Run("hyprctl", "-j", "cursorpos");
                if (json == null) return false;
                JObject cursor = JObject.Parse(json);
                point = new Point((int)cursor["x"], (int)cursor["y"]);
                return true;
            }
            catch { return false; }
        }
        // XWayland's pointer is not a reliable global Wayland pointer. Do not fabricate a position.
        return !IsWayland && LinuxX11.TryGetCursorPosition(out point);
    }

    public static bool Activate(IntPtr handle) => Backend switch
    {
        "Hyprland" => LinuxCommand.Run("hyprctl", "dispatch", "focuswindow", "address:0x" + handle.ToInt64().ToString("x")) != null,
        "sway" => LinuxCommand.Run("swaymsg", "[con_id=" + handle.ToInt64() + "]", "focus") != null,
        "X11" => LinuxX11.Activate(handle),
        _ => false
    };

    public static bool SetCursorPosition(int x, int y) => !IsWayland && LinuxX11.SetCursorPosition(x, y);
}
