// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Drawing;
using System.Text.Json;
using ShareX.AvaloniaUI.Linux;

internal static class KdePlacementChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(KdeCapture.Detect("Plasma:KDE", null, false, false), "KDE detection: desktop token");
        check(KdeCapture.Detect(null, "true", false, false), "KDE detection: full session");
        check(KdeCapture.Detect(null, null, true, false), "KDE detection: kscreen-doctor fallback");
        check(!KdeCapture.Detect("GNOME", null, true, false) && !KdeCapture.Detect(null, null, true, true),
            "KDE detection: installed kscreen-doctor does not override another compositor");
        check(!KdeCapture.UseActiveMonitor(true, true, false) && !KdeCapture.UseActiveMonitor(false, true, false),
            "KDE regression: region captures every output even when ActiveMonitorMode was saved as true");
        check(KdeCapture.UseActiveMonitor(false, false, true) && KdeCapture.UseActiveMonitor(true, false, false),
            "other compositors retain active-monitor behavior");
        var native = NativeRegionSelector.Decide(true, true, true, null, false);
        check(native.UseSlurp && native.Reason.Contains("KDE"), "KDE Wayland: default slurp and doctor explanation agree");
        check(!NativeRegionSelector.Decide(true, true, true, "overlay", false).UseSlurp, "KDE Wayland: explicit overlay uses verified placement");
        check(!NativeRegionSelector.Decide(true, true, false, null, false).UseSlurp &&
            NativeRegionSelector.Decide(true, true, false, null, false).Reason.Contains("missing"), "KDE Wayland: missing slurp explains required placement check");
        check(!NativeRegionSelector.Decide(false, true, true, null, false).UseSlurp, "KDE X11: use full-desktop overlay");
        check(NativeRegionSelector.Decide(true, false, true, "slurp", false).UseSlurp &&
            NativeRegionSelector.Decide(true, false, true, null, true).UseSlurp &&
            !NativeRegionSelector.Decide(true, false, true, null, false).UseSlurp, "slurp overrides and niri default retained");

        Rectangle desktop = new(-1920, -120, 4480, 1560);
        string expected = "{\"x\":-1920,\"y\":-120,\"width\":4480,\"height\":1560}";
        string title = "ShareX capture \"test\" [unique]";
        List<string> calls = [];
        bool placed = await KdeCapture.PlaceAsync(title, desktop, script =>
        {
            calls.Add(script);
            // KWin initially puts the overlay on the RIGHT output, while the frozen desktop starts on the LEFT.
            return Task.FromResult(calls.Count < 3 ? "{\"x\":0,\"y\":0,\"width\":2560,\"height\":1440}" : expected);
        });
        check(placed && calls.Count == 4, "KWin fake: retry until the window covers BOTH monitors, not just the pointer's output");
        check(calls[0].Contains(JsonSerializer.Serialize(title)) && calls[0].Contains("x: -1920") && calls[0].Contains("width: 4480") &&
            calls[0].Contains("keepAbove = true"), "KWin fake: exact unique title is escaped; logical desktop rectangle and stacking sent");
        check(!calls[1].Contains("frameGeometry ="), "KWin fake: placement measured separately after configure processing");
        int failures = 0;
        check(!await KdeCapture.PlaceAsync(title, desktop, script => { failures++; return Task.FromResult("null"); }) && failures == 6,
            "KWin fake: unmapped/missing overlay fails after bounded retries");
        check(!await KdeCapture.PlaceAsync(title, desktop, script => Task.FromResult("{\"x\":0,\"y\":0,\"width\":2560,\"height\":1440}")),
            "KWin fake: a persistent single-monitor overlay is rejected");
        check(!await KdeCapture.PlaceAsync(title, desktop, script => throw new InvalidOperationException("scripting denied")),
            "KWin fake: denied scripting triggers failure rather than accepting an unplaced overlay");
    }
}
