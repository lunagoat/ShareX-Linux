# ShareX for Linux — porting brief (READ FIRST)

Goal: ShareX (https://github.com/ShareX/ShareX, GPL v3) running **natively on Linux** with **every feature working as it does on Windows**:
capture, recording, editor, uploaders, tools, history, hotkeys, workflows, watch folders, etc.

## The key fact

Upstream ShareX (develop branch, Oct 2026) is already mid-migration from WinForms to **Avalonia 12 + SkiaSharp on net10.0**
(121 `.axaml` files, `ShareX.ImageEditor` is 100% cross-platform). What is left Windows-only is **native interop and
Windows-only runtime behaviour**, not UI. The whole solution already **compiles and starts** on Linux (Wayland, Hyprland) with a
plain `net10.0` target. The port = replace each Windows-only runtime piece with a Linux implementation **behind the seam that
already exists** (e.g. `IHotkeyHost`, `WindowsClipboard`/`AvaloniaClipboard`, `WindowsNativeWindow`, `WindowsTrayIcon`,
`WindowsPrintDialog`), and make the UI/strings say Linux instead of Windows where visible.

No decompiling is needed — the source is the spec. Read the C# for intended behaviour, then reproduce it on Linux.

## Target environment (what to optimise for)

- Primary: **Hyprland (Wayland)** on CachyOS/Arch, PipeWire, xdg-desktop-portal(-hyprland, -gtk).
- Should also work (best effort, but design for it): other wlroots compositors (sway), GNOME/KDE Wayland (portals), and X11.
- Wayland realities: no global hotkeys without a portal/compositor binding, no arbitrary screen grabbing without
  wlr-screencopy / ext-image-copy-capture / the Screenshot+ScreenCast portals, no window positioning/always-on-top by the client.
  Solve them the way a real Linux app would (portals, `grim`, compositor IPC such as `hyprctl`/`swaymsg`, D-Bus, `wl-clipboard`),
  and **detect the session/compositor at runtime** (`XDG_SESSION_TYPE`, `XDG_CURRENT_DESKTOP`, `HYPRLAND_INSTANCE_SIGNATURE`, `SWAYSOCK`).
- Tools installed on the dev machine: grim slurp wl-copy wl-paste xdotool xclip ffmpeg tesseract mpv gpu-screen-recorder pw-record parec pactl
  v4l2-ctl notify-send gdbus busctl 7z jq satty. Not installed: wf-recorder, ydotool, wtype, sway, weston. Prefer **native C# (D-Bus / P/Invoke to libX11, libwayland not required)**
  over shelling out when practical; shelling out to the tools above is acceptable where it is the robust option, but check they exist and degrade gracefully with a clear message.
  D-Bus from C#: use `Tmds.DBus.Protocol` (add to `Directory.Packages.props`).

## Build / run / test

```bash
source scripts/linux/env.sh                 # user-local .NET 10 SDK at ~/.dotnet (no system SDK)
scripts/linux/build.sh                      # full build, prints only errors
SX_SANDBOX=/tmp/sx-$USER-mytest timeout 60 scripts/linux/run.sh [args]   # isolated HOME/XDG dirs
```

* Output binary: `ShareX/bin/Debug/net10.0/ShareX` (apphost). Settings land under the sandbox, **never test against the real `~`**.
* Windows appear on the **user's live Hyprland desktop** (a nested headless compositor does not work here). So:
  always wrap runs in `timeout`, close what you open (`pkill -x ShareX`), move test windows off the way with
  `hyprctl dispatch movetoworkspacesilent special:test,class:ShareX`, never leave fullscreen/region overlays up, and never
  run anything that locks the screen, changes the user's Hyprland config, or sends input to other apps.
* Screenshots of your own windows for verification: `hyprctl clients -j` → geometry → `grim -g "x,y WxH" out.png` then look at the PNG.
* Prefer **Avalonia.Headless** unit/UI tests (renders to PNG, no display needed) and plain xUnit tests for logic. Put tests in
  a new `tests/` project (`ShareX.Linux.Tests`, add to `ShareX.sln`); keep them fast and offline.
* Network: only your own tests against localhost / local fake servers. Do not hit real upload services with credentials.
* The dev machine is the user's daily driver: **do not** install packages, use sudo, edit dotfiles, touch `~/.config`, `~/Documents`,
  or kill processes you did not start.

## Source layout (what lives where)

| Project | Role | Windows-isms to remove |
|---|---|---|
| `ShareX.HelpersLib` | shared helpers, settings, input, clipboard, native wrappers, Skia image helpers | `Native/*` (user32/gdi32/dwm/shell32), DPAPI, `WindowsHotkeyHost`, `WindowsClipboard`, `WindowsTrayIcon`, `WindowsNativeWindow`, printing, registry, taskbar, icons |
| `ShareX.Avalonia` | shared Avalonia controls/theme, `ScreenColorPickerWindow` | user32 P/Invoke in the colour picker |
| `ShareX.ScreenCaptureLib` | screenshot capture, region overlay, scrolling capture, FFmpeg option UI, recording window | `Screenshot*.cs` (GDI/DXGI), `HDRScreenCapture`, window lists, region overlay, scrolling capture |
| `ShareX.ScreenRecordingLib` | recording engine. **Windows impl moved to `LegacyWindows/` (reference only, not compiled); `ScreenRecorder` is a stub with the real public API** | all of it → ffmpeg/PipeWire/PulseAudio/V4L2 |
| `ShareX.Tools` | OCR, ruler, colour picker, hash checker, QR, inspect window, mouse highlighter, video player/converter, pin to screen, … | `WindowsMediaPlayer`, `InspectWindow`, `MouseHighlighter` hook, video thumbnails, BorderlessWindow, MonitorTest |
| `ShareX.UploadersLib` | all uploaders + custom uploader (.sxcu) engine | OAuth browser/loopback flows, anything using Windows paths/registry |
| `ShareX.HistoryLib`, `ShareX.ImageEffectsLib` | history DB, image effects | `System.Drawing` leftovers (works at compile time, **throws at runtime on Linux** — see below) |
| `ShareX.ImageEditor(.App)` | editor, already Avalonia | `WindowsDesktopWallpaperService`, `WindowsCursorBitmapRenderer`, `EditorView.ImageInsert` P/Invoke |
| `ShareX` | the app: task pipeline, settings UI, main window, hotkeys, CLI, screen record bar | `Infrastructure/*` (paths, registry options, file-type registration, startup), `TaskHelpers`, `IntegrationHelpers`, `StartupManager`, `ShareXUpdateManager`, tray, notification windows |
| `ShareX.Steam`, `ShareX.Setup` | Windows packaging | ignore (Linux packaging lives in `packaging/`) |

### `System.Drawing.Common` trap
`System.Drawing.Common` was added as a *compile-only* compat package. `Bitmap`/`Graphics`/`Icon`/`SoundPlayer`-style types **throw
`PlatformNotSupportedException` on Linux**. Primitives (`Color`, `Point`, `Rectangle`, `Size`) are fine. Replace runtime uses
of Bitmap/Graphics/Icon/Font with SkiaSharp (`SKBitmap`, `SKCanvas`, …) — the code base already has `SkiaImageHelpers`/`SkiaDrawing`.
Grep for `System.Drawing.Bitmap|Graphics|Icon|Image\b|Imaging|Printing|Text` to find them.

## Rules for all workers

1. You own **one work package** (below) in **your own git worktree/branch**. Do not edit outside your package unless unavoidable;
   if you must touch a shared file, keep the edit minimal and mention it in your final report.
2. New Linux code goes in **new files** under a `Linux/` folder of the owning project (namespace `ShareX.<Project>.Linux` or the project's own),
   so merges stay trivial. Prefer small `if (OperatingSystem.IsLinux())`-free design: replace the Windows implementation behind its seam,
   register the Linux one as the default. (Windows support is not a goal of this fork.)
3. Commit early and often on your branch with clear messages. End commit messages with the trailer line
   `Co-Authored-By: <your agent name> <noreply@example.com>`.
4. The build must stay green (`scripts/linux/build.sh` → `0 Error(s)`) at every commit.
5. Don't leave dead code: when a Windows file is fully replaced, delete it (or move to `Legacy/` excluded from compile if it is a useful reference for another worker).
6. Verify features by actually running them (see Build/run/test). "It compiles" is not done. Record what you verified and how in `docs/status/<package>.md`
   (feature → status [works / partial / missing] → how verified → known gaps).
7. Be honest in your final report: list what works, what doesn't, what you could not test.
8. Match upstream code style (file license header, 4-space indent, braces on new lines in older files, file-scoped namespaces in the newer `Presentation` code — follow the file you are editing).

## Work packages

### WP1 — Platform core (`ShareX.HelpersLib`, `ShareX/Infrastructure`, `ShareX/*Manager.cs`)
Paths & settings (XDG: config → `~/.config/ShareX`, data/history/logs → `~/.local/share/ShareX` or keep one personal folder; honour `--portable`, `--sandbox`, `PersonalPath`);
replace `RegistryHelpers`/`SystemOptions` registry storage with a file-based store; DPAPI replacement for encrypted settings (Secret Service via D-Bus when available, else a
per-user key file with 0600); autostart (`~/.config/autostart/*.desktop`, `StartupManager`); single instance (named pipe → unix domain socket in `$XDG_RUNTIME_DIR`, args forwarding); desktop notifications
(`org.freedesktop.Notifications` D-Bus incl. actions/click-to-open); "open folder and select file" (`org.freedesktop.FileManager1.ShowItems`, fallback xdg-open); open URL/file (xdg-open);
file-type registration/shell integration (`.desktop` file, MIME types for `.sxcu`/`.sxie`, optional Dolphin/Nautilus "Upload with ShareX" actions); update checker → disabled or "new version" notice only;
print (CUPS `lp`) ; wallpaper setting (`WindowsDesktopWallpaperService`: gsettings / KDE / `awww`/`swww`/`hyprpaper` / feh); DWM/Taskbar/desktop-icon managers → removed or no-ops with the UI hiding Windows-only options;
`ShortcutHelpers`, `FileDialogHelpers`, `FileHelpers` path handling (case sensitivity, `\` vs `/`, reserved chars), `Helpers.OSVersion` etc.; localization strings that say "Windows" in user-visible text (en first).
Also: tray icon (`WindowsTrayIcon`/`LucideTrayIcon` → Avalonia `TrayIcon`/StatusNotifierItem) and make the main window/tray menu behaviour right.

### WP2 — Input, windows & clipboard (`ShareX.HelpersLib/Input`, `Native/WindowInfo`, clipboard classes)
`IHotkeyHost` Linux implementation(s): XDG **GlobalShortcuts portal** (`org.freedesktop.portal.GlobalShortcuts`, works on KDE/GNOME 48+/xdg-desktop-portal-hyprland), **X11 XGrabKey** via P/Invoke to libX11 for Xorg/XWayland-less sessions,
and a **CLI/IPC trigger** path (`ShareX/ShareXCLIManager.cs`: `sharex -RectangleRegion`, `-PrintScreen`, `-ScreenRecorder`, … executed by the running instance) so users can bind keys in Hyprland/sway config. Provide a UI hint listing the exact `bind = …` lines for Hyprland/sway.
`WindowInfo` / window enumeration on Linux: Hyprland (`hyprctl clients -j` or the IPC socket directly), sway (`swaymsg -t get_tree`), X11 (`_NET_CLIENT_LIST`, `XGetWindowAttributes`, `_NET_WM_NAME`, frame extents), KDE (KWin D-Bus script) best effort —
needed by active-window / window-list / window-under-cursor capture, `ShareX.ScreenCaptureLib/Helpers/WindowsList.cs`, InspectWindow, window rules, "Window title" name-pattern tokens, `ApplicationIcon`.
Cursor position & cursor image (Hyprland `hyprctl cursorpos`, X11 `XQueryPointer`, Avalonia where possible), mouse/keyboard simulation if used (`SendInput` usages → uinput/portal RemoteDesktop or drop with UI note).
Clipboard: `ClipboardHelpers`/`WindowsClipboard`/`AvaloniaClipboard` → full Linux behaviour: copy image (PNG), text, file list (`text/uri-list`, `x-special/gnome-copied-files`), URL; paste/clipboard upload from image/text/files; Wayland needs a persistent selection owner (use `wl-copy` for image data or keep owner alive).
Clipboard viewer tool depends on this.

### WP3 — Screen capture (`ShareX.ScreenCaptureLib`, `ShareX/CaptureHelpers`, `ShareX.Avalonia/Windows/ScreenColorPicker*`)
Backends, auto-detected, with fallbacks: wlroots (`wlr-screencopy` or `grim`; Hyprland also supports `ext-image-copy-capture`), portals (`org.freedesktop.portal.Screenshot` for GNOME/KDE), X11 (`XGetImage`/XComposite via libX11/libXrandr P/Invoke).
Fullscreen / per-monitor / active monitor / active window / window-under-cursor / custom region / last region captures with correct **HiDPI + fractional scaling + multi-monitor** geometry (logical vs physical pixels), cursor compositing option, transparent window capture (use compositor-provided window geometry; no DWM).
Region selection overlay (`Presentation/RegionCapture`): the frozen-screenshot overlay across all monitors on Wayland (one borderless fullscreen Avalonia window per monitor or layer-shell if reachable), crosshair, magnifier, snapping to windows/UI rects (needs WP2 `WindowInfo`), all shape/annotation tools in the region mode (rectangle, ellipse, line, arrow, text, blur, pixelate, highlight, step, freehand, cursor, speech balloon, image), toolbar, hotkeys inside the overlay, multi-region, Esc to cancel.
Scrolling capture (`ScrollingCapture*`: needs synthetic scroll input → portal RemoteDesktop/`uinput`/X11 XTest; document what works per session type), Auto capture, "Capture webpage"/screen color picker (`ScreenColorPickerWindow` P/Invoke → screenshot-based picker), pin-to-screen, ruler overlay support.
Remove `HDRScreenCapture`/DXGI paths.

### WP4 — Screen recording (`ShareX.ScreenRecordingLib`, `ShareX.ScreenCaptureLib/ScreenRecording` + `Presentation/ScreenRecording`, `ShareX/ScreenRecordManager.cs`, `ShareX/Presentation/ScreenRecording`)
Implement the real `ScreenRecorder` behind the existing public API of `ShareX.ScreenRecordingLib` (`RecordingOptions`, `RecordingResult`, `AudioCaptureDevices`, `CameraCaptureDevices`, pause/resume/stop/`Resume(Rectangle)`, duration limit, Diagnostic events).
Video sources: Hyprland/wlroots (`gpu-screen-recorder` / wlr-screencopy frames piped to ffmpeg, or `ext-image-copy-capture`), GNOME/KDE/any (ScreenCast portal + PipeWire → ffmpeg `-f pipewire`/GStreamer), X11 (`ffmpeg -f x11grab`/`kmsgrab`), region + window + monitor, cursor on/off.
Audio: system audio (PipeWire monitor of default sink) + microphone through ffmpeg/pulse (`pactl`/`pw-cli` for device enumeration), gain, mixing. Camera: V4L2 enumeration (`v4l2-ctl`), overlay (position/shape/size/margin) composited with ffmpeg filter graph.
Outputs: MP4 (H.264/AAC, use VAAPI/NVENC when available, else libx264), WebM/VP9, MKV, **GIF** (palettegen/paletteuse), the FFmpeg custom-args mode (`FFmpegOptions`) and ShareX's "screen recording" task flow (start/stop/pause hotkeys, countdown, recording bar, region border, auto-start on region select, after-record upload).
Replace the "download FFmpeg" flow with system ffmpeg detection + clear message. Pause = segment & concat.

### WP5 — Tools (`ShareX.Tools`)
Make every tool in the Tools menu launch and work on Linux: Colour picker (screen), Ruler, Pin to screen, QR code (generate/scan from screen), Hash checker, OCR (done via tesseract: verify UI + language list + auto-copy flow), Inspect window (WP2 WindowInfo), Directory indexer, Image combiner/splitter/thumbnailer/resizer/converter/comparer/watermark/analyze/metadata, Icon converter, Animated GIF maker/trimmer, Video converter / thumbnailer (ffmpeg), Video player (`WindowsMediaPlayer` → LibVLCSharp or mpv/libmpv embedding, or ffplay fallback), Background remover (ONNX runtime: replace `Microsoft.ML.OnnxRuntime.DirectML` by the CPU/CUDA package), Borderless window (compositor rules → document/limit), Mouse highlighter (overlay + global mouse hook → X11 XInput2 / Hyprland cursor polling), Monitor test, Network monitor, Clipboard viewer, Remote storage browser, Metadata viewer.
Anything physically impossible on Wayland must degrade with a clear in-app message and a documented alternative — not crash.

### WP6 — App integration, uploaders, history (`ShareX`, `ShareX.UploadersLib`, `ShareX.HistoryLib`, `ShareX.ImageEffectsLib`, `ShareX.ImageEditor`)
The task pipeline end to end: capture → after-capture tasks (copy, save, annotate in editor, add effects/watermark, print, open, upload, pin, OCR, delete file) → upload → after-upload (copy URL, shorten, open URL, QR, notification) with history entry, for **every task type** (capture, file/folder/clipboard/URL/text/drag-drop upload, watch folders, hotkey workflows, "send to" actions, external programs/Actions with `\`→`/` path handling and shell quoting).
Uploaders: run the full upload test-suite against local fake HTTP/FTP/SFTP servers for the generic engine (custom uploader `.sxcu` multipart/JSON/XML/regex/JSONPath parsing, OAuth1/2 flows with `xdg-open` + loopback listener or `ShareX` URI handler), FTP/SFTP/WebDAV/S3/Azure/… config paths, URL shortener/sharing services, "test upload" buttons; fix every Windows-only assumption (path separators, `Environment.SpecialFolder` values that are empty on Linux, file name sanitizing, MIME detection).
History (SQLite) + history window + image history; image effects library (replace System.Drawing remains); `ShareX.ImageEditor` integration (open from capture, save, "upload", insert image, cursor bitmap renderer, wallpaper action).
Main window, application settings, task settings, hotkey settings, uploader config UIs: open each page, change every setting type, round-trip the JSON settings, fix crashes/blank pages; hide or relabel Windows-only options (Run at startup text, shell extension, Steam, Store, "Windows" wording).
Also the browser extension native-messaging host (`ShareX.NativeMessagingHost`, manifest locations for Firefox/Chromium on Linux).

### WP7 — Packaging, CLI & docs (`packaging/`, `README.md`, `docs/`)
`PKGBUILD` (Arch/CachyOS, builds from source with `dotnet publish`, optional deps listed: ffmpeg, grim, slurp, wl-clipboard, tesseract, gpu-screen-recorder, xdg-desktop-portal-*), a Flatpak manifest draft, `.desktop` + icons (hicolor, from `ShareX_Icon.ico`/SVG), AppStream metainfo, MIME xml, `sharex` wrapper script, install script for `~/.local` (`scripts/linux/install-user.sh`, idempotent, no sudo), Hyprland/sway/KDE/GNOME keybinding examples,
Linux README with feature support matrix by session type (what works on Hyprland/sway/GNOME/KDE/X11), troubleshooting (portals, PipeWire, ffmpeg), self-contained single-file `dotnet publish -r linux-x64` profile that actually starts.

## Integration protocol
Each worker reports to the lead by writing `docs/status/<package>.md` and committing it. The lead merges branches into `linux-port` in this order: WP1, WP2, WP3, WP4, WP5, WP6, WP7, resolving conflicts, then runs the end-to-end QA matrix (`docs/status/QA.md`).
