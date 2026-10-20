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
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Bitmap = SkiaSharp.SKBitmap;

namespace ShareX
{
    public class CaptureRegion : CaptureBase
    {
        protected override TaskMetadata Execute(TaskSettings taskSettings)
        {
            return null;
        }

        protected override async Task<TaskMetadata> ExecuteAsync(TaskSettings taskSettings)
        {
            return await ExecuteRegionCaptureAvaloniaAsync(taskSettings);
        }

        /// <summary>Region selection by the compositor's own overlay (slurp), for desktops where a window cannot be placed over every monitor.</summary>
        private static async Task<TaskMetadata> ExecuteNativeSelectorCaptureAsync(Screenshot screenshot)
        {
            Rectangle? region = await Task.Run(() =>
            {
                IEnumerable<Rectangle> windows;
                try
                {
                    windows = ShareX.AvaloniaUI.Linux.LinuxCaptureWindows.GetWindows().Select(w => w.Rectangle).ToArray();
                }
                catch (Exception)
                {
                    windows = [];
                }

                return ShareX.AvaloniaUI.Linux.NativeRegionSelector.Select(windows);
            });

            if (region == null)
            {
                return null;
            }

            // Give the compositor a moment to take slurp's overlay off the screen before capturing.
            await Task.Delay(150);
            SKBitmap image = await Task.Run(() => screenshot.CaptureRectangle(region.Value));
            return image == null || image.Handle == IntPtr.Zero ? null : new TaskMetadata(image);
        }

        protected async Task<TaskMetadata> ExecuteRegionCaptureAvaloniaAsync(TaskSettings taskSettings)
        {
            Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
            screenshot.CaptureCursor = System.OperatingSystem.IsLinux() && taskSettings.CaptureSettings.ShowCursor;

            if (System.OperatingSystem.IsLinux() && ShareX.AvaloniaUI.Linux.NativeRegionSelector.ShouldUse)
            {
                return await ExecuteNativeSelectorCaptureAsync(screenshot);
            }

            bool activeMonitorMode = taskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode ||
                (System.OperatingSystem.IsLinux() && ShareX.AvaloniaUI.Linux.NiriIpc.IsAvailable); // a niri window cannot span monitors
            Rectangle screenBounds = activeMonitorMode
                ? CaptureHelpers.GetActiveScreenBounds()
                : CaptureHelpers.GetScreenBounds();

            SKBitmap frozenScreenshot = activeMonitorMode
                ? screenshot.CaptureActiveMonitor()
                : screenshot.CaptureFullscreen();

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
                    using Bitmap cursor = cursorData.ToBitmap(frozenScreenshot, screenBounds.Location);
                    cursorBitmap = cursor.Copy();
                    cursorPosition = new Point(
                        cursorData.DrawPosition.X - screenBounds.X,
                        cursorData.DrawPosition.Y - screenBounds.Y);
                }
            }

            AvaloniaRegionCaptureRequest request = new AvaloniaRegionCaptureRequest
            {
                Screenshot = frozenScreenshot,
                ScreenBounds = screenBounds,
                RegionCaptureOptions = taskSettings.CaptureSettingsReference.RegionCaptureOptions,
                ImageEditorOptions = taskSettings.ToolsSettingsReference.ImageEditorOptions,
                EnableAnnotations = !taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation,
                CursorBitmap = cursorBitmap,
                CursorPosition = cursorPosition
            };

            AvaloniaRegionCaptureResult result = await RegionCaptureIntegration.CaptureAsync(request);
            if (result == null)
            {
                return null;
            }

            using (result.Image)
            {
                Bitmap output = result.Image.Copy();
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
    }
}
