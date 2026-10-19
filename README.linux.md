# ShareX for Linux

This is the Linux port of [ShareX](https://getsharex.com): the same application —
capture, recording, annotation, tools, uploaders, history, workflows — running natively
on Wayland and X11, using the desktop's own facilities (portals, `grim`, PipeWire,
`wl-clipboard`, `ffmpeg`, compositor IPC) instead of Win32 APIs.

* The porting plan and architecture live in [docs/LINUX_PORT.md](docs/LINUX_PORT.md).
* Per-package verification results (what works, what does not) are in [docs/status/](docs/status).
* The complete command line reference is in [docs/CLI.md](docs/CLI.md).

> **Status.** The port is a work in progress; the tables below are the *target*
> behaviour per session type. Cells marked ✅ were exercised in the port's test
> environment (Hyprland + XWayland), 🟢 are implemented by design with the listed
> mechanism, ⚠️ work with caveats and ❌ are not possible on that session type.
> `docs/status/QA.md` (maintained by the integration lead) records the end-to-end
> results per session type.

## Requirements

| | |
| --- | --- |
| Runtime | x86-64 (or aarch64) Linux, glibc, a Wayland or X11 session |
| Framework | none — the published build is self-contained (.NET is bundled) |
| Build | .NET 10 SDK (`scripts/linux/env.sh` expects a user-local SDK in `~/.dotnet`) |

Recommended packages by session:

| Session | Install |
| --- | --- |
| Hyprland | `grim slurp wl-clipboard ffmpeg gpu-screen-recorder tesseract xdg-desktop-portal xdg-desktop-portal-hyprland libnotify xdg-utils` |
| sway / wlroots | `grim slurp wl-clipboard ffmpeg tesseract xdg-desktop-portal xdg-desktop-portal-wlr libnotify xdg-utils` |
| GNOME | `ffmpeg tesseract xdg-desktop-portal xdg-desktop-portal-gnome libnotify xdg-utils` (+ the AppIndicator extension for the tray) |
| KDE Plasma | `ffmpeg tesseract xdg-desktop-portal xdg-desktop-portal-kde libnotify xdg-utils` |
| X11 | `ffmpeg tesseract xclip xdotool libnotify xdg-utils` |

Everything that shells out to an external tool checks for it and reports a clear message
when it is missing; nothing crashes because `grim` or `ffmpeg` is absent.

## Installing

### From a published build (per user, no root)

```bash
source scripts/linux/env.sh
scripts/linux/publish.sh                 # -> dist/linux-x64/ShareX (self-contained, single file)
scripts/linux/install-user.sh            # installs into ~/.local, idempotent
~/.local/bin/sharex -OpenMainWindow
```

`install-user.sh` writes only into `~/.local`:

```
~/.local/bin/sharex                                    launcher
~/.local/lib/sharex/ShareX                             application
~/.local/share/applications/sharex.desktop             menu entry
~/.local/share/metainfo/io.github.ShareX.ShareX.metainfo.xml
~/.local/share/mime/packages/sharex.xml                .sxcu / .sxie
~/.local/share/icons/hicolor/<size>/apps/sharex.png    icons
```

Remove it again with `scripts/linux/install-user.sh --uninstall`. It honors `$HOME`,
`$XDG_DATA_HOME` and `$XDG_BIN_HOME`, so it can be tested in a throw-away home directory.

### Arch Linux / CachyOS

```bash
cd packaging/arch
makepkg -f -i        # add --nodeps if you use the user-local SDK in ~/.dotnet
```

`packaging/arch/PKGBUILD` builds from the checkout with `dotnet publish` and installs to
`/usr`. Optional dependencies (ffmpeg, grim, slurp, wl-clipboard, portals, tesseract, …)
are listed there and in the package metadata.

### Flatpak (draft)

`packaging/flatpak/io.github.ShareX.ShareX.yml` is a draft manifest that has not been
build-tested yet (it still needs a pinned .NET SDK checksum and a release source):

```bash
flatpak-builder --user --install --force-clean build-dir packaging/flatpak/io.github.ShareX.ShareX.yml
flatpak run io.github.ShareX.ShareX
```

### Build and run from source

```bash
source scripts/linux/env.sh
scripts/linux/build.sh                                        # debug build
SX_SANDBOX=/tmp/sx-mytest timeout 60 scripts/linux/run.sh     # run with an isolated HOME
```

## First run

* On first start ShareX offers its start screen; the main window and the tray icon follow.
* ShareX keeps its state in a single **personal folder**. On a normal desktop that is
  `~/Documents/ShareX` (it uses the desktop's XDG *Documents* user directory, so it follows
  `xdg-user-dirs`):

  | What | Location (inside the personal folder) |
  | --- | --- |
  | Settings | `ApplicationConfig.json`, `UploadersConfig.json`, `HotkeysConfig.json` |
  | History | `History.db` |
  | Logs | `Logs/ShareX-Log-<yyyy-MM>.txt` |
  | Screenshots, image effects, backups | `Screenshots/`, `ImageEffects/`, `Backup/` |

  Note that this is **not** an XDG config/data layout and it is **not** redirected by
  overriding `$HOME` (`XDG_CONFIG_HOME`/`XDG_DATA_HOME` do not move it either) — it follows
  the desktop user's Documents directory. Because of that, the `SX_SANDBOX`-style
  throw-away homes used by the test scripts do not isolate ShareX's own files; see
  `docs/status/wp7-packaging.md` for details and a workaround. Change the folder in
  *Application settings → Paths*, or use `sharex -portable`, which keeps everything next to
  the executable.
  `-portable` expects a `ShareX/` folder next to the executable, so it only works when the
  executable itself is not named `ShareX` (on this port the packaged binary *is* named
  `ShareX`, so `-portable` fails with a *ShareX – Error* dialog and the personal folder is
  used instead; see `docs/status/wp7-packaging.md`).

  The single-file build also unpacks its native libraries on first start into
  `~/.net/ShareX/` (this one does honour `$HOME`; override with
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).

* Logs record the parsed command line ("Flags: …") and every action/error.
  `sharex -sandbox` starts a throw-away instance that does not read or write your settings
  or history, but it still appends its log lines to the personal folder's `Logs/`
  (a known limitation tracked in `docs/status/wp7-packaging.md`).
* ShareX is single-instance: a second `sharex -RectangleRegion` is handed over to the
  running instance through a per-user socket. Use `-multi` for a separate instance.

## Global hotkeys on Linux

Wayland has no global hotkey API that an ordinary client can use, so ShareX supports three
ways to get the same result:

1. **Compositor key bindings** calling the CLI (Hyprland and sway; recommended, always
   available) — see below.
2. **`org.freedesktop.portal.GlobalShortcuts`** (KDE Plasma 6, GNOME 48+, and
   xdg-desktop-portal-hyprland when present): configure the keys from ShareX's Hotkeys
   settings; the desktop asks for confirmation once.
3. **X11 `XGrabKey`** on X11/XWayland sessions: hotkeys work exactly as configured in the
   Hotkeys window.

The full command line reference is [docs/CLI.md](docs/CLI.md).

### Hyprland (`~/.config/hypr/hyprland.conf`)

```ini
bind = , PRINT,         exec, sharex -RectangleRegion
bind = SUPER SHIFT, S,  exec, sharex -RectangleRegion
bind = SUPER, S,        exec, sharex -PrintScreen
bind = SUPER SHIFT, W,  exec, sharex -ActiveWindow
bind = SUPER SHIFT, M,  exec, sharex -ActiveMonitor
bind = SUPER SHIFT, L,  exec, sharex -LastRegion
bind = SUPER SHIFT, R,  exec, sharex -ScreenRecorder
bind = SUPER ALT, R,    exec, sharex -ScreenRecorderGIF
bind = SUPER SHIFT, X,  exec, sharex -StopScreenRecording
bind = SUPER SHIFT, O,  exec, sharex -OCR
bind = SUPER SHIFT, P,  exec, sharex -ScreenColorPicker
bind = SUPER SHIFT, E,  exec, sharex -ImageEditor
bind = SUPER SHIFT, H,  exec, sharex -OpenHistory
```

### sway (`~/.config/sway/config`)

```ini
bindsym Print          exec sharex -RectangleRegion
bindsym $mod+Shift+s   exec sharex -RectangleRegion
bindsym $mod+s         exec sharex -PrintScreen
bindsym $mod+Shift+w   exec sharex -ActiveWindow
bindsym $mod+Shift+r   exec sharex -ScreenRecorder
bindsym $mod+Shift+x   exec sharex -StopScreenRecording
bindsym $mod+Shift+o   exec sharex -OCR
bindsym $mod+Shift+h   exec sharex -OpenHistory
```

### KDE Plasma

*System Settings → Keyboard → Shortcuts → Add → Command or Script*, add one entry per action
(command `sharex -RectangleRegion`, `sharex -PrintScreen`, `sharex -ScreenRecorder`, …).
Alternatively let ShareX register through the Global Shortcuts portal from its Hotkeys
settings. The screenshot and screen-cast portals ask for permission the first time.

### GNOME

```bash
base=org.gnome.settings-daemon.plugins.media-keys
path=/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/
gsettings set $base custom-keybindings \
  "['${path}sharex-region/', '${path}sharex-screen/', '${path}sharex-record/']"

gsettings set ${base}.custom-keybinding:${path}sharex-region/ name 'ShareX region'
gsettings set ${base}.custom-keybinding:${path}sharex-region/ command 'sharex -RectangleRegion'
gsettings set ${base}.custom-keybinding:${path}sharex-region/ binding '<Control><Shift>Print'

gsettings set ${base}.custom-keybinding:${path}sharex-screen/ name 'ShareX screen'
gsettings set ${base}.custom-keybinding:${path}sharex-screen/ command 'sharex -PrintScreen'
gsettings set ${base}.custom-keybinding:${path}sharex-screen/ binding 'Print'

gsettings set ${base}.custom-keybinding:${path}sharex-record/ name 'ShareX record'
gsettings set ${base}.custom-keybinding:${path}sharex-record/ command 'sharex -ScreenRecorder'
gsettings set ${base}.custom-keybinding:${path}sharex-record/ binding '<Control><Shift>R'
```

### X11

No configuration needed: enable hotkeys in ShareX's Hotkeys window and they are grabbed
with `XGrabKey`. Under a Wayland session the X11 backend (XWayland) also works, but the
keys are then only seen by XWayland clients.

## Feature support matrix by session type

Legend: ✅ verified on this session type · 🟢 implemented (mechanism listed), not yet
exercised on that session · ⚠️ works with caveats/permissions/extra packages ·
❌ not possible; the documented alternative applies.

| Feature | Hyprland | sway | GNOME | KDE | X11 |
| --- | --- | --- | --- | --- | --- |
| Application UI (Avalonia) | ✅ XWayland | 🟢 XWayland | 🟢 XWayland | 🟢 XWayland | 🟢 Xlib |
| Tray icon | ✅ SNI | 🟢 SNI | ⚠️ needs AppIndicator extension | 🟢 SNI | 🟢 SNI |
| Notifications (`org.freedesktop.Notifications`) | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| Clipboard: text | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| Clipboard: images / file list | ⚠️ `wl-clipboard` | ⚠️ `wl-clipboard` | ⚠️ `wl-clipboard` | ⚠️ `wl-clipboard` | ⚠️ `xclip` |
| Fullscreen / monitor capture | ✅ `grim` | 🟢 `grim` | 🟢 Screenshot portal | 🟢 Screenshot portal | 🟢 `XGetImage` |
| Region capture + overlay editor | ✅ frozen overlay + `grim` | 🟢 `grim`/`slurp` | ⚠️ portal permission prompt | ⚠️ portal permission prompt | 🟢 Xlib overlay |
| Active window capture | ✅ `hyprctl` | 🟢 `swaymsg` | ⚠️ no portal window API | ⚠️ KWin D-Bus | 🟢 EWMH |
| Window list / window under cursor | ✅ `hyprctl` | 🟢 `swaymsg` | ❌ (use region capture) | ⚠️ KWin script | 🟢 EWMH |
| Scrolling capture | ⚠️ needs synthetic input | ⚠️ needs synthetic input | ⚠️ portal RemoteDesktop | ⚠️ portal RemoteDesktop | 🟢 XTest |
| Auto capture | ✅ timer + capture backend | 🟢 | 🟢 | 🟢 | 🟢 |
| Screen recording (MP4/WebM/MKV) | ✅ `ffmpeg`/`gpu-screen-recorder` | 🟢 `ffmpeg` + wlroots | 🟢 ScreenCast portal + PipeWire | 🟢 ScreenCast portal + PipeWire | 🟢 `x11grab` |
| Recording: system audio | ✅ PipeWire monitor | 🟢 | 🟢 | 🟢 | 🟢 PulseAudio monitor |
| Recording: microphone | ✅ `pw-record`/ffmpeg | 🟢 | 🟢 | 🟢 | 🟢 |
| Recording: camera overlay (V4L2) | ✅ `ffmpeg` filter graph | 🟢 | 🟢 | 🟢 | 🟢 |
| GIF recording | ✅ `ffmpeg` palettegen | 🟢 | 🟢 | 🟢 | 🟢 |
| Global hotkeys | ⚠️ compositor bindings (or portal) | ⚠️ compositor bindings | ⚠️ GlobalShortcuts portal (GNOME 48+) | 🟢 GlobalShortcuts portal | ✅ `XGrabKey` |
| Watch folders | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| Uploaders (.sxcu, FTP/SFTP/WebDAV/S3/…) | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| History (SQLite) | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| OCR (`tesseract`) | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| QR code generate / scan region | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| Screen colour picker | ✅ screenshot-based | 🟢 | 🟢 | 🟢 | 🟢 |
| Ruler overlay | ⚠️ overlay window limits | ⚠️ | ⚠️ | ⚠️ | 🟢 |
| Pin to screen | ⚠️ no client-side positioning | ⚠️ | ⚠️ | ⚠️ | 🟢 |
| Mouse highlighter | ⚠️ needs input hook (X11/portal) | ⚠️ | ⚠️ | ⚠️ | 🟢 XInput2 |
| Borderless/topmost for other windows | ❌ use compositor rules (`hyprctl`) | ❌ `swaymsg` rules | ❌ | ❌ | ⚠️ `_NET_WM_STATE` |
| Inspect window | ✅ `hyprctl` | 🟢 `swaymsg` | ❌ | ⚠️ | 🟢 |
| Video player / converter / trimmer | ✅ `mpv`/`ffmpeg` | 🟢 | 🟢 | 🟢 | 🟢 |
| Image editor, effects, tools | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| Autostart (`~/.config/autostart`) | ✅ | 🟢 | 🟢 | 🟢 | 🟢 |
| `.sxcu`/`.sxie` file association | ✅ installer + MIME | 🟢 | 🟢 | 🟢 | 🟢 |
| Browser extension (native messaging) | ⚠️ manifest install needed | ⚠️ | ⚠️ | ⚠️ | ⚠️ |

Notes:

* The current build renders through Avalonia's **X11 backend**, so under Wayland it runs as
  an XWayland client (`DISPLAY` must exist). A session without XWayland fails with
  `XOpenDisplay failed`. The Wayland-native backend (`Avalonia.Wayland`) is not referenced
  by the project yet.
* Wayland clients cannot move, raise or resize other applications' windows; anything that
  needs that (pin to screen, borderless, topmost, ruler overlays) is implemented through
  compositor rules or degrades with an explanation.
* Wayland also has no clipboard that survives the owning process, so ShareX keeps a
  selection owner alive (`wl-clipboard`) while the copied data is needed.

## Troubleshooting

**Nothing happens / no window.** Check `$WAYLAND_DISPLAY` and `$DISPLAY`; the current build
needs an X display (XWayland). Look at the log file or run `sharex -sandbox` in a terminal
to see the output immediately.

**Screenshots fail with a permission prompt or silently do nothing.**
*Make sure a portal is running*: `systemctl --user status xdg-desktop-portal`
and the session-specific backend (`xdg-desktop-portal-hyprland`, `-wlr`, `-gtk`, `-gnome`,
`-kde`). Test it with `dbus-send --session --print-reply --dest=org.freedesktop.portal.Desktop
/org/freedesktop/portal/desktop org.freedesktop.DBus.Properties.Get string:org.freedesktop.portal.Screenshot string:version`.
On Hyprland, region capture also needs `grim` (and preferably `slurp`) on `PATH` and
`HYPRLAND_INSTANCE_SIGNATURE` visible to the app (it is, when started from the session).

**Recording produces no audio.** ShareX records the PipeWire/PulseAudio monitor of the
default sink; check `pactl info`, `pactl list short sources` (look for `<sink>.monitor`).
On a minimal install, `pipewire-pulse` (or `pulseaudio`) must be running.

**Recording fails immediately.** `ffmpeg -version` must work; hardware encoders are
optional (`ffmpeg -encoders | grep -E 'vaapi|nvenc'`). For wlroots sessions
`gpu-screen-recorder` gives better performance if installed.

**The tray icon is missing on GNOME.** GNOME does not show StatusNotifierItem icons out of
the box; install the AppIndicator/KStatusNotifierItem extension
(`gnome-shell-extension-appindicator`) and log back in.

**Clipboard paste is empty after copying an image.** Keep a clipboard manager installed on
Wayland, or use ShareX's own history to re-copy — a copy only exists while an owner owns
the selection.

**Uploads fail with a path error.** ShareX uses `/` paths and treats them case-sensitively
on Linux. Windows-style backslash paths in imported settings/actions are translated, but
custom uploader bodies with hard-coded `\` may need to be edited in *Custom uploaders*.

**Everything is too small / blurry under fractional scaling.** The current X11/XWayland
path uses XWayland's scaling; try `GDK_SCALE`/`QT_SCALE_FACTOR` equivalents for Avalonia
(`AVALONIA_GLOBAL_SCALE_FACTOR=1.5`) or set the scale factor in ShareX's application
settings.

**Debug output.** `sharex -sandbox -silent` writes to a throw-away configuration, and the
log in `Logs/ShareX-Log-<yyyy-MM>.txt` contains the full command line, the startup flags and
every exception.

## Command line

`sharex --help` prints a short summary; [docs/CLI.md](docs/CLI.md) documents every action,
the flags and examples for each desktop:

```bash
sharex -RectangleRegion            # capture a region
sharex -AutoClose -PrintScreen     # capture and exit (scripting)
sharex -task "Upload to server" -ActiveWindow
sharex -workflow "OCR and copy"
sharex ~/Pictures/shot.png         # upload a file
sharex -CustomUploader ~/my-host.sxcu
```

## Packaging files

```
packaging/arch/PKGBUILD                                  Arch/CachyOS package (built and smoke-tested)
packaging/flatpak/io.github.ShareX.ShareX.yml            Flatpak manifest (draft)
packaging/linux/sharex                                   launcher installed as bin/sharex
packaging/linux/sharex.desktop                           desktop entry (menu + MIME associations)
packaging/linux/sharex.xml                               shared-mime-info for .sxcu/.sxie
packaging/linux/io.github.ShareX.ShareX.metainfo.xml     AppStream metadata
packaging/linux/icons/hicolor/**                         icons generated from ShareX_Icon.ico
packaging/linux/generate-icons.sh                        regenerates the icon set
scripts/linux/publish.sh                                 self-contained single-file publish
scripts/linux/install-user.sh                            per-user install into ~/.local
```

Layouts the launcher resolves: `/usr/bin/sharex` + `/usr/lib/sharex/ShareX`,
`~/.local/bin/sharex` + `~/.local/lib/sharex/ShareX`, `/app/bin/sharex` +
`/app/lib/sharex/ShareX` (Flatpak). `SHAREX_BIN` / `SHAREX_LIB` override the lookup.

## License

ShareX is licensed under the GNU General Public License v3.0 — see [LICENSE.txt](LICENSE.txt).
