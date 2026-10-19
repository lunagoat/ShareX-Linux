// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

/// <summary>External selection owners keep data available after a capture window or ShareX exits.
/// No shell is used: file names, URLs and arbitrary clipboard bytes are passed literally.</summary>
public static class LinuxClipboard
{
    public static bool IsAvailable => LinuxWindowSystem.IsWayland
        ? LinuxCommand.Exists("wl-copy") && LinuxCommand.Exists("wl-paste") : LinuxCommand.Exists("xclip");
    public static string[] GetFormats()
    {
        string formats = LinuxWindowSystem.IsWayland ? LinuxCommand.Run("wl-paste", "--list-types") :
            LinuxCommand.Run("xclip", "-selection", "clipboard", "-out", "-target", "TARGETS");
        return (formats ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static byte[] Read(string mime)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo(LinuxWindowSystem.IsWayland ? "wl-paste" : "xclip")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in LinuxWindowSystem.IsWayland ? new[] { "--no-newline", "--type", mime } :
            new[] { "-selection", "clipboard", "-out", "-target", mime }) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start();
            using MemoryStream output = new();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            Task copy = CopyBoundedAsync(process.StandardOutput.BaseStream, output, timeout.Token);
            _ = process.StandardError.ReadToEndAsync();
            copy.GetAwaiter().GetResult();
            if (!process.WaitForExit(1000)) { process.Kill(true); return null; }
            return process.ExitCode == 0 ? output.ToArray() : null;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return null;
        }
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > 128 * 1024 * 1024) throw new IOException("Clipboard data exceeds 128 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    public static bool Write(string mime, byte[] bytes)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo(LinuxWindowSystem.IsWayland ? "wl-copy" : "xclip")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true };
        foreach (string arg in LinuxWindowSystem.IsWayland ? new[] { "--type", mime } :
            new[] { "-selection", "clipboard", "-in", "-target", mime, "-silent" }) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start();
            _ = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            process.StandardInput.BaseStream.WriteAsync(bytes, timeout.Token).AsTask().GetAwaiter().GetResult();
            process.StandardInput.Close();
            // xclip stays alive as the selection owner. wl-copy forks its owner before returning.
            if (!LinuxWindowSystem.IsWayland) return !process.WaitForExit(150) || process.ExitCode == 0;
            if (!process.WaitForExit(5000)) { process.Kill(true); return false; }
            return process.ExitCode == 0;
        }
        catch (Exception exception)
        {
            DebugHelper.WriteException(exception, "Install wl-clipboard (Wayland) or xclip (X11) for persistent clipboard support.");
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return false;
        }
    }

    public static bool Clear() => LinuxWindowSystem.IsWayland ? LinuxCommand.Run("wl-copy", "--clear") != null : Write("UTF8_STRING", []);
    public static bool SetText(string text) => Write(LinuxWindowSystem.IsWayland ? "text/plain;charset=utf-8" : "UTF8_STRING", Encoding.UTF8.GetBytes(text));
    public static string GetText()
    {
        string[] types = GetFormats();
        string mime = new[] { "text/plain;charset=utf-8", "UTF8_STRING", "text/plain", "text/uri-list", "STRING" }.FirstOrDefault(types.Contains);
        byte[] data = mime == null ? null : Read(mime);
        return data == null ? null : Encoding.UTF8.GetString(data);
    }
    public static bool SetFiles(string[] paths) => Write("text/uri-list", Encoding.UTF8.GetBytes(SerializeFiles(paths)));
    public static string SerializeFiles(string[] paths) => string.Join("\r\n", paths.Select(p => new Uri(Path.GetFullPath(p)).AbsoluteUri)) + "\r\n";
    public static string[] ParseFiles(string value) => value?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Where(x => !x.StartsWith('#') && x is not "copy" and not "cut")
        .Select(x => Uri.TryCreate(x, UriKind.Absolute, out Uri uri) && uri.IsFile && (uri.Host.Length == 0 || uri.IsLoopback) ? uri.LocalPath : null)
        .Where(x => x != null).ToArray();
    public static string[] GetFiles()
    {
        string[] formats = GetFormats();
        string format = formats.Contains("x-special/gnome-copied-files") ? "x-special/gnome-copied-files" : "text/uri-list";
        if (!formats.Contains(format)) return null;
        byte[] bytes = Read(format);
        return bytes == null ? null : ParseFiles(Encoding.UTF8.GetString(bytes));
    }
    public static bool Contains(string kind)
    {
        string[] formats = GetFormats();
        return kind switch
        {
            ClipboardDataFormats.Bitmap => formats.Contains("image/png"),
            ClipboardDataFormats.Text => formats.Any(x => x.StartsWith("text/plain") || x is "UTF8_STRING" or "STRING"),
            ClipboardDataFormats.FileDrop => formats.Any(x => x is "text/uri-list" or "x-special/gnome-copied-files"),
            _ => formats.Contains(kind)
        };
    }
    public static ClipboardData Capture()
    {
        ClipboardData snapshot = new();
        foreach (string mime in GetFormats())
        {
            byte[] bytes = Read(mime);
            if (bytes == null) continue;
            snapshot.SetData(mime, mime.StartsWith("text/") || mime is "UTF8_STRING" or "STRING" or "x-special/gnome-copied-files" ? Encoding.UTF8.GetString(bytes) : bytes);
        }
        string text = GetText();
        if (text != null) snapshot.SetData(ClipboardDataFormats.UnicodeText, text);
        string[] files = GetFiles();
        if (files?.Length > 0) snapshot.SetData(ClipboardDataFormats.FileDrop, files);
        if (snapshot.GetData("image/png") is byte[] png) snapshot.SetData(ClipboardDataFormats.Bitmap, png);
        return snapshot;
    }
}
