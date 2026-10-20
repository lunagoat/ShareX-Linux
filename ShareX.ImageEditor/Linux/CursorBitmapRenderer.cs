// ShareX - GPL-3.0-or-later. Xcursor theme images work on both Wayland and X11.
using System.Runtime.InteropServices;
using ShareX.ImageEditor.Core.Annotations;
using SkiaSharp;

namespace ShareX.ImageEditor.Linux;

internal static class CursorBitmapRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorImage
    {
        public uint Version, Size, Width, Height, XHot, YHot, Delay;
        public IntPtr Pixels;
    }

    [DllImport("libXcursor.so.1", CharSet = CharSet.Ansi)]
    private static extern IntPtr XcursorLibraryLoadImage(string name, string? theme, int size);
    [DllImport("libXcursor.so.1")]
    private static extern void XcursorImageDestroy(IntPtr image);

    internal static SKBitmap? Create(CursorType type)
    {
        string name = type switch
        {
            CursorType.AppStarting => "left_ptr_watch", CursorType.Cross => "crosshair",
            CursorType.Hand => "hand2", CursorType.Help => "question_arrow", CursorType.IBeam => "xterm",
            CursorType.No => "not-allowed", CursorType.SizeAll => "fleur", CursorType.SizeNESW => "nesw-resize",
            CursorType.SizeNS => "ns-resize", CursorType.SizeNWSE => "nwse-resize", CursorType.SizeWE => "ew-resize",
            CursorType.UpArrow => "center_ptr", CursorType.WaitCursor => "watch", _ => "left_ptr"
        };
        IntPtr pointer = IntPtr.Zero;
        try
        {
            string? theme = Environment.GetEnvironmentVariable("XCURSOR_THEME");
            pointer = XcursorLibraryLoadImage(name, theme, 32);
            if (pointer == IntPtr.Zero) pointer = XcursorLibraryLoadImage(name, "Adwaita", 32);
            if (pointer == IntPtr.Zero) pointer = XcursorLibraryLoadImage("left_ptr", "default", 32);
            if (pointer == IntPtr.Zero) return null;
            CursorImage image = Marshal.PtrToStructure<CursorImage>(pointer);
            if (image.Width == 0 || image.Height == 0 || image.Width > 1024 || image.Height > 1024) return null;
            int[] pixels = new int[checked((int)(image.Width * image.Height))];
            Marshal.Copy(image.Pixels, pixels, 0, pixels.Length);
            SKBitmap bitmap = new((int)image.Width, (int)image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            return bitmap;
        }
        catch (DllNotFoundException) { return null; }
        finally { if (pointer != IntPtr.Zero) XcursorImageDestroy(pointer); }
    }
}
