// ShareX native overlay renderer tests and benchmark. GPL-3.0-or-later.
using System.Diagnostics;
using System.Text.Json;
using ShareX.OverlayRender;
using SkiaSharp;

namespace ShareX.OverlayRender.Tests;

internal static class Program
{
    private static readonly OverlayOptions Bare = new() { ShowMagnifier = false, ShowInfo = false, ShowCenterCrosshair = false };
    private sealed record Layout(string Name, double Scale, (int Width, int Height)[] Sizes);
    private sealed record Sample(double Total, double Render, double Copy, long Bytes, int Monitors, int Regions);
    private sealed record Result(string Layout, string Scenario, Sample[] Samples);
    private static readonly Layout[] Layouts =
    [
        new("1440p @1.0", 1, [(2560, 1440)]),
        new("4K @1.5", 1.5, [(2560, 1440)]),
        new("Dual @1.5", 1.5, [(1920, 1080), (2560, 1440)]),
        new("Triple @1.5", 1.5, [(2560, 1440), (2560, 1440), (1920, 1080)])
    ];

    public static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("DISPLAY", null);
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        if (args.Contains("--memory"))
        {
            Memory(args.FirstOrDefault(x => x is "Dual" or "Triple") ?? "Triple");
            return 0;
        }
        Correctness();
        Console.WriteLine("PASS: retained/full-redraw equivalence, damage containment, mixed scales, pixels, HUD, shapes, external bitmap, no-op.");
        if (!args.Contains("--bench")) return 0;
        var results = new List<Result>();
        Console.WriteLine("| Layout | Event | p50 / p95 / max ms | MiB copied/event | result |");
        Console.WriteLine("| --- | --- | ---: | ---: | --- |");
        foreach (Layout layout in Layouts)
        foreach (string scenario in new[] { "Mouse", "Selection", "Rectangle", "Hover" })
        {
            Result r = Benchmark(layout, scenario);
            results.Add(r);
            double[] times = r.Samples.Select(x => x.Total).Order().ToArray();
            double limit = scenario == "Mouse" ? 16 : 33;
            Console.WriteLine($"| {layout.Name} | {scenario} | {Percentile(times, .5):F2} / {Percentile(times, .95):F2} / {times[^1]:F2} | {r.Samples.Average(x => x.Bytes) / 1048576:F2} | {(times[^1] <= limit ? "PASS" : "FAIL")} |");
        }
        string output = args.FirstOrDefault(x => x.EndsWith(".json")) ?? "/tmp/sharex-native-scene.json";
        using var shot = Screenshot(3840, 2160);
        var build = new List<double>();
        for (int i = 0; i < 21; i++)
        {
            using var renderer = new MonitorRenderer("cache", new(0, 0, 2560, 1440), 1.5, shot);
            if (i > 0) build.Add(renderer.DimCacheBuildTime.TotalMilliseconds);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Results = results,
            DimCacheMilliseconds = build,
            PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64
        }, new JsonSerializerOptions { WriteIndented = true }));
        build.Sort();
        Console.WriteLine($"4K dim-cache build (20 warm samples): {Percentile(build.ToArray(), .5):F2} / {Percentile(build.ToArray(), .95):F2} / {build[^1]:F2} ms");
        Console.WriteLine($"Samples: {output}; process peak (entire sequential harness): {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F1} MiB");
        return 0;
    }

    private static void Memory(string name)
    {
        Layout layout = Layouts.Single(x => x.Name.StartsWith(name));
        long baseline = Process.GetCurrentProcess().WorkingSet64;
        var renderers = new List<MonitorRenderer>();
        try
        {
            int x = 0;
            foreach (var size in layout.Sizes)
            {
                var shot = Screenshot((int)(size.Width * layout.Scale), (int)(size.Height * layout.Scale));
                var renderer = new MonitorRenderer($"monitor-{renderers.Count}", new(x, 0, size.Width, size.Height), layout.Scale, shot, takeScreenshotOwnership: true);
                renderers.Add(renderer);
                renderer.Update(new OverlayScene { Pointer = new(x + 800, 600), Selection = new(x + 220, 220, 1000, 600),
                    Shape = new(OverlayShapeKind.Rectangle, new(x + 250, 250, 900, 500), SKColors.Red, SKColors.Transparent) });
                Console.WriteLine($"{renderer.MonitorId}: {shot.Width}x{shot.Height}, retained {renderer.RetainedBytes / 1048576.0:F2} MiB, dim cache {renderer.DimCacheBuildTime.TotalMilliseconds:F2} ms");
                x += size.Width;
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Console.WriteLine($"{layout.Name}: retained {renderers.Sum(r => r.RetainedBytes) / 1048576.0:F2} MiB; RSS {Process.GetCurrentProcess().WorkingSet64 / 1048576.0:F2} MiB; baseline {baseline / 1048576.0:F2} MiB; no presentation copies.");
        }
        finally { foreach (var r in renderers) r.Dispose(); }
    }

    private static double Percentile(double[] values, double p) => values[(int)Math.Ceiling(values.Length * p) - 1];
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static unsafe SKBitmap Screenshot(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            uint* row = (uint*)((byte*)bitmap.GetPixels() + y * bitmap.RowBytes);
            for (int x = 0; x < width; x++) row[x] = 0xFF000000u | (uint)((x / 32 % 2 == 0 ? 180 : 80) << 16 | (y / 24 % 2 == 0 ? 160 : 70) << 8 | 120);
        }
        return bitmap;
    }

    private static unsafe void Equal(SKBitmap a, SKBitmap b, string message, int tolerance = 0)
    {
        for (int y = 0; y < a.Height; y++)
        {
            var first = new ReadOnlySpan<byte>((byte*)a.GetPixels() + y * a.RowBytes, a.Width * 4);
            var second = new ReadOnlySpan<byte>((byte*)b.GetPixels() + y * b.RowBytes, b.Width * 4);
            if (first.SequenceEqual(second)) continue;
            for (int index = 0; index < first.Length; index++)
            {
                if (Math.Abs(first[index] - second[index]) <= tolerance) continue;
                throw new InvalidOperationException($"{message}: mismatch ({index / 4}, {y}), {a.GetPixel(index / 4, y)} vs {b.GetPixel(index / 4, y)}");
            }
        }
    }

    private static void Correctness()
    {
        var random = new Random(584);
        foreach (double scale in new[] { 1, 1.25, 1.5, 2 })
        {
            LogicalRect bounds = new(-180, -90, 320, 240);
            using var shot = Screenshot((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale));
            using var renderer = new MonitorRenderer("test", bounds, scale, shot);
            using var output = new SKBitmap(shot.Info);
            using var toolbar = new SKBitmap(30, 12, SKColorType.Bgra8888, SKAlphaType.Premul);
            toolbar.Erase(new SKColor(255, 0, 0, 180));
            for (int i = 0; i < 90; i++)
            {
                double x = -200 + random.Next(320), y = -110 + random.Next(240);
                LogicalRect rect = new(x, y, random.Next(1, 190), random.Next(1, 130));
                var scene = new OverlayScene
                {
                    Pointer = new(x, y),
                    Selection = i % 3 == 0 ? rect : default,
                    Hover = i % 3 == 1 ? rect : default,
                    ShowHandles = i % 2 == 0,
                    Options = new OverlayOptions { ShowScreenCrosshair = i % 2 == 0, BackgroundDimStrength = i % 7 == 0 ? 35 : 20,
                        UseSquareMagnifier = i % 2 == 0, MagnifierSize = 50, MagnifierPixelCount = i % 3 == 0 ? 4 : 15,
                        UseCustomInfoText = true, ShowInfo = i % 4 != 0, ShowMagnifier = i % 5 != 0 },
                    Shape = i % 4 == 0 ? null : new((OverlayShapeKind)(i % 3), rect, SKColors.Red,
                        i % 2 == 0 ? SKColors.Transparent : new SKColor(0, 255, 0, 90), 3),
                    ExternalOverlay = i % 3 == 0 ? null : new(toolbar, new(x, y, 30, 12), i),
                    InfoText = i % 3 == 0 ? "Custom\ninfo" : null
                };
                MonitorDamage d = renderer.Update(scene);
                for (int first = 0; first < d.Regions.Count; first++)
                for (int second = first + 1; second < d.Regions.Count; second++)
                    Check(!d.Regions[first].IntersectsWith(d.Regions[second]), "Damage rectangles must not overlap.");
                foreach (var region in d.Regions) MonitorRenderer.Copy(renderer.Framebuffer, output, region);
                Equal(output, renderer.Framebuffer, $"damage scale={scale}, event={i}");
                using var reference = new MonitorRenderer("reference", bounds, scale, shot);
                reference.Update(scene);
                // Complex Skia clips can round antialiased blends by up to 3 channel values.
                Equal(renderer.Framebuffer, reference.Framebuffer, $"full redraw scale={scale}, event={i}", 3);
                Check(renderer.Update(scene).Regions.Count == 0, "Identical update must not write pixels.");
            }
        }
        PixelTests();
        using (var oddShot = Screenshot(402, 302))
        using (var odd = new MonitorRenderer("fractional", new(0, 0, 321, 241), 1.25, oddShot))
        {
            Check(odd.Update(new OverlayScene { Options = Bare }).Bounds == new SKRectI(0, 0, 402, 302), "fractional ceil raster size");
            var line = new OverlayScene { Options = Bare, Shape = new(OverlayShapeKind.Line, new(20, 20, 100, 0), SKColors.Red, SKColors.Transparent, 3) };
            Check(odd.Update(line).Regions.Count > 0, "horizontal line damage");
            Check(odd.Framebuffer.GetPixel(70, 25).Red == 255, "horizontal line rendered");
            odd.Update(line with { Shape = line.Shape! with { Bounds = new(20, 20, 0, 100) } });
            Check(odd.Framebuffer.GetPixel(25, 70).Red == 255, "vertical line rendered");
        }
        // Shared global coordinates must map correctly on both sides of a mixed-scale seam.
        using var leftShot = Screenshot(200, 150);
        using var rightShot = Screenshot(300, 225);
        using var left = new MonitorRenderer("left", new(-200, 0, 200, 150), 1, leftShot);
        using var right = new MonitorRenderer("right", new(0, 0, 200, 150), 1.5, rightShot);
        var spanning = new OverlayScene { Selection = new(-50, 20, 100, 60), Options = Bare };
        left.Update(spanning); right.Update(spanning);
        Check(left.Framebuffer.GetPixel(170, 40) == leftShot.GetPixel(170, 40), "left seam interior");
        Check(right.Framebuffer.GetPixel(30, 60) == rightShot.GetPixel(30, 60), "right seam interior");
        var move = spanning with { Selection = new(-50, 20, 110, 60) };
        MonitorDamage local = right.Update(move);
        Check(local.Bounds.Width < rightShot.Width, "selection damage must stay local");
    }

    private static void PixelTests()
    {
        using var shot = new SKBitmap(240, 180, SKColorType.Bgra8888, SKAlphaType.Premul);
        shot.Erase(SKColors.White);
        using var r = new MonitorRenderer("pixels", new(0, 0, 240, 180), 1, shot);
        var scene = new OverlayScene { Selection = new(30, 30, 120, 100), ShowHandles = true, Options = Bare };
        r.Update(scene);
        Check(r.Framebuffer.GetPixel(5, 5).Red is >= 203 and <= 205, "20% black dim");
        Check(r.Framebuffer.GetPixel(60, 60) == SKColors.White, "undimmed selection");
        foreach (var p in new[] { (30, 30), (90, 30), (150, 30), (150, 80), (150, 130), (90, 130), (30, 130), (30, 80) })
            Check(r.Framebuffer.GetPixel(p.Item1, p.Item2) == SKColors.White, "white handle center");
        r.Update(scene with { ShowHandles = false });
        SKColor accent = r.Framebuffer.GetPixel(37, 30);
        Check(Math.Abs(accent.Red - 62) < 4 && Math.Abs(accent.Blue - 242) < 4, "accent border (undashed segment)");
        r.Update(scene with { Options = Bare with { ShowCenterCrosshair = true } });
        Check(r.Framebuffer.GetPixel(90, 80) == Bare.Accent, "center crosshair");
        r.Update(new OverlayScene { Hover = new(0, 0, 240, 180), Options = Bare });
        Check(r.Framebuffer.GetPixel(120, 90) == SKColors.White, "full-monitor hover restores original");
        r.Update(new OverlayScene { Options = Bare });
        using var external = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        external.Erase(SKColors.Red);
        var toolbarScene = new OverlayScene { Options = Bare, ExternalOverlay = new(external, new(10, 10, 8, 8)) };
        r.Update(toolbarScene);
        Check(r.Framebuffer.GetPixel(12, 12) == SKColors.Red, "external overlay");
        external.Erase(SKColors.Blue);
        MonitorDamage revision = r.Update(toolbarScene with { ExternalOverlay = toolbarScene.ExternalOverlay! with { Revision = 1 } });
        Check(revision.Bounds.Width <= 10 && r.Framebuffer.GetPixel(12, 12) == SKColors.Blue, "bitmap revision updates locally");
        r.Update(new OverlayScene { Options = Bare });
        Check(r.Framebuffer.GetPixel(120, 90).Red is >= 203 and <= 205, "hover removal restores dim cache");
    }

    private static Result Benchmark(Layout layout, string scenario)
    {
        var renderers = new List<MonitorRenderer>();
        var shots = new List<SKBitmap>();
        var outputs = new List<SKBitmap>();
        var samples = new List<Sample>();
        try
        {
            int offset = 0;
            foreach (var size in layout.Sizes)
            {
                var shot = Screenshot((int)(size.Width * layout.Scale), (int)(size.Height * layout.Scale));
                shots.Add(shot);
                renderers.Add(new($"monitor-{renderers.Count}", new(offset, 0, size.Width, size.Height), layout.Scale, shot));
                outputs.Add(new SKBitmap(shot.Info));
                offset += size.Width;
            }
            int target = Array.FindIndex(layout.Sizes, x => x.Width == layout.Sizes.Max(y => y.Width));
            var anchorMonitor = renderers[target].LogicalBounds;
            foreach (var (r, output) in renderers.Zip(outputs))
            {
                r.Update(new OverlayScene());
                MonitorRenderer.Copy(r.Framebuffer, output, new(0, 0, output.Width, output.Height));
            }
            for (int i = 0; i < 280; i++)
            {
                // Same deterministic endpoint sweep as OverlayBench: 40 warmup + 240 events.
                double x = 400 + i % 160 * 9, y = 400 + i % 80 * 7;
                LogicalRect selection = scenario == "Selection" ? new(anchorMonitor.X + 220, 220, x - 220, y - 220) : default;
                if (scenario == "Rectangle") selection = new(0, 0, offset, 1440);
                int hoverMonitor = i % renderers.Count;
                bool hoverVisible = renderers.Count > 1 || i % 2 == 0;
                var scene = new OverlayScene
                {
                    Pointer = scenario == "Hover" ? new(renderers[hoverMonitor].LogicalBounds.X + 800 + i % 13, 600) : new(anchorMonitor.X + x, y), Selection = selection,
                    Hover = scenario == "Hover" && hoverVisible ? renderers[hoverMonitor].LogicalBounds : default,
                    Shape = scenario == "Rectangle" ? new(OverlayShapeKind.Rectangle, new(anchorMonitor.X + 220, 220, x - 220, y - 220), SKColors.Red, SKColors.Transparent) : null
                };
                long start = Stopwatch.GetTimestamp();
                var damages = renderers.Select(r => r.Update(scene)).ToArray();
                double renderMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long copyStart = Stopwatch.GetTimestamp();
                long bytes = 0;
                int monitors = 0, regions = 0;
                for (int j = 0; j < renderers.Count; j++)
                {
                    if (damages[j].Regions.Count > 0) monitors++;
                    foreach (var rect in damages[j].Regions)
                    {
                        MonitorRenderer.Copy(renderers[j].Framebuffer, outputs[j], rect);
                        bytes += (long)rect.Width * rect.Height * 4;
                        regions++;
                    }
                }
                double copyMs = Stopwatch.GetElapsedTime(copyStart).TotalMilliseconds;
                double totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (i > 0 && scenario != "Hover")
                    Check(damages.Where((_, j) => j != target).All(d => d.Regions.Count == 0), "untouched monitor wrote pixels");
                if (i > 0 && scenario == "Hover")
                    Check(monitors == Math.Min(2, renderers.Count), "hover must touch only old/new monitors");
                if (i >= 40) samples.Add(new(totalMs, renderMs, copyMs, bytes, monitors, regions));
                for (int j = 0; j < renderers.Count; j++) Equal(outputs[j], renderers[j].Framebuffer, "presentation");
                if (i is 39 or 279)
                {
                    for (int j = 0; j < renderers.Count; j++)
                    {
                        using var reference = new MonitorRenderer("reference", renderers[j].LogicalBounds, layout.Scale, shots[j]);
                        reference.Update(scene);
                        Equal(renderers[j].Framebuffer, reference.Framebuffer, "benchmark full redraw", 3);
                    }
                }
            }
        }
        finally
        {
            foreach (var r in renderers) r.Dispose();
            foreach (var b in shots.Concat(outputs)) b.Dispose();
        }
        return new(layout.Name, scenario, samples.ToArray());
    }
}
