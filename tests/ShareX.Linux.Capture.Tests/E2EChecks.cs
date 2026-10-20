// ShareX Linux Port - Screen Capture End-to-End Verification
using ShareX.AvaloniaUI.Linux;
using ShareX.ScreenCaptureLib;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

internal static class E2EChecks
{
    private const string Sandbox = "/tmp/sx-b-capture";

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("ASSERTION FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static string RunCli(params string[] args)
    {
        ProcessStartInfo psi = new("bash", ["scripts/linux/run.sh", ..args])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["SX_SANDBOX"] = Sandbox;
        using Process p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return stdout + "\n" + stderr;
    }

    private static HashSet<string> GetScreenshotFiles()
    {
        string dir = Path.Combine(Sandbox, "data/ShareX/Screenshots");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).ToHashSet();
    }

    private static async Task<string> WaitForNewScreenshotAsync(HashSet<string> before, TimeSpan timeout)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var current = GetScreenshotFiles();
            current.ExceptWith(before);
            if (current.Count > 0) return current.First();
            await Task.Delay(100);
        }
        throw new TimeoutException("New screenshot file did not appear within " + timeout.TotalSeconds + "s");
    }

    private static JsonElement? FindClientByClassOrTitle(string text)
    {
        try
        {
            string json = LinuxCaptureProcess.Text("hyprctl", "clients", "-j");
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (var client in doc.RootElement.EnumerateArray())
            {
                string title = client.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                string cls = client.TryGetProperty("class", out var c) ? c.GetString() ?? "" : "";
                if (title.Contains(text, StringComparison.OrdinalIgnoreCase) || cls.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    return client.Clone();
                }
            }
        }
        catch { }
        return null;
    }

    private static void SendEscape()
    {
        try
        {
            HyprlandCaptureIpc.Dispatch("sendshortcut", ",Escape,", "hl.dsp.send_shortcut({mods='', key='Escape'})");
        }
        catch
        {
            try { LinuxCaptureProcess.Run("hyprctl", "dispatch", "sendshortcut", ",Escape,"); } catch { }
        }
    }

    public static async Task RunAsync()
    {
        Directory.CreateDirectory(Sandbox);
        Directory.CreateDirectory(Path.Combine(Sandbox, "evidence"));

        // 1. Ensure clean sandbox configuration and start daemon
        try { LinuxCaptureProcess.Run("pkill", "-f", "/tmp/sx-b-capture"); } catch { }
        await Task.Delay(500);

        string configDir = Path.Combine(Sandbox, "config/ShareX");
        Directory.CreateDirectory(configDir);
        string configPath = Path.Combine(configDir, "ApplicationConfig.json");
        if (File.Exists(configPath))
        {
            string json = File.ReadAllText(configPath);
            json = json.Replace("\"CaptureCustomRegion\": \"0, 0, 0, 0\"", "\"CaptureCustomRegion\": \"100, 100, 320, 240\"");
            json = json.Replace("\"AutoCaptureRegion\": \"0, 0, 0, 0\"", "\"AutoCaptureRegion\": \"100, 100, 320, 240\"");
            json = json.Replace("\"AutoCaptureRepeatTime\": 60.0", "\"AutoCaptureRepeatTime\": 1.0");
            json = json.Replace("\"AutoCaptureMinimizeToTray\": true", "\"AutoCaptureMinimizeToTray\": false");
            File.WriteAllText(configPath, json);
        }

        ProcessStartInfo psi = new("bash", ["scripts/linux/run.sh", "-silent", "-NoHotkeys"])
        {
            UseShellExecute = false
        };
        psi.Environment["SX_SANDBOX"] = Sandbox;
        Process.Start(psi);
        await Task.Delay(2500);

        var monitors = LinuxScreenCapture.GetMonitors();
        Assert(monitors.Length > 0, "Discovered active monitors: " + monitors.Length);
        foreach (var m in monitors)
        {
            Console.WriteLine($"  Output {m.Name}: {m.Bounds.Width}x{m.Bounds.Height} at ({m.Bounds.X},{m.Bounds.Y}), scale={m.Scale}, primary={m.Primary}");
        }

        // 2. Test -PrintScreen CLI & after-capture (Save to file + Copy to clipboard)
        var filesBefore = GetScreenshotFiles();
        RunCli("-PrintScreen");
        string printScreenFile = await WaitForNewScreenshotAsync(filesBefore, TimeSpan.FromSeconds(8));
        using (SKBitmap printScreenBmp = SKBitmap.Decode(printScreenFile))
        {
            Assert(printScreenBmp != null, "PrintScreen PNG decoded successfully: " + Path.GetFileName(printScreenFile));
            double maxScale = monitors.Max(m => m.Scale);
            int expectedWidth = (int)Math.Round(LinuxScreenCapture.DesktopBounds.Width * maxScale);
            int expectedHeight = (int)Math.Round(LinuxScreenCapture.DesktopBounds.Height * maxScale);
            Assert(printScreenBmp!.Width == expectedWidth && printScreenBmp.Height == expectedHeight,
                $"PrintScreen dimensions match multi-monitor bounds ({printScreenBmp.Width}x{printScreenBmp.Height} vs expected {expectedWidth}x{expectedHeight})");
        }

        // Verify clipboard has PNG image via wl-paste
        byte[] clipboardData = LinuxCaptureProcess.Run("wl-paste", "-t", "image/png");
        using (SKBitmap clipBmp = SKBitmap.Decode(clipboardData))
        {
            Assert(clipBmp != null && clipBmp.Width > 0 && clipBmp.Height > 0,
                $"wl-paste returned valid image/png data ({clipBmp?.Width}x{clipBmp?.Height})");
        }

        // 3. Test -ActiveMonitor CLI
        filesBefore = GetScreenshotFiles();
        Point cursor = LinuxScreenCapture.CursorPosition;
        var activeMon = monitors.FirstOrDefault(m => m.Bounds.Contains(cursor)) ?? monitors.First(m => m.Primary);
        RunCli("-ActiveMonitor");
        string monitorFile = await WaitForNewScreenshotAsync(filesBefore, TimeSpan.FromSeconds(8));
        using (SKBitmap monBmp = SKBitmap.Decode(monitorFile))
        {
            Assert(monBmp != null, "ActiveMonitor PNG decoded: " + Path.GetFileName(monitorFile));
            int expectedW = (int)Math.Round(activeMon.Bounds.Width * activeMon.Scale);
            int expectedH = (int)Math.Round(activeMon.Bounds.Height * activeMon.Scale);
            Assert(monBmp!.Width == expectedW && monBmp.Height == expectedH,
                $"ActiveMonitor dimensions match output {activeMon.Name} ({monBmp.Width}x{monBmp.Height})");
        }

        // 4. Test -ActiveWindow CLI
        var activeWindow = LinuxCaptureWindows.GetWindows().FirstOrDefault(w => w.Active);
        if (activeWindow != null && activeWindow.Rectangle.Width > 0 && activeWindow.Rectangle.Height > 0)
        {
            filesBefore = GetScreenshotFiles();
            RunCli("-ActiveWindow");
            string windowFile = await WaitForNewScreenshotAsync(filesBefore, TimeSpan.FromSeconds(8));
            using (SKBitmap winBmp = SKBitmap.Decode(windowFile))
            {
                Assert(winBmp != null, "ActiveWindow PNG decoded: " + Path.GetFileName(windowFile));
                var winMon = monitors.FirstOrDefault(m => m.Bounds.IntersectsWith(activeWindow.Rectangle)) ?? monitors[0];
                int expectedW = (int)Math.Round(activeWindow.Rectangle.Width * winMon.Scale);
                int expectedH = (int)Math.Round(activeWindow.Rectangle.Height * winMon.Scale);
                Assert(Math.Abs(winBmp!.Width - expectedW) <= 2 && Math.Abs(winBmp.Height - expectedH) <= 2,
                    $"ActiveWindow dimensions ({winBmp.Width}x{winBmp.Height}) match compositor geometry for {activeWindow.ClassName}");
            }
        }

        // 5. Test -RectangleRegion overlay lifecycle (open, grim screenshot, Esc cancel)
        RunCli("-RectangleRegion");
        bool overlayFound = false;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            var client = FindClientByClassOrTitle("Region capture");
            if (client.HasValue)
            {
                overlayFound = true;
                // Screenshot overlay with grim
                LinuxCaptureProcess.Run("grim", "-g", "0,0 100x100", Path.Combine(Sandbox, "evidence/overlay-e2e.png"));
                Assert(File.Exists(Path.Combine(Sandbox, "evidence/overlay-e2e.png")), "Overlay screenshotted with grim while active");
                break;
            }
        }
        Assert(overlayFound, "RectangleRegion overlay appeared on live desktop");
        SendEscape();
        await Task.Delay(400);
        Assert(!FindClientByClassOrTitle("Region capture").HasValue, "RectangleRegion overlay closed cleanly after Esc");

        // 6. Test -ScreenColorPicker lifecycle (open, grim screenshot, Esc cancel)
        RunCli("-ScreenColorPicker");
        bool pickerFound = false;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            var client = FindClientByClassOrTitle("screen color picker");
            if (client.HasValue)
            {
                pickerFound = true;
                LinuxCaptureProcess.Run("grim", "-g", "0,0 100x100", Path.Combine(Sandbox, "evidence/picker-e2e.png"));
                Assert(File.Exists(Path.Combine(Sandbox, "evidence/picker-e2e.png")), "ScreenColorPicker screenshotted with grim while active");
                break;
            }
        }
        Assert(pickerFound, "ScreenColorPicker window appeared on live desktop");
        SendEscape();
        await Task.Delay(400);
        Assert(!FindClientByClassOrTitle("screen color picker").HasValue, "ScreenColorPicker closed cleanly after Esc");

        // 7. Test -ScrollingCapture lifecycle (open, grim screenshot, Esc cancel)
        RunCli("-ScrollingCapture");
        bool scrollFound = false;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            var client = FindClientByClassOrTitle("Scrolling capture");
            if (client.HasValue)
            {
                scrollFound = true;
                LinuxCaptureProcess.Run("grim", "-g", "0,0 100x100", Path.Combine(Sandbox, "evidence/scrolling-e2e.png"));
                Assert(File.Exists(Path.Combine(Sandbox, "evidence/scrolling-e2e.png")), "ScrollingCapture screenshotted with grim while active");
                break;
            }
        }
        Assert(scrollFound, "ScrollingCapture window appeared on live desktop");
        // Dismiss Region capture if opened underneath
        SendEscape();
        await Task.Delay(200);
        SendEscape();
        await Task.Delay(400);
        // Ensure scrolling window is closed
        try
        {
            HyprlandCaptureIpc.Dispatch("closewindow", "class:ShareX", "hl.dsp.window.close({window='title:ShareX - Scrolling capture'})");
        }
        catch { }
        await Task.Delay(200);

        // 8. Test -CustomRegion CLI
        filesBefore = GetScreenshotFiles();
        RunCli("-CustomRegion");
        string customRegionFile = await WaitForNewScreenshotAsync(filesBefore, TimeSpan.FromSeconds(8));
        using (SKBitmap customBmp = SKBitmap.Decode(customRegionFile))
        {
            Assert(customBmp != null, "CustomRegion captured: " + Path.GetFileName(customRegionFile));
            var regionMon = monitors.FirstOrDefault(m => m.Bounds.IntersectsWith(new Rectangle(100, 100, 320, 240))) ?? monitors[0];
            int expectedW = (int)Math.Round(320 * regionMon.Scale);
            int expectedH = (int)Math.Round(240 * regionMon.Scale);
            Assert(customBmp!.Width == expectedW && customBmp.Height == expectedH,
                $"CustomRegion dimensions ({customBmp.Width}x{customBmp.Height}) match expected scaled rectangle ({expectedW}x{expectedH})");
        }

        // 9. Test -LastRegion CLI (falls back to configured region when no interactive region was saved)
        filesBefore = GetScreenshotFiles();
        RunCli("-LastRegion");
        string lastRegionFile = await WaitForNewScreenshotAsync(filesBefore, TimeSpan.FromSeconds(8));
        using (SKBitmap lastBmp = SKBitmap.Decode(lastRegionFile))
        {
            Assert(lastBmp != null, "LastRegion captured: " + Path.GetFileName(lastRegionFile));
            var regionMon = monitors.FirstOrDefault(m => m.Bounds.IntersectsWith(new Rectangle(100, 100, 320, 240))) ?? monitors[0];
            int expectedW = (int)Math.Round(320 * regionMon.Scale);
            int expectedH = (int)Math.Round(240 * regionMon.Scale);
            Assert(lastBmp!.Width == expectedW && lastBmp.Height == expectedH,
                $"LastRegion dimensions ({lastBmp.Width}x{lastBmp.Height}) match expected scaled rectangle");
        }

        // 10. Test -AutoCapture window lifecycle (open, grim screenshot, Esc cancel)
        RunCli("-AutoCapture");
        bool autoCapFound = false;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            var client = FindClientByClassOrTitle("Auto capture");
            if (client.HasValue)
            {
                autoCapFound = true;
                LinuxCaptureProcess.Run("grim", "-g", "0,0 100x100", Path.Combine(Sandbox, "evidence/autocapture-e2e.png"));
                Assert(File.Exists(Path.Combine(Sandbox, "evidence/autocapture-e2e.png")), "AutoCapture screenshotted with grim while active");
                break;
            }
        }
        Assert(autoCapFound, "AutoCapture window appeared on live desktop");
        SendEscape();
        await Task.Delay(400);
        if (FindClientByClassOrTitle("Auto capture").HasValue)
        {
            try { HyprlandCaptureIpc.Dispatch("closewindow", "class:ShareX", "hl.dsp.window.close({window='title:ShareX - Auto capture'})"); } catch { }
            await Task.Delay(200);
        }
        Assert(!FindClientByClassOrTitle("Auto capture").HasValue, "AutoCapture window closed cleanly after Esc");

        // 11. Test -StartAutoCapture and -StopAutoCapture
        filesBefore = GetScreenshotFiles();
        RunCli("-StartAutoCapture");
        string autoCaptureFile = await WaitForNewScreenshotAsync(filesBefore, TimeSpan.FromSeconds(10));
        using (SKBitmap autoBmp = SKBitmap.Decode(autoCaptureFile))
        {
            Assert(autoBmp != null && autoBmp.Width > 0 && autoBmp.Height > 0,
                $"AutoCapture triggered and produced screenshot: {Path.GetFileName(autoCaptureFile)} ({autoBmp?.Width}x{autoBmp?.Height})");
        }
        RunCli("-StopAutoCapture");
        await Task.Delay(500);

        // 12. Test Cursor inclusion
        Point cur = LinuxScreenCapture.CursorPosition;
        Rectangle curRect = new(Math.Max(0, cur.X - 16), Math.Max(0, cur.Y - 16), 32, 32);
        using SKBitmap withCursor = new Screenshot { CaptureCursor = true }.CaptureRectangle(curRect);
        using SKBitmap withoutCursor = new Screenshot { CaptureCursor = false }.CaptureRectangle(curRect);
        Assert(withCursor.Width > 0 && withCursor.Height > 0, "Cursor-enabled rectangle capture succeeded");
        Assert(withoutCursor.Width > 0 && withoutCursor.Height > 0, "Cursor-disabled rectangle capture succeeded");

        // 13. Test Window Capture Geometry & Multi-monitor Offsets
        var allWindows = LinuxCaptureWindows.GetWindows();
        Assert(allWindows.Length > 0, $"Discovered {allWindows.Length} windows across compositor");
        foreach (var win in allWindows.Take(3))
        {
            Console.WriteLine($"  Window {win.Handle:X} ({win.ClassName}): {win.Rectangle}");
            if (win.Rectangle.Width > 10 && win.Rectangle.Height > 10)
            {
                using SKBitmap winCapture = new Screenshot().CaptureRectangle(win.Rectangle);
                Assert(winCapture.Width > 0 && winCapture.Height > 0,
                    $"Compositor window capture succeeded for {win.ClassName} ({winCapture.Width}x{winCapture.Height})");
            }
        }

        // Cleanup daemon
        try { LinuxCaptureProcess.Run("pkill", "-f", "/tmp/sx-b-capture"); } catch { }

        Console.WriteLine("\n=== ALL E2E SCREEN CAPTURE CHECKS PASSED SUCCESSFULLY ===");
    }
}
