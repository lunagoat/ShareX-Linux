// ShareX Linux integration checks. GPL-3.0-or-later.
using System.Net;
using ShareX;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.UploadersLib;
using static NetworkChecks;

internal static class WorkflowChecks
{
    internal static async Task RunAsync(string sandbox)
    {
        string input = Path.Combine(sandbox, "action ü \"quote\" $;\\.txt");
        await File.WriteAllTextAsync(input, "literal action data");
        var action = new ExternalProgram("Copy", "cp") { Args = "-- '$input' \"$output\"", OutputExtension = "out" };
        string output = await action.RunAsync(input);
        Assert(output == Path.ChangeExtension(input, "out") && File.ReadAllText(output) == "literal action data", "Action argument boundaries");
        var failed = new ExternalProgram("Failure", "/bin/false");
        Assert(await failed.RunAsync(input) == null, "nonzero action exit");
        Console.WriteLine("PASS Actions PATH lookup, quoting, Unicode, metacharacters and exit handling");

        string folder = Path.Combine(sandbox, "watch");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        using (var watch = new WatchFolder { Settings = new WatchFolderSettings { FolderPath = folder, Filter = "*.txt" } })
        {
            var seen = new List<string>();
            watch.FileWatcherTrigger += path => seen.Add(Path.GetFileName(path));
            watch.Enable();
            await File.WriteAllTextAsync(Path.Combine(folder, "created.txt"), "ready");
            await File.WriteAllTextAsync(Path.Combine(folder, "empty.txt"), "");
            string staging = Path.Combine(sandbox, "staging.tmp");
            await File.WriteAllTextAsync(staging, "atomic rename");
            File.Move(staging, Path.Combine(folder, "renamed.txt"), true);
            await File.WriteAllTextAsync(Path.Combine(folder, "ignored.bin"), "ignored");
            await Task.Delay(2300);
            Assert(seen.Order().SequenceEqual(new[] { "created.txt", "empty.txt", "renamed.txt" }), "watch create/zero-length/rename/filter: " + string.Join(',', seen));
            await File.WriteAllTextAsync(Path.Combine(folder, "cancelled.txt"), "cancel");
            watch.Dispose();
            await Task.Delay(1500);
            Assert(!seen.Contains("cancelled.txt"), "watch disposal cancels pending events");
        }
        Console.WriteLine("PASS watch folder create, atomic rename, empty file, filter and cancellation");

        string historyPath = Path.Combine(sandbox, "history; ü.db");
        if (File.Exists(historyPath)) File.Delete(historyPath);
        using (var history = new HistoryManagerSQLite(historyPath))
        {
            await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(() =>
            {
                var item = new HistoryItem { FileName = "file" + index, FilePath = input, DateTime = DateTime.Now, URL = "http://127.0.0.1/" + index };
                Assert(history.AppendHistoryItem(item), "concurrent history append");
                Assert(history.GetHistoryItems().Count > 0, "concurrent history read");
            })));
            Assert(history.GetHistoryItems().Count == 40, "history concurrent count");
            var edit = history.GetHistoryItems()[0]; edit.Tags = new() { ["platform"] = "ü" }; history.Edit(edit);
            Assert(history.GetHistoryItems().Single(x => x.Id == edit.Id).Tags.ContainsValue("ü"), "history edit");
            history.Delete(edit);
            Assert(history.GetHistoryItems().Count == 39, "history delete");
        }
        using (var reopened = new HistoryManagerSQLite(historyPath)) Assert(reopened.GetHistoryItems().Count == 39, "history reopen");
        Console.WriteLine("PASS SQLite concurrent read/write, edit, delete, reopen and semicolon filename");

        string oldBrowser = HelpersOptions.BrowserPath;
        HelpersOptions.BrowserPath = "/bin/true";
        try
        {
            using var oauth = new OAuthListener(new FakeOAuth());
            Assert(await oauth.ConnectAsync(), "OAuth callback with preceding bad state");
            using var denied = new OAuthListener(new FakeOAuth { Denied = true });
            Assert(!await denied.ConnectAsync(), "OAuth denied callback");
            using var cancel = new CancellationTokenSource(150);
            using var waiting = new OAuthListener(new FakeOAuth { NoCallback = true });
            try { await waiting.ConnectAsync(cancel.Token); throw new Exception("OAuth cancellation failed"); }
            catch (OperationCanceledException) { }
            using var disposed = new OAuthListener(new FakeOAuth { NoCallback = true });
            var pending = disposed.ConnectAsync();
            await Task.Delay(100); disposed.Dispose();
            Assert(!await pending.WaitAsync(TimeSpan.FromSeconds(2)), "OAuth listener dispose");
        }
        finally { HelpersOptions.BrowserPath = oldBrowser; }
        Console.WriteLine("PASS OAuth loopback bind-before-launch, invalid state, success, denial, cancellation and disposal");
    }
}

internal sealed class FakeOAuth : IOAuth2Loopback
{
    public OAuth2Info AuthInfo { get; } = new("local", "local-secret");
    public string RedirectURI { get; set; } = "";
    public string State { get; set; } = "";
    public string Scope { get; set; } = "";
    internal bool Denied { get; init; }
    internal bool NoCallback { get; init; }
    public Task<string> GetAuthorizationURLAsync(CancellationToken cancellationToken = default)
    {
        if (!NoCallback) _ = Task.Run(async () =>
        {
            using HttpClient client = new();
            using var bad = await client.GetAsync(RedirectURI + "?state=incorrect&code=attacker", cancellationToken);
            NetworkChecks.Assert(bad.StatusCode == HttpStatusCode.BadRequest, "OAuth invalid state HTTP response");
            using var good = await client.GetAsync(RedirectURI + "?state=" + State + (Denied ? "&error=access_denied" : "&code=fake-code"), cancellationToken);
            NetworkChecks.Assert(good.IsSuccessStatusCode, "OAuth callback HTTP response");
        });
        return Task.FromResult("http://127.0.0.1/authorization-test");
    }
    public Task<bool> GetAccessTokenAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(code == "fake-code");
    public Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CheckAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<OAuthUserInfo?> GetUserInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult<OAuthUserInfo?>(null);
}
