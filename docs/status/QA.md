# ShareX Linux port — QA matrix (lead, 2026-10-19)

Environment: CachyOS, Hyprland (Wayland) with xdg-desktop-portal-hyprland, PipeWire, NVIDIA (NVENC). Three monitors:
DP-3 3840x2160 @1.5, DP-2 2560x1440 @1, DP-1 1920x1080 @1 (logical layout 7040x1440). The app runs through XWayland
(Avalonia 12 has no native Wayland backend). "Verified" below means the lead ran it personally on this machine
(not only the worker that wrote the code).

| Area | Status | Verified how |
|---|---|---|
| Build on plain `net10.0` Linux, no Windows SDK pieces, no `System.Drawing.Common` package | works | `scripts/linux/build.sh` → 0 errors |
| Start / welcome screen / main window / tray | works | launched, screenshot; startup test suite 0 failures (~90 settings pages rendered headless) |
| Fullscreen capture, 3 monitors, mixed scale (CLI `-PrintScreen` to a running instance) | works | 10560x2160 image, all monitors at correct offsets, visually checked |
| Global hotkeys via GlobalShortcuts portal (Hyprland) | works | 5 default shortcuts appear in `hyprctl globalshortcuts`; `hl.dsp.global("sharex:2")` triggered capture → clipboard + file. Needs a `sharex.desktop` entry (installer / first-run registration) or the portal refuses the app id |
| Region overlay, Esc cancel, colour picker, scrolling capture, auto capture, window capture, last/custom region | works | `tests/ShareX.Linux.Capture.Tests -- --e2e` all PASS (re-run by lead) |
| Screen recording (MP4 region/fullscreen, GIF, pause/resume, audio, window, bar/countdown) | works | `tests/ShareX.Recording.Smoke`; lead re-ran mp4 and ffprobed: 640x360 H.264, 61 frames, 4.07 s, NVENC |
| Tools (all 30 tools, 35 windows, libmpv video player, OCR/tesseract, ONNX background remover CPU) | works | `tests/ShareX.Linux.Tools.Tests` 0 failures (re-run by lead) |
| Uploads: custom uploader, clipboard image/text, URL shortener, watch folders, CLI upload, actions with spaces in path, FTP, history window | works | `tests/e-uploaders-qa/verify_pipeline.py` all PASS (re-run by lead) |
| SFTP / WebDAV / S3 / OAuth loopback | works (worker-verified) | worker's local fake servers; not re-run by lead |
| Clipboard formats (png, text, uri-list, gnome-copied-files) | works (worker-verified) | `tests/ShareX.Input.Linux.Tests` |
| Settings round trip, single instance, CLI forwarding | works | startup tests; lead ran CLI forwarding |
| Packaging (PKGBUILD, user installer, publish profile, desktop/metainfo/mime) | works (worker-verified) | `packaging/`, `scripts/linux/install-user.sh`, `scripts/linux/publish.sh`; Flatpak is a draft |
| Image effects (all 51) | works | `tests/ShareX.Effects.Tests`: every effect runs on Linux, none blank/throwing (lead-written, lead-run) |
| Image editor | works | `tests/ShareX.Editor.Tests` (13 tools, undo/redo, rotate/flip/resize/crop/canvas, png/jpg/webp export: 52 checks) + live editor window screenshot with full toolbar; fontconfig resolves sans-serif |
| Wallpaper reading (editor backgrounds) on Hyprland | works | awww/swww/hyprpaper provider added; `awww query` format checked on this machine |
| Encrypted settings, print (CUPS lp), shell integration, IPC, startup/persistence | works | `python3 tests/ShareX.Platform.Tests/run.py` all assertions passed (lead-run) |
| Desktop notifications with actions + thumbnail | works | lead observed `org.freedesktop.Notifications.Notify` with Open / Copy image / Edit image actions via dbus-monitor |
| Portable mode (`-portable`) | works | data goes to `ShareX-Data` beside the executable (was colliding with the `ShareX` binary); nothing written to XDG dirs |
| Installed build (`scripts/linux/install-user.sh` → `~/.local`) | works | self-contained build launched with the real profile: 5 portal shortcuts registered as `sharex:N`, hotkey capture saved + copied |
| sway / GNOME / KDE / pure X11 sessions | unverified | code paths exist (sway IPC, X11 EWMH, KWin script pending), no such session on this machine |
| KDE/GNOME window enumeration | missing | compositors expose no standard API |

## Known gaps / limitations
- XWayland: Wayland compositors cannot give an X client real global coordinates for other apps' windows; capture uses compositor IPC (`hyprctl`) instead. Overlay behaviour on other compositors is untested.
- Scrolling capture needs synthetic input (X11 XTest/uinput); works on this session through XWayland, other sessions untested.
- The portal needs the app id (`sharex`, `io.github.ShareX.ShareX`, …) to have a `.desktop` file in an XDG applications dir.
- Auto-update is disabled (package manager owns updates). Windows-only features (shell extension, taskbar progress, DWM, HDR correction, Steam/Store) are hidden or removed.
- Installed for the user in `~/.local` (launcher `~/.local/bin/sharex`, app `~/.local/lib/sharex`); autostart is not enabled. Compositor key binds for the hotkeys are the user's choice (see docs/CLI.md for Hyprland lines); the user's own Print/Satty binds were left alone.
