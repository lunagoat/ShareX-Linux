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
using System.IO;

namespace ShareX.HelpersLib
{
    public class PrintHelper : IDisposable
    {
        public PrintType PrintType { get; }
        public SKBitmap Image { get; }
        public string Text { get; }
        public PrintSettings Settings { get; set; }
        public bool Printable => Settings != null && (PrintType == PrintType.Image ? Image != null : !string.IsNullOrEmpty(Text) && Settings.TextFont != null);
        public PrintHelper(SKBitmap image) { PrintType = PrintType.Image; Image = image; }
        public PrintHelper(string text) { PrintType = PrintType.Text; Text = text; }
        public void Dispose() { }
        public void ShowPreview() { if (Printable) PrintPreviewWindow.ShowPreview(RenderPreviewPage, Print); }
        internal (SKBitmap Bitmap, bool HasMore) RenderPreviewPage(int pageIndex)
        {
            Size size = new(850, 1100);
            if (PrintType == PrintType.Image) return (RenderImagePage(Image, size, Settings), false);
            using ImageFont font = Settings.TextFont;
            PrintTextHelper renderer = new() { Text = Text, Font = font };
            renderer.BeginPrint();
            SKBitmap page = null;
            bool hasMore = false;
            for (int index = 0; index <= pageIndex; index++)
            {
                page?.Dispose();
                page = renderer.RenderPage(size, new Rectangle(100, 100, 650, 900), out hasMore);
                if (!hasMore) break;
            }
            return (page, hasMore);
        }
        public void TryDefaultPrinterOverride() { /* CUPS validates the destination at submission. */ }
        public void SavePdf(string path)
        {
            if (!Printable) throw new InvalidOperationException("No printable content.");
            using FileStream stream = new(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            using SKDocument document = SKDocument.CreatePdf(stream);
            bool more;
            int index = 0;
            do
            {
                var rendered = RenderPreviewPage(index++);
                using SKBitmap bitmap = rendered.Bitmap;
                more = rendered.HasMore;
                SKCanvas canvas = document.BeginPage(612, 792);
                if (bitmap != null) canvas.DrawBitmap(bitmap, new SKRect(0, 0, 612, 792));
                document.EndPage();
            } while (more);
            document.Close();
        }
        public bool Print()
        {
            if (!Printable) return false;
            if (LinuxDesktop.FindCommand("lp") == null)
            {
                ShareX.AvaloniaUI.MessageBox.Show("Printing requires the CUPS client command 'lp'. Install your distribution's CUPS client package and configure a printer.", "ShareX");
                return false;
            }
            string printer = Settings.DefaultPrinterOverride;
            int copies = 1;
            if (Settings.ShowPrintDialog && !LinuxPrintDialog.Show(ref printer, ref copies)) return false;
            string directory = Path.Combine(LinuxPaths.CacheHome, "ShareX", "print-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string path = Path.Combine(directory, "print.pdf");
            try
            {
                SavePdf(path);
                var args = new System.Collections.Generic.List<string> { "-t", "ShareX", "-n", copies.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                if (!string.IsNullOrWhiteSpace(printer)) { args.Add("-d"); args.Add(printer); }
                args.Add("--"); args.Add(path);
                var result = LinuxDesktop.RunAsync("lp", args.ToArray(), 15000).GetAwaiter().GetResult();
                if (!result.Success) ShareX.AvaloniaUI.MessageBox.Show("CUPS could not print: " + result.Output, "ShareX");
                return result.Success;
            }
            finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
        }

        internal static SKBitmap RenderImagePage(SKBitmap source, Size pageSize, PrintSettings settings)
        {
            SKBitmap rotated = null;
            SKBitmap image = source;
            SKBitmap page = SkiaImageHelpers.CreateBitmap(pageSize.Width * 3, pageSize.Height * 3);
            Rectangle rectangle = new(0, 0, pageSize.Width, pageSize.Height);
            rectangle.Inflate(-settings.Margin, -settings.Margin);
            try
            {
                using SKCanvas canvas = new(page);
                canvas.Clear(SKColors.White);
                if (rectangle.Width <= 0 || rectangle.Height <= 0) return page;
                if (settings.AutoRotateImage && ((rectangle.Width > rectangle.Height && source.Width < source.Height) ||
                    (rectangle.Width < rectangle.Height && source.Width > source.Height)))
                {
                    rotated = source.Copy();
                    SkiaImageHelpers.RotateFlipInPlace(rotated, 1);
                    image = rotated;
                }
                canvas.Scale(3, 3);
                canvas.ClipRect(rectangle.ToSKRect());
                if (settings.AutoScaleImage)
                {
                    float scale = Math.Min(rectangle.Width / (float)image.Width, rectangle.Height / (float)image.Height);
                    if (!settings.AllowEnlargeImage) scale = Math.Min(1, scale);
                    float width = image.Width * scale, height = image.Height * scale;
                    float x = rectangle.X, y = rectangle.Y;
                    if (settings.CenterImage)
                    {
                        x += (rectangle.Width - width) / 2;
                        y += (rectangle.Height - height) / 2;
                    }
                    canvas.DrawImage(image, x, y, width, height);
                }
                else
                {
                    canvas.DrawBitmap(image, rectangle.X, rectangle.Y);
                }
                return page;
            }
            catch { page.Dispose(); throw; }
            finally { rotated?.Dispose(); }
        }
    }
}
