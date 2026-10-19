# WP1 — platform core

First integration commit: XDG settings/data paths, profile-scoped Unix socket, file-based platform options, autostart and MIME/desktop integration.

| Feature | Status | Verification | Gaps |
| --- | --- | --- | --- |
| Startup, settings, clean exit | works | Full build: 0 errors. Sandbox `-silent -NoHotkeys -exit` returned 0, created all three JSON settings and History.db, log ends `ShareX closed.` | More UI and restart verification pending |
| Paths | works | Config in `/tmp/sx-wp1-platform/config/ShareX`, history/logs in `data/ShareX` | Portable/custom/sandbox assertions pending |
| Single instance | partial | Compiles; Unix socket with bounded JSON messages, 0600 endpoint and profile lock | Forwarding/recovery runtime tests pending |
| Desktop integration and autostart | partial | Linux implementations installed; app registration no longer invokes shell32 | Functional assertions pending |
| Encryption, notifications, printing, wallpaper, UI audit | partial | In progress | Runtime verification pending |

Decisions: default configuration is in `$XDG_CONFIG_HOME/ShareX`, data in `$XDG_DATA_HOME/ShareX`. Explicit portable or PersonalPath keeps everything in one folder. `--sandbox` retains upstream no-persistence semantics; `SX_SANDBOX` isolates persistent test profiles. Distribution updates are disabled and left to the package manager. No tests touch the real home/config/documents.

Dependency seam: `ShareX/Linux/StartupHotkeyHost.cs` temporarily implements IHotkeyHost lifetime only; WP2 must replace its construction in Forms/MainForm.cs with their real Linux host. This is necessary to run WP1 independently; it does not claim working global hotkeys. Native messaging registration expects WP6's built Linux `ShareX.NativeMessagingHost` beside the main app.
