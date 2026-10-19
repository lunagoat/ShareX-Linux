using Avalonia.Controls;
using Avalonia.Threading;
using ShareX;
using ShareX.AvaloniaUI.Integration;
using ShareX.ScreenCaptureLib;
using System.Drawing;
using System.Reflection;
using NativeRecorder = ShareX.ScreenRecordingLib.ScreenRecorder;

string folder = Environment.GetEnvironmentVariable("SX_SANDBOX") ?? throw new Exception("Set SX_SANDBOX.");
Directory.CreateDirectory(folder);
AvaloniaBootstrapper.Initialize([], async () =>
{
    try
    {
        var settings = new TaskSettings();
        var state = typeof(TaskSettings).Assembly.GetType("ShareX.ApplicationState")!;
        state.GetProperty("DefaultTaskSettings", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, settings);
        state.GetProperty("Settings", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, new ApplicationConfig());
        var ffmpeg = new FFmpegOptionsWindow();
        ffmpeg.Title = "WP4 FFmpeg options smoke";
        ffmpeg.Show();
        await Task.Delay(1800);
        if (ffmpeg.Options.FFmpeg.VideoSource != "desktop") throw new Exception("Linux video source not selected.");
        await Snapshot("options", ffmpeg.Title!);
        ffmpeg.Close();
        Console.WriteLine("FFmpeg options opened, discovered Linux devices and closed.");
        var barType = typeof(TaskSettings).Assembly.GetType("ShareX.ScreenRecorderBarWindow")!;
        var bar = (Window)Activator.CreateInstance(barType, settings, new Rectangle(2600, 80, 640, 360), IntPtr.Zero)!;
        bar.Title = "WP4 recording setup smoke";
        var setup = (Task<bool>)barType.GetMethod("ShowSetupAsync")!.Invoke(bar, null)!;
        await Task.Delay(1800);
        await Snapshot("setup", bar.Title!);
        barType.GetMethod("RequestRecord")!.Invoke(bar, null);
        if (!await setup) throw new Exception("Setup did not accept record.");
        barType.GetMethod("Commit")!.Invoke(bar, [settings]);
        Console.WriteLine("Recording setup bar opened, accepted and committed.");
        string output = Path.Combine(folder, "ui.mp4"); File.Delete(output);
        await using NativeRecorder recorder = new(new ShareX.ScreenRecordingLib.RecordingOptions
        { OutputPath = output, Region = new(2600, 80, 640, 360), FramesPerSecond = 15, CaptureSystemAudio = false, RequireHardwareEncoder = false });
        var controls = new ScreenRecordWindow(new Rectangle(2600, 80, 640, 360)) { Title = "WP4 recording controls smoke", UseInProcessPause = true };
        controls.PauseRequested += recorder.Pause;
        controls.ResumeRequested += recorder.Resume;
        controls.StopRequested += recorder.RequestStop;
        controls.Show();
        controls.ChangeState(ScreenRecordState.BeforeStart);
        controls.StartCountdown(2000);
        await Task.Delay(400);
        await Snapshot("countdown", controls.Title!);
        controls.ChangeState(ScreenRecordState.AfterStart);
        await recorder.StartAsync();
        controls.ChangeState(ScreenRecordState.AfterRecordingStart);
        await Task.Delay(1100);
        await Snapshot("controls", controls.Title!);
        controls.PauseResumeRecording();
        if (!recorder.IsPaused) throw new Exception("Pause button did not pause engine.");
        await Task.Delay(500);
        controls.PauseResumeRecording();
        await Task.Delay(1100);
        controls.StartStopRecording();
        var result = await recorder.Completion;
        controls.ChangeState(ScreenRecordState.RecordingEnd);
        controls.Close(); controls.Dispose();
        Console.WriteLine("Recording controls start/pause/resume/stop: " + result);
        var personal = typeof(TaskSettings).Assembly.GetType("ShareX.PersonalPathManager")!;
        personal.GetProperty("CustomPersonalPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, folder);
        settings.OverrideScreenshotsFolder = true; settings.ScreenshotsFolder = Path.Combine(folder, "manager");
        Directory.CreateDirectory(settings.ScreenshotsFolder);
        settings.GeneralSettings.PlaySoundAfterAction = false;
        settings.GeneralSettings.PlaySoundAfterCapture = false;
        settings.GeneralSettings.PlaySoundAfterUpload = false;
        settings.AfterCaptureJob = AfterCaptureTasks.ShowAfterCaptureWindow;
        settings.CaptureSettings.ScreenRecordShowBar = false;
        settings.CaptureSettings.CaptureCustomRegion = new(2600, 80, 640, 360);
        settings.CaptureSettings.ScreenRecordFixedDuration = true;
        settings.CaptureSettings.ScreenRecordDuration = 2;
        settings.CaptureSettings.ScreenRecordFPS = 15;
        settings.CaptureSettings.ScreenRecordSystemAudio = true;
        settings.CaptureSettings.ScreenRecordSystemAudioDeviceId = "";
        settings.CaptureSettings.ScreenRecordRequireHardwareEncoder = false;
        foreach (ScreenRecordOutput format in new[] { ScreenRecordOutput.FFmpeg, ScreenRecordOutput.GIF })
        {
            settings.UploadSettings.NameFormatPattern = "manager-" + format;
            File.Delete(Path.Combine(settings.ScreenshotsFolder, "manager-" + format + (format == ScreenRecordOutput.GIF ? ".gif" : ".mp4")));
            ScreenRecordManager.StartStopRecording(format, ScreenRecordStartMethod.CustomRegion, settings);
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (ScreenRecordManager.IsRecording && DateTime.UtcNow < deadline) await Task.Delay(50);
            if (ScreenRecordManager.IsRecording) throw new Exception("ScreenRecordManager did not finish.");
            await Task.Delay(250);
            var desktop = (Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
            foreach (var window in desktop.Windows.ToArray()) if (window is AfterCaptureWindow) window.Close();
            string file = Path.Combine(settings.ScreenshotsFolder, "manager-" + format + (format == ScreenRecordOutput.GIF ? ".gif" : ".mp4"));
            if (!File.Exists(file) || new FileInfo(file).Length < 100) throw new Exception("Manager output missing: " + file);
            Console.WriteLine("ScreenRecordManager completed " + file + "; after-capture prompt canceled to avoid uploads.");
        }

    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    finally { AvaloniaBootstrapper.Shutdown(); }
}, () => { });
AvaloniaBootstrapper.Run();

async Task Snapshot(string name, string title)
{
    using var query = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("hyprctl")
    { ArgumentList = { "clients", "-j" }, RedirectStandardOutput = true, UseShellExecute = false })!;
    string text = await query.StandardOutput.ReadToEndAsync(); await query.WaitForExitAsync();
    using var data = System.Text.Json.JsonDocument.Parse(text);
    foreach (var client in data.RootElement.EnumerateArray())
    {
        if (client.GetProperty("pid").GetInt32() != Environment.ProcessId || client.GetProperty("title").GetString() != title) continue;
        var at = client.GetProperty("at"); var size = client.GetProperty("size");
        string geometry = $"{at[0].GetInt32()},{at[1].GetInt32()} {size[0].GetInt32()}x{size[1].GetInt32()}";
        using var grab = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("grim")
        { ArgumentList = { "-g", geometry, Path.Combine(folder, name + ".png") }, UseShellExecute = false })!;
        await grab.WaitForExitAsync();
        Console.WriteLine("UI snapshot " + name + ": " + geometry);
        break;
    }
}
