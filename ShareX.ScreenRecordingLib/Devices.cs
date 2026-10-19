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
    public static IReadOnlyList<CameraCaptureDevice> GetCameras() => [];
}

public static class AudioCaptureDevices
{
    public static IReadOnlyList<AudioCaptureDevice> GetMicrophones() => [];
    public static IReadOnlyList<AudioCaptureDevice> GetSystemAudioDevices() => [];
    public static AudioCaptureDevice? GetDefaultSystemAudioDevice() => null;
    public static AudioCaptureDevice? GetDefaultMicrophone() => null;
}
