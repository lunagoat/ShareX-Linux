#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.Tools;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;
using NativeRecordingOptions = ShareX.ScreenRecordingLib.RecordingOptions;
using NativeScreenRecorder = ShareX.ScreenRecordingLib.ScreenRecorder;

namespace ShareX
{
    public static class ScreenRecordManager
    {
        public static bool IsRecording { get; private set; }

        private static NativeScreenRecorder nativeRecorder;
        private static ScreenRecordWindow recordForm;
        private static ScreenRecorderBarWindow configurationBar;

        public static async void StartStopRecording(ScreenRecordOutput outputType, ScreenRecordStartMethod startMethod, TaskSettings taskSettings)
        {
            if (IsRecording)
            {
                if (configurationBar != null) configurationBar.RequestRecord();
                else recordForm?.StartStopRecording();
                return;
            }
            try { await StartRecording(outputType, taskSettings, startMethod); }
            catch (Exception ex)
            {
                IsRecording = false;
                DebugHelper.WriteException(ex);
                MessageBox.Show(ex.Message, "ShareX recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void StopRecording()
        {
            configurationBar?.Cancel();
            nativeRecorder?.RequestStop();
        }
        public static void PauseScreenRecording() => recordForm?.PauseResumeRecording();
        public static void AbortRecording() { configurationBar?.Cancel(); recordForm?.AbortRecording(); }

        private static async Task StartRecording(ScreenRecordOutput outputType, TaskSettings taskSettings, ScreenRecordStartMethod startMethod)
        {
            if (!TaskHelpers.CheckFFmpeg(taskSettings)) return;
            if (taskSettings.GeneralSettings.ToastWindowAutoHide) NotificationWindow.CloseActiveWindow();
            Rectangle captureRectangle = Rectangle.Empty;
            IntPtr captureWindow = IntPtr.Zero;
            TaskMetadata metadata = new();
            switch (startMethod)
            {
                case ScreenRecordStartMethod.Region:
                    var selection = await RegionCaptureTasks.GetRectangleRegionAsync(taskSettings.CaptureSettings.RegionCaptureOptions);
                    if (selection == null) return;
                    captureRectangle = selection.Value.Rectangle;
                    metadata.UpdateInfo(selection.Value.WindowInfo);
                    break;
                case ScreenRecordStartMethod.ActiveWindow:
                    (captureRectangle, captureWindow) = ShareX.ScreenRecordingLib.RecordingTargets.ActiveWindow();
                    break;
                case ScreenRecordStartMethod.CustomRegion:
                    captureRectangle = taskSettings.CaptureSettings.CaptureCustomRegion;
                    break;
                case ScreenRecordStartMethod.LastRegion:
                    captureRectangle = ApplicationState.Settings.ScreenRecordRegion;
                    break;
            }
            Rectangle screens = ShareX.ScreenRecordingLib.RecordingTargets.DesktopBounds() ?? CaptureHelpers.GetScreenBounds();
            captureRectangle = CaptureHelpers.EvenRectangleSize(Rectangle.Intersect(captureRectangle, screens));
            if (IsRecording || !captureRectangle.IsValid() || nativeRecorder != null) return;
            IsRecording = true;
            if (taskSettings.CaptureSettings.ScreenRecordShowBar)
            {
                try
                {
                    configurationBar = new(taskSettings, captureRectangle, captureWindow);
                    bool record = await configurationBar.ShowSetupAsync();
                    configurationBar.Commit(taskSettings);
                    SettingManager.SaveApplicationConfigAsync();
                    SettingManager.SaveHotkeysConfigAsync();
                    if (!record) { IsRecording = false; return; }
                    captureRectangle = configurationBar.RecordingRegion;
                    captureWindow = configurationBar.CaptureWindow;
                }
                finally { configurationBar?.Cancel(); configurationBar = null; }
            }
            bool gif = outputType == ScreenRecordOutput.GIF || (!taskSettings.CaptureSettings.ScreenRecordUseNative && taskSettings.CaptureSettings.FFmpegOptions.VideoCodec == FFmpegVideoCodec.gif);
            int fps = gif ? taskSettings.CaptureSettings.GIFFPS : taskSettings.CaptureSettings.ScreenRecordFPS;
            string extension = gif ? "gif" : taskSettings.CaptureSettings.ScreenRecordUseNative ? "mp4" : taskSettings.CaptureSettings.FFmpegOptions.Extension;
            ApplicationState.Settings.ScreenRecordRegion = captureRectangle;
            recordForm = new ScreenRecordWindow(captureRectangle)
            {
                ActivateWindow = startMethod == ScreenRecordStartMethod.Region,
                Duration = taskSettings.CaptureSettings.ScreenRecordFixedDuration ? taskSettings.CaptureSettings.ScreenRecordDuration : 0,
                AskConfirmationOnAbort = taskSettings.CaptureSettings.ScreenRecordAskConfirmationOnAbort,
                ShowRecordingTimer = taskSettings.CaptureSettings.ScreenRecordShowTimer,
                ShowRecordingButtonLabels = taskSettings.CaptureSettings.ScreenRecordShowButtonLabels,
                UseInProcessPause = true
            };
            recordForm.PauseRequested += () => nativeRecorder?.Pause();
            recordForm.ResumeRequested += () =>
            {
                if (captureWindow == IntPtr.Zero) nativeRecorder?.Resume(recordForm.RecordingRegion);
                else nativeRecorder?.Resume();
            };
            recordForm.StopRequested += StopRecording;
            recordForm.Show();
            string path = "";
            bool aborted = true;
            try
            {
                await Task.Run(async () =>
                {
                    string requestedPath = Path.Combine(TaskHelpers.GetScreenshotsFolder(taskSettings, metadata), TaskHelpers.GetFileName(taskSettings, extension, metadata));
                    bool exists = File.Exists(requestedPath);
                    path = TaskHelpers.HandleExistsFile(requestedPath, taskSettings);
                    if (string.IsNullOrEmpty(path)) return;
                    bool replace = exists && string.Equals(path, requestedPath, StringComparison.Ordinal);
                    aborted = await RecordNativeAsync(path, captureWindow, taskSettings, replace, fps);
                });
            }
            finally
            {
                if (nativeRecorder != null) { await nativeRecorder.DisposeAsync(); nativeRecorder = null; }
                // Mark automatic completion before Close, which otherwise requests an abort.
                if (!aborted) recordForm.ChangeState(ScreenRecordState.RecordingEnd);
                recordForm.Close(); recordForm.Dispose(); recordForm = null;
                IsRecording = false;
            }
            if (!aborted && File.Exists(path))
            {
                TaskHelpers.ShowAfterCaptureWindow(taskSettings, result =>
                {
                    if (!result.Accepted) return;
                    if (!string.IsNullOrEmpty(result.FileName)) path = FileHelpers.RenameFile(path, result.FileName + Path.GetExtension(path));
                    WorkerTask task = WorkerTask.CreateFileJobTask(path, metadata, taskSettings, result.FileName);
                    TaskManager.Start(task);
                }, null, path);
            }
        }

        private static string GetOutputArguments(TaskSettings settings, int fps)
        {
            if (settings.CaptureSettings.ScreenRecordUseNative) return settings.CaptureSettings.FFmpegOptions.UserArgs;
            // Reuse the existing codec/quality settings; the Linux engine supplies capture inputs
            // and owns the final output filename. ArgumentList keeps spaces and filters intact.
            ScreenRecordingOptions encoding = new()
            {
                IsRecording = false, FFmpeg = settings.CaptureSettings.FFmpegOptions,
                FPS = fps, InputPath = "input", OutputPath = "output"
            };
            var arguments = NativeScreenRecorder.SplitArguments(encoding.GetFFmpegArgs());
            return string.Join(" ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(System.Linq.Enumerable.Skip(arguments, 2), arguments.Count - 4),
                arg => "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
        }

        private static async Task<bool> RecordNativeAsync(string path, IntPtr captureWindow, TaskSettings settings, bool replaceExisting, int fps)
        {
            // HandleExistsFile has already applied the user's overwrite policy. Commit a completed
            // recording afterward so aborts, restarts and startup errors preserve an existing file.
            string recordingPath = Path.Combine(Path.GetDirectoryName(path), $"{Path.GetFileNameWithoutExtension(path)}-recording-{Guid.NewGuid():N}{Path.GetExtension(path)}");
            while (true)
            {
                bool completed = false;
                NativeRecordingOptions options = new()
                {
                    OutputPath = recordingPath,
                    Region = recordForm.RecordingRegion,
                    WindowHandle = captureWindow,
                    FramesPerSecond = fps,
                    FFmpegPath = settings.CaptureSettings.FFmpegOptions.FFmpegPath,
                    ExtraArguments = GetOutputArguments(settings, fps),
                    CustomCommands = settings.CaptureSettings.FFmpegOptions.UseCustomCommands ? settings.CaptureSettings.FFmpegOptions.CustomCommands : "",
                    VideoBitrate = settings.CaptureSettings.ScreenRecordVideoBitrate * 1000,
                    CaptureSystemAudio = settings.CaptureSettings.ScreenRecordUseNative ? settings.CaptureSettings.ScreenRecordSystemAudio : settings.CaptureSettings.FFmpegOptions.IsAudioSourceSelected,
                    SystemAudioDeviceId = settings.CaptureSettings.ScreenRecordUseNative ? settings.CaptureSettings.ScreenRecordSystemAudioDeviceId : settings.CaptureSettings.FFmpegOptions.AudioSource,
                    SystemAudioGain = settings.CaptureSettings.ScreenRecordSystemAudioGain,
                    CaptureMicrophone = settings.CaptureSettings.ScreenRecordUseNative && settings.CaptureSettings.ScreenRecordMicrophone,
                    MicrophoneDeviceId = settings.CaptureSettings.ScreenRecordMicrophoneDeviceId,
                    MicrophoneGain = settings.CaptureSettings.ScreenRecordMicrophoneGain,
                    CaptureCamera = settings.CaptureSettings.ScreenRecordCamera,
                    CameraDeviceId = settings.CaptureSettings.ScreenRecordCameraDeviceId,
                    CameraResolution = settings.CaptureSettings.ScreenRecordCameraResolution,
                    CameraFramesPerSecond = settings.CaptureSettings.ScreenRecordCameraFPS,
                    CameraPosition = settings.CaptureSettings.ScreenRecordCameraPosition,
                    CameraShape = settings.CaptureSettings.ScreenRecordCameraShape,
                    CameraWidthPercent = settings.CaptureSettings.ScreenRecordCameraWidthPercent,
                    CameraMargin = settings.CaptureSettings.ScreenRecordCameraMargin,
                    RequireHardwareEncoder = settings.CaptureSettings.ScreenRecordRequireHardwareEncoder,
                    IncludeCursor = settings.CaptureSettings.ScreenRecordShowCursor,
                    Duration = settings.CaptureSettings.ScreenRecordFixedDuration ? TimeSpan.FromSeconds(settings.CaptureSettings.ScreenRecordDuration) : TimeSpan.Zero
                };
                nativeRecorder = new NativeScreenRecorder(options);
                nativeRecorder.Diagnostic += message => DebugHelper.WriteLine("Native recorder: " + message);
                try
                {
                    recordForm.ChangeState(ScreenRecordState.BeforeStart);
                    // Initialize the GPU, codecs and audio endpoints before the countdown.
                    // Prepare does not start video or audio capture.
                    await nativeRecorder.PrepareAsync();
                    int delay = settings.CaptureSettings.ScreenRecordStartDelayEnabled
                        ? (int)(settings.CaptureSettings.ScreenRecordStartDelay * 1000) : 0;
                    if (delay > 0)
                    {
                        recordForm.InvokeSafe(() => recordForm.StartCountdown(delay));
                        recordForm.RecordResetEvent.WaitOne(delay);
                    }
                    recordForm.ConsumeRestartRequest();
                    if (recordForm.Status == ScreenRecordingStatus.Aborted) return true;
                    if (recordForm.Status == ScreenRecordingStatus.Stopped) return true;
                    // A waiting recording region can be dragged. Rebuild for its final physical coordinates.
                    if (captureWindow == IntPtr.Zero && options.Region != recordForm.RecordingRegion)
                    {
                        await nativeRecorder.DisposeAsync();
                        nativeRecorder = new NativeScreenRecorder(options with { Region = recordForm.RecordingRegion });
                        nativeRecorder.Diagnostic += message => DebugHelper.WriteLine("Native recorder: " + message);
                        await nativeRecorder.PrepareAsync();
                    }
                    recordForm.ChangeState(ScreenRecordState.AfterStart);
                    using (IDisposable highlighter = settings.CaptureSettings.ScreenRecordMouseHighlighter
                        ? await MouseHighlighterManager.BeginRecordingAsync(settings.ToolsSettings.MouseHighlighterOptions) : null)
                    {
                        await nativeRecorder.StartAsync();
                        if (recordForm.Status == ScreenRecordingStatus.Aborted || recordForm.Status == ScreenRecordingStatus.Stopped || recordForm.RestartRequested)
                            nativeRecorder.RequestStop();
                        else ScreenRecorder_RecordingStarted();
                        var result = await nativeRecorder.Completion;
                        completed = true;
                        DebugHelper.WriteLine("Native screen recording completed. Encoder: {0}, hardware: {1}, frames: {2}, dropped: {3}, audio discontinuities: {4}",
                            result.Encoder.Name, result.Encoder.IsHardwareAccelerated, result.VideoFrames, result.DroppedVideoFrames, result.AudioDiscontinuities);
                    }
                }
                catch (OperationCanceledException) when (recordForm.Status == ScreenRecordingStatus.Aborted || recordForm.Status == ScreenRecordingStatus.Stopped || recordForm.RestartRequested)
                {
                    // A stop during preparation or before the first frame removes the incomplete file.
                }
                finally
                {
                    await nativeRecorder.DisposeAsync();
                    nativeRecorder = null;
                    recordForm.ChangeState(ScreenRecordState.RecordingEnd);
                }
                if (recordForm.RestartRequested)
                {
                    FileHelpers.DeleteFile(recordingPath);
                    continue;
                }
                TaskHelpers.PlayNotificationSoundAsync(NotificationSound.ActionCompleted, settings);
                bool aborted = recordForm.Status == ScreenRecordingStatus.Aborted;
                if (aborted) FileHelpers.DeleteFile(recordingPath);
                if (!aborted && completed)
                {
                    try { File.Move(recordingPath, path, replaceExisting); }
                    catch (Exception ex)
                    {
                        throw new IOException($"The recording finished, but its output could not be moved to '{path}'. The completed recording is available at '{recordingPath}'.", ex);
                    }
                }
                return aborted || !completed;
            }
        }

        private static void ScreenRecorder_RecordingStarted()
        {
            recordForm.ChangeState(ScreenRecordState.AfterRecordingStart);
        }

    }
}
