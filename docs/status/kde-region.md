# KDE multi-monitor region capture

* Monitor geometry / portal cropping: fixture verified. `tests/ShareX.Compositor.Tests` covers two unequal outputs, fractional scale, primary on the right, negative origins, and fake portal rasters at 1x/2x including selections across the boundary. No compositor or windows are opened.
* KScreen rotated sizes: corrected double rotation; the installed KScreen `output.h` documents `size()` as already transformed. Plasma 6 `priority == 1` identifies the primary output. Explicit logical sizes override scale-derived sizes.
* Real KDE hardware, portal raster layout on mixed-DPI monitors and KWin overlay placement: untested.

* X11 overlay sizing: desktop bounds are physical pixels, so divide by Avalonia RenderScaling; Wayland bounds remain logical. Hardware verification pending.
* Region selection policy: fixture verified. KDE ignores saved ActiveMonitorMode for region capture. Every KDE session (Wayland and X11) uses the ShareX overlay with full-desktop KWin placement; if placement cannot be verified, ShareX logs it and continues with the plain overlay window. Doctor prints the same policy decision.
* KWin placement and cursor: fake verified, not hardware verified. Temporary D-Bus scripts scope changes to the unique capture caption, retry and measure actual geometry, and reject persistent single-monitor placement. Wayland cursor comes from workspace.cursorPos, with full-desktop capture on scripting failure. No stale XQueryPointer is used on KDE Wayland.
* D-Bus integration: verified on a private dbus-run-session with fake org.kde.KWin; loadScript/run/Result/unloadScript and temporary file cleanup, including denial paths. No windows opened.
* Validation: scripts/linux/build.sh; dotnet run --project tests/ShareX.Compositor.Tests; dbus-run-session -- env SHAREX_TEST_PRIVATE_BUS=1 DISPLAY= WAYLAND_DISPLAY= dotnet run --project tests/ShareX.Compositor.Tests --no-build -- --fake-kwin.
* Follow-up top-strip fix: working tree only. `.git/index.lock` creation is rejected as read-only, so no step commits can be made in this clone.

## Follow-up: top strip not painted

The user's selector and exact symptom are still unknown. No hardware reproduction or GUI test was attempted.

Certain code findings:

* v14 already requested `noBorder=true`, `keepAbove=true`, `fullScreen=false`, and cleared maximize; the Avalonia window already has `WindowDecorations="None"`. Avalonia 12.1.4 translates this to `_MOTIF_WM_HINTS` with decorations zero. A missing decoration request is therefore not established.
* v14 verified only `frameGeometry`. It could accept an outer frame covering the desktop with a smaller, downward-offset painted client. Placement now waits for decoration/maximize changes, sets the full desktop rectangle separately, then checks BOTH frame and read-only `clientGeometry`, `noBorder`, and `keepAbove` after configure processing. Refusal fails the capture rather than silently accepting a missing strip.
* KWin's old layers.cpp comment claims keep-above is above docks, but Plasma 6.5 `Window::belongsToLayer()` places docks and keep-above windows in `AboveLayer`. Thus `keepAbove` alone does not guarantee coverage. Placement now explicitly calls `workspace.raiseWindow(w)` and verifies that no visible intersecting dock follows the overlay in `workspace.stackingOrder`. `layer` is read-only; it is not assigned. Fullscreen remains false to avoid a compositor-selected single-output rectangle. Skip-taskbar/pager/switcher are set on the uniquely titled disposable overlay only.
* No panels, persistent rules, other windows or workareas are modified. Closing the transient overlay removes its window state (including on placement failure); every request still unloads its script and deletes its private file. No restoration of panel state is needed.
* KScreen output geometry and portal crops use full bounds, including the top strip, independent of panel reservations. `LayerShellInsets.TopInset` only moves toolbar controls and defaults to zero on KDE; it does not crop or translate the frozen image. A synthetic fixture pairs KScreen output data with KWin workarea/panel metadata for a 44px top reservation (this is not a claim that kscreen-doctor emits workareas). Full bounds and portal pixels at rows 0..43 are verified at 1x and 2x.

The stacking omission and frame-only verification are certain defects; which, if either, caused this report remains inferred. Normal borderless placement across the full output union plus a verified raise is preferred over maximizing to a workarea or fullscreen on one output. Persistent KWin constraints are rejected rather than assumed away.

Doctor now identifies the selector/placement path explicitly. On KDE it obtains full output vs per-output maximize/workarea geometry and dock rectangles through a temporary **read-only KWin D-Bus script**, without opening or activating windows. kscreen-doctor supplies full output geometry, not panel reservations. If scripting is unavailable, a bounded `qdbus6`/`qdbus supportInformation` query supplies screen geometry when possible and the report says workareas/panels are unavailable. It never invokes the interactive `queryWindowInfo` picker.

Sources: [KWin scripting API](https://develop.kde.org/docs/plasma/kwin/api/), [Plasma 6.5 layer selection](https://github.com/KDE/kwin/blob/Plasma/6.5/src/window.cpp#L524), [decoration changes recheck workspace position](https://github.com/KDE/kwin/blob/Plasma/6.5/src/x11window.cpp#L886), [Avalonia 12.1.4 Motif hints](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.X11/X11Window.cs#L316).

If the KDE user still sees a gap, request these terminal outputs from the same session/launch environment as ShareX:

```sh
sharex --doctor > sharex-doctor.txt 2>&1
kscreen-doctor -j > kscreen.json 2>&1
qdbus6 org.kde.KWin /KWin org.kde.KWin.supportInformation > kwin-support.txt 2>&1
```

Use `qdbus` in place of `qdbus6` on Plasma 5. Send the files plus ShareX version, launch method, which monitor is affected, the strip height, panel visibility mode, and whether the strip shows a live panel, live wallpaper, black/transparent area, titlebar, or only a missing toolbar.

Follow-up validation (2026-10-19): `scripts/linux/build.sh` succeeded with zero errors; compositor fixtures/fakes and private-bus `--fake-kwin` checks passed. The headless, isolated `--doctor` smoke run printed the new KDE path and unavailable-workarea fallback without a KWin service; its exit status was 1 because the isolated configuration had no Hyprland config (inherited session environment). It was not a KDE hardware test. No capture window was opened and no processes were killed by name.
