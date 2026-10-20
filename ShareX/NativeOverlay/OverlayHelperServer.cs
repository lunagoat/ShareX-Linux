// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Core.Annotations;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Newtonsoft.Json;
using ShareX.AvaloniaUI.Theming;
using ShareX.NativeOverlay;
using ShareX.NativeOverlay.Wayland;
using ShareX.OverlayAvaloniaLayer;
using ShareX.OverlayHost;
using ShareX.OverlayRender;
using SkiaSharp;
using HostAction = ShareX.OverlayHost.RegionCaptureAction;

namespace ShareX;

internal sealed class OverlayHelperApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ShareX.Avalonia/"))
        { Source = new Uri("avares://ShareX.Avalonia/Theming/ShareXTheme.axaml") });
        foreach (string name in new[] { "ControlStyles", "ToolStyles" })
            Styles.Add(new StyleInclude(new Uri("avares://ShareX.Avalonia/")) { Source = new Uri($"avares://ShareX.Avalonia/Theming/{name}.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://ShareX.ImageEditor/"))
        { Source = new Uri("avares://ShareX.ImageEditor/Presentation/Theming/EditorStyles.axaml") });
        RequestedThemeVariant = ThemeManager.ShareXDark;
    }
}
internal static class OverlayHelperServer
{
    internal static bool IsTestDisplay
    {
        get
        {
            string name = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
            return name.StartsWith("sx-", StringComparison.Ordinal) && Path.GetFileName(name) == name;
        }
    }
    internal static int Run(string[] args)
    {
        // Guard before initializing Avalonia, reading pixels, probing or opening ANY display.
        bool test = args.Contains("--overlay-test");
        if (test && (!IsTestDisplay || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SX_SANDBOX"))))
        { Console.Error.WriteLine("Refusing overlay test: requires sx- socket and SX_SANDBOX."); return 9; }
        var protocol = Console.Out;
        Console.SetOut(Console.Error);
        object writeLock = new();
        void Send(OverlayHelperResponse response)
        {
            lock (writeLock) { protocol.WriteLine(JsonConvert.SerializeObject(response)); protocol.Flush(); }
        }
        try
        {
            string directory = args[Array.IndexOf(args, "--overlay-helper") + 1];
            var request = JsonConvert.DeserializeObject<OverlayHelperRequest>(File.ReadAllText(Path.Combine(directory, "request.json"))) ?? throw new IOException("Missing request.");
            if (request.Test.Length > 0 && !test) throw new IOException("Test injection requires --overlay-test.");
            if (request.Test == "crash") throw new IOException("Injected helper failure.");
            if (request.Test == "heartbeat-only") while (true) { Send(new("heartbeat")); Thread.Sleep(100); }
            if (request.Test == "stop") { Send(new("heartbeat")); Thread.Sleep(Timeout.Infinite); }
            AppBuilder.Configure<OverlayHelperApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true })
                .UseSkia().WithInterFont().SetupWithoutStarting();
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            WaylandOverlay.Register();
            using var session = OverlaySessionFactory.Open();
            if (!session.Outputs.SequenceEqual(request.Outputs.Select(o => o.Output))) throw new IOException("Output layout changed since screenshot capture.");
            var renderers = request.Outputs.Select(f =>
            {
                if (Path.GetFileName(f.File) != f.File) throw new IOException("Invalid tile name.");
                var image = OverlayPixels.Read(Path.Combine(directory, f.File)) ?? throw new IOException("Invalid screenshot tile.");
                var r = f.Output.Logical;
                return new MonitorRenderer(f.Output.Name, new(r.X, r.Y, r.Width, r.Height), f.Output.Scale, image, takeScreenshotOwnership: true);
            }).ToArray();
            using var extension = new AnnotationOverlayExtension(session.Desktop, session.Outputs, new()
            { ImageEditorOptions = request.EditorOptions, RegionCaptureOptions = request.Options, EnableAnnotations = request.EnableAnnotations });
            extension.Layer.Workspace.SetWorkspaceBackground(ComposeBackground(directory, session.Desktop, request.Outputs, 1));
            var o = request.Options;
            var last = request.LastRegion;
            var options = new CaptureOptions
            {
                QuickCapture = o.QuickCapture, DetectWindows = o.DetectWindows, DetectControls = o.DetectControls, ActiveMonitorMode = o.ActiveMonitorMode,
                RightClick = Enum.Parse<HostAction>(o.RegionCaptureActionRightClick.ToString()), MiddleClick = Enum.Parse<HostAction>(o.RegionCaptureActionMiddleClick.ToString()),
                X1Click = Enum.Parse<HostAction>(o.RegionCaptureActionX1Click.ToString()), X2Click = Enum.Parse<HostAction>(o.RegionCaptureActionX2Click.ToString()),
                LastRegion = new(last.X, last.Y, last.Width, last.Height),
                Visuals = new() { BackgroundDimStrength = o.BackgroundDimStrength, ShowCenterCrosshair = o.ShowCenterCrosshair,
                    ShowScreenCrosshair = o.ShowScreenCrosshair, ShowMagnifier = o.ShowMagnifier, MagnifierSize = o.MagnifierSize,
                    MagnifierPixelCount = o.MagnifierPixelCount, UseSquareMagnifier = o.UseSquareMagnifier, ShowInfo = o.ShowInfo,
                    UseCustomInfoText = o.UseCustomInfoText, CustomInfoText = o.CustomInfoText }
            };
            var host = new OverlayHostLoop(session, renderers, options: options, extension: extension, windows: new Windows(request.Windows), initialPointer: request.InitialPointer);
            // Pump on the headless UI thread. Heartbeats certify progress on this SAME worker.
            // Esc remains on the independent native dispatch thread, even inside stalled Avalonia.
            using var emergency = new Timer(_ =>
            {
                if (session.CancelRequested) { Send(new("cancelled", "Escape")); Environment.Exit(0); }
            }, null, 10, 10);
            DateTime nextHeartbeat = DateTime.MinValue;
            while (!host.Completion.IsCompleted)
            {
                host.Controller.Invalidate(); // includes deferred popup/layout/animation changes
                host.Pump();
                if (request.Test == "toolbar" && !File.Exists(Path.Combine(directory, "toolbar.json")))
                {
                    var button = extension.Layer.Window.GetVisualDescendants().OfType<Button>().First(b =>
                        b.IsVisible && b.Tag != null && b.DataContext is ToolbarCustomizationItemViewModel { Tool: EditorTool.Rectangle });
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), extension.Layer.Window)!.Value;
                    using var file = OverlayPixels.Create(Path.Combine(directory, "toolbar.json"));
                    using var writer = new StreamWriter(file);
                    writer.Write(JsonConvert.SerializeObject(new[] { (int)point.X + session.Desktop.X, (int)point.Y + session.Desktop.Y }));
                }
                if (DateTime.UtcNow >= nextHeartbeat)
                { Send(new("heartbeat")); nextHeartbeat = DateTime.UtcNow.AddMilliseconds(200); }
                if (request.Test == "stall") Thread.Sleep(Timeout.Infinite);
                Thread.Sleep(8);
            }
            var result = host.Completion.GetAwaiter().GetResult();
            if (result.Capture is not { } capture)
            {
                if (result.CancellationReason == "Escape") Send(new("cancelled", "Escape"));
                else Send(new("error", result.CancellationReason));
            }
            else
            {
                var raw = capture.Bounds;
                var b = new LogicalRect(Math.Floor(raw.X), Math.Floor(raw.Y), Math.Ceiling(raw.Right) - Math.Floor(raw.X), Math.Ceiling(raw.Bottom) - Math.Floor(raw.Y));
                using var image = RenderResult(directory, extension.Layer, session.Desktop, request.Outputs, b);
                OverlayPixels.Write(Path.Combine(directory, "result.shm"), image);
                Send(new("ok", Selection: new((int)Math.Floor(b.X), (int)Math.Floor(b.Y), (int)Math.Ceiling(b.Width), (int)Math.Ceiling(b.Height)),
                    Modified: extension.Layer.HasAnnotations, Window: capture.Window?.Metadata is long handle ? handle : 0));
            }
            if (result.Outputs != null) foreach (var tile in result.Outputs) tile.Pixels.Dispose();
            foreach (var renderer in renderers) renderer.Dispose();
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); Send(new("error", e.Message)); return 1; }
    }
    private sealed class Windows(WindowRect[] windows) : IWindowRectProvider
    { public System.Collections.Generic.IEnumerable<WindowRect> GetRects(bool includeControls) => windows; }

    private static SKBitmap RenderResult(string directory, AnnotationLayerHost layer, OverlayRect desktop, FrozenOutput[] outputs, LogicalRect selection)
    {
        double scale = outputs.Where(o => Intersects(o.Output.Logical, selection)).Max(o => o.Output.Scale);
        using var background = ComposeBackground(directory, desktop, outputs, scale);
        return layer.RenderResult(background, new(selection.X - desktop.X, selection.Y - desktop.Y, selection.Width, selection.Height), scale)
            ?? throw new IOException("Annotation export failed.");
    }
    private static SKBitmap ComposeBackground(string directory, OverlayRect desktop, FrozenOutput[] outputs, double scale)
    {
        var background = new SKBitmap((int)Math.Ceiling(desktop.Width * scale), (int)Math.Ceiling(desktop.Height * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(background);
            canvas.Clear(SKColors.Transparent);
            foreach (var output in outputs)
            {
                // Source pixels are already in compositor orientation, including rotated outputs.
                using var image = OverlayPixels.Read(Path.Combine(directory, output.File));
                var r = output.Output.Logical;
                canvas.DrawBitmap(image, new SKRect((float)((r.X - desktop.X) * scale), (float)((r.Y - desktop.Y) * scale),
                    (float)((r.Right - desktop.X) * scale), (float)((r.Bottom - desktop.Y) * scale)));
            }
            return background;
        }
        catch { background.Dispose(); throw; }
    }
    private static bool Intersects(OverlayRect r, LogicalRect b) => r.X < b.Right && r.Right > b.X && r.Y < b.Bottom && r.Bottom > b.Y;
}
