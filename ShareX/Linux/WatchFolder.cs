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

using ShareX.HelpersLib;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;

public class WatchFolder : IDisposable
{
    public WatchFolderSettings Settings { get; set; }
    public TaskSettings TaskSettings { get; set; }
    public delegate void FileWatcherTriggerEventHandler(string path);
    public event FileWatcherTriggerEventHandler FileWatcherTrigger;

    private SynchronizationContext context;
    private FileSystemWatcher watcher;
    private CancellationTokenSource lifetime;
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.Ordinal);

    public virtual void Enable()
    {
        Dispose();
        string path = FileHelpers.ExpandFolderVariables(Settings.FolderPath);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        context = SynchronizationContext.Current ?? new SynchronizationContext();
        lifetime = new CancellationTokenSource();
        watcher = new FileSystemWatcher(path)
        {
            Filter = string.IsNullOrEmpty(Settings.Filter) ? "*" : Settings.Filter,
            IncludeSubdirectories = Settings.IncludeSubdirectories
        };
        CancellationToken token = lifetime.Token;
        // Linux writers often publish a file by creating, writing/modifying, or renaming it into the watched directory.
        watcher.Created += (_, e) => Queue(e.FullPath, token);
        watcher.Changed += (_, e) => Queue(e.FullPath, token);
        watcher.Renamed += (_, e) => Queue(e.FullPath, token);
        watcher.Error += (_, e) => DebugHelper.WriteException(e.GetException());
        watcher.EnableRaisingEvents = true;
    }

    private async void Queue(string path, CancellationToken token)
    {
        if (Directory.Exists(path) || !pending.TryAdd(path, 0)) return;
        try
        {
            long previousSize = -1;
            DateTime previousWrite = DateTime.MinValue;
            int stable = 0;
            for (int poll = 0; poll < 120 && stable < 4; poll++)
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                FileInfo info = new(path);
                if (!info.Exists) return;
                if (info.Length == previousSize && info.LastWriteTimeUtc == previousWrite && !FileHelpers.IsFileLocked(path)) stable++;
                else stable = 0;
                previousSize = info.Length;
                previousWrite = info.LastWriteTimeUtc;
            }
            if (stable >= 4 && !token.IsCancellationRequested)
            {
                context.Post(_ =>
                {
                    if (!token.IsCancellationRequested && File.Exists(path)) OnFileWatcherTrigger(path);
                }, null);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException exception) { DebugHelper.WriteException(exception); }
        catch (UnauthorizedAccessException exception) { DebugHelper.WriteException(exception); }
        finally { pending.TryRemove(path, out _); }
    }

    protected void OnFileWatcherTrigger(string path) => FileWatcherTrigger?.Invoke(path);

    public void Dispose()
    {
        watcher?.Dispose();
        watcher = null;
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
    }
}
