# ShareX for Linux

An unofficial, native Linux port of [ShareX](https://github.com/ShareX/ShareX) — screen capture, recording,
annotation, OCR, uploaders (custom `.sxcu` included), history, workflows and the rest of the toolbox —
running on Wayland and X11 through the desktop's own facilities (portals, `grim`, PipeWire, ffmpeg,
compositor IPC) instead of Win32.

It is built from upstream ShareX's current Avalonia/.NET 10 code base; the Linux work is the platform layer
underneath it (capture, hotkeys, recording, clipboard, notifications, packaging).

## Install

```bash
curl -fsSL https://raw.githubusercontent.com/lunagoat/ShareX-Linux/main/install.sh | bash
```

Per-user (`~/.local`), no sudo, no .NET needed (the release is self-contained), x86-64. The installer verifies a
SHA-256 checksum and prints which optional helpers (`grim`, `slurp`, `wl-clipboard`, `ffmpeg`, `tesseract`, …) are
missing for your distribution. Then run `sharex`.

```bash
# other ways
curl -fsSL https://raw.githubusercontent.com/lunagoat/ShareX-Linux/main/install.sh | bash -s -- --from-source   # build here (also arm64)
curl -fsSL https://raw.githubusercontent.com/lunagoat/ShareX-Linux/main/install.sh | bash -s -- --version v21.0.0-linux.1
curl -fsSL https://raw.githubusercontent.com/lunagoat/ShareX-Linux/main/install.sh | bash -s -- --uninstall
```

Arch / CachyOS users can also build a package: `cd packaging/arch && makepkg -si`.

## Hotkeys

**Hyprland, sway and other wlroots compositors never bind keys on their own** — an app can only ask. So after installing,
add key binds that run ShareX. ShareX prints them for you, based on your own hotkey settings:

```bash
sharex --print-binds hyprland         # hyprland.lua  (Lua config, Hyprland 0.55+)
sharex --print-binds hyprland-conf    # hyprland.conf
sharex --print-binds sway
```

```lua
-- example output: paste into hyprland.lua, drop the keys you already use, reload
hl.bind("CTRL + Print", hl.dsp.exec_cmd("~/.local/bin/sharex -RectangleRegion"))  -- Capture region
hl.bind("Print", hl.dsp.exec_cmd("~/.local/bin/sharex -PrintScreen"))             -- Capture entire screen
```

The same text (with a copy button) is in ShareX → Hotkey settings → *Linux global shortcuts*. Command binds work on every
compositor and start ShareX if it isn't running yet.

Optional: ShareX also registers its hotkeys with the desktop **GlobalShortcuts portal** (KDE and GNOME show them in their
shortcut settings; on Hyprland they are listed by `hyprctl globalshortcuts` as `sharex:RectangleRegion`, `sharex:PrintScreen`, …).
Hyprland ignores the key an app asks for, so there you bind them yourself with `sharex --print-binds hyprland --global`
(`hl.dsp.global("sharex:RectangleRegion")`). Use one method per key, not both. These binds only do something while ShareX is
running; Hyprland keeps old entries listed until it restarts, which is harmless.

The full command reference and more compositor examples are in [docs/CLI.md](docs/CLI.md).

## What works

Verified on Hyprland (Wayland, 3 monitors with mixed scaling, PipeWire, NVIDIA) — see [docs/status/QA.md](docs/status/QA.md)
for the complete matrix and how each item was checked:

* capture: fullscreen, monitor, active window, any window, region overlay with annotation tools, last region,
  scrolling capture, auto capture, screen colour picker
* screen recording: MP4/WebM/GIF, region/window/screen, pause/resume, system + microphone audio, camera overlay,
  hardware encoders (NVENC/VAAPI) with software fallback, custom FFmpeg arguments
* image editor, 51 image effects, OCR (tesseract), QR codes, ruler, pin to screen, video converter/thumbnailer/player, and the other tools
* uploads: custom uploaders, clipboard/text/URL uploads, URL shorteners, watch folders, FTP/SFTP/WebDAV/S3, after-capture and after-upload tasks, history
* desktop integration: tray icon, notifications with actions, autostart, `.sxcu`/`.sxie` file associations, CUPS printing, encrypted settings

Other sessions (sway, GNOME, KDE, plain X11) have code paths but have not been tested on real machines yet —
reports welcome. Wayland has no way to see other applications' windows without compositor help, so window
capture needs Hyprland, sway or X11. [README.linux.md](README.linux.md) has the per-session support matrix and troubleshooting.

## Build from source

```bash
git clone https://github.com/lunagoat/ShareX-Linux && cd ShareX-Linux
source scripts/linux/env.sh        # uses ~/.dotnet if present, otherwise your `dotnet` (SDK 10 required)
scripts/linux/build.sh             # build
scripts/linux/install-user.sh      # publish a self-contained build and install it into ~/.local
```

Tests live in `tests/` (headless effects/editor tests run anywhere; capture/recording/tools tests need a live desktop session).
The porting notes are in [docs/LINUX_PORT.md](docs/LINUX_PORT.md).

## License and credits

ShareX is © the ShareX Team and licensed under the GNU GPL v3 (see [LICENSE.txt](LICENSE.txt)); this port keeps that
license. This project is not affiliated with or endorsed by the ShareX team — please report problems with the Linux port
here, not to the upstream project.
