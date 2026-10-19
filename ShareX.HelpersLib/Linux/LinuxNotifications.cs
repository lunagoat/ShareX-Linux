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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

/// <summary>libnotify client for org.freedesktop.Notifications, including action signals.</summary>
public static class LinuxNotifications
{
    private static readonly CancellationTokenSource Shutdown = new();
    private static readonly ConcurrentDictionary<uint, byte> Active = new();
    public static async Task<bool> ShowAsync(string title, string text, string icon, int duration,
        IReadOnlyDictionary<string, string> actions, Action<string> actionInvoked)
    {
        string executable = LinuxDesktop.FindCommand("notify-send");
        if (executable == null || Shutdown.IsCancellationRequested) return false;
        using Process process = new();
        process.StartInfo = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "--app-name=ShareX", "--app-icon=sharex", "--print-id", "--wait", "--expire-time=" + Math.Max(1, duration),
            "--hint=string:desktop-entry:sharex" }) process.StartInfo.ArgumentList.Add(arg);
        if (!string.IsNullOrEmpty(icon)) { process.StartInfo.ArgumentList.Add("--icon"); process.StartInfo.ArgumentList.Add(icon); }
        foreach (var action in actions) process.StartInfo.ArgumentList.Add("--action=" + action.Key + "=" + action.Value);
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(title ?? "ShareX");
        // libnotify bodies support markup. Treat uploader/server text as plain text.
        process.StartInfo.ArgumentList.Add(System.Security.SecurityElement.Escape(text ?? ""));
        uint id = 0;
        try
        {
            process.Start();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token);
            deadline.CancelAfter(3000);
            string first = await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (!uint.TryParse(first, out id) || id == 0) return false;
            Active.TryAdd(id, 0);
            // Some servers ignore expiry. Keep the action owner alive, bounded to one hour.
            deadline.CancelAfter(TimeSpan.FromHours(1));
            string action = await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (action != null && actions.ContainsKey(action)) actionInvoked?.Invoke(action);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { return id != 0 || Shutdown.IsCancellationRequested; }
        catch (Exception ex) { DebugHelper.WriteException(ex); return false; }
        finally
        {
            if (id != 0)
            {
                Active.TryRemove(id, out _);
                await CloseAsync(id).ConfigureAwait(false);
            }
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); } }
            catch (InvalidOperationException) { }
        }
    }

    private static Task<(bool Success, string Output)> CloseAsync(uint id) => LinuxDesktop.RunAsync("gdbus", new[] {
        "call", "--session", "--dest", "org.freedesktop.Notifications", "--object-path", "/org/freedesktop/Notifications",
        "--method", "org.freedesktop.Notifications.CloseNotification", id.ToString(System.Globalization.CultureInfo.InvariantCulture) }, 2000);

    public static void CloseAll()
    {
        Shutdown.Cancel();
        Task.WhenAll(System.Linq.Enumerable.Select(Active.Keys, CloseAsync)).GetAwaiter().GetResult();
    }
}
