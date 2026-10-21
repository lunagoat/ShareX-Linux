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

using ShareX.ImageEditor.Core.ImageEffects.Filters;
using ShareX.ImageEditor.Core.ImageEffects.Manipulations;
using SkiaSharp;

namespace ShareX.ImageEditor.Core.ImageEffects.Helpers;

/// <summary>
/// Image manipulation utilities for the Editor library.
/// </summary>
public static class ImageHelpers
{
    public static SKBitmap Crop(SKBitmap bitmap, SKRectI rect)
    {
        if (bitmap is null) throw new ArgumentNullException(nameof(bitmap));

        SKRectI bounded = new SKRectI(
            Math.Clamp(rect.Left, 0, bitmap.Width),
            Math.Clamp(rect.Top, 0, bitmap.Height),
            Math.Clamp(rect.Right, 0, bitmap.Width),
            Math.Clamp(rect.Bottom, 0, bitmap.Height));

        if (bounded.Width <= 0 || bounded.Height <= 0)
        {
            return new SKBitmap();
        }

        SKBitmap subset = new SKBitmap(bounded.Width, bounded.Height);
        return bitmap.ExtractSubset(subset, bounded) ? subset : new SKBitmap();
    }

    public static SKBitmap Crop(SKBitmap bitmap, int x, int y, int width, int height)
    {
        // Add in 64 bits before clipping so large positive extents cannot wrap
        // into negative coordinates or an enormous allocation.
        int right = (int)Math.Clamp((long)x + width, int.MinValue, int.MaxValue);
        int bottom = (int)Math.Clamp((long)y + height, int.MinValue, int.MaxValue);
        return Crop(bitmap, new SKRectI(x, y, right, bottom));
    }

    /// <summary>
    /// Cut out a horizontal or vertical section from the image and join the remaining parts
    /// </summary>
    /// <param name="bitmap">Source bitmap</param>
    /// <param name="startPos">Start position (X for vertical cut, Y for horizontal cut)</param>
    /// <param name="endPos">End position (X for vertical cut, Y for horizontal cut)</param>
    /// <param name="isVertical">True for vertical cut (left-right), false for horizontal cut (top-bottom)</param>
    /// <returns>New bitmap with the section cut out</returns>
    public static SKBitmap CutOut(SKBitmap bitmap, int startPos, int endPos, bool isVertical)
    {
        if (bitmap is null) throw new ArgumentNullException(nameof(bitmap));

        // Ensure start is before end
        if (startPos > endPos)
        {
            (startPos, endPos) = (endPos, startPos);
        }

        if (isVertical)
        {
            // Vertical cut: remove columns from startPos to endPos
            int cutWidth = endPos - startPos;
            if (cutWidth <= 0 || cutWidth >= bitmap.Width)
            {
                return bitmap; // Nothing to cut or invalid range
            }

            int newWidth = bitmap.Width - cutWidth;
            int newHeight = bitmap.Height;

            SKBitmap result = new SKBitmap(newWidth, newHeight);
            using SKCanvas canvas = new SKCanvas(result);

            // Draw left part (0 to startPos)
            if (startPos > 0)
            {
                SKRect srcLeft = new SKRect(0, 0, startPos, bitmap.Height);
                SKRect dstLeft = new SKRect(0, 0, startPos, newHeight);
                canvas.DrawBitmap(bitmap, srcLeft, dstLeft);
            }

            // Draw right part (endPos to width), positioned after left part
            if (endPos < bitmap.Width)
            {
                SKRect srcRight = new SKRect(endPos, 0, bitmap.Width, bitmap.Height);
                SKRect dstRight = new SKRect(startPos, 0, newWidth, newHeight);
                canvas.DrawBitmap(bitmap, srcRight, dstRight);
            }

            return result;
        }
        else
        {
            // Horizontal cut: remove rows from startPos to endPos
            int cutHeight = endPos - startPos;
            if (cutHeight <= 0 || cutHeight >= bitmap.Height)
            {
                return bitmap; // Nothing to cut or invalid range
            }

            int newWidth = bitmap.Width;
            int newHeight = bitmap.Height - cutHeight;

            SKBitmap result = new SKBitmap(newWidth, newHeight);
            using SKCanvas canvas = new SKCanvas(result);

            // Draw top part (0 to startPos)
            if (startPos > 0)
            {
                SKRect srcTop = new SKRect(0, 0, bitmap.Width, startPos);
                SKRect dstTop = new SKRect(0, 0, newWidth, startPos);
                canvas.DrawBitmap(bitmap, srcTop, dstTop);
            }

            // Draw bottom part (endPos to height), positioned after top part
            if (endPos < bitmap.Height)
            {
                SKRect srcBottom = new SKRect(0, endPos, bitmap.Width, bitmap.Height);
                SKRect dstBottom = new SKRect(0, startPos, newWidth, newHeight);
                canvas.DrawBitmap(bitmap, srcBottom, dstBottom);
            }

            return result;
        }
    }

    public static void SaveBitmap(SKBitmap bitmap, string filePath, int quality = 100)
    {
        if (bitmap is null) throw new ArgumentNullException(nameof(bitmap));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SKEncodedImageFormat format = GetEncodedFormat(filePath);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, quality);
        using FileStream stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static SKEncodedImageFormat GetEncodedFormat(string filePath)
    {
        string extension = Path.GetExtension(filePath)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
        return extension switch
        {
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            "avif" => SKEncodedImageFormat.Avif,
            "gif" => SKEncodedImageFormat.Gif,
            "bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };
    }

    /// <summary>
    /// Resize a bitmap to the specified dimensions with optional aspect ratio preservation
    /// </summary>
    /// <param name="source">Source bitmap to resize</param>
    /// <param name="width">Target width</param>
    /// <param name="height">Target height</param>
    /// <param name="maintainAspectRatio">If true, scales proportionally to fit within width/height</param>
    /// <param name="quality">Filter quality for scaling</param>
    /// <returns>New resized bitmap</returns>
    public static SKBitmap Resize(SKBitmap source, int width, int height, bool maintainAspectRatio = false, SKSamplingOptions sampling = default)
    {
        // Quality ignored in Effect currently or hardcoded to High
        return new ResizeImageEffect { Width = width, Height = height, MaintainAspectRatio = maintainAspectRatio }.Apply(source);
    }

    /// <summary>
    /// Rotate bitmap 90 degrees clockwise
    /// </summary>
    public static SKBitmap Rotate90Clockwise(SKBitmap source)
    {
        return RotateImageEffect.Clockwise90.Apply(source);
    }

    /// <summary>
    /// Rotate bitmap 90 degrees counter-clockwise
    /// </summary>
    public static SKBitmap Rotate90CounterClockwise(SKBitmap source)
    {
        return RotateImageEffect.CounterClockwise90.Apply(source);
    }

    /// <summary>
    /// Rotate bitmap 180 degrees
    /// </summary>
    public static SKBitmap Rotate180(SKBitmap source)
    {
        return RotateImageEffect.Rotate180.Apply(source);
    }

    /// <summary>
    /// Flip bitmap horizontally (mirror left-to-right)
    /// </summary>
    /// <summary>
    /// Flip bitmap horizontally (mirror left-to-right)
    /// </summary>
    public static SKBitmap FlipHorizontal(SKBitmap source)
    {
        return FlipImageEffect.Horizontal.Apply(source);
    }

    /// <summary>
    /// Flip bitmap vertically (mirror top-to-bottom)
    /// </summary>
    /// <summary>
    /// Flip bitmap vertically (mirror top-to-bottom)
    /// </summary>
    public static SKBitmap FlipVertical(SKBitmap source)
    {
        return FlipImageEffect.Vertical.Apply(source);
    }

    /// <summary>
    /// Resize canvas by adding padding around the image
    /// </summary>
    /// <param name="source">Source bitmap</param>
    /// <param name="left">Left padding in pixels</param>
    /// <param name="top">Top padding in pixels</param>
    /// <param name="right">Right padding in pixels</param>
    /// <param name="bottom">Bottom padding in pixels</param>
    /// <param name="backgroundColor">Background color for the padding</param>
    /// <returns>New bitmap with padding</returns>
    public static SKBitmap ResizeCanvas(SKBitmap source, int left, int top, int right, int bottom, SKColor backgroundColor)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        int newWidth = source.Width + left + right;
        int newHeight = source.Height + top + bottom;

        if (newWidth <= 0 || newHeight <= 0)
        {
            throw new ArgumentException("Canvas size after padding must be greater than 0");
        }

        SKBitmap result = new SKBitmap(newWidth, newHeight, source.ColorType, source.AlphaType);
        using (SKCanvas canvas = new SKCanvas(result))
        {
            canvas.Clear(backgroundColor);
            canvas.DrawBitmap(source, left, top);
        }
        return result;
    }

    /// <summary>
    /// Auto-crops the image by removing edges that match the specified color within tolerance
    /// </summary>
    /// <param name="source">Source bitmap</param>
    /// <param name="color">Color to match for cropping</param>
    /// <param name="tolerance">Color tolerance (0-255)</param>
    /// <returns>Cropped bitmap</returns>
    public static SKBitmap AutoCrop(SKBitmap source, SKColor color, int tolerance = 0)
    {
        return new AutoCropImageEffect(color, tolerance).Apply(source);
    }

    /// <summary>
    /// Finds the bounds of pixels that differ from the background, or null for a uniform image.
    /// </summary>
    internal static SKRectI? FindContentBounds(SKBitmap source, SKColor background, int tolerance)
    {
        // Only the outermost content pixels determine the crop. Scan inward
        // from each edge and stop at the first non-background pixel.
        bool RowMatches(int y)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (!ColorsMatch(source.GetPixel(x, y), background, tolerance)) return false;
            }
            return true;
        }

        bool ColumnMatches(int x, int top, int bottom)
        {
            for (int y = top; y <= bottom; y++)
            {
                if (!ColorsMatch(source.GetPixel(x, y), background, tolerance)) return false;
            }
            return true;
        }

        int minY = 0;
        while (minY < source.Height && RowMatches(minY)) minY++;
        if (minY == source.Height) return null;

        int maxY = source.Height - 1;
        while (maxY > minY && RowMatches(maxY)) maxY--;

        int minX = 0;
        while (minX < source.Width && ColumnMatches(minX, minY, maxY)) minX++;
        int maxX = source.Width - 1;
        while (maxX > minX && ColumnMatches(maxX, minY, maxY)) maxX--;

        return new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    internal static SKBitmap CropToContentBounds(SKBitmap source, SKRectI? bounds)
    {
        return bounds is SKRectI rect
            ? Crop(source, rect.Left, rect.Top, rect.Width, rect.Height)
            : new SKBitmap(1, 1, source.ColorType, source.AlphaType);
    }

    internal static bool ColorsMatch(SKColor c1, SKColor c2, int tolerance)
    {
        return Math.Abs(c1.Red - c2.Red) <= tolerance &&
               Math.Abs(c1.Green - c2.Green) <= tolerance &&
               Math.Abs(c1.Blue - c2.Blue) <= tolerance &&
               Math.Abs(c1.Alpha - c2.Alpha) <= tolerance;
    }

    // ============== FILTERS ==============

    public enum BorderType { Outside, Inside }
    public enum DashStyle { Solid, Dash, Dot, DashDot }

    public static SKBitmap ApplyBorder(SKBitmap source, BorderType type, int size, DashStyle dashStyle, SKColor color)
    {
        return new BorderImageEffect(type, size, dashStyle, color).Apply(source);
    }

    public static SKBitmap ApplyBevel(
        SKBitmap source,
        int size,
        float strength,
        float lightAngle,
        SKColor highlightColor,
        SKColor shadowColor)
    {
        return new BevelImageEffect
        {
            Size = size,
            Strength = strength,
            LightAngle = lightAngle,
            HighlightColor = highlightColor,
            ShadowColor = shadowColor
        }.Apply(source);
    }

    public static SKBitmap ApplyOutline(SKBitmap source, int size, int padding, bool outlineOnly, SKColor color)
    {
        return new OutlineImageEffect(size, padding, outlineOnly, color).Apply(source);
    }

    public static SKBitmap ApplyShadow(SKBitmap source, float opacity, int size, SKColor color, int offsetX, int offsetY, bool autoResize)
    {
        return new ShadowImageEffect(opacity, size, color, offsetX, offsetY, autoResize).Apply(source);
    }

    public static SKBitmap ApplyGlow(SKBitmap source, int size, float strength, SKColor color, int offsetX, int offsetY, bool autoResize)
    {
        return new GlowImageEffect(size, strength, color, offsetX, offsetY, autoResize).Apply(source);
    }

    public static SKBitmap ApplyBloom(SKBitmap source, float threshold, float softKnee, float radius, float intensity)
    {
        return new BloomImageEffect
        {
            Threshold = threshold,
            SoftKnee = softKnee,
            Radius = radius,
            Intensity = intensity
        }.Apply(source);
    }

    public static SKBitmap ApplyHalation(SKBitmap source, float threshold, float radius, float strength, float warmth)
    {
        return new HalationImageEffect
        {
            Threshold = threshold,
            Radius = radius,
            Strength = strength,
            Warmth = warmth
        }.Apply(source);
    }

    public static SKBitmap ApplyChromaticAberration(SKBitmap source, float amount, float edgeStart, float strength)
    {
        return new ChromaticAberrationImageEffect
        {
            Amount = amount,
            EdgeStart = edgeStart,
            Strength = strength
        }.Apply(source);
    }

    public static SKBitmap ApplyLiquidGlass(SKBitmap source, float distortion, float refraction, int chromaShift, float gloss, float flowScale)
    {
        return new LiquidGlassImageEffect
        {
            Distortion = distortion,
            Refraction = refraction,
            ChromaShift = chromaShift,
            Gloss = gloss,
            FlowScale = flowScale
        }.Apply(source);
    }

    public static SKBitmap ApplyNeonEdgeGlow(
        SKBitmap source,
        float edgeStrength,
        int threshold,
        float glowRadius,
        float glowIntensity,
        float baseDim,
        SKColor neonColor)
    {
        return new NeonEdgeGlowImageEffect
        {
            EdgeStrength = edgeStrength,
            Threshold = threshold,
            GlowRadius = glowRadius,
            GlowIntensity = glowIntensity,
            BaseDim = baseDim,
            NeonColor = neonColor
        }.Apply(source);
    }

    public static SKBitmap ApplyReflection(SKBitmap source, int percentage, int maxAlpha, int minAlpha, int offset, bool skew, int skewSize)
    {
        return new ReflectionImageEffect(percentage, maxAlpha, minAlpha, offset, skew, skewSize).Apply(source);
    }

    public static SKBitmap ApplyTornEdge(SKBitmap source, int depth, int range, bool top, bool right, bool bottom, bool left, bool curved)
    {
        return new TornEdgeImageEffect(depth, range, top, right, bottom, left, curved).Apply(source);
    }

    public static SKBitmap ApplyWaveEdge(SKBitmap source, int depth, int range, bool top, bool right, bool bottom, bool left)
    {
        return new WaveEdgeImageEffect(depth, range, top, right, bottom, left).Apply(source);
    }

    public static SKBitmap ApplySlice(SKBitmap source, int minHeight, int maxHeight, int minShift, int maxShift)
    {
        return new SliceImageEffect(minHeight, maxHeight, minShift, maxShift).Apply(source);
    }

}