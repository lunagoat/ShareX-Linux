// ShareX Linux integration checks. GPL-3.0-or-later.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShareX;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.UploadersLib;
using ShareX.UploadersLib.FileUploaders;
using ShareX.UploadersLib.TextUploaders;

internal static class NetworkChecks
{
    internal static async Task RunAsync(string sandbox)
    {
        using LocalHttp server = new();
        string file = Path.Combine(sandbox, "upload ü 'quoted'.bin");
        await File.WriteAllTextAsync(file, "ShareX localhost upload payload");
        var custom = CustomUploaderItem.Init();
        custom.RequestURL = server.Url + "json";
        custom.FileFormName = "file";
        custom.Arguments = new() { ["label"] = "Linux integration" };
        custom.Headers = new() { ["X-ShareX-Test"] = "local" };
        custom.URL = "{json:files[0].url}";
        custom.ThumbnailURL = "{json:thumb}";
        custom.DeletionURL = "{json:delete}";
        custom.DestinationType = CustomUploaderDestinationType.FileUploader | CustomUploaderDestinationType.TextUploader | CustomUploaderDestinationType.ImageUploader;
        // Import the same JSON shape used by .sxcu files before invoking the production engine.
        string sxcu = Path.Combine(sandbox, "local.sxcu");
        await File.WriteAllTextAsync(sxcu, JsonConvert.SerializeObject(custom));
        custom = JsonHelpers.DeserializeFromFile<CustomUploaderItem>(sxcu);
        {
            var uploader = new CustomFileUploader(custom);
            var result = await uploader.UploadFileAsync(file);
            Assert(!result.IsError && result.URL == server.Url + "stored", "multipart JSONPath response: " + result.ErrorsToString());
            Assert(result.ThumbnailURL == server.Url + "thumb" && result.DeletionURL == server.Url + "delete", "response URLs");
            var request = server.Requests.Last();
            Assert(request.Body.Contains("ShareX localhost upload payload") && request.Body.Contains("Linux integration"), "multipart body");
            Assert(request.TestHeader == "local", "custom header");
        }
        custom.Body = CustomUploaderBody.Binary;
        {
            var uploader = new CustomFileUploader(custom);
            var result = await uploader.UploadFileAsync(file);
            Assert(result.URL == server.Url + "stored", "binary upload");
            Assert(server.Requests.Last().Body == "ShareX localhost upload payload", "binary bytes");
        }
        foreach (var (body, route, template, response) in new[]
        {
            (CustomUploaderBody.JSON, "json", "{\"text\":\"{input}\"}", "{json:files[0].url}"),
            (CustomUploaderBody.XML, "xml", "<text>{input}</text>", "{xml:/files/file/url}"),
            (CustomUploaderBody.FormURLEncoded, "regex", "", "{regex:url=(.+)|1}"),
            (CustomUploaderBody.MultipartFormData, "regex", "", "{regex:url=(.+)|1}")
        })
        {
            custom.Body = body; custom.RequestURL = server.Url + route; custom.Data = template;
            custom.URL = response; custom.ThumbnailURL = ""; custom.DeletionURL = "";
            custom.FileFormName = ""; custom.Arguments = new() { ["text"] = "{input}" };
            var uploader = new CustomTextUploader(custom);
            var result = await uploader.UploadTextAsync("ü \"quote\" & <text>", "text.txt");
            Assert(result.URL == server.Url + "stored", body + ": " + result.ErrorsToString());
            if (body == CustomUploaderBody.JSON)
                Assert((string?)JObject.Parse(server.Requests.Last().Body)["text"] == "ü \"quote\" & <text>", "JSON body escaping");
            if (body == CustomUploaderBody.XML)
                Assert(System.Xml.Linq.XElement.Parse(server.Requests.Last().Body).Value == "ü \"quote\" & <text>", "XML body escaping");
        }
        custom.Body = CustomUploaderBody.Binary; custom.RequestURL = server.Url + "fail";
        {
            var uploader = new CustomFileUploader(custom);
            var result = await uploader.UploadFileAsync(file);
            Assert(uploader.IsError, "HTTP error propagation");
        }
        Console.WriteLine("PASS custom .sxcu multipart/binary/JSON/XML/form uploads and JSONPath/XPath/regex parsing");

        custom.Body = CustomUploaderBody.MultipartFormData; custom.RequestURL = server.Url + "json";
        custom.FileFormName = "file"; custom.URL = "{json:files[0].url}";
        ApplicationState.UploadersConfig.CustomUploadersList = [custom];
        ApplicationState.UploadersConfig.CustomFileUploaderSelected = 0;
        ApplicationState.UploadersConfig.CustomTextUploaderSelected = 0;
        ApplicationState.UploadersConfig.CustomImageUploaderSelected = 0;
        var settings = ApplicationState.DefaultTaskSettings;
        settings.FileDestination = FileDestination.CustomFileUploader;
        settings.ImageDestination = ImageDestination.CustomImageUploader;
        settings.TextDestination = TextDestination.CustomTextUploader;
        settings.AfterCaptureJob = AfterCaptureTasks.None;
        settings.AfterUploadJob = AfterUploadTasks.None;
        settings.GeneralSettings.ShowToastNotificationAfterTaskCompleted = false;
        settings.GeneralSettings.PlaySoundAfterUpload = false;
        settings.GeneralSettings.PlaySoundAfterCapture = false;
        ApplicationState.Settings.ShowLargeFileSizeWarning = 0;
        ApplicationState.Settings.HistorySaveTasks = true;
        ApplicationState.Settings.HistoryCheckURL = true;
        TaskbarManager.Enabled = false;
        int before = ApplicationState.HistoryManager!.GetHistoryItems().Count;
        var task = WorkerTask.CreateFileUploaderTask(file, settings);
        await Pipeline(task);
        var textTask = WorkerTask.CreateTextUploaderTask("pipeline text ü", settings);
        await Pipeline(textTask);
        await Task.Delay(100);
        Assert(ApplicationState.HistoryManager.GetHistoryItems().Count == before + 2, "pipeline persisted history");
        Console.WriteLine("PASS file/text task pipeline -> local upload -> SQLite history");

        var s3Settings = new AmazonS3Settings
        {
            Endpoint = server.Url.TrimEnd('/'), UsePathStyle = true, Region = "us-east-1", Bucket = "test-bucket",
            AccessKeyID = "local-test", SecretAccessKey = "local-secret", SetPublicACL = false, ObjectPrefix = "folder/"
        };
        {
            var s3 = new AmazonS3(s3Settings);
            var result = await s3.UploadFileAsync(file);
            Assert(!result.IsError && !string.IsNullOrEmpty(result.URL), "S3 local upload: " + result.ErrorsToString());
            var request = server.Requests.Last();
            Assert(request.Method == "PUT" && request.Path.StartsWith("/test-bucket/folder/"), "S3 path style");
            Assert(request.Authorization?.StartsWith("AWS4-HMAC-SHA256") == true, "S3 signing");
        }
        Console.WriteLine("PASS S3 signed PUT against local HTTP endpoint");
    }

    internal static async Task Pipeline(WorkerTask task)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        task.TaskCompleted += _ => completion.TrySetResult();
        TaskManager.Start(task);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert(!task.Info.Result.IsError && !task.StopRequested && task.Info.Result.URL?.EndsWith("/stored") == true,
            "task pipeline result: " + task.Info.Result.ErrorsToString());
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}

internal sealed class LocalHttp : IDisposable
{
    private readonly HttpListener listener = new();
    internal string Url { get; }
    internal record Request(string Method, string Path, string Body, string? TestHeader, string? Authorization);
    internal ConcurrentQueue<Request> Requests { get; } = new();
    internal LocalHttp()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Url = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(Url); listener.Start();
        _ = Task.Run(Serve);
    }
    private async Task Serve()
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream);
                string input = await reader.ReadToEndAsync();
                Requests.Enqueue(new(context.Request.HttpMethod, context.Request.Url!.AbsolutePath, input,
                    context.Request.Headers["X-ShareX-Test"], context.Request.Headers["Authorization"]));
                using var response = context.Response;
                string path = context.Request.Url.AbsolutePath;
                string body = path == "/xml" ? $"<files><file><url>{Url}stored</url></file></files>" :
                    path == "/regex" ? $"url={Url}stored" : JsonConvert.SerializeObject(new { files = new[] { new { url = Url + "stored" } }, thumb = Url + "thumb", delete = Url + "delete" });
                if (path == "/fail") response.StatusCode = 500;
                response.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes);
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { }
    }
    public void Dispose() => listener.Close();
}
