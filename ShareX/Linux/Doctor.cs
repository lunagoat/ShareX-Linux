// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using Newtonsoft.Json.Linq;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ShareX;

/// <summary>
/// <c>sharex --doctor</c>: explains why a hotkey might not do anything. Checks the session, the configured hotkeys, whether the
/// compositor actually has a bind for each key, what the capture does afterwards (a task that opens a window waits for it), and
/// what the last attempts did according to the log. Needs no display.
/// </summary>
internal static class Doctor
{
    private static int problems;

    private static void Ok(string text) => Console.WriteLine("  [ ok ] " + text);
    private static void Info(string text) => Console.WriteLine("         " + text);
    private static void Problem(string text, string fix = null)
    {
        problems++;
        Console.WriteLine("  [FAIL] " + text);
        if (fix != null) Console.WriteLine("         -> " + fix);
    }

    private static void Section(string title) { Console.WriteLine(); Console.WriteLine(title); }

    private static string Run(string file, params string[] args)
    {
        try
        {
            ProcessStartInfo psi = new(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return output;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static int Execute()
    {
        string versionFile = Path.Combine(AppContext.BaseDirectory, "version");
        Console.WriteLine("ShareX doctor - release " + (File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "(development build)"));

        string discoveredCompositor = CompositorEnvironment.Ensure();
        string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "?";
        Section("Session");
        Info($"desktop={desktop}  session={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "?"}  WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "-"}  DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "-"}");
        string display = DisplayGuard.Check();
        if (display == null) Ok("an X server is reachable (ShareX's windows need one)");
        else Problem(display.Split('\n')[0], "see docs/COMPATIBILITY.md");

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")))
        {
            Info("niri: " + (Run("niri", "msg", "version")?.Split('\n').FirstOrDefault()?.Trim() ?? "(niri command not found)"));
            try
            {
                string workspaces = Run("niri", "msg", "--json", "workspaces");
                string windows = Run("niri", "msg", "--json", "windows");
                if (workspaces != null && windows != null)
                {
                    JArray ws = JArray.Parse(workspaces);
                    Info($"workspaces: {ws.Count}, active: {string.Join(",", ws.Where(x => (bool?)x["is_active"] == true).Select(x => "#" + x["idx"] + " on " + x["output"]))}");
                    foreach (JToken w in JArray.Parse(windows).Where(x => (string)x["app_id"] == "ShareX"))
                        Info($"ShareX window \"{w["title"]}\" is on workspace id {w["workspace_id"]}");
                }
            }
            catch (Exception) { }
        }

        bool wlroots = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));
        if (wlroots)
        {
            foreach (string tool in new[] { "grim", "slurp", "wl-copy" })
            {
                if (Run("sh", "-c", "command -v " + tool)?.Trim().Length > 0) Ok(tool + " is installed");
                else Problem(tool + " is not installed", tool == "wl-copy" ? "install wl-clipboard" : "install " + tool + " with your package manager (needed for screenshots / region selection on this desktop)");
            }
        }

        int running = Process.GetProcessesByName("ShareX").Count(x => x.Id != Environment.ProcessId);
        if (running > 0) Ok("ShareX is running (" + running + " process" + (running > 1 ? "es" : "") + ")");
        else Info("ShareX is not running; a key bind will start it (that takes a second or two).");

        // The running ShareX decides what to do from ITS environment, which can differ from this terminal's (autostart, systemd, launchers).
        foreach (Process other in Process.GetProcessesByName("ShareX").Where(x => x.Id != Environment.ProcessId))
        {
            try
            {
                Dictionary<string, string> env = File.ReadAllText($"/proc/{other.Id}/environ").Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Split('=', 2)).Where(x => x.Length == 2).GroupBy(x => x[0]).ToDictionary(x => x.Key, x => x.First()[1]);
                foreach (string key in new[] { "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS", "NIRI_SOCKET", "SWAYSOCK", "HYPRLAND_INSTANCE_SIGNATURE", "DISPLAY", "XDG_CURRENT_DESKTOP" })
                {
                    string mine = Environment.GetEnvironmentVariable(key);
                    if (string.IsNullOrEmpty(mine)) continue;

                    // ShareX finds the compositor sockets and the X server itself when they are missing, so those are only worth a note.
                    bool selfHealing = key is "NIRI_SOCKET" or "SWAYSOCK" or "HYPRLAND_INSTANCE_SIGNATURE" or "DISPLAY" or "XDG_CURRENT_DESKTOP";
                    if (!env.ContainsKey(key))
                    {
                        if (selfHealing) Info($"the running ShareX (pid {other.Id}) was started without {key}; it looks the compositor up itself");
                        else Problem($"the running ShareX (pid {other.Id}) was started without {key}, so it cannot work properly",
                            "restart it from your session: sharex -ExitShareX, then use the key again (or start it with your compositor's spawn-at-startup)");
                    }
                    else if (env[key] != mine && key is "WAYLAND_DISPLAY" or "NIRI_SOCKET" or "SWAYSOCK")
                    {
                        Problem($"the running ShareX has a different {key} ({env[key]}) than this terminal ({mine})", "restart ShareX from your current session");
                    }
                }
            }
            catch (Exception) { /* not readable */ }
        }
        if (discoveredCompositor != null) Info("this terminal had no compositor variable; ShareX falls back to: " + discoveredCompositor);

        List<HotkeySettings> hotkeys = [];
        Section("Hotkeys configured in ShareX");
        try
        {
            string path = Path.Combine(AppPaths.PersonalFolder, "HotkeysConfig.json");
            hotkeys = (File.Exists(path) ? HotkeysConfig.Load(path, null, false) : new HotkeysConfig()).Hotkeys
                .Where(x => x?.HotkeyInfo != null && x.TaskSettings != null && x.TaskSettings.Job != HotkeyType.None && x.HotkeyInfo.IsValidHotkey).ToList();
            HotkeyNames.Assign(hotkeys);
            if (hotkeys.Count == 0) Problem("no hotkey is configured", "open ShareX -> Hotkey settings and add one, or delete HotkeysConfig.json to get the defaults");
            foreach (HotkeySettings h in hotkeys) Info($"{h.HotkeyInfo,-24} -> {h.TaskSettings.Job}");
            if (hotkeys.Count > 0 && !hotkeys.Any(h => h.HotkeyInfo.KeyCode == InputKey.PrintScreen && !h.HotkeyInfo.Control && !h.HotkeyInfo.Alt && !h.HotkeyInfo.Shift && !h.HotkeyInfo.Win))
                Info("note: no ShareX hotkey uses Print Screen on its own, so pressing just Print does nothing by design. Change a hotkey in ShareX or press one listed above.");
        }
        catch (Exception e) { Problem("could not read the hotkey settings: " + e.Message); }

        Section("What happens after a capture");
        try
        {
            string path = Path.Combine(AppPaths.PersonalFolder, "ApplicationConfig.json");
            if (File.Exists(path))
            {
                string jobs = JObject.Parse(File.ReadAllText(path)).SelectTokens("..AfterCaptureJob").Select(x => (string)x).FirstOrDefault() ?? "";
                Info("default after-capture tasks: " + (jobs.Length > 0 ? jobs : "(default)"));
                string[] blocking = new[] { "ShowQuickTaskMenu", "ShowAfterCaptureWindow", "BeautifyImage", "AddImageEffects", "AnnotateImage" }
                    .Where(b => jobs.Split(',').Select(x => x.Trim()).Contains(b)).ToArray();
                if (blocking.Length > 0)
                    Problem("these tasks open a window and the capture waits for it: " + string.Join(", ", blocking),
                        "if a key press seems to do nothing, look for a ShareX window (maybe on another workspace); remove the task in Task settings -> After capture");
                else Ok("no after-capture task opens a window");
            }
        }
        catch (Exception e) { Info("could not read the settings: " + e.Message); }

        Section("Compositor key binds");
        BindDialect? dialect = CompositorBinds.Detect();
        if (dialect is null) Info("no supported compositor config found");
        else if (dialect is BindDialect.Kde or BindDialect.Gnome or BindDialect.Niri)
        {
            Info(dialect + ": key binds are made outside a config file ShareX edits; see: sharex --print-binds " + dialect.ToString().ToLowerInvariant());
            if (dialect == BindDialect.Kde) Info("KDE: assign the keys under System Settings -> Shortcuts -> ShareX (and free Print from Spectacle).");
        }
        else
        {
            string config = CompositorBinds.ConfigPath(dialect.Value);
            if (!File.Exists(config)) Problem("config not found: " + config);
            else
            {
                string[] lines = File.ReadAllLines(config);
                Dictionary<string, string> bound = CompositorBinds.FindBinds(lines, dialect.Value, includeShareXBlock: true);
                bool block = lines.Any(x => x.Contains(">>> ShareX binds"));
                Info("config: " + config + (block ? "  (has a ShareX block)" : "  (no ShareX block)"));
                int missing = 0;
                foreach (HotkeySettings h in hotkeys)
                {
                    List<string> mods = new();
                    if (h.HotkeyInfo.Win) mods.Add("SUPER");
                    if (h.HotkeyInfo.Control) mods.Add("CTRL");
                    if (h.HotkeyInfo.Alt) mods.Add("ALT");
                    if (h.HotkeyInfo.Shift) mods.Add("SHIFT");
                    string key = LinuxKeyNames.KeySym(h.HotkeyInfo.KeyCode) ?? h.HotkeyInfo.KeyCode.ToString();
                    string combo = HotkeyBindText.Normalize(mods, key);
                    string name = h.HotkeyInfo + " (" + h.TaskSettings.Job + ")";
                    if (!bound.TryGetValue(combo, out string line)) { Info("not bound in the config: " + name); missing++; }
                    else if (line.Contains("sharex", StringComparison.OrdinalIgnoreCase) && !line.Contains(h.TaskSettings.Job.ToString(), StringComparison.OrdinalIgnoreCase))
                        Problem(name + " is bound to a different ShareX action: " + (line.Length > 90 ? line[..90] + "..." : line), "run: sharex --install-binds (it rewrites ShareX's block), or edit that line");
                    else if (line.Contains("sharex", StringComparison.OrdinalIgnoreCase)) Ok(name + " is bound to ShareX");
                    else Problem(name + " is bound to something else: " + (line.Length > 90 ? line[..90] + "..." : line),
                        "remove that line, or change the ShareX hotkey; ShareX skips keys that are already taken");
                }
                if (missing > 0) Problem(missing + " hotkey(s) have no bind in your compositor config", "run: sharex --install-binds   (backs the file up first)");
            }

            if (dialect is BindDialect.HyprlandLua or BindDialect.HyprlandConf && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            {
                string errors = Run("hyprctl", "configerrors")?.Trim();
                if (!string.IsNullOrEmpty(errors)) Problem("Hyprland reports config errors, so binds may not have loaded:\n         " + errors.Replace("\n", "\n         "), "fix them, then: hyprctl reload");
                else Ok("Hyprland reports no config errors");
                int shortcuts = (Run("hyprctl", "globalshortcuts") ?? "").Split('\n').Count(x => x.StartsWith("sharex:"));
                Info(shortcuts + " ShareX portal shortcut(s) are listed by Hyprland (only used if you bind them with the global dispatcher).");
            }
        }

        Section("Recent activity (log)");
        try
        {
            string logs = Path.GetDirectoryName(AppPaths.LogsFilePath);
            string latest = Directory.Exists(logs) ? Directory.GetFiles(logs, "*.txt").OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
            if (latest == null) Info("no log yet");
            else
            {
                string[] interesting = File.ReadAllLines(latest).Where(x => x.Contains("Hotkey triggered") || x.Contains("Arguments received") || x.Contains("Executing:")
                    || x.Contains("Task status") || x.Contains("Task completed") || x.Contains("Image saved") || x.Contains("failed", StringComparison.OrdinalIgnoreCase)).ToArray();
                interesting = System.Linq.Enumerable.TakeLast(interesting, 8).ToArray();
                foreach (string line in interesting) Info(line.Length > 150 ? line[..150] : line);
                if (interesting.Length > 0 && interesting[^1].Contains("Task status: Preparing"))
                    Problem("the last capture never finished (stuck at 'Preparing')", "usually a window it opened is still waiting; check the after-capture tasks above");
                Info("log: " + latest);
            }
        }
        catch (Exception e) { Info("could not read the log: " + e.Message); }

        Console.WriteLine();
        Console.WriteLine(problems == 0 ? "No problems found." : problems + " problem(s) found.");
        return problems == 0 ? 0 : 1;
    }
}
