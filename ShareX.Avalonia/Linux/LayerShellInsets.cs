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
