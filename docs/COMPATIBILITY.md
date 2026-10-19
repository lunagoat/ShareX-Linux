# Desktop compatibility

ShareX's UI toolkit (Avalonia) talks to an **X server**, so on a Wayland session ShareX needs **XWayland** (GNOME, KDE, Hyprland and sway
start it on demand) or **xwayland-satellite** (niri). Everything else — screenshots, window lists, hotkeys, recording — goes through the
desktop's own interfaces. "Tested" means a person ran it on that desktop; "implemented" means the code path exists and its parsing has
unit tests, but nobody has run it on the real desktop yet. **Only Hyprland has been tested.** Reports for the others are very welcome.

| | Hyprland | sway | niri | KDE Plasma (Wayland) | GNOME (Wayland) | X11 sessions (i3, Xfce, KDE/GNOME on X11, …) |
|---|---|---|---|---|---|---|
| Status | **tested** | implemented | implemented | implemented | implemented | implemented |
| Needs | XWayland | XWayland | **xwayland-satellite** | XWayland | XWayland | — |
| Screenshot | `grim` | `grim` | `grim` | desktop portal | desktop portal | built-in (X11) |
| Monitor layout / scale | `hyprctl` | `swaymsg` | `niri msg` | `kscreen-doctor` | XWayland (RandR) | RandR |
| Window capture (window list) | `hyprctl` | `swaymsg` | `niri msg` (experimental) | — (XWayland windows only) | — (XWayland windows only) | EWMH |
| Region / fullscreen / monitor capture | yes | yes | yes | yes | yes | yes |
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

## Known limits

* Wayland does not let an application list other applications' windows. Window capture works where the compositor offers an API
  (Hyprland, sway, niri); on GNOME and KDE Wayland use region or full-screen capture.
* Scrolling capture needs to send scroll events: works through XWayland and X11; elsewhere it needs a remote-desktop portal approval.
* Hyprland keeps portal shortcut entries listed until it restarts (harmless).
* Prebuilt releases are x86-64 only; use `install.sh --from-source` on arm64.
