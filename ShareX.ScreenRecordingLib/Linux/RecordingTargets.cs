// ShareX - GPL v3. See LICENSE.txt.
using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace ShareX.ScreenRecordingLib;

/// <summary>Recording-local geometry shim until the shared Linux window provider is merged.</summary>
public static class RecordingTargets
{
    public static (Rectangle Region, nint Handle) ActiveWindow()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            using JsonDocument data = JsonDocument.Parse(LinuxProcess.Run("hyprctl", "activewindow", "-j").GetAwaiter().GetResult());
            var root = data.RootElement;
            if (!root.TryGetProperty("address", out var addr) || addr.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(addr.GetString()))
                throw new InvalidOperationException("No active window.");
            string address = addr.GetString()!;
            string hex = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
            return (ReadHyprland(root), (nint)Convert.ToInt64(hex, 16));
        }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            using JsonDocument data = JsonDocument.Parse(LinuxProcess.Run("swaymsg", "-t", "get_tree", "-r").GetAwaiter().GetResult());
            JsonElement? window = Focused(data.RootElement);
            if (window is not { } found) throw new IOException("No focused window.");
            return (ReadSway(found), (nint)found.GetProperty("id").GetInt64());
        }
        string id = LinuxProcess.Run("xdotool", "getactivewindow").GetAwaiter().GetResult().Trim();
        return (X11Window(id), (nint)long.Parse(id, CultureInfo.InvariantCulture));
    }

    public static Rectangle Window(nint handle)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            using JsonDocument data = JsonDocument.Parse(LinuxProcess.Run("hyprctl", "clients", "-j").GetAwaiter().GetResult());
            foreach (JsonElement window in data.RootElement.EnumerateArray())
            {
                if (window.TryGetProperty("address", out var addr) && addr.GetString() is { } address)
                {
                    string hex = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
                    if (Convert.ToInt64(hex, 16) == handle.ToInt64()) return ReadHyprland(window);
                }
            }
            throw new IOException("The recorded window has closed or is unavailable.");
        }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            using JsonDocument data = JsonDocument.Parse(LinuxProcess.Run("swaymsg", "-t", "get_tree", "-r").GetAwaiter().GetResult());
            JsonElement? found = Find(data.RootElement, node => node.TryGetProperty("id", out var id) && id.GetInt64() == handle.ToInt64());
            return found.HasValue ? ReadSway(found.Value) : throw new IOException("The recorded window has closed.");
        }
        return X11Window(handle.ToString());
    }

    public static Rectangle? DesktopBounds()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return null;
        using JsonDocument data = JsonDocument.Parse(LinuxProcess.Run("hyprctl", "monitors", "-j").GetAwaiter().GetResult());
        Rectangle? bounds = null;
        foreach (JsonElement monitor in data.RootElement.EnumerateArray())
        {
            double scale = monitor.GetProperty("scale").GetDouble();
            int width = monitor.GetProperty("width").GetInt32(), height = monitor.GetProperty("height").GetInt32();
            if (monitor.GetProperty("transform").GetInt32() % 2 != 0) (width, height) = (height, width);
            Rectangle rect = new(monitor.GetProperty("x").GetInt32(), monitor.GetProperty("y").GetInt32(), (int)Math.Round(width / scale), (int)Math.Round(height / scale));
            bounds = bounds.HasValue ? Rectangle.Union(bounds.Value, rect) : rect;
        }
        return bounds;
    }
    private static Rectangle ReadHyprland(JsonElement window)
    {
        var at = window.GetProperty("at"); var size = window.GetProperty("size");
        return new(at[0].GetInt32(), at[1].GetInt32(), size[0].GetInt32(), size[1].GetInt32());
    }
    private static Rectangle ReadSway(JsonElement window)
    {
        var rect = window.GetProperty("rect");
        return new(rect.GetProperty("x").GetInt32(), rect.GetProperty("y").GetInt32(), rect.GetProperty("width").GetInt32(), rect.GetProperty("height").GetInt32());
    }
    private static JsonElement? Focused(JsonElement node) => Find(node, n => n.TryGetProperty("focused", out var focus) && focus.GetBoolean());
    private static JsonElement? Find(JsonElement node, Func<JsonElement, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (string property in new[] { "nodes", "floating_nodes" })
            if (node.TryGetProperty(property, out var children))
                foreach (var child in children.EnumerateArray())
                    if (Find(child, predicate) is { } result) return result;
        return null;
    }
    private static Rectangle X11Window(string id)
    {
        string text = LinuxProcess.Run("xdotool", "getwindowgeometry", "--shell", id).GetAwaiter().GetResult();
        var values = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);
        return new(int.Parse(values["X"]), int.Parse(values["Y"]), int.Parse(values["WIDTH"]), int.Parse(values["HEIGHT"]));
    }
}
