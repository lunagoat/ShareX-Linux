#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Avalonia;
using SkiaSharp;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShareX.Tools.Linux;

// WP5-local seam until WP2's compositor backend is merged. Handles are compositor IDs, never Win32 HWNDs.
internal static class DesktopTools
{
    public static bool Hyprland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));
    public static bool Sway => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK"));
    public static bool Wayland => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public static string WindowSupport => Hyprland ? "Hyprland: compositor window geometry and process details. Child controls cannot be inspected on Wayland."
        : Sway ? "Sway: compositor window geometry and process details. Child controls cannot be inspected on Wayland."
        : Wayland ? "Window inspection requires Hyprland, sway, or X11. On GNOME/KDE Wayland use the compositor's window inspector."
        : "X11: window geometry and process details. Requires xdotool.";

    public static IReadOnlyList<DesktopWindow> GetWindows()
    {
        if (Hyprland)
        {
            using JsonDocument document = JsonDocument.Parse(ToolProcess.Read("hyprctl", "clients", "-j"));
            return document.RootElement.EnumerateArray().Where(w => !w.TryGetProperty("mapped", out var mapped) || mapped.GetBoolean())
                .Select(w => new DesktopWindow(new IntPtr(long.Parse(w.GetProperty("address").GetString()![2..], NumberStyles.HexNumber)),
                w.GetProperty("title").GetString() ?? "", w.GetProperty("class").GetString() ?? "", w.GetProperty("pid").GetInt32(),
                new Rectangle(w.GetProperty("at")[0].GetInt32(), w.GetProperty("at")[1].GetInt32(),
                    w.GetProperty("size")[0].GetInt32(), w.GetProperty("size")[1].GetInt32()),
                w.TryGetProperty("floating", out var floating) && floating.GetBoolean(),
                w.TryGetProperty("fullscreen", out var full) ? full.GetInt32() : 0)).ToArray();
        }
        if (Sway)
        {
            using JsonDocument document = JsonDocument.Parse(ToolProcess.Read("swaymsg", "-t", "get_tree", "-r"));
            List<DesktopWindow> result = [];
            WalkSway(document.RootElement, result);
            return result;
        }
        if (Wayland) return [];
        List<DesktopWindow> windows = [];
        foreach (string id in ToolProcess.Read("xdotool", "search", "--onlyvisible", "--name", ".").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string title = ToolProcess.Read("xdotool", "getwindowname", id).Trim();
                string geometry = ToolProcess.Read("xdotool", "getwindowgeometry", "--shell", id);
                var fields = geometry.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Contains('='))
                    .Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => x[1]);
                int pid = 0;
                try { int.TryParse(ToolProcess.Read("xdotool", "getwindowpid", id).Trim(), out pid); } catch { }
                windows.Add(new(new IntPtr(long.Parse(id, CultureInfo.InvariantCulture)), title, "X11", pid,
                    new(int.Parse(fields["X"]), int.Parse(fields["Y"]), int.Parse(fields["WIDTH"]), int.Parse(fields["HEIGHT"])), false, 0));
            }
            catch (Exception ex) { ToolsDiagnostics.ReportWarning(nameof(DesktopTools), "A window closed during enumeration.", ex); }
        }
        return windows;
    }

    private static void WalkSway(JsonElement node, List<DesktopWindow> result)
    {
        if (node.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number && node.TryGetProperty("rect", out var rect))
            result.Add(new(new IntPtr(node.GetProperty("id").GetInt64()), node.GetProperty("name").GetString() ?? "",
                node.TryGetProperty("app_id", out var app) && app.ValueKind == JsonValueKind.String ? app.GetString()! : "XWayland",
                pid.GetInt32(), new(rect.GetProperty("x").GetInt32(), rect.GetProperty("y").GetInt32(),
                    rect.GetProperty("width").GetInt32(), rect.GetProperty("height").GetInt32()), false,
                node.TryGetProperty("fullscreen_mode", out var mode) ? mode.GetInt32() : 0));
        foreach (string name in new[] { "nodes", "floating_nodes" })
            if (node.TryGetProperty(name, out var children)) foreach (var child in children.EnumerateArray()) WalkSway(child, result);
    }

    public static PixelPoint? CursorPosition()
    {
        if (Hyprland)
        {
            using var document = JsonDocument.Parse(ToolProcess.Read("hyprctl", "cursorpos", "-j"));
            return new(document.RootElement.GetProperty("x").GetInt32(), document.RootElement.GetProperty("y").GetInt32());
        }
        if (Wayland) return null;
        nint display = XOpenDisplay(0);
        if (display == 0) return null;
        try { return XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out int x, out int y, out _, out _, out _) ? new(x, y) : null; }
        finally { XCloseDisplay(display); }
    }

    public static SKBitmap Capture(PixelRect bounds)
    {
        byte[] png;
        if (Wayland)
            png = ToolProcess.RunAsync("grim", ["-s", "1", "-g", $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}", "-"]).GetAwaiter().GetResult();
        else
            png = ToolProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "x11grab", "-video_size", $"{bounds.Width}x{bounds.Height}",
                "-i", $"{Environment.GetEnvironmentVariable("DISPLAY")}+{bounds.X},{bounds.Y}", "-frames:v", "1", "-threads", "1", "-f", "image2pipe", "-c:v", "png", "pipe:1"]).GetAwaiter().GetResult();
        return SKBitmap.Decode(png) ?? throw new InvalidOperationException("The compositor did not return a screenshot. On GNOME/KDE Wayland use portal screen capture.");
    }

    [DllImport("libX11.so.6")] internal static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] internal static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] internal static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool XQueryPointer(nint display, nint window, out nint root, out nint child, out int rootX, out int rootY,
        out int x, out int y, out uint mask);
}

internal sealed record DesktopWindow(IntPtr Handle, string Text, string ClassName, int ProcessId, Rectangle Rectangle, bool Floating, int Fullscreen)
{
    public string ProcessName { get { try { return System.Diagnostics.Process.GetProcessById(ProcessId).ProcessName; } catch { return ClassName; } } }
    public string ProcessFileName { get { try { return File.ResolveLinkTarget($"/proc/{ProcessId}/exe", true)?.FullName ?? ""; } catch { return ""; } } }
    public Rectangle ClientRectangle => Rectangle;
    public string Style => Floating ? "Floating" : "Tiled";
    public string ExStyle => Fullscreen > 0 ? "Fullscreen" : "Normal";
    public bool TopMost { get => false; set => throw new NotSupportedException("Use your compositor's pin/keep-above action for this window."); }
    public byte Opacity { get => 255; set => throw new NotSupportedException("Use your compositor's opacity rule for this window."); }
}
