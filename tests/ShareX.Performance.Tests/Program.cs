// ShareX performance checks. GPL-3.0-or-later. No desktop initialization or network access.
using System.Diagnostics;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using SkiaSharp;

string sandbox = Path.Combine(Path.GetTempPath(), "sx-perf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
try
{
    using SKBitmap bitmap = new(1024, 768, SKColorType.Bgra8888, SKAlphaType.Premul);
    bitmap.Erase(new SKColor(25, 100, 200, 128));
    foreach (ImageFileFormat format in Enum.GetValues<ImageFileFormat>())
    {
        using MemoryStream expected = new();
        bitmap.Save(expected, format);
        Check(SkiaImageHelpers.ImageToBase64(bitmap, format) == Convert.ToBase64String(expected.ToArray()), "base64 " + format);
    }
    Measure("image base64 BMP (1024x768)", 20, () =>
    {
        string result = SkiaImageHelpers.ImageToBase64(bitmap, ImageFileFormat.Bmp);
        GC.KeepAlive(result);
    });

    string imagePath = Path.Combine(sandbox, "animation-input.bmp");
    using (FileStream image = File.Create(imagePath)) bitmap.Save(image, ImageFileFormat.Bmp);
    Measure("animation open (2.25 MiB BMP)", 20, () =>
    {
        using SkiaAnimatedImage animation = new(imagePath);
        Check(animation.Width == bitmap.Width && animation.Height == bitmap.Height && animation.FrameCount == 1, "animation metadata");
    });
    using (SkiaAnimatedImage snapshot = new(imagePath))
    {
        // Loading retains its own encoded snapshot; subsequent file changes must not affect decoding.
        File.WriteAllText(imagePath, "replaced");
        using SKBitmap frame = snapshot.GetFrame(0);
        Check(frame.Width == bitmap.Width && frame.GetPixel(0, 0).Alpha == 255, "snapshot frame");
    }

    HistoryItem[] items = Enumerable.Range(0, 5000).Select(i => new HistoryItem
    {
        FileName = $"image-{i}.png", FilePath = i % 2 == 0 ? null : $"/tmp/{i}.png",
        DateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i),
        URL = $"https://example.invalid/{i}", Type = "Image", Host = i % 2 == 0 ? null : "test",
        Tags = i % 2 == 0 ? null : new() { ["index"] = i.ToString() }
    }).ToArray();
    // Warm both paths before collecting samples. Each append uses its own empty database.
    int database = 0;
    Measure("history append (5000 rows)", 1, () =>
    {
        using HistoryManagerSQLite history = new(Path.Combine(sandbox, $"history-{database++}.db"));
        Check(history.AppendHistoryItems(items), "append");
        Check(items.Select(i => i.Id).Distinct().Count() == items.Length && items[0].Id > 0, "assigned IDs");
    });
    using HistoryManagerSQLite saved = new(Path.Combine(sandbox, "read.db"));
    Check(saved.AppendHistoryItems(items), "seed");
    Measure("history load (5000 rows)", 5, () => Check(saved.GetHistoryItems().Count == items.Length, "load count"));
    List<HistoryItem> loaded = saved.GetHistoryItems();
    for (int i = 0; i < items.Length; i++)
    {
        Check(loaded[i].Id == items[i].Id && loaded[i].DateTime == items[i].DateTime &&
            loaded[i].FileName == items[i].FileName && loaded[i].FilePath == (items[i].FilePath ?? "") &&
            loaded[i].Host == (items[i].Host ?? "") && loaded[i].URL == items[i].URL &&
            (i % 2 == 0 ? loaded[i].Tags == null : loaded[i].Tags["index"] == i.ToString()), "row roundtrip " + i);
    }
    Console.WriteLine("PASS image encodings, history IDs, nullable values, timestamps and tags");
}
finally
{
    Directory.Delete(sandbox, true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Measure(string name, int iterations, Action action)
{
    action();
    List<double> times = new();
    List<long> allocations = new();
    for (int sample = 0; sample < 5; sample++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch timer = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        timer.Stop();
        times.Add(timer.Elapsed.TotalMilliseconds / iterations);
        allocations.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations);
    }
    times.Sort();
    allocations.Sort();
    Console.WriteLine($"{name}: median {times[2]:F3} ms/op, {allocations[2]:N0} managed bytes/op (5 samples x {iterations})");
}
