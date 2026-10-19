# e2-uploaders — Upload pipeline, uploaders, and history on Linux

QA and fixes for the upload pipeline, custom uploaders (.sxcu), storage backends, task automation, external programs/actions, after-upload tasks, history window, and path/naming handling on Linux/Wayland.

## Status Summary

| Feature | Status | Verification | Known gaps |
|---|---|---|---|
| Integration check suite (`ShareX.Integration.Tests`) | works | Executed `SX_SANDBOX=/tmp/sx-wp6-tests dotnet run --project tests/ShareX.Integration.Tests` headless; verified all 17 sub-suites pass (settings, custom .sxcu multipart/JSON/XML/regex, file/text task pipeline, S3 signed PUT, Actions quoting/Unicode/metachars, watch folders, SQLite concurrent read/write, OAuth loopback, FTP passive, ownCloud WebDAV, SFTP key auth, native messaging manifests and framing, image effects/watermark/editor, cursor annotations, video thumbnails, image->effects->save->upload->history pipeline). | None. |
| ThreadWorker & WorkerTask crash resilience | works | Wrapped `ThreadWorker.WorkThread` in try-catch-finally so background threads never silently crash without notifying completion listeners. Added unhandled exception logging to `WorkerTask.ThreadDoWork`. | None. |
| Custom uploader (.sxcu multipart, JSON, regex, JSONPath) | works | Verified end-to-end through real task pipeline via `tests/e-uploaders-qa/verify_pipeline.py` (TEST 1, 2, 3, 4) against local fake HTTP server: multipart form-data, JSON body, regex extraction (`{regex:url=(.+)\|1}`), and JSONPath extraction (`{json:files[0].url}`). | Remote cloud services require API credentials; fully verified offline with local mock server. |
| After-upload tasks (wl-paste URL copy & notifications) | works | Verified URL copied to Wayland clipboard using `wl-paste` check, and notification dispatched via `notify-send` stub logging desktop notifications. | Requires active FreeDesktop notification daemon for visual toast display. |
| Clipboard image and text upload | works | Verified `wl-copy -t image/png` image clipboard upload, as well as text clipboard upload through task pipeline. | None. |
| URL shortener | works | Verified after-upload URL shortener using custom shortener config, resulting in shortened URL written to SQLite history and clipboard. | None. |
| Watch folders | works | Fixed `ShareX/Linux/WatchFolder.cs` by subscribing `watcher.Changed` to capture Linux inotify `IN_MODIFY` events (overwrites / truncations in addition to creates and atomic renames). Verified automatic detection, debounce, and pipeline upload. | None. |
| Drag/drop-equivalent CLI upload forwarding | works | Verified CLI argument forwarding (`run.sh /path/to/file`) to running daemon instance triggering background upload and History entry creation. | None. |
| External programs / Actions with spaces in path | works | Verified execution of script located in a directory path containing spaces (`.../path with spaces/action tools/stamp tool.sh`), `$input` and `$output` argument quoting, and modification of payload before upload. | None. |
| Storage uploaders (FTP, SFTP, WebDAV, S3) | works | Verified FTP passive transfer (EPSV/PASV) end-to-end through app pipeline; SFTP key auth with rootless OpenSSH server; ownCloud WebDAV PUT; and S3 AWS4-HMAC-SHA256 signing. Fixed backslash normalization in `LocalhostAccount.cs`. | None. |
| OAuth loopback listener & xdg-open handling | works | Verified local loopback listener binding, xdg-open launch stub with callback URL forwarding, and auth code parsing. | None. |
| File-name and path patterns (%y-%m-%d, '/' separators) | works | Verified token substitution (`%y`, `%mo`, `%d`), case-sensitive Linux paths, and '/' directory separators across image and file storage paths. | None. |
| History entry creation & History window | works | Verified SQLite History.db entries created with URLs, filenames, timestamps, and types. HistoryWindow launched on live Hyprland desktop, verified via `hyprctl clients -j`, and screenshot captured using `grim` showing rendered entries and previews. | None. |

---

## Detailed Test Verification

### 1. Integration Suite (`tests/ShareX.Integration.Tests`)
Run with `SX_SANDBOX=/tmp/sx-wp6-tests dotnet run --project tests/ShareX.Integration.Tests`:
- **PASS** settings windows and pages
- **PASS** custom .sxcu multipart/binary/JSON/XML/form uploads and JSONPath/XPath/regex parsing
- **PASS** file/text task pipeline -> local upload -> SQLite history
- **PASS** S3 signed PUT against local HTTP endpoint
- **PASS** Actions PATH lookup, quoting, Unicode, metacharacters and exit handling
- **PASS** watch folder create, atomic rename, empty file, filter and cancellation
- **PASS** SQLite concurrent read/write, edit, delete, reopen and semicolon filename
- **PASS** OAuth loopback bind-before-launch, invalid state, success, denial, cancellation and disposal
- **PASS** FTP passive transfer, Unicode filename and imported backslash subfolder
- **PASS** ownCloud WebDAV PUT against local HTTP server
- **PASS** SFTP key authentication and upload against isolated rootless OpenSSH
- **PASS** Chromium, Chrome and Firefox native messaging manifest registration/removal
- **PASS** native messaging binary stdin/stdout framing and subprocess payload handoff
- **PASS** image effects/watermark, editor open/load/export and centered image insertion
- **PASS** every cursor annotation type (Xcursor themes and bundled cursor assets)
- **PASS** video thumbnails through ffmpeg and Skia
- **PASS** image -> effects -> save -> upload -> history; delete-file task; URL download/reupload

### 2. End-to-End Pipeline Suite (`tests/e-uploaders-qa/verify_pipeline.py`)
Run against live Wayland/Hyprland session and isolated sandbox:
- **TEST 1 (Custom uploader file upload):** Uploaded a file using custom `.sxcu` multipart format. URL extracted via JSONPath, saved to SQLite history, copied to Wayland clipboard (`wl-paste`), and notified via `notify-send`.
- **TEST 2 (Clipboard image upload):** Populated Wayland clipboard with PNG image via `wl-copy -t image/png`. Ran `run.sh -ClipboardUpload` - ShareX correctly detected clipboard image format, uploaded to custom server, and updated clipboard with resulting URL.
- **TEST 3 (Clipboard text upload):** Populated Wayland clipboard with text snippet via `wl-copy`. Ran `run.sh -ClipboardUpload` - ShareX uploaded text using custom uploader.
- **TEST 4 (URL shortener):** Uploaded URL and piped through custom shortener `{json:short}`, verifying shortened URL in clipboard and history.
- **TEST 5 (Watch folders):** Spawned ShareX daemon with `WatchFolderList` active. Wrote a new file into the watched folder. Verified file watcher inotify debounce (1.25s) and automatic pipeline upload.
- **TEST 6 (Drag/drop CLI forwarding):** With daemon running, ran client `run.sh /path/to/file`. Confirmed single-instance CLI IPC message was routed to daemon and successfully uploaded.
- **TEST 7 (Actions with spaces in path):** Placed an action script inside `/path with spaces/action tools/stamp tool.sh`. Confirmed execution without escaping errors, modified input, and uploaded modified result.
- **TEST 8 (FTP Upload):** Uploaded binary file to local FTP server in passive mode (PASV/EPSV). Confirmed complete file reception.
- **TEST 9 (History GUI on Hyprland):** Launched History window via `-OpenHistory`. Inspected `hyprctl clients -j`, verified window presence `ShareX - History (<N> items)`, and captured rendered window with `grim`.

---

## Fixes Committed
1. **ThreadWorker & WorkerTask Exception Catching (`bd711ed`):**
   - Added `try-catch-finally` in `ThreadWorker.cs` to ensure `OnCompleted()` is always called even if an exception occurs, avoiding silent thread hangs.
   - Added error handling and logging to `WorkerTask.ThreadDoWork`.
   - Updated `MediaChecks.cs` with `FileExistAction.Overwrite` to avoid interactive prompts in automated runs.
2. **WatchFolder File Modifications (`2adc67d`):**
   - Subscribed `watcher.Changed` in `ShareX/Linux/WatchFolder.cs` so inotify `IN_MODIFY` events on Linux (from direct file writes or in-place overwrites) trigger the debounce queue.
3. **LocalhostAccount Cross-Platform Path Normalization (`297938e`):**
   - Normalized `SubFolderPath` in `LocalhostAccount.cs` by replacing Windows `\` with `Path.DirectorySeparatorChar` for local filesystem targets and `/` for HTTP URLs.
