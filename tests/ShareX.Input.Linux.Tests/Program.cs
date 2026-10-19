// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using ShareX.HelpersLib;
using System.Drawing;

static void Check(bool success, string name)
{
    if (!success) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

var hypr = LinuxWindowSystem.ParseHyprland("""
[{"address":"0x560a239c3960","title":"Native Wayland","class":"test","pid":123,"at":[-100,20],"size":[800,600],"workspace":{"id":2},"mapped":true},
 {"address":"0x560a239c3961","title":"Hidden workspace","at":[0,0],"size":[100,100],"workspace":{"id":3},"mapped":true},
 {"address":"0x560a239c3962","title":"Pinned","at":[0,0],"size":[100,100],"workspace":{"id":3},"mapped":true,"pinned":true}]
""", """[{"activeWorkspace":{"id":2},"specialWorkspace":{"id":0}}]""", """{"address":"0x560a239c3960"}""");
Check(hypr[0].Handle.ToInt64() == 0x560a239c3960 && hypr[0].Active, "64-bit compositor handle and active window");
Check(hypr[0].Bounds == new Rectangle(-100,20,800,600), "logical coordinates and negative origins");
Check(hypr[0].Visible && !hypr[1].Visible && hypr[2].Visible, "workspace visibility and pinned windows");
var sway = LinuxWindowSystem.ParseSway("""
{"nodes":[{"type":"workspace","visible":true,"nodes":[{"id":100,"app_id":"native","pid":123,"name":"Sway","focused":true,"rect":{"x":-20,"y":5,"width":500,"height":400},"window_rect":{"x":2,"y":20,"width":496,"height":378}}],"floating_nodes":[{"id":101,"window":42,"window_properties":{"class":"Xwayland"},"rect":{"width":30,"height":40}}]}]}
""");
Check(sway.Length == 2 && sway[0].Active && sway[1].ClassName == "Xwayland", "sway native and floating XWayland windows");
Check(sway[0].ClientBounds == new Rectangle(-18,25,496,378), "sway client geometry");
Check(new WindowInfo(IntPtr.Zero).ProcessId == 0 && new WindowInfo(IntPtr.Zero).Rectangle.IsEmpty, "stale or absent window safe defaults");
if (args.Contains("--live"))
{
    var windows = LinuxWindowSystem.GetWindows(true);
    Check(windows.Count > 0, LinuxWindowSystem.Backend + " live window enumeration");
    int count = 0;
    NativeMethods.EnumWindows((handle, parameter) => { var w = new WindowInfo(handle); _ = w.Text; _ = w.ProcessName; _ = w.ClientRectangle; count++; return true; }, IntPtr.Zero);
    Check(count > 0, "public WindowInfo and NativeMethods APIs");
    Check(NativeMethods.GetForegroundWindow() != IntPtr.Zero, "live active window");
    Check(NativeMethods.GetCursorPos(out var point), "live global cursor position");
    Console.WriteLine($"Live: {windows.Count} windows, cursor {point.X},{point.Y}; no desktop mutations performed.");
}

Check(LinuxClipboard.ParseFiles("# comment\r\nfile:///tmp/a%20b.png\r\nhttps://example.invalid/file\r\nfile://remotehost/unsafe\r\n").SequenceEqual(new[] { "/tmp/a b.png" }), "URI file parsing ignores comments, remote hosts and non-files");
Check(LinuxClipboard.ParseFiles("copy\nfile:///tmp/%CE%BB.txt\n").Single() == "/tmp/λ.txt", "GNOME file clipboard parsing");
if (args.Contains("--clipboard"))
{
    Check(LinuxClipboard.IsAvailable, "persistent clipboard tools available");
    string[] originalFormats = LinuxClipboard.GetFormats();
    string originalMime = new[] { "image/png", "text/uri-list", "text/plain;charset=utf-8", "UTF8_STRING", "text/plain" }.FirstOrDefault(originalFormats.Contains);
    byte[] original = originalMime == null ? null : LinuxClipboard.Read(originalMime);
    try
    {
        const string text = "ShareX λ 🐧\nsecond line\n";
        Check(ClipboardHelpers.CopyText(text) && ClipboardHelpers.ContainsText() && ClipboardHelpers.GetText() == text, "public clipboard Unicode text round trip, preserves trailing newline");
        var paths = new[] { "/tmp/sx-wp2/a b.png", "/tmp/sx-wp2/λ.txt" };
        Check(ClipboardHelpers.CopyFile(paths) && ClipboardHelpers.ContainsFileDropList() && ClipboardHelpers.GetFileDropList().SequenceEqual(paths), "public clipboard file list round trip");
        Check(LinuxClipboard.Write("x-special/gnome-copied-files", System.Text.Encoding.UTF8.GetBytes("copy\nfile:///tmp/a%20b.png\n")) && ClipboardHelpers.GetFileDropList().Single() == "/tmp/a b.png", "GNOME clipboard interop");
        using SkiaSharp.SKBitmap image = new(3, 2);
        image.Erase(new SkiaSharp.SKColor(40, 80, 120, 100));
        Check(ClipboardHelpers.CopyImage(image) && ClipboardHelpers.ContainsImage(), "PNG selection owner");
        using var pasted = ClipboardHelpers.GetImage();
        Check(pasted != null && pasted.Width == 3 && pasted.Height == 2 && pasted.GetPixel(0, 0).Alpha == 100, "PNG image round trip with alpha");
        Check(ClipboardHelpers.CaptureData().GetData("image/png") is byte[], "clipboard viewer MIME snapshot");
        Check(ClipboardHelpers.Clear() && !ClipboardHelpers.ContainsImage(), "clipboard clear");
    }
    finally
    {
        if (original != null) LinuxClipboard.Write(originalMime, original);
        else LinuxClipboard.Clear();
    }
}

var gesture = new HotkeyInfo(InputKey.Control | InputKey.Shift | InputKey.PrintScreen);
Check(LinuxKeyNames.PortalTrigger(gesture) == "CTRL+SHIFT+Print", "XDG portal trigger syntax");
Check(LinuxKeyNames.BindLine(gesture, "RectangleRegion") == "bind = CTRL SHIFT, Print, exec, sharex -RectangleRegion", "Hyprland bind line");
Check(LinuxKeyNames.BindLine(gesture, "RectangleRegion", true) == "bindsym Ctrl+Shift+Print exec sharex -RectangleRegion", "sway bind line");
if (args.Contains("--portal"))
{
    using LinuxGlobalShortcutsPortal portal = new();
    await portal.CreateAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check(true, "live GlobalShortcuts portal session creation and disposal");
}
if (args.Contains("--ipc-child"))
{
    using var child = new SingleInstanceManager("wp2-test", "wp2-test", new[] { "-RectangleRegion", "λ space.png" });
    Check(!child.IsFirstInstance, "child CLI forwarded to running instance");
}
if (args.Contains("--ipc"))
{
    using var parent = new SingleInstanceManager("wp2-test", "wp2-test", []);
    Check(parent.IsFirstInstance, "first CLI instance owns Unix socket");
    TaskCompletionSource<string[]> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    parent.ArgumentsReceived += values => received.TrySetResult(values);
    using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--ipc-child" } });
    var values = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await child!.WaitForExitAsync();
    Check(child.ExitCode == 0 && values.SequenceEqual(new[] { "-RectangleRegion", "λ space.png" }), "CLI IPC preserves task names, Unicode and argument boundaries");
}
