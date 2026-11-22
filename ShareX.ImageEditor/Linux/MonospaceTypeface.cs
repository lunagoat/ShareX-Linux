// ShareX - GPL-3.0-or-later. Monospace text for effects that lay glyphs out on a grid.
using SkiaSharp;

namespace ShareX.ImageEditor.Linux;

internal static class MonospaceTypeface
{
    /// <summary>
    /// Consolas where it is installed (Windows), otherwise the desktop's monospace font (fontconfig "monospace").
    /// SKTypeface.FromFamilyName would silently return the proportional default font for a missing family, which
    /// breaks the character grid of ASCII art and the digital rain effect. Null when no monospace font is found.
    /// </summary>
    public static SKTypeface? Create()
    {
        return SKFontManager.Default.MatchFamily("Consolas") ?? SKFontManager.Default.MatchFamily("monospace");
    }
}
