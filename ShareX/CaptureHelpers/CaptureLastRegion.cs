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

using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Bitmap = SkiaSharp.SKBitmap;

namespace ShareX
{
    public class CaptureLastRegion : CaptureRegion
    {
        protected override async Task<TaskMetadata> ExecuteAsync(TaskSettings taskSettings)
        {
            if (RegionCaptureIntegration.LastRegionRectangle.IsEmpty)
            {
                if (taskSettings?.CaptureSettings?.CaptureCustomRegion.Width > 0 &&
                    taskSettings.CaptureSettings.CaptureCustomRegion.Height > 0)
                {
                    return Execute(taskSettings);
                }

                return await ExecuteRegionCaptureAvaloniaAsync(taskSettings);
            }

            return Execute(taskSettings);
        }

        protected override TaskMetadata Execute(TaskSettings taskSettings)
        {
            Rectangle bounds = RegionCaptureIntegration.LastRegionRectangle;
            IReadOnlyList<Rectangle> regions = RegionCaptureIntegration.LastRegionRectangles;

            // Nothing captured yet in this session (it is kept in memory only): use the configured custom region.
            if (bounds.IsEmpty)
            {
                bounds = taskSettings?.CaptureSettings?.CaptureCustomRegion ?? Rectangle.Empty;
                regions = [];
            }

            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                TaskMetadata metadata = CreateMetadata(bounds);
                Bitmap bmp = TaskHelpers.GetScreenshot(taskSettings).CaptureRectangle(bounds);

                if (bmp != null && regions.Count > 1)
                {
                    // Regions are desktop units; on HiDPI Wayland the capture has more pixels than its desktop area.
                    double scaleX = (double)bmp.Width / bounds.Width, scaleY = (double)bmp.Height / bounds.Height;
                    List<Rectangle> localRegions = regions
                        .Select(region => Rectangle.FromLTRB(
                            (int)Math.Floor((region.Left - bounds.X) * scaleX), (int)Math.Floor((region.Top - bounds.Y) * scaleY),
                            (int)Math.Ceiling((region.Right - bounds.X) * scaleX), (int)Math.Ceiling((region.Bottom - bounds.Y) * scaleY)))
                        .ToList();

                    Bitmap masked = RegionCaptureTasks.ApplyRegionMask(bmp, localRegions);
                    bmp.Dispose();
                    bmp = masked;
                }

                metadata.Image = bmp;
                return metadata;
            }

            return null;
        }
    }
}
