// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ShareX;

/// <summary>
/// Writes ShareX's key binds into the compositor configuration (Hyprland and sway have no way for an application to bind keys itself).
/// The lines live in one marked block so they can be updated or removed again; the file is backed up first and keys that the
/// user already binds elsewhere are left alone.
/// </summary>
internal static class CompositorBinds
{
    private const string BeginMarker = ">>> ShareX binds (managed by `sharex --install-binds`; remove with `sharex --remove-binds`)";
    private const string EndMarker = "<<< ShareX binds";

    public static BindDialect? Detect()
    {
        string desktop = ((Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "") + ":" + (Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP") ?? "")).ToLowerInvariant();
        string configDir = ConfigHome();

        // What is running right now decides first; config files only break ties (a Hyprland config may exist on a GNOME session).
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) || desktop.Contains("hyprland"))
            return File.Exists(Path.Combine(configDir, "hypr", "hyprland.lua")) ? BindDialect.HyprlandLua : BindDialect.HyprlandConf;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) || desktop.Contains("sway")) return BindDialect.Sway;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")) || desktop.Contains("niri")) return BindDialect.Niri;
        if (desktop.Contains("kde") || desktop.Contains("plasma")) return BindDialect.Kde;
        if (desktop.Contains("gnome") || desktop.Contains("ubuntu")) return BindDialect.Gnome;

        if (File.Exists(Path.Combine(configDir, "hypr", "hyprland.lua"))) return BindDialect.HyprlandLua;
        if (File.Exists(Path.Combine(configDir, "hypr", "hyprland.conf"))) return BindDialect.HyprlandConf;
        if (File.Exists(Path.Combine(configDir, "sway", "config"))) return BindDialect.Sway;
        return null;
    }

    public static string ConfigPath(BindDialect dialect)
    {
        string configDir = ConfigHome();
        return dialect switch
        {
            BindDialect.HyprlandLua => Path.Combine(configDir, "hypr", "hyprland.lua"),
            BindDialect.HyprlandConf => Path.Combine(configDir, "hypr", "hyprland.conf"),
            _ => Path.Combine(configDir, "sway", "config")
        };
    }

    private static string ConfigHome()
    {
        string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    private static string Comment(BindDialect dialect) => dialect == BindDialect.HyprlandLua ? "--" : "#";

    private static (int Start, int End) FindBlock(string[] lines, BindDialect dialect)
    {
        int start = Array.FindIndex(lines, x => x.Contains(Comment(dialect) + " " + BeginMarker.Split('(')[0].Trim(), StringComparison.Ordinal));
        if (start < 0) return (-1, -1);
        int end = Array.FindIndex(lines, start, x => x.Contains(Comment(dialect) + " " + EndMarker, StringComparison.Ordinal));
        return (start, end < 0 ? lines.Length - 1 : end);
    }

    /// <summary>Key combinations bound in the config outside ShareX's own block.</summary>
    private static HashSet<string> BoundKeys(string[] lines, BindDialect dialect)
    {
        HashSet<string> keys = new();
        (int start, int end) = FindBlock(lines, dialect);

        for (int i = 0; i < lines.Length; i++)
        {
            if (start >= 0 && i >= start && i <= end) continue;
            string line = lines[i].Trim();
            if (line.StartsWith(Comment(dialect))) continue;

            Match m;
            if (dialect == BindDialect.HyprlandLua)
            {
                if ((m = Regex.Match(line, "^hl\\.bind\\(\\s*\"([^\"]*)\"")).Success)
                {
                    string[] parts = m.Groups[1].Value.Split('+');
                    keys.Add(HotkeyBindText.Normalize(parts[..^1], parts[^1]));
                }
                else if ((m = Regex.Match(line, "^hl\\.bind\\(\\s*\\w+\\s*\\.\\.\\s*\"([^\"]*)\"")).Success)
                {
                    string[] parts = ("SUPER" + m.Groups[1].Value).Split('+');
                    keys.Add(HotkeyBindText.Normalize(parts[..^1], parts[^1]));
                }
            }
            else if (dialect == BindDialect.HyprlandConf)
            {
                if ((m = Regex.Match(line, "^bind[a-z]*\\s*=\\s*([^,]*),\\s*([^,]+),")).Success)
                {
                    keys.Add(HotkeyBindText.Normalize(m.Groups[1].Value.Split(new[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries), m.Groups[2].Value));
                }
            }
            else if ((m = Regex.Match(line, "^bindsym\\s+(\\S+)\\s")).Success)
            {
                string[] parts = m.Groups[1].Value.Split('+');
                keys.Add(HotkeyBindText.Normalize(parts[..^1], parts[^1]));
            }
        }

        return keys;
    }

    public static string Install(BindDialect dialect, IEnumerable<HotkeySettings> hotkeys, bool portal, string launcher, bool reload = true)
    {
        string path = ConfigPath(dialect);
        if (!File.Exists(path))
        {
            return "Could not find " + path + ". Use `sharex --print-binds` and add the lines to your compositor configuration yourself.";
        }

        string[] lines = File.ReadAllLines(path);
        HashSet<string> taken = BoundKeys(lines, dialect);
        string block = HotkeyBindText.Build(hotkeys, dialect, portal, launcher, taken);

        string c = Comment(dialect);
        List<string> blockLines = new() { c + " " + BeginMarker };
        blockLines.AddRange(block.TrimEnd().Split('\n').Select(x => x.TrimEnd('\r')));
        blockLines.Add(c + " " + EndMarker);

        (int start, int end) = FindBlock(lines, dialect);
        List<string> result = lines.ToList();
        if (start >= 0)
        {
            result.RemoveRange(start, end - start + 1);
            result.InsertRange(start, blockLines);
        }
        else
        {
            if (result.Count > 0 && result[^1].Trim().Length > 0) result.Add("");
            result.AddRange(blockLines);
        }

        Backup(path);
        File.WriteAllText(path, string.Join("\n", result) + "\n");

        int skipped = blockLines.Count(x => x.Contains("skipped, key already bound"));
        int added = blockLines.Count(x => !x.TrimStart().StartsWith(c));
        string message = $"Added {added} ShareX key bind(s) to {path}" + (skipped > 0 ? $" ({skipped} skipped because you already use those keys; they are listed as comments in the block)" : "") + ".";
        return message + " " + Reload(dialect, reload);
    }

    public static string Remove(BindDialect dialect, bool reload = true)
    {
        string path = ConfigPath(dialect);
        if (!File.Exists(path)) return "Could not find " + path + ".";

        string[] lines = File.ReadAllLines(path);
        (int start, int end) = FindBlock(lines, dialect);
        if (start < 0) return "No ShareX block found in " + path + ".";

        List<string> result = lines.ToList();
        result.RemoveRange(start, end - start + 1);
        while (result.Count > 0 && result[^1].Trim().Length == 0) result.RemoveAt(result.Count - 1);

        Backup(path);
        File.WriteAllText(path, string.Join("\n", result) + "\n");
        return "Removed the ShareX key binds from " + path + ". " + Reload(dialect, reload);
    }

    private static void Backup(string path)
    {
        string stamp = path + ".bak-sharex-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string target = stamp;
        for (int i = 2; File.Exists(target); i++) target = stamp + "-" + i;
        File.Copy(path, target, false);
    }

    private static string Reload(BindDialect dialect, bool reload)
    {
        if (!reload) return "";

        try
        {
            ProcessStartInfo psi = dialect == BindDialect.Sway ? new ProcessStartInfo("swaymsg", "reload") : new ProcessStartInfo("hyprctl", "reload");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            using Process process = Process.Start(psi);
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? "Compositor configuration reloaded." : "Reload it yourself (the reload command failed).";
        }
        catch (Exception)
        {
            return "Reload your compositor configuration to apply it.";
        }
    }

    /// <summary>Implements <c>sharex --install-binds [hyprland|hyprland-conf|sway] [--global]</c> and <c>--remove-binds</c>.</summary>
    public static int Run(string[] args, bool remove)
    {
        string name = args.FirstOrDefault(x => !x.StartsWith("-"));
        BindDialect? dialect = name?.ToLowerInvariant() switch
        {
            "hyprland" or "hyprland-lua" or "lua" => BindDialect.HyprlandLua,
            "hyprland-conf" or "conf" => BindDialect.HyprlandConf,
            "sway" => BindDialect.Sway,
            "niri" => BindDialect.Niri,
            "gnome" => BindDialect.Gnome,
            "kde" or "plasma" => BindDialect.Kde,
            null => Detect(),
            _ => throw new ArgumentException("Unknown compositor '" + name + "'. Use hyprland, hyprland-conf, sway, niri, gnome or kde.")
        };

        if (dialect == null)
        {
            Console.Error.WriteLine("No Hyprland or sway configuration found. Use `sharex --print-binds` and add the lines to your compositor by hand.");
            return 1;
        }

        if (dialect is BindDialect.Niri or BindDialect.Gnome or BindDialect.Kde)
        {
            // These desktops keep their shortcuts somewhere an app cannot edit safely (niri's KDL config, dconf, KDE's shortcut daemon).
            Console.WriteLine(dialect == BindDialect.Niri ? "niri: ShareX cannot edit config.kdl for you. Add the lines below to your binds { } block:" :
                dialect == BindDialect.Gnome ? "GNOME: run  sharex --print-binds gnome | sh  to add the shortcuts (shown below)." : "KDE Plasma:");
            Console.WriteLine();
            return HotkeyBindText.PrintBinds([dialect.Value.ToString().ToLowerInvariant()]);
        }

        if (remove)
        {
            Console.WriteLine(Remove(dialect.Value));
            return 0;
        }

        string path = System.IO.Path.Combine(AppPaths.PersonalFolder, "HotkeysConfig.json");
        HotkeysConfig config = File.Exists(path) ? HotkeysConfig.Load(path, null, false) : new HotkeysConfig();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string launcher = File.Exists(System.IO.Path.Combine(home, ".local", "bin", "sharex")) ? "~/.local/bin/sharex" : "sharex";
        Console.WriteLine(Install(dialect.Value, config.Hotkeys, args.Contains("--global"), launcher));
        return 0;
    }
}
