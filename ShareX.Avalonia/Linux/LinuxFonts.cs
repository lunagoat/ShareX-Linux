// ShareX - GPL-3.0-or-later. Desktop fonts for the Windows font names used throughout upstream ShareX.
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>
/// Upstream ShareX asks for Windows fonts by name (Segoe UI, Consolas, Cascadia Mono). Avalonia does not follow
/// fontconfig's aliases for a missing family, so every one of them became the proportional default font, and
/// monospace views (hashes, logs, HTTP responses, FFmpeg command lines) lost their alignment. Map them to the
/// desktop's own fonts through fontconfig's generic names, so ShareX also follows the user's font settings.
/// </summary>
public static class LinuxFonts
{
    public static FontManagerOptions CreateOptions() => new()
    {
        FontFamilyMappings = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase)
        {
            ["Segoe UI"] = new FontFamily("sans-serif"),
            ["Segoe UI Emoji"] = new FontFamily("emoji"),
            ["Consolas"] = new FontFamily("monospace"),
            ["Cascadia Mono"] = new FontFamily("monospace"),
            ["Cascadia Code"] = new FontFamily("monospace"),
            ["Lucida Console"] = new FontFamily("monospace")
        }
    };
}
