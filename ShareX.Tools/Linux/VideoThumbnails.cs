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

using Avalonia.Media.Imaging;
using ShareX.Tools.Linux;
using System.Globalization;

namespace ShareX.Tools;

internal static class NativeVideoThumbnails
{
    public static async Task<IReadOnlyList<VideoTrimmerThumbnail>> CreateAsync(string path, double duration, CancellationToken token)
    {
        List<VideoTrimmerThumbnail> thumbnails = [];
        try
        {
            for (int i = 0; i < 12; i++)
            {
                double target = duration * i / 12;
                byte[] png = await ToolProcess.RunAsync("ffmpeg", ["-v", "error", "-ss", target.ToString("R", CultureInfo.InvariantCulture),
                    "-i", Path.GetFullPath(path), "-frames:v", "1", "-vf", "scale=160:90:force_original_aspect_ratio=decrease",
                    "-threads", "1", "-f", "image2pipe", "-c:v", "png", "pipe:1"], token);
                using MemoryStream stream = new(png, writable: false);
                thumbnails.Add(new(target, new Bitmap(stream)));
            }
            return thumbnails;
        }
        catch
        {
            foreach (var thumbnail in thumbnails) thumbnail.Image.Dispose();
            throw;
        }
    }
}
