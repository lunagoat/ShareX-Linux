// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ShareX.HelpersLib;

internal static class LinuxX11
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ErrorEvent { public int Type; public IntPtr Display; public nuint Resource, Serial; public byte Code, Request, Minor; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorHandler(IntPtr display, ref ErrorEvent error);
    [DllImport(Library)] private static extern IntPtr XSetErrorHandler(ErrorHandler handler);
    [DllImport(Library)] internal static extern int XSync(IntPtr display, bool discard);
    private static readonly ConcurrentDictionary<IntPtr, int> errors = new();
    private static readonly ErrorHandler errorHandler = OnError;
    private static ErrorHandler previousHandler;
    private static readonly object errorLock = new();
    private static bool installed;
    private static int OnError(IntPtr display, ref ErrorEvent error)
    {
        if (errors.ContainsKey(display)) { errors[display] = error.Code; return 0; }
        return previousHandler?.Invoke(display, ref error) ?? 0;
    }
    internal static IDisposable TrackErrors(IntPtr display)
    {
        lock (errorLock)
        {
            if (!installed)
            {
                IntPtr previous = XSetErrorHandler(errorHandler);
                if (previous != IntPtr.Zero) previousHandler = Marshal.GetDelegateForFunctionPointer<ErrorHandler>(previous);
                installed = true;
            }
        }
        errors[display] = 0;
        return new ErrorScope(display);
    }
    internal static void ClearError(IntPtr display) => errors[display] = 0;
    internal static int GetError(IntPtr display) => errors.GetValueOrDefault(display);
    private sealed class ErrorScope(IntPtr display) : IDisposable { public void Dispose() => errors.TryRemove(display, out _); }

    internal const string Library = "libX11.so.6";
    [DllImport(Library)] internal static extern IntPtr XOpenDisplay(string name);
    [DllImport(Library)] internal static extern int XCloseDisplay(IntPtr display);
    [DllImport(Library)] internal static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Library)] internal static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(Library)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, long length,
        bool delete, IntPtr requestedType, out IntPtr actualType, out int format, out nuint count, out nuint remaining, out IntPtr data);
    [DllImport(Library)] internal static extern int XFree(IntPtr data);
    [DllImport(Library)] private static extern int XGetGeometry(IntPtr display, IntPtr window, out IntPtr root, out int x, out int y,
        out uint width, out uint height, out uint border, out uint depth);
    [DllImport(Library)] private static extern bool XTranslateCoordinates(IntPtr display, IntPtr source, IntPtr target, int x, int y,
        out int resultX, out int resultY, out IntPtr child);
    [DllImport(Library)] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
        out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
    [DllImport(Library)] private static extern int XWarpPointer(IntPtr display, IntPtr source, IntPtr target, int sx, int sy, uint sw, uint sh, int x, int y);
    [DllImport(Library)] internal static extern int XFlush(IntPtr display);
    [DllImport(Library)] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long mask, ref ClientMessage message);
    [StructLayout(LayoutKind.Sequential)]
    private struct ClientMessage
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display, Window, MessageType;
        public int Format;
        public nint Data0, Data1, Data2, Data3, Data4;
        // XEvent is a union of 24 longs, not just an XClientMessageEvent.
        private nint pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7, pad8, pad9, pad10, pad11;
    }

    internal static long[] Numbers(IntPtr display, IntPtr window, string property)
    {
        IntPtr data = IntPtr.Zero;
        try
        {
            if (XGetWindowProperty(display, window, XInternAtom(display, property, false), 0, 65536, false, IntPtr.Zero,
                out _, out int format, out nuint count, out _, out data) != 0 || data == IntPtr.Zero || format != 32) return [];
            long[] result = new long[(int)count];
            for (int i = 0; i < result.Length; i++) result[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64();
            return result;
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    private static string Text(IntPtr display, IntPtr window, string property)
    {
        IntPtr data = IntPtr.Zero;
        try
        {
            if (XGetWindowProperty(display, window, XInternAtom(display, property, false), 0, 65536, false, IntPtr.Zero,
                out _, out int format, out nuint count, out _, out data) != 0 || data == IntPtr.Zero || format != 8) return "";
            byte[] bytes = new byte[(int)count];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    public static LinuxWindowSystem.Window[] GetWindows()
    {
        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return [];
        using IDisposable errors = TrackErrors(display);
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            long active = Numbers(display, root, "_NET_ACTIVE_WINDOW").FirstOrDefault();
            long desktop = Numbers(display, root, "_NET_CURRENT_DESKTOP").FirstOrDefault();
            long[] clients = Numbers(display, root, "_NET_CLIENT_LIST_STACKING");
            if (clients.Length == 0) clients = Numbers(display, root, "_NET_CLIENT_LIST");
            List<LinuxWindowSystem.Window> windows = new();
            foreach (long id in clients.Reverse())
            {
                IntPtr handle = new(id);
                if (XGetGeometry(display, handle, out _, out _, out _, out uint width, out uint height, out _, out _) == 0) continue;
                XTranslateCoordinates(display, handle, root, 0, 0, out int x, out int y, out _);
                Rectangle client = new(x, y, (int)width, (int)height);
                long[] extents = Numbers(display, handle, "_NET_FRAME_EXTENTS");
                Rectangle frame = extents.Length < 4 ? client : Rectangle.FromLTRB(x - (int)extents[0], y - (int)extents[2],
                    x + (int)width + (int)extents[1], y + (int)height + (int)extents[3]);
                long[] states = Numbers(display, handle, "_NET_WM_STATE");
                bool minimized = states.Contains(XInternAtom(display, "_NET_WM_STATE_HIDDEN", false).ToInt64());
                long[] desktops = Numbers(display, handle, "_NET_WM_DESKTOP");
                bool visible = !minimized && (desktops.Length == 0 || desktops[0] == desktop || (uint)desktops[0] == uint.MaxValue);
                string title = Text(display, handle, "_NET_WM_NAME");
                if (title.Length == 0) title = Text(display, handle, "WM_NAME");
                windows.Add(new(handle, title, Text(display, handle, "WM_CLASS").Split('\0').LastOrDefault() ?? "",
                    (int)Numbers(display, handle, "_NET_WM_PID").FirstOrDefault(), frame, client, visible, id == active,
                    states.Contains(XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_VERT", false).ToInt64()), minimized));
            }
            return windows.ToArray();
        }
        finally { XCloseDisplay(display); }
    }

    public static bool TryGetCursorPosition(out Point point)
    {
        point = Point.Empty;
        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return false;
        try
        {
            bool result = XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out int x, out int y, out _, out _, out _);
            point = new(x, y);
            return result;
        }
        finally { XCloseDisplay(display); }
    }

    public static bool SetCursorPosition(int x, int y)
    {
        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return false;
        try { XWarpPointer(display, IntPtr.Zero, XDefaultRootWindow(display), 0, 0, 0, 0, x, y); XFlush(display); return true; }
        finally { XCloseDisplay(display); }
    }

    public static bool Activate(IntPtr handle)
    {
        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero) return false;
        try
        {
            ClientMessage message = new() { Type = 33, Display = display, Window = handle,
                MessageType = XInternAtom(display, "_NET_ACTIVE_WINDOW", false), Format = 32, Data0 = 1 };
            bool result = XSendEvent(display, XDefaultRootWindow(display), false, (1L << 20) | (1L << 19), ref message) != 0;
            XFlush(display);
            return result;
        }
        finally { XCloseDisplay(display); }
    }
}
