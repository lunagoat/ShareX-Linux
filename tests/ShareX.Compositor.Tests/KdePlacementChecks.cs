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
        string expected = State(desktop);
        string title = "ShareX capture \"test\" [unique]";
        List<string> calls = [];
        bool placed = await KdeCapture.PlaceAsync(title, desktop, script =>
        {
            calls.Add(script);
            // KWin initially puts the overlay on the RIGHT output, while the frozen desktop starts on the LEFT.
            return Task.FromResult(calls.Count < 4 ? "{\"x\":0,\"y\":0,\"width\":2560,\"height\":1440}" : expected);
        });
        check(placed && calls.Count == 6, "KWin fake: retry until the window covers BOTH monitors, not just the pointer's output");
        check(calls[0].Contains(JsonSerializer.Serialize(title)) && calls[1].Contains("x: -1920") && calls[1].Contains("width: 4480") &&
            calls[0].Contains("keepAbove = true"), "KWin fake: exact unique title is escaped; logical desktop rectangle and stacking sent");
        check(!calls[2].Contains("frameGeometry =") && calls[2].Contains("w.clientGeometry"), "KWin fake: placement measured separately after configure processing");
        check(new[] { "noBorder = true", "keepBelow = false", "skipTaskbar = true", "skipPager = true", "skipSwitcher = true",
            "fullScreen = false", "setMaximize(false, false)" }.All(calls[0].Contains) && !calls[0].Contains("frameGeometry ="),
            "KWin: clear fullscreen/maximize, borders and below state before a separate geometry configure; hide transient overlay from switchers");
        Rectangle full = new(0, 0, 4480, 1440);
        Rectangle work = new(0, 44, 4480, 1396);
        int panelCalls = 0;
        check(await KdeCapture.PlaceAsync(title, full, script => Task.FromResult(++panelCalls <= 3 ? State(work) : State(full))),
            "KWin: retry work-area-clamped y=44 placement until client covers the top panel at y=0");
        check(!await KdeCapture.PlaceAsync(title, full, script => Task.FromResult(State(work, full))),
            "KWin: full outer frame with client starting below top panel is rejected");
        check(!await KdeCapture.PlaceAsync(title, full, script => Task.FromResult(State(full, full, false))),
            "KWin: refused border removal is rejected even with matching geometry");
        check(!await KdeCapture.PlaceAsync(title, full, script => Task.FromResult(State(full, full, true, false))),
            "KWin: refused keepAbove is rejected even with matching geometry");
        check(calls[1].Contains("workspace.raiseWindow(w)") && calls[2].Contains("workspace.stackingOrder") &&
            calls[2].Contains("p.dock") && calls[2].Contains("!p.hidden"),
            "KWin: explicitly raise above same-layer Plasma panels and verify overlapping visible docks in stacking order");
        check(!await KdeCapture.PlaceAsync(title, full, script => Task.FromResult(State(full, full, true, true, false))),
            "KWin: panel remaining above correctly placed keepAbove overlay is rejected");
        int failures = 0;
        check(!await KdeCapture.PlaceAsync(title, desktop, script => { failures++; return Task.FromResult("null"); }) && failures == 9,
            "KWin fake: unmapped/missing overlay fails after bounded retries");
        check(!await KdeCapture.PlaceAsync(title, desktop, script => Task.FromResult("{\"x\":0,\"y\":0,\"width\":2560,\"height\":1440}")),
            "KWin fake: a persistent single-monitor overlay is rejected");
        check(!await KdeCapture.PlaceAsync(title, desktop, script => throw new InvalidOperationException("scripting denied")),
            "KWin fake: denied scripting triggers failure rather than accepting an unplaced overlay");
    }
    private static string State(Rectangle client, Rectangle? frame = null, bool noBorder = true, bool keepAbove = true, bool abovePanels = true)
    {
        Rectangle f = frame ?? client;
        return JsonSerializer.Serialize(new { x = client.X, y = client.Y, width = client.Width, height = client.Height,
            frame = new { x = f.X, y = f.Y, width = f.Width, height = f.Height }, noBorder, keepAbove, abovePanels });
    }
}
