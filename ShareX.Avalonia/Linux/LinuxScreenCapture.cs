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

using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>All desktop rectangles are compositor logical coordinates. Bitmap pixels are explicit.</summary>
public sealed record LinuxCaptureMonitor(string Name, Rectangle Bounds, double Scale, bool Primary);

public static class LinuxScreenCapture
{
    public static bool IsWayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) &&
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "x11";
    private static readonly object MonitorLock = new();
    private static LinuxCaptureMonitor[]? _monitors;
    private static long _monitorTime;

    private static string? _monitorSource;

    public static string? MonitorSource
    {
        get
        {
            lock (MonitorLock)
            {
                if (_monitors == null) GetMonitors();
                return _monitorSource;
            }
        }
    }

    public static LinuxCaptureMonitor[] GetMonitors()
    {
        lock (MonitorLock)
        {
            if (_monitors != null && Environment.TickCount64 - _monitorTime < 1000) return _monitors;
            LinuxCaptureMonitor[] monitors = ReadMonitors(out _monitorSource);
            _monitors = monitors;
            _monitorTime = Environment.TickCount64;
            return monitors;
        }
    }

    public static LinuxCaptureMonitor[] GetMonitors(out string source)
    {
        var monitors = GetMonitors();
        source = _monitorSource ?? "unknown";
        return monitors;
    }

    public static LinuxCaptureMonitor[] ParseHyprlandMonitors(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Where(m => !m.TryGetProperty("disabled", out var disabled) || !disabled.GetBoolean())
            .Select(m =>
            {
                double scale = m.GetProperty("scale").GetDouble();
                if (!double.IsFinite(scale) || scale <= 0) scale = 1;
                int width = m.GetProperty("width").GetInt32(), height = m.GetProperty("height").GetInt32();
                int transform = m.TryGetProperty("transform", out var t) ? t.GetInt32() : 0;
                if ((transform & 1) != 0) (width, height) = (height, width);
                return new LinuxCaptureMonitor(m.GetProperty("name").GetString()!,
                    new Rectangle(m.GetProperty("x").GetInt32(), m.GetProperty("y").GetInt32(),
                        (int)Math.Round(width / scale), (int)Math.Round(height / scale)), scale,
                    m.TryGetProperty("focused", out var f) && f.GetBoolean());
            }).ToArray();
    }

    /// <summary>`niri msg --json outputs`: a map of output name to an object with a "logical" rectangle and scale.</summary>
    public static LinuxCaptureMonitor[] ParseNiriOutputs(string json, string? focusedName = null)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<LinuxCaptureMonitor> monitors = [];

        foreach (JsonProperty output in document.RootElement.EnumerateObject())
        {
            // Disabled outputs have no logical rectangle.
            if (!output.Value.TryGetProperty("logical", out JsonElement logical) || logical.ValueKind != JsonValueKind.Object) continue;
            double scale = logical.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 1;
            if (!double.IsFinite(scale) || scale <= 0) scale = 1;
            string name = output.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : output.Name;
            monitors.Add(new LinuxCaptureMonitor(name, new Rectangle(logical.GetProperty("x").GetInt32(), logical.GetProperty("y").GetInt32(),
                logical.GetProperty("width").GetInt32(), logical.GetProperty("height").GetInt32()), scale, name == focusedName));
        }

        return monitors.ToArray();
    }

    /// <summary>`niri msg --json workspaces`: the focused workspace names the focused output.</summary>
    public static string? ParseNiriFocusedOutput(string workspacesJson)
    {
        using JsonDocument document = JsonDocument.Parse(workspacesJson);
        foreach (JsonElement ws in document.RootElement.EnumerateArray())
        {
            if (ws.TryGetProperty("is_focused", out var focused) && focused.ValueKind == JsonValueKind.True && ws.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String)
                return output.GetString();
        }

        return null;
    }

    /// <summary>`kscreen-doctor -j`: logical positions and transformed output sizes, with optional explicit logical sizes.</summary>
    public static LinuxCaptureMonitor[] ParseKScreenDoctor(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<LinuxCaptureMonitor> monitors = [];

        foreach (JsonElement output in document.RootElement.GetProperty("outputs").EnumerateArray())
        {
            if (output.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
            if (output.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.False) continue;
            if (!output.TryGetProperty("pos", out var pos) || !output.TryGetProperty("size", out var size)) continue;

            double scale = output.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 1;
            if (!double.IsFinite(scale) || scale <= 0) scale = 1;
            int width = size.GetProperty("width").GetInt32(), height = size.GetProperty("height").GetInt32();
            // KScreen Output::size is already transformed (including rotation). Do not rotate it twice.
            int logicalWidth = (int)Math.Ceiling(width / scale), logicalHeight = (int)Math.Ceiling(height / scale);
            if (output.TryGetProperty("explicitLogicalSize", out var logical) && logical.ValueKind == JsonValueKind.Object &&
                logical.TryGetProperty("width", out var lw) && logical.TryGetProperty("height", out var lh) &&
                lw.GetDouble() > 0 && lh.GetDouble() > 0)
            {
                logicalWidth = (int)Math.Ceiling(lw.GetDouble());
                logicalHeight = (int)Math.Ceiling(lh.GetDouble());
            }
            if (logicalWidth <= 0 || logicalHeight <= 0) continue;

            monitors.Add(new LinuxCaptureMonitor(output.GetProperty("name").GetString()!,
                new Rectangle(pos.GetProperty("x").GetInt32(), pos.GetProperty("y").GetInt32(), logicalWidth, logicalHeight),
                scale, (output.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True) ||
                    (output.TryGetProperty("priority", out var priority) && priority.GetInt32() == 1)));
        }

        return monitors.ToArray();
    }

    private static LinuxCaptureMonitor[] ReadMonitors(out string source)
    {
        source = "unknown";
        if (IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")))
        {
            try
            {
                string focused = null;
                try { focused = ParseNiriFocusedOutput(LinuxCaptureProcess.Text("niri", "msg", "--json", "workspaces")); }
                catch (Exception) { }
                if (focused == null)
                {
                    try { using JsonDocument f = JsonDocument.Parse(LinuxCaptureProcess.Text("niri", "msg", "--json", "focused-output")); focused = f.RootElement.GetProperty("name").GetString(); }
                    catch (Exception) { }
                }
                LinuxCaptureMonitor[] monitors = ParseNiriOutputs(LinuxCaptureProcess.Text("niri", "msg", "--json", "outputs"), focused);
                if (monitors.Length > 0)
                {
                    source = "niri";
                    return monitors;
                }
            }
            catch (Exception) { }
        }
        else if (IsWayland && KdeCapture.IsSession)
        {
            try
            {
                LinuxCaptureMonitor[] monitors = ParseKScreenDoctor(LinuxCaptureProcess.Text("kscreen-doctor", "-j"));
                if (monitors.Length > 0)
                {
                    source = "kscreen-doctor";
                    return monitors;
                }
            }
            catch (Exception) { }
        }

        if (IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            source = "hyprctl";
            return ParseHyprlandMonitors(LinuxCaptureProcess.Text("hyprctl", "monitors", "-j"));
        }
        if (IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            using JsonDocument doc = JsonDocument.Parse(LinuxCaptureProcess.Text("swaymsg", "-t", "get_outputs", "-r"));
            source = "swaymsg";
            return doc.RootElement.EnumerateArray().Where(m => m.GetProperty("active").GetBoolean()).Select(m =>
            {
                var r = m.GetProperty("rect");
                return new LinuxCaptureMonitor(m.GetProperty("name").GetString()!,
                    new Rectangle(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(), r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32()),
                    m.GetProperty("scale").GetDouble(), m.GetProperty("focused").GetBoolean());
            }).ToArray();
        }
        // XRandR is also useful on generic Wayland through XWayland: positions are logical.
        try
        {
            string text = LinuxCaptureProcess.Text("xrandr", "--query");
            var matches = Regex.Matches(text, @"(?m)^(\S+) connected (primary )?(\d+)x(\d+)([+-]\d+)([+-]\d+)");
            if (matches.Count > 0)
            {
                source = "xrandr";
                return matches.Select(m => new LinuxCaptureMonitor(m.Groups[1].Value,
                    new Rectangle(int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture),
                        int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)), 1, m.Groups[2].Success)).ToArray();
            }
        }
        catch (Exception) { }
        source = "none";
        throw new PlatformNotSupportedException("Cannot discover displays. Install xrandr, or use Hyprland/sway compositor IPC.");
    }

    /// <summary>
    /// Uses compositor focus on niri or KWin's global pointer on KDE Wayland. XWayland pointer queries may be stale over native windows.
    /// If KWin scripting is unavailable, capture the entire desktop rather than silently choosing the wrong monitor.
    /// </summary>
    public static bool TryGetFocusedMonitor(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!IsWayland) return false;
        if (KdeCapture.IsSession)
        {
            Point? cursor = KdeCapture.CursorPositionAsync().GetAwaiter().GetResult();
            bounds = cursor is Point p ? GetMonitors().FirstOrDefault(m => m.Bounds.Contains(p))?.Bounds ?? DesktopBounds : DesktopBounds;
            return true;
        }
        if (!NiriIpc.IsAvailable) return false;

        try
        {
            LinuxCaptureMonitor? focused = GetMonitors().FirstOrDefault(m => m.Primary);
            if (focused == null) return false;
            bounds = focused.Bounds;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Rectangle DesktopBounds => GetMonitors().Select(m => m.Bounds).Aggregate(Rectangle.Union);

    public static Point CursorPosition
    {
        get
        {
            if (IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            {
                using JsonDocument doc = JsonDocument.Parse(LinuxCaptureProcess.Text("hyprctl", "cursorpos", "-j"));
                return new Point((int)doc.RootElement.GetProperty("x").GetDouble(), (int)doc.RootElement.GetProperty("y").GetDouble());
            }
            if (IsWayland && KdeCapture.IsSession)
            {
                Point? cursor = KdeCapture.CursorPositionAsync().GetAwaiter().GetResult();
                if (cursor != null) return cursor.Value;
                // No global pointer API is available: use a safe toolbar anchor, never a stale XWayland pointer.
                Rectangle primary = GetMonitors().FirstOrDefault(m => m.Primary)?.Bounds ?? DesktopBounds;
                return new Point(primary.X + primary.Width / 2, primary.Y + primary.Height / 2);
            }
            return X11Capture.GetCursorPosition();
        }
    }

    public static void MoveCursor(Point point)
    {
        if (IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            HyprlandCaptureIpc.Dispatch("movecursor", FormattableString.Invariant($"{point.X} {point.Y}"),
                FormattableString.Invariant($"hl.dsp.cursor.move({{x={point.X},y={point.Y}}})"));
        else if (!IsWayland) X11Capture.MoveCursor(point);
    }

    /// <summary>Captures at the highest intersecting output scale, preserving every source pixel.
    /// Lower density outputs are resampled only when a capture crosses mixed-scale outputs.</summary>
    public static SKBitmap Capture(Rectangle rectangle, bool cursor = false)
    {
        rectangle = Rectangle.Intersect(DesktopBounds, rectangle);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) throw new ArgumentOutOfRangeException(nameof(rectangle), "The capture area is outside the desktop.");
        if (!IsWayland) return X11Capture.Capture(rectangle, cursor);
        var intersecting = GetMonitors().Where(m => m.Bounds.IntersectsWith(rectangle)).ToArray();
        double scale = intersecting.Length > 0 ? intersecting.Max(m => m.Scale) : 1.0;
        List<string> args = ["-t", "png", "-s", scale.ToString(CultureInfo.InvariantCulture), "-g",
            FormattableString.Invariant($"{rectangle.X},{rectangle.Y} {rectangle.Width}x{rectangle.Height}")];
        if (cursor) args.Add("-c");
        args.Add("-");
        try
        {
            return SKBitmap.Decode(LinuxCaptureProcess.Run("grim", args.ToArray())) ?? throw new InvalidOperationException("grim returned an invalid PNG.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // GNOME/KDE do not expose wlr-screencopy. Portal permission belongs to the compositor.
            using SKBitmap desktop = ScreenshotPortal.CaptureAsync().GetAwaiter().GetResult();
            return CropDesktop(desktop, DesktopBounds, rectangle);
        }
    }

    public static SKBitmap CropDesktop(SKBitmap desktop, Rectangle bounds, Rectangle rectangle)
    {
        double sx = (double)desktop.Width / bounds.Width, sy = (double)desktop.Height / bounds.Height;
        SKRectI crop = new((int)Math.Floor((rectangle.Left - bounds.Left) * sx), (int)Math.Floor((rectangle.Top - bounds.Top) * sy),
            (int)Math.Ceiling((rectangle.Right - bounds.Left) * sx), (int)Math.Ceiling((rectangle.Bottom - bounds.Top) * sy));
        SKBitmap result = new(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (!desktop.ExtractSubset(result, crop)) { result.Dispose(); throw new InvalidOperationException("The selected region is outside the portal screenshot."); }
        return result;
    }
}
