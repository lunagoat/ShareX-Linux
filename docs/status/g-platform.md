# Platform Integration Status (g-platform)

Platform integration QA and implementation status for native Linux port (C#/.NET 10/Avalonia).

| Feature | Status | Verification | Gaps / Notes |
| --- | --- | --- | --- |
| Settings encryption (DPAPI replacement, Secret Service / key file 0600, plaintext fallback) | in progress | Verified AES-GCM round-trip & key file 0600 in Platform.Tests | Need to verify plaintext backward-compatibility & live Secret Service |
| Desktop notifications (org.freedesktop.Notifications, actions / click-to-open) | in progress | Mock D-Bus verified in Platform.Tests | Need live busctl/dbus-monitor verification on desktop |
| Print (CUPS lp/lpstat, dialog, PDF generation) | in progress | Fake lp + Skia PDF verified in Platform.Tests | Need to check real lpstat/lp handling and print dialog |
| Wallpaper setting (Hyprland awww/swww/hyprpaper, gsettings/KDE, graceful degradation) | unverified | Code audit pending | Needs implementation/verification |
| Autostart .desktop write/remove | in progress | Unit test verified XDG autostart write/remove | Check sandbox integration |
| MIME registration for .sxcu/.sxie | in progress | Unit test verified | Check sandbox XDG dir integration |
| Open folder and select (FileManager1.ShowItems + xdg-open fallback) | in progress | Mock D-Bus verified | Check with Dolphin / live environment |
| Open URL / file (xdg-open) | in progress | Tested in Platform.Tests | Check quoting / edge cases |
| Portable mode (`-portable` folder collision fix) | missing | Code audit needed | Upstream uses 'ShareX' directory name beside executable which collides with 'ShareX' binary |
| Custom PersonalPath | in progress | Tested in Platform.Tests | Verify CLI and SystemOptions precedence |
| `-sandbox` CLI option | unverified | Code audit needed | Verify no persistence to disk |
| Update checker disabled | works | SystemOptions.DisableUpdateCheck is true; UI handles disabled state | Verified |
| Windows wording in Strings.resx | pending | Audit needed | Grep Strings.resx for Windows/Explorer/taskbar/registry and relabel |
| File name sanitising for Linux | in progress | Basic test passed | Verify illegal chars (: vs /) on Linux vs Windows |
| Environment.SpecialFolder empty fallbacks | in progress | Basic test passed | Audit all usages in codebase |
| App sandbox isolation (never touches real ~) | in progress | SX_SANDBOX sets XDG dirs | Verify no leaks to real ~ |
