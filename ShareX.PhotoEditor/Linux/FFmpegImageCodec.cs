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

namespace ShareX.PhotoEditor.Linux;

/// <summary>
/// Formats Skia cannot read or write (TIFF, and AVIF/HEIC with a recent FFmpeg), converted through the system ffmpeg.
/// The Windows build used GDI+ for this. Images go through files in a private temporary folder, not pipes, because
/// <see cref="LinuxDesktop.RunAsync"/> exchanges text.
/// </summary>
internal static class FFmpegImageCodec
{
    private const int TimeoutMilliseconds = 60_000;

    /// <summary>Decodes the first frame of <paramref name="path"/>, or returns null when ffmpeg is missing or fails.</summary>
    public static SKBitmap? Decode(string path)
    {
        DirectoryInfo? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("sharex-image-");
            string png = Path.Combine(directory.FullName, "decoded.png");
            (bool success, string output) = LinuxDesktop.RunAsync("ffmpeg",
                ["-v", "error", "-nostdin", "-i", Path.GetFullPath(path), "-frames:v", "1", "-pix_fmt", "rgba", "-y", png],
                TimeoutMilliseconds).GetAwaiter().GetResult();
            if (!success)
            {
                DebugHelper.WriteLine("ffmpeg could not decode \"{0}\": {1}", path, output);
                return null;
            }

            return SKBitmap.Decode(png);
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            return null;
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    /// <summary>Writes <paramref name="bitmap"/> to <paramref name="stream"/> as a lossless (deflate) TIFF with alpha.</summary>
    public static void EncodeTiff(SKBitmap bitmap, Stream stream)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("sharex-image-");
        try
        {
            string png = Path.Combine(directory.FullName, "source.png");
            string tiff = Path.Combine(directory.FullName, "encoded.tif");
            using (FileStream file = File.Create(png))
            {
                bitmap.Save(file, SKEncodedImageFormat.Png);
            }

            (bool success, string output) = LinuxDesktop.RunAsync("ffmpeg",
                ["-v", "error", "-nostdin", "-i", png, "-frames:v", "1", "-c:v", "tiff", "-pix_fmt", "rgba",
                 "-compression_algo", "deflate", "-y", tiff],
                TimeoutMilliseconds).GetAwaiter().GetResult();
            if (!success)
            {
                throw new InvalidOperationException("Saving TIFF files needs ffmpeg. " + output);
            }

            using FileStream encoded = File.OpenRead(tiff);
            encoded.CopyTo(stream);
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    private static void DeleteQuietly(DirectoryInfo? directory)
    {
        try
        {
            directory?.Delete(true);
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
        }
    }
}
