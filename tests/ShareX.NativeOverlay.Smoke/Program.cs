// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
// Smoke test for the per-output layer-shell session. Only runs inside a nested compositor whose socket is named sx-*.
using ShareX.NativeOverlay;
using ShareX.NativeOverlay.Wayland;

string display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
if (!display.StartsWith("sx-"))
{
    Console.Error.WriteLine($"refusing: WAYLAND_DISPLAY='{display}' is not a nested test compositor (socket must start with sx-)");
    return 9;
}

WaylandOverlay.Register();
int seconds = args.Length > 0 ? int.Parse(args[0]) : 30;
using var session = OverlaySessionFactory.Open();
Console.WriteLine($"desktop {session.Desktop}");
for (int i = 0; i < session.Outputs.Count; i++)
    Console.WriteLine($"output {i} {session.Outputs[i].Name} logical {session.Outputs[i].Logical} scale {session.Outputs[i].Scale} pixels {session.PixelSize(i)}");

var frames = new byte[session.Outputs.Count][];
var presents = new int[session.Outputs.Count];
uint[] colors = { 0xFF204060, 0xFF603020, 0xFF206030, 0xFF603060 };

void Full(int i)
{
    var (w, h) = session.PixelSize(i);
    frames[i] = new byte[w * h * 4];
    var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frames[i].AsSpan());
    span.Fill(colors[i % colors.Length]);
    session.Present(i, frames[i], w * 4, null);
    presents[i]++;
}
for (int i = 0; i < frames.Length; i++) Full(i);

int last = -1; OverlayRect? lastRect = null;
var stop = DateTime.UtcNow.AddSeconds(seconds);
var reader = session.Events;
while (DateTime.UtcNow < stop)
{
    using var cts = new CancellationTokenSource(250);
    OverlayEvent ev;
    try { if (!await reader.WaitToReadAsync(cts.Token)) break; ev = await reader.ReadAsync(); }
    catch (OperationCanceledException) { continue; }
    switch (ev)
    {
        case LayoutChanged:
            Console.WriteLine("layout changed");
            frames = new byte[session.Outputs.Count][]; presents = new int[session.Outputs.Count];
            for (int i = 0; i < frames.Length; i++) Full(i);
            last = -1; lastRect = null;
            break;
        case PointerMoved m:
            for (int i = 0; i < session.Outputs.Count; i++)
            {
                var o = session.Outputs[i];
                if (m.X < o.Logical.X || m.X >= o.Logical.Right || m.Y < o.Logical.Y || m.Y >= o.Logical.Bottom) continue;
                var (w, h) = session.PixelSize(i);
                if (last >= 0 && last != i && lastRect is { } old) { Restore(last, old); }
                var rect = new OverlayRect((int)((m.X - o.Logical.X) * o.Scale) - 20, (int)((m.Y - o.Logical.Y) * o.Scale) - 20, 40, 40);
                rect = new OverlayRect(Math.Max(0, rect.X), Math.Max(0, rect.Y), Math.Min(40, w - Math.Max(0, rect.X)), Math.Min(40, h - Math.Max(0, rect.Y)));
                if (last == i && lastRect is { } prev) Restore(i, prev);
                var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frames[i].AsSpan());
                for (int y = rect.Y; y < rect.Bottom; y++) span.Slice(y * w + rect.X, rect.Width).Fill(0xFFFFFFFF);
                session.Present(i, frames[i], w * 4, OverlayRect.Union(rect, lastRect is { } l && last == i ? l : rect));
                presents[i]++;
                last = i; lastRect = rect;
            }
            break;
        case PointerButton b: Console.WriteLine($"button {b.Button} {(b.Pressed ? "down" : "up")} at {b.X:0.#},{b.Y:0.#}"); break;
        case KeyEvent { Pressed: true } k:
            Console.WriteLine($"key evdev {k.EvdevCode} sym 0x{k.Keysym:x} text '{k.Text}'");
            if (k.EvdevCode == 1) goto done;
            break;
        case OverlayClosed: Console.WriteLine("closed by compositor"); goto done;
    }
}
done:
Console.WriteLine("presents per output: " + string.Join(", ", presents));
return 0;

void Restore(int i, OverlayRect r)
{
    var (w, _) = session.PixelSize(i);
    var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frames[i].AsSpan());
    for (int y = r.Y; y < r.Bottom; y++) span.Slice(y * w + r.X, r.Width).Fill(colors[i % colors.Length]);
}
