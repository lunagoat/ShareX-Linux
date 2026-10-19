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
using System.Text;

namespace ShareX.Tools.Linux;

internal static class ToolProcess
{
    public static string Resolve(string name, string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException($"This tool requires '{name}' in PATH. Install it using your distribution's package manager.");
    }

    public static async Task<byte[]> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token = default,
        int timeoutSeconds = 30)
    {
        ProcessStartInfo info = new(Resolve(executable)) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        using MemoryStream output = new();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(copy, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
        string message = await error.ConfigureAwait(false);
        timeout.Token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} failed: {message.Trim()}");
        return output.ToArray();
    }

    public static string Read(string executable, params string[] arguments) =>
        Encoding.UTF8.GetString(RunAsync(executable, arguments, timeoutSeconds: 3).GetAwaiter().GetResult());
}
