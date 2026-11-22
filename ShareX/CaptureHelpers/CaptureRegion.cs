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

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;
using System;
using System.Drawing;
using System.Threading.Tasks;
using Bitmap = SkiaSharp.SKBitmap;

namespace ShareX
{
    public class CaptureRegion : CaptureBase
    {
        // Lets the region capture window disappear and the window's title bar finish fading to its active color, which
        // Windows starts when the region capture window closes. Shorter waits failed on Windows 11 because the two white
        // background captures differed.
        private const int TransparentCaptureDelay = 300;

        protected override TaskMetadata Execute(TaskSettings taskSettings)
        {
            return null;
        }

        protected override async Task<TaskMetadata> ExecuteAsync(TaskSettings taskSettings)
        {
            return await ExecuteRegionCaptureAvaloniaAsync(taskSettings);
        }

        protected async Task<TaskMetadata> ExecuteRegionCaptureAvaloniaAsync(TaskSettings taskSettings)
        {
            // Start the native overlay helper first: its startup then overlaps the screenshot below.
            using NativeRegionCapture.PrewarmedHelper prewarmed = NativeRegionCapture.Prewarm(
                taskSettings.CaptureSettingsReference.RegionCaptureOptions,
                taskSettings.ToolsSettingsReference.ImageEditorOptions,
                !taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation);

            Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
            screenshot.CaptureCursor = System.OperatingSystem.IsLinux() && taskSettings.CaptureSettings.ShowCursor;

            bool activeMonitorMode = ShareX.AvaloniaUI.Linux.KdeCapture.UseActiveMonitor(
                taskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode,
                System.OperatingSystem.IsLinux() && ShareX.AvaloniaUI.Linux.KdeCapture.IsSession,
                System.OperatingSystem.IsLinux() && ShareX.AvaloniaUI.Linux.NiriIpc.IsAvailable);
            if (NativeRegionCapture.Choose(taskSettings.CaptureSettings.RegionCaptureOptions, out _)) activeMonitorMode = false;
            Rectangle screenBounds = activeMonitorMode
                ? CaptureHelpers.GetActiveScreenBounds()
                : CaptureHelpers.GetScreenBounds();

            SKBitmap frozenScreenshot = screenshot.CaptureRectangle(screenBounds);

            if (frozenScreenshot == null || frozenScreenshot.Handle == IntPtr.Zero)
            {
                return null;
            }

            SKBitmap cursorBitmap = null;
            Point cursorPosition = Point.Empty;

            if (taskSettings.CaptureSettings.ShowCursor && !System.OperatingSystem.IsLinux())
            {
                CursorData cursorData = new CursorData();
                if (cursorData.IsVisible)
                {
                    cursorBitmap = cursorData.ToBitmap(frozenScreenshot, screenBounds.Location);
                    cursorPosition = new Point(
                        cursorData.DrawPosition.X - screenBounds.X,
                        cursorData.DrawPosition.Y - screenBounds.Y);
                }
            }

            AvaloniaRegionCaptureRequest request = new AvaloniaRegionCaptureRequest
            {
                Screenshot = frozenScreenshot,
                // Screen captures are opaque: WindowsImageInterop.Capture and HDR correction set alpha to 255.
                ScreenshotIsOpaque = true,
                ScreenBounds = screenBounds,
                RegionCaptureOptions = taskSettings.CaptureSettingsReference.RegionCaptureOptions,
                ImageEditorOptions = taskSettings.ToolsSettingsReference.ImageEditorOptions,
                EnableAnnotations = !taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation,
                AllowMultipleRegions = true,
                CursorBitmap = cursorBitmap,
                CursorPosition = cursorPosition
            };

            AvaloniaRegionCaptureResult result = await NativeRegionCapture.CaptureAsync(request, prewarmed);
            if (result == null)
            {
                return null;
            }

            using (result.Image)
            {
                Bitmap output = null;

                // Linux: compositors do not expose window shapes or shadows, so a second capture would add nothing.
                if (!OperatingSystem.IsLinux() && result.WindowHandle != IntPtr.Zero && taskSettings.CaptureSettings.CaptureTransparent)
                {
                    output = await CaptureWindowTransparentAsync(taskSettings, result);
                }

                output ??= result.Image.Copy();
                TaskMetadata metadata = new TaskMetadata(output);

                if (result.ImageModified)
                {
                    AllowAnnotation = false;
                }

                if (result.WindowInfo != null)
                {
                    metadata.UpdateInfo(result.WindowInfo);
                }

                return metadata;
            }
        }

        /// <summary>
        /// When a detected window was captured whole, gives it the rounded corners and, with "Capture window with
        /// shadow", the shadow that window capture has with "Capture window with transparency". The pixels inside the
        /// window, annotations included, still come from the region capture. Returns null if that isn't possible.
        /// </summary>
        private static async Task<Bitmap> CaptureWindowTransparentAsync(TaskSettings taskSettings, AvaloniaRegionCaptureResult result)
        {
            await Task.Delay(TransparentCaptureDelay);

            try
            {
                Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
                screenshot.CaptureCursor = false;
                return screenshot.CaptureWindowTransparent(result.WindowHandle, result.Image, result.ScreenRectangle);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, "Transparent window capture failed.");
                return null;
            }
        }
    }
}
