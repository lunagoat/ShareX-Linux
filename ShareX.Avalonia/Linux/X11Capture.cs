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
using SkiaSharp;

namespace ShareX.AvaloniaUI.Linux;

public static class X11Capture
{
    private const string X11 = "libX11.so.6";
    private static nint Open() => XOpenDisplay(null) is var display && display != 0 ? display : throw new InvalidOperationException("Cannot connect to the X11 display.");

    public static Point GetCursorPosition()
    {
        nint display = Open();
        try
        {
            XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out int x, out int y, out _, out _, out _);
            return new Point(x, y);
        }
        finally { XCloseDisplay(display); }
    }

    public static void MoveCursor(Point point)
    {
        nint display = Open();
        try { XWarpPointer(display, 0, XDefaultRootWindow(display), 0, 0, 0, 0, point.X, point.Y); XFlush(display); }
        finally { XCloseDisplay(display); }
    }

    public static unsafe SKBitmap Capture(Rectangle rectangle, bool cursor)
    {
        nint display = Open(), image = 0;
        SKBitmap? result = null;
        try
        {
            image = XGetImage(display, XDefaultRootWindow(display), rectangle.X, rectangle.Y,
                (uint)rectangle.Width, (uint)rectangle.Height, nuint.MaxValue, 2);
            if (image == 0) throw new InvalidOperationException("XGetImage failed. Check the selected screen bounds.");
            XImage info = Marshal.PtrToStructure<XImage>(image);
            result = new SKBitmap(rectangle.Width, rectangle.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (int y = 0; y < rectangle.Height; y++)
            {
                uint* target = (uint*)((byte*)result.GetPixels() + y * result.RowBytes);
                byte* row = (byte*)info.Data + y * info.BytesPerLine;
                for (int x = 0; x < rectangle.Width; x++)
                {
                    ulong pixel = 0;
                    int bytes = info.BitsPerPixel / 8;
                    if (bytes is 2 or 3 or 4)
                    {
                        for (int b = 0; b < bytes; b++) pixel |= (ulong)row[x * bytes + b] << (8 * (info.ByteOrder == 0 ? b : bytes - b - 1));
                    }
                    else pixel = (ulong)XGetPixel(image, x, y);
                    target[x] = 0xff000000 | (uint)(Channel(pixel, info.RedMask) << 16) | (uint)(Channel(pixel, info.GreenMask) << 8) | Channel(pixel, info.BlueMask);
                }
            }
            if (cursor) DrawCursor(display, result, rectangle.Location);
            return result;
        }
        catch { result?.Dispose(); throw; }
        finally { if (image != 0) XDestroyImage(image); XCloseDisplay(display); }
    }

    private static uint Channel(ulong pixel, nuint mask)
    {
        ulong m = (ulong)mask;
        if (m == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(m);
        return (uint)(((pixel & m) >> shift) * 255 / (m >> shift));
    }

    private static unsafe void DrawCursor(nint display, SKBitmap target, Point origin)
    {
        nint pointer = XFixesGetCursorImage(display);
        if (pointer == 0) return;
        try
        {
            XCursor image = Marshal.PtrToStructure<XCursor>(pointer);
            using SKBitmap bitmap = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            uint* pixels = (uint*)bitmap.GetPixels();
            for (int i = 0; i < image.Width * image.Height; i++) pixels[i] = (uint)((nuint*)image.Pixels)[i];
            using SKCanvas canvas = new(target);
            canvas.DrawBitmap(bitmap, image.X - image.XHot - origin.X, image.Y - image.YHot - origin.Y);
        }
        finally { XFree(pointer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int Width, Height, XOffset, Format;
        public nint Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
        public nuint RedMask, GreenMask, BlueMask;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct XCursor
    {
        public short X, Y;
        public ushort Width, Height, XHot, YHot;
        public nuint Serial;
        public nint Pixels;
    }
    [DllImport(X11)] private static extern nint XOpenDisplay(string? name);
    [DllImport(X11)] private static extern int XCloseDisplay(nint display);
    [DllImport(X11)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(X11)] private static extern nint XGetImage(nint display, nint window, int x, int y, uint width, uint height, nuint planes, int format);
    [DllImport(X11)] private static extern nuint XGetPixel(nint image, int x, int y);
    [DllImport(X11)] private static extern int XDestroyImage(nint image);
    [DllImport(X11)] private static extern int XFree(nint pointer);
    [DllImport(X11)] private static extern bool XQueryPointer(nint display, nint window, out nint root, out nint child, out int x, out int y, out int wx, out int wy, out uint mask);
    [DllImport(X11)] private static extern int XWarpPointer(nint display, nint source, nint destination, int sx, int sy, uint sw, uint sh, int dx, int dy);
    [DllImport(X11)] private static extern int XFlush(nint display);
    [DllImport("libXfixes.so.3")] private static extern nint XFixesGetCursorImage(nint display);
}
