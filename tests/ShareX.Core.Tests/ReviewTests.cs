using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.Tools;
using ShareX.UploadersLib;
using ShareX.UploadersLib.URLShorteners;
using System.Net;
using System.Globalization;
using System.Reflection;

internal static class ReviewTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("History concurrent SQLite round trip", HistoryRoundTrip));
        tests.Add(("HTTP factory reuses keyed clients", ClientReuse));
    }

    private static async Task HistoryRoundTrip()
    {
        using var manager = new HistoryManagerSQLite(Path.Combine(Program.Root, "history.db"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            Program.Check(manager.AppendHistoryItem(new HistoryItem
            {
                FileName = i + ".png", FilePath = Path.Combine(Program.Root, i + ".png"),
                DateTime = new DateTime(2026, 10, 20, 12, 34, 56, DateTimeKind.Utc),
                Tags = new Dictionary<string, string> { ["test"] = i.ToString(CultureInfo.InvariantCulture) }
            }), "SQLite append failed.");
        })));
        // Call the internal load directly so an error can never open a UI message box.
        var load = typeof(HistoryManagerSQLite).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string)]);
        var items = (List<HistoryItem>)load.Invoke(manager, [manager.FilePath]);
        Program.Check(items.Count == 20 && items.Select(i => i.Id).Distinct().Count() == 20, "Concurrent history entries lost or duplicated.");
        Program.Check(items.All(i => i.DateTime.Year == 2026 && i.DateTime.Kind == DateTimeKind.Utc && i.Tags.Count == 1), "History date/tags round trip failed.");
    }

    internal static async Task PrintFindings()
    {
        string root = Path.Combine(Program.Root, "index-cycle");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "leaf.txt"), "x");
        Directory.CreateSymbolicLink(Path.Combine(root, "loop"), root);
        string json = Indexer.Index(root, new IndexerSettings { Output = IndexerOutput.Json, MaxDepthLevel = 6, CreateParseableJson = false });
        int copies = json.Split("leaf.txt").Length - 1;
        // Evidence for an intentionally unfixed finding: cap traversal to keep this test safe.
        Console.WriteLine($"FINDING directory ancestor symlink indexes one file {copies} times at depth limit 6.");

        string query = OAuthManager.GenerateQuery("https://unit.invalid/resource?existing=1", null,
            ShareX.UploadersLib.HttpMethod.GET, new OAuthInfo("consumer", "secret"));
        Console.WriteLine($"FINDING OAuth existing query retained: {query.Contains("existing=1")}.");

        string scalar = "𝔸";
        Console.WriteLine($"FINDING non-emoji Unicode scalar: normal={URLHelpers.URLEncode(scalar)}, ignoreEmoji={URLHelpers.URLEncodeIgnoreEmoji(scalar)}.");
        foreach (int length in new[] { 2048, 4096 })
        {
            string value = new string('a', length);
            long before = GC.GetAllocatedBytesForCurrentThread();
            URLHelpers.URLEncodeIgnoreEmoji(value);
            Console.WriteLine($"FINDING ignoreEmoji ASCII length {length} allocated {GC.GetAllocatedBytesForCurrentThread() - before} bytes.");
        }

        using var http = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>maintenance</html>") }), infiniteTimeout: true);
        var bitly = new BitlyURLShortener(new OAuth2Info("client", "secret") { Token = new OAuth2Token { access_token = "fake" } });
        try
        {
            await bitly.ShortenURLAsync("https://unit.invalid/input");
            Console.WriteLine("FINDING Bitly unexpected HTML did not throw (recheck report).");
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            Console.WriteLine("FINDING Bitly HTTP 200 HTML response escapes as JsonReaderException.");
        }
    }

    private static Task ClientReuse()
    {
        HttpClientFactory.Reset();
        Program.Check(ReferenceEquals(HttpClientFactory.Create(), HttpClientFactory.Create()), "Default HTTP clients are not reused.");
        Program.Check(!ReferenceEquals(HttpClientFactory.Create(), HttpClientFactory.Create(allowAutoRedirect: false)), "Redirect settings share a client.");
        Program.Check(!ReferenceEquals(HttpClientFactory.Create(), HttpClientFactory.Create(infiniteTimeout: true)), "Timeout settings share a client.");
        return Task.CompletedTask;
    }
}
