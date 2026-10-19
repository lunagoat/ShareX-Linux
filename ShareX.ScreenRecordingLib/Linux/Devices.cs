// ShareX - GPL v3. See LICENSE.txt.
using System.Text.Json;

namespace ShareX.ScreenRecordingLib;

/// <summary>An audio endpoint. Id can be passed to RecordingOptions.MicrophoneDeviceId or SystemAudioDeviceId.</summary>
public sealed record AudioCaptureDevice(string Id, string Name);

/// <summary>A camera. Id is the V4L2 device node.</summary>
public sealed record CameraCaptureDevice(string Id, string Name);

public enum CameraOverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight }
public enum CameraOverlayShape { Rectangle, Circle }
public enum CameraCaptureResolution { Size640x480, Size1280x720, Size1920x1080 }

/// <summary>The camera's negotiated mode; actual delivery depends on the device.</summary>
public sealed record CameraCaptureInfo(string Name, int Width, int Height, double FramesPerSecond, string PixelFormat);

public static class CameraCaptureDevices
{
    public static IReadOnlyList<CameraCaptureDevice> GetCameras()
    {
        if (!Directory.Exists("/sys/class/video4linux")) return [];
        return Directory.GetDirectories("/sys/class/video4linux").Order()
            .Select(path => new CameraCaptureDevice("/dev/" + Path.GetFileName(path),
                File.ReadAllText(Path.Combine(path, "name")).Trim())).ToArray();
    }
}

public static class AudioCaptureDevices
{
    private static AudioCaptureDevice[] Sources(bool monitors)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(LinuxProcess.Run("pactl", "-f", "json", "list", "sources").GetAwaiter().GetResult());
            return document.RootElement.EnumerateArray()
                .Where(source => source.GetProperty("name").GetString()!.EndsWith(".monitor") == monitors)
                .Select(source => new AudioCaptureDevice(source.GetProperty("name").GetString()!, source.GetProperty("description").GetString()!)).ToArray();
        }
        catch { return []; }
    }
    public static IReadOnlyList<AudioCaptureDevice> GetMicrophones() => Sources(false);
    public static IReadOnlyList<AudioCaptureDevice> GetSystemAudioDevices() => Sources(true);
    public static AudioCaptureDevice? GetDefaultSystemAudioDevice() => Default(true);
    public static AudioCaptureDevice? GetDefaultMicrophone() => Default(false);
    private static AudioCaptureDevice? Default(bool monitor)
    {
        try
        {
            string name = LinuxProcess.Run("pactl", monitor ? "get-default-sink" : "get-default-source").GetAwaiter().GetResult().Trim();
            if (monitor) name += ".monitor";
            return Sources(monitor).FirstOrDefault(source => source.Id == name);
        }
        catch { return null; }
    }
}
