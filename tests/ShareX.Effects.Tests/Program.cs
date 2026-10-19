// Applies every ImageEffect with its default settings to a test image on Linux and checks it neither throws nor
// returns a blank/null bitmap. Output PNGs go to $SX_SANDBOX/effects (or /tmp/sx-effects).
using ShareX.ImageEffectsLib;
using SkiaSharp;

string outDir = Path.Combine(Environment.GetEnvironmentVariable("SX_SANDBOX") ?? "/tmp/sx-effects", "effects");
Directory.CreateDirectory(outDir);

SKBitmap MakeSource()
{
    SKBitmap bmp = new(320, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
    using SKCanvas canvas = new(bmp);
    using SKPaint gradient = new() { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(320, 200), [SKColors.Red, SKColors.Lime, SKColors.Blue], null, SKShaderTileMode.Clamp) };
    canvas.DrawRect(0, 0, 320, 200, gradient);
    using SKPaint white = new() { Color = SKColors.White, IsAntialias = true };
    canvas.DrawCircle(160, 100, 60, white);
    using SKPaint black = new() { Color = SKColors.Black, IsAntialias = true, TextSize = 28 };
    canvas.DrawText("ShareX", 110, 110, black);
    return bmp;
}

bool Same(SKBitmap a, SKBitmap b) => a.Width == b.Width && a.Height == b.Height && a.Bytes.AsSpan().SequenceEqual(b.Bytes);
bool Blank(SKBitmap b)
{
    ReadOnlySpan<byte> bytes = b.Bytes;
    for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) return false;
    return true;
}

int failures = 0, passed = 0, unchanged = 0;
List<Type> types = typeof(ImageEffect).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ImageEffect).IsAssignableFrom(t)).OrderBy(t => t.Name).ToList();

Console.WriteLine($"{types.Count} effect types");
foreach (Type type in types)
{
    ImageEffect effect;
    try { effect = (ImageEffect)Activator.CreateInstance(type); }
    catch (Exception e) { Console.WriteLine($"SKIP {type.Name}: needs arguments ({e.GetType().Name})"); continue; }

    using SKBitmap source = MakeSource();
    using SKBitmap input = source.Copy(); // effects dispose the bitmap they are given
    try
    {
        using SKBitmap result = effect.Apply(input);
        if (result == null) { Console.WriteLine($"FAIL {type.Name}: returned null"); failures++; continue; }
        if (Blank(result)) { Console.WriteLine($"FAIL {type.Name}: output is fully transparent"); failures++; continue; }
        bool same = Same(source, result);
        if (same) unchanged++;
        using SKData png = SKImage.FromBitmap(result).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(outDir, type.Name + ".png"), png.ToArray());
        Console.WriteLine($"PASS {type.Name} {result.Width}x{result.Height}{(same ? " (unchanged at defaults)" : "")}");
        passed++;
    }
    catch (Exception e)
    {
        Console.WriteLine($"FAIL {type.Name}: {e.GetType().Name}: {e.Message}");
        failures++;
    }
}

Console.WriteLine($"RESULT: {passed} passed, {failures} failures, {unchanged} unchanged at default settings, of {types.Count} effects; output {outDir}");
return failures == 0 ? 0 : 1;
