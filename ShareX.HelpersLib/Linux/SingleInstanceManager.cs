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
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

/// <summary>Per-user, per-profile Unix socket with bounded argument forwarding.</summary>
public class SingleInstanceManager : IDisposable
{
    public event Action<string[]> ArgumentsReceived;
    public string MutexName { get; }
    public string PipeName { get; }
    public bool IsSingleInstance { get; }
    public bool IsFirstInstance { get; }
    private const int MaxMessageBytes = 1024 * 1024;
    private readonly CancellationTokenSource cancellation = new();
    private readonly FileStream instanceLock;
    private readonly Socket listener;
    private readonly string socketPath;
    private readonly Task listenTask;

    public SingleInstanceManager(string mutexName, string pipeName, string[] args) : this(mutexName, pipeName, true, args) { }

    public SingleInstanceManager(string mutexName, string pipeName, bool isSingleInstance, string[] args)
    {
        MutexName = mutexName;
        PipeName = pipeName;
        IsSingleInstance = isSingleInstance;
        if (!isSingleInstance) { IsFirstInstance = true; return; }
        string profile = LinuxPaths.CredentialDirectory ?? LinuxPaths.ConfigDirectory;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile + pipeName)))[..16].ToLowerInvariant();
        string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime) || !Path.IsPathFullyQualified(runtime) || !Directory.Exists(runtime)) runtime = Path.GetTempPath();
        string directory = Path.Combine(runtime, $"sharex-{LinuxUser.UserId}-{hash}");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (new DirectoryInfo(directory).LinkTarget != null) throw new IOException("The ShareX runtime directory must not be a symbolic link.");
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        socketPath = Path.Combine(directory, "ipc.sock");
        instanceLock = new FileStream(Path.Combine(directory, "instance.lock"), new FileStreamOptions {
            Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        try { instanceLock.Lock(0, 1); IsFirstInstance = true; }
        catch (IOException) { IsFirstInstance = false; }
        if (IsFirstInstance)
        {
            // The lock proves that a leftover endpoint belongs to a dead instance.
            File.Delete(socketPath);
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(16);
            listenTask = ListenAsync();
        }
        else ForwardAsync(args).GetAwaiter().GetResult();
    }

    protected virtual void OnArgumentsReceived(string[] arguments) => ArgumentsReceived?.Invoke(arguments);

    private async Task ListenAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using Socket client = await listener.AcceptAsync(cancellation.Token).ConfigureAwait(false);
                using NetworkStream stream = new(client, ownsSocket: false);
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                deadline.CancelAfter(5000);
                byte[] header = new byte[4];
                await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0 || length > MaxMessageBytes) continue;
                byte[] buffer = new byte[length];
                await stream.ReadExactlyAsync(buffer, deadline.Token).ConfigureAwait(false);
                string[] args = JsonSerializer.Deserialize<string[]>(buffer);
                if (args == null || args.Length > 100 || Array.Exists(args, arg => arg == null)) continue;
                // Acknowledge delivery before executing UI work. Preserve receive order.
                OnArgumentsReceived(args);
                await stream.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!cancellation.IsCancellationRequested) { DebugHelper.WriteException(ex); }
        }
    }

    private async Task ForwardAsync(string[] args)
    {
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(args ?? Array.Empty<string>());
        if (message.Length > MaxMessageBytes || args?.Length > 100) throw new ArgumentException("Too many command-line arguments.");
        using CancellationTokenSource deadline = new(5000);
        while (true)
        {
            using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), deadline.Token).ConfigureAwait(false); }
            catch (SocketException) when (!deadline.IsCancellationRequested)
            {
                await Task.Delay(50, deadline.Token).ConfigureAwait(false);
                continue;
            }
            using NetworkStream stream = new(client, ownsSocket: false);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
            await stream.WriteAsync(header, deadline.Token).ConfigureAwait(false);
            await stream.WriteAsync(message, deadline.Token).ConfigureAwait(false);
            await stream.ReadExactlyAsync(new byte[1], deadline.Token).ConfigureAwait(false);
            return;
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        // Cancellation interrupts AcceptAsync without racing disposed Socket handles.
        if (listenTask != null) { try { listenTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
        listener?.Dispose();
        if (IsFirstInstance && socketPath != null) File.Delete(socketPath);
        instanceLock?.Dispose();
        cancellation.Dispose();
    }
}
