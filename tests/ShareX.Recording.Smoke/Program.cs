using System.Drawing;
using ShareX.ScreenRecordingLib;

string folder = Environment.GetEnvironmentVariable("SX_SANDBOX") ?? throw new Exception("Set SX_SANDBOX to an isolated /tmp directory.");
Directory.CreateDirectory(folder);
string mode = args.FirstOrDefault() ?? "mp4";
Rectangle region = mode == "fullscreen" ? new(2560, 0, 2560, 1440) : new(2600, 80, 640, 360);
if (args.Length >= 5) region = new(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
string extension = mode is "gif" or "mkv" ? mode : mode is "webm" or "vp9" ? "webm" : "mp4";
string output = Path.Combine(folder, mode + "." + extension);
File.Delete(output);
Console.WriteLine("System: " + string.Join(", ", AudioCaptureDevices.GetSystemAudioDevices()));
Console.WriteLine("Microphones: " + string.Join(", ", AudioCaptureDevices.GetMicrophones()));
Console.WriteLine("Cameras: " + string.Join(", ", CameraCaptureDevices.GetCameras()));

string extraArgs = mode switch
{
    "libx265" => "-c:v libx265 -crf 28 -tag:v hvc1",
    "vp9" => "-c:v libvpx-vp9 -b:v 1000k",
    _ => ""
};

TimeSpan duration = mode switch
{
    "stop-paused" or "interactive-stop" => TimeSpan.Zero,
    "duration" => TimeSpan.FromSeconds(5),
    _ => TimeSpan.FromSeconds(4)
};

await using ScreenRecorder recorder = new(new RecordingOptions
{
    OutputPath = output, Region = region, FramesPerSecond = 15, RequireHardwareEncoder = false,
    CaptureSystemAudio = mode == "audio", CaptureMicrophone = mode == "audio",
    Duration = duration,
    IncludeCursor = mode != "cursor-off",
    ExtraArguments = extraArgs,
    CustomCommands = mode == "custom" ? "-hide_banner -loglevel error -f image2pipe -framerate $fps$ -i pipe:0 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -y \"$output$\"" : "",
    CaptureCamera = mode == "camera", CameraShape = CameraOverlayShape.Circle,
    WindowHandle = mode == "window" ? (nint)Convert.ToInt64(args[5], 16) : 0
});
recorder.Diagnostic += Console.WriteLine;
await recorder.PrepareAsync();
await recorder.StartAsync();

if (mode == "duration")
{
    // Duration limit automatically terminates the recording
}
else if (mode == "interactive-stop")
{
    await Task.Delay(2500);
    recorder.RequestStop();
}
else
{
    await Task.Delay(1200);
    recorder.Pause();
    TimeSpan paused = recorder.Elapsed;
    await Task.Delay(800);
    if (Math.Abs((recorder.Elapsed - paused).TotalMilliseconds) > 30) throw new Exception("Paused clock advanced.");
    if (mode == "stop-paused") recorder.RequestStop();
    else if (mode == "window") recorder.Resume();
    else recorder.Resume(mode == "move" ? new(region.X + 40, region.Y + 30, region.Width, region.Height) : region);
}

var result = await recorder.Completion.WaitAsync(TimeSpan.FromSeconds(45));
int minFrames = mode switch { "stop-paused" => 10, "interactive-stop" => 20, _ => 30 };
double minSec = mode switch { "stop-paused" => 1.0, "interactive-stop" => 2.0, _ => 3.0 };
double maxSec = mode switch { "stop-paused" => 2.5, "interactive-stop" => 4.0, "duration" => 6.5, _ => 6.0 };
if (result.VideoFrames < minFrames || result.Duration.TotalSeconds < minSec || result.Duration.TotalSeconds > maxSec) throw new Exception("Invalid recording duration/frame count: " + result);
if (recorder.IsRecording) throw new Exception("Recorder still active.");
Console.WriteLine(result);

