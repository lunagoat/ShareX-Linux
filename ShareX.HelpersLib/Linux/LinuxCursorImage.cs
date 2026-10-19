// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using SkiaSharp;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ShareX.HelpersLib;

internal static class LinuxCursorImage
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorImage
    {
        public short X, Y;
        public ushort Width, Height, HotX, HotY;
        public nuint Serial;
        public IntPtr Pixels;
        public nuint Atom;
        public IntPtr Name;
    }
    [DllImport("libXfixes.so.3")] private static extern IntPtr XFixesGetCursorImage(IntPtr display);
    public static SKBitmap Capture(out Point position, out Point hotspot)
    {
        position = hotspot = Point.Empty;
        if (LinuxWindowSystem.IsWayland) return null; // compositor capture must include the Wayland cursor
        IntPtr display = LinuxX11.XOpenDisplay(null);
        if (display == IntPtr.Zero) return null;
        IntPtr pointer = IntPtr.Zero;
        try
        {
            pointer = XFixesGetCursorImage(display);
            if (pointer == IntPtr.Zero) return null;
            CursorImage image = Marshal.PtrToStructure<CursorImage>(pointer);
            if (image.Width == 0 || image.Height == 0 || image.Width > 1024 || image.Height > 1024) return null;
            SKBitmap bitmap = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            // XFixes stores premultiplied ARGB32 pixels in native unsigned longs (64 bits on Linux x64).
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    Marshal.WriteInt32(bitmap.GetPixels(), (y * image.Width + x) * 4,
                        unchecked((int)Marshal.ReadIntPtr(image.Pixels, (y * image.Width + x) * IntPtr.Size).ToInt64()));
            position = new(image.X, image.Y);
            hotspot = new(image.HotX, image.HotY);
            return bitmap;
        }
        catch (DllNotFoundException) { return null; }
        finally { if (pointer != IntPtr.Zero) LinuxX11.XFree(pointer); LinuxX11.XCloseDisplay(display); }
    }
}
