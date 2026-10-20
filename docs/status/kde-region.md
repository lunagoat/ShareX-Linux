# KDE multi-monitor region capture

* Monitor geometry / portal cropping: fixture verified. `tests/ShareX.Compositor.Tests` covers two unequal outputs, fractional scale, primary on the right, negative origins, and fake portal rasters at 1x/2x including selections across the boundary. No compositor or windows are opened.
* KScreen rotated sizes: corrected double rotation; the installed KScreen `output.h` documents `size()` as already transformed. Plasma 6 `priority == 1` identifies the primary output. Explicit logical sizes override scale-derived sizes.
* Real KDE hardware, portal raster layout on mixed-DPI monitors and KWin overlay placement: untested.

* X11 overlay sizing: desktop bounds are physical pixels, so divide by Avalonia RenderScaling; Wayland bounds remain logical. Hardware verification pending.
* Region selection policy: fixture verified. KDE ignores saved ActiveMonitorMode for region capture. Wayland defaults to installed slurp; X11 and explicit overlay use full-desktop KWin placement. Doctor prints the same policy decision.
* KWin placement and cursor: fake verified, not hardware verified. Temporary D-Bus scripts scope changes to the unique capture caption, retry and measure actual geometry, and reject persistent single-monitor placement. Wayland cursor comes from workspace.cursorPos, with full-desktop capture on scripting failure. No stale XQueryPointer is used on KDE Wayland.
* D-Bus integration: verified on a private dbus-run-session with fake org.kde.KWin; loadScript/run/Result/unloadScript and temporary file cleanup, including denial paths. No windows opened.
* Validation: scripts/linux/build.sh; dotnet run --project tests/ShareX.Compositor.Tests; dbus-run-session -- env SHAREX_TEST_PRIVATE_BUS=1 DISPLAY= WAYLAND_DISPLAY= dotnet run --project tests/ShareX.Compositor.Tests --no-build -- --fake-kwin.
* Git metadata in this environment is read-only. Step commits are preserved in /tmp/sharex-kde-fix-git (an isolated metadata copy), with a bundle for import; source changes are present in the requested workspace.
