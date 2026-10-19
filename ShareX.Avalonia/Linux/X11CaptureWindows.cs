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

using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ShareX.AvaloniaUI.Linux;

internal static class X11CaptureWindows
{
    public static LinuxCaptureWindow[] GetWindows()
    {
        nint display = XOpenDisplay(null);
        if (display == 0) return [];
        try
        {
            nint root = XDefaultRootWindow(display);
            nint Property(nint window, string name, out nuint count)
            {
                XGetWindowProperty(display, window, XInternAtom(display, name, false), 0, 1048576, false, 0, out _, out _, out count, out _, out nint data);
                return data;
            }
            nint activeProperty = Property(root, "_NET_ACTIVE_WINDOW", out nuint activeCount);
            nint active = activeCount > 0 ? Marshal.ReadIntPtr(activeProperty) : 0;
            if (activeProperty != 0) XFree(activeProperty);
            nint list = Property(root, "_NET_CLIENT_LIST_STACKING", out nuint count);
            List<LinuxCaptureWindow> result = [];
            try
            {
                for (int i = (int)count - 1; i >= 0; i--)
                {
                    nint handle = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                    if (XGetWindowAttributes(display, handle, out var a) == 0 || a.MapState != 2) continue;
                    XTranslateCoordinates(display, handle, root, 0, 0, out int x, out int y, out _);
                    nint title = Property(handle, "_NET_WM_NAME", out nuint length);
                    string text = title != 0 ? Marshal.PtrToStringUTF8(title, (int)length) ?? "" : "";
                    if (title != 0) XFree(title);
                    result.Add(new LinuxCaptureWindow(handle, new Rectangle(x, y, a.Width, a.Height), text, "", active == handle));
                }
            }
            finally { if (list != 0) XFree(list); }
            return result.ToArray();
        }
        finally { XCloseDisplay(display); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public int X, Y, Width, Height, BorderWidth, Depth;
        public nint Visual, Root;
        public int Class, BitGravity, WinGravity, BackingStore;
        public nuint BackingPlanes, BackingPixel;
        public int SaveUnder;
        public nint Colormap;
        public int MapInstalled, MapState;
        public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Screen;
    }
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(nint display, nint window, nint property, nint offset, nint length, bool delete, nint requestedType, out nint type, out int format, out nuint count, out nuint remaining, out nint data);
    [DllImport("libX11.so.6")] private static extern int XGetWindowAttributes(nint display, nint window, out Attributes attributes);
    [DllImport("libX11.so.6")] private static extern bool XTranslateCoordinates(nint display, nint source, nint destination, int sx, int sy, out int dx, out int dy, out nint child);
    [DllImport("libX11.so.6")] private static extern int XFree(nint data);
}
