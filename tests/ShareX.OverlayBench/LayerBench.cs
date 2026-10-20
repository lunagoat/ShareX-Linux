// GPL-3.0-or-later.
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.OverlayAvaloniaLayer;
using SkiaSharp;

namespace ShareX.OverlayBench;

internal static class LayerBench
{
    private const int Warmup = 40;
    private const int DefaultEvents = 240;

    private sealed record Layout(string Name, double Scale, Size[] Monitors);
    public sealed record LayerSample(double Total, double Avalonia, double Raster, double Diff, long Bytes, int DirtyRegionCount);
    public sealed record LayerResult(string Layout, string Scenario, LayerSample[] Samples);

    private static readonly Layout[] Layouts =
    [
        new("1440p", 1.0, [new(2560, 1440)]),
        new("4K", 1.5, [new(2560, 1440)]),
        new("Dual", 1.5, [new(1920, 1080), new(2560, 1440)]),
        new("Triple", 1.5, [new(2560, 1440), new(2560, 1440), new(1920, 1080)])
    ];

    public static int Run(string[] args)
    {
        bool smoke = args.Contains("--smoke");
        string? targetLayout = args.FirstOrDefault(x => x.StartsWith("--layout="))?["--layout=".Length..];
        int count = smoke ? 8 : DefaultEvents;
        var results = new List<LayerResult>();

        Console.WriteLine($"=== Avalonia Annotation Layer Benchmark (Events: {count}, Warmup: {Warmup}) ===");
        Console.WriteLine($"{"Layout",-11} {"Scenario",-15} {"p50 (ms)",-10} {"p95 (ms)",-10} {"max (ms)",-10} {"Dirty KB",-10} {"Target",-10} Status");
        Console.WriteLine(new string('-', 85));

        foreach (Layout layout in Layouts)
        {
            if (targetLayout != null && !layout.Name.Equals(targetLayout, StringComparison.OrdinalIgnoreCase)) continue;
            if (smoke && targetLayout == null && layout.Name != "1440p") continue;

            foreach (string scenario in new[] { "Hover", "ToolSwitch", "Rectangle", "Text", "Undo" })
            {
                var result = RunScenario(layout, scenario, count);
                results.Add(result);

                double[] times = result.Samples.Select(x => x.Total).Order().ToArray();
                double p50 = Percentile(times, 0.50);
                double p95 = Percentile(times, 0.95);
                double max = times[^1];
                double avgBytes = result.Samples.Average(x => x.Bytes) / 1024.0;

                double target = scenario is "Hover" or "ToolSwitch" ? 16.0 : 33.0;
                bool pass = scenario is "Hover" or "ToolSwitch"
                    ? p95 <= target
                    : p95 <= target && max <= target;

                string status = pass ? "PASS" : "FAIL";
                string targetStr = scenario is "Hover" or "ToolSwitch" ? "p95<=16" : "max<=33";

                Console.WriteLine($"{layout.Name + " @" + layout.Scale:F1,-11} {scenario,-15} {p50,8:F2}  {p95,8:F2}  {max,8:F2}  {avgBytes,8:F1}  {targetStr,-10} {status}");
            }
        }

        string output = args.FirstOrDefault(x => x.EndsWith(".json") && !x.Contains("sharex-overlay-bench")) ?? "/tmp/sharex-layer-bench.json";
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nSamples: {output}; peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F1} MiB");

        return 0;
    }

    private static double Percentile(double[] sorted, double percentile) =>
        sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private static LayerResult RunScenario(Layout layout, string scenario, int count)
    {
        Size desktopSize = new(layout.Monitors.Sum(x => x.Width), layout.Monitors.Max(x => x.Height));
        Rect desktopBounds = new(0, 0, desktopSize.Width, desktopSize.Height);

        int activeMonitorIndex = Array.FindIndex(layout.Monitors, x => x.Width == layout.Monitors.Max(m => m.Width));
        double monitorOffset = layout.Monitors.Take(activeMonitorIndex).Sum(x => x.Width);
        Rect activeMonitor = new(monitorOffset, 0, layout.Monitors[activeMonitorIndex].Width, layout.Monitors[activeMonitorIndex].Height);

        var options = new AnnotationLayerOptions
        {
            RenderScaling = layout.Scale,
            EnableAnnotations = true
        };

        using var host = AnnotationLayerHost.Create(desktopBounds, options);
        host.SetActiveMonitor(activeMonitor);

        // Initial warm up ticks to settle layout
        for (int i = 0; i < 12; i++) host.Render();

        var samples = new List<LayerSample>();

        // Pre-configure scenario
        Point anchor = new(monitorOffset + 220, 220);
        Point toolbarCenter = new(activeMonitor.X + activeMonitor.Width / 2d, activeMonitor.Y + 15);

        if (scenario == "Rectangle")
        {
            host.SetSelection(new Rect(monitorOffset, 0, activeMonitor.Width, activeMonitor.Height));
            host.SelectTool(EditorTool.Rectangle);
            host.Render();
            host.SendPointer(PointerAction.Move, anchor);
            host.SendPointer(PointerAction.Down, anchor, MouseButton.Left);
            host.Render();
        }
        else if (scenario == "Text")
        {
            host.SetSelection(new Rect(monitorOffset, 0, activeMonitor.Width, activeMonitor.Height));
            host.SelectTool(EditorTool.Text);
            host.Render();
            Point textClick = new(monitorOffset + 400, 300);
            host.SendPointer(PointerAction.Move, textClick);
            host.SendPointer(PointerAction.Down, textClick, MouseButton.Left);
            host.Render();
            host.SendPointer(PointerAction.Up, textClick, MouseButton.Left);
            host.Render();
        }
        else if (scenario == "Undo")
        {
            host.SetSelection(new Rect(monitorOffset, 0, activeMonitor.Width, activeMonitor.Height));
            host.SelectTool(EditorTool.Rectangle);
            host.Render();
            host.SendPointer(PointerAction.Move, anchor);
            host.SendPointer(PointerAction.Down, anchor, MouseButton.Left);
            host.Render();
            host.SendPointer(PointerAction.Move, new Point(anchor.X + 200, anchor.Y + 200), modifiers: RawInputModifiers.LeftMouseButton);
            host.Render();
            host.SendPointer(PointerAction.Up, new Point(anchor.X + 200, anchor.Y + 200), MouseButton.Left);
            host.Render();
            if (!host.HasAnnotations) throw new InvalidOperationException("Failed to create annotation for Undo test.");
        }

        int totalEvents = Warmup + count;
        for (int i = 0; i < totalEvents; i++)
        {
            switch (scenario)
            {
                case "Hover":
                {
                    // Move across toolbar buttons horizontally
                    double x = toolbarCenter.X - 200 + (i % 25) * 16;
                    double y = toolbarCenter.Y + 10;
                    host.SendPointer(PointerAction.Move, new Point(x, y));
                    break;
                }
                case "ToolSwitch":
                {
                    // Alternate selecting tools
                    var tool = (i % 3) switch
                    {
                        0 => EditorTool.Rectangle,
                        1 => EditorTool.Ellipse,
                        _ => EditorTool.Arrow
                    };
                    host.SelectTool(tool);
                    break;
                }
                case "Rectangle":
                {
                    Point p = new(monitorOffset + 400 + i % 160 * 9, 400 + i % 80 * 7);
                    host.SendPointer(PointerAction.Move, p, modifiers: RawInputModifiers.LeftMouseButton);
                    break;
                }
                case "Text":
                {
                    char c = (char)('a' + (i % 26));
                    host.SendTextInput(c.ToString());
                    break;
                }
                case "Undo":
                {
                    if (!host.HasAnnotations)
                    {
                        Point p1 = new(anchor.X + 50, anchor.Y + 50);
                        Point p2 = new(anchor.X + 150, anchor.Y + 150);
                        host.SelectTool(EditorTool.Rectangle);
                        host.SendPointer(PointerAction.Move, p1);
                        host.SendPointer(PointerAction.Down, p1, MouseButton.Left);
                        host.SendPointer(PointerAction.Move, p2, modifiers: RawInputModifiers.LeftMouseButton);
                        host.SendPointer(PointerAction.Up, p2, MouseButton.Left);
                        host.Render();
                    }
                    host.Undo();
                    break;
                }
            }

            var renderResult = host.Render();

            if (i >= Warmup)
            {
                samples.Add(new LayerSample(
                    renderResult.TotalMilliseconds,
                    renderResult.AvaloniaMilliseconds,
                    renderResult.RasterMilliseconds,
                    renderResult.DiffMilliseconds,
                    renderResult.ChangedBytes,
                    renderResult.DirtyRegions.Count
                ));
            }
        }

        // Final validations
        if (scenario == "Rectangle")
        {
            host.SendPointer(PointerAction.Up, new Point(anchor.X + 500, 700), MouseButton.Left);
            host.Render();
            if (!host.HasAnnotations) throw new InvalidOperationException("Rectangle scenario produced no annotations.");

            // Test RenderResult parity
            int physW = (int)(desktopSize.Width * layout.Scale);
            int physH = (int)(desktopSize.Height * layout.Scale);
            using var testScreenshot = new SKBitmap(physW, physH);
            testScreenshot.Erase(SKColors.CornflowerBlue);
            using var croppedResult = host.RenderResult(testScreenshot, new Rect(anchor.X, anchor.Y, 200, 200));
            if (croppedResult == null || croppedResult.Width <= 0 || croppedResult.Height <= 0)
                throw new InvalidOperationException("RenderResult failed to produce cropped screenshot.");
        }

        return new LayerResult(layout.Name, scenario, samples.ToArray());
    }
}
