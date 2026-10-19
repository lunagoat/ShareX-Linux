// ShareX - GPL v3. See LICENSE.txt.
using Avalonia.Controls;
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using ShareX.HelpersLib;

namespace ShareX.ScreenCaptureLib;

/// <summary>XShape keeps recording outlines hollow and passes input through their aperture.</summary>
public static class RecordingWindowPlatform
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public short X, Y;
        public ushort Width, Height;
        public Rect(int x, int y, int width, int height)
        { X = (short)x; Y = (short)y; Width = (ushort)Math.Max(0, width); Height = (ushort)Math.Max(0, height); }
    }

    public static void Shape(Window window, Rect[] rectangles, bool clickThrough = false)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "XID" || handle.Handle == IntPtr.Zero) return;
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) return;
        try
        {
            XShapeCombineRectangles(display, handle.Handle, 0, 0, 0, rectangles, rectangles.Length, 0, 0);
            XShapeCombineRectangles(display, handle.Handle, 2, 0, 0, clickThrough ? [] : rectangles, clickThrough ? 0 : rectangles.Length, 0, 0);
            XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }
    /// <summary>Position only this process's own recording overlay on Hyprland, without persistent rules.</summary>
    public static async void Place(Window window, int x, int y, int width, int height)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return;
        string title = window.Title ?? "";
        try
        {
            await Task.Delay(80);
            string text = await Command("clients", "-j");
            using JsonDocument clients = JsonDocument.Parse(text);
            foreach (JsonElement client in clients.RootElement.EnumerateArray())
            {
                if (client.GetProperty("pid").GetInt32() != Environment.ProcessId || client.GetProperty("title").GetString() != title) continue;
                string address = client.GetProperty("address").GetString()!;
                await Command("dispatch", "setfloating", "address:" + address);
                await Command("dispatch", "resizewindowpixel", $"exact {width} {height},address:{address}");
                await Command("dispatch", "movewindowpixel", $"exact {x} {y},address:{address}");
                break;
            }
        }
        catch (Exception ex) { DebugHelper.WriteLine("Recording overlay placement: " + ex.Message); }
    }
    private static async Task<string> Command(params string[] args)
    {
        ProcessStartInfo info = new("hyprctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return await output;
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, [In] Rect[] rectangles, int count, int operation, int ordering);
}
