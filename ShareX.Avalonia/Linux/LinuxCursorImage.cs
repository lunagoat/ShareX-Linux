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

using System.Runtime.InteropServices;
using SkiaSharp;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>Loads the installed Xcursor theme without connecting to an X server (also works on Wayland).</summary>
public static class LinuxCursorImage
{
    public static unsafe SKBitmap? Load(string type, int size = 32)
    {
        string name = type switch
        {
            "Hand" => "pointer", "IBeam" => "text", "Cross" => "crosshair", "Help" => "help",
            "WaitCursor" => "wait", "AppStarting" => "progress", "No" => "not-allowed",
            "SizeAll" => "move", "SizeNESW" => "nesw-resize", "SizeNWSE" => "nwse-resize",
            "SizeWE" => "ew-resize", "SizeNS" => "ns-resize", "UpArrow" => "up-arrow", _ => "left_ptr"
        };
        nint pointer = XcursorLibraryLoadImage(name, Environment.GetEnvironmentVariable("XCURSOR_THEME"), size);
        if (pointer == 0) pointer = XcursorLibraryLoadImage("left_ptr", null, size);
        if (pointer == 0) return null;
        try
        {
            CursorImage image = Marshal.PtrToStructure<CursorImage>(pointer);
            SKBitmap bitmap = new((int)image.Width, (int)image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (int row = 0; row < image.Height; row++)
                Buffer.MemoryCopy((byte*)image.Pixels + row * image.Width * 4, (byte*)bitmap.GetPixels() + row * bitmap.RowBytes, bitmap.RowBytes, image.Width * 4);
            return bitmap;
        }
        finally { XcursorImageDestroy(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorImage
    {
        public uint Version, Size, Width, Height, XHot, YHot, Delay;
        public nint Pixels;
    }
    [DllImport("libXcursor.so.1")] private static extern nint XcursorLibraryLoadImage(string name, string? theme, int size);
    [DllImport("libXcursor.so.1")] private static extern void XcursorImageDestroy(nint image);
}
