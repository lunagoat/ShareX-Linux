# ShareX command line reference (Linux port)

This is the complete reference of everything the `sharex` launcher and the ShareX
executable accept on Linux. It was derived from the source:

* `ShareX/ShareXCLIManager.cs` — action commands (`CheckCLIHotkey`, `-task`, `-workflow`,
  `-CustomUploader`, `-ImageEffect`, `-NativeMessagingInput`) and positional file/URL uploads.
* `ShareX/Infrastructure/ApplicationCommandLine.cs` — `-AutoClose`, `-uninstall`.
* `ShareX/Program.cs` — `-multi`/`-m`, `-silent`/`-s`, `-NoHotkeys`.
* `ShareX/Infrastructure/PersonalPathManager.cs` — `-portable`/`-p`, `-sandbox`.
* `packaging/linux/sharex` — the launcher that wraps the executable (`-h`, `--version`).

## How commands are parsed

* An argument starting with `-` is a **command**; the next argument that does not start
  with `-` is taken as that command's **parameter**.
* Command names are compared case-insensitively (`-rectangleRegion` == `-RectangleRegion`);
  parameters (task/workflow names, file paths) are compared exactly.
* An argument that is not a command is treated as a **file path or URL**: URLs are
  downloaded and uploaded, local files are uploaded. Use `-task` in the same command
  line to choose which task profile is used.
* ShareX is single-instance: the second `sharex -RectangleRegion` hands the command to the
  running instance over its local socket and exits. Add `-multi` to start a separate
  instance instead.
* Actions with a parameter that is not an existing file are reported in the log; the
  process does not abort.

## Launcher options (`packaging/linux/sharex`)

These are handled by the `sharex` shell wrapper, before ShareX itself is started:

| Option | Meaning |
| --- | --- |
| `-h`, `--help` | Print the short usage summary and exit. |
| `--version` | Print the ShareX version and exit. |

The wrapper also translates file-manager/MIME invocations: a `.sxcu` file becomes
`-CustomUploader <file>` and a `.sxie` file becomes `-ImageEffect <file>`, which is what
the desktop file and the shared-mime-info definitions install.

## Global control flags

| Flag | Meaning |
| --- | --- |
| `-multi`, `-m` | Do not use the single-instance socket; start a second ShareX process. |
| `-silent`, `-s` | Start without showing the main window (tray only). |
| `-NoHotkeys` | Do not warn about hotkeys that could not be registered. |
| `-AutoClose` | Exit ShareX after the tasks started from this command line have finished. Useful for scripts: `sharex -AutoClose -PrintScreen`. |
| `-portable`, `-p` | Keep settings and history in a `ShareX/` folder next to the executable instead of the personal folder. **Linux caveat:** that folder name collides with the executable name, so it only works if the executable is not itself called `ShareX` (the packaged binary is); otherwise ShareX shows a *ShareX – Error* dialog and falls back to the personal folder. |
| `-sandbox` | Run with a throw-away configuration: settings and history are neither read nor written. Log output still goes to the personal folder's `Logs/`. |
| `-uninstall` | Steam build only: remove integration entries and exit. No effect in normal builds. |

The "personal folder" is `~/Documents/ShareX` on a normal desktop (the desktop's XDG
*Documents* directory), not an XDG config/data directory, and it is not moved by overriding
`$HOME`. See the "First run" section of `README.linux.md`.

## Task and workflow selection

| Command | Parameter | Meaning |
| --- | --- | --- |
| `-task <name>` | Name (its `Description`) of a task/hotkey in *Hotkeys* settings | Use that task's settings for the files/URLs in the same command line. Example: `sharex -task "Upload screenshot" /tmp/shot.png` |
| `-workflow <name>` | Name of a configured workflow/hotkey | Run that workflow (its full after-capture/after-upload chain) immediately. |

Names are matched exactly against the task name shown in the Hotkeys window (or the
localised action name when the task has no custom description). Either way the running
instance must already have those settings loaded.

## Import commands

| Command | Parameter | Meaning |
| --- | --- | --- |
| `-CustomUploader <file.sxcu>` | Path to a `.sxcu` file | Import a custom uploader configuration. |
| `-ImageEffect <file.sxie>` | Path to a `.sxie` file | Import an image effect preset. |
| `-NativeMessagingInput <file.json>` | Path to a JSON file | Handle a request from the browser extension (native messaging host). |

## Actions

Every name in the `HotkeyType` enum (`ShareX/Enums.cs`) can be used as a command. The
description column is the same string ShareX shows in its hotkey list
(`ShareX.HelpersLib/Localization/Strings.resx`, key `HotkeyType_<Name>`).

### Miscellaneous

| Command | Description |
| --- | --- |
| `-None` | None |

### Upload

| Command | Description |
| --- | --- |
| `-FileUpload` | Upload file |
| `-FolderUpload` | Upload folder |
| `-ClipboardUpload` | Upload from clipboard |
| `-ClipboardUploadWithContentViewer` | Upload from clipboard with content viewer |
| `-UploadText` | Upload text |
| `-UploadURL` | Upload from URL |
| `-DragDropUpload` | Drag and drop upload |
| `-ShortenURL` | Shorten URL |
| `-StopUploads` | Stop all active uploads |

### Screen capture

| Command | Description |
| --- | --- |
| `-PrintScreen` | Capture entire screen |
| `-ActiveWindow` | Capture active window |
| `-CustomWindow` | Capture pre configured window |
| `-ActiveMonitor` | Capture active monitor |
| `-RectangleRegion` | Capture region |
| `-CustomRegion` | Capture pre configured region |
| `-LastRegion` | Capture last region |
| `-ScrollingCapture` | Start/Stop scrolling capture |
| `-AutoCapture` | Auto capture |
| `-StartAutoCapture` | Start auto capture using last region |
| `-StopAutoCapture` | Stop auto capture |

### Screen record

| Command | Description |
| --- | --- |
| `-ScreenRecorder` | Start/Stop screen recording |
| `-ScreenRecorderActiveWindow` | Start/Stop screen recording using active window region |
| `-ScreenRecorderCustomRegion` | Start/Stop screen recording using pre configured region |
| `-StartScreenRecorder` | Start/Stop screen recording using last region |
| `-ScreenRecorderGIF` | Start/Stop screen recording (GIF) |
| `-ScreenRecorderGIFActiveWindow` | Start/Stop screen recording (GIF) using active window region |
| `-ScreenRecorderGIFCustomRegion` | Start/Stop screen recording (GIF) using pre configured region |
| `-StartScreenRecorderGIF` | Start/Stop screen recording (GIF) using last region |
| `-StopScreenRecording` | Stop screen recording |
| `-PauseScreenRecording` | Pause screen recording |
| `-AbortScreenRecording` | Abort screen recording |

### Tools

| Command | Description |
| --- | --- |
| `-ColorPicker` | Color picker |
| `-ScreenColorPicker` | Screen color picker |
| `-Ruler` | Ruler |
| `-MouseHighlighter` | Toggle mouse highlighter |
| `-PinToScreen` | Pin to screen |
| `-PinToScreenFromScreen` | Pin to screen (From screen) |
| `-PinToScreenFromClipboard` | Pin to screen (From clipboard) |
| `-PinToScreenFromFile` | Pin to screen (From file) |
| `-PinToScreenCloseAll` | Pin to screen (Close all) |
| `-ImageEditor` | Image editor |
| `-ImageBeautifier` | Image beautifier |
| `-ImageEffects` | Image effects |
| `-ImageViewer` | Image viewer |
| `-BackgroundRemover` | Background remover |
| `-ImageComparer` | Image comparer |
| `-IconConverter` | Icon converter |
| `-ImageCombiner` | Image combiner |
| `-ImageSplitter` | Image splitter |
| `-ImageResizer` | Image resizer |
| `-ImageConverter` | Image converter |
| `-ImageWatermark` | Image watermark |
| `-ImageThumbnailer` | Image thumbnailer |
| `-VideoConverter` | Video converter |
| `-VideoTrimmer` | Video trimmer |
| `-VideoThumbnailer` | Video thumbnailer |
| `-AnalyzeImage` | Analyze image |
| `-OCR` | OCR |
| `-QRCode` | QR code |
| `-QRCodeDecodeFromScreen` | QR code (Scan screen) |
| `-QRCodeScanRegion` | QR code (Scan region) |
| `-HashCheck` | Hash checker |
| `-Metadata` | Metadata |
| `-StripMetadata` | Strip metadata |
| `-IndexFolder` | Directory indexer |
| `-ClipboardViewer` | Clipboard viewer |
| `-BorderlessWindow` | Borderless window |
| `-ActiveWindowBorderless` | Make active window borderless |
| `-ActiveWindowTopMost` | Make active window top most |
| `-InspectWindow` | Inspect window |
| `-NetworkMonitor` | Network monitor |
| `-MonitorTest` | Monitor test |
| `-AnimatedGifTrimmer` | Animated GIF trimmer |

### Other

| Command | Description |
| --- | --- |
| `-DisableHotkeys` | Disable/Enable hotkeys |
| `-OpenMainWindow` | Open main window |
| `-OpenScreenshotsFolder` | Open screenshots folder |
| `-OpenHistory` | Open history window |
| `-OpenImageHistory` | Open image history window |
| `-ToggleActionsToolbar` | Toggle actions toolbar |
| `-ToggleTrayMenu` | Toggle tray menu |
| `-ExitShareX` | Exit ShareX |


## Linux platform notes

* **Screen capture** (`-PrintScreen`, `-ActiveWindow`, `-ActiveMonitor`, `-RectangleRegion`,
  `-CustomRegion`, `-CustomWindow`, `-LastRegion`) works through the backend that matches
  your session: `grim`/wlr-screencopy on wlroots (Hyprland, sway), the org.freedesktop.portal
  `Screenshot` portal on GNOME/KDE, `XGetImage` on X11. Window-relative captures additionally
  need the compositor's window list (Hyprland/sway IPC, X11) — see the support matrix in
  `README.linux.md`.
* **Screen recording** (`-ScreenRecorder*`, `-StopScreenRecording`, `-PauseScreenRecording`,
  `-AbortScreenRecording`) needs `ffmpeg`, and for hardware encoding `gpu-screen-recorder`
  (wlroots) or the ScreenCast portal (GNOME/KDE).
* **Clipboard actions** (`-ClipboardUpload`, `-ClipboardUploadWithContentViewer`,
  `-PinToScreenFromClipboard`) need `wl-clipboard` on Wayland or `xclip` on X11.
* **Input-dependent tools** (`-MouseHighlighter`, `-ActiveWindowTopMost`,
  `-ActiveWindowBorderless`, `-BorderlessWindow`) are limited by the compositor: a Wayland
  client cannot move or raise other applications' windows, so these either use compositor
  rules (Hyprland/sway) or report that the action is unavailable.
* **Scrolling capture and auto capture** need synthetic input (portal RemoteDesktop or X11
  XTest) plus a working capture backend; on Wayland without a portal supporting RemoteDesktop
  they will report that they are unavailable.
* **Hotkey-related actions** (`-DisableHotkeys`, `-ToggleTrayMenu`) act on the running
  instance; binding keys is done by your compositor/desktop (see `README.linux.md`).

## Examples

```bash
# Capture a region and exit when the task is done (scripting / cron)
sharex -AutoClose -RectangleRegion

# Capture the active window and upload it with a saved task profile
sharex -task "Upload to my server" -ActiveWindow

# Start/stop a recording from a key binding
sharex -ScreenRecorder        # start
sharex -StopScreenRecording   # stop (or press again for the toggle form)

# Upload files and URLs
sharex /home/me/Pictures/shot.png
sharex https://example.com/file.zip

# Import a custom uploader from the file manager
sharex -CustomUploader ~/Downloads/my-host.sxcu

# Run a workflow by name
sharex -workflow "OCR and copy"
```

### Hyprland (`~/.config/hypr/hyprland.conf`)

```ini
# Screenshots
bind = , PRINT,         exec, sharex -RectangleRegion
bind = SUPER SHIFT, S,  exec, sharex -RectangleRegion
bind = SUPER, S,        exec, sharex -PrintScreen
bind = SUPER SHIFT, W,  exec, sharex -ActiveWindow
bind = SUPER SHIFT, M,  exec, sharex -ActiveMonitor
bind = SUPER SHIFT, L,  exec, sharex -LastRegion
bind = SUPER SHIFT, C,  exec, sharex -ScrollingCapture

# Screen recording
bind = SUPER SHIFT, R,  exec, sharex -ScreenRecorder
bind = SUPER ALT, R,    exec, sharex -ScreenRecorderGIF
bind = SUPER SHIFT, X,  exec, sharex -StopScreenRecording

# Tools
bind = SUPER SHIFT, O,  exec, sharex -OCR
bind = SUPER SHIFT, Q,  exec, sharex -QRCodeScanRegion
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

Global shortcuts are configured in *System Settings → Keyboard → Shortcuts → Add → Command
or Script*. Add one entry per action with the command `sharex -RectangleRegion`,
`sharex -PrintScreen`, `sharex -ScreenRecorder`, … and assign a key. Plasma also exposes
these through `kglobalaccel`, so they can be scripted with `kwriteconfig6`/`qdbus6` if you
prefer configuration files. ShareX can additionally register through the
`org.freedesktop.portal.GlobalShortcuts` portal on Plasma 6, in which case the keys are
configured from ShareX's hotkey settings instead.

### GNOME

```bash
# One-off: add three custom shortcuts to the media-keys schema
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

GNOME also offers the `org.freedesktop.portal.GlobalShortcuts` portal (GNOME 48+), which
ShareX can use directly from its hotkey settings.

### X11 sessions

On an X11 session ShareX registers its own global hotkeys (`XGrabKey`) from the Hotkeys
window, and for tools that need it, uses `xdotool`/`xclip`. No compositor configuration is
required.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Command handed over (or finished, with `-AutoClose`) |
| `1` | Startup failure (missing display, unhandled exception — see the log) |
| `127` | Launcher could not find the ShareX executable |

Logs (including the parsed startup flags and any action errors) are written to
`ShareX-Log-<yyyy-MM>.txt` inside ShareX's `Logs` folder; see `README.linux.md` for the
location on Linux.
