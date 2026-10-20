// ShareX Linux bug regressions. GPL-3.0-or-later. No display or desktop services required.
using System.Diagnostics;
using ShareX.HelpersLib.Linux;

int failures = 0;
void Check(bool ok, string name)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}");
    if (!ok) failures++;
}

string input = "/tmp/a $output %input %output '雪'.png";
string output = "/tmp/b $input %input $output.png";
string[] expanded = ActionCommand.Arguments("--input=\"$input\" '%input' \"$output\" %output ''", input, output).ToArray();
Check(expanded.SequenceEqual(new[] { "--input=" + input, input, output, output, "" }),
    "action substitution preserves placeholder-like text in filenames and empty arguments");
Check(ActionCommand.Arguments(null, input, output).Single() == input, "default action input remains literal");

// Use only child processes started by this test; no compositor tools are invoked.
var elapsed = Stopwatch.StartNew();
bool cancelled = false;
try
{
    ShareX.AvaloniaUI.Linux.LinuxCaptureProcess.Run("/bin/sh", TimeSpan.FromMilliseconds(100), "-c", "sleep 2");
}
catch (OperationCanceledException) { cancelled = true; }
elapsed.Stop();
Console.WriteLine($"capture timeout: {elapsed.ElapsedMilliseconds} ms");
Check(cancelled && elapsed.Elapsed < TimeSpan.FromSeconds(1.5), "capture deadline also bounds stdout copying");

elapsed.Restart();
var desktopResult = await ShareX.HelpersLib.LinuxDesktop.RunAsync("/bin/sh", ["-c", "sleep 2"],
    timeout: 100, standardInput: new string('x', 1024 * 1024));
elapsed.Stop();
Console.WriteLine($"desktop timeout: {elapsed.ElapsedMilliseconds} ms");
Check(!desktopResult.Success && desktopResult.Output.Contains("timed out") && elapsed.Elapsed < TimeSpan.FromSeconds(1.5),
    "desktop deadline bounds blocked stdin writes");

var captureBytes = ShareX.AvaloniaUI.Linux.LinuxCaptureProcess.Run("/bin/sh", TimeSpan.FromSeconds(2),
    "-c", "printf 'pixels'; printf 'diagnostic' >&2");
Check(System.Text.Encoding.UTF8.GetString(captureBytes) == "pixels", "capture success preserves stdout and drains stderr");
bool failed = false;
try { ShareX.AvaloniaUI.Linux.LinuxCaptureProcess.Run("/bin/sh", "-c", "printf 'bad tool' >&2; exit 7"); }
catch (InvalidOperationException ex) { failed = ex.Message.Contains("bad tool"); }
Check(failed, "capture failure still reports tool diagnostics");

elapsed.Restart();
var bufferedInput = await ShareX.HelpersLib.LinuxDesktop.RunAsync("/bin/sh", ["-c", "sleep 2"],
    timeout: 100, standardInput: new string('x', 9216));
Check(!bufferedInput.Success && bufferedInput.Output.Contains("timed out") && elapsed.Elapsed < TimeSpan.FromSeconds(1.5),
    "desktop deadline also bounds flushing buffered stdin");

var duplex = await ShareX.HelpersLib.LinuxDesktop.RunAsync("/bin/sh",
    ["-c", "head -c 131072 /dev/zero; wc -c"], timeout: 2000, standardInput: new string('x', 1024 * 1024));
Check(duplex.Success && duplex.Output.EndsWith("1048576"), "desktop drains output while delivering all stdin bytes");
using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
{
    elapsed.Restart();
    var result = await ShareX.HelpersLib.LinuxDesktop.RunAsync("/bin/sh", ["-c", "sleep 2"],
        timeout: 5000, cancellationToken: cancellation.Token, standardInput: new string('x', 1024 * 1024));
    Check(!result.Success && result.Output.Contains("cancelled") && elapsed.Elapsed < TimeSpan.FromSeconds(1.5),
        "desktop caller cancellation interrupts a blocked stdin write");
}

// A killed command must also be reaped before the runner returns.
string pidFile = Path.Combine(Path.GetTempPath(), "sharex-bug-child-" + Guid.NewGuid().ToString("N"));
try
{
    try
    {
        ShareX.AvaloniaUI.Linux.LinuxCaptureProcess.Run("/bin/sh", TimeSpan.FromMilliseconds(250),
            "-c", "printf '%s' $$ > \"$1\"; exec sleep 2", "fixture", pidFile);
    }
    catch (OperationCanceledException) { }
    Check(File.Exists(pidFile) && !Directory.Exists("/proc/" + File.ReadAllText(pidFile)), "capture timeout reaps the child process");
    File.Delete(pidFile);
    var result = await ShareX.HelpersLib.LinuxDesktop.RunAsync("/bin/sh",
        ["-c", "printf '%s' $$ > \"$1\"; exec sleep 2", "fixture", pidFile], timeout: 250,
        standardInput: new string('x', 1024 * 1024));
    Check(!result.Success && File.Exists(pidFile) && !Directory.Exists("/proc/" + File.ReadAllText(pidFile)),
        "desktop timeout reaps the child process");
}
finally { File.Delete(pidFile); }
Console.WriteLine($"RESULT: {failures} failures");
return failures == 0 ? 0 : 1;
