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

using ShareX.Tools.Linux;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShareX.Tools;

internal sealed class MouseHighlighterMouseHook : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private Exception? _failure;
    public Exception? Failure => Volatile.Read(ref _failure);

    public MouseHighlighterMouseHook(MouseHighlighterInputBuffer input)
    {
        if (DesktopTools.Wayland && !DesktopTools.Hyprland)
            throw new NotSupportedException("Global pointer tracking requires Hyprland or X11. Use the compositor's cursor highlighting/accessibility effect on this Wayland session.");
        TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(input, initialized)) { IsBackground = true, Name = "ShareX Linux pointer input" };
        _thread.Start();
        try { initialized.Task.GetAwaiter().GetResult(); }
        catch { _thread.Join(); _stop.Dispose(); throw; }
    }

    private unsafe void Run(MouseHighlighterInputBuffer input, TaskCompletionSource initialized)
    {
        nint display = 0;
        try
        {
            if (DesktopTools.Hyprland)
            {
                if (DesktopTools.CursorPosition() is not { } point) throw new NotSupportedException("Hyprland cursor tracking is unavailable.");
                input.SetPosition(new(point.X, point.Y));
                initialized.SetResult();
                while (!_stop.IsCancellationRequested)
                {
                    if (DesktopTools.CursorPosition() is { } cursor) input.SetPosition(new(cursor.X, cursor.Y));
                    _stop.Token.WaitHandle.WaitOne(25);
                }
                return;
            }
            display = DesktopTools.XOpenDisplay(0);
            if (display == 0) throw new InvalidOperationException("Cannot connect to X11.");
            int major = 2, minor = 0;
            if (XIQueryVersion(display, ref major, ref minor) != 0) throw new NotSupportedException("XInput2 is unavailable.");
            if (!XQueryExtension(display, "XInputExtension", out int opcode, out _, out _)) throw new NotSupportedException("XInput2 is unavailable.");
            byte* bits = stackalloc byte[4];
            new Span<byte>(bits, 4).Clear();
            foreach (int bit in new[] { 15, 16, 17 }) bits[bit / 8] |= (byte)(1 << (bit % 8));
            EventMask mask = new() { Device = 1, Length = 4, Mask = (nint)bits };
            XISelectEvents(display, DesktopTools.XDefaultRootWindow(display), ref mask, 1);
            XFlush(display);
            initialized.SetResult();
            nint ev = Marshal.AllocHGlobal(192); // XEvent union: 24 longs on Linux x64/arm64
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    while (XPending(display) > 0)
                    {
                        XNextEvent(display, ev);
                        if (Marshal.ReadInt32(ev) != 35 || Marshal.ReadInt32(ev, 32) != opcode || !XGetEventData(display, ev)) continue;
                        try
                        {
                            int kind = Marshal.ReadInt32(ev, 36);
                            if (DesktopTools.XQueryPointer(display, DesktopTools.XDefaultRootWindow(display), out _, out _, out int x, out int y, out _, out _, out _))
                            {
                                System.Drawing.Point point = new(x, y);
                                input.SetPosition(point);
                                if (kind is 15 or 16)
                                {
                                    int detail = Marshal.ReadInt32(Marshal.ReadIntPtr(ev, 48), 56);
                                    if (detail is >= 1 and <= 3)
                                        input.PublishButton(new(detail == 1 ? MouseHighlightButton.Primary : detail == 3 ? MouseHighlightButton.Secondary : MouseHighlightButton.Middle,
                                            kind == 15, point, Stopwatch.GetTimestamp()));
                                }
                            }
                        }
                        finally { XFreeEventData(display, ev); }
                    }
                    _stop.Token.WaitHandle.WaitOne(8);
                }
            }
            finally { Marshal.FreeHGlobal(ev); }
        }
        catch (Exception ex) { Volatile.Write(ref _failure, ex); initialized.TrySetException(ex); }
        finally { if (display != 0) DesktopTools.XCloseDisplay(display); }
    }
    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join();
        _stop.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct EventMask { public int Device, Length; public nint Mask; }
    [DllImport("libXi.so.6")] private static extern int XIQueryVersion(nint display, ref int major, ref int minor);
    [DllImport("libXi.so.6")] private static extern int XISelectEvents(nint display, nint window, ref EventMask mask, int count);
    [DllImport("libX11.so.6")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool XQueryExtension(nint display, string name, out int opcode, out int firstEvent, out int firstError);
    [DllImport("libX11.so.6")] private static extern int XPending(nint display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(nint display, nint ev);
    [DllImport("libX11.so.6")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool XGetEventData(nint display, nint cookie);
    [DllImport("libX11.so.6")] private static extern void XFreeEventData(nint display, nint cookie);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
}
