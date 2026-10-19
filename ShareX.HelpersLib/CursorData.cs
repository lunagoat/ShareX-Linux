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

using SkiaSharp;
using System;
using System.Drawing;

namespace ShareX.HelpersLib
{
    public class CursorData : IDisposable
    {
        private SKBitmap bitmap;
        public IntPtr Handle { get; private set; }
        public Point Position { get; private set; }
        public Size Size { get; private set; }
        public float SizeMultiplier => 1f;
        public bool IsDefaultSize => true;
        public Point Hotspot { get; private set; }
        public Point DrawPosition => new(Position.X - Hotspot.X, Position.Y - Hotspot.Y);
        public bool IsVisible => bitmap != null;
        public CursorData() => UpdateCursorData();
        public void UpdateCursorData()
        {
            bitmap?.Dispose();
            bitmap = LinuxCursorImage.Capture(out Point position, out Point hotspot);
            Position = bitmap == null ? CaptureHelpers.GetCursorPosition() : position;
            Hotspot = hotspot;
            Size = bitmap == null ? Size.Empty : new Size(bitmap.Width, bitmap.Height);
        }
        public static float GetCursorSizeMultiplier() => 1f;
        // HDC drawing has no Linux equivalent. Capture backends draw onto SKBitmap or request compositor cursor inclusion.
        public void DrawCursor(IntPtr destination) { }
        public void DrawCursor(IntPtr destination, Point offset) { }
        public void DrawCursor(SKBitmap image) => DrawCursor(image, Point.Empty);
        public void DrawCursor(SKBitmap image, Point offset)
        {
            if (bitmap == null) return;
            using SKCanvas canvas = new(image);
            canvas.DrawBitmap(bitmap, DrawPosition.X - offset.X, DrawPosition.Y - offset.Y);
        }
        public SKBitmap ToBitmap() => bitmap?.Copy();
        public SKBitmap ToBitmap(SKBitmap background, Point offset) => ToBitmap();
        public void Dispose() { bitmap?.Dispose(); bitmap = null; }
    }
}
