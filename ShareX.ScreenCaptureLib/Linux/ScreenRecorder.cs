// ShareX - GPL v3. See LICENSE.txt.
using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using NativeRecorder = ShareX.ScreenRecordingLib.ScreenRecorder;

namespace ShareX.ScreenCaptureLib
{
    /// <summary>Compatibility adapter for the existing FFmpeg recording task flow.</summary>
    public class ScreenRecorder : IDisposable
    {
        public bool IsRecording { get; private set; }
        public int FPS { get; set; }
        public float DurationSeconds { get; set; }
        public Rectangle CaptureRectangle { get; private set; }
        public string CachePath { get; private set; }
        public ScreenRecordOutput OutputType { get; private set; }
        public ScreenRecordingOptions Options { get; set; }
        public event Action RecordingStarted;
        public delegate void ProgressEventHandler(int progress);
        public event ProgressEventHandler EncodingProgressChanged;
        private FFmpegCLIManager ffmpeg;
        private NativeRecorder recorder;
        private bool stopRequested;

        public ScreenRecorder(ScreenRecordOutput outputType, ScreenRecordingOptions options, Screenshot screenshot, Rectangle captureRectangle)
        {
            Options = options; FPS = options.FPS; DurationSeconds = options.Duration;
            CaptureRectangle = captureRectangle; CachePath = options.OutputPath; OutputType = outputType;
            ffmpeg = new FFmpegCLIManager(options.FFmpeg.FFmpegPath) { ShowError = true };
            ffmpeg.EncodeStarted += () => RecordingStarted?.Invoke();
            ffmpeg.EncodeProgressChanged += OnEncodingProgressChanged;
        }

        public void StartRecording()
        {
            IsRecording = true;
            try
            {
                string output = Path.ChangeExtension(Options.OutputPath, Options.IsLossless ? "mp4" : Options.FFmpeg.Extension);
                recorder = new NativeRecorder(new ShareX.ScreenRecordingLib.RecordingOptions
                {
                    OutputPath = output, Region = CaptureRectangle, FramesPerSecond = FPS,
                    IncludeCursor = Options.DrawCursor, RequireHardwareEncoder = false,
                    CaptureSystemAudio = Options.FFmpeg.IsAudioSourceSelected,
                    SystemAudioDeviceId = Options.FFmpeg.AudioSource,
                    Duration = TimeSpan.FromSeconds(DurationSeconds), FFmpegPath = Options.FFmpeg.FFmpegPath,
                    ExtraArguments = Options.IsLossless ? "-crf 0" : Options.FFmpeg.UserArgs,
                    CustomCommands = !Options.IsLossless && Options.FFmpeg.UseCustomCommands ? Options.FFmpeg.CustomCommands : ""
                });
                if (stopRequested) return;
                recorder.StartAsync().GetAwaiter().GetResult();
                RecordingStarted?.Invoke();
                if (stopRequested) recorder.RequestStop();
                recorder.Completion.GetAwaiter().GetResult();
            }
            finally { IsRecording = false; }
        }
        public void StopRecording() { stopRequested = true; recorder?.RequestStop(); ffmpeg?.Close(); }
        public void SaveAsGIF(string path, GIFQuality quality) => FFmpegEncodeAsGIF(Options.OutputPath, path);
        public bool FFmpegEncodeVideo(string input, string output)
        {
            FileHelpers.CreateDirectoryFromFilePath(output);

            Options.IsRecording = false;
            Options.IsLossless = false;
            Options.InputPath = input;
            Options.OutputPath = output;

            try
            {
                ffmpeg.TrackEncodeProgress = true;

                return ffmpeg.Run(Options.GetFFmpegCommands());
            }
            finally
            {
                ffmpeg.TrackEncodeProgress = false;
            }
        }

        public bool FFmpegEncodeAsGIF(string input, string output)
        {
            FileHelpers.CreateDirectoryFromFilePath(output);

            try
            {
                ffmpeg.TrackEncodeProgress = true;

                StringBuilder args = new StringBuilder();

                args.Append($"-i \"{input}\" ");

                // https://ffmpeg.org/ffmpeg-filters.html#palettegen-1
                args.Append($"-lavfi \"palettegen=stats_mode={Options.FFmpeg.GIFStatsMode}[palette],");

                // https://ffmpeg.org/ffmpeg-filters.html#paletteuse
                args.Append($"[0:v][palette]paletteuse=dither={Options.FFmpeg.GIFDither}");

                if (Options.FFmpeg.GIFDither == FFmpegPaletteUseDither.bayer)
                {
                    args.Append($":bayer_scale={Options.FFmpeg.GIFBayerScale}");
                }

                if (Options.FFmpeg.GIFStatsMode == FFmpegPaletteGenStatsMode.single)
                {
                    args.Append(":new=1");
                }

                args.Append("\" ");
                args.Append("-y ");
                args.Append($"\"{output}\"");

                return ffmpeg.Run(args.ToString());
            }
            finally
            {
                ffmpeg.TrackEncodeProgress = false;
            }
        }


        private void OnEncodingProgressChanged(float progress) => EncodingProgressChanged?.Invoke((int)progress);
        public void Dispose() { recorder?.Dispose(); ffmpeg?.Dispose(); }
    }
}
