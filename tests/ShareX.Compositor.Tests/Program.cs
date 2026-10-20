// Offline fixture tests for the compositor parsers (niri, KDE Plasma). The JSON mirrors what `niri msg --json ...` and
// `kscreen-doctor -j` print; there is no niri or Plasma session on the development machine, so these pin the parsing logic.
using ShareX.AvaloniaUI.Linux;

int failures = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); if (!ok) failures++; }

// ---- grim -t ppm decoding (the screenshot fast path)
{
    byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n# comment\n3 2\n255\n");
    byte[] pixels = [255, 0, 0,  0, 255, 0,  0, 0, 255,   10, 20, 30,  40, 50, 60,  70, 80, 90];
    using var decoded = LinuxScreenCapture.DecodePpm([.. header, .. pixels]);
    Check(decoded != null && decoded.Width == 3 && decoded.Height == 2, "ppm: size parsed (comment skipped)");
    Check(decoded != null && decoded.GetPixel(0, 0) == new SkiaSharp.SKColor(255, 0, 0) && decoded.GetPixel(2, 0) == new SkiaSharp.SKColor(0, 0, 255)
        && decoded.GetPixel(1, 1) == new SkiaSharp.SKColor(40, 50, 60) && decoded.GetPixel(2, 1).Alpha == 255, "ppm: RGB mapped to opaque BGRA");
    Check(LinuxScreenCapture.DecodePpm([.. header, 1, 2, 3]) == null, "ppm: truncated data is rejected");
    Check(LinuxScreenCapture.DecodePpm(System.Text.Encoding.ASCII.GetBytes("P5\n1 1\n255\nx")) == null && LinuxScreenCapture.DecodePpm([]) == null, "ppm: other formats and empty input are rejected");
}

// ---- rendering policy: software (no GPU driver in the process) except on GNOME/X11, overridable
{
    string?[] saved = [Environment.GetEnvironmentVariable("SHAREX_RENDERING"), Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")];
    bool Policy(string? rendering, string? session, string? desktop)
    {
        Environment.SetEnvironmentVariable("SHAREX_RENDERING", rendering); Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", session);
        Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", desktop); Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        return ShareX.AvaloniaUI.Integration.AvaloniaBootstrapper.UseSoftwareRendering();
    }
    Check(Policy(null, "wayland", "Hyprland") && Policy(null, "wayland", "niri") && Policy(null, "wayland", "KDE"), "rendering: software by default on non-GNOME Wayland");
    Check(!Policy(null, "wayland", "ubuntu:GNOME") && !Policy(null, "x11", "KDE") && !Policy(null, null, null), "rendering: GPU kept on GNOME and X11 sessions");
    Check(Policy("software", "x11", "GNOME") && !Policy("gpu", "wayland", "Hyprland"), "rendering: SHAREX_RENDERING overrides the default");
    Environment.SetEnvironmentVariable("SHAREX_RENDERING", saved[0]); Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", saved[1]);
    Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", saved[2]); Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", saved[3]);
}

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
  { "id": 2, "name": "HDMI-A-1", "enabled": true, "connected": true, "primary": false, "pos": { "x": 2560, "y": 0 }, "size": { "width": 1920, "height": 1080 }, "scale": 1.0, "rotation": 2 },
  { "id": 3, "name": "DP-1", "enabled": false, "connected": true, "primary": false, "pos": { "x": 0, "y": 0 }, "size": { "width": 1920, "height": 1080 }, "scale": 1.0, "rotation": 1 } ],
  "screen": {} }
""";
var kde = LinuxScreenCapture.ParseKScreenDoctor(kscreen);
Check(kde.Length == 2, "kde: disabled output ignored");
Check(kde[0].Bounds == new System.Drawing.Rectangle(0, 0, 2560, 1440) && kde[0].Primary, "kde: physical size divided by the scale");
Check(kde[1].Bounds == new System.Drawing.Rectangle(2560, 0, 1920, 1080), "kde: transformed output size is not rotated twice");

// ---- layer-shell bars (hyprctl layers -j)
const string layers = """
{ "DP-3": { "levels": { "0": [ { "x": 0, "y": 0, "w": 2560, "h": 1440, "namespace": "awww-daemon" } ], "1": [],
    "2": [ { "x": 8, "y": 6, "w": 2544, "h": 30, "namespace": "waybar" } ],
    "3": [ { "x": 2060, "y": 0, "w": 500, "h": 1440, "namespace": "swaync-notification-window" } ] } },
  "DP-2": { "levels": { "0": [], "1": [], "2": [ { "x": 2560, "y": 1400, "w": 2560, "h": 40, "namespace": "bottom-bar" } ], "3": [] } } }
""";
Check(LayerShellInsets.ParseHyprlandTopInset(layers, new System.Drawing.Rectangle(0, 0, 2560, 1440)) == 36, "bars: waybar (incl. its margin) is the top inset; the tall notification column is ignored");
Check(LayerShellInsets.ParseHyprlandTopInset(layers, new System.Drawing.Rectangle(2560, 0, 2560, 1440)) == 0, "bars: a bottom bar does not move the toolbar");
Check(LayerShellInsets.ParseHyprlandTopInset("{}", new System.Drawing.Rectangle(0, 0, 100, 100)) == 0, "bars: nothing reported");

// ---- niri layers (no geometry available)
const string niriLayers = """
[ {"namespace":"waybar","output":"DP-3","layer":"Top","keyboard_interactivity":"None"},
  {"namespace":"swaync-notification-window","output":"DP-2","layer":"Overlay","keyboard_interactivity":"None"},
  {"namespace":"awww-daemon","output":"DP-2","layer":"Background","keyboard_interactivity":"None"} ]
""";
Check(LayerShellInsets.ParseNiriHasTopBar(niriLayers, "DP-3"), "niri layers: waybar on the top layer is a bar");
Check(!LayerShellInsets.ParseNiriHasTopBar(niriLayers, "DP-2"), "niri layers: a notification daemon and the wallpaper are not bars");
Check(!LayerShellInsets.ParseNiriHasTopBar("[]", "DP-1"), "niri layers: nothing on the layers");

// ---- sway: the visible workspace excludes bars and nags
const string swayWorkspaces = """
[ { "name": "1", "visible": true, "focused": true, "output": "DP-1", "rect": { "x": 0, "y": 60, "width": 2560, "height": 1380 } },
  { "name": "2", "visible": false, "focused": false, "output": "DP-1", "rect": { "x": 0, "y": 0, "width": 2560, "height": 1440 } },
  { "name": "3", "visible": true, "focused": false, "output": "DP-2", "rect": { "x": 2560, "y": 0, "width": 1920, "height": 1080 } } ]
""";
Check(LayerShellInsets.ParseSwayTopInset(swayWorkspaces, new System.Drawing.Rectangle(0, 0, 2560, 1440)) == 60, "sway: top bars = workspace top minus output top");
Check(LayerShellInsets.ParseSwayTopInset(swayWorkspaces, new System.Drawing.Rectangle(2560, 0, 1920, 1080)) == 0, "sway: an output without bars has no inset");

// ---- niri focused output from the workspace list (three outputs, the right one focused)
const string niriWs = """
[ {"id":1,"idx":1,"output":"DVI-D-1","is_focused":false,"is_active":true}, {"id":2,"idx":2,"output":"DP-1","is_focused":false,"is_active":true},
  {"id":6,"idx":1,"output":"HDMI-A-1","is_focused":true,"is_active":true}, {"id":7,"idx":2,"output":"HDMI-A-1","is_focused":false,"is_active":false} ]
""";
Check(LinuxScreenCapture.ParseNiriFocusedOutput(niriWs) == "HDMI-A-1", "niri: focused output comes from the focused workspace");
Check(LinuxScreenCapture.ParseNiriFocusedOutput("[]") == null, "niri: no workspaces, no focused output");

// ---- DoctorDisplays: xrandr --listmonitors dual-monitor XWayland fixture
const string xrandrDual = """
WARNING: running xrandr against an Xwayland server. See the xrandr man page for details.
Monitors: 2
 0: +*DP-1 2560/600x1440/340+0+0  DP-1
 1: +DP-2 1920/540x1080/300+2560+0  DP-2
""";
var xMonitors = DoctorDisplays.ParseXRandRListMonitors(xrandrDual);
Check(xMonitors.Length == 2, "xrandr: parsed two monitors ignoring warning");
Check(xMonitors[0].Name == "DP-1" && xMonitors[0].Bounds == new System.Drawing.Rectangle(0, 0, 2560, 1440) && xMonitors[0].Primary, "xrandr: primary monitor bounds and flag");
Check(xMonitors[1].Name == "DP-2" && xMonitors[1].Bounds == new System.Drawing.Rectangle(2560, 0, 1920, 1080) && !xMonitors[1].Primary, "xrandr: second monitor bounds and primary=false");

// ---- DoctorDisplays: kscreen-doctor JSON dual-monitor layout
const string kscreenDoctorDual = """
{
  "outputs": [
    { "id": 1, "name": "eDP-1", "enabled": true, "connected": true, "primary": true, "pos": { "x": 0, "y": 0 }, "size": { "width": 1920, "height": 1080 }, "scale": 1.0, "rotation": 1 },
    { "id": 2, "name": "HDMI-A-1", "enabled": true, "connected": true, "primary": false, "pos": { "x": 1920, "y": 0 }, "size": { "width": 3840, "height": 2160 }, "scale": 2.0, "rotation": 1 },
    { "id": 3, "name": "DP-1", "enabled": false, "connected": false, "primary": false, "pos": { "x": 0, "y": 0 }, "size": { "width": 1920, "height": 1080 }, "scale": 1.0, "rotation": 1 }
  ]
}
""";
var kdeMonitors = DoctorDisplays.ParseKScreenDoctorJson(kscreenDoctorDual);
Check(kdeMonitors.Length == 2, "kscreen json: parsed 2 enabled outputs");
Check(kdeMonitors[0].Name == "eDP-1" && kdeMonitors[0].Bounds == new System.Drawing.Rectangle(0, 0, 1920, 1080) && kdeMonitors[0].Primary, "kscreen json: primary output");
Check(kdeMonitors[1].Name == "HDMI-A-1" && kdeMonitors[1].Bounds == new System.Drawing.Rectangle(1920, 0, 1920, 1080) && kdeMonitors[1].Scale == 2.0, "kscreen json: scaled output logical bounds");

// ---- DoctorDisplays: pointer monitor resolution & disagreement detection
var pOnFirst = new System.Drawing.Point(500, 500);
var pOnSecond = new System.Drawing.Point(2500, 500);
var pOutside = new System.Drawing.Point(9000, 9000);
Check(DoctorDisplays.FindMonitorForPoint(kdeMonitors, pOnFirst)?.Name == "eDP-1", "pointer: pOnFirst falls on eDP-1");
Check(DoctorDisplays.FindMonitorForPoint(kdeMonitors, pOnSecond)?.Name == "HDMI-A-1", "pointer: pOnSecond falls on HDMI-A-1");
Check(DoctorDisplays.FindMonitorForPoint(kdeMonitors, pOutside) == null, "pointer: pOutside is outside any monitor");

// Pointer disagreement: X11 thinks cursor is on eDP-1, compositor knows it is on HDMI-A-1
var verdictsDisagreed = DoctorDisplays.EvaluateVerdicts(kdeMonitors, null, pOnFirst, pOnSecond);
Check(verdictsDisagreed.Any(v => v.Contains("pointer") && v.Contains("differs")), "pointer: flags monitor disagreement in verdicts");

// Pointer agreement: identical coords
var verdictsAgreed = DoctorDisplays.EvaluateVerdicts(kdeMonitors, null, pOnFirst, pOnFirst);
Check(verdictsAgreed.Count == 0, "pointer: no false warning when positions agree");

// Monitor layout mismatch detection between XWayland and compositor
const string xrandrMismatched = """
Monitors: 2
 0: +DP-1 1920x1080+0+0  eDP-1
 1: +HDMI-1 1920x1080+0+1080  HDMI-A-1
""";
var xMismatched = DoctorDisplays.ParseXRandRListMonitors(xrandrMismatched);
var verdictsMismatch = DoctorDisplays.EvaluateVerdicts(kdeMonitors, xMismatched, null, null);
Check(verdictsMismatch.Any(v => v.Contains("layout mismatch")), "verdicts: flags monitor layout mismatch between XWayland and compositor");
KdeChecks.Run(Check);
await KdePlacementChecks.RunAsync(Check);
if (args.Contains("--fake-kwin")) await KWinDbusChecks.RunAsync(Check);

Console.WriteLine($"RESULT: {failures} failures");
return failures == 0 ? 0 : 1;
