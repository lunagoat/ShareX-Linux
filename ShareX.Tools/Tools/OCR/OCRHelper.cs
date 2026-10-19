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
using System.Diagnostics;
using System.Globalization;
using Bitmap = SkiaSharp.SKBitmap;
using ImageFormat = SkiaSharp.SKEncodedImageFormat;

namespace ShareX.Tools;

/// <summary>OCR through the system <c>tesseract</c> binary (the Windows build used the WinRT OCR engine).</summary>
public static class OCRHelper
{
    // Tesseract language codes (ISO 639-2) that don't map to a .NET culture through ThreeLetterISOLanguageName.
    private static readonly Dictionary<string, string> SpecialCultures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chi_sim"] = "zh-Hans",
        ["chi_tra"] = "zh-Hant",
        ["deu"] = "de",
        ["fra"] = "fr",
        ["nld"] = "nl",
        ["ces"] = "cs",
        ["ell"] = "el",
        ["fas"] = "fa",
        ["isl"] = "is",
        ["kat"] = "ka",
        ["mkd"] = "mk",
        ["msa"] = "ms",
        ["ron"] = "ro",
        ["slk"] = "sk",
        ["sqi"] = "sq",
        ["bod"] = "bo",
        ["cym"] = "cy",
        ["eus"] = "eu",
        ["hye"] = "hy",
        ["mya"] = "my",
        ["zho"] = "zh"
    };

    private static OCRLanguageOption[]? _languages;

    public static bool IsSupported => GetInstalledTesseractCodes().Length > 0;

    public static OCRLanguageOption[] AvailableLanguages
    {
        get
        {
            ThrowIfNotSupported();
            return _languages ??= GetInstalledTesseractCodes()
                .Where(x => x != "osd")
                .Select(code =>
                {
                    string tag = CodeToTag(code);
                    string name;
                    try { name = CultureInfo.GetCultureInfo(tag).EnglishName; }
                    catch (CultureNotFoundException) { name = code; }
                    return new OCRLanguageOption(name, tag);
                })
                .ToArray();
        }
    }

    public static void ThrowIfNotSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(
                "OCR requires the 'tesseract' command and at least one language pack (for example the tesseract-data-eng package).");
        }
    }

    public static async Task<string> OCR(Bitmap bitmap, string languageTag = "en", float scaleFactor = 1f,
        bool singleLine = false)
    {
        ThrowIfNotSupported();
        scaleFactor = Math.Max(scaleFactor, 1f);

        return await Task.Run(async () =>
        {
            using Bitmap scaledBitmap = SkiaImageHelpers.ScaleImageFast(bitmap, scaleFactor);
            return await OCRInternal(scaledBitmap, languageTag, singleLine);
        });
    }

    private static async Task<string> OCRInternal(Bitmap bitmap, string languageTag, bool singleLine)
    {
        string code = TagToCode(languageTag);

        if (!GetInstalledTesseractCodes().Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"OCR language \"{languageTag}\" is not installed (tesseract language pack \"{code}\").");
        }

        ProcessStartInfo psi = new("tesseract")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        psi.ArgumentList.Add("stdin");
        psi.ArgumentList.Add("stdout");
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add(code);
        psi.ArgumentList.Add("--psm");
        psi.ArgumentList.Add(singleLine ? "7" : "3");

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start tesseract.");

        using (SkiaSharp.SKData data = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(ImageFormat.Png, 100))
        {
            await process.StandardInput.BaseStream.WriteAsync(data.ToArray());
        }

        process.StandardInput.Close();

        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        string text = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("tesseract failed: " + (await stderrTask).Trim());
        }

        text = text.Replace("\f", "").TrimEnd();
        string[] lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Chinese and Japanese are written without spaces, tesseract inserts one between every glyph.
        if (languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
            languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            lines = lines.Select(line => line.Replace(" ", "")).ToArray();
        }

        return string.Join(singleLine ? " " : Environment.NewLine, lines);
    }

    private static string[]? _installedCodes;

    private static string[] GetInstalledTesseractCodes()
    {
        if (_installedCodes != null)
        {
            return _installedCodes;
        }

        try
        {
            ProcessStartInfo psi = new("tesseract", "--list-langs")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null) return _installedCodes = [];

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);

            // First line is a "List of available languages" header.
            _installedCodes = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .ToArray();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _installedCodes = [];
        }

        return _installedCodes;
    }

    private static string CodeToTag(string code)
    {
        if (SpecialCultures.TryGetValue(code, out string? tag)) return tag;

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.ThreeLetterISOLanguageName.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                return culture.Name;
            }
        }

        return code;
    }

    private static string TagToCode(string tag)
    {
        foreach (KeyValuePair<string, string> pair in SpecialCultures)
        {
            if (pair.Value.Equals(tag, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }

        try { return CultureInfo.GetCultureInfo(tag).ThreeLetterISOLanguageName; }
        catch (CultureNotFoundException) { return tag; }
    }
}
