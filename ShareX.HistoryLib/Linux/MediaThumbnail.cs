// ShareX - GPL-3.0-or-later. Linux media previews without the Windows shell.
using ShareX.HelpersLib;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HistoryLib.Linux;

public static class MediaThumbnail
{
    public static SKBitmap Load(string path, int width, int height, CancellationToken cancellationToken = default)
    {
        if (!FileHelpers.IsVideoFile(path)) return null;
        return LoadAsync(path, width, height, cancellationToken).GetAwaiter().GetResult();
    }

    private static async Task<SKBitmap> LoadAsync(string path, int width, int height, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using Process process = new();
        process.StartInfo = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string argument in new[] { "-v", "error", "-i", path, "-frames:v", "1", "-vf",
            $"scale={Math.Max(1, width)}:{Math.Max(1, height)}:force_original_aspect_ratio=decrease", "-f", "image2pipe", "-vcodec", "png", "pipe:1" })
            process.StartInfo.ArgumentList.Add(argument);
        bool started = false;
        try
        {
            process.Start();
            started = true;
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
            using MemoryStream png = new();
            await process.StandardOutput.BaseStream.CopyToAsync(png, timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string error = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) { DebugHelper.WriteLine("Video thumbnail: " + error); return null; }
            png.Position = 0;
            return SKBitmap.Decode(png);
        }
        catch (OperationCanceledException) { return null; }
        catch (System.ComponentModel.Win32Exception exception) { DebugHelper.WriteLine("Video previews need ffmpeg: " + exception.Message); return null; }
        finally { if (started && !process.HasExited) process.Kill(true); }
    }
}
