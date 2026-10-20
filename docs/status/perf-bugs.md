# Linux bug hunt: bugs

All fixes below were preceded by failing offline regressions against the original code. No display, overlay, real compositor, installation, release script, or external network service was used. Builds used the local SDK from `scripts/linux/env.sh`; NuGet auditing was disabled. This was a targeted review of the requested Linux paths, not a claim that every possible bug was eliminated.

Changes:

- `ShareX.HelpersLib/Linux/ActionCommand.cs`: expand placeholders in a single pass over the argument template. Repeated `Replace` calls were expanding placeholder-like text inside already substituted filenames. Concrete input `/tmp/a $output %input %output '雪'.png` failed both explicit-template and default-input checks. The fix preserves filenames literally, including spaces, quotes, Unicode and placeholder text. Effect: correct external-action arguments; no performance improvement claimed.
- `ShareX.Avalonia/Linux/LinuxCaptureProcess.cs`: apply the command deadline to stdout copying and stderr reading as well as process exit, observe all operations, and wait for the killed child to exit before disposing resources. Previously `Task.WhenAll` waited for stdout EOF even after the exit-wait task timed out. A local `sh -c 'sleep 2'` with a 100 ms deadline took 2,013 ms before the fix and 118 ms after it. Effect: stalled capture tools return near the configured deadline and their child process is reaped.
- `ShareX.HelpersLib/Linux/LinuxDesktop.cs`: start the deadline before stdin delivery, drain stdout/stderr concurrently, cancel writes and buffered flushes, and reap the child on failure. Previously writing 1 MiB to a child that never read stdin blocked before the deadline even existed. A 100 ms deadline took 2,024 ms before the fix and 104 ms after it. Effect: blocked input and caller cancellation are bounded; bidirectional commands avoid pipe deadlocks. The duplex regression sends 1 MiB while the child first emits 128 KiB and verifies every input byte arrives.
- `scripts/linux/build.sh`: enable pipeline failure detection and preserve the compiler's exit status separately from filtering/truncation. The original wrapper returned 0 when a fake compiler printed an error and exited 42; the fixed wrapper returns 42. Successful builds still succeed with normal output, no matching output, or more than 80 warning lines. Effect: callers can reliably detect failed builds.
- `tests/ShareX.Linux.Bugs.Tests/{ShareX.Linux.Bugs.Tests.csproj,Program.cs,scripts.sh}`: add standalone headless regressions referencing the production projects, plus a fake-SDK shell fixture. Cover literal argument expansion, deadlines, buffered stdin flushing, caller cancellation, full-duplex delivery, child reaping, successful output, failure diagnostics and wrapper status handling. Effect: regression coverage for all four fixes; no production behavior change.

Findings left unchanged (unconfirmed concerns, not additional proven bugs):

- `ShareX.HelpersLib/Linux/LinuxCommand.cs`, `ShareX.ScreenRecordingLib/Linux/LinuxProcess.cs`, and `ShareX.ScreenRecordingLib/Linux/ScreenRecorder.cs` retain complete command/encoder output in strings. Excessively verbose or indefinitely running tools could grow memory use. No representative recording/tool workload demonstrating harmful growth was reproduced; imposing arbitrary output limits could discard diagnostics or valid results.
- `ShareX.NativeOverlay/Wayland/WaylandOverlaySession.cs`, `ShareX.NativeOverlay/Wayland/Rendering.cs`, and `ShareX.NativeOverlay/OverlayEventQueue.cs` have unbounded work/discrete-event queues (consecutive pointer motion is coalesced). Rendering work also captures pixel arrays. Sustained producer/consumer imbalance could retain memory. No realistic backlog was reproduced, and real compositor connections were forbidden; event-dropping/backpressure changes need ordering and rendering evidence first.
- `ShareX/NativeOverlay/NativeRegionCapture.cs`: partial prewarm startup assigns the directory before assigning `Child`, while `PrewarmedHelper.Dispose` assumes a non-null child. A failure in that interval may mask the original error and skip directory cleanup. Left unchanged because no injected production startup-failure regression was built; reproducing via a real overlay was outside the permitted validation.
- `scripts/linux/install-user.sh` and `packaging/linux/sharex` scan `/proc` before signaling processes by executable path. PID reuse between inspection and signaling is a possible race. No deterministic reproduction was obtained. These routines were inspected only; no installation or launcher process-signaling path was executed.

Validation:

- `dotnet run --project tests/ShareX.Linux.Bugs.Tests -c Release -p:NuGetAudit=false`: passed, 11 checks and 0 failures (final measured deadlines: 118 ms capture, 104 ms desktop).
- `bash tests/ShareX.Linux.Bugs.Tests/scripts.sh`: passed compiler failure, success, empty-output and truncated-warning cases.
- `tests/ShareX.OverlayHost.Tests/run.sh`: passed, 121 checks.
- `tests/ShareX.OverlayRender.Tests/run.sh`: passed retained/full-redraw equivalence and all reported checks.
- `tests/ShareX.OverlayIntegration.Tests/run.sh --headless`: passed, exit 0; explicitly reported no compositor connections or desktop capture.
- `dotnet run --project tests/ShareX.Compositor.Tests -c Release -p:NuGetAudit=false`: passed, 0 failures.
- `dotnet build ShareX.sln -c Release -p:NuGetAudit=false --disable-build-servers -m:1 -p:UseSharedCompilation=false`: passed, 0 errors (60 warnings).
- `bash -n` on `install.sh`, `scripts/linux/*.sh`, `packaging/linux/sharex`, `packaging/linux/generate-icons.sh` and the new shell regression: passed. `git diff --check`: passed.

Each separate fix had a commit attempt using the requested trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. All attempts stopped at `git add`: `.git/index.lock` could not be created because `.git` is read-only. Changes remain uncommitted as authorized. No push was attempted.
