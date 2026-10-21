using ShareX.HelpersLib;
using ShareX.UploadersLib;
using System.Net;
using System.Net.Http;
using HttpMethod = ShareX.UploadersLib.HttpMethod;

internal static class UploaderTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("Uploader error log excludes credentials", ErrorLog));
        tests.Add(("Uploader request stream ownership and ranges", StreamOwnership));
        tests.Add(("Uploader explicit cancellation", Cancellation));
        tests.Add(("Uploader timeout", Timeout));
    }

    private static async Task ErrorLog()
    {
        string[] secrets = ["query-secret", "path-secret", "cookie-secret", "body-secret", "redirect-secret"];
        string logPath = Path.Combine(Program.Root, "uploader.log");
        DebugHelper.Init(logPath);
        DebugHelper.Logger.AsyncWrite = false;
        using var http = new FakeHttp((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"access_token\":\"body-secret\"}"),
                RequestMessage = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://unit.invalid/redirect-secret")
            };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "session=cookie-secret");
            return Task.FromResult(response);
        }, infiniteTimeout: true);
        var uploader = new TestUploader();
        Program.Check(await uploader.Get("https://unit.invalid/path-secret?access_token=query-secret") == null, "Error response should fail.");
        DebugHelper.Flush();
        string log = File.ReadAllText(logPath);
        Program.Check(!secrets.Any(log.Contains), "HTTP error log contains credentials: " + string.Join(", ", secrets.Where(log.Contains)));
        Program.Check(log.Contains("401"), "Log lost HTTP failure status.");
        Program.Check(uploader.Errors.ToString().Contains("body-secret"), "Detailed response must remain available to the error UI.");
    }

    private static async Task StreamOwnership()
    {
        byte[] sent = null;
        using var http = new FakeHttp(async (request, token) =>
        {
            sent = await request.Content.ReadAsByteArrayAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }, infiniteTimeout: true);
        using var source = new MemoryStream([0, 1, 2, 3, 4]);
        var uploader = new TestUploader();
        Program.Check(await uploader.Range(source, 1, 3) == "ok", "Range upload failed.");
        Program.Check(sent.SequenceEqual(new byte[] { 1, 2, 3 }), "Range upload sent wrong bytes.");
        Program.Check(source.CanRead, "Uploader disposed caller-owned source.");
    }

    private static async Task Cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new FakeHttp(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            throw new Exception("Unreachable");
        }, infiniteTimeout: true);
        var uploader = new TestUploader();
        Task<string> request = uploader.Operation();
        await entered.Task;
        uploader.StopUpload();
        Program.Check(await request.WaitAsync(TimeSpan.FromSeconds(1)) == null, "StopUpload did not cancel operation.");
        Program.Check(!uploader.IsUploading, "Uploader stayed busy after cancellation.");
    }

    private static async Task Timeout()
    {
        using var http = new FakeHttp(async (_, token) =>
        {
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            throw new Exception("Unreachable");
        }, infiniteTimeout: true);
        var uploader = new TestUploader { RequestTimeout = TimeSpan.FromMilliseconds(50) };
        Program.Check(await uploader.Get("https://unit.invalid/timeout") == null, "Timed-out request succeeded.");
        Program.Check(uploader.Errors.Count == 1 && !uploader.IsUploading, "Timeout was not reported/reset.");
    }

    private sealed class TestUploader : Uploader
    {
        internal Task<string> Get(string url) => SendRequestAsync(HttpMethod.GET, url, (Stream)null);
        internal Task<string> Range(Stream stream, long start, long length) => SendRequestAsync(HttpMethod.PUT, "https://unit.invalid/range", stream, start, length);
        internal Task<string> Operation() => RunOperationAsync(_ => Get("https://unit.invalid/cancel"));
    }
}
