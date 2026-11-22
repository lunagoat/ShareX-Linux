// ShareX - GPL v3. See LICENSE.txt.
using Avalonia.Controls;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using ShareX.HelpersLib;
using ShareX.AvaloniaUI.Linux;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShareX.ScreenCaptureLib;

/// <summary>XShape keeps recording outlines hollow and passes input through their aperture.</summary>
public static class RecordingWindowPlatform
{
    /// <summary>Hyprland, where <see cref="Place"/> positions windows in compositor (logical) pixels.</summary>
    public static bool IsHyprland => OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public short X, Y;
        public ushort Width, Height;
        public Rect(int x, int y, int width, int height)
        { X = (short)x; Y = (short)y; Width = (ushort)Math.Max(0, width); Height = (ushort)Math.Max(0, height); }
    }

    /// <summary>Rectangles are in the window's X pixels (DIPs times RenderScaling).</summary>
    public static void Shape(Window window, Rect[] rectangles, bool clickThrough = false)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "XID" || handle.Handle == IntPtr.Zero) return;
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) return;
        try
        {
            // XWayland does not pass a window's bounding shape on to the Wayland compositor, which shows the whole
            // window buffer; the shape only stops the transparent background from being painted, so the area outside
            // it showed as solid black. XWayland windows are always composited, so the transparent background alone
            // keeps the outline hollow there; the input shape still lets clicks through.
            if (!LinuxScreenCapture.IsWayland)
            {
                XShapeCombineRectangles(display, handle.Handle, 0, 0, 0, rectangles, rectangles.Length, 0, 0);
            }
            XShapeCombineRectangles(display, handle.Handle, 2, 0, 0, clickThrough ? [] : rectangles, clickThrough ? 0 : rectangles.Length, 0, 0);
            XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }
    /// <summary>
    /// X pixels per compositor (logical) pixel for a recording window over <paramref name="region"/>. Hyprland with
    /// xwayland:force_zero_scaling gives XWayland windows native pixels, so a window 100 logical pixels wide on a 1.5×
    /// monitor is 150 X pixels wide, and content laid out for 100 left the rest of the window black. Elsewhere the
    /// compositor scales XWayland windows itself and this is 1.
    /// </summary>
    public static double WindowPixelsPerLogicalPixel(DrawingRectangle region)
    {
        if (!IsHyprland) return 1;
        try
        {
            using JsonDocument option = JsonDocument.Parse(LinuxCaptureProcess.Text("hyprctl", "getoption", "xwayland:force_zero_scaling", "-j"));
            // Hyprland 0.57 reports {"bool": true}; older versions {"int": 1}.
            JsonElement root = option.RootElement;
            bool zeroScaling = root.TryGetProperty("bool", out JsonElement flag) ? flag.ValueKind == JsonValueKind.True
                : root.TryGetProperty("int", out JsonElement number) && number.ValueKind == JsonValueKind.Number && number.GetInt64() != 0;
            if (!zeroScaling) return 1;
            System.Drawing.Point center = new(region.X + region.Width / 2, region.Y + region.Height / 2);
            LinuxCaptureMonitor[] monitors = LinuxScreenCapture.GetMonitors();
            LinuxCaptureMonitor? monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(center)) ??
                monitors.OrderByDescending(m => Area(DrawingRectangle.Intersect(m.Bounds, region))).FirstOrDefault();
            return monitor != null && double.IsFinite(monitor.Scale) && monitor.Scale > 0 ? monitor.Scale : 1;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine("Recording overlay scale: " + ex.Message);
            return 1;
        }

        static long Area(DrawingRectangle rectangle) => (long)rectangle.Width * rectangle.Height;
    }

    /// <summary>
    /// Position only this process's own recording overlay on Hyprland, without persistent rules. Coordinates are
    /// compositor (logical) pixels. <paramref name="seeThrough"/> turns off the blur, shadow, border and rounding
    /// that Hyprland would otherwise draw over the area being recorded.
    /// </summary>
    public static async Task Place(Window window, int x, int y, int width, int height, bool seeThrough = false)
    {
        if (!IsHyprland) return;
        string title = window.Title ?? "";
        try
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(attempt == 0 ? 80 : 100);
                string text = await Command("clients", "-j");
                using JsonDocument clients = JsonDocument.Parse(text);
                JsonElement? match = null;
                foreach (JsonElement client in clients.RootElement.EnumerateArray())
                {
                    if (client.GetProperty("pid").GetInt32() != Environment.ProcessId) continue;
                    if (!string.IsNullOrEmpty(title) && client.GetProperty("title").GetString() != title) continue;
                    match = client;
                    break;
                }
                if (match is not JsonElement found) continue;

                // Hyprland with a Lua config (hyprland.lua) only accepts hl.dsp.* dispatchers; the helper picks the form.
                string selector = "address:" + found.GetProperty("address").GetString();
                string target = HyprlandCaptureIpc.Quote(selector);
                int currentMonitor = found.TryGetProperty("monitor", out JsonElement monitorId) ? monitorId.GetInt32() : -1;
                // Hyprland keeps a floating window on the monitor it appeared on when it is moved by position, and
                // sizes an XWayland window for that monitor's scale; send it to the target monitor's workspace first.
                (int Id, string Workspace)? targetMonitor = FindMonitor(await Command("monitors", "-j"), x + width / 2, y + height / 2);
                await Task.Run(() =>
                {
                    HyprlandCaptureIpc.Dispatch("setfloating", selector, $"hl.dsp.window.float({{action=\"enable\",window={target}}})");
                    if (seeThrough)
                    {
                        foreach ((string prop, string value) in new[] { ("no_blur", "1"), ("no_shadow", "1"), ("border_size", "0"), ("rounding", "0"), ("no_anim", "1"), ("no_dim", "1") })
                        {
                            try
                            {
                                HyprlandCaptureIpc.Dispatch("setprop", $"{selector} {prop} {value}",
                                    $"hl.dsp.window.set_prop({{prop=\"{prop}\",value=\"{value}\",window={target}}})");
                            }
                            catch (Exception ex) { DebugHelper.WriteLine($"Recording overlay property {prop}: " + ex.Message); }
                        }
                    }
                    if (targetMonitor is { } monitor && monitor.Id != currentMonitor && monitor.Workspace.Length > 0)
                    {
                        HyprlandCaptureIpc.Dispatch("movetoworkspacesilent", $"{monitor.Workspace},{selector}",
                            $"hl.dsp.window.move({{workspace={HyprlandCaptureIpc.Quote(monitor.Workspace)},follow=false,window={target}}})");
                    }
                    HyprlandCaptureIpc.Dispatch("movewindowpixel", $"exact {x} {y},{selector}",
                        $"hl.dsp.window.move({{x={x},y={y},relative=false,window={target}}})");
                    if (width >= 10 && height >= 10)
                    {
                        HyprlandCaptureIpc.Dispatch("resizewindowpixel", $"exact {width} {height},{selector}",
                            $"hl.dsp.window.resize({{x={width},y={height},relative=false,window={target}}})");
                    }
                    HyprlandCaptureIpc.Dispatch("movewindowpixel", $"exact {x} {y},{selector}",
                        $"hl.dsp.window.move({{x={x},y={y},relative=false,window={target}}})");
                });
                break;
            }
        }
        catch (Exception ex) { DebugHelper.WriteLine("Recording overlay placement: " + ex.Message); }
    }

    /// <summary>The Hyprland monitor (id and active workspace) containing a logical point.</summary>
    private static (int Id, string Workspace)? FindMonitor(string monitorsJson, int x, int y)
    {
        using JsonDocument monitors = JsonDocument.Parse(monitorsJson);
        foreach (JsonElement monitor in monitors.RootElement.EnumerateArray())
        {
            double scale = monitor.GetProperty("scale").GetDouble();
            if (!double.IsFinite(scale) || scale <= 0) scale = 1;
            int width = monitor.GetProperty("width").GetInt32(), height = monitor.GetProperty("height").GetInt32();
            if (monitor.TryGetProperty("transform", out JsonElement transform) && (transform.GetInt32() & 1) != 0) (width, height) = (height, width);
            int left = monitor.GetProperty("x").GetInt32(), top = monitor.GetProperty("y").GetInt32();
            if (x < left || y < top || x >= left + width / scale || y >= top + height / scale) continue;
            string workspace = monitor.TryGetProperty("activeWorkspace", out JsonElement active) && active.TryGetProperty("id", out JsonElement id)
                ? id.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            return (monitor.GetProperty("id").GetInt32(), workspace);
        }
        return null;
    }

    private static async Task<string> Command(params string[] args)
    {
        ProcessStartInfo info = new("hyprctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return await output;
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, [In] Rect[] rectangles, int count, int operation, int ordering);
}
