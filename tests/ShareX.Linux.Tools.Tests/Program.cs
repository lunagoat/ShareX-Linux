// ShareX Linux tool integration checks. GPL v3; see repository LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShareX.AvaloniaUI.Integration;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.Tools;
using ShareX.Tools.Linux;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    private static readonly string Root = Path.Combine(Environment.GetEnvironmentVariable("SX_SANDBOX") ?? "/tmp/sx-wp5", "tools-tests");
    private static string ImagePath => Path.Combine(Root, "image with spaces.png");
    private static string SecondImage => Path.Combine(Root, "second.png");
    private static string VideoPath => Path.Combine(Root, "video with spaces.mp4");
    private static readonly List<string> Failures = [];
    private static bool Live;
    private static string? Filter;

    public static int Main(string[] args)
    {
        Live = args.Contains("--live");
        Filter = args.FirstOrDefault(x => x.StartsWith("--only="))?[7..];
        Directory.CreateDirectory(Root);
        if (Live) AvaloniaBootstrapper.EnsureInitialized();
        else
        {
            AppBuilder.Configure<ShareXAvaloniaApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia().WithInterFont().SetupWithoutStarting();
            ThemeManager.Refresh();
        }
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        using CancellationTokenSource stop = new();
        var task = Run();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(stop.Cancel));
        Dispatcher.UIThread.MainLoop(stop.Token);
        try { task.GetAwaiter().GetResult(); } catch (Exception ex) { Failures.Add(ex.ToString()); }
        File.WriteAllLines(Path.Combine(Root, Live ? "live-result.txt" : "headless-result.txt"), Failures.Count == 0 ? ["PASS"] : Failures);
        foreach (string failure in Failures) Console.WriteLine("FAIL " + failure);
        Console.WriteLine($"RESULT: {Failures.Count} failures; artifacts: {Root}");
        return Failures.Count == 0 ? 0 : 1;
    }

    private static async Task Check(string name, Func<Task> action)
    {
        if (Filter != null && !name.Contains(Filter, StringComparison.OrdinalIgnoreCase)) return;
        try { await action(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Failures.Add(name + ": " + ex); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Run()
    {
        using (SKBitmap image = new(640, 240, SKColorType.Bgra8888, SKAlphaType.Premul))
        {
            using SKCanvas canvas = new(image);
            canvas.Clear(SKColors.White);
            using SKPaint paint = new() { Color = SKColors.Black, IsAntialias = true };
            using SKFont font = new(SKTypeface.FromFamilyName("DejaVu Sans"), 48);
            canvas.DrawText("ShareX Linux 12345", 24, 132, font, paint);
            image.Save(ImagePath, SKEncodedImageFormat.Png);
            canvas.Clear(SKColors.CornflowerBlue);
            image.Save(SecondImage, SKEncodedImageFormat.Png);
        }
        await Check("image formats resize watermark compare icon", ImageOperations);
        await Check("directory indexer all formats", DirectoryIndex);
        await Check("animated GIF maker and trimmer", GifOperations);
        await Check("OCR tesseract language list and auto-copy", Ocr);
        await Check("hash algorithms", Hashes);
        await Check("QR generation and scan", Qr);
        await Check("background remover CPU ONNX inference and cache", Onnx);
        await Check("video converter thumbnailer trimmer", Videos);
        await Check("network probe localhost", async () =>
        {
            var result = await new NetworkMonitorProbe().ProbeAsync(new("Localhost", [new("loopback", "127.0.0.1")]), TimeSpan.FromSeconds(2), default);
            Require(result.Success, result.ErrorMessage);
        });
        await Check("window inspection compositor", () =>
        {
            var windows = DesktopTools.GetWindows();
            Console.WriteLine($"  {windows.Count} compositor windows; {DesktopTools.WindowSupport}");
            if (DesktopTools.Hyprland) Require(windows.Count > 0, "No compositor windows returned");
            return Task.CompletedTask;
        });
        await Check("all tool windows", Windows);
        await Check("video playback seek pause step audio", Playback);
        if (Live) await Check("mouse halo overlay and borderless roundtrip", Overlays);
    }

    private static Task ImageOperations()
    {
        using SKBitmap source = SkiaImageHelpers.LoadImage(ImagePath);
        foreach (var format in Enum.GetValues<ImageConverterOutputFormat>())
        {
            string path = Path.Combine(Root, "convert." + ImageConverterService.GetFileExtension(format));
            ImageConverterService.Save(source, path, format, 85, SKColors.White);
            using SKBitmap decoded = SkiaImageHelpers.LoadImage(path);
            Require(decoded.Width == 640 && decoded.Height == 240, "Image conversion dimensions changed: " + format);
        }
        foreach (var mode in Enum.GetValues<ImageResizeMode>())
        {
            using var resized = ImageResizerService.Resize(source, 80, 60, mode);
            Require(resized.Width > 0 && resized.Height > 0, "Resize failed: " + mode);
        }
        using var watermarked = ImageWatermarkService.Apply(source, null,
            new(ImageWatermarkType.Text, "ShareX", ImageWatermarkPosition.Center, 10, 80, 32, System.Drawing.Color.Red, 100, 25));
        var compare = new ImageComparisonService().Compare(source, watermarked);
        Require(compare != null, "Image comparison failed");
        foreach (var depth in Enum.GetValues<IconBitDepth>())
        {
            byte[] icon = IconConverterService.Convert(source, [16, 32, 64, 256], depth);
            Require(icon[2] == 1 && icon.Length > 100, "Invalid ICO output");
            File.WriteAllBytes(Path.Combine(Root, $"icon-{depth}.ico"), icon);
        }
        return Task.CompletedTask;
    }
    private static Task DirectoryIndex()
    {
        string folder = Path.Combine(Root, "index");
        Directory.CreateDirectory(Path.Combine(folder, "child"));
        File.WriteAllText(Path.Combine(folder, "child", "hello.txt"), "hello");
        foreach (var indexer in new Indexer[] { new IndexerText(new()), new IndexerJson(new()), new IndexerXml(new()), new IndexerHtml(new()) })
        {
            string text = indexer.Index(folder);
            Require(text.Contains("hello.txt"), indexer.GetType().Name + " omitted nested files");
            File.WriteAllText(Path.Combine(Root, indexer.GetType().Name + ".txt"), text);
        }
        return Task.CompletedTask;
    }
    private static Task GifOperations()
    {
        string path = Path.Combine(Root, "animated.gif");
        AnimatedGifMakerService.Create(path, [ImagePath, SecondImage, ImagePath], 200, true, 0);
        using var doc = AnimatedGifTrimmerDocument.Open(path, default);
        Require(doc.FrameCount == 3 && Math.Abs(doc.Duration - .6) < .02, "GIF timing mismatch");
        Require(doc.RenderFrame(1, default).Length > 100, "No GIF preview");
        Require(doc.Export(Path.Combine(Root, "trimmed.gif"), 1, 3, true, null, default), "GIF export failed");
        using var trimmed = AnimatedGifTrimmerDocument.Open(Path.Combine(Root, "trimmed.gif"), default);
        Require(trimmed.FrameCount == 2, "GIF trim did not cut frames");
        return Task.CompletedTask;
    }
    private static async Task Ocr()
    {
        Require(OCRHelper.AvailableLanguages.Any(l => l.LanguageTag == "en"), "English OCR pack unavailable");
        string? copied = null;
        using var vm = new OCRViewModel(File.ReadAllBytes(ImagePath), OCRHelper.AvailableLanguages,
            new OCROptions { Language = "en", AutoCopy = true, SingleLine = true }, async (data, language, scale, line) =>
            {
                using SKBitmap bitmap = SKBitmap.Decode(data);
                return await OCRHelper.OCR(bitmap, language, scale, line);
            });
        vm.CopyTextRequested = text => { copied = text; return Task.CompletedTask; };
        await vm.StartAsync();
        Require(vm.Result.Contains("Linux 12345"), "OCR text: " + vm.Result + " " + vm.ErrorMessage);
        Require(copied == vm.ResultText, "OCR auto-copy not invoked");
    }
    private static async Task Hashes()
    {
        foreach (var type in Enum.GetValues<HashType>())
        {
            string hash = await new HashChecker().Start(ImagePath, type);
            Require(hash.Length > 0, "Empty hash: " + type);
            if (type == HashType.SHA256) Require(hash.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ImagePath))), StringComparison.OrdinalIgnoreCase), "SHA256 mismatch");
        }
    }
    private static Task Qr()
    {
        using var image = ShareX.TaskHelpers.GenerateQRCode("ShareX Linux QR 12345", 320);
        Require(ShareX.TaskHelpers.BarcodeScan(image).Contains("ShareX Linux QR 12345"), "QR scan failed");
        image.Save(Path.Combine(Root, "qr.png"), SKEncodedImageFormat.Png);
        return Task.CompletedTask;
    }
    private static Task Onnx()
    {
        string modelPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mask.onnx");
        using var source = SkiaImageHelpers.LoadImage(ImagePath);
        using var service = new BackgroundRemovalService();
        var model = new BackgroundRemovalModel { FilePath = modelPath, FileName = "mask.onnx", FileSize = new FileInfo(modelPath).Length };
        var first = service.RemoveBackground(source, model, BackgroundRemovalDevice.Auto);
        using var output = first.Image;
        var second = service.RemoveBackground(source, model, BackgroundRemovalDevice.Auto);
        using var cached = second.Image;
        Require(!first.IsSessionCached && second.IsSessionCached && first.ExecutionDevice == "CPU", "ONNX session cache/device failed");
        Require(output.GetPixel(5, 5).Alpha < output.GetPixel(output.Width - 5, 5).Alpha, "ONNX alpha mask did not apply");
        output.Save(Path.Combine(Root, "background-removed.png"), SKEncodedImageFormat.Png);
        return Task.CompletedTask;
    }
    private static async Task Videos()
    {
        await ToolProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-t", "2", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", "-c:a", "aac", "-y", VideoPath]);
        var options = new VideoConverterOptions();
        string output = Path.Combine(Root, "converted.webm");
        using var ffmpeg = new FFmpegCLIManager(ToolProcess.Resolve("ffmpeg")) { ShowError = false };
        Require(await Task.Run(() => ffmpeg.Run($"-i \\\"{VideoPath}\\\" -c:v libvpx-vp9 -threads 1 -c:a libopus -y \\\"{output}\\\"".Replace("\\\"", "\""))), "Video conversion failed: " + ffmpeg.Output);
        var thumbnailer = new VideoThumbnailer("ffmpeg", new VideoThumbnailOptions { ThumbnailCount = 9, ColumnCount = 3,
            OutputLocation = ThumbnailLocationType.CustomFolder, CustomOutputDirectory = Root, CombineScreenshots = true,
            KeepScreenshots = true, OpenDirectory = false, UploadThumbnails = false });
        var thumbnails = await Task.Run(() => thumbnailer.TakeThumbnails(VideoPath));
        Require(thumbnails.Count == 1 && File.Exists(thumbnails[0].FilePath), "Contact sheet failed");
        Require(Directory.GetFiles(Root, "video with spaces-*.png").Length == 9, "Short video thumbnails overwrote one another");
        var filmstrip = await NativeVideoThumbnails.CreateAsync(VideoPath, 2, default);
        Require(filmstrip.Count == 12, "Trimmer filmstrip incomplete");
        foreach (var thumbnail in filmstrip) thumbnail.Image.Dispose();
        var service = new VideoTrimmerService("ffmpeg");
        string trim = Path.Combine(Root, "trim.mp4");
        await service.TrimAsync(VideoPath, trim, .4, 1.4, 2, true, new Progress<double>(), default);
        Require(File.Exists(trim), "Video trim failed");
    }

    private static async Task Windows()
    {
        var toolTypes = typeof(VideoPlayerWindow).Assembly.GetTypes().Where(t => t.IsPublic && !t.IsAbstract && typeof(Window).IsAssignableFrom(t)
            && t.GetConstructor(Type.EmptyTypes) != null).OrderBy(t => t.Name);
        foreach (var type in toolTypes)
        {
            Window? window = null;
            try
            {
                window = (Window)Activator.CreateInstance(type)!;
                if (window is NetworkMonitorWindow && window.DataContext is NetworkMonitorViewModel network)
                    network.SelectedTarget = new("Localhost", [new("loopback", "127.0.0.1")]);
                if (window is MonitorTestWindow) window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Show();
                await Task.Delay(Live ? 300 : 80);
                window.UpdateLayout();
                using RenderTargetBitmap bitmap = new(new PixelSize(Math.Max(1, (int)window.Bounds.Width), Math.Max(1, (int)window.Bounds.Height)), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Root, type.Name + (Live ? "-live" : "-headless") + ".png"), PngBitmapEncoderOptions.Default);
                if (Live)
                {
                    try
                    {
                        var own = DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && w.Text == window.Title);
                        if (own != null && own.Rectangle.Width > 0 && own.Rectangle.Height > 0)
                        {
                            ToolProcess.RunAsync("grim", ["-g", $"{own.Rectangle.X},{own.Rectangle.Y} {own.Rectangle.Width}x{own.Rectangle.Height}",
                                Path.Combine(Root, type.Name + "-grim.png")]).GetAwaiter().GetResult();
                        }
                    }
                    catch { }
                }
                Console.WriteLine("  window " + type.Name);
            }
            catch (Exception ex) { Failures.Add(type.Name + ": " + ex); }
            finally { window?.Close(); }
        }
        // The mouse settings window has no parameterless constructor.
        var mouse = new MouseHighlighterWindow(new(), null);
        mouse.Show();
        await Task.Delay(100);
        mouse.Close();
        // A real OCR image validates source preview, languages and result layout.
        var ocr = new OCRWindow(File.ReadAllBytes(ImagePath), OCRHelper.AvailableLanguages, new OCROptions { SingleLine = true },
            async (data, language, scale, line) => { using var b = SKBitmap.Decode(data); return await OCRHelper.OCR(b, language, scale, line); },
            () => Task.FromResult<byte[]?>(null));
        ocr.Show();
        await Task.Delay(600);
        using (RenderTargetBitmap bitmap = new(new PixelSize(1000, 700), new Vector(96, 96)))
        {
            bitmap.Render(ocr); bitmap.Save(Path.Combine(Root, "OCR-result.png"), PngBitmapEncoderOptions.Default);
        }
        ocr.Close();
    }
    private static async Task Playback()
    {
        if (!File.Exists(VideoPath)) await Videos();
        MediaPlayer player = new();
        Window window = new() { Content = player, Width = 640, Height = 400, Title = "ShareX WP5 playback test" };
        window.Show();
        try
        {
            double position = 0;
            Exception? error = null;
            player.PositionChanged += p => position = p;
            player.PlaybackFailed += ex => error = ex;
            var info = await player.LoadAsync(VideoPath);
            Require(info.Width == 320 && info.Height == 180 && info.Duration > 1.9, "Video metadata mismatch");
            player.IsMuted = true; // Do not play test audio on the user's desktop.
            player.Volume = .3;
            player.Play();
            await Task.Delay(450);
            Require(error == null && position > .1, "Playback did not advance: " + error);
            player.Pause();
            double paused = position;
            await Task.Delay(200);
            Require(Math.Abs(position - paused) < .15, "Pause did not stop playback");
            player.Seek(1);
            await Task.Delay(200);
            Require(Math.Abs(position - 1) < .2, "Seek failed: " + position);
            player.StepFrame(true);
            await Task.Delay(200);
            Require(position > .99, "Frame step failed");
            window.UpdateLayout();
            Console.WriteLine($"  playback bounds: window={window.Bounds}, player={player.Bounds}, visible={player.IsVisible}, attached={player.IsAttachedToVisualTree()}");
            if (!Live) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (RenderTargetBitmap bitmap = new(new PixelSize(640, 400), new Vector(96, 96)))
            {
                bitmap.Render(window); bitmap.Save(Path.Combine(Root, Live ? "playback-live.png" : "playback-headless.png"), PngBitmapEncoderOptions.Default);
            }
            player.SaveFrame(Path.Combine(Root, "mpv-raw.png"));
            using (var rendered = SKBitmap.Decode(Path.Combine(Root, Live ? "playback-live.png" : "playback-headless.png")))
                Require(rendered.Pixels.Any(p => p.Red > 50 || p.Green > 50 || p.Blue > 50), "Playback rendered a black frame");
            // Loading another file must discard old end-file events and frames.
            await player.LoadAsync(VideoPath);
        }
        finally { player.Dispose(); window.Close(); }
    }
    private static async Task Overlays()
    {
        var window = new Window { Title = "ShareX WP5 borderless test", Width = 400, Height = 250 };
        window.Show();
        await Task.Delay(300);
        try
        {
            var own = DesktopTools.GetWindows().First(w => w.ProcessId == Environment.ProcessId && w.Text == window.Title);
            BorderlessWindowManager.ToggleBorderlessWindow(own.Handle);
            await Task.Delay(150);
            Require(DesktopTools.GetWindows().First(w => w.Handle == own.Handle).Fullscreen > 0, "Borderless did not fullscreen");
            BorderlessWindowManager.ToggleBorderlessWindow(own.Handle);
            await Task.Delay(150);
            Require(DesktopTools.GetWindows().First(w => w.Handle == own.Handle).Fullscreen == own.Fullscreen, "Borderless did not restore");
            using var service = new MouseHighlighterService(new() { Mode = MouseHighlightMode.Circle, AlwaysColor = System.Drawing.Color.FromArgb(80, 255, 200, 0) });
            await Task.Delay(700);
            Require(DesktopTools.GetWindows().Any(w => w.ProcessId == Environment.ProcessId && w.Text == "ShareX mouse highlighter overlay"), "No halo overlay");
        }
        finally { window.Close(); }
    }
}
