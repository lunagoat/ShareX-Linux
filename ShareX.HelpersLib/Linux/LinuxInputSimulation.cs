// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Runtime.InteropServices;

namespace ShareX.HelpersLib;

public static class LinuxInputSimulation
{
    public static bool IsSupported => !LinuxWindowSystem.IsWayland;
    public const string WaylandLimitation = "Wayland blocks arbitrary keyboard and mouse injection. Scrolling capture and simulated input require an X11 session or a user-approved remote desktop service.";
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool press, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, bool press, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeRelativeMotionEvent(IntPtr display, int x, int y, nuint delay);
    public static uint Send(INPUT[] inputs)
    {
        if (!IsSupported) { DebugHelper.WriteLine(WaylandLimitation); return 0; }
        IntPtr display = LinuxX11.XOpenDisplay(null);
        if (display == IntPtr.Zero) return 0;
        uint count = 0;
        try
        {
            foreach (INPUT input in inputs)
            {
                if (input.Type == InputType.InputKeyboard)
                {
                    var key = input.Data.Keyboard;
                    string symbol = key.dwFlags.HasFlag(KeyboardEventFlags.KEYEVENTF_UNICODE) ?
                        key.wScan <= 0x7f ? ((char)key.wScan).ToString() : "U" + key.wScan.ToString("X4") : LinuxKeyNames.KeySym((InputKey)key.wVk);
                    if (string.IsNullOrEmpty(symbol)) break;
                    byte code = LinuxX11Hotkeys.XKeysymToKeycode(display, LinuxX11Hotkeys.XStringToKeysym(symbol));
                    if (code == 0 || XTestFakeKeyEvent(display, code, !key.dwFlags.HasFlag(KeyboardEventFlags.KEYEVENTF_KEYUP), 0) == 0) break;
                }
                else if (input.Type == InputType.InputMouse)
                {
                    var mouse = input.Data.Mouse;
                    var flags = mouse.dwFlags;
                    if (flags.HasFlag(MouseEventFlags.MOUSEEVENTF_MOVE))
                    {
                        if (flags.HasFlag(MouseEventFlags.MOUSEEVENTF_ABSOLUTE)) XTestFakeMotionEvent(display, -1, mouse.dx, mouse.dy, 0);
                        else XTestFakeRelativeMotionEvent(display, mouse.dx, mouse.dy, 0);
                    }
                    foreach (var (down, up, button) in new[] {
                        (MouseEventFlags.MOUSEEVENTF_LEFTDOWN, MouseEventFlags.MOUSEEVENTF_LEFTUP, 1u),
                        (MouseEventFlags.MOUSEEVENTF_MIDDLEDOWN, MouseEventFlags.MOUSEEVENTF_MIDDLEUP, 2u),
                        (MouseEventFlags.MOUSEEVENTF_RIGHTDOWN, MouseEventFlags.MOUSEEVENTF_RIGHTUP, 3u),
                        (MouseEventFlags.MOUSEEVENTF_XDOWN, MouseEventFlags.MOUSEEVENTF_XUP, mouse.mouseData == 1 ? 8u : 9u) })
                    {
                        if (flags.HasFlag(down)) XTestFakeButtonEvent(display, button, true, 0);
                        if (flags.HasFlag(up)) XTestFakeButtonEvent(display, button, false, 0);
                    }
                    if (flags.HasFlag(MouseEventFlags.MOUSEEVENTF_WHEEL) || flags.HasFlag((MouseEventFlags)0x1000))
                    {
                        int delta = unchecked((int)mouse.mouseData);
                        uint button = flags.HasFlag((MouseEventFlags)0x1000) ? delta > 0 ? 7u : 6u : delta > 0 ? 4u : 5u;
                        int steps = Math.Min(100, Math.Max(1, Math.Abs(delta / 120)));
                        for (int i = 0; i < steps; i++) { XTestFakeButtonEvent(display, button, true, 0); XTestFakeButtonEvent(display, button, false, 0); }
                    }
                }
                else break;
                count++;
            }
            LinuxX11.XFlush(display);
        }
        catch (DllNotFoundException exception) { DebugHelper.WriteException(exception, "X11 input simulation requires libXtst."); }
        finally { LinuxX11.XCloseDisplay(display); }
        return count;
    }
}
