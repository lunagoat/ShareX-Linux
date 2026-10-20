// ShareX overlay render benchmark. GPL-3.0-or-later.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Integration;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShareX.OverlayBench;

internal sealed class BenchApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ShareX.Avalonia/"))
        { Source = new Uri("avares://ShareX.Avalonia/Theming/ShareXTheme.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://ShareX.Avalonia/"))
        { Source = new Uri("avares://ShareX.Avalonia/Theming/ControlStyles.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://ShareX.Avalonia/"))
        { Source = new Uri("avares://ShareX.Avalonia/Theming/ToolStyles.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://ShareX.ImageEditor/"))
        { Source = new Uri("avares://ShareX.ImageEditor/Presentation/Theming/EditorStyles.axaml") });
        RequestedThemeVariant = ThemeManager.ShareXDark;
    }
}

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Warmup = 40;
    private const int Events = 240;
    private sealed record Layout(string Name, double Scale, Size[] Monitors);
    private sealed record Sample(double Total, double Avalonia, double Raster, double Diff, double Copy, long Bytes, int Surfaces);
    private sealed record Result(string Layout, string Mode, string Scenario, Sample[] Samples);
    private static readonly Layout[] Layouts =
    [
        new("1440p", 1, [new(2560, 1440)]),
        new("4K", 1.5, [new(2560, 1440)]),
        // The lag-case dimensions are logical, matching the archived host tests.
        new("Dual", 1.5, [new(1920, 1080), new(2560, 1440)]),
        new("Triple", 1.5, [new(2560, 1440), new(2560, 1440), new(1920, 1080)])
    ];

    public static int Main(string[] args)
    {
        // Never select Avalonia.Desktop, connect to a compositor, or run production OnOpened.
        Environment.SetEnvironmentVariable("DISPLAY", null);
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "headless");
        AppBuilder.Configure<BenchApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true })
            .UseSkia().WithInterFont().SetupWithoutStarting();
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        if (args.Contains("--native-parity")) return NativeSceneParity.Run();
        if (args.Contains("--layer")) return LayerBench.Run(args);
        var results = new List<Result>();
        foreach (Layout layout in Layouts)
        foreach (string mode in new[] { "A", "B" })
        foreach (string scenario in new[] { "Mouse", "Selection", "Rectangle", "Hover" })
        {
            if (args.Contains("--smoke") && layout.Name != "1440p") continue;
            var result = Run(layout, mode, scenario, args.Contains("--smoke") ? 8 : Events);
            results.Add(result);
            double[] times = result.Samples.Select(x => x.Total).Order().ToArray();
            Console.WriteLine($"{layout.Name,-7} {mode} {scenario,-10} {Percentile(times, .5):F2} / {Percentile(times, .95):F2} / {times[^1]:F2} ms");
        }
        string output = args.FirstOrDefault(x => x.EndsWith(".json")) ?? "/tmp/sharex-overlay-bench.json";
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Samples: {output}; peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F1} MiB");
        return 0;
    }

    private static double Percentile(double[] sorted, double percentile) => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    private static void Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static void Tick()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Surface : IDisposable
    {
        public RegionCaptureWindow Window { get; }
        public RetainedHeadlessFrames.RetainedFrameSurface Frame { get; }
        public byte[] Previous = [];
        public byte[] Output = [];
        public int Offset;
        public Size Size;
        public Surface(Size size, double scale, int offset)
        {
            Offset = offset;
            Size = size;
            var bitmap = new SKBitmap((int)(size.Width * scale), (int)(size.Height * scale));
            // Nonuniform, deterministic frozen screenshot; no capture APIs.
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint())
            {
                canvas.Clear(SKColors.DarkSlateBlue);
                for (int x = 0; x < bitmap.Width; x += 64)
                {
                    paint.Color = new SKColor((byte)(x % 251), (byte)(x % 193), (byte)(x % 137));
                    canvas.DrawRect(x, 0, 32, bitmap.Height, paint);
                }
            }
            Window = new RegionCaptureWindow(new AvaloniaRegionCaptureRequest
            {
                Screenshot = bitmap,
                ScreenBounds = new DrawingRectangle(0, 0, (int)size.Width, (int)size.Height),
                RegionCaptureOptions = new RegionCaptureOptions { QuickCapture = false, DetectWindows = false },
                ImageEditorOptions = new ImageEditorOptions()
            });
            // Replace ONLY desktop startup. Keep the existing workspace, controls, tools and input handlers.
            var opened = (EventHandler)typeof(RegionCaptureWindow).GetMethod("OnOpened", Private | BindingFlags.DeclaredOnly)!.CreateDelegate(typeof(EventHandler), Window);
            Window.Opened -= opened;
            Frame = RetainedHeadlessFrames.Attach(Window);
            Window.SetRenderScaling(scale);
            Window.Width = size.Width;
            Window.Height = size.Height;
            Window.Show(); // HeadlessWindowImpl only; never a native toplevel.
            Tick();
            Call(Window, "UpdatePixelTransforms");
            var workspace = Field<EditorView>(Window, "_editorWorkspace");
            workspace.ConfigureForFullscreenWorkspace();
            workspace.LoadWorkspaceImage(bitmap);
            Set(Window, "_workspaceOwnsScreenshot", true);
            Set(Window, "_keyboardInputEnabled", true);
            Field<Grid>(Window, "_captureToolbar").Transitions = null;
            Field<Grid>(Window, "_regionInputSurface").Focus();
            Tick();
            if (!Frame.TryGetFrame(out var frame) || frame.Width != bitmap.Width || frame.Height != bitmap.Height)
                throw new InvalidOperationException("Incorrect fractional-scale framebuffer size.");
        }
        public void Dispose()
        {
            Window.Close();
            Tick();
            Frame.Dispose();
        }
    }

    private static Result Run(Layout layout, string mode, string scenario, int count)
    {
        Size desktop = new(layout.Monitors.Sum(x => x.Width), layout.Monitors.Max(x => x.Height));
        var surfaces = new List<Surface>();
        int offset = 0;
        foreach (Size size in mode == "A" ? new[] { desktop } : layout.Monitors)
        {
            surfaces.Add(new Surface(size, layout.Scale, offset));
            offset += (int)size.Width;
        }
        try
        {
            int activeMonitor = Array.FindIndex(layout.Monitors, x => x.Width == layout.Monitors.Max(m => m.Width));
            double monitorOffset = layout.Monitors.Take(activeMonitor).Sum(x => x.Width);
            Surface active = surfaces[mode == "A" ? 0 : activeMonitor];
            Point anchor = new((mode == "A" ? monitorOffset : 0) + 220, 220);
            if (scenario == "Rectangle")
            {
                Call(active.Window, "SetSelection", new Rect(0, 0, desktop.Width * layout.Scale, desktop.Height * layout.Scale), null);
                Field<MainViewModel>(active.Window, "_viewModel").ToolbarAdapter.SelectTool(EditorTool.Rectangle);
                Tick();
            }
            if (scenario is "Selection" or "Rectangle")
            {
                active.Window.MouseMove(anchor);
                active.Window.MouseDown(anchor, MouseButton.Left);
                Tick();
            }
            for (int i = 0; i < 12; i++) Tick();
            foreach (var surface in surfaces) Transfer(surface, false);
            var samples = new List<Sample>();
            int previousMonitor = -1;
            for (int i = 0; i < Warmup + count; i++)
            {
                foreach (var surface in surfaces) { surface.Frame.RasterMilliseconds = 0; surface.Frame.RenderCount = 0; }
                long start = Stopwatch.GetTimestamp();
                var touched = new HashSet<Surface>();
                if (scenario == "Hover")
                {
                    int monitor = i % layout.Monitors.Length;
                    double xOffset = layout.Monitors.Take(monitor).Sum(x => x.Width);
                    Surface target = surfaces[mode == "A" ? 0 : monitor];
                    if (mode == "B" && previousMonitor >= 0 && previousMonitor != monitor)
                    {
                        Surface old = surfaces[previousMonitor];
                        Field<RegionSelectionOverlay>(old.Window, "_regionOverlay").HoverRectangle = default;
                        Field<StackPanel>(old.Window, "_magnifierPanel").IsVisible = false;
                        touched.Add(old);
                    }
                    bool visible = layout.Monitors.Length > 1 || i % 2 == 0;
                    SimpleWindowInfo[] windows = visible ? [new SimpleWindowInfo(new IntPtr(1), new DrawingRectangle(
                        mode == "A" ? (int)xOffset : 0, 0, (int)layout.Monitors[monitor].Width, (int)layout.Monitors[monitor].Height)) { IsWindow = true }] : [];
                    Set(target.Window, "_windows", windows);
                    target.Window.MouseMove(new Point((mode == "A" ? xOffset : 0) + 800 + i % 13, 600));
                    Rect hover = Field<RegionSelectionOverlay>(target.Window, "_regionOverlay").HoverRectangle;
                    if (visible && Math.Abs(hover.Width - layout.Monitors[monitor].Width * layout.Scale) > 1)
                        throw new InvalidOperationException("Hover did not cover the monitor.");
                    touched.Add(target);
                    previousMonitor = monitor;
                }
                else
                {
                    // Identical large/small rectangle motion for A and B, wholly within the largest monitor.
                    Point point = new((mode == "A" ? monitorOffset : 0) + 400 + i % 160 * 9,
                        400 + i % 80 * 7);
                    active.Window.MouseMove(point, scenario == "Mouse" ? RawInputModifiers.None : RawInputModifiers.LeftMouseButton);
                    touched.Add(active);
                    if (scenario == "Selection" && Field<RegionSelectionOverlay>(active.Window, "_regionOverlay").SelectionRectangle.Width <= 0)
                        throw new InvalidOperationException("Selection input failed.");
                }
                Tick();
                double render = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                double raster = surfaces.Sum(s => s.Frame.RasterMilliseconds);
                int rendered = surfaces.Sum(s => s.Frame.RenderCount);
                if (rendered == 0) throw new InvalidOperationException("Event did not render a frame.");
                if (mode == "B" && surfaces.Except(touched).Any(s => s.Frame.RenderCount != 0))
                    throw new InvalidOperationException("An untouched monitor rendered.");
                double diff = 0, copy = 0;
                long bytes = 0;
                foreach (Surface surface in touched)
                {
                    var transfer = Transfer(surface, mode == "B");
                    diff += transfer.Diff;
                    copy += transfer.Copy;
                    bytes += transfer.Bytes;
                }
                double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                foreach (Surface surface in touched) VerifyCopy(surface);
                if (i >= Warmup) samples.Add(new Sample(total, Math.Max(0, render - raster), raster, diff, copy, bytes, rendered));
            }
            if (scenario == "Rectangle")
            {
                active.Window.MouseUp(new Point(anchor.X + 500, 700), MouseButton.Left);
                Tick();
                if (!Field<MainViewModel>(active.Window, "_viewModel").HasAnnotations)
                    throw new InvalidOperationException("Rectangle tool created no annotation.");
            }
            return new Result(layout.Name, mode, scenario, samples.ToArray());
        }
        finally
        {
            foreach (Surface surface in surfaces) surface.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static unsafe void VerifyCopy(Surface surface)
    {
        if (!surface.Frame.TryGetFrame(out var frame)) throw new InvalidOperationException("No frame.");
        var source = new ReadOnlySpan<byte>((void*)frame.Address, frame.Stride * frame.Height);
        if (!surface.Output.AsSpan().SequenceEqual(source))
            throw new InvalidOperationException("Dirty copy diverged from raster.");
    }

    // SIMD row comparison first, pixel scan only on changed rows. B copies the exact changed bounding rectangle.
    // A copies the entire desktop and then diffs against the prior presented buffer.
    private static unsafe (double Diff, double Copy, long Bytes) Transfer(Surface surface, bool dirtyOnly)
    {
        if (!surface.Frame.TryGetFrame(out var frame)) throw new InvalidOperationException("No frame.");
        int length = frame.Stride * frame.Height;
        if (surface.Previous.Length != length)
        {
            surface.Previous = new byte[length];
            surface.Output = new byte[length];
        }
        var source = new ReadOnlySpan<byte>((void*)frame.Address, length);
        long start = Stopwatch.GetTimestamp();
        double copy = 0;
        if (!dirtyOnly)
        {
            source.CopyTo(surface.Output);
            copy = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            source = surface.Output;
        }
        start = Stopwatch.GetTimestamp();
        int left = frame.Width, right = 0, top = frame.Height, bottom = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            var row = source.Slice(y * frame.Stride, frame.Width * 4);
            var prior = surface.Previous.AsSpan(y * frame.Stride, frame.Width * 4);
            if (row.SequenceEqual(prior)) continue;
            var pixels = MemoryMarshal.Cast<byte, uint>(row);
            var old = MemoryMarshal.Cast<byte, uint>(prior);
            int x = 0, end = frame.Width - 1;
            while (pixels[x] == old[x]) x++;
            while (pixels[end] == old[end]) end--;
            left = Math.Min(left, x);
            right = Math.Max(right, end + 1);
            top = Math.Min(top, y);
            bottom = y + 1;
        }
        double diff = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        long bytes = dirtyOnly ? 0 : length;
        for (int y = top; y < bottom; y++)
        {
            int index = y * frame.Stride + left * 4;
            int size = (right - left) * 4;
            var row = source.Slice(index, size);
            if (dirtyOnly) { row.CopyTo(surface.Output.AsSpan(index, size)); bytes += size; }
            row.CopyTo(surface.Previous.AsSpan(index, size));
        }
        copy += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (diff, copy, bytes);
    }
}
