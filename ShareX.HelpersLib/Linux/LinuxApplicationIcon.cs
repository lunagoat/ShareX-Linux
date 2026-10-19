// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace ShareX.HelpersLib;

public static class LinuxApplicationIcon
{
    public static SKBitmap Load(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Contains('/') || appId.Contains('\\')) return null;
        try
        {
            string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share");
            string[] roots = new[] { dataHome }.Concat((Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share").Split(':')).ToArray();
            string icon = appId;
            foreach (string root in roots)
            {
                string directory = Path.Combine(root, "applications");
                if (!Directory.Exists(directory)) continue;
                string desktop = Directory.EnumerateFiles(directory, "*.desktop").FirstOrDefault(file =>
                    Path.GetFileNameWithoutExtension(file).Equals(appId, StringComparison.OrdinalIgnoreCase) ||
                    File.ReadLines(file).Any(line => line.Equals("StartupWMClass=" + appId, StringComparison.OrdinalIgnoreCase)));
                if (desktop == null) continue;
                icon = File.ReadLines(desktop).FirstOrDefault(x => x.StartsWith("Icon="))?[5..] ?? appId;
                break;
            }
            if (Path.IsPathRooted(icon)) return File.Exists(icon) ? SKBitmap.Decode(icon) : null;
            if (icon.Contains('/') || icon.Contains('\\')) return null;
            foreach (string root in roots)
            {
                foreach (string theme in new[] { "hicolor", "Adwaita", "breeze" })
                    foreach (string size in new[] { "64x64", "48x48", "128x128", "32x32", "256x256", "16x16" })
                        foreach (string suffix in new[] { ".png", ".xpm" })
                        {
                            string path = Path.Combine(root, "icons", theme, size, "apps", icon + suffix);
                            if (File.Exists(path)) return SKBitmap.Decode(path);
                        }
                string pixmap = Path.Combine(root, "pixmaps", icon + ".png");
                if (File.Exists(pixmap)) return SKBitmap.Decode(pixmap);
            }
        }
        catch (Exception exception) { DebugHelper.WriteException(exception, "Unable to load application icon."); }
        return null;
    }
}
