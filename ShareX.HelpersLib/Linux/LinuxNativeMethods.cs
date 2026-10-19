// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Drawing;

namespace ShareX.HelpersLib;

public static partial class NativeMethods
{
    public static bool EnumWindows(EnumWindowsProc callback, IntPtr parameter)
    {
        foreach (LinuxWindowSystem.Window window in LinuxWindowSystem.GetWindows(true))
            if (!callback(window.Handle, parameter)) return false;
        return true;
    }
    public static bool GetCursorPos(out POINT position)
    {
        bool result = LinuxWindowSystem.TryGetCursorPosition(out Point point);
        position = (POINT)point;
        return result;
    }
    public static bool SetCursorPos(int x, int y) => LinuxWindowSystem.SetCursorPosition(x, y);
    public static IntPtr GetForegroundWindow() => LinuxWindowSystem.ActiveWindow;
    public static bool SetForegroundWindow(IntPtr handle) => LinuxWindowSystem.Activate(handle);
    public static IntPtr GetParent(IntPtr handle) => IntPtr.Zero;
    public static bool IsWindowVisible(IntPtr handle) => LinuxWindowSystem.Find(handle)?.Visible == true;
    public static bool IsIconic(IntPtr handle) => LinuxWindowSystem.Find(handle)?.Minimized == true;
    public static bool IsZoomed(IntPtr handle) => LinuxWindowSystem.Find(handle)?.Maximized == true;
    public static uint GetWindowThreadProcessId(IntPtr handle, out uint processId)
    {
        processId = (uint)(LinuxWindowSystem.Find(handle)?.Pid ?? 0);
        return processId;
    }
    public static bool GetWindowRect(IntPtr handle, out RECT rectangle)
    {
        LinuxWindowSystem.Window window = LinuxWindowSystem.Find(handle);
        rectangle = window?.Bounds ?? Rectangle.Empty;
        return window != null;
    }
    public static bool GetClientRect(IntPtr handle, out RECT rectangle)
    {
        LinuxWindowSystem.Window window = LinuxWindowSystem.Find(handle);
        rectangle = window == null ? Rectangle.Empty : new Rectangle(Point.Empty, window.ClientBounds.Size);
        return window != null;
    }
}
