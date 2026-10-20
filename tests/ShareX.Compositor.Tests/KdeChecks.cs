// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Drawing;
using ShareX.AvaloniaUI.Linux;
using SkiaSharp;

internal static class KdeChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var layout in new[]
        {
            (Name: "top-panel", Left: new Rectangle(0, 0, 1920, 1080), Right: new Rectangle(1920, 0, 2560, 1440)),
            (Name: "dual", Left: new Rectangle(0, 0, 1920, 1080), Right: new Rectangle(1920, 0, 2560, 1440)),
            (Name: "scaled", Left: new Rectangle(0, 0, 1920, 1080), Right: new Rectangle(1920, 0, 1707, 960)),
            (Name: "primary-right", Left: new Rectangle(-1920, -120, 1920, 1080), Right: new Rectangle(0, 0, 2560, 1440))
        })
        {
            var monitors = LinuxScreenCapture.ParseKScreenDoctor(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kde-" + layout.Name + ".json")));
            check(monitors.Length == 2 && monitors[0].Bounds == layout.Left && monitors[1].Bounds == layout.Right,
                "KDE " + layout.Name + ": both monitors in logical coordinates");
            check(monitors.Single(m => m.Primary).Name == (layout.Name == "primary-right" ? "DP-2" : "DP-1"),
                "KDE " + layout.Name + ": Plasma 6 priority identifies primary, independently of order");
            Rectangle desktop = monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            Rectangle expected = layout.Name switch
            {
                "dual" or "top-panel" => new Rectangle(0, 0, 4480, 1440),
                "scaled" => new Rectangle(0, 0, 3627, 1080),
                _ => new Rectangle(-1920, -120, 4480, 1560)
            };
            check(desktop == expected, "KDE " + layout.Name + ": desktop union includes every monitor and negative origins");
            // Fake a portal PNG at a uniform desktop raster scale. Colours encode the monitor's position.
            foreach (int rasterScale in new[] { 1, 2 })
            {
                using SKBitmap png = new(desktop.Width * rasterScale, desktop.Height * rasterScale);
                png.Erase(SKColors.Black);
                using (SKCanvas canvas = new(png))
                using (SKPaint paint = new())
                {
                    foreach (var monitor in monitors)
                    {
                        paint.Color = monitor.Name == "DP-1" ? SKColors.Red : SKColors.Blue;
                        Rectangle b = monitor.Bounds;
                        canvas.DrawRect((b.X - desktop.X) * rasterScale, (b.Y - desktop.Y) * rasterScale,
                            b.Width * rasterScale, b.Height * rasterScale, paint);
                    }
                    if (layout.Name == "top-panel")
                    {
                        paint.Color = SKColors.Green;
                        canvas.DrawRect(0, 0, 1920 * rasterScale, 44 * rasterScale, paint);
                    }
                }
                foreach (var monitor in monitors)
                {
                    using SKBitmap crop = LinuxScreenCapture.CropDesktop(png, desktop, monitor.Bounds);
                    SKColor color = monitor.Name == "DP-1" ? SKColors.Red : SKColors.Blue;
                    check(crop.Width == monitor.Bounds.Width * rasterScale && crop.Height == monitor.Bounds.Height * rasterScale &&
                        crop.GetPixel(0, 0) == (layout.Name == "top-panel" && monitor.Name == "DP-1" ? SKColors.Green : color) && crop.GetPixel(crop.Width - 1, crop.Height - 1) == color,
                        $"KDE {layout.Name}: {monitor.Name} portal crop at {rasterScale}x has the correct size and both corner pixels");
                }
                Rectangle captureBounds = KdeCapture.UseActiveMonitor(true, true, false) ? monitors[0].Bounds : desktop;
                using SKBitmap frozen = LinuxScreenCapture.CropDesktop(png, desktop, captureBounds);
                check(frozen.Width == png.Width && frozen.Height == png.Height,
                    $"KDE {layout.Name}: saved active-monitor mode still freezes every output");
                if (layout.Name == "top-panel")
                {
                    using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kde-top-panel.json")));
                    check(fixture.RootElement.GetProperty("kwin").GetProperty("workArea").GetProperty("y").GetInt32() == 44 &&
                        captureBounds.Top == 0 && frozen.GetPixel(0, 0) == SKColors.Green && frozen.GetPixel(0, 44 * rasterScale - 1) == SKColors.Green &&
                        frozen.GetPixel(0, 44 * rasterScale) == SKColors.Red,
                        $"KDE top panel: full desktop and frozen raster preserve rows 0..43 at {rasterScale}x despite reserved work area");
                }
                Rectangle crossing = new(layout.Right.X - 20, 60, 40, 20);
                using SKBitmap across = LinuxScreenCapture.CropDesktop(png, desktop, crossing);
                check(across.GetPixel(0, 0) == SKColors.Red && across.GetPixel(across.Width - 1, 0) == SKColors.Blue,
                    $"KDE {layout.Name}: selection crosses the monitor boundary at {rasterScale}x");
            }
        }
        var explicitSize = LinuxScreenCapture.ParseKScreenDoctor("""
            {"outputs":[{"name":"DP-1","enabled":true,"connected":true,"pos":{"x":0,"y":0},
            "size":{"width":2560,"height":1440},"scale":1.5,"explicitLogicalSize":{"width":1706.5,"height":959.5}}]}
            """);
        check(explicitSize.Single().Bounds == new Rectangle(0, 0, 1707, 960), "KDE: explicit logical size takes precedence and rounds up");
        CheckDiscovery(check);
    }

    private static void CheckDiscovery(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "sharex-kscreen-fake-" + Guid.NewGuid().ToString("N"));
        string[] variables = ["PATH", "XDG_CURRENT_DESKTOP", "XDG_SESSION_TYPE", "WAYLAND_DISPLAY", "NIRI_SOCKET", "HYPRLAND_INSTANCE_SIGNATURE", "SWAYSOCK"];
        var previous = variables.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        Directory.CreateDirectory(temp);
        try
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kde-dual.json"));
            string executable = Path.Combine(temp, "kscreen-doctor");
            File.WriteAllText(executable, "#!/bin/sh\n[ \"$1\" = -j ] || exit 1\nprintf '%s' '" + json + "'\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (string variable in variables) Environment.SetEnvironmentVariable(variable, null);
            Environment.SetEnvironmentVariable("PATH", temp);
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "KDE");
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "fixture-only");
            check(DoctorDisplays.DetectWindowPlacementPath() == "kde-kwin",
                "KDE doctor: reports kde-kwin placement");
            check(DoctorDisplays.FormatKdeGeometry("{}")[0].Contains("unavailable"), "KDE doctor: malformed geometry fails soft");
            var monitors = LinuxScreenCapture.GetMonitors();
            check(monitors.Length == 2 && monitors[1].Bounds == new Rectangle(1920, 0, 2560, 1440) &&
                LinuxScreenCapture.DesktopBounds == new Rectangle(0, 0, 4480, 1440),
                "KDE process fake: GetMonitors and DesktopBounds use kscreen-doctor -j without touching a display");
        }
        finally
        {
            foreach (var variable in previous) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            Directory.Delete(temp, true);
        }
    }
}
