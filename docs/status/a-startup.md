# a-startup — Startup, CLI, Settings UI, and Persistence

Summary of QA and fixes for the startup lifecycle, CLI argument forwarding, settings persistence, UI page audits, and tray/notification behavior on Linux.

## Status Summary

| Feature | Status | Verification | Known gaps |
|---|---|---|---|
| Startup & CLI command forwarding | works | Verified with isolated sandbox `/tmp/sx-a-startup-cli` executing running instance forwarding: `-PrintScreen`, `-ClipboardUpload`, file upload argument (`/tmp/.../test-upload.txt`), `-Silent`, `-NoHotkeys`, and `-exit`. All forwarded commands executed without timeout. | None. Forwarded file paths are resolved to absolute paths before transmission over the Unix domain socket. |
| Settings persistence round trip | works | Tested in isolated sandbox `/tmp/sx-a-startup-persistence`: Modified `ApplicationConfig.json`, `TaskSettings`, and `HotkeysConfig.json`; triggered save; exited cleanly via `-exit`; re-launched and verified that all modified fields persisted intact. | None. |
| Main window & Start screen | works | Rendered and visually audited both headlessly and live on Hyprland desktop. Captured live window via `hyprctl clients -j` and `grim`. Startup screen correctly suppresses blocking `ShowAsync()`. | None. |
| Application settings (all 12 pages) | works | Automated test suite in `tests/ShareX.Startup.Tests` rendered each page (General, Theme, Integration, Paths, Settings, Main window, Clipboard formats, Upload, History, Print, Proxy, Advanced) to PNG and audited visual tree for empty content and Windows-only wording. | Windows Explorer context menu toggles and Windows print dialog options are hidden on Linux via `WindowsIntegrationVisible` (`OperatingSystem.IsWindows()`). |
| Task settings (all 14 pages, default & custom) | works | Verified both default and custom task configs across all sections (Task, Notifications, Effects, Thumbnail, Region capture, Screen recorder, OCR, File naming, Clipboard upload, Uploader filters, Image editor, Mouse highlighter, Actions, Watch folders, Advanced). | Windows-only options (capture transparency, GDI controls) are hidden on Linux. |
| Hotkey settings & Linux bindings | works | Verified hotkey table, add/remove/edit actions, reset dialog binding fallback, and Linux shortcut configuration window (`LinuxHotkeyBindingWindow`) generating Hyprland and sway `bind` statements. | Global shortcuts on Wayland require compositor bindings or XDG desktop portal shortcut approvals. |
| Uploaders destination settings (all 48 pages) | works | Rendered all 48 service pages across 4 categories (Image uploaders, Text uploaders, File uploaders, URL shorteners). 0 blank pages, 0 exceptions. | Real uploads to remote services require valid third-party API credentials. |
| Custom uploader settings & About windows | works | Rendered and verified `CustomUploaderSettingsWindow` and `AboutWindow` without errors or clipping. | None. |
| Tray icon (FreeDesktop StatusNotifierItem) | works | Uses Avalonia `TrayIcon` registered via `DesktopServices.RegisterTrayIcon`, supporting SNI on Wayland/X11 with native tray context menu. | Requires an active desktop notification area / system tray bar (e.g. waybar) to display icon visually. |
| Notifications (Native & Avalonia fallback) | works | `NativeNotificationPresenter` invokes `LinuxNotifications` (`notify-send` / `org.freedesktop.Notifications`) with actions and thumbnail icon; gracefully falls back to Avalonia `NotificationWindow`. | Notification action execution depends on compositor/daemon action support in `notify-send`. |

---

## Root Causes Found & Addressed

1. **CLI Argument Forwarding Timeout Bug (`SingleInstanceCommandRouter`)**:
   - **Root Cause**: In `ShareX/Program.cs`, when `ShowStartScreen` was true (the default on fresh launch), `await startScreen.ShowAsync()` ran before `hotkeyHost.Initialize()`, `MainWindowIntegration.Initialize()`, and `SingleInstanceCommandRouter.MarkReady()`. Because `ShowAsync()` waited for the window to close, `MarkReady()` was never called during startup, causing any secondary instance forwarding arguments via Unix domain socket to time out after 5 seconds with `"Arguments were not processed because application startup did not complete within 5 seconds"`.
   - **Fix**: Reordered `StartApplicationAsync` to initialize the hotkey host, runtime settings, main window, and mark `SingleInstanceCommandRouter.MarkReady()` immediately. `StartScreenWindow` is now displayed modelessly (`Show()`) and is bypassed completely during silent launches or when action commands are supplied on the command line.
   - **CLI Enhancements**: Added `HasActionCommands` check to `ShareXCLIManager` and `ApplicationCommandLine` to ensure that action jobs execute without activating the main window if running silently. Updated `SingleInstanceManager.ForwardAsync` to convert relative file arguments to absolute paths prior to sending them across the IPC socket.

2. **Uninitialized `Hotkeys` Collection in `HotkeyManager`**:
   - **Root Cause**: `HotkeyManager.Hotkeys` was null until `UpdateHotkeys()` completed. Opening `HotkeySettingsWindow` or invoking `RegisterFailedHotkeys()` prior to `UpdateHotkeys()` threw an `ArgumentNullException`.
   - **Fix**: Initialized `Hotkeys` to `new()` by default and added null guards to `RegisterAllHotkeys()`, `RegisterFailedHotkeys()`, and `UnregisterAllHotkeys()`.

3. **`HotkeySettingsWindow` Modal Fallback Binding**:
   - **Root Cause**: In `HotkeySettingsWindow.axaml`, `<Border IsVisible="{Binding IsResetConfirmationVisible}" ...>` defaulted to visible when `DataContext` was not yet set, causing the "Reset hotkeys?" overlay to block the hotkey list.
   - **Fix**: Added `FallbackValue=False` to the `IsResetConfirmationVisible` binding.

4. **UI Audit and Automated Regression Test Suite**:
   - Created `tests/ShareX.Startup.Tests` which initializes Avalonia headlessly, walks the complete navigation hierarchy of Application Settings (12 pages), Task Settings (14 pages), Hotkey Settings, Destination Settings (48 pages), Custom Uploader Settings, About, Linux Hotkey Bindings, and Toast Notifications, saves high-resolution PNG renders to `/tmp/sx-a-startup-ui/screenshots/`, and verifies no visual elements are blank or leaking Windows-only text.

---

## How to Verify

- **Build**:
  ```bash
  source scripts/linux/env.sh
  scripts/linux/build.sh
  ```
  Expected output: `0 Error(s)`

- **UI Audit Suite (renders 92 screenshots and validates all pages)**:
  ```bash
  source scripts/linux/env.sh
  SX_SANDBOX=/tmp/sx-a-startup-ui dotnet run --project tests/ShareX.Startup.Tests
  ```
  Expected output: `Failures: 0`, `Windows-only wording warnings: 0`

- **CLI Forwarding Tests**:
  Launch ShareX in background with sandbox, then test running second instances with `-PrintScreen`, `-ClipboardUpload`, a file argument, `-Silent`, `-NoHotkeys`, and `-exit`.
