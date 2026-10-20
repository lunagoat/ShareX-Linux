// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>
/// Optional region selection with <c>slurp</c>, a layer-shell overlay drawn by the compositor itself, selected with
/// SHAREX_REGION_SELECTOR=slurp. ShareX's own overlay (annotation tools, magnifier) is the default everywhere; this is the alternative
/// for setups where that overlay misbehaves, and it can select across monitors on niri.
/// </summary>
public static class NativeRegionSelector
{
    public static bool ShouldUse
    {
        get
        {
            if (!OperatingSystem.IsLinux() || !LinuxScreenCapture.IsWayland) return false;
            string choice = Environment.GetEnvironmentVariable("SHAREX_REGION_SELECTOR")?.Trim().ToLowerInvariant() ?? "";
            if (choice == "overlay") return false;
            // niri and sway: the overlay is placed through the compositor's IPC (see LinuxCaptureOverlay), so slurp is only used when asked for.
            if (choice != "slurp") return false;
            return FindExecutable("slurp") != null;
        }
    }

    private static string? FindExecutable(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);

    /// <summary>Lets the user drag a region or click a window. Returns null when cancelled (Escape or right click).</summary>
    public static Rectangle? Select(IEnumerable<Rectangle> windows)
    {
        ProcessStartInfo info = new("slurp")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // x y width height in logical desktop pixels: the same coordinates grim and ShareX's capture code use.
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add("%x %y %w %h");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("#3b9bffff");
        info.ArgumentList.Add("-w");
        info.ArgumentList.Add("2");

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start slurp.");

        // Predefined rectangles let a click select a whole window.
        foreach (Rectangle w in windows.Where(w => w.Width > 1 && w.Height > 1))
        {
            process.StandardInput.WriteLine(FormattableString.Invariant($"{w.X},{w.Y} {w.Width}x{w.Height}"));
        }

        process.StandardInput.Close();
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || output.Length == 0) return null;

        string[] parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) return null;

        int[] v = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return v[2] > 0 && v[3] > 0 ? new Rectangle(v[0], v[1], v[2], v[3]) : null;
    }
}
