# WP2 — Input, windows and clipboard

Window backend and persistent clipboard delivered. Work continues on hotkeys.

| Feature | Status | Verification | Known gaps |
|---|---|---|---|
| Hyprland window enumeration, active window, metadata, process, geometry, cursor | works | Console regression harness against live Hyprland; compositor fixtures for negative origins, 64-bit handles, workspace visibility and pinned windows | Uses bounded hyprctl calls; logical coordinates require capture backend scaling |
| sway window enumeration | partial | Native/XWayland/floating/client geometry fixtures | No sway session on machine; global pointer unavailable through sway IPC |
| X11 window enumeration/cursor | partial | Compiled native EWMH/XQueryPointer implementation | No Xorg session for live verification |
| WindowInfo public API | works | Existing callers compile; live enumeration through NativeMethods and WindowInfo | Style flags are compatibility properties; Wayland positioning/opacity/topmost require compositor rules; legacy System.Drawing Icon returns null |
| KDE/GNOME Wayland enumeration | missing | Unsupported backend returns an empty list | Compositors do not expose a standard enumeration API; KWin script backend pending |
| Clipboard | works | Live public API round trips: Unicode/newlines, file lists, GNOME copied-files, PNG alpha, MIME viewer snapshot, clear; original clipboard restored | wl-clipboard or xclip recommended; file copy advertises text/uri-list; GNOME-specific format is accepted on paste |
| Global shortcuts / CLI helper / synthetic input / cursor bitmap | missing | Source audit | Next implementation pass |

Decisions: opaque compositor IDs stay IntPtr; no Int32 truncation. Wayland geometry stays logical desktop coordinates. A missing global pointer reports false rather than using misleading XWayland coordinates. No external windows are moved or focused during verification.

Run: `source scripts/linux/env.sh; SX_SANDBOX=/tmp/sx-wp2 timeout 45 dotnet run --project tests/ShareX.Input.Linux.Tests -- --live`.

Git administrative directory is outside the allowed writable roots. Commits are held in `/tmp/sx-wp2/git` on `wp2-input`; final bundle will be `/tmp/sx-wp2/wp2-input.bundle`. Original worktree branch ref cannot be updated in this sandbox.
