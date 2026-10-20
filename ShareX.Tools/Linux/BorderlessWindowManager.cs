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
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShareX.Tools;

public static class BorderlessWindowManager
{
    private static readonly ConcurrentDictionary<IntPtr, DesktopWindow> Saved = new();
    public static string SupportMessage => DesktopTools.Hyprland
        ? "Uses Hyprland fullscreen/maximize for the selected window. Toggle again to restore. Client-drawn decorations are controlled by the application."
        : DesktopTools.Sway ? "Uses sway fullscreen for the selected window. Client-drawn decorations are controlled by the application."
        : DesktopTools.Wayland ? "This compositor does not expose window control. Use its fullscreen shortcut or application/window rules."
        : "Uses X11/EWMH fullscreen or maximize. Toggle again to restore.";

    public static bool ToggleBorderlessWindow(string windowTitle, bool useWorkingArea = false)
    {
        var window = DesktopTools.GetWindows().FirstOrDefault(w => w.Text.Equals(windowTitle, StringComparison.CurrentCultureIgnoreCase));
        if (window == null)
        {
            if (DesktopTools.Wayland && !DesktopTools.Hyprland && !DesktopTools.Sway) throw new NotSupportedException(SupportMessage);
            return false;
        }
        ToggleBorderlessWindow(window.Handle, useWorkingArea);
        return true;
    }

    public static void ToggleBorderlessWindow(IntPtr handle, bool useWorkingArea = false)
    {
        var window = DesktopTools.GetWindows().FirstOrDefault(w => w.Handle == handle)
            ?? throw new InvalidOperationException("The selected window has closed.");
        bool restore = Saved.TryGetValue(handle, out var saved);
        if (DesktopTools.Hyprland)
        {
            int state = restore ? saved!.Fullscreen : useWorkingArea ? 1 : 2;
            string addr = $"0x{handle.ToInt64():x}";
            bool evalSuccess = false;
            try
            {
                string eval = ToolProcess.Read("hyprctl", "eval",
                    $"return hl.dispatch(hl.dsp.window.fullscreen_state({{ window = hl.get_window('address:{addr}'), internal = {state}, client = {state} }}))").Trim();
                if (eval.Contains("ok")) evalSuccess = true;
            }
            catch { }

            if (!evalSuccess)
            {
                // Fallback for older Hyprland versions without Lua dispatch
                using var active = JsonDocument.Parse(ToolProcess.Read("hyprctl", "activewindow", "-j"));
                string? previous = active.RootElement.TryGetProperty("address", out var address) ? address.GetString() : null;
                try
                {
                    Dispatch("focuswindow", $"address:{addr}");
                    Dispatch("fullscreenstate", $"{state} {state} set");
                }
                finally { if (!string.IsNullOrEmpty(previous)) Dispatch("focuswindow", previous.StartsWith("0x") ? previous : "address:" + previous); }
            }
        }
        else if (DesktopTools.Sway)
        {
            ToolProcess.Read("swaymsg", $"[con_id={handle.ToInt64()}] fullscreen {(restore && saved!.Fullscreen == 0 ? "disable" : "enable")}");
        }
        else if (!DesktopTools.Wayland)
        {
            X11Fullscreen(handle, restore ? saved!.Fullscreen > 0 : true, useWorkingArea);
        }
        else throw new NotSupportedException(SupportMessage);
        if (restore) Saved.TryRemove(handle, out _);
        else Saved[handle] = window;
    }

    private static void Dispatch(string name, string argument)
    {
        string response = ToolProcess.Read("hyprctl", "dispatch", name, argument).Trim();
        if (response != "ok") throw new InvalidOperationException(response);
    }

    private static void X11Fullscreen(nint window, bool enable, bool workingArea)
    {
        nint display = DesktopTools.XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("Cannot connect to X11.");
        try
        {
            nint first = XInternAtom(display, workingArea ? "_NET_WM_STATE_MAXIMIZED_VERT" : "_NET_WM_STATE_FULLSCREEN", false);
            nint second = workingArea ? XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ", false) : 0;
            // XClientMessageEvent uses native longs, padded to the XEvent union size.
            nint ev = Marshal.AllocHGlobal(192);
            try
            {
                for (int i = 0; i < 192; i += 8) Marshal.WriteInt64(ev, i, 0);
                Marshal.WriteInt32(ev, 0, 33);
                Marshal.WriteIntPtr(ev, 24, display);
                Marshal.WriteIntPtr(ev, 32, window);
                Marshal.WriteIntPtr(ev, 40, XInternAtom(display, "_NET_WM_STATE", false));
                Marshal.WriteInt32(ev, 48, 32);
                Marshal.WriteInt64(ev, 56, enable ? 1 : 0);
                Marshal.WriteIntPtr(ev, 64, first);
                Marshal.WriteIntPtr(ev, 72, second);
                Marshal.WriteInt64(ev, 80, 1);
                XSendEvent(display, DesktopTools.XDefaultRootWindow(display), false, (nint)((1 << 20) | (1 << 19)), ev);
                XFlush(display);
            }
            finally { Marshal.FreeHGlobal(ev); }
        }
        finally { DesktopTools.XCloseDisplay(display); }
    }
    [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(nint display, nint window, [MarshalAs(UnmanagedType.Bool)] bool propagate, nint mask, nint ev);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
}
