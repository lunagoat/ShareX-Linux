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
| Region selection | ShareX overlay (annotations, magnifier) | ShareX overlay, placed via `swaymsg` (tested nested) | ShareX overlay, fullscreen on the focused monitor via `niri msg` (tested nested) | ShareX overlay (unverified) | ShareX overlay (unverified) | ShareX overlay |
| Fullscreen / monitor capture | yes | yes | yes | yes | yes | yes |
| Hotkeys | portal binds **or** `sharex --install-binds` | `sharex --install-binds` | `sharex --print-binds niri` (add to `config.kdl`) | portal: shortcuts show up in System Settings | `sharex --print-binds gnome \| sh`, or the portal on GNOME 48+ | built-in key grabs |
| Screen recording | `grim` frames / `gpu-screen-recorder` | same | portal via `gpu-screen-recorder` | portal via `gpu-screen-recorder` | portal via `gpu-screen-recorder` | ffmpeg `x11grab` |
| Tray icon | yes | needs a bar with a tray | needs a bar with a tray | yes | needs the *AppIndicator* extension | depends on the panel |
| Notifications | yes | needs a notification daemon | needs a notification daemon | yes | yes | needs a notification daemon |

## What each desktop needs

* **All Wayland desktops:** `wl-clipboard`, `ffmpeg`, `tesseract` (optional OCR), `xdg-desktop-portal` + your desktop's portal backend
  (`-hyprland`, `-wlr`, `-kde`, `-gnome`). `grim` + `slurp` on wlroots-style compositors (Hyprland, sway, niri).
* **niri:** install **xwayland-satellite**. niri 25.08+ starts it automatically; on older versions add `spawn-at-startup "xwayland-satellite"`.
  Without it ShareX prints a clear message and exits instead of crashing. Key binds go in the `binds { }` block of `~/.config/niri/config.kdl`.
* **KDE Plasma:** Spectacle owns `Print`; assign other keys or free it in *System Settings → Shortcuts*. Install `xdg-desktop-portal-kde`.
  Screen capture goes through the portal, so Plasma asks for permission the first time.
* **GNOME:** the tray icon needs the *AppIndicator and KStatusNotifierItem* extension. Screen capture goes through the portal (permission prompt first time).
  Recording asks which screen to share each time (the portal's picker).
* **X11 sessions:** nothing special; `xclip`/`xdotool` help.

## Region selection and ShareX's own overlay on niri and sway

ShareX's region capture, colour picker and recording UIs are ordinary X11 windows laid over the screen, so each compositor has to be
told where to put them. Hyprland, sway and niri are all handled through their IPC (tested in nested niri 26.04 and sway 1.12 sessions):

* **Hyprland:** positioned over every monitor.
* **sway:** the overlay is floated and placed over the whole desktop (floating windows may span outputs).
* **niri:** a niri window cannot span monitors, so the overlay is made fullscreen on the **focused monitor** and region capture works within it.
  For a selection that crosses monitors use `SHAREX_REGION_SELECTOR=slurp`. If the keybind is pressed on an **empty workspace**, ShareX uses
  `slurp` automatically: a window there would be the workspace's first window, and niri would add a new empty workspace below it
  (which stays until you leave) — `slurp` is a layer-shell surface, not a window, so it causes no such churn. Force the overlay anyway with
  `SHAREX_REGION_SELECTOR=overlay`.
* Panels such as waybar are drawn above ordinary windows, so ShareX's toolbar is moved out from under them: exactly on Hyprland and sway,
  and by a default of 48 px on niri (niri does not report a bar's size; set `SHAREX_TOOLBAR_INSET=<pixels>` to change it, `0` to disable).
* `SHAREX_REGION_SELECTOR=slurp` swaps the overlay for `slurp`, the compositor's own selector (drag a region or click a window); you keep
  ShareX's after-capture tasks but lose the overlay's annotation tools and magnifier.

Window capture on niri only knows floating windows' positions (niri does not report where tiled windows are).
The recording-region window and scrolling capture are not adapted to niri and sway yet.

## Known limits

* Wayland does not let an application list other applications' windows. Window capture works where the compositor offers an API
  (Hyprland, sway, niri); on GNOME and KDE Wayland use region or full-screen capture.
* Scrolling capture needs to send scroll events: works through XWayland and X11; elsewhere it needs a remote-desktop portal approval.
* Hyprland keeps portal shortcut entries listed until it restarts (harmless).
* Prebuilt releases are x86-64 only; use `install.sh --from-source` on arm64.
