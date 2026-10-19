// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

/// <summary>Per-user/config Unix-domain command forwarding. The config hash isolates sandbox and
/// portable sessions; a locked file prevents stale-socket cleanup from racing another startup.</summary>
internal sealed class LinuxInstanceConnection : IDisposable
{
    private readonly FileStream instanceLock;
    private readonly Socket server;
    private readonly CancellationTokenSource stop = new();
    private readonly string path;
    public bool IsFirstInstance { get; }
    public LinuxInstanceConnection(string name, string[] args, Action<string[]> received)
    {
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string identity = Environment.UserName + "\n" + config + "\n" + name;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
        string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtime)) runtime = System.IO.Path.GetTempPath();
        string directory = System.IO.Path.Combine(runtime, "sharex-" + hash);
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        path = System.IO.Path.Combine(directory, "commands.sock");
        if (Encoding.UTF8.GetByteCount(path) > 100) throw new IOException("ShareX runtime path is too long for a Unix socket.");
        try
        {
            instanceLock = new FileStream(System.IO.Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.SetUnixFileMode(System.IO.Path.Combine(directory, "instance.lock"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException)
        {
            Forward(args);
            return;
        }
        try
        {
            if (File.Exists(path)) File.Delete(path);
            server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            server.Listen(5);
            IsFirstInstance = true;
            _ = ListenAsync(received);
        }
        catch { instanceLock.Dispose(); throw; }
    }
    private void Forward(string[] args)
    {
        if (args?.Length > 100) throw new ArgumentException("At most 100 ShareX arguments are supported.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(args ?? []);
        if (bytes.Length > 65536) throw new ArgumentException("ShareX arguments exceed 64 KiB.");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                client.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token).AsTask().GetAwaiter().GetResult();
                using NetworkStream stream = new(client);
                stream.WriteAsync(bytes, timeout.Token).AsTask().GetAwaiter().GetResult();
                client.Shutdown(SocketShutdown.Send);
                return;
            }
            catch (SocketException) when (!timeout.IsCancellationRequested) { Task.Delay(50, timeout.Token).GetAwaiter().GetResult(); }
        }
    }
    private async Task ListenAsync(Action<string[]> received)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using Socket client = await server.AcceptAsync(stop.Token);
                using NetworkStream stream = new(client);
                using MemoryStream data = new();
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] buffer = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (data.Length + count > 65536) throw new IOException("ShareX command exceeds 64 KiB.");
                    data.Write(buffer, 0, count);
                }
                string[] args = JsonSerializer.Deserialize<string[]>(data.ToArray());
                if (args != null && args.Length <= 100) received(args);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { return; }
            catch (Exception exception) { DebugHelper.WriteException(exception, "ShareX command forwarding failed."); }
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        server?.Dispose();
        if (IsFirstInstance && File.Exists(path)) File.Delete(path);
        instanceLock?.Dispose();
        // The lock file must remain: unlinking it permits separate processes to lock different inodes.
    }
}
