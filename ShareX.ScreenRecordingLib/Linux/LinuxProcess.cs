// ShareX - GPL v3. See LICENSE.txt.
using System.Diagnostics;

namespace ShareX.ScreenRecordingLib;

internal static class LinuxProcess
{
    internal static string Find(string name)
    {
        if (Path.IsPathRooted(name)) return name;
        return (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':')
            .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists) ?? name;
    }

    internal static Process Start(string executable, IEnumerable<string> arguments, bool input = false)
    {
        ProcessStartInfo info = new(Find(executable))
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
            RedirectStandardInput = input, CreateNoWindow = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException($"Could not start {executable}.");
    }

    internal static async Task<string> Run(string executable, params string[] arguments)
        => await Run(TimeSpan.FromSeconds(30), executable, arguments).ConfigureAwait(false);

    internal static async Task<string> Run(TimeSpan limit, string executable, params string[] arguments)
    {
        using Process process = Start(executable, arguments);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(limit);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        string message = await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new IOException($"{executable} exited with {process.ExitCode}: {message}");
        return await output.ConfigureAwait(false);
    }
}
