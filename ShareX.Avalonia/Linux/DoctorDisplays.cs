// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>
/// Read-only display and pointer diagnostics for multi-monitor troubleshooting.
/// Evaluates monitor geometry, cross-checks independent views (XWayland, kscreen-doctor, wlr-randr),
/// inspects pointer coordinates for stale XWayland offsets, and diagnoses overlay placement.
/// </summary>
public static class DoctorDisplays
{
    public static string? FindExecutable(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);

    /// <summary>Parses the output of `xrandr --listmonitors` (what X/XWayland reports).</summary>
    public static LinuxCaptureMonitor[] ParseXRandRListMonitors(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];
        List<LinuxCaptureMonitor> monitors = [];
        var matches = Regex.Matches(output, @"(?m)^\s*\d+:\s*([+*]+)\s*(\S+)?\s+(\d+)(?:/\d+)?x(\d+)(?:/\d+)?\+([+-]?\d+)\+([+-]?\d+)\s+(\S+)");
        foreach (Match m in matches)
        {
            string flags = m.Groups[1].Value;
            bool primary = flags.Contains('*');
            int width = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int height = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            int x = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            int y = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
            string name = m.Groups[7].Value;
            monitors.Add(new LinuxCaptureMonitor(name, new Rectangle(x, y, width, height), 1.0, primary));
        }
        return monitors.ToArray();
    }

    /// <summary>`kscreen-doctor -j` (KDE Plasma JSON format).</summary>
    public static LinuxCaptureMonitor[] ParseKScreenDoctorJson(string json)
    {
        return LinuxScreenCapture.ParseKScreenDoctor(json);
    }

    /// <summary>`kscreen-doctor -o` (KDE Plasma human-readable format).</summary>
    public static LinuxCaptureMonitor[] ParseKScreenDoctorOutputs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        List<LinuxCaptureMonitor> monitors = [];
        var matches = Regex.Matches(text, @"(?m)^Output:\s*\d+\s+(\S+)\s+(.*?Geometry:\s*([+-]?\d+),([+-]?\d+)\s+(\d+)x(\d+).*)");
        foreach (Match m in matches)
        {
            string name = m.Groups[1].Value;
            string rest = m.Groups[2].Value;
            if (!rest.Contains("enabled", StringComparison.OrdinalIgnoreCase) || !rest.Contains("connected", StringComparison.OrdinalIgnoreCase)) continue;
            int x = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int y = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            int w = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            int h = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);

            var scaleMatch = Regex.Match(rest, @"Scale:\s*([0-9.]+)");
            double scale = scaleMatch.Success && double.TryParse(scaleMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sc) && sc > 0 ? sc : 1.0;
            bool primary = rest.Contains("primary", StringComparison.OrdinalIgnoreCase);

            int logW = (int)Math.Round(w / scale);
            int logH = (int)Math.Round(h / scale);
            monitors.Add(new LinuxCaptureMonitor(name, new Rectangle(x, y, logW, logH), scale, primary));
        }
        return monitors.ToArray();
    }

    /// <summary>Parses `wlr-randr` output blocks.</summary>
    public static LinuxCaptureMonitor[] ParseWlrRandr(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];
        List<LinuxCaptureMonitor> monitors = [];
        string[] blocks = Regex.Split(output.Trim(), @"(?m)^(?=\S)");
        foreach (string block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            var lines = block.Trim().Split('\n');
            var firstLine = lines[0].Trim();
            var nameMatch = Regex.Match(firstLine, @"^(\S+)");
            if (!nameMatch.Success) continue;
            string name = nameMatch.Groups[1].Value;
            if (!block.Contains("Enabled: yes", StringComparison.OrdinalIgnoreCase)) continue;

            var posMatch = Regex.Match(block, @"Position:\s*([+-]?\d+),([+-]?\d+)");
            int x = posMatch.Success ? int.Parse(posMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            int y = posMatch.Success ? int.Parse(posMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0;

            var scaleMatch = Regex.Match(block, @"Scale:\s*([0-9.]+)");
            double scale = scaleMatch.Success && double.TryParse(scaleMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sc) && sc > 0 ? sc : 1.0;

            var modeMatch = Regex.Match(block, @"(\d+)x(\d+)\s*px.*\(current\)");
            if (!modeMatch.Success) continue;
            int w = int.Parse(modeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            int h = int.Parse(modeMatch.Groups[2].Value, CultureInfo.InvariantCulture);

            int logW = (int)Math.Round(w / scale);
            int logH = (int)Math.Round(h / scale);
            monitors.Add(new LinuxCaptureMonitor(name, new Rectangle(x, y, logW, logH), scale, false));
        }
        return monitors.ToArray();
    }

    /// <summary>Finds which monitor contains the given coordinates.</summary>
    public static LinuxCaptureMonitor? FindMonitorForPoint(IEnumerable<LinuxCaptureMonitor> monitors, Point pt)
    {
        return monitors.FirstOrDefault(m => m.Bounds.Contains(pt));
    }

    public static string FormatMonitor(LinuxCaptureMonitor m)
    {
        return $"{m.Name}: {m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height} scale {m.Scale:0.##}{(m.Primary ? " [primary]" : "")}";
    }

    public static string FormatSession()
    {
        var vars = new[] { "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP", "DESKTOP_SESSION", "WAYLAND_DISPLAY", "DISPLAY", "KDE_FULL_SESSION" };
        var parts = vars
            .Select(v => (Key: v, Value: Environment.GetEnvironmentVariable(v)))
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => $"{x.Key}={x.Value}");
        string sessionStr = string.Join("  ", parts);
        string appMode = (LinuxScreenCapture.IsWayland || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) ? "ShareX runs on XWayland" : "ShareX runs on X11";
        return $"{sessionStr}  ({appMode})";
    }

    public static string DetectWindowPlacementPath()
    {
        if (KdeCapture.IsSession) return "kde-kwin";
        if (NiriIpc.IsAvailable) return "niri";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return "sway";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return "hyprland";
        return "none";
    }

    public static List<string> FormatKdeGeometry(string? json)
    {
        List<string> lines = [];
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json ?? "");
            foreach (var output in doc.RootElement.GetProperty("outputs").EnumerateArray())
                lines.Add($"KWin {output.GetProperty("name").GetString()}: full {Rect(output.GetProperty("full"))}; workarea {Rect(output.GetProperty("work"))}");
            foreach (var panel in doc.RootElement.GetProperty("panels").EnumerateArray())
                lines.Add($"KWin panel: {Rect(panel.GetProperty("geometry"))}; keepAbove={panel.GetProperty("keepAbove")}; keepBelow={panel.GetProperty("keepBelow")}");
            if (lines.Count > 0) return lines;
        }
        catch (Exception) { }
        return ["KWin workarea/panels: unavailable (scripting denied, unsupported API, or no session bus); kscreen-doctor reports full outputs only"];

        static string Rect(JsonElement r) => $"{r.GetProperty("x")},{r.GetProperty("y")} {r.GetProperty("width")}x{r.GetProperty("height")}";
    }

    public static List<string> QueryKdeGeometry()
    {
        var lines = FormatKdeGeometry(KdeCapture.DesktopGeometryAsync().GetAwaiter().GetResult());
        if (!lines[0].Contains("unavailable")) return lines;
        // KWin has no public qdbus workarea getter. supportInformation can still expose screen geometry
        // when scripting is forbidden. Never use queryWindowInfo: it starts an interactive picker.
        string? qdbus = FindExecutable("qdbus6") ?? FindExecutable("qdbus");
        string? support = qdbus == null ? null : LinuxCaptureProcess.TryText(qdbus, TimeSpan.FromSeconds(2),
            "org.kde.KWin", "/KWin", "org.kde.KWin.supportInformation");
        if (!string.IsNullOrWhiteSpace(support))
            foreach (string line in support.Split('\n').Where(l => l.Contains("Geometry:", StringComparison.OrdinalIgnoreCase)))
                lines.Add("KWin screen (qdbus supportInformation): " + line.Trim());
        return lines;
    }

    public static string CheckInstalledTools()
    {
        var tools = new[] { "grim", "kscreen-doctor", "xrandr" };
        return string.Join(", ", tools.Select(t => $"{t} ({(FindExecutable(t) != null ? "installed" : "not installed")})"));
    }

    public static (Point? Point, string Description) QueryX11Pointer()
    {
        try
        {
            Point pt = X11Capture.GetCursorPosition();
            return (pt, $"{pt.X},{pt.Y}");
        }
        catch (Exception ex)
        {
            return (null, "not available (" + ex.Message.Split('\n')[0] + ")");
        }
    }

    public static (Point? Point, string Description) QueryCompositorPointer()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            string? json = LinuxCaptureProcess.TryText("hyprctl", TimeSpan.FromSeconds(2), "cursorpos", "-j");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    int x = (int)Math.Round(doc.RootElement.GetProperty("x").GetDouble());
                    int y = (int)Math.Round(doc.RootElement.GetProperty("y").GetDouble());
                    return (new Point(x, y), $"{x},{y} (via hyprctl cursorpos)");
                }
                catch { }
            }
            return (null, "not available (hyprctl cursorpos failed)");
        }

        if ((Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KDE_FULL_SESSION")))
        {
            string? qdbus = FindExecutable("qdbus6") ?? FindExecutable("qdbus");
            if (qdbus == null)
            {
                return (null, "not available (neither qdbus6 nor qdbus installed)");
            }

            string? methods = LinuxCaptureProcess.TryText(qdbus, TimeSpan.FromSeconds(2), "org.kde.KWin", "/KWin");
            if (methods == null)
            {
                return (null, "not available (org.kde.KWin D-Bus service not reachable)");
            }

            var candidate = methods.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("cursorpos", StringComparison.OrdinalIgnoreCase));
            if (candidate != null)
            {
                string methodName = candidate.Split(' ').Last().Split('(')[0];
                string? result = LinuxCaptureProcess.TryText(qdbus, TimeSpan.FromSeconds(2), "org.kde.KWin", "/KWin", methodName);
                if (!string.IsNullOrEmpty(result))
                {
                    return (null, $"{result.Trim()} (via {methodName})");
                }
            }

            return (null, "not available (org.kde.KWin has no cursor position query)");
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            return (null, "not available (swaymsg has no cursor position query)");
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")))
        {
            return (null, "not available (niri has no cursor position query)");
        }

        if (!LinuxScreenCapture.IsWayland)
        {
            try
            {
                Point pt = X11Capture.GetCursorPosition();
                return (pt, $"{pt.X},{pt.Y} (native X11 session)");
            }
            catch { }
        }

        return (null, "not available (compositor query not supported)");
    }

    public static List<string> EvaluateVerdicts(
        IReadOnlyList<LinuxCaptureMonitor>? sharexMonitors,
        IReadOnlyList<LinuxCaptureMonitor>? xrandrMonitors,
        Point? x11Pt,
        Point? compPt)
    {
        List<string> verdicts = [];

        if (x11Pt.HasValue && compPt.HasValue)
        {
            var x11Mon = sharexMonitors != null ? FindMonitorForPoint(sharexMonitors, x11Pt.Value) : null;
            var compMon = sharexMonitors != null ? FindMonitorForPoint(sharexMonitors, compPt.Value) : null;

            if (x11Mon != null && compMon != null && x11Mon.Name != compMon.Name)
            {
                verdicts.Add($"WARNING: pointer position from X11 differs from the compositor's (X11 on {x11Mon.Name}, compositor on {compMon.Name} - stale XWayland pointer)");
            }
            else
            {
                int dx = Math.Abs(x11Pt.Value.X - compPt.Value.X);
                int dy = Math.Abs(x11Pt.Value.Y - compPt.Value.Y);
                if (dx > 10 || dy > 10)
                {
                    verdicts.Add($"WARNING: pointer position from X11 differs from the compositor's (X11: {x11Pt.Value.X},{x11Pt.Value.Y}; compositor: {compPt.Value.X},{compPt.Value.Y})");
                }
            }
        }

        if (xrandrMonitors != null && xrandrMonitors.Count > 0 && sharexMonitors != null && sharexMonitors.Count > 0)
        {
            if (xrandrMonitors.Count != sharexMonitors.Count)
            {
                verdicts.Add($"WARNING: monitor count from XWayland ({xrandrMonitors.Count}) differs from compositor ({sharexMonitors.Count})");
            }
            else
            {
                foreach (var xm in xrandrMonitors)
                {
                    var sm = sharexMonitors.FirstOrDefault(m => m.Name == xm.Name);
                    if (sm != null && sm.Bounds.Location != xm.Bounds.Location)
                    {
                        verdicts.Add($"WARNING: monitor layout mismatch: {xm.Name} is at {xm.Bounds.X},{xm.Bounds.Y} in XWayland but {sm.Bounds.X},{sm.Bounds.Y} in compositor");
                        break;
                    }
                }
            }
        }

        return verdicts;
    }

    public static List<string> GenerateReport()
    {
        List<string> lines = [];

        // 1. session
        lines.Add("session: " + FormatSession());

        // 2. monitors as ShareX sees them
        LinuxCaptureMonitor[]? monitors = null;
        string source = "unknown";
        try
        {
            monitors = LinuxScreenCapture.GetMonitors(out source);
        }
        catch (Exception ex)
        {
            lines.Add($"monitors (ShareX): not available ({ex.Message.Split('\n')[0]})");
        }

        if (monitors != null && monitors.Length > 0)
        {
            lines.Add($"monitors (ShareX via {source}):");
            foreach (var m in monitors)
            {
                lines.Add($"  {FormatMonitor(m)}");
            }
            Rectangle desktopBounds = monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            lines.Add($"  desktop bounds: {desktopBounds.X},{desktopBounds.Y} {desktopBounds.Width}x{desktopBounds.Height}");
        }

        // 3. Independent views for cross-checking
        string? xrandrOutput = LinuxCaptureProcess.TryText("xrandr", TimeSpan.FromSeconds(3), "--listmonitors");
        LinuxCaptureMonitor[]? xrandrMonitors = null;
        if (!string.IsNullOrEmpty(xrandrOutput))
        {
            xrandrMonitors = ParseXRandRListMonitors(xrandrOutput);
            if (xrandrMonitors.Length > 0)
            {
                lines.Add("xrandr --listmonitors (X/XWayland):");
                foreach (var m in xrandrMonitors)
                {
                    lines.Add($"  {m.Name}: {m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height}{(m.Primary ? " [primary]" : "")}");
                }
            }
        }
        else if (FindExecutable("xrandr") != null)
        {
            lines.Add("xrandr --listmonitors: not available");
        }

        if (FindExecutable("kscreen-doctor") != null)
        {
            string? kscreenJson = LinuxCaptureProcess.TryText("kscreen-doctor", TimeSpan.FromSeconds(3), "-j");
            LinuxCaptureMonitor[]? kdeMonitors = null;
            if (!string.IsNullOrEmpty(kscreenJson))
            {
                try { kdeMonitors = ParseKScreenDoctorJson(kscreenJson); } catch { }
            }
            if (kdeMonitors == null || kdeMonitors.Length == 0)
            {
                string? kscreenOut = LinuxCaptureProcess.TryText("kscreen-doctor", TimeSpan.FromSeconds(3), "-o");
                if (!string.IsNullOrEmpty(kscreenOut))
                {
                    kdeMonitors = ParseKScreenDoctorOutputs(kscreenOut);
                }
            }

            if (kdeMonitors != null && kdeMonitors.Length > 0)
            {
                lines.Add("kscreen-doctor:");
                foreach (var m in kdeMonitors)
                {
                    lines.Add($"  {FormatMonitor(m)}");
                }
            }
        }

        if (FindExecutable("wlr-randr") != null)
        {
            string? wlrOutput = LinuxCaptureProcess.TryText("wlr-randr", TimeSpan.FromSeconds(3));
            if (!string.IsNullOrEmpty(wlrOutput))
            {
                var wlrMonitors = ParseWlrRandr(wlrOutput);
                if (wlrMonitors.Length > 0)
                {
                    lines.Add("wlr-randr:");
                    foreach (var m in wlrMonitors)
                    {
                        lines.Add($"  {FormatMonitor(m)}");
                    }
                }
            }
        }

        // 4. pointer
        var (x11Pt, x11Desc) = QueryX11Pointer();
        var (compPt, compDesc) = QueryCompositorPointer();

        string x11MonName = (x11Pt.HasValue && monitors != null) ? FindMonitorForPoint(monitors, x11Pt.Value)?.Name ?? "outside bounds" : "?";
        string compMonName = (compPt.HasValue && monitors != null) ? FindMonitorForPoint(monitors, compPt.Value)?.Name ?? "outside bounds" : "?";

        lines.Add("pointer:");
        lines.Add($"  X11 query: {x11Desc}{(x11Pt.HasValue ? $" on {x11MonName}" : "")}");
        lines.Add($"  compositor: {compDesc}{(compPt.HasValue ? $" on {compMonName}" : "")}");

        if (x11Pt.HasValue && compPt.HasValue)
        {
            if (x11MonName != compMonName)
            {
                lines.Add($"  DISAGREE: X11 is on {x11MonName}, compositor is on {compMonName} (stale XWayland pointer)");
            }
            else if (Math.Abs(x11Pt.Value.X - compPt.Value.X) > 10 || Math.Abs(x11Pt.Value.Y - compPt.Value.Y) > 10)
            {
                lines.Add($"  DISAGREE: pointer coordinates differ by {Math.Sqrt(Math.Pow(x11Pt.Value.X - compPt.Value.X, 2) + Math.Pow(x11Pt.Value.Y - compPt.Value.Y, 2)):F0}px");
            }
        }

        // 5. Region selection, tools
        string placement = DetectWindowPlacementPath();
        lines.Add($"region selection: ShareX overlay (placement: {placement})");
        if (KdeCapture.IsSession)
        {
            lines.AddRange(QueryKdeGeometry());
        }

        lines.Add($"tools: {CheckInstalledTools()}");

        // 6. Verdict list
        var verdicts = EvaluateVerdicts(monitors, xrandrMonitors, x11Pt, compPt);
        if (verdicts.Count > 0)
        {
            lines.Add("verdict: " + string.Join(" | ", verdicts));
        }
        else
        {
            lines.Add("verdict: ok (no display or pointer anomalies detected)");
        }

        return lines;
    }

    public static void Print(Action<string> info)
    {
        foreach (string line in GenerateReport())
        {
            info(line);
        }
    }
}
