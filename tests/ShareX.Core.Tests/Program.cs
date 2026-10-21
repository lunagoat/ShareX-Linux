using ShareX.HelpersLib;
using System.Net;
using System.Reflection;
using System.Text;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> tests = new();
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "sharex-core-" + Guid.NewGuid().ToString("N"));

    private static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        DownloaderTests.Register(tests);
        SettingsTests.Register(tests);
        UploaderTests.Register(tests);
        FileAndZipTests.Register(tests);
        OAuthTests.Register(tests);
        WebHelpersTests.Register(tests);
        ReviewTests.Register(tests);
        int failures = 0;
        int executed = 0;
        try
        {
            if (args.Contains("--findings"))
            {
                await ReviewTests.PrintFindings();
                return 0;
            }

            foreach (var test in tests.Where(t => args.Length == 0 || t.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)))
            {
                executed++;
                try
                {
                    await test.Run().WaitAsync(TimeSpan.FromSeconds(10));
                    Console.WriteLine("PASS " + test.Name);
                }
                catch (Exception e)
                {
                    failures++;
                    Console.WriteLine("FAIL " + test.Name + ": " + e);
                }
            }
        }
        finally
        {
            HttpClientFactory.Reset();
            Directory.Delete(Root, true);
        }
        Console.WriteLine($"{executed} tests, {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

// Replace only the factory's cached transport. Every HTTP operation stays in memory;
// no listener, socket, DNS lookup, application, or display is started by these tests.
internal sealed class FakeHttp : IDisposable
{
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    internal FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, bool infiniteTimeout = false)
    {
        HttpClientFactory.Reset();
        HttpClientFactory.Create(infiniteTimeout: infiniteTimeout);
        var clients = (Dictionary<(bool, bool), HttpClient>)typeof(HttpClientFactory)
            .GetField("clients", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        clients[(true, infiniteTimeout)].Dispose();
        clients[(true, infiniteTimeout)] = new HttpClient(new Handler(send));
    }

    public void Dispose() => HttpClientFactory.Reset();

    internal static HttpResponseMessage Response(Stream stream, long? length)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = length;
        return response;
    }
}

internal static class DownloaderTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("Downloader known length", () => Download(7)));
        tests.Add(("Downloader unknown length", () => Download(null)));
        tests.Add(("Downloader empty file", () => Download(0, "")));
        tests.Add(("Downloader premature EOF", PrematureEnd));
        tests.Add(("Downloader cancel headers", CancelHeaders));
        tests.Add(("Downloader cancel body", CancelBody));
        tests.Add(("Downloader cancel from size event", CancelFromEvent));
    }

    private static async Task Download(long? length, string data = "payload")
    {
        using var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Response(new NonSeekableStream(Encoding.UTF8.GetBytes(data)), length)));
        string path = Path.Combine(Program.Root, Guid.NewGuid().ToString("N"));
        var downloader = new FileDownloader("https://unit.invalid/download", path);
        Program.Check(await downloader.StartDownload(), "Successful HTTP response must download.");
        Program.Check(File.ReadAllText(path) == data, "Downloaded bytes differ.");
        Program.Check(downloader.DownloadedSize == Encoding.UTF8.GetByteCount(data), "Incorrect byte count.");
        Program.Check(!downloader.IsDownloading, "Download state was not reset.");
    }

    private static async Task PrematureEnd()
    {
        // Limit reads so the old zero-byte loop fails promptly instead of spinning forever.
        using var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Response(new NonSeekableStream([1, 2]), 10)));
        var downloader = new FileDownloader("https://unit.invalid/short", Path.Combine(Program.Root, "short"));
        try
        {
            await downloader.StartDownload();
            throw new Exception("Truncated response was accepted.");
        }
        catch (EndOfStreamException) { }
        Program.Check(!downloader.IsDownloading, "Download state was not reset after failure.");
    }

    private static async Task CancelHeaders()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new FakeHttp(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Unreachable");
        });
        var downloader = new FileDownloader("https://unit.invalid/blocked", Path.Combine(Program.Root, "headers"));
        Task<bool> download = downloader.StartDownload();
        await entered.Task;
        downloader.StopDownload();
        Program.Check(!await download.WaitAsync(TimeSpan.FromSeconds(1)), "Cancellation must return false.");
        Program.Check(!downloader.IsDownloading, "Canceled downloader stayed busy.");
    }

    private static async Task CancelBody()
    {
        var stream = new BlockedStream();
        using var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Response(stream, 100)));
        string path = Path.Combine(Program.Root, "body");
        var downloader = new FileDownloader("https://unit.invalid/body", path);
        Task<bool> download = downloader.StartDownload();
        await stream.Entered.Task;
        downloader.StopDownload();
        Program.Check(!await download.WaitAsync(TimeSpan.FromSeconds(1)), "Cancellation must return false.");
        Program.Check(!File.Exists(path), "Canceled partial file was retained.");
        Program.Check(stream.Disposed, "Canceled response stream was leaked.");
    }

    private static async Task CancelFromEvent()
    {
        using var http = new FakeHttp((_, _) => Task.FromResult(FakeHttp.Response(new MemoryStream([1]), 1)));
        string path = Path.Combine(Program.Root, "event");
        var downloader = new FileDownloader("https://unit.invalid/event", path);
        downloader.FileSizeReceived += downloader.StopDownload;
        Program.Check(!await downloader.StartDownload(), "Canceled download reported success.");
        Program.Check(!File.Exists(path), "Canceled file was retained.");
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        private int reads;
        public override bool CanSeek => false;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (++reads > 4) throw new InvalidOperationException("Repeated reads after EOF (busy loop).");
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }

    private sealed class BlockedStream : MemoryStream
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
