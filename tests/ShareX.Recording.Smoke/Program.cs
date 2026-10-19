using System.Drawing;
using ShareX.ScreenRecordingLib;

string folder = Environment.GetEnvironmentVariable("SX_SANDBOX") ?? throw new Exception("Set SX_SANDBOX to an isolated /tmp directory.");
Directory.CreateDirectory(folder);
string mode = args.FirstOrDefault() ?? "mp4";
Rectangle region = mode == "fullscreen" ? new(2560, 0, 2560, 1440) : new(2600, 80, 640, 360);
if (args.Length >= 5) region = new(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
string extension = mode is "gif" or "webm" or "mkv" ? mode : "mp4";
string output = Path.Combine(folder, mode + "." + extension);
File.Delete(output);
Console.WriteLine("System: " + string.Join(", ", AudioCaptureDevices.GetSystemAudioDevices()));
Console.WriteLine("Microphones: " + string.Join(", ", AudioCaptureDevices.GetMicrophones()));
Console.WriteLine("Cameras: " + string.Join(", ", CameraCaptureDevices.GetCameras()));
await using ScreenRecorder recorder = new(new RecordingOptions
{
    OutputPath = output, Region = region, FramesPerSecond = 15, RequireHardwareEncoder = false,
    CaptureSystemAudio = mode == "audio", CaptureMicrophone = mode == "audio", Duration = mode == "stop-paused" ? TimeSpan.Zero : TimeSpan.FromSeconds(4),
    CustomCommands = mode == "custom" ? "-hide_banner -loglevel error -f image2pipe -framerate $fps$ -i pipe:0 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -y \"$output$\"" : "",
    CaptureCamera = mode == "camera", CameraShape = CameraOverlayShape.Circle,
    WindowHandle = mode == "window" ? (nint)Convert.ToInt64(args[5], 16) : 0
});
recorder.Diagnostic += Console.WriteLine;
await recorder.PrepareAsync();
await recorder.StartAsync();
await Task.Delay(1200);
recorder.Pause();
TimeSpan paused = recorder.Elapsed;
await Task.Delay(800);
if (Math.Abs((recorder.Elapsed - paused).TotalMilliseconds) > 30) throw new Exception("Paused clock advanced.");
if (mode == "stop-paused") recorder.RequestStop();
else if (mode == "window") recorder.Resume();
else recorder.Resume(mode == "move" ? new(region.X + 40, region.Y + 30, region.Width, region.Height) : region);
var result = await recorder.Completion.WaitAsync(TimeSpan.FromSeconds(45));
if (result.VideoFrames < (mode == "stop-paused" ? 10 : 30) || result.Duration.TotalSeconds < (mode == "stop-paused" ? 1 : 3) || result.Duration.TotalSeconds > 6) throw new Exception("Invalid recording duration/frame count: " + result);
if (recorder.IsRecording) throw new Exception("Recorder still active.");
Console.WriteLine(result);
