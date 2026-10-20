// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Drawing;
using System.Text.Json;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>
/// Panels such as waybar are layer-shell surfaces, which the compositor draws above ordinary windows (and swallows their clicks).
/// An overlay window cannot get above them unless it is fullscreen on one monitor, so UI near a screen edge has to stay out from under them.
/// </summary>
public static class LayerShellInsets
{
    /// <summary>Height of the bar(s) covering the top edge of <paramref name="monitor"/> (0 when there is none or the compositor cannot tell).</summary>
    public static int TopInset(Rectangle monitor)
    {
        if (NiriIpc.IsAvailable) return NiriTopInset(monitor);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            try { return ParseSwayTopInset(LinuxCaptureProcess.Text("swaymsg", "-t", "get_workspaces", "-r"), monitor); }
            catch (Exception) { return 0; }
        }
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return 0;

        try
        {
            return ParseHyprlandTopInset(LinuxCaptureProcess.Text("hyprctl", "layers", "-j"), monitor);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// sway: a visible workspace's rectangle already excludes exclusive bars and nags (swaybar, waybar with an exclusive zone), so the
    /// distance from the monitor's top edge to the workspace's top edge is the space to stay out of.
    /// </summary>
    public static int ParseSwayTopInset(string workspacesJson, Rectangle monitor)
    {
        int inset = 0;
        using JsonDocument document = JsonDocument.Parse(workspacesJson);

        foreach (JsonElement ws in document.RootElement.EnumerateArray())
        {
            if (!ws.TryGetProperty("visible", out var visible) || visible.ValueKind != JsonValueKind.True || !ws.TryGetProperty("rect", out var r)) continue;
            Rectangle rect = new(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(), r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32());
            if (rect.IntersectsWith(monitor)) inset = Math.Max(inset, Math.Max(0, rect.Y - monitor.Y));
        }

        return inset;
    }

    // Surfaces that sit on the top layer but are not bars.
    private static readonly string[] NotBars = ["notification", "swaync", "mako", "dunst", "fnott", "wob", "swayosd", "launcher", "fuzzel", "wofi", "rofi", "tofi", "lock", "polkit", "osd", "gtk-layer-shell"];

    /// <summary>
    /// niri lists layer-shell surfaces (namespace, output, layer) but not their size, so an exact bar height is unknown. When a bar-like surface
    /// is on the top layer of that output, assume a typical bar plus margin; SHAREX_TOOLBAR_INSET=&lt;pixels&gt; overrides it (0 turns it off).
    /// </summary>
    public static int NiriTopInset(Rectangle monitor)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("SHAREX_TOOLBAR_INSET"), out int fixedInset)) return Math.Max(0, fixedInset);

        try
        {
            string outputName = LinuxScreenCapture.GetMonitors().FirstOrDefault(m => m.Bounds == monitor)?.Name ?? "";
            return ParseNiriHasTopBar(LinuxCaptureProcess.Text("niri", "msg", "--json", "layers"), outputName) ? 48 : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>`niri msg --json layers`: [{ "namespace": "waybar", "output": "DP-1", "layer": "Top", ... }].</summary>
    public static bool ParseNiriHasTopBar(string json, string outputName)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        foreach (JsonElement surface in document.RootElement.EnumerateArray())
        {
            string layer = surface.TryGetProperty("layer", out var l) ? l.GetString() ?? "" : "";
            string output = surface.TryGetProperty("output", out var o) ? o.GetString() ?? "" : "";
            string name = (surface.TryGetProperty("namespace", out var n) ? n.GetString() ?? "" : "").ToLowerInvariant();

            if ((layer is "Top" or "Overlay") && (outputName.Length == 0 || output == outputName) && !NotBars.Any(name.Contains)) return true;
        }

        return false;
    }

    /// <summary>
    /// `hyprctl layers -j`: { "DP-1": { "levels": { "0": [...], "1": [...], "2": [...top...], "3": [...overlay...] } } }, coordinates in
    /// the global layout. Only the top and overlay levels sit above windows. Wide, short surfaces touching the top edge are bars; tall ones
    /// (notification columns, launchers) are not.
    /// </summary>
    public static int ParseHyprlandTopInset(string json, Rectangle monitor)
    {
        int inset = 0;
        using JsonDocument document = JsonDocument.Parse(json);

        foreach (JsonProperty output in document.RootElement.EnumerateObject())
        {
            if (!output.Value.TryGetProperty("levels", out JsonElement levels)) continue;

            foreach (string level in new[] { "2", "3" })
            {
                if (!levels.TryGetProperty(level, out JsonElement layers) || layers.ValueKind != JsonValueKind.Array) continue;

                foreach (JsonElement layer in layers.EnumerateArray())
                {
                    Rectangle r = new(layer.GetProperty("x").GetInt32(), layer.GetProperty("y").GetInt32(), layer.GetProperty("w").GetInt32(), layer.GetProperty("h").GetInt32());
                    if (r.Width <= 0 || r.Height <= 0 || !r.IntersectsWith(monitor)) continue;
                    if (r.Width < monitor.Width * 0.4) continue;                 // a side panel or notification column
                    if (r.Height > monitor.Height / 3) continue;                 // not a bar
                    if (r.Y - monitor.Y > monitor.Height / 4) continue;          // a bar at the bottom
                    inset = Math.Max(inset, r.Bottom - monitor.Y);
                }
            }
        }

        return inset;
    }
}
