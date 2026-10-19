// ShareX Linux integration checks. GPL-3.0-or-later.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ShareX.UploadersLib;
using ShareX.UploadersLib.FileUploaders;
using static NetworkChecks;

internal static class StorageChecks
{
    internal static async Task RunAsync(string sandbox)
    {
        string file = Path.Combine(sandbox, "storage ü space.txt");
        await File.WriteAllTextAsync(file, "local storage payload");
        using (LocalFtp server = new())
        {
            using FTP ftp = new(new FTPAccount
            {
                Host = "127.0.0.1", Port = server.Port, Username = "test", Password = "local-only",
                SubFolderPath = "folder\\nested", HttpHomePath = "127.0.0.1/files"
            });
            var result = await ftp.UploadFileAsync(file);
            Assert(!ftp.IsError && !string.IsNullOrEmpty(result.URL), "FTP upload: " + ftp.ToErrorString());
            var stored = await server.Stored.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert(stored.Path == "/folder/nested/storage ü space.txt" || stored.Path == "folder/nested/storage ü space.txt", "FTP remote path " + stored.Path);
            Assert(stored.Body == "local storage payload", "FTP payload");
        }
        Console.WriteLine("PASS FTP passive transfer, Unicode filename and imported backslash subfolder");

        using (LocalHttp webdav = new())
        {
            var owncloud = new OwnCloud(webdav.Url, "local", "secret") { Path = "folder", CreateShare = false };
            var result = await owncloud.UploadFileAsync(file);
            Assert(!owncloud.IsError && !result.IsURLExpected, "WebDAV upload: " + owncloud.ToErrorString());
            var request = webdav.Requests.Last();
            Assert(request.Method == "PUT" && request.Path.StartsWith("/remote.php/webdav/folder/"), "WebDAV URL");
            Assert(request.Body == "local storage payload" && request.Authorization?.StartsWith("Basic ") == true, "WebDAV body and auth");
        }
        Console.WriteLine("PASS ownCloud WebDAV PUT against local HTTP server");

        if (!File.Exists("/usr/bin/sshd") || !File.Exists("/usr/bin/ssh-keygen"))
        {
            Console.WriteLine("SKIP SFTP: local sshd and ssh-keygen are unavailable");
            return;
        }
        string ssh = Path.Combine(sandbox, "sshd"); Directory.CreateDirectory(ssh);
        string hostKey = Path.Combine(ssh, "host"); string userKey = Path.Combine(ssh, "user");
        foreach (string key in new[] { hostKey, userKey })
        {
            if (!File.Exists(key)) await RunProcess("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", key);
        }
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        string config = Path.Combine(ssh, "config");
        await File.WriteAllTextAsync(config, $"ListenAddress 127.0.0.1\nPort {port}\nHostKey {hostKey}\nPidFile {ssh}/pid\nAuthorizedKeysFile {userKey}.pub\nStrictModes no\nPasswordAuthentication no\nKbdInteractiveAuthentication no\nUsePAM no\nSubsystem sftp internal-sftp\n");
        using Process daemon = new() { StartInfo = new ProcessStartInfo("/usr/bin/sshd") { UseShellExecute = false, RedirectStandardError = true } };
        foreach (string arg in new[] { "-D", "-e", "-f", config }) daemon.StartInfo.ArgumentList.Add(arg);
        daemon.Start();
        Task<string> errors = daemon.StandardError.ReadToEndAsync();
        try
        {
            await Task.Delay(350);
            if (daemon.HasExited) throw new Exception("Local rootless sshd failed: " + await errors);
            string destination = Path.Combine(ssh, "uploaded"); Directory.CreateDirectory(destination);
            using SFTP sftp = new(new FTPAccount
            {
                Protocol = FTPProtocol.SFTP, Host = "127.0.0.1", Port = port,
                Username = Environment.UserName, Keypath = userKey,
                SubFolderPath = destination, HttpHomePath = "127.0.0.1/files"
            });
            var result = await sftp.UploadFileAsync(file);
            Assert(!sftp.IsError && !string.IsNullOrEmpty(result.URL), "SFTP: " + sftp.ToErrorString());
            Assert(File.ReadAllText(Path.Combine(destination, Path.GetFileName(file))) == "local storage payload", "SFTP uploaded bytes");
        }
        finally { if (!daemon.HasExited) { daemon.Kill(true); await daemon.WaitForExitAsync(); } }
        Console.WriteLine("PASS SFTP key authentication and upload against isolated rootless OpenSSH");
    }

    internal static async Task RunProcess(string executable, params string[] arguments)
    {
        using Process process = new() { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true } };
        foreach (string arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert(process.ExitCode == 0, executable + ": " + error);
    }
}

internal sealed class LocalFtp : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private TcpListener? passive;
    internal int Port { get; }
    internal TaskCompletionSource<(string Path, string Body)> Stored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal LocalFtp() { listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; _ = Task.Run(Serve); }
    private async Task Serve()
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 ShareX local test FTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                string command = line.Split(' ', 2)[0];
                string argument = line.Contains(' ') ? line[(command.Length + 1)..] : "";
                switch (command)
                {
                    case "USER": await writer.WriteLineAsync("331 Password required"); break;
                    case "PASS": await writer.WriteLineAsync("230 Logged in"); break;
                    case "FEAT": await writer.WriteLineAsync("211-Features\r\n UTF8\r\n EPSV\r\n211 End"); break;
                    case "SYST": await writer.WriteLineAsync("215 UNIX Type: L8"); break;
                    case "PWD": await writer.WriteLineAsync("257 \"/\""); break;
                    case "TYPE": case "OPTS": case "CWD": case "NOOP": await writer.WriteLineAsync("200 OK"); break;
                    case "SIZE": await writer.WriteLineAsync("550 Not found"); break;
                    case "EPSV": case "PASV":
                        passive?.Stop(); passive = new TcpListener(IPAddress.Loopback, 0); passive.Start();
                        int port = ((IPEndPoint)passive.LocalEndpoint).Port;
                        await writer.WriteLineAsync(command == "EPSV" ? $"229 Entering Extended Passive Mode (|||{port}|)" : $"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})");
                        break;
                    case "STOR":
                        await writer.WriteLineAsync("150 Opening data");
                        using (var data = await passive!.AcceptTcpClientAsync())
                        using (var content = new StreamReader(data.GetStream()))
                        {
                            string body = await content.ReadToEndAsync();
                            Stored.TrySetResult((argument, body));
                        }
                        await writer.WriteLineAsync("226 Transfer complete"); break;
                    case "QUIT": await writer.WriteLineAsync("221 Bye"); return;
                    default: await writer.WriteLineAsync("502 Unsupported"); break;
                }
            }
        }
        catch (Exception e) { Stored.TrySetException(e); }
    }
    public void Dispose() { passive?.Stop(); listener.Stop(); }
}
