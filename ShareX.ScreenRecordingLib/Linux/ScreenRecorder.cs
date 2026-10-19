// ShareX - GPL v3. See LICENSE.txt.
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ShareX.ScreenRecordingLib;

/// <summary>One Linux recording session. Pause closes a segment; resume opens a new one.</summary>
public sealed class ScreenRecorder : IDisposable, IAsyncDisposable
{
    private readonly RecordingOptions options;
    private readonly object sync = new();
    private readonly Stopwatch clock = new();
    private readonly TaskCompletionSource<RecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? worker;
    private Rectangle region;
    private bool prepared, stop, paused, disposed, ownsOutput;
    private string? workspace;
    private string backend = "";
    private string videoEncoder = "libx264";
    private string? vaapiDevice;
    private long dropped;
    private int pauseGeneration;
    private bool cameraDisabled;
    public Task<RecordingResult> Completion => completed.Task;
    public VideoEncoderInfo? Encoder { get; private set; }
    public CameraCaptureInfo? Camera { get; private set; }
    public event Action<string>? Diagnostic;
    public bool IsRecording { get; private set; }
    public bool IsPaused { get { lock (sync) return paused; } }
    public TimeSpan Elapsed { get { lock (sync) return clock.Elapsed; } }

    public ScreenRecorder(RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options with
        {
            OutputPath = Path.GetFullPath(options.OutputPath),
            SystemAudioDeviceId = string.IsNullOrWhiteSpace(options.SystemAudioDeviceId) ? null : options.SystemAudioDeviceId,
            MicrophoneDeviceId = string.IsNullOrWhiteSpace(options.MicrophoneDeviceId) ? null : options.MicrophoneDeviceId,
            CameraDeviceId = string.IsNullOrWhiteSpace(options.CameraDeviceId) ? null : options.CameraDeviceId
        };
        region = options.Region;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (prepared) return;
        await LinuxProcess.Run(options.FFmpegPath, "-version").WaitAsync(cancellationToken).ConfigureAwait(false);
        bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        backend = wayland ? (File.Exists(LinuxProcess.Find("grim")) &&
            (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) ||
             !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK"))) ? "grim" : "portal") : "x11";
        if (backend == "portal" && !File.Exists(LinuxProcess.Find("gpu-screen-recorder")))
            throw new InvalidOperationException("Wayland recording on this compositor needs gpu-screen-recorder and a ScreenCast desktop portal. Install them with your distribution's package manager.");
        await SelectEncoder().ConfigureAwait(false);
        if (options.WindowHandle != 0) region = await WindowRegion().ConfigureAwait(false);
        if (region.Width < 2 || region.Height < 2) throw new ArgumentException("The capture target must be at least 2 × 2 pixels.");
        Encoder = new(videoEncoder, videoEncoder != "libx264", false);
        prepared = true;
        Diagnostic?.Invoke($"Linux capture backend: {backend}; output: {region.Width & ~1} × {region.Height & ~1}. Wayland uses logical coordinates.");
    }

    private async Task SelectEncoder()
    {
        if (File.Exists("/dev/nvidia0"))
        {
            try
            {
                await LinuxProcess.Run(options.FFmpegPath, "-v", "error", "-f", "lavfi", "-i", "color=size=64x64:rate=1", "-frames:v", "1", "-c:v", "h264_nvenc", "-f", "null", "-").ConfigureAwait(false);
                videoEncoder = "h264_nvenc";
                return;
            }
            catch (Exception ex) { Diagnostic?.Invoke("NVENC unavailable: " + ex.Message); }
        }
        if (Directory.Exists("/dev/dri"))
        {
            foreach (string device in Directory.GetFiles("/dev/dri", "renderD*"))
            {
                try
                {
                    await LinuxProcess.Run(options.FFmpegPath, "-v", "error", "-vaapi_device", device, "-f", "lavfi", "-i", "color=size=64x64:rate=1", "-vf", "format=nv12,hwupload", "-frames:v", "1", "-c:v", "h264_vaapi", "-f", "null", "-").ConfigureAwait(false);
                    videoEncoder = "h264_vaapi"; vaapiDevice = device;
                    return;
                }
                catch (Exception ex) { Diagnostic?.Invoke("VAAPI unavailable: " + ex.Message); }
            }
        }
        if (options.RequireHardwareEncoder)
            throw new InvalidOperationException("No usable NVENC/VAAPI encoder was found. Allow software encoding in recording settings, or make your GPU render device and FFmpeg hardware encoder available.");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker != null) throw new InvalidOperationException("A recorder instance can only be started once.");
            if (stop) throw new OperationCanceledException("Recording stopped before capture began.");
            worker = Task.Run(Run);
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(RequestStop);
        await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Pause()
    {
        lock (sync)
        {
            if (!IsRecording || paused || stop) return;
            paused = true;
            pauseGeneration++;
            clock.Stop();
        }
    }
    public void Resume() => ResumeCore(null);
    public void Resume(Rectangle region)
    {
        if (options.WindowHandle != 0) throw new InvalidOperationException("Window recording follows its target window.");
        if ((region.Width & ~1) != (this.region.Width & ~1) || (region.Height & ~1) != (this.region.Height & ~1))
            throw new ArgumentException("Moving a region cannot change its output dimensions.", nameof(region));
        ResumeCore(region);
    }
    private void ResumeCore(Rectangle? newRegion)
    {
        lock (sync)
        {
            if (!paused || stop) return;
            if (newRegion.HasValue) region = newRegion.Value;
            paused = false;
            clock.Start();
        }
    }
    public void RequestStop() { lock (sync) { stop = true; clock.Stop(); } }
    public Task<RecordingResult> StopAsync()
    {
        if (worker == null) throw new InvalidOperationException("Recording has not started.");
        RequestStop();
        return Completion;
    }
    private bool EndSegment { get { lock (sync) return stop || paused || (options.Duration > TimeSpan.Zero && clock.Elapsed >= options.Duration); } }
    private bool Stop { get { lock (sync) return stop || (options.Duration > TimeSpan.Zero && clock.Elapsed >= options.Duration); } }

    private async Task Run()
    {
        List<string> segments = [];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
            using (new FileStream(options.OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            ownsOutput = true;
            workspace = Path.Combine(Path.GetDirectoryName(options.OutputPath)!, $".sharex-recording-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workspace);
            lock (sync) { IsRecording = true; clock.Start(); }
            while (!Stop)
            {
                if (IsPaused) { await Task.Delay(20).ConfigureAwait(false); continue; }
                string segment = Path.Combine(workspace, $"segment-{segments.Count:D5}.mp4");
                try { await CaptureSegment(segment).ConfigureAwait(false); }
                catch (Exception ex) when (options.CaptureCamera && Camera != null && !cameraDisabled)
                {
                    cameraDisabled = true;
                    Camera = null;
                    Diagnostic?.Invoke("Camera capture failed; continuing screen and audio capture: " + ex.Message);
                    File.Delete(segment);
                    await CaptureSegment(segment).ConfigureAwait(false);
                }
                segments.Add(segment);
            }
            lock (sync) { clock.Stop(); IsRecording = false; }
            if (segments.Count == 0) throw new OperationCanceledException("Recording stopped before the first frame.");
            await FinalizeOutput(segments).ConfigureAwait(false);
            string probe = await LinuxProcess.Run("ffprobe", "-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames:format=duration", "-of", "json", options.OutputPath).ConfigureAwait(false);
            using JsonDocument data = JsonDocument.Parse(probe);
            long count = data.RootElement.GetProperty("streams").EnumerateArray()
                .Where(s => s.GetProperty("codec_type").GetString() == "video")
                .Select(s => long.Parse(s.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture)).FirstOrDefault();
            double duration = double.Parse(data.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            completed.TrySetResult(new(options.OutputPath, TimeSpan.FromSeconds(duration), count, dropped, 0, Encoder!));
        }
        catch (Exception ex)
        {
            if (ownsOutput) File.Delete(options.OutputPath);
            started.TrySetException(ex);
            completed.TrySetException(ex);
        }
        finally
        {
            lock (sync) { clock.Stop(); IsRecording = false; }
            if (workspace != null) Directory.Delete(workspace, true);
        }
    }

    private async Task CaptureSegment(string path)
    {
        int generation; lock (sync) generation = pauseGeneration;
        bool End() => EndSegment || generation != Volatile.Read(ref pauseGeneration);
        int width = region.Width & ~1, height = region.Height & ~1;
        if (backend == "portal") { await CapturePortal(path); return; }
        List<string> args = ["-hide_banner", "-loglevel", "warning", "-filter_complex_threads", "1", "-thread_queue_size", "64"];
        if (vaapiDevice != null) args.AddRange(["-vaapi_device", vaapiDevice]);
        if (backend == "grim") args.AddRange(["-f", "image2pipe", "-framerate", N(options.FramesPerSecond), "-probesize", "32", "-analyzeduration", "0", "-i", "pipe:0"]);
        else args.AddRange(["-f", "x11grab", "-draw_mouse", options.IncludeCursor ? "1" : "0", "-framerate", N(options.FramesPerSecond), "-video_size", $"{width}x{height}", "-i", $"{Environment.GetEnvironmentVariable("DISPLAY")}+{region.X},{region.Y}"]);
        if (backend == "x11" && options.WindowHandle != 0)
        {
            args.RemoveRange(args.Count - 2, 2);
            args.AddRange(["-window_id", options.WindowHandle.ToString(), "-i", Environment.GetEnvironmentVariable("DISPLAY") ?? ":0"]);
        }
        List<(int Index, float Gain)> audio = [];
        if (options.CaptureSystemAudio)
            AddAudio(options.SystemAudioDeviceId ?? AudioCaptureDevices.GetDefaultSystemAudioDevice()?.Id ?? "@DEFAULT_MONITOR@", options.SystemAudioGain);
        if (options.CaptureMicrophone)
            AddAudio(options.MicrophoneDeviceId ?? "default", options.MicrophoneGain);
        void AddAudio(string device, float gain)
        {
            args.AddRange(["-thread_queue_size", "64", "-f", "pulse", "-sample_rate", "48000", "-channels", "2", "-i", device]);
            audio.Add((audio.Count + 1, gain));
        }
        StringBuilder filters = new($"[0:v]scale={width}:{height},setsar=1[v]");
        string video = "[v]";
        if (options.CaptureCamera && !cameraDisabled)
        {
            CameraCaptureDevice? device = CameraCaptureDevices.GetCameras().FirstOrDefault(d => d.Id == options.CameraDeviceId) ??
                (options.CameraDeviceId == null ? CameraCaptureDevices.GetCameras().FirstOrDefault() : null);
            if (device == null || !File.Exists(device.Id)) Diagnostic?.Invoke("Camera unavailable; continuing screen and audio capture.");
            else
            {
                int cw = options.CameraResolution == CameraCaptureResolution.Size640x480 ? 640 : options.CameraResolution == CameraCaptureResolution.Size1920x1080 ? 1920 : 1280;
                int ch = cw == 640 ? 480 : cw == 1920 ? 1080 : 720;
                int index = audio.Count + 1;
                args.AddRange(["-thread_queue_size", "64", "-f", "v4l2", "-framerate", N(options.CameraFramesPerSecond), "-video_size", $"{cw}x{ch}", "-i", device.Id]);
                int overlayWidth = Math.Max(2, width * options.CameraWidthPercent / 100) & ~1;
                int overlayHeight = options.CameraShape == CameraOverlayShape.Circle ? overlayWidth : overlayWidth * ch / cw;
                overlayHeight = Math.Min(height, overlayHeight);
                int margin = Math.Min(options.CameraMargin, Math.Max(0, Math.Min(width - overlayWidth, height - overlayHeight)));
                string x = options.CameraPosition is CameraOverlayPosition.TopRight or CameraOverlayPosition.BottomRight ? $"W-w-{margin}" : N(margin);
                string y = options.CameraPosition is CameraOverlayPosition.BottomLeft or CameraOverlayPosition.BottomRight ? $"H-h-{margin}" : N(margin);
                filters.Append($";[{index}:v]");
                if (options.CameraShape == CameraOverlayShape.Circle) filters.Append("crop=min(iw\\,ih):min(iw\\,ih),");
                filters.Append($"scale={overlayWidth}:{overlayHeight},format=yuva420p");
                if (options.CameraShape == CameraOverlayShape.Circle) filters.Append(",geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='if(lte(pow(X-W/2,2)+pow(Y-H/2,2),pow(min(W,H)/2,2)),255,0)'");
                filters.Append($"[cam];[v][cam]overlay={x}:{y}:eof_action=pass[outv]");
                video = "[outv]";
                Camera = new(device.Name, cw, ch, options.CameraFramesPerSecond, "V4L2");
            }
        }
        for (int i = 0; i < audio.Count; i++)
            filters.Append($";[{audio[i].Index}:a]asetpts=PTS-STARTPTS,volume={audio[i].Gain.ToString(CultureInfo.InvariantCulture)},aresample=async=1:first_pts=0[a{i}]");
        if (audio.Count > 1) filters.Append(";[a0][a1]amix=inputs=2:duration=longest:normalize=0[a]");
        if (vaapiDevice != null) { filters.Append($";{video}format=nv12,hwupload[hw]"); video = "[hw]"; }
        args.AddRange(["-filter_complex", filters.ToString(), "-map", video]);
        if (audio.Count > 0) args.AddRange(["-map", audio.Count == 1 ? "[a0]" : "[a]", "-c:a", "aac", "-b:a", N(options.AudioBitrate), "-shortest"]);
        args.AddRange(["-c:v", videoEncoder, "-b:v", N(options.VideoBitrate), "-threads", "2"]);
        if (videoEncoder == "libx264") args.AddRange(["-preset", "ultrafast", "-pix_fmt", "yuv420p"]);
        args.AddRange(["-y", path]);
        if (!string.IsNullOrWhiteSpace(options.CustomCommands))
        {
            string command = options.CustomCommands
                .Replace("$fps$", N(options.FramesPerSecond), StringComparison.OrdinalIgnoreCase)
                .Replace("$area_x$", N(region.X), StringComparison.OrdinalIgnoreCase)
                .Replace("$area_y$", N(region.Y), StringComparison.OrdinalIgnoreCase)
                .Replace("$area_width$", N(region.Width), StringComparison.OrdinalIgnoreCase)
                .Replace("$area_height$", N(region.Height), StringComparison.OrdinalIgnoreCase)
                .Replace("$cursor$", options.IncludeCursor ? "1" : "0", StringComparison.OrdinalIgnoreCase)
                .Replace("$duration$", (options.Duration > TimeSpan.Zero ? Math.Max(0.1, (options.Duration - Elapsed).TotalSeconds) : 86400).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("$output$", path, StringComparison.OrdinalIgnoreCase);
            if (!options.CustomCommands.Contains("$output$", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Custom recording commands must use $output$ for the segment output path.");
            args = SplitArguments(command).ToList();
        }
        using Process encoder = LinuxProcess.Start(options.FFmpegPath, args, true);
        Task<string> errors = encoder.StandardError.ReadToEndAsync();
        Task<string> output = encoder.StandardOutput.ReadToEndAsync();
        try
        {
            if (backend == "grim")
            {
                Stopwatch timer = Stopwatch.StartNew();
                long written = 0;
                while (!End() || written == 0)
                {
                    Rectangle target = options.WindowHandle == 0 ? region : await WindowRegion().ConfigureAwait(false);
                    List<string> grab = ["-s", "1", "-t", "png", "-l", "0", "-g", $"{target.X},{target.Y} {target.Width}x{target.Height}"];
                    if (options.IncludeCursor) grab.Add("-c");
                    grab.Add("-");
                    byte[] png = await Grab(grab).ConfigureAwait(false);
                    long due = Math.Max(written + 1, (long)(timer.Elapsed.TotalSeconds * options.FramesPerSecond));
                    // Duplicate the newest frame when capture is slower than the requested rate, preserving A/V time.
                    while (written < due)
                    {
                        await encoder.StandardInput.BaseStream.WriteAsync(png).ConfigureAwait(false);
                        written++;
                        if (written < due) dropped++;
                    }
                    started.TrySetResult();
                    int delay = (int)(written * 1000.0 / options.FramesPerSecond - timer.Elapsed.TotalMilliseconds);
                    if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
                    if (Stop) break;
                }
                encoder.StandardInput.Close();
            }
            else
            {
                await Task.Delay(350).ConfigureAwait(false);
                if (encoder.HasExited) throw new IOException(await errors.ConfigureAwait(false));
                started.TrySetResult();
                while (!End() && !encoder.HasExited) await Task.Delay(20).ConfigureAwait(false);
                await encoder.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                encoder.StandardInput.Close();
            }
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            await encoder.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (encoder.ExitCode != 0) throw new IOException("FFmpeg recording failed: " + await errors.ConfigureAwait(false));
            string warning = await errors.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(warning)) Diagnostic?.Invoke(warning);
        }
        finally
        {
            if (!encoder.HasExited) { encoder.Kill(true); await encoder.WaitForExitAsync().ConfigureAwait(false); }
            await output.ConfigureAwait(false);
        }
    }

    private async Task<byte[]> Grab(List<string> args)
    {
        using Process grab = LinuxProcess.Start("grim", args);
        Task<string> error = grab.StandardError.ReadToEndAsync();
        using MemoryStream image = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        try
        {
            await grab.StandardOutput.BaseStream.CopyToAsync(image, timeout.Token).ConfigureAwait(false);
            await grab.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (grab.ExitCode != 0) throw new IOException("Wayland screen capture failed: " + await error.ConfigureAwait(false));
            return image.ToArray();
        }
        finally { if (!grab.HasExited) { grab.Kill(true); await grab.WaitForExitAsync().ConfigureAwait(false); } }
    }

    private async Task CapturePortal(string path)
    {
        List<string> args = ["-w", "portal", "-f", N(options.FramesPerSecond), "-k", "h264", "-encoder", "cpu", "-cursor", options.IncludeCursor ? "yes" : "no", "-restore-portal-session", "yes", "-portal-session-token-filepath", Path.Combine(workspace!, "portal-token"), "-o", path];
        if (options.CaptureSystemAudio) args.AddRange(["-a", options.SystemAudioDeviceId ?? "default_output"]);
        if (options.CaptureMicrophone) args.AddRange(["-a", options.MicrophoneDeviceId ?? "default_input"]);
        using Process capture = LinuxProcess.Start("gpu-screen-recorder", args);
        Task<string> errors = capture.StandardError.ReadToEndAsync();
        Task<string> output = capture.StandardOutput.ReadToEndAsync();
        try
        {
            while (!EndSegment && !capture.HasExited)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0) started.TrySetResult();
                await Task.Delay(50).ConfigureAwait(false);
            }
            if (!capture.HasExited) kill(capture.Id, 2);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            await capture.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (capture.ExitCode != 0 || !File.Exists(path)) throw new IOException("ScreenCast portal recording failed: " + await errors.ConfigureAwait(false));
            started.TrySetResult();
        }
        finally { if (!capture.HasExited) { capture.Kill(true); await capture.WaitForExitAsync().ConfigureAwait(false); } await output.ConfigureAwait(false); }
    }

    private Task<Rectangle> WindowRegion() => Task.Run(() => RecordingTargets.Window(options.WindowHandle));

    private async Task FinalizeOutput(List<string> segments)
    {
        string list = Path.Combine(workspace!, "segments.txt");
        await File.WriteAllLinesAsync(list, segments.Select(path => "file '" + Path.GetFileName(path) + "'")).ConfigureAwait(false);
        string extension = Path.GetExtension(options.OutputPath).ToLowerInvariant();
        List<string> args = ["-hide_banner", "-loglevel", "error", "-filter_complex_threads", "1", "-filter_threads", "1", "-f", "concat", "-safe", "0", "-i", list];
        if (vaapiDevice != null) args.InsertRange(0, ["-vaapi_device", vaapiDevice]);
        if (extension is ".m4a" or ".ogg" or ".opus" or ".mp3" or ".wav")
        {
            string codec = extension switch { ".ogg" => "libvorbis", ".opus" => "libopus", ".mp3" => "libmp3lame", ".wav" => "pcm_s16le", _ => "aac" };
            args.AddRange(["-vn", "-c:a", codec]);
        }
        else if (extension == ".gif") args.AddRange(["-an", "-filter_complex", "[0:v]split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0"]);
        else if (extension is ".webp" or ".apng") args.AddRange(["-an", "-c:v", extension == ".webp" ? "libwebp_anim" : "apng", "-loop", "0"]);
        else
        {
            args.AddRange(["-c:v", extension == ".webm" ? "libvpx-vp9" : videoEncoder, "-b:v", N(options.VideoBitrate), "-pix_fmt", "yuv420p", "-threads", "2"]);
            if (extension == ".webm") args.AddRange(["-deadline", "realtime", "-cpu-used", "6", "-c:a", "libopus"]);
            else
            {
                if (videoEncoder == "libx264") args.AddRange(["-preset", "ultrafast"]);
                if (vaapiDevice != null) args.AddRange(["-vf", "format=nv12,hwupload"]);
                args.AddRange(["-c:a", "aac", "-b:a", N(options.AudioBitrate)]);
            }
            if (extension == ".mp4") args.AddRange(["-movflags", "+faststart"]);
        }
        args.AddRange(SplitArguments(options.ExtraArguments));
        args.AddRange(["-y", options.OutputPath]);
        await LinuxProcess.Run(TimeSpan.FromHours(12), options.FFmpegPath, args.ToArray()).ConfigureAwait(false);
        Encoder = new(extension == ".gif" ? "gif" : extension == ".webm" ? "libvpx-vp9" : videoEncoder, extension is ".mp4" or ".mkv" or ".avi" && videoEncoder != "libx264", false);
    }

    /// <summary>Parse FFmpeg arguments without invoking a shell or expanding shell metacharacters.</summary>
    public static IReadOnlyList<string> SplitArguments(string value)
    {
        List<string> result = []; StringBuilder word = new(); char quote = '\0'; bool active = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length && (value[i + 1] is '\\' or '"' or '\'' || char.IsWhiteSpace(value[i + 1]))) { word.Append(value[++i]); active = true; }
            else if (quote != '\0') { if (c == quote) quote = '\0'; else word.Append(c); active = true; }
            else if (c is '\'' or '"') { quote = c; active = true; }
            else if (char.IsWhiteSpace(c)) { if (active) { result.Add(word.ToString()); word.Clear(); active = false; } }
            else { word.Append(c); active = true; }
        }
        if (quote != '\0') throw new ArgumentException("Unclosed quote in FFmpeg arguments.");
        if (active) result.Add(word.ToString());
        return result;
    }
    private static string N(int number) => number.ToString(CultureInfo.InvariantCulture);
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        RequestStop();
        if (worker != null) await worker.ConfigureAwait(false);
        else completed.TrySetCanceled();
        disposed = true;
    }
}
