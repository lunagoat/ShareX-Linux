// Offline fixture tests for the compositor parsers (niri, KDE Plasma). The JSON mirrors what `niri msg --json ...` and
// `kscreen-doctor -j` print; there is no niri or Plasma session on the development machine, so these pin the parsing logic.
using ShareX.AvaloniaUI.Linux;

int failures = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); if (!ok) failures++; }

// ---- niri outputs: a 4K panel at 1.5 scale and a 1080p monitor to its right; a disabled output has no "logical"
const string niriOutputs = """
{
  "eDP-1": { "name": "eDP-1", "make": "BOE", "model": "x", "serial": null, "physical_size": [340, 190], "modes": [], "current_mode": 0,
             "vrr_supported": false, "vrr_enabled": false,
             "logical": { "x": 0, "y": 0, "width": 2560, "height": 1440, "scale": 1.5, "transform": "Normal" } },
  "DP-2":  { "name": "DP-2", "modes": [], "current_mode": 0, "logical": { "x": 2560, "y": 100, "width": 1920, "height": 1080, "scale": 1.0, "transform": "Normal" } },
  "HDMI-A-1": { "name": "HDMI-A-1", "modes": [], "current_mode": null, "logical": null }
}
""";
var monitors = LinuxScreenCapture.ParseNiriOutputs(niriOutputs, "DP-2");
Check(monitors.Length == 2, "niri: disabled output ignored");
Check(monitors.Single(m => m.Name == "eDP-1").Bounds == new System.Drawing.Rectangle(0, 0, 2560, 1440) && monitors.Single(m => m.Name == "eDP-1").Scale == 1.5, "niri: logical rectangle and scale");
Check(monitors.Single(m => m.Name == "DP-2").Primary && !monitors.Single(m => m.Name == "eDP-1").Primary, "niri: focused output marked primary");

// ---- niri windows: tile position is relative to the workspace view, output origin must be added
const string niriWorkspaces = """
[ { "id": 1, "idx": 1, "name": null, "output": "eDP-1", "is_urgent": false, "is_active": true, "is_focused": true, "active_window_id": 10 },
  { "id": 2, "idx": 2, "name": null, "output": "eDP-1", "is_urgent": false, "is_active": false, "is_focused": false, "active_window_id": 11 },
  { "id": 3, "idx": 1, "name": null, "output": "DP-2",  "is_urgent": false, "is_active": true, "is_focused": false, "active_window_id": 12 } ]
""";
const string niriWindows = """
[ { "id": 10, "title": "Terminal", "app_id": "foot", "pid": 100, "workspace_id": 1, "is_focused": true, "is_floating": false, "is_urgent": false,
    "layout": { "pos_in_scrolling_layout": [1, 1], "tile_size": [1270, 1400], "window_size": [1260, 1390], "tile_pos_in_workspace_view": [10, 20], "window_offset_in_tile": [5, 5] } },
  { "id": 11, "title": "Hidden", "app_id": "firefox", "pid": 101, "workspace_id": 2, "is_focused": false, "is_floating": false, "is_urgent": false,
    "layout": { "pos_in_scrolling_layout": [1, 1], "tile_size": [800, 600], "window_size": [800, 600], "tile_pos_in_workspace_view": null, "window_offset_in_tile": [0, 0] } },
  { "id": 12, "title": "Editor", "app_id": "code", "pid": 102, "workspace_id": 3, "is_focused": false, "is_floating": true, "is_urgent": false,
    "layout": { "pos_in_scrolling_layout": null, "tile_size": [900, 700], "window_size": [900, 700], "tile_pos_in_workspace_view": [100, 50], "window_offset_in_tile": [0, 0] } } ]
""";
var windows = NiriIpc.Parse(niriWindows, niriWorkspaces, niriOutputs);
Check(windows.Length == 3, "niri: all windows listed");
Check(windows[0].Id == 10 && windows[0].Focused, "niri: focused window first");
Check(windows[0].Bounds == new System.Drawing.Rectangle(15, 25, 1260, 1390) && windows[0].Visible, "niri: tile position + window offset on the first output");
var editor = windows.Single(w => w.Id == 12);
Check(editor.Bounds == new System.Drawing.Rectangle(2660, 150, 900, 700) && editor.Visible, "niri: output origin added for a window on the second output");
Check(!windows.Single(w => w.Id == 11).Visible && windows.Single(w => w.Id == 11).Bounds.IsEmpty, "niri: window off the active workspace is not visible");
Check(NiriIpc.Parse("[]", "[]", "{}").Length == 0, "niri: empty desktop");

// ---- KDE Plasma
const string kscreen = """
{ "outputs": [
  { "id": 1, "name": "DP-3", "enabled": true, "connected": true, "primary": true, "pos": { "x": 0, "y": 0 }, "size": { "width": 3840, "height": 2160 }, "scale": 1.5, "rotation": 1 },
  { "id": 2, "name": "HDMI-A-1", "enabled": true, "connected": true, "primary": false, "pos": { "x": 2560, "y": 0 }, "size": { "width": 1080, "height": 1920 }, "scale": 1.0, "rotation": 2 },
  { "id": 3, "name": "DP-1", "enabled": false, "connected": true, "primary": false, "pos": { "x": 0, "y": 0 }, "size": { "width": 1920, "height": 1080 }, "scale": 1.0, "rotation": 1 } ],
  "screen": {} }
""";
var kde = LinuxScreenCapture.ParseKScreenDoctor(kscreen);
Check(kde.Length == 2, "kde: disabled output ignored");
Check(kde[0].Bounds == new System.Drawing.Rectangle(0, 0, 2560, 1440) && kde[0].Primary, "kde: physical size divided by the scale");
Check(kde[1].Bounds == new System.Drawing.Rectangle(2560, 0, 1920, 1080), "kde: rotated output swaps width and height");

Console.WriteLine($"RESULT: {failures} failures");
return failures == 0 ? 0 : 1;
