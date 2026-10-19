// ShareX Linux tool integration checks. GPL v3; see repository LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShareX.AvaloniaUI.Integration;
using ShareX.AvaloniaUI.Theming;
using ShareX.AvaloniaUI.Windows;
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
        await Check("combiner splitter thumbnailer", CombinerSplitterThumbnailer);
        await Check("directory indexer all formats", DirectoryIndex);
        await Check("animated GIF maker and trimmer", GifOperations);
        await Check("OCR tesseract language list and auto-copy", Ocr);
        await Check("hash algorithms", Hashes);
        await Check("QR generation and scan", Qr);
        await Check("background remover CPU ONNX inference and cache", Onnx);
        await Check("video converter thumbnailer trimmer", Videos);
        await Check("metadata reader and stripper", MetadataTest);
        await Check("tool viewmodels and features", ToolViewModels);
        await Check("screen color picker sampling", ColorPickerSampleTest);
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

    private static async Task CombinerSplitterThumbnailer()
    {
        using SKBitmap source = SkiaImageHelpers.LoadImage(ImagePath);

        // Combine
        using var combinedH = SkiaImageHelpers.CombineImages([ImagePath, SecondImage], ImageOrientation.Horizontal);
        Require(combinedH != null && combinedH.Width == 1280 && combinedH.Height == 240, "Combine horizontal failed");
        using var combinedV = SkiaImageHelpers.CombineImages([ImagePath, SecondImage], ImageOrientation.Vertical);
        Require(combinedV != null && combinedV.Width == 640 && combinedV.Height == 480, "Combine vertical failed");

        bool combinedViaVm = false;
        var combinerVm = new ImageCombinerViewModel(new ImageCombinerOptions(), new ImageCombinerServices
        {
            CreatePreviewAsync = req => Task.FromResult<byte[]?>(File.ReadAllBytes(ImagePath)),
            ProcessAsync = req => { combinedViaVm = true; return Task.CompletedTask; }
        }, [ImagePath, SecondImage]);
        Require(combinerVm.CanCombine, "ImageCombinerViewModel CanCombine false");
        await combinerVm.CombineCommand.ExecuteAsync(null);
        Require(combinedViaVm, "ImageCombinerViewModel ProcessAsync was not called");

        // Split
        var split = SkiaImageHelpers.SplitImage(source, 2, 2);
        Require(split.Count == 4, "SplitImage count != 4");
        foreach (var tile in split)
        {
            Require(tile.Width == 320 && tile.Height == 120, "Split tile dimension mismatch");
            tile.Dispose();
        }

        var splitterVm = new ImageSplitterViewModel();
        splitterVm.SetImageFile(ImagePath);
        splitterVm.OutputFolderPath = Root;
        splitterVm.RowCount = 2;
        splitterVm.ColumnCount = 2;
        Require(splitterVm.CanProcess, "ImageSplitterViewModel CanProcess false");
        await splitterVm.SplitImageCommand.ExecuteAsync(null);
        string baseName = Path.GetFileNameWithoutExtension(ImagePath);
        Require(File.Exists(Path.Combine(Root, $"{baseName}1.png")), "Split output tile 1 missing");
        Require(File.Exists(Path.Combine(Root, $"{baseName}4.png")), "Split output tile 4 missing");

        // Thumbnailer
        using var thumb = SkiaImageHelpers.CreateThumbnail(source, 160, 60);
        Require(thumb.Width <= 160 && thumb.Height <= 60 && thumb.Width > 0 && thumb.Height > 0, "Thumbnail dimension mismatch");

        var thumbnailerVm = new ImageThumbnailerViewModel();
        thumbnailerVm.AddFiles([ImagePath]);
        thumbnailerVm.OutputFolderPath = Root;
        thumbnailerVm.Width = 120;
        thumbnailerVm.Height = 80;
        Require(thumbnailerVm.CanGenerate, "ImageThumbnailerViewModel CanGenerate false");
        await thumbnailerVm.GenerateCommand.ExecuteAsync(null);
        string thumbPath = Path.Combine(Root, baseName + "_th.jpg");
        Require(File.Exists(thumbPath), "ImageThumbnailer output missing: " + thumbPath);
    }

    private static async Task MetadataTest()
    {
        var metaPng = await MetadataService.ReadMetadataAsync(ImagePath);
        Require(metaPng.Count > 0, "PNG metadata empty");
        Require(metaPng.Any(m => m.Tag.Contains("Width") || m.Tag.Contains("Height")), "PNG metadata missing dimension tags");

        string jpegPath = Path.Combine(Root, "test-meta.jpg");
        using (var bmp = SkiaImageHelpers.LoadImage(ImagePath))
        {
            SkiaImageHelpers.SaveJPEG(bmp, jpegPath, 90);
        }
        var metaJpg = await MetadataService.ReadMetadataAsync(jpegPath);
        Require(metaJpg.Count > 0, "JPEG metadata empty");
        Require(MetadataService.CanStripMetadata(jpegPath), "JPEG should be strippable");
        await MetadataService.StripMetadataAsync(jpegPath);
        Require(File.Exists(jpegPath) && new FileInfo(jpegPath).Length > 0, "Stripped JPEG missing or empty");
        using var stripped = SkiaImageHelpers.LoadImage(jpegPath);
        Require(stripped.Width == 640 && stripped.Height == 240, "Stripped JPEG dimension mismatch");
    }

    private static Task ColorPickerSampleTest()
    {
        using SKBitmap sampleBmp = new(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKCanvas canvas = new(sampleBmp);
        canvas.Clear(new SKColor(255, 128, 64, 255));
        var picker = new ScreenColorPicker(sampleBmp, new PixelRect(0, 0, 100, 100), 1.0, new ScreenColorPickerOptions(), _ => { });
        var sample = picker.Sample(new Point(50, 50));
        Require(sample.Color.R == 255 && sample.Color.G == 128 && sample.Color.B == 64, "ScreenColorPicker sampled wrong color: " + sample.Color);
        picker.Close();
        return Task.CompletedTask;
    }

    private static async Task ToolViewModels()
    {
        // QRCodeViewModel
        var qrVm = new QRCodeViewModel(new QRCodeServices
        {
            GeneratePreviewAsync = (t, s) => Task.FromResult<byte[]?>(File.ReadAllBytes(ImagePath)),
            ScanAsync = (m, p) => Task.FromResult<string[]?>(["ShareX Linux QR 12345"]),
            SaveAsync = (t, s, p) => Task.FromResult(true),
            CopyImage = (t, s) => { },
            UploadImage = (t, s) => { }
        }, new QRCodeWindowOptions { InitialText = "ShareX Linux QR 12345" });
        await qrVm.InitializeAsync();
        Require(qrVm.HasQRCode, "QRCodeViewModel failed to generate preview");
        Require(qrVm.HasValidContent, "QRCodeViewModel content invalid");

        // HashCheckerViewModel
        var hashVm = new HashCheckerViewModel(async (fp, alg, prog, ct) =>
        {
            return await new HashChecker().Start(fp, (HashType)alg);
        }, filePath: ImagePath);
        hashVm.SelectedAlgorithm = HashCheckerViewModel.Algorithms.First(a => a.Algorithm == HashCheckerAlgorithm.Sha256);
        await hashVm.CheckCommand.ExecuteAsync(null);
        Require(hashVm.ResultHash.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ImagePath))), StringComparison.OrdinalIgnoreCase), "HashCheckerViewModel SHA256 mismatch");

        // InspectWindowViewModel
        var inspectVm = new InspectWindowViewModel();
        inspectVm.ReloadWindowList();
        Console.WriteLine($"  InspectWindow list: {inspectVm.Windows.Count} items");
        if (DesktopTools.Hyprland)
        {
            Require(inspectVm.Windows.Count > 0, "InspectWindow list empty on Hyprland");
            inspectVm.SelectedListItem = inspectVm.Windows[0];
            Require(inspectVm.HasSelection, "InspectWindow failed to select window");
            Require(inspectVm.Details.Count > 0, "InspectWindow details empty");
        }

        // ClipboardViewerViewModel
        ClipboardHelpers.CopyText("ShareX Linux Clipboard Test");
        var clipVm = new ClipboardViewerViewModel();
        clipVm.RefreshCommand.Execute(null);
        Console.WriteLine($"  Clipboard formats: {string.Join(", ", clipVm.Formats)}");
        Require(clipVm.HasFormats, "ClipboardViewer found no formats");

        // MonitorTestViewModel
        var monVm = new MonitorTestViewModel();
        foreach (var mode in Enum.GetValues<MonitorTestMode>())
        {
            monVm.SelectMode(mode);
            Require(monVm.SelectedMode == mode, "MonitorTest mode select failed: " + mode);
        }

        // AnalyzeImageViewModel
        var aiVm = new AnalyzeImageViewModel(ImagePath, new AIOptions());
        Require(aiVm.HasImage, "AnalyzeImageViewModel failed to load image");
        Require(aiVm.PresetPrompts.Count > 0, "AnalyzeImageViewModel preset prompts empty");

        // RemoteStorageBrowserViewModel with mock provider
        var mockProvider = new MockRemoteStorageProvider();
        var storageVm = new RemoteStorageBrowserViewModel([mockProvider]);
        await storageVm.InitializeAsync();
        Require(storageVm.Items.Count == 2, "RemoteStorage items count mismatch");
        var folderItem = storageVm.Items.First(i => i.IsFolder);
        await storageVm.OpenFolderAsync(folderItem);
        Require(storageVm.Items.Count == 1, "RemoteStorage folder navigation failed");
        await storageVm.GoUpAsync();
        Require(storageVm.Items.Count == 2, "RemoteStorage GoUp failed");

        // PinToScreen
        PinToScreenManager.Pin(File.ReadAllBytes(ImagePath), new PinToScreenOptions(), _ => { });
        await Task.Delay(100);
        PinToScreenManager.CloseAll();
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
                        var own = DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && !string.IsNullOrEmpty(window.Title) && w.Text == window.Title)
                            ?? DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && !string.IsNullOrEmpty(window.Title) && (w.Text.Contains(window.Title) || window.Title.Contains(w.Text)))
                            ?? DesktopTools.GetWindows().LastOrDefault(w => w.ProcessId == Environment.ProcessId);
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

        // ColorPickerWindow from ShareX.HelpersLib
        try
        {
            var colorPicker = new ColorPickerWindow();
            colorPicker.Show();
            await Task.Delay(Live ? 300 : 80);
            colorPicker.UpdateLayout();
            using RenderTargetBitmap cpBitmap = new(new PixelSize(Math.Max(1, (int)colorPicker.Bounds.Width), Math.Max(1, (int)colorPicker.Bounds.Height)), new Vector(96, 96));
            cpBitmap.Render(colorPicker);
            cpBitmap.Save(Path.Combine(Root, "ColorPickerWindow" + (Live ? "-live" : "-headless") + ".png"), PngBitmapEncoderOptions.Default);
            if (Live)
            {
                try
                {
                    var own = DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && !string.IsNullOrEmpty(colorPicker.Title) && w.Text == colorPicker.Title)
                        ?? DesktopTools.GetWindows().LastOrDefault(w => w.ProcessId == Environment.ProcessId);
                    if (own != null && own.Rectangle.Width > 0 && own.Rectangle.Height > 0)
                    {
                        ToolProcess.RunAsync("grim", ["-g", $"{own.Rectangle.X},{own.Rectangle.Y} {own.Rectangle.Width}x{own.Rectangle.Height}",
                            Path.Combine(Root, "ColorPickerWindow-grim.png")]).GetAwaiter().GetResult();
                    }
                }
                catch { }
            }
            colorPicker.Close();
            Console.WriteLine("  window ColorPickerWindow");
        }
        catch (Exception ex) { Failures.Add("ColorPickerWindow: " + ex); }

        // The mouse settings window has no parameterless constructor.
        try
        {
            var mouse = new MouseHighlighterWindow(new(), null);
            mouse.Show();
            await Task.Delay(Live ? 300 : 80);
            mouse.UpdateLayout();
            using RenderTargetBitmap mBitmap = new(new PixelSize(Math.Max(1, (int)mouse.Bounds.Width), Math.Max(1, (int)mouse.Bounds.Height)), new Vector(96, 96));
            mBitmap.Render(mouse);
            mBitmap.Save(Path.Combine(Root, "MouseHighlighterWindow" + (Live ? "-live" : "-headless") + ".png"), PngBitmapEncoderOptions.Default);
            if (Live)
            {
                try
                {
                    var own = DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && !string.IsNullOrEmpty(mouse.Title) && w.Text == mouse.Title)
                        ?? DesktopTools.GetWindows().LastOrDefault(w => w.ProcessId == Environment.ProcessId);
                    if (own != null && own.Rectangle.Width > 0 && own.Rectangle.Height > 0)
                    {
                        ToolProcess.RunAsync("grim", ["-g", $"{own.Rectangle.X},{own.Rectangle.Y} {own.Rectangle.Width}x{own.Rectangle.Height}",
                            Path.Combine(Root, "MouseHighlighterWindow-grim.png")]).GetAwaiter().GetResult();
                    }
                }
                catch { }
            }
            mouse.Close();
            Console.WriteLine("  window MouseHighlighterWindow");
        }
        catch (Exception ex) { Failures.Add("MouseHighlighterWindow: " + ex); }

        // PinToScreenWindow active with real image
        try
        {
            var pin = new PinToScreenWindow(File.ReadAllBytes(ImagePath), new PinToScreenOptions { Border = true, Shadow = true }, _ => { });
            pin.Show();
            await Task.Delay(Live ? 300 : 80);
            pin.UpdateLayout();
            using RenderTargetBitmap pBitmap = new(new PixelSize(Math.Max(1, (int)pin.Bounds.Width), Math.Max(1, (int)pin.Bounds.Height)), new Vector(96, 96));
            pBitmap.Render(pin);
            pBitmap.Save(Path.Combine(Root, "PinToScreenWindow-Active" + (Live ? "-live" : "-headless") + ".png"), PngBitmapEncoderOptions.Default);
            if (Live)
            {
                try
                {
                    var own = DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && !string.IsNullOrEmpty(pin.Title) && w.Text == pin.Title)
                        ?? DesktopTools.GetWindows().LastOrDefault(w => w.ProcessId == Environment.ProcessId);
                    if (own != null && own.Rectangle.Width > 0 && own.Rectangle.Height > 0)
                    {
                        ToolProcess.RunAsync("grim", ["-g", $"{own.Rectangle.X},{own.Rectangle.Y} {own.Rectangle.Width}x{own.Rectangle.Height}",
                            Path.Combine(Root, "PinToScreenWindow-Active-grim.png")]).GetAwaiter().GetResult();
                    }
                }
                catch { }
            }
            pin.Close();
            Console.WriteLine("  window PinToScreenWindow (active with image)");
        }
        catch (Exception ex) { Failures.Add("PinToScreenWindow-Active: " + ex); }

        // A real OCR image validates source preview, languages and result layout.
        var ocr = new OCRWindow(File.ReadAllBytes(ImagePath), OCRHelper.AvailableLanguages, new OCROptions { SingleLine = true },
            async (data, language, scale, line) => { using var b = SKBitmap.Decode(data); return await OCRHelper.OCR(b, language, scale, line); },
            () => Task.FromResult<byte[]?>(null));
        ocr.Show();
        await Task.Delay(Live ? 600 : 200);
        ocr.UpdateLayout();
        using (RenderTargetBitmap bitmap = new(new PixelSize(1000, 700), new Vector(96, 96)))
        {
            bitmap.Render(ocr); bitmap.Save(Path.Combine(Root, "OCR-result.png"), PngBitmapEncoderOptions.Default);
        }
        if (Live)
        {
            try
            {
                var own = DesktopTools.GetWindows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && !string.IsNullOrEmpty(ocr.Title) && w.Text == ocr.Title)
                    ?? DesktopTools.GetWindows().LastOrDefault(w => w.ProcessId == Environment.ProcessId);
                if (own != null && own.Rectangle.Width > 0 && own.Rectangle.Height > 0)
                {
                    ToolProcess.RunAsync("grim", ["-g", $"{own.Rectangle.X},{own.Rectangle.Y} {own.Rectangle.Width}x{own.Rectangle.Height}",
                        Path.Combine(Root, "OCRWindow-grim.png")]).GetAwaiter().GetResult();
                }
            }
            catch { }
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

    private sealed class MockRemoteStorageProvider : IRemoteStorageProvider
    {
        public string DisplayName => "MockStorage";
        public string RootDisplayName => "/";
        public string RootPath => "/";
        public RemoteStorageProviderCapabilities Capabilities => RemoteStorageProviderCapabilities.Download | RemoteStorageProviderCapabilities.Upload | RemoteStorageProviderCapabilities.CreateFolder;

        public string GetDisplayPath(string path) => path;
        public string? GetParentPath(string path) => path == "/" ? null : "/";
        public Task<IReadOnlyList<RemoteStorageItem>> GetItemsAsync(string path, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RemoteStorageItem> items = path == "/"
                ? [new RemoteStorageItem("Folder1", "/Folder1", true), new RemoteStorageItem("file1.png", "/file1.png", false, 1024, DateTimeOffset.UtcNow)]
                : [new RemoteStorageItem("nested.txt", $"{path}/nested.txt", false, 512, DateTimeOffset.UtcNow)];
            return Task.FromResult(items);
        }
        public Task DownloadAsync(RemoteStorageItem item, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UploadAsync(string directoryPath, string fileName, Stream source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateFolderAsync(string directoryPath, string folderName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(RemoteStorageItem item, string newName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasChildrenAsync(RemoteStorageItem folder, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(RemoteStorageItem item, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string? GetUrl(RemoteStorageItem item) => "https://example.com" + item.Path;
    }
}
