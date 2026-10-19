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

using ShareX.AvaloniaUI.Linux;
using ShareX.HelpersLib;
using System;
using System.Drawing;
using Bitmap = SkiaSharp.SKBitmap;

namespace ShareX.ScreenCaptureLib
{
    public class Screenshot
    {
        public bool CaptureCursor { get; set; }
        public bool CaptureClientArea { get; set; }
        public bool RemoveOutsideScreenArea { get; set; } = true;
        public bool CaptureShadow { get; set; }
        public int ShadowOffset { get; set; } = 20;
        public bool AutoHideTaskbar { get; set; }
        public bool HDRScreenshotColorCorrection { get; set; }

        public Bitmap CaptureRectangle(Rectangle rect) => LinuxScreenCapture.Capture(rect, CaptureCursor);
        public Bitmap CaptureFullscreen() => CaptureRectangle(LinuxScreenCapture.DesktopBounds);
        public Bitmap CaptureActiveMonitor() => CaptureRectangle(CaptureHelpers.GetActiveScreenBounds());
        public Bitmap CaptureWindow(IntPtr handle) => handle == IntPtr.Zero ? null : CaptureRectangle(CaptureHelpers.GetWindowRectangle(handle));
        public Bitmap CaptureActiveWindow() => CaptureWindow(LinuxCaptureWindows.ActiveHandle);
        // Wayland captures the compositor output, including decorations, opacity and shadows.
        // It cannot reconstruct alpha behind an already composited window.
        public Bitmap CaptureWindowTransparent(IntPtr handle) => CaptureWindow(handle);
        public Bitmap CaptureActiveWindowTransparent() => CaptureActiveWindow();
    }
}
