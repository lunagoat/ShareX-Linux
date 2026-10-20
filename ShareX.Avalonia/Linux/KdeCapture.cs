// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Drawing;
using System.Text.Json;
using Tmds.DBus.Protocol;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>KWin scripting uses compositor coordinates, including for XWayland windows and the global pointer.</summary>
public static class KdeCapture
{
    public static bool IsSession => Detect(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
        Environment.GetEnvironmentVariable("KDE_FULL_SESSION"), NativeRegionSelector.FindExecutable("kscreen-doctor") != null,
        NiriIpc.IsAvailable || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")));

    public static bool Detect(string? desktop, string? fullSession, bool hasKScreen, bool otherCompositor) =>
        (desktop ?? "").Split(':').Any(d => d.Equals("KDE", StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(fullSession, "true", StringComparison.OrdinalIgnoreCase) || fullSession == "1" ||
        (string.IsNullOrWhiteSpace(desktop) && hasKScreen && !otherCompositor);

    public static bool UseActiveMonitor(bool requested, bool kde, bool niri) => !kde && (requested || niri);

    public static async Task<Point?> CursorPositionAsync()
    {
        try
        {
            string json = await RunScriptAsync("var p = workspace.cursorPos; report({ x: p.x, y: p.y });").ConfigureAwait(false);
            using JsonDocument result = JsonDocument.Parse(json);
            return new Point((int)result.RootElement.GetProperty("x").GetDouble(), (int)result.RootElement.GetProperty("y").GetDouble());
        }
        catch (Exception) { return null; }
    }

    public static async Task<bool> PlaceAsync(string title, Rectangle bounds, Func<string, Task<string>>? runScript = null)
    {
        runScript ??= RunScriptAsync;
        string find = "var w = workspace.windowList().find(function(w) { return (w.captionNormal || w.caption) === " + JsonSerializer.Serialize(title) + "; });\n";
        string measure = "if (w) { var r = w.frameGeometry; report({ x: r.x, y: r.y, width: r.width, height: r.height }); } else { report(null); }";
        try
        {
            // Opened precedes mapping. Also verify after KWin has processed the asynchronous XWayland configure.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await runScript(find + "if (w) { w.fullScreen = false; w.setMaximize(false, false); w.noBorder = true; w.keepAbove = true; " +
                    FormattableString.Invariant($"w.frameGeometry = {{ x: {bounds.X}, y: {bounds.Y}, width: {bounds.Width}, height: {bounds.Height} }}; }}\n") + measure).ConfigureAwait(false);
                await Task.Delay(150).ConfigureAwait(false);
                using JsonDocument actual = JsonDocument.Parse(await runScript(find + measure).ConfigureAwait(false));
                if (MatchesBounds(actual.RootElement, bounds)) return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    private static bool MatchesBounds(JsonElement actual, Rectangle bounds) => actual.ValueKind == JsonValueKind.Object &&
        Math.Abs(actual.GetProperty("x").GetDouble() - bounds.X) < 1 && Math.Abs(actual.GetProperty("y").GetDouble() - bounds.Y) < 1 &&
        Math.Abs(actual.GetProperty("width").GetDouble() - bounds.Width) < 1 && Math.Abs(actual.GetProperty("height").GetDouble() - bounds.Height) < 1;

    private static async Task<string> RunScriptAsync(string body)
    {
        using DBusConnection connection = new(DBusAddress.Session!);
        await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        ScriptReply reply = new();
        connection.AddMethodHandler(reply);
        string name = "sharex_capture_" + Guid.NewGuid().ToString("N");
        string file = Path.Combine(Path.GetTempPath(), name + ".js");
        string script = "function report(value) { callDBus(" + JsonSerializer.Serialize(connection.UniqueName) +
            ", '/org/sharex/Capture', 'org.sharex.Capture', 'Result', JSON.stringify(value)); }\n" + body;
        try
        {
            FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (FileStream stream = new(file, options))
            using (StreamWriter writer = new(stream)) await writer.WriteAsync(script).ConfigureAwait(false);
            int id = await connection.CallMethodAsync(Load(), (Message message, object? _) => message.GetBodyReader().ReadInt32(), null)
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (id < 0) throw new InvalidOperationException("KWin could not load the capture script.");
            await connection.CallMethodAsync(Call("/Scripting/Script" + id, "org.kde.kwin.Script", "run"))
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return await reply.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        finally
        {
            try { await connection.CallMethodAsync(Unload()).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception) { }
            File.Delete(file);
        }

        MessageBuffer Load()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: "/Scripting", @interface: "org.kde.kwin.Scripting", member: "loadScript", signature: "ss");
            writer.WriteString(file);
            writer.WriteString(name);
            return writer.CreateMessage();
        }
        MessageBuffer Call(string path, string iface, string member)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: path, @interface: iface, member: member);
            return writer.CreateMessage();
        }
        MessageBuffer Unload()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: "/Scripting", @interface: "org.kde.kwin.Scripting", member: "unloadScript", signature: "s");
            writer.WriteString(name);
            return writer.CreateMessage();
        }
    }

    private sealed class ScriptReply : IPathMethodHandler
    {
        public string Path => "/org/sharex/Capture";
        public bool HandlesChildPaths => false;
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.Request.InterfaceAsString != "org.sharex.Capture" || context.Request.MemberAsString != "Result")
            {
                context.ReplyUnknownMethodError();
                return default;
            }
            Completion.TrySetResult(context.Request.GetBodyReader().ReadString());
            using var reply = context.CreateReplyWriter(null);
            context.Reply(reply.CreateMessage());
            return default;
        }
    }
}

public sealed class CaptureOverlayPlacementException : InvalidOperationException
{
    public CaptureOverlayPlacementException() : base("KWin could not place the region overlay across the desktop. Install slurp on Plasma 6 Wayland and use SHAREX_REGION_SELECTOR=slurp, or check KWin scripting permissions.") { }
}
