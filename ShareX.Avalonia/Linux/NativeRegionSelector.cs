// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>
/// Optional region selection with <c>slurp</c>, a layer-shell overlay drawn by the compositor itself, selected with
/// SHAREX_REGION_SELECTOR=slurp. This is also the default on KDE Wayland when installed, and on an empty niri workspace.
/// It selects across outputs without depending on XWayland window placement, but has no annotation tools or magnifier.
/// </summary>
public static class NativeRegionSelector
{
    public sealed record SelectionDecision(bool UseSlurp, string Reason);

    public static SelectionDecision Decide(bool wayland, bool kde, bool slurpInstalled, string? choice, bool emptyNiriWorkspace)
    {
        choice = choice?.Trim().ToLowerInvariant() ?? "";
        if (!wayland) return new(false, "ShareX overlay: X11 session");
        if (choice == "overlay") return new(false, "ShareX overlay: explicitly requested; KDE placement must be verified");
        bool requested = choice == "slurp" || (choice == "" && (kde || emptyNiriWorkspace));
        if (!requested) return new(false, "ShareX overlay: compositor default");
        if (!slurpInstalled) return new(false, "ShareX overlay: slurp is missing; KDE requires verified whole-desktop placement or capture fails with an installation hint");
        return new(true, choice == "slurp" ? "slurp: explicitly requested" : kde
            ? "slurp: KDE Wayland XWayland overlay spanning is unverified; layer-shell selection covers every monitor"
            : "slurp: empty niri workspace");
    }

    public static SelectionDecision Decision => Decide(OperatingSystem.IsLinux() && LinuxScreenCapture.IsWayland, KdeCapture.IsSession,
        FindExecutable("slurp") != null, Environment.GetEnvironmentVariable("SHAREX_REGION_SELECTOR"),
        LinuxScreenCapture.IsWayland && NiriIpc.IsAvailable && NiriFocusedWorkspaceIsEmpty());

    public static bool IsInstalled => FindExecutable("slurp") != null;

    public static bool ShouldUse => Decision.UseSlurp;

    private static bool NiriFocusedWorkspaceIsEmpty()
    {
        try
        {
            using JsonDocument workspaces = JsonDocument.Parse(LinuxCaptureProcess.Text("niri", "msg", "--json", "workspaces"));
            using JsonDocument windows = JsonDocument.Parse(LinuxCaptureProcess.Text("niri", "msg", "--json", "windows"));

            foreach (JsonElement ws in workspaces.RootElement.EnumerateArray())
            {
                if (ws.TryGetProperty("is_focused", out var focused) && focused.ValueKind == JsonValueKind.True)
                {
                    long id = ws.GetProperty("id").GetInt64();
                    return !windows.RootElement.EnumerateArray().Any(w => w.TryGetProperty("workspace_id", out var wid) && wid.ValueKind == JsonValueKind.Number && wid.GetInt64() == id);
                }
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    internal static string? FindExecutable(string name) =>
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
