// ShareX Linux integration checks. GPL-3.0-or-later.
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using ShareX;
using ShareX.HelpersLib;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Integration;
using ShareX.ImageEditor.Presentation.Views;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEffectsLib;
using ShareX.HistoryLib.Linux;
using SkiaSharp;
using static NetworkChecks;

internal static class MediaChecks
{
    internal static async Task RunAsync(string sandbox)
    {
        ImageEditorIntegration.Initialize();
        using var source = new SKBitmap(80, 60);
        source.Erase(SKColors.Crimson);
        using (SKBitmap effects = ImageEffectPreset.GetDefaultPreset().ApplyEffects(source))
            Assert(effects.Width == 80 && effects.Height == 90, "Canvas and text watermark effects");
        var editor = new EditorWindow(); editor.Show(); editor.LoadImage(source.Copy()); Dispatcher.UIThread.RunJobs();
        using (SKBitmap? result = editor.GetResultBitmap()) Assert(result is { Width: 80, Height: 60 }, "Editor load/export");
        var view = editor.FindControl<EditorView>("Editor");
        // Find the editor by its actual XAML name if the host changed it.
        view ??= Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(editor).OfType<EditorView>().FirstOrDefault();
        Assert(view != null, "editor view is present");
        MethodInfo insert = typeof(EditorView).GetMethod("InsertImageWithPlacementAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var inserted = new SKBitmap(10, 10); inserted.Erase(SKColors.Blue);
        await (Task)insert.Invoke(view, [inserted, InsertImagePlacement.Center])!;
        editor.Close();
        Console.WriteLine("PASS image effects/watermark, editor open/load/export and centered image insertion");
        Type renderer = typeof(EditorWindow).Assembly.GetType("ShareX.ImageEditor.Presentation.Rendering.WindowsCursorBitmapRenderer")!;
        MethodInfo render = renderer.GetMethod("CreateAnnotationBitmap", BindingFlags.Static | BindingFlags.Public)!;
        foreach (CursorType cursor in Enum.GetValues<CursorType>())
        {
            using var bitmap = (SKBitmap?)render.Invoke(null, [cursor]);
            Assert(bitmap is { Width: > 0, Height: > 0 }, "cursor annotation " + cursor);
        }
        Console.WriteLine("PASS every cursor annotation type (Xcursor themes and bundled cursor assets)");
        string video = Path.Combine(sandbox, "thumbnail ü.mp4");
        await StorageChecks.RunProcess("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=160x100:d=0.1", "-pix_fmt", "yuv420p", video);
        using (var preview = MediaThumbnail.Load(video, 100, 60)) Assert(preview is { Width: <= 100, Height: <= 60 }, "ffmpeg video thumbnail");
        Console.WriteLine("PASS video thumbnails through ffmpeg and Skia");

        using var server = new LocalHttp();
        var custom = ApplicationState.UploadersConfig.CustomUploadersList[0];
        custom.RequestURL = server.Url + "json";
        var settings = ApplicationState.DefaultTaskSettings;
        settings.AfterCaptureJob = AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.AddImageEffects | AfterCaptureTasks.UploadImageToHost;
        settings.ImageSettings.ShowImageEffectsWindowAfterCapture = false;
        settings.ImageSettings.ImageEffectOnlyRegionCapture = false;
        settings.OverrideScreenshotsFolder = true;
        settings.ScreenshotsFolder = Path.Combine(sandbox, "captures");
        var task = WorkerTask.CreateImageUploaderTask(new TaskMetadata(source.Copy()), settings, "capture-integration");
        await NetworkChecks.Pipeline(task);
        Assert(File.Exists(task.Info.FilePath), "capture pipeline saved image");
        using (SKBitmap saved = SKBitmap.Decode(task.Info.FilePath)) Assert(saved.Height == 90, "saved image includes effects");
        Assert(ApplicationState.HistoryManager!.GetHistoryItems().Any(x => x.FilePath == task.Info.FilePath), "image upload in history");
        settings.AfterCaptureJob |= AfterCaptureTasks.DeleteFile;
        var deletion = WorkerTask.CreateImageUploaderTask(new TaskMetadata(source.Copy()), settings, "capture-delete");
        await NetworkChecks.Pipeline(deletion);
        Assert(!File.Exists(deletion.Info.FilePath), "DeleteFile after successful image upload");
        settings.AfterCaptureJob = AfterCaptureTasks.None;
        var download = WorkerTask.CreateDownloadTask(server.Url + "download.bin", true, settings);
        await NetworkChecks.Pipeline(download);
        Assert(File.Exists(download.Info.FilePath), "URL download uploaded from saved file");
        Console.WriteLine("PASS image -> effects -> save -> upload -> history; delete-file task; URL download/reupload");
    }
}
