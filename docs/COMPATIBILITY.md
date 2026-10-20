# Desktop compatibility

ShareX's UI toolkit (Avalonia) talks to an **X server**, so on a Wayland session ShareX needs **XWayland** (GNOME, KDE, Hyprland and sway
start it on demand) or **xwayland-satellite** (niri). Everything else — screenshots, window lists, hotkeys, recording — goes through the
desktop's own interfaces. "Tested" means a person ran it on that desktop; "implemented" means the code path exists and its parsing has
unit tests, but nobody has run it on the real desktop yet. **Hyprland is tested on real hardware; niri and sway were tested inside nested sessions** (a real compositor in a window), which exercises the IPC and window placement but not multi-monitor layouts or real bars. Reports for the others are very welcome.

| | Hyprland | sway | niri | KDE Plasma (Wayland) | GNOME (Wayland) | X11 sessions (i3, Xfce, KDE/GNOME on X11, …) |
|---|---|---|---|---|---|---|
| Status | **tested** | tested nested | tested nested | implemented | implemented | implemented |
| Needs | XWayland | XWayland | **xwayland-satellite** | XWayland | XWayland | — |
| Screenshot | `grim` | `grim` | `grim` | desktop portal | desktop portal | built-in (X11) |
| Monitor layout / scale | `hyprctl` | `swaymsg` | `niri msg` | `kscreen-doctor` | XWayland (RandR) | RandR |
| Window capture (window list) | `hyprctl` | `swaymsg` | `niri msg` (floating windows only) | — (XWayland windows only) | — (XWayland windows only) | EWMH |
| Region selection | ShareX overlay (annotations, magnifier) | ShareX overlay, placed via `swaymsg` (tested nested) | ShareX overlay, fullscreen on the focused monitor via `niri msg` (tested nested) | ShareX overlay, placed over the whole desktop via KWin scripting (hardware untested) | ShareX overlay (unverified) | ShareX overlay |
| Fullscreen / monitor capture | yes | yes | yes | yes | yes | yes |
| Hotkeys | portal binds **or** `sharex --install-binds` | `sharex --install-binds` | `sharex --print-binds niri` (add to `config.kdl`) | portal: shortcuts show up in System Settings | `sharex --print-binds gnome \| sh`, or the portal on GNOME 48+ | built-in key grabs |
| Screen recording | `grim` frames / `gpu-screen-recorder` | same | portal via `gpu-screen-recorder` | portal via `gpu-screen-recorder` | portal via `gpu-screen-recorder` | ffmpeg `x11grab` |
| Tray icon | yes | needs a bar with a tray | needs a bar with a tray | yes | needs the *AppIndicator* extension | depends on the panel |
| Notifications | yes | needs a notification daemon | needs a notification daemon | yes | yes | needs a notification daemon |

## What each desktop needs

* **All Wayland desktops:** `wl-clipboard`, `ffmpeg`, `tesseract` (optional OCR), `xdg-desktop-portal` + your desktop's portal backend
  (`-hyprland`, `-wlr`, `-kde`, `-gnome`). `grim` on wlroots-style compositors (Hyprland, sway, niri).
* **niri:** install **xwayland-satellite**. niri 25.08+ starts it automatically; on older versions add `spawn-at-startup "xwayland-satellite"`.
  Without it ShareX prints a clear message and exits instead of crashing. Key binds go in the `binds { }` block of `~/.config/niri/config.kdl`.
* **KDE Plasma:** Spectacle owns `Print`; assign other keys or free it in *System Settings → Shortcuts*. Install `xdg-desktop-portal-kde`.
  Screen capture goes through the portal, so Plasma asks for permission the first time; `grim` is not required.
* **GNOME:** the tray icon needs the *AppIndicator and KStatusNotifierItem* extension. Screen capture goes through the portal (permission prompt first time).
  Recording asks which screen to share each time (the portal's picker).
* **X11 sessions:** nothing special; `xclip`/`xdotool` help.

## Region selection: the native overlay

On every compositor that offers **wlr-layer-shell** (Hyprland, sway, niri, KDE Plasma) region capture uses ShareX's **native overlay**: a helper
process (`sharex --overlay-helper`) draws the frozen screenshot, dim, selection, magnifier and ShareX's own toolbar and annotation tools as
layer-shell surfaces, one per monitor, so one selection can span every monitor (mixed scales included) on any desktop. It shows in `sharex --doctor`
as "region selection: native layer-shell overlay".

* Esc cancels at any time, also if the overlay's drawing thread is stuck. After 45 seconds without input the overlay closes by itself.
* If the helper cannot start, crashes or stops answering, ShareX logs why and falls back to the window overlay described below.
* `SHAREX_OVERLAY=window` forces the window overlay, `SHAREX_OVERLAY=native` forces the native one. `SHAREX_OVERLAY_TIMING=1` writes a stage-by-stage timeline of the overlay start to the ShareX log (screenshot, helper startup, first frame).
* Speed: the helper starts while the screenshot is still being taken, the screenshot is read as raw PPM (not PNG), and the build is ReadyToRun-compiled and not compressed. On a three-monitor 22-megapixel desktop, Print to visible overlay went from about 2.4 s to about 0.65 s.
* **GNOME** has no layer-shell, so it keeps the window overlay for now.
* Like the Windows version, the selector does not draw over fullscreen games; the fullscreen and active-window hotkeys work there.
* Tested: nested Hyprland (two outputs, 1.0x and 1.6x, automated end-to-end test) and real Hyprland desktops (3 monitors with a 1.5x one; a laptop
  at 1.25x). Not yet tested on real KDE, niri or sway.

## The window overlay (GNOME, and the fallback)

### Window overlay placement on niri and sway

ShareX's region capture, colour picker and recording UIs are ordinary X11 windows laid over the screen, so each compositor has to be
told where to put them. Hyprland, sway and niri are all handled through their IPC (tested in nested niri 26.04 and sway 1.12 sessions):

* **Hyprland:** positioned over every monitor.
* **sway:** the overlay is floated and placed over the whole desktop (floating windows may span outputs).
* **niri:** a niri window cannot span monitors, so the overlay is made fullscreen on the **focused monitor** and region capture works within it.
  A selection therefore cannot cross monitors. If the keybind is pressed on an **empty workspace**, niri adds a new empty workspace below it
  for the overlay window (it stays until you leave).
* Panels such as waybar are drawn above ordinary windows, so ShareX's toolbar is moved out from under them: exactly on Hyprland and sway,
  and by a default of 48 px on niri (niri does not report a bar's size; set `SHAREX_TOOLBAR_INSET=<pixels>` to change it, `0` to disable).

Window capture on niri only knows floating windows' positions (niri does not report where tiled windows are).
The recording-region window and scrolling capture are not adapted to niri and sway yet.

## KDE multi-monitor region capture

KDE region capture always uses the **whole desktop**, including when an older task has `ActiveMonitorMode` enabled. An overlay freezes
that whole rectangle once, so each monitor's image stays at its desktop position.

ShareX loads a temporary KWin script over D-Bus to move/resize its uniquely titled capture window to the full logical desktop rectangle,
including panel reservations. It requests no decorations, clears maximize/fullscreen, skips taskbar/pager/switcher, and explicitly raises the
keep-above window (Plasma panels can share its stacking layer). It checks both frame and painted client geometry, border/keep-above state and
stacking above visible overlapping docks after KWin processes the configure, retrying if needed. If the placement still cannot be verified
(for example KWin scripting is not permitted), ShareX logs that and carries on with the overlay window as the toolkit placed it: the capture
still covers every monitor, but the window may not line up. KDE X11 uses the same placement check. The script is unloaded and its private
temporary file removed after each request.

KDE Wayland global pointer queries use `workspace.cursorPos` through the same temporary scripting mechanism, never `XQueryPointer`.
If unavailable, active-monitor screenshot requests use the full desktop, and overlay toolbar initialization uses the primary monitor's centre
as an anchor. X11 sessions continue using their real X11 pointer. `sharex --doctor` prints the selected region selector and its reason; it does
not open a test overlay or certify that KWin will allow placement. It names the selector/placement path and, when scripting is available,
reports full output vs reserved workarea and panel geometry through a read-only temporary KWin script; qdbus support information provides
a screen-geometry fallback. Unavailable workareas are reported explicitly. See [top-strip follow-up diagnostics](status/kde-region.md#follow-up-top-strip-not-painted).

**Verified without a desktop:** `tests/ShareX.Compositor.Tests` covers kscreen-doctor JSON for two unequal outputs, fractional scale, primary
on the right, negative origins, transformed rotation and explicit logical sizes. Fake portal rasters test monitor crops and selections crossing
monitor boundaries at 1x/2x. Placement fakes reproduce a window on the right output while the frozen rectangle starts on the left, and verify
retries, rejection of single-monitor geometry, escaped window titles, and scripting denial. A private D-Bus fake tests script loading, running,
callbacks, cursor queries, unloading and file cleanup. A synthetic 44px top-panel reservation verifies full output bounds and top-row portal
pixels at 1x/2x; placement fakes reject frame-only coverage, workarea clamping, refused borders/stacking and panels above the overlay.
No GUI or live compositor is used.

**Still needs a real KDE user:** KWin spanning and stacking above panels on X11/XWayland, and portal PNG
layout on mixed-DPI / fractional-scale hardware. The crop tests assume a portal PNG is a uniformly scaled raster of the logical desktop;
the portal provides no per-output geometry metadata. Fixtures establish the coordinate calculations under that assumption, not KDE's actual
PNG composition. The original report is consistent with a stale XWayland cursor plus missing placement, but that causal chain is inferred.

## Known limits

* Wayland does not let an application list other applications' windows. Window capture works where the compositor offers an API
  (Hyprland, sway, niri); on GNOME and KDE Wayland use region or full-screen capture.
* Scrolling capture needs to send scroll events: works through XWayland and X11; elsewhere it needs a remote-desktop portal approval.
* Hyprland keeps portal shortcut entries listed until it restarts (harmless).
* Prebuilt releases are x86-64 only; use `install.sh --from-source` on arm64.
