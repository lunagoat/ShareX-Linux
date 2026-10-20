#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System.Diagnostics;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>Bounded processes with separate argument vectors; never invokes a shell.</summary>
public static class LinuxCaptureProcess
{
    public static Process Start(string executable, params string[] arguments)
    {
        ProcessStartInfo info = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException($"Cannot start {executable}.");
    }

    public static byte[] Run(string executable, TimeSpan timeout, params string[] arguments)
    {
        using Process process = Start(executable, arguments);
        using MemoryStream output = new();
        using CancellationTokenSource timeoutCts = new(timeout);
        // Every pipe operation must share the deadline: WhenAll otherwise waits for
        // stdout EOF even after the process wait has timed out.
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeoutCts.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        Task completed = Task.WhenAll(copy, error, process.WaitForExitAsync(timeoutCts.Token));
        try
        {
            completed.GetAwaiter().GetResult();
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            process.WaitForExit();
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}: {error.GetAwaiter().GetResult().Trim()} {System.Text.Encoding.UTF8.GetString(output.ToArray()).Trim()}");
        return output.ToArray();
    }

    public static byte[] Run(string executable, params string[] arguments) =>
        Run(executable, TimeSpan.FromSeconds(15), arguments);

    public static string Text(string executable, params string[] arguments) =>
        System.Text.Encoding.UTF8.GetString(Run(executable, arguments));

    public static string Text(string executable, TimeSpan timeout, params string[] arguments) =>
        System.Text.Encoding.UTF8.GetString(Run(executable, timeout, arguments));

    public static string? TryText(string executable, TimeSpan timeout, params string[] arguments)
    {
        try
        {
            return Text(executable, timeout, arguments);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? TryText(string executable, params string[] arguments) =>
        TryText(executable, TimeSpan.FromSeconds(3), arguments);

    public static string? FindExecutable(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
}
