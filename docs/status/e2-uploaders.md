# e2-uploaders — Upload pipeline, uploaders, and history on Linux

QA and fixes for the upload pipeline, custom uploaders (.sxcu), storage backends, task automation, external programs/actions, after-upload tasks, history window, and path/naming handling on Linux/Wayland.

## Status Summary

| Feature | Status | Verification | Known gaps |
|---|---|---|---|
| Integration check suite (`ShareX.Integration.Tests`) | works | Executed `SX_SANDBOX=/tmp/sx-wp6-tests dotnet run --project tests/ShareX.Integration.Tests` headless; verified all 17 sub-suites pass (settings, custom .sxcu multipart/JSON/XML/regex, file/text task pipeline, S3 signed PUT, Actions quoting/Unicode/metachars, watch folders, SQLite concurrent read/write, OAuth loopback, FTP passive, ownCloud WebDAV, SFTP key auth, native messaging manifests and framing, image effects/watermark/editor, cursor annotations, video thumbnails, image->effects->save->upload->history pipeline). | None. |
| ThreadWorker & WorkerTask crash resilience | works | Wrapped `ThreadWorker.WorkThread` in try-catch-finally so background threads never silently swallow crashes without notifying completion listeners. Added unhandled exception logging to `WorkerTask.ThreadDoWork`. | None. |
| Custom uploader (.sxcu multipart, JSON, regex, JSONPath) | works | Verified end-to-end via integration checks and local fake HTTP server with multipart form-data, JSON body, regex pattern extraction, and JSONPath extraction. | Remote upload services require credentials; verified offline with local mock server. |
| After-upload tasks (wl-paste URL copy & notifications) | works | Verified URL copied to Wayland clipboard via `wl-paste` check, and notification sent via FreeDesktop/notify-send. | Active notification daemon needed for visual toast. |
| History entry creation & History window | works | Verified SQLite History.db entries created with URLs, filenames, timestamps, and types. HistoryWindow instantiated and rendered. | None. |
| Watch folders | works | Verified folder monitoring, debounce, and automatic pipeline upload on file creation. | None. |
| Drag/drop-equivalent CLI upload | works | Verified CLI argument forwarding (`run.sh /path/to/file`) to running daemon instance triggering background upload. | None. |
| External programs / Actions with spaces in path | works | Verified execution of script located in directory with spaces, argument quoting (`$input`, `$output`), and modification of payload before upload. | None. |
| Storage uploaders (FTP, SFTP, WebDAV, S3) | works | Verified FTP passive transfer, rootless SFTP with SSH keys, ownCloud WebDAV PUT, and S3 AWS4-HMAC-SHA256 signing against local endpoints. | None. |
| OAuth loopback listener & xdg-open handling | works | Verified local loopback listener binding, xdg-open launch stub, and auth code callback flow. | None. |
| File-name and path patterns (%y-%m-%d, '/' separators) | works | Verified token substitution, case-sensitive Linux paths, and '/' directory separators. | None. |

