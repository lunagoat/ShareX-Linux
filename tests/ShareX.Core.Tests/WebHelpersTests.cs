using ShareX.HelpersLib;
using System.Net;

internal static class WebHelpersTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("Web download streams without whole-body buffering", StreamingDownload));
        foreach (var example in new (string Header, string Expected)[]
        {
            ("attachment; filename=plain.txt", "plain.txt"),
            ("attachment; filename=\"quoted.txt\"", "quoted.txt"),
            ("attachment; filename=\"fallback.txt\"; filename*=UTF-8''caf%C3%A9.txt", "café.txt"),
            ("attachment", null),
            ("attachment; filename=\"../../escape.txt\"", "escape.txt"),
            ("attachment; filename=\"..\\\\..\\\\escape.txt\"", "escape.txt"),
            ("attachment; filename=\"..\"", null)
        })
        {
            tests.Add(("Server filename " + example.Header, async () =>
            {
                using var http = new FakeHttp((_, _) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
                    response.Content.Headers.TryAddWithoutValidation("Content-Disposition", example.Header);
                    return Task.FromResult(response);
                });
                string name = await WebHelpers.GetFileNameFromWebServerAsync("https://unit.invalid/file");
                Program.Check(name == example.Expected, $"Expected '{example.Expected}', received '{name}'.");
            }));
        }
    }
    private static async Task StreamingDownload()
    {
        var content = new StreamingContent(new byte[8 * 1024 * 1024]);
        using var http = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        string path = Path.Combine(Program.Root, "streamed.bin");
        long before = GC.GetTotalAllocatedBytes(true);
        await WebHelpers.DownloadFileAsync("https://unit.invalid/large", path);
        long allocated = GC.GetTotalAllocatedBytes(true) - before;
        Console.WriteLine($"MEASURE 8 MiB file download allocated {allocated:N0} managed bytes; buffered={content.Serialized}.");
        Program.Check(new FileInfo(path).Length == 8 * 1024 * 1024, "Streamed file length differs.");
        Program.Check(!content.Serialized, "File response was copied into an intermediate whole-body HTTP buffer.");
    }

    private sealed class StreamingContent(byte[] data) : HttpContent
    {
        internal bool Serialized;
        protected override bool TryComputeLength(out long length)
        {
            length = data.Length;
            return true;
        }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
        {
            Serialized = true;
            return stream.WriteAsync(data).AsTask();
        }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(data, false));
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => CreateContentReadStreamAsync();
    }

}
