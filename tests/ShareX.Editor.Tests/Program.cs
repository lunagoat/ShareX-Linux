// Annotations are drawn by the Avalonia view (not EditorCore.Render), so this checks the model: annotation creation, undo/redo,
// image operations and exports. View-level rendering is covered by the capture tests (EditorView) and live runs.
// Drives ShareX.ImageEditor's EditorCore without a window: draws with every annotation tool through pointer events,
// checks undo/redo, the image operations, and exports to PNG/JPEG/WebP. Output goes to $SX_SANDBOX/editor.
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Core.Editor;
using SkiaSharp;

string outDir = Path.Combine(Environment.GetEnvironmentVariable("SX_SANDBOX") ?? "/tmp/sx-editor", "editor");
Directory.CreateDirectory(outDir);
int failures = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); if (!ok) failures++; }

SKBitmap Source()
{
    SKBitmap bmp = new(640, 400, SKColorType.Bgra8888, SKAlphaType.Premul);
    using SKCanvas canvas = new(bmp);
    using SKPaint p = new() { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(640, 400), [SKColors.CornflowerBlue, SKColors.Orange, SKColors.SeaGreen], null, SKShaderTileMode.Clamp) };
    canvas.DrawRect(0, 0, 640, 400, p);
    for (int i = 0; i < 640; i += 40) { using SKPaint l = new() { Color = SKColors.White.WithAlpha(90) }; canvas.DrawLine(i, 0, i, 400, l); }
    return bmp;
}

SKBitmap RenderAll(EditorCore core)
{
    SKBitmap bmp = new(core.SourceImage.Width, core.SourceImage.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
    using SKCanvas canvas = new(bmp);
    core.Render(canvas);
    return bmp;
}

bool Differs(SKBitmap a, SKBitmap b) => a.Width != b.Width || a.Height != b.Height || !a.Bytes.AsSpan().SequenceEqual(b.Bytes);

void Drag(EditorCore core, SKPoint from, SKPoint to)
{
    core.OnPointerPressed(from);
    for (int i = 1; i <= 8; i++) core.OnPointerMoved(new SKPoint(from.X + (to.X - from.X) * i / 8, from.Y + (to.Y - from.Y) * i / 8));
    core.OnPointerReleased(to);
}

// --- every drawing tool ---
EditorTool[] drawTools = [EditorTool.Rectangle, EditorTool.Ellipse, EditorTool.Line, EditorTool.Arrow, EditorTool.Freehand, EditorTool.Step,
    EditorTool.Highlight, EditorTool.Blur, EditorTool.Pixelate, EditorTool.Magnify, EditorTool.Spotlight, EditorTool.SpeechBalloon, EditorTool.Text];
foreach (EditorTool tool in drawTools)
{
    try
    {
        using EditorCore core = new();
        core.LoadImage(Source());
        core.ActiveTool = tool;
        int count = core.Annotations.Count;
        Drag(core, new SKPoint(120, 90), new SKPoint(420, 260));
        bool added = core.Annotations.Count > count;
        Check(added, $"{tool}: annotation added");
        if (added)
        {
            core.Undo();
            Check(core.Annotations.Count == count, $"{tool}: undo removes it");
            core.Redo();
            Check(core.Annotations.Count == count + 1, $"{tool}: redo restores it");
            using SKBitmap final = RenderAll(core);
            using SKData png = SKImage.FromBitmap(final).Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(outDir, tool + ".png"), png.ToArray());
        }
    }
    catch (Exception e) { Check(false, $"{tool}: threw {e.GetType().Name}: {e.Message}"); }
}

// --- image operations ---
try
{
    using EditorCore core = new();
    core.LoadImage(Source());
    Check(core.Rotate90Clockwise() && core.SourceImage.Width == 400 && core.SourceImage.Height == 640, "rotate 90 cw swaps dimensions");
    Check(core.Rotate90CounterClockwise() && core.SourceImage.Width == 640, "rotate 90 ccw restores");
    Check(core.Rotate180() && core.SourceImage.Width == 640, "rotate 180");
    Check(core.FlipHorizontal() && core.FlipVertical(), "flip horizontal and vertical");
    Check(core.ResizeImage(320, 200) && core.SourceImage.Width == 320, "resize image");
    Check(core.ResizeCanvas(10, 10, 10, 10, SKColors.Black) && core.SourceImage.Width == 340, "resize canvas adds borders");
    Check(core.Crop(new SKRect(20, 20, 220, 120)) && core.SourceImage.Width == 200 && core.SourceImage.Height == 100, "crop to rectangle");
    Check(core.RotateCustomAngle(15f, true), "rotate custom angle");
    core.Undo();
    Check(core.CanRedo, "undo of image operation is redoable");
}
catch (Exception e) { Check(false, $"image operations threw {e.GetType().Name}: {e.Message}"); }

// --- export formats ---
try
{
    using EditorCore core = new();
    core.LoadImage(Source());
    core.ActiveTool = EditorTool.Rectangle;
    Drag(core, new SKPoint(100, 100), new SKPoint(300, 250));
    using SKBitmap flat = RenderAll(core);
    foreach ((string ext, SKEncodedImageFormat fmt) in new[] { ("png", SKEncodedImageFormat.Png), ("jpg", SKEncodedImageFormat.Jpeg), ("webp", SKEncodedImageFormat.Webp) })
    {
        using SKData data = SKImage.FromBitmap(flat).Encode(fmt, 90);
        string path = Path.Combine(outDir, "export." + ext);
        File.WriteAllBytes(path, data.ToArray());
        using SKBitmap back = SKBitmap.Decode(path);
        Check(back != null && back.Width == 640 && back.Height == 400, $"export {ext} round-trips");
    }
}
catch (Exception e) { Check(false, $"export threw {e.GetType().Name}: {e.Message}"); }

// --- text uses a Linux font ---
using (SKTypeface face = SKTypeface.FromFamilyName("sans-serif"))
    Check(face != null && face.FamilyName.Length > 0, $"fontconfig resolves sans-serif ({face?.FamilyName})");

Console.WriteLine($"RESULT: {failures} failures; output {outDir}");
return failures == 0 ? 0 : 1;
