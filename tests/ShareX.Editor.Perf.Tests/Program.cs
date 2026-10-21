using System.Diagnostics;
using ShareX.ImageEditor.Core.Editor;
using ShareX.ImageEditor.Core.ImageEffects.Helpers;
using ShareX.ImageEffectsLib;
using SkiaSharp;

int failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}");
    if (!condition) failures++;
}

SKRectI? ReferenceBounds(SKBitmap source, SKColor background, int tolerance)
{
    int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
    for (int y = 0; y < source.Height; y++)
    for (int x = 0; x < source.Width; x++)
    {
        SKColor color = source.GetPixel(x, y);
        if (Math.Abs(color.Red - background.Red) <= tolerance &&
            Math.Abs(color.Green - background.Green) <= tolerance &&
            Math.Abs(color.Blue - background.Blue) <= tolerance &&
            Math.Abs(color.Alpha - background.Alpha) <= tolerance) continue;
        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
    }
    return maxX < 0 ? null : new SKRectI(minX, minY, maxX + 1, maxY + 1);
}

// Compare the public auto-crop output against the original full scan, including
// alpha, tolerance boundaries, uniform images, and content touching each edge.
Random random = new(7321);
foreach (SKColorType colorType in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888, SKColorType.Rgb565 })
foreach (int tolerance in new[] { -1, 0, 1, 30, 255 })
for (int trial = 0; trial < 30; trial++)
{
    using SKBitmap source = new(13, 11, colorType, SKAlphaType.Premul);
    SKColor background = new(80, 100, 120, 255);
    source.Erase(background);
    for (int i = 0; i < trial; i++)
        source.SetPixel(random.Next(13), random.Next(11), new SKColor((byte)random.Next(256), 100, 120, (byte)random.Next(256)));
    SKRectI? bounds = ReferenceBounds(source, background, tolerance);
    using SKBitmap actual = ImageHelpers.AutoCrop(source, background, tolerance);
    using SKBitmap expected = bounds is SKRectI rect ? ImageHelpers.Crop(source, rect) : new SKBitmap(1, 1, colorType, source.AlphaType);
    if (actual.Width != expected.Width || actual.Height != expected.Height || !actual.Bytes.AsSpan().SequenceEqual(expected.Bytes))
    {
        Check(false, $"auto-crop reference {colorType}/{tolerance}/{trial}");
    }
}
Check(failures == 0, "450 auto-crop cases match original full scan");

foreach (string scenario in new[] { "dense", "border", "uniform" })
{
    using SKBitmap source = new(3840, 2160);
    source.Erase(SKColors.White);
    if (scenario != "uniform")
    {
        using SKCanvas canvas = new(source);
        using SKPaint paint = new() { Color = SKColors.Black };
        int inset = scenario == "border" ? 100 : 0;
        canvas.DrawRect(inset, inset, source.Width - 2 * inset, source.Height - 2 * inset, paint);
    }
    for (int i = 0; i < 2; i++) { using SKBitmap warmup = ImageHelpers.AutoCrop(source, SKColors.White); }
    Stopwatch timer = Stopwatch.StartNew();
    const int iterations = 8;
    for (int i = 0; i < iterations; i++) { using SKBitmap result = ImageHelpers.AutoCrop(source, SKColors.White); }
    Console.WriteLine($"BENCH auto-crop {scenario} 3840x2160: {timer.Elapsed.TotalMilliseconds / iterations:F3} ms/op ({iterations} iterations)");
}
// A bare filename must work relative to the current directory.
string saveDirectory = Path.Combine(Path.GetTempPath(), "sx-editor-save-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(saveDirectory);
string previousDirectory = Environment.CurrentDirectory;
try
{
    Environment.CurrentDirectory = saveDirectory;
    using SKBitmap source = new(2, 3);
    source.Erase(SKColors.Coral);
    foreach (string filename in new[] { "image.png", "nested/image.png" })
    {
        try
        {
            ImageHelpers.SaveBitmap(source, filename);
            using SKBitmap decoded = SKBitmap.Decode(filename);
            Check(decoded != null && decoded.Width == 2 && decoded.Height == 3 && decoded.GetPixel(0, 0) == SKColors.Coral,
                $"save and decode {filename}");
        }
        catch (Exception ex) { Check(false, $"save {filename}: {ex.GetType().Name}"); }
    }
}
finally
{
    Environment.CurrentDirectory = previousDirectory;
    Directory.Delete(saveDirectory, true);
}

// Preset owns its working copies; effects may consume, replace, or return them.
using (SKBitmap source = new(4, 4))
{
    foreach (string mode in new[] { "replace", "same", "consume", "null", "throw" })
    {
        TrackingEffect effect = new(mode);
        ImageEffectPreset preset = new() { Effects = [effect] };
        try
        {
            using SKBitmap result = preset.ApplyEffects(source);
            if (mode == "throw") Check(false, "throwing effect propagates error");
            Check(effect.Input != null && (mode == "same" ? effect.Input.Handle != IntPtr.Zero : effect.Input.Handle == IntPtr.Zero),
                $"preset releases intermediate: {mode}");
            if (mode == "null") Check(result == null, "preset preserves null effect result");
        }
        catch (InvalidOperationException) when (mode == "throw")
        {
            Check(effect.Input.Handle == IntPtr.Zero, "preset releases input when effect throws");
        }
        Check(source.Handle != IntPtr.Zero, $"preset preserves caller source: {mode}");
        effect.Input?.Dispose();
    }
}

try
{
    using (SKBitmap source = new(4, 4))
    {
        source.Erase(SKColors.Coral);
        using SKBitmap crop = ImageHelpers.Crop(source, 1, 1, int.MaxValue, int.MaxValue);
        Check(crop.Width == 3 && crop.Height == 3 && crop.GetPixel(0, 0) == SKColors.Coral,
            "crop clips overflowing positive extents to image bounds");
        using SKBitmap outside = ImageHelpers.Crop(source, new SKRectI(int.MaxValue, 0, int.MinValue, 4));
        Check(outside.Width == 0 && outside.Height == 0, "inverted extreme crop stays empty");
        using SKBitmap spanning = ImageHelpers.Crop(source, new SKRectI(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue));
        Check(spanning.Width == 4 && spanning.Height == 4, "extreme spanning rectangle clips to image");
        using SKBitmap negative = ImageHelpers.Crop(source, 1, 1, -1, 2);
        Check(negative.Width == 0 && negative.Height == 0, "negative crop dimensions stay empty");
    }
}
catch (Exception ex) { Check(false, $"extreme crop: {ex.GetType().Name}"); }

using (SKBitmap source = new(1920, 1080))
{
    TrackingEffect[] effects = [new("replace"), new("replace"), new("replace")];
    ImageEffectPreset preset = new() { Effects = effects.Cast<ImageEffect>().ToList() };
    double elapsed = 0;
    int liveInputs = 0;
    for (int i = 0; i < 10; i++)
    {
        long started = Stopwatch.GetTimestamp();
        using (SKBitmap result = preset.ApplyEffects(source)) { }
        elapsed += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        liveInputs = effects.Count(effect => effect.Input.Handle != IntPtr.Zero);
        // Clean up leaked inputs after timing so the baseline remains bounded.
        foreach (TrackingEffect effect in effects) effect.Input.Dispose();
    }
    Console.WriteLine($"BENCH preset three replacements 1920x1080: {elapsed / 10:F3} ms/op; unreleased inputs: {liveInputs} ({(long)liveInputs * 1920 * 1080 * 4} pixel bytes)");
}

using (EditorCore core = new())
{
    core.LoadImage(new SKBitmap(4, 4));
    core.ResizeImage(2, 2);
    core.Undo();
    Check(core.CanRedo, "resize undo prepares redo");
    Check(!core.CutOut(0, 4, true), "whole-image cutout is rejected");
    Check(core.CanRedo && !core.CanUndo, "rejected cutout preserves both history stacks");
    core.Redo();
    Check(core.SourceImage.Width == 2, "resize remains redoable after rejected cutout");
}

return failures == 0 ? 0 : 1;

sealed class TrackingEffect(string mode) : ImageEffect
{
    public SKBitmap Input;
    public override SKBitmap Apply(SKBitmap bitmap)
    {
        Input = bitmap;
        if (mode == "throw") throw new InvalidOperationException("test failure");
        if (mode == "null") return null;
        if (mode == "same") return bitmap;
        SKBitmap result = bitmap.Copy();
        if (mode == "consume") bitmap.Dispose();
        return result;
    }
}

