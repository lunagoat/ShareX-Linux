# Shared core bugs and performance audit

Report name: **core**. Date: **2026-10-20**.

Read `docs/LINUX_PORT.md` and `docs/COMPATIBILITY.md` first. Changes are confined to
this clone. No real Wayland/X11 display, application window, overlay, desktop
capture, live upload service, or network test server was opened. HTTP fixtures use
in-memory message handlers; restore was the only permitted network operation.
Files, logs, and test databases use private directories under `/tmp`.

## Changes

1. **`ShareX.HelpersLib/FileDownloader.cs`** — Download unknown-length and empty responses, reject premature EOF, pass cancellation through HTTP/header/body operations, return false on cancellation, and serialize start/stop state. Before the fix, six regression cases failed: missing/zero Content-Length skipped downloads, early EOF repeatedly read zero bytes, cancellation could not interrupt headers/body, and cancellation from the size event reported success. Seven downloader tests now pass. Estimated effect: removes skipped downloads, an EOF busy loop, and indefinite waits after an explicit stop; canceled partial outputs are removed, while cancellation before output creation preserves an existing file.
2. **`ShareX.HelpersLib/Settings/SettingsBase.cs`** — Serialize destination/version changes with the save, report each operation's actual destination, recover existing GUID-named temporary saves newest-first, and remove temporary files after failed saves. Three original regressions reproduced wrong concurrent-save notifications, failed interrupted-save recovery, and orphan temporary files. Backup round trips and invalid-path fallback also pass. Estimated effect: prevents destination races on one settings instance, restores crash recovery, and avoids accumulation of failed temporary writes. Existing atomic replacement and backup behavior remain in use.
3. **`ShareX.HelpersLib/Helpers/FileHelpers.cs`** — Parse numeric filename suffixes with invariant `long.TryParse`, preserve unrepresentable suffixes, and guard increment overflow. A file named `photo (999999999999999999999999).png` previously threw `OverflowException`; maximum and consecutive-maximum suffixes are covered too. Estimated effect: unique-name generation succeeds for these inputs without negative wraparound; normal `(3)` to `(4)` behavior is preserved.
4. **`ShareX.HelpersLib/Helpers/WebHelpers.cs` — server filenames** — Read typed Content-Disposition parameters, prefer decoded `filename*`, strip both kinds of path separators, sanitize the basename, and reject dot/dot-dot names. Six of seven header cases failed before the fix, including `filename=plain.txt`, bare `attachment`, UTF-8 names, and traversal names. Estimated effect: prevents header substring exceptions and returning server-controlled paths; ordinary quoted names still work.
5. **`ShareX.HelpersLib/Helpers/WebHelpers.cs` — file downloads** — Request `ResponseHeadersRead` when copying a response to disk. The 8 MiB fixture previously allocated **8,421,000 managed bytes** and buffered the entire body; afterward it allocated **2,280 bytes** and streamed directly, with exact output length verified. This removes the additional allocation proportional to file size. Figures exclude the fixture's preallocated payload and are managed allocation measurements, not peak RSS or network throughput; actual transport buffering differs.
6. **`ShareX.HelpersLib/Zip/ZipManager.cs`** — Reject entries whose output file or parent directories below the destination are existing symlinks/reparse points; normalize trailing destination separators. Before the fix, the fixture created a file outside the destination through a directory link and overwrote an outside file through a file link. Link, lexical traversal, sibling-prefix, safe-entry, and trailing-separator checks now pass. Estimated effect: prevents these existing-link extraction escapes, at the cost of metadata checks along each entry path. Concurrent filesystem mutation and hardlinks remain limitations, listed below.
7. **`ShareX.UploadersLib/BaseUploaders/Uploader.cs`, `ShareX/TaskManager.cs`** — Replace persistent HTTP error dumps with exception type/status and remove the task pipeline's duplicate full-error dump. All five synthetic path/query/cookie/body/redirect secrets reached the log before the fix; the base logger regression now excludes them while retaining HTTP 401 and detailed in-memory UI errors. Estimated effect: prevents these credential-bearing error details from entering persistent logs and reduces error-log size. The TaskManager call site was verified by source review and the full build, without invoking its UI-dependent completion flow.
8. **`ShareX.UploadersLib/OAuth/OAuthManager.cs`** — Encode parameter names and values before sorting with ordinal comparison. The previous output included unescaped names and culture-dependent ordering; tests now verify exact encoded ordering under en-US, tr-TR, and sv-SE. Estimated effect: correct OAuth signatures for these parameter sets; no throughput claim. Existing RSA-SHA1 signing passes independent signature verification and was left unchanged.
9. **`tests/ShareX.Core.Tests/`** — Add a standalone console project, runner/fake transport in `Program.cs`, regression files `SettingsTests.cs`, `UploaderTests.cs`, `FileAndZipTests.cs`, `OAuthTests.cs`, `WebHelpersTests.cs`, review fixtures in `ReviewTests.cs`, and `README.md`. It is added to no solution or other project. **33 tests pass**, including uploader source ownership/ranges/cancellation/timeouts, keyed HTTP-client reuse, and 20 concurrent SQLite history appends with a Thai-culture date/tag round trip. Optional `--findings` runs bounded reproductions of remaining findings. Effect: offline regression coverage; no production behavior change.

This report, `docs/status/perf-core.md`, records the changes, evidence, remaining
findings, and validation limits.

## Findings left unchanged

“Reproduced” means an executed offline fixture demonstrated the behavior.
“Static/unproven” means source evidence identifies a concern, but this audit did
not establish its runtime impact with a failing test. These are not claims of
reproduced crashes or measured leaks.

1. **Reproduced — directory-link recursion (`ShareX.Tools/Tools/DirectoryIndexer/Indexer.cs`).** A directory containing `loop -> itself` indexes the same leaf **six times** with a depth limit of six. Unlimited traversal has no ancestor-cycle check. Left unchanged: cycle handling needs dedicated traversal tests covering legitimate directory aliases and all index output modes; the bounded reproducer is retained under `--findings`.
2. **Reproduced — emoji-preserving URL encoding (`ShareX.HelpersLib/Helpers/URLHelpers.cs`).** Non-emoji scalar `𝔸` becomes `%EF%BF%BD%EF%BF%BD` instead of `%F0%9D%94%B8`. Repeated suffix substrings also allocated **4,629,032 bytes for 2,048 ASCII characters** and **17,646,192 for 4,096**, consistent with quadratic allocation. Left unchanged: a separate scalar/emoji-sequence parser change needs dedicated sequence coverage rather than modifying its matching behavior in this patch.
3. **Reproduced — existing OAuth query parameters (`ShareX.UploadersLib/OAuth/OAuthManager.cs`).** `GenerateQuery("https://unit.invalid/resource?existing=1", ...)` drops `existing=1`. Left unchanged: preserving and signing existing queries correctly also requires duplicate-parameter semantics beyond the current dictionary API.
4. **Reproduced — unexpected service response parsing (`ShareX.UploadersLib/URLShorteners/BitlyURLShortener.cs`).** An HTTP 200 body `<html>maintenance</html>` escapes `ShortenURLAsync` as `JsonReaderException`. Similar unguarded JSON/XML parsing exists in legacy uploaders such as ImageShack, but those services were not exercised. Left unchanged: service parsing needs dedicated error-contract fixtures and consistent translation to uploader errors.
5. **Static/unproven — OCR process/resource lifetime (`ShareX.Tools/Tools/OCR/OCRHelper.cs`).** Language discovery performs synchronous `ReadToEnd` before its timed wait and does not drain redirected stderr; OCR has no cancellation/deadline, starts draining output after writing input, and does not explicitly dispose the temporary `SKImage` used for encoding. Left unchanged: needs fake-process/backpressure and native-resource lifetime tests; no hang or leak measurement was claimed.
6. **Static/unproven — uploader stop/disposal race (`ShareX.UploadersLib/BaseUploaders/Uploader.cs`).** `StopUpload` captures a cancellation source under a lock, cancels it outside the lock, and operation completion can dispose it between those steps. Left unchanged: normal cancellation passes, but a controlled scheduling reproduction is needed before changing cancellation ownership.
7. **Static/unproven — multiple settings instances (`ShareX.HelpersLib/Settings/SettingsBase.cs`).** Save locking remains per instance, so two instances writing the same new path can still race between existence checking and move/replace, and backups are not serialized by destination. Left unchanged: needs deterministic cross-instance fixtures and an explicit shared-path lock lifetime policy. Atomic writes prevent the original truncation concern; this patch fixes the reproduced single-instance race.
8. **Static/unproven — shared task collections (`ShareX/TaskManager.cs`, `ShareX/RecentTaskManager.cs`).** Public mutable lists/queues can be enumerated outside the writers' synchronization. Most worker events marshal through a captured context, so an ordinary UI-thread race was not demonstrated. Left unchanged: needs controlled dispatcher/CLI scheduling tests before changing collection ownership or public APIs. `ImageFilesCache`'s inspected caller uses a local scoped cache, so its unlocked dictionary was not treated as a proven concurrency bug.
9. **Static/unproven — watch-folder callback exceptions (`ShareX/Linux/WatchFolder.cs`, `ShareX/WatchFolderManager.cs`).** The posted callback invokes subscribers outside the polling method's catch block; subscriber `File.Move` or upload-start failures can escape through the dispatch context. Left unchanged: requires dispatcher and filesystem-failure fixtures and a defined user-visible error path.
10. **Static/unproven — late preview completion (`ShareX.Tools/Tools/ImageCombiner/ImageCombinerViewModel.cs`, `ShareX.Tools/Tools/QRCode/QRCodeViewModel.cs`).** Dispose releases the current preview without invalidating an outstanding async generation, which may subsequently assign another bitmap. Left unchanged: needs controlled completion/disposal tests and a view-model lifetime contract; no native bitmap leak was measured.
11. **Static/unproven — ffmpeg filename quoting (`ShareX.Tools/Tools/VideoConverter/VideoConverterViewModel.cs`).** Arguments interpolate filenames inside quotes; a Unix filename containing a quote, such as `a" -an "b.mp4`, can alter argument boundaries. Left unchanged: needs an argv-recording fake launcher and validation of the conversion handler's argument contract. No ffmpeg process was launched for this audit.
12. **Static/unproven — remaining sensitive diagnostics (`ShareX.HelpersLib/Helpers/URLHelpers.cs`, `ShareX/TaskManager.cs`, `ShareX.UploadersLib/FileUploaders/BackblazeB2.cs`).** Opened/successful URLs and some service-specific response diagnostics are still logged; signed or capability URLs could disclose secrets. Left unchanged: these separate paths need their own fixtures and a consistent URL/response logging policy. This patch removes HTTP error dumps and their duplicate task-failure logging, not every diagnostic containing a URL.
13. **Static/unproven — deadlines and proxy changes (`ShareX.HelpersLib/FileDownloader.cs`, `ShareX.HelpersLib/HttpClientFactory.cs`, `ShareX.UploadersLib/BaseUploaders/Uploader.cs`).** A downloader body still has no automatic deadline, uploader defaults intentionally allow infinite request time, and factory reset/proxy changes dispose clients that may have active requests. Left unchanged: cancellation now works in the reproduced downloader cases; changing default deadlines or proxy-transition behavior needs a compatibility policy and active-request fixtures.
14. **Static/unproven — stronger extraction confinement (`ShareX.HelpersLib/Zip/ZipManager.cs`).** Metadata checks and file opening are separate operations; a concurrent directory/symlink swap could race them, and existing hardlinked files are not detected. Left unchanged: needs handle-relative, race-resistant filesystem operations or extraction into a private staging tree. The regression proves rejection of existing symlinks, not confinement against hostile concurrent mutation.

## Validation

Executed after the fixes, with display/session variables removed:

- `source scripts/linux/env.sh; dotnet build ShareX.sln -c Release` — **0 errors**, 50 existing/platform warnings in this run.
- `dotnet run --project tests/ShareX.Core.Tests -c Release` — **33 tests, 0 failures**.
- `dotnet run --project tests/ShareX.Core.Tests -c Release --no-build -- --findings` — produced the bounded evidence quoted above; no sockets or displays.
- `tests/ShareX.OverlayHost.Tests/run.sh` — **121 checks pass**, explicitly reports no display/window APIs invoked.
- `tests/ShareX.OverlayRender.Tests/run.sh` — **passes** retained/full-redraw equivalence, damage containment, scales, pixels, HUD, shapes, external bitmap, and no-op checks.
- `tests/ShareX.OverlayIntegration.Tests/run.sh --headless` — **passes**, including stop/crash/heartbeat fallback scenarios; explicitly reports no compositor connections or desktop capture.
- `dotnet run --project tests/ShareX.Compositor.Tests -c Release` — **0 failures**.
- `git diff --check` — **passes**.

An earlier concurrent test/solution build collided on Avalonia PDB output files;
subsequent checks completed successfully. No live uploader, GUI workflow, OCR
process/backpressure scenario, or real ffmpeg invocation was tested. Current-culture
formatting in user-facing name patterns was not indiscriminately replaced with
invariant formatting. Notification timers have stop calls on close; no runtime
timer leak was established. No production changes were needed in HistoryLib or
Tools for the fixes proved here.

## Commit limitation

The first per-change commit attempt failed at `git add`:

```text
fatal: Unable to create '.../.git/index.lock': Read-only file system
```

All changes are left **uncommitted** as instructed for a read-only `.git`. No
permission bypass, alternate git directory, push, release script, or writes to
`~/.local`/`~/.config` were attempted. No processes were killed by name. The
requested commit trailer would be:

```text
Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```
