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

#nullable disable

using ShareX.HelpersLib;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;

namespace ShareX.Tools
{
    public class VideoThumbnailer
    {
        public delegate void ProgressChangedEventHandler(int current, int length);
        public event ProgressChangedEventHandler ProgressChanged;

        public string FFmpegPath { get; private set; }
        public VideoThumbnailOptions Options { get; private set; }
        public string MediaPath { get; private set; }
        public VideoInfo VideoInfo { get; private set; }

        public VideoThumbnailer(string ffmpegPath, VideoThumbnailOptions options)
        {
            FFmpegPath = Linux.ToolProcess.Resolve("ffmpeg", ffmpegPath);
            Options = options;
        }

        private void UpdateVideoInfo()
        {
            using (FFmpegCLIManager ffmpeg = new FFmpegCLIManager(FFmpegPath))
            {
                VideoInfo = ffmpeg.GetVideoInfo(MediaPath);
            }
        }

        public List<VideoThumbnailInfo> TakeThumbnails(string mediaPath)
        {
            MediaPath = mediaPath;

            UpdateVideoInfo();

            if (VideoInfo == null || VideoInfo.Duration == TimeSpan.Zero)
            {
                return null;
            }

            List<VideoThumbnailInfo> tempThumbnails = new List<VideoThumbnailInfo>();

            for (int i = 0; i < Options.ThumbnailCount; i++)
            {
                string mediaFileName = Path.GetFileNameWithoutExtension(MediaPath);

                double interval = VideoInfo.Duration.TotalSeconds / (Options.ThumbnailCount + 1d);
                double timeSliceElapsed = Options.RandomFrame
                    ? interval * (i + 0.25 + RandomFast.NextDouble() * 0.5)
                    : interval * (i + 1);
                string fileName = $"{mediaFileName}-{i + 1:000}-{timeSliceElapsed.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}.{Options.ImageFormat.GetDescription()}";
                string tempThumbnailPath = Path.Combine(GetOutputDirectory(), fileName);
                Linux.ToolProcess.RunAsync(FFmpegPath, ["-v", "error", "-nostdin", "-ss", timeSliceElapsed.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    "-i", Path.GetFullPath(MediaPath), "-frames:v", "1", "-threads", "1", "-y", tempThumbnailPath]).GetAwaiter().GetResult();

                if (File.Exists(tempThumbnailPath))
                {
                    VideoThumbnailInfo screenshotInfo = new VideoThumbnailInfo(tempThumbnailPath)
                    {
                        Timestamp = TimeSpan.FromSeconds(timeSliceElapsed)
                    };

                    tempThumbnails.Add(screenshotInfo);
                }

                OnProgressChanged(i + 1, Options.ThumbnailCount);
            }

            return Finish(tempThumbnails);
        }

        private List<VideoThumbnailInfo> Finish(List<VideoThumbnailInfo> tempThumbnails)
        {
            List<VideoThumbnailInfo> thumbnails = new List<VideoThumbnailInfo>();

            if (tempThumbnails != null && tempThumbnails.Count > 0)
            {
                if (Options.CombineScreenshots)
                {
                    using (SKBitmap img = CombineScreenshots(tempThumbnails))
                    {
                        string tempFilePath = Path.Combine(GetOutputDirectory(), Path.GetFileNameWithoutExtension(MediaPath) + Options.FilenameSuffix + "." + Options.ImageFormat.GetDescription());
                        SkiaImageHelpers.SaveImage(img, tempFilePath);
                        thumbnails.Add(new VideoThumbnailInfo(tempFilePath));
                    }

                    if (!Options.KeepScreenshots)
                    {
                        tempThumbnails.ForEach(x => File.Delete(x.FilePath));
                    }
                }
                else
                {
                    thumbnails.AddRange(tempThumbnails);
                }

                if (Options.OpenDirectory && thumbnails.Count > 0)
                {
                    FileHelpers.OpenFolderWithFile(thumbnails[0].FilePath);
                }
            }

            return thumbnails;
        }

        protected void OnProgressChanged(int current, int length)
        {
            ProgressChanged?.Invoke(current, length);
        }

        private string GetOutputDirectory()
        {
            string directory;

            switch (Options.OutputLocation)
            {
                default:
                case ThumbnailLocationType.DefaultFolder:
                    directory = Options.DefaultOutputDirectory;
                    break;
                case ThumbnailLocationType.ParentFolder:
                    directory = Path.GetDirectoryName(MediaPath);
                    break;
                case ThumbnailLocationType.CustomFolder:
                    directory = FileHelpers.ExpandFolderVariables(Options.CustomOutputDirectory);
                    break;
            }

            FileHelpers.CreateDirectory(directory);

            return directory;
        }

        private SKBitmap CombineScreenshots(List<VideoThumbnailInfo> thumbnails)
        {
            List<SKBitmap> images = new List<SKBitmap>();
            SKBitmap finalImage = null;

            try
            {
                string infoString = "";
                int infoStringHeight = 0;

                if (Options.AddVideoInfo)
                {
                    infoString = VideoInfo.ToString();

                    using (ImageFont font = new ImageFont("Arial", 12))
                    {
                        infoStringHeight = SkiaDrawing.MeasureText(infoString, font).Height;
                    }
                }

                foreach (VideoThumbnailInfo thumbnail in thumbnails)
                {
                    SKBitmap bmp = SkiaImageHelpers.LoadImage(thumbnail.FilePath);

                    if (Options.MaxThumbnailWidth > 0 && bmp.Width > Options.MaxThumbnailWidth)
                    {
                        int maxThumbnailHeight = (int)((float)Options.MaxThumbnailWidth / bmp.Width * bmp.Height);
                        SKBitmap resized = SkiaImageHelpers.ResizeImage(bmp, Options.MaxThumbnailWidth, Math.Max(1, maxThumbnailHeight));
                        bmp.Dispose();
                        bmp = resized;
                    }

                    images.Add(bmp);
                }

                int columnCount = Math.Max(1, Options.ColumnCount);

                int thumbWidth = images[0].Width;

                int width = (Options.Padding * 2) +
                            (thumbWidth * columnCount) +
                            ((columnCount - 1) * Options.Spacing);

                int rowCount = (int)Math.Ceiling(images.Count / (float)columnCount);

                int thumbHeight = images[0].Height;

                int height = (Options.Padding * 3) +
                             infoStringHeight +
                             (thumbHeight * rowCount) +
                             ((rowCount - 1) * Options.Spacing);

                finalImage = SkiaImageHelpers.CreateBitmap(width, height);

                using (SKCanvas g = new SKCanvas(finalImage))
                {
                    g.Clear(Color.WhiteSmoke);

                    if (!string.IsNullOrEmpty(infoString))
                    {
                        using (ImageFont font = new ImageFont("Arial", 12))
                        {
                            using SKPaint textPaint = SkiaDrawing.Fill(Color.Black);
                            g.DrawText(infoString, new PointF(Options.Padding, Options.Padding), font, textPaint);
                        }
                    }

                    int i = 0;
                    int offsetY = (Options.Padding * 2) + infoStringHeight;

                    for (int y = 0; y < rowCount; y++)
                    {
                        int offsetX = Options.Padding;

                        for (int x = 0; x < columnCount; x++)
                        {
                            if (Options.DrawShadow)
                            {
                                int shadowOffset = 3;

                                using (SKPaint shadowBrush = SkiaDrawing.Fill(Color.FromArgb(75, Color.Black)))
                                {
                                    g.FillRectangle(shadowBrush, offsetX + shadowOffset, offsetY + shadowOffset, thumbWidth, thumbHeight);
                                }
                            }

                            g.DrawImage(images[i], offsetX, offsetY, thumbWidth, thumbHeight);

                            if (Options.DrawBorder)
                            {
                                using SKPaint borderPaint = SkiaDrawing.Stroke(Color.Black);
                                g.DrawRectangleProper(borderPaint, offsetX, offsetY, thumbWidth, thumbHeight);
                            }

                            if (Options.AddTimestamp)
                            {
                                int timestampOffset = 10;

                                using (ImageFont font = new ImageFont("Arial", 10, ImageFontStyle.Bold))
                                {
                                    using SKPaint foreground = SkiaDrawing.Fill(Color.White);
                                    using SKPaint shadow = SkiaDrawing.Fill(Color.Black);
                                    g.DrawText(thumbnails[i].Timestamp.ToString(), new PointF(offsetX + timestampOffset + 1, offsetY + timestampOffset + 1), font, shadow);
                                    g.DrawText(thumbnails[i].Timestamp.ToString(), new PointF(offsetX + timestampOffset, offsetY + timestampOffset), font, foreground);
                                }
                            }

                            i++;

                            if (i >= images.Count)
                            {
                                return finalImage;
                            }

                            offsetX += thumbWidth + Options.Spacing;
                        }

                        offsetY += thumbHeight + Options.Spacing;
                    }
                }

                return finalImage;
            }
            catch
            {
                if (finalImage != null)
                {
                    finalImage.Dispose();
                }

                throw;
            }
            finally
            {
                foreach (SKBitmap image in images)
                {
                    if (image != null)
                    {
                        image.Dispose();
                    }
                }
            }
        }
    }
}
