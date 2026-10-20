// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Drawing;
using System.Text.Json;

namespace ShareX.AvaloniaUI.Linux;

public sealed record NiriWindow(long Id, string Title, string AppId, int Pid, Rectangle Bounds, bool Visible, bool Focused);

/// <summary>Reads window geometry from niri's IPC (<c>niri msg --json windows|workspaces|outputs</c>).</summary>
public static class NiriIpc
{
    public static bool IsAvailable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET"));

    public static NiriWindow[] GetWindows()
    {
        return Parse(LinuxCaptureProcess.Text("niri", "msg", "--json", "windows"), LinuxCaptureProcess.Text("niri", "msg", "--json", "workspaces"),
            LinuxCaptureProcess.Text("niri", "msg", "--json", "outputs"));
    }

    /// <summary>
    /// niri only reports a window's position (inside its workspace view) for floating windows; for tiled windows
    /// <c>tile_pos_in_workspace_view</c> is null, so their rectangle is unknown. The desktop rectangle is the output's logical origin plus
    /// that position; windows without a position are returned as not visible with an empty rectangle.
    /// </summary>
    public static NiriWindow[] Parse(string windowsJson, string workspacesJson, string outputsJson)
    {
        using JsonDocument windows = JsonDocument.Parse(windowsJson), workspaces = JsonDocument.Parse(workspacesJson), outputs = JsonDocument.Parse(outputsJson);

        Dictionary<long, (string Output, bool Active)> spaces = new();
        foreach (JsonElement ws in workspaces.RootElement.EnumerateArray())
        {
            spaces[Long(ws, "id") ?? -1] = (Str(ws, "output") ?? "", Bool(ws, "is_active"));
        }

        List<NiriWindow> result = [];
        foreach (JsonElement w in windows.RootElement.EnumerateArray())
        {
            long spaceId = Long(w, "workspace_id") ?? -1;
            spaces.TryGetValue(spaceId, out var space);

            JsonElement layout = w.TryGetProperty("layout", out var l) ? l : default;
            double[] size = Pair(layout, "window_size") ?? Pair(layout, "tile_size") ?? [0, 0];
            double[]? tile = Pair(layout, "tile_pos_in_workspace_view");
            double[] offset = Pair(layout, "window_offset_in_tile") ?? [0, 0];

            Rectangle bounds = Rectangle.Empty;
            bool placed = false;
            if (tile != null && outputs.RootElement.TryGetProperty(space.Output ?? "", out var output) &&
                output.TryGetProperty("logical", out var logical) && logical.ValueKind == JsonValueKind.Object)
            {
                bounds = new Rectangle((int)Math.Round(logical.GetProperty("x").GetDouble() + tile[0] + offset[0]),
                    (int)Math.Round(logical.GetProperty("y").GetDouble() + tile[1] + offset[1]), (int)Math.Round(size[0]), (int)Math.Round(size[1]));
                placed = true;
            }

            result.Add(new NiriWindow(Long(w, "id") ?? 0, Str(w, "title") ?? "", Str(w, "app_id") ?? "", (int)(Long(w, "pid") ?? 0), bounds,
                placed && space.Active, Bool(w, "is_focused")));
        }

        return result.OrderByDescending(x => x.Focused).ToArray();
    }

    private static long? Long(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static double[]? Pair(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 2
            ? [v[0].GetDouble(), v[1].GetDouble()] : null;
}
