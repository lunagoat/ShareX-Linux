// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ShareX.AvaloniaUI.Linux;
using ShareX.NativeOverlay;
using ShareX.NativeOverlay.Wayland;
using ShareX.OverlayHost;
using ShareX.OverlayRender;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;

namespace ShareX;

internal static class NativeRegionCapture
{
    internal static bool Choose(out string reason)
    {
        string? choice = Environment.GetEnvironmentVariable("SHAREX_OVERLAY");
        if (choice == "window") { reason = "SHAREX_OVERLAY=window"; return false; }
        if (!OperatingSystem.IsLinux() || !LinuxScreenCapture.IsWayland) { reason = "not a Wayland session"; return false; }
        WaylandOverlay.Register();
        if (!Supported()) { reason = "layer-shell unavailable"; return false; }
        reason = choice == "native" ? "SHAREX_OVERLAY=native" : "layer-shell available";
        return true;
    }

    // The probe connects to the compositor (tens of milliseconds) and the answer cannot change within a session.
    private static (string Display, bool Supported)? probe;
    private static bool Supported()
    {
        string display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
        if (probe is { } known && known.Display == display) return known.Supported;
        bool result = OverlaySessionFactory.IsSupported();
        probe = (display, result);
        return result;
    }

    /// <summary>A helper process started before the screenshot exists, so its startup overlaps the capture.</summary>
    internal sealed class PrewarmedHelper : IDisposable
    {
        public string Directory = "";
        public Process Child = null!;
        public (int Parent, string Birth) Identity;
        public Task Errors = Task.CompletedTask;
        public IReadOnlyList<OverlayOutput> Outputs = [];
        public bool Consumed;
        public void Dispose()
        {
            if (Consumed) return;
            Consumed = true;
            KillVerified(Child, Identity);
            try { Child.WaitForExit(3000); } catch (Exception) { }
            Child.Dispose();
            try { System.IO.Directory.Delete(Directory, true); } catch (Exception) { }
        }
    }

    /// <summary>Starts the helper and writes everything it needs except the screenshot. Null when the native overlay is not used or cannot be prepared.</summary>
    internal static PrewarmedHelper? Prewarm(ShareX.ScreenCaptureLib.RegionCaptureOptions options, ShareX.ImageEditor.Integration.ImageEditorOptions editorOptions, bool enableAnnotations, string test = "")
    {
        PrewarmedHelper? helper = null;
        try
        {
            if (!Choose(out _)) return null;
            string directory = OverlayPixels.CreateDirectory();
            helper = new PrewarmedHelper { Directory = directory };
            (helper.Child, helper.Identity, helper.Errors) = StartChild(directory, test);
            var outputs = WaylandOverlay.GetOutputs();
            helper.Outputs = outputs;
            var (windows, pointer) = Discover(outputs);
            var last = RegionCaptureIntegration.LastRegionRectangle;
            var message = new OverlayHelperRequest { Outputs = outputs.Select((o, i) => new FrozenOutput(o, i + ".shm")).ToArray(), Options = options,
                EditorOptions = editorOptions, EnableAnnotations = enableAnnotations, Windows = windows,
                LastRegion = new(last.X, last.Y, last.Width, last.Height), InitialPointer = pointer, TilesPending = true, Test = test };
            // The helper polls for request.json, so publish it complete: write a temporary file, then rename.
            string temporary = Path.Combine(directory, "request.json.tmp");
            using (var stream = OverlayPixels.Create(temporary))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonConvert.SerializeObject(message));
            File.Move(temporary, Path.Combine(directory, "request.json"));
            return helper;
        }
        catch (Exception e)
        {
            Log("Native overlay prewarm failed (continuing without it): " + e.Message);
            helper?.Dispose();
            return null;
        }
    }

    private static (WindowRect[] Windows, LogicalPoint Pointer) Discover(IReadOnlyList<OverlayOutput> outputs)
    {
        WindowRect[] windows = [];
        try { windows = LinuxCaptureWindows.GetWindows().Select(w => new WindowRect(new(w.Rectangle.X, w.Rectangle.Y, w.Rectangle.Width, w.Rectangle.Height), Metadata: w.Handle.ToInt64())).ToArray(); }
        catch (Exception e) { Log("Native overlay window discovery: " + e.Message); }
        var initial = outputs[0].Logical;
        var pointer = new LogicalPoint(initial.X + initial.Width / 2, initial.Y + initial.Height / 2);
        try { var position = LinuxScreenCapture.CursorPosition; pointer = new(position.X, position.Y); }
        catch (Exception e) { Log("Native overlay pointer discovery: " + e.Message); }
        return (windows, pointer);
    }

    internal static async Task<AvaloniaRegionCaptureResult?> CaptureAsync(AvaloniaRegionCaptureRequest request, PrewarmedHelper? prewarmed = null)
    {
        bool native = Choose(out string reason);
        if (!native) prewarmed?.Dispose();
        return await CaptureWithFallbackAsync(request, native, reason, () => RunAsync(request, "", prewarmed), () => RegionCaptureIntegration.CaptureAsync(request));
    }
    internal static async Task<AvaloniaRegionCaptureResult?> CaptureWithFallbackAsync(AvaloniaRegionCaptureRequest request,
        bool native, string reason, Func<Task<AvaloniaRegionCaptureResult?>> run, Func<Task<AvaloniaRegionCaptureResult?>> window)
    {
        if (native)
        {
            Log("region selection: native layer-shell overlay (" + reason + ")");
            try
            {
                var result = await run();
                request.Screenshot.Dispose(); request.CursorBitmap?.Dispose();
                if (result != null) RegionCaptureIntegration.LastRegionRectangle = result.ScreenRectangle;
                return result;
            }
            catch (Exception e) { reason = "native helper failed: " + e.Message; }
        }
        Log("region selection: window overlay (" + reason + ")");
        return await window();
    }
    internal static void Log(string message)
    {
        if (LinuxCaptureOverlay.Diagnostic is { } diagnostic) diagnostic(message);
        else ShareX.HelpersLib.DebugHelper.WriteLine(message);
    }
    internal static async Task<AvaloniaRegionCaptureResult?> RunAsync(AvaloniaRegionCaptureRequest request, string test = "", PrewarmedHelper? prewarmed = null)
    {
        var timer = Stopwatch.StartNew();
        bool timing = Environment.GetEnvironmentVariable("SHAREX_OVERLAY_TIMING") == "1";
        void Mark(string stage) { if (timing) Log($"timing +{timer.ElapsedMilliseconds} ms since native capture start: {stage}"); }
        string directory = prewarmed?.Directory ?? OverlayPixels.CreateDirectory();
        try
        {
            var outputs = prewarmed?.Outputs ?? WaylandOverlay.GetOutputs();
            Mark("outputs queried");
            var frozen = outputs.Select((output, i) =>
            {
                using var tile = SplitScreenshot(request.Screenshot, request.ScreenBounds, output);
                string name = i + ".shm";
                OverlayPixels.Write(Path.Combine(directory, name), tile);
                return new FrozenOutput(output, name);
            }).ToArray();
            Mark("screenshot tiles written");
            if (prewarmed != null)
            {
                // The helper has been starting up since before the screenshot; tell it the tiles are complete.
                using (OverlayPixels.Create(Path.Combine(directory, "tiles.ready"))) { }
                prewarmed.Consumed = true;
                Mark("helper told the screenshot is ready");
                return await SuperviseChildAsync(prewarmed.Child, prewarmed.Identity, prewarmed.Errors, directory, null).ConfigureAwait(false);
            }
            var (windows, pointer) = Discover(outputs);
            Mark("windows and pointer discovered");
            var last = RegionCaptureIntegration.LastRegionRectangle;
            var message = new OverlayHelperRequest { Outputs = frozen, Options = request.RegionCaptureOptions,
                EditorOptions = request.ImageEditorOptions, EnableAnnotations = request.EnableAnnotations, Windows = windows,
                LastRegion = new(last.X, last.Y, last.Width, last.Height), InitialPointer = pointer, Test = test };
            using var stream = OverlayPixels.Create(Path.Combine(directory, "request.json"));
            using (var writer = new StreamWriter(stream, leaveOpen: true)) writer.Write(JsonConvert.SerializeObject(message));
            stream.Close();
            Mark("request written, starting helper");
            return await SuperviseAsync(directory, test).ConfigureAwait(false);
        }
        finally { try { Directory.Delete(directory, true); } catch (DirectoryNotFoundException) { } }
    }

    internal static SKBitmap SplitScreenshot(SKBitmap screenshot, Rectangle bounds, OverlayOutput output)
    {
        var r = output.Logical;
        if (!bounds.Contains(new Rectangle(r.X, r.Y, r.Width, r.Height)))
            throw new InvalidOperationException("Frozen desktop does not include every native output.");
        var tile = new SKBitmap((int)Math.Ceiling(r.Width * output.Scale), (int)Math.Ceiling(r.Height * output.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(tile);
            double sx = (double)screenshot.Width / bounds.Width, sy = (double)screenshot.Height / bounds.Height;
            canvas.DrawBitmap(screenshot, new SKRect((float)((r.X - bounds.X) * sx), (float)((r.Y - bounds.Y) * sy),
                (float)((r.Right - bounds.X) * sx), (float)((r.Bottom - bounds.Y) * sy)), new SKRect(0, 0, tile.Width, tile.Height));
            return tile;
        }
        catch { tile.Dispose(); throw; }
    }

    internal static ProcessStartInfo StartInfo(string directory, string test)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No application executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet") start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--overlay-helper"); start.ArgumentList.Add(directory);
        if (test.Length > 0) start.ArgumentList.Add("--overlay-test");
        return start;
    }
    private static (int Parent, string Birth) ChildIdentity(int pid)
    {
        string stat = File.ReadAllText($"/proc/{pid}/stat");
        string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return (int.Parse(fields[1]), fields[19]); // ppid and kernel process start ticks
    }
    internal static (Process Child, (int Parent, string Birth) Identity, Task Errors) StartChild(string directory, string test)
    {
        var child = Process.Start(StartInfo(directory, test)) ?? throw new IOException("Helper failed to start.");
        var identity = ChildIdentity(child.Id);
        // Drain stderr independently so a full diagnostics pipe can never stop rendering/heartbeats.
        Task errors = Task.Run(async () => { while (await child.StandardError.ReadLineAsync() is { } line) Log("overlay-helper: " + line); });
        return (child, identity, errors);
    }
    /// <summary>Only a Process.Start child, with the same kernel start ticks and a verified parent PID.
    /// Never target a process name or kill a compositor/application process tree.</summary>
    internal static void KillVerified(Process child, (int Parent, string Birth) identity)
    {
        if (child.HasExited) return;
        try
        {
            var current = ChildIdentity(child.Id);
            if (current.Parent == Environment.ProcessId && current.Birth == identity.Birth) child.Kill();
        }
        catch (Exception) when (child.HasExited) { }
    }
    internal static async Task<AvaloniaRegionCaptureResult?> SuperviseAsync(string directory, string test, TimeSpan? hardTimeout = null)
    {
        var (child, identity, errors) = StartChild(directory, test);
        return await SuperviseChildAsync(child, identity, errors, directory, hardTimeout).ConfigureAwait(false);
    }
    internal static async Task<AvaloniaRegionCaptureResult?> SuperviseChildAsync(Process child, (int Parent, string Birth) identity, Task errors, string directory, TimeSpan? hardTimeout)
    {
        using var owned = child;
        using var hard = new CancellationTokenSource(hardTimeout ?? TimeSpan.FromSeconds(120));
        try
        {
            bool ready = false;
            while (true)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(hard.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(ready ? 3 : 20));
                string? line;
                try { line = await child.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw new TimeoutException(hard.IsCancellationRequested ? "helper hard timeout" : "helper heartbeat/startup timeout"); }
                if (line == null) throw new IOException("Helper exited before returning a result.");
                var response = JsonConvert.DeserializeObject<OverlayHelperResponse>(line) ?? throw new IOException("Invalid helper response.");
                if (response.Status == "heartbeat") { ready = true; continue; }
                if (response.Status == "cancelled") return null;
                if (response.Status != "ok" || response.Selection is not { } selection) throw new IOException(response.Reason ?? "Invalid helper result.");
                var image = OverlayPixels.Read(Path.Combine(directory, "result.shm")) ?? throw new IOException("Missing helper image.");
                var rectangle = new Rectangle(selection.X, selection.Y, selection.Width, selection.Height);
                return new(image, rectangle, response.Window == 0 ? null : new ShareX.HelpersLib.WindowInfo((nint)response.Window), response.Modified);
            }
        }
        finally
        {
            KillVerified(child, identity);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
        }
    }
}
