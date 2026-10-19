using System.Drawing;

namespace ShareX.ScreenRecordingLib;

/// <summary>Linux screen recorder. Placeholder until the ffmpeg backend lands.</summary>
public sealed class ScreenRecorder : IDisposable, IAsyncDisposable
{
    private readonly RecordingOptions options;

    public Task<RecordingResult> Completion { get; } = new TaskCompletionSource<RecordingResult>().Task;
    public VideoEncoderInfo? Encoder { get; private set; }
    public CameraCaptureInfo? Camera { get; private set; }
    /// <summary>Nonfatal camera errors; screen and audio recording continue.</summary>
    public event Action<string>? Diagnostic;
    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public TimeSpan Elapsed => TimeSpan.Zero;

    public ScreenRecorder(RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options with { OutputPath = Path.GetFullPath(options.OutputPath) };
    }

    public Task PrepareAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public void Pause() => throw new NotImplementedException();
    public void Resume() => throw new NotImplementedException();
    public void Resume(Rectangle region) => throw new NotImplementedException();
    public void RequestStop() => throw new NotImplementedException();
    public Task<RecordingResult> StopAsync() => throw new NotImplementedException();
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
