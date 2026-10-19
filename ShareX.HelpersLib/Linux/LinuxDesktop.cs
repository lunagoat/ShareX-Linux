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

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

public static class LinuxDesktop
{
    public static string FindCommand(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(Path.IsPathFullyQualified).Select(directory => Path.Combine(directory, command))
            .FirstOrDefault(path => File.Exists(path) && (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);
    }

    public static async Task<(bool Success, string Output)> RunAsync(string command, string[] arguments, int timeout = 5000, CancellationToken cancellationToken = default, string standardInput = null)
    {
        string executable = FindCommand(command);
        if (executable == null) return (false, $"{command} is not installed.");
        using Process process = new();
        process.StartInfo = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = standardInput != null };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            if (standardInput != null)
            {
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                return (false, $"{command} was cancelled or timed out.");
            }
            string stdout = await output.ConfigureAwait(false), stderr = await error.ConfigureAwait(false);
            return (process.ExitCode == 0, process.ExitCode == 0 ? stdout.Trim() : stderr.Trim());
        }
        catch (Exception ex) { DebugHelper.WriteException(ex); return (false, ex.Message); }
    }

    public static bool Open(string target)
    {
        // Absolute filenames cannot be mistaken for xdg-open switches. No shell parsing.
        var result = RunAsync("xdg-open", new[] { target }).GetAwaiter().GetResult();
        if (!result.Success) DebugHelper.WriteLine("Unable to open: " + result.Output);
        return result.Success;
    }

    public static bool ShowItem(string path)
    {
        string uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        string uris = "['" + uri.Replace("'", "\\'") + "']";
        var result = RunAsync("gdbus", new[] { "call", "--session", "--dest", "org.freedesktop.FileManager1",
            "--object-path", "/org/freedesktop/FileManager1", "--method", "org.freedesktop.FileManager1.ShowItems", uris, "" }).GetAwaiter().GetResult();
        return result.Success || Open(Path.GetDirectoryName(Path.GetFullPath(path)));
    }
}
