// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Drawing;
using System.Text.RegularExpressions;
using ShareX.AvaloniaUI.Linux;
using Tmds.DBus.Protocol;

// Run ONLY under a private bus: dbus-run-session -- env SHAREX_TEST_PRIVATE_BUS=1 dotnet run --project ... -- --fake-kwin
internal sealed class KWinDbusChecks : IPathMethodHandler
{
    public string Path => "/Scripting";
    public bool HandlesChildPaths => true;
    private string script;
    private string file;
    public int Loads, Runs, Unloads;
    public bool Denied;
    public List<string> Files = [];
    public List<string> Scripts = [];

    public static async Task RunAsync(Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("SHAREX_TEST_PRIVATE_BUS") != "1")
            throw new InvalidOperationException("Use dbus-run-session with SHAREX_TEST_PRIVATE_BUS=1; never register a fake KWin on the desktop bus.");
        using DBusConnection service = new(DBusAddress.Session!);
        await service.ConnectAsync();
        await service.RequestNameAsync("org.kde.KWin");
        KWinDbusChecks fake = new();
        service.AddMethodHandler(fake);
        Point? pointer = await KdeCapture.CursorPositionAsync();
        check(pointer == new Point(3100, 400), "KWin private D-Bus fake: compositor cursor on right monitor, without XQueryPointer");
        check(await KdeCapture.PlaceAsync("ShareX [unique]", new Rectangle(0, 0, 4480, 1440)),
            "KWin private D-Bus fake: loadScript/run/callback/measure/unloadScript sequence");
        check(fake.Loads == 4 && fake.Runs == 4 && fake.Unloads == 4 && fake.Files.All(f => !File.Exists(f)),
            "KWin private D-Bus fake: scripts unloaded and private temporary files deleted");
        check(fake.Scripts.Any(s => s.Contains("noBorder = true") && s.Contains("keepAbove = true") &&
            s.Contains("skipSwitcher = true")) && fake.Scripts.Any(s => s.Contains("w.clientGeometry") && s.Contains("workspace.stackingOrder")) &&
            fake.Scripts.Any(s => s.Contains("workspace.raiseWindow(w)")),
            "KWin private D-Bus fake: borderless above-panel preparation and client geometry verification reach the bus");
        var geometry = DoctorDisplays.FormatKdeGeometry(await KdeCapture.DesktopGeometryAsync());
        check(geometry.Any(l => l.Contains("full 0,0 1920x1080; workarea 0,44 1920x1036")) &&
            geometry.Any(l => l.Contains("panel: 0,0 1920x44")), "KWin private D-Bus fake: doctor reports full output, reserved workarea and panel");
        check(fake.Files.All(f => !File.Exists(f)) && fake.Loads == fake.Unloads,
            "KWin private D-Bus fake: diagnostic scripts also unload and delete files");
        fake.Denied = true;
        check(await KdeCapture.DesktopGeometryAsync() == null && DoctorDisplays.FormatKdeGeometry(null)[0].Contains("unavailable"),
            "KWin private D-Bus fake: denied geometry diagnostic fails soft");
        check(await KdeCapture.CursorPositionAsync() == null, "KWin private D-Bus fake: permission denial does not fabricate a cursor");
        check(!await KdeCapture.PlaceAsync("ShareX [unique]", new Rectangle(0, 0, 4480, 1440)),
            "KWin private D-Bus fake: permission denial rejects overlay placement");
        check(fake.Loads == fake.Unloads && fake.Files.All(f => !File.Exists(f)),
            "KWin private D-Bus fake: denial paths also unload scripts and remove every temporary file");
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        string member = context.Request.MemberAsString;
        if (member == "loadScript")
        {
            var reader = context.Request.GetBodyReader();
            file = reader.ReadString();
            string name = reader.ReadString();
            if (!name.StartsWith("sharex_capture_")) throw new Exception("Unexpected script name");
            script = File.ReadAllText(file);
            if ((File.GetUnixFileMode(file) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                throw new Exception("KWin script must be private to the user");
            Files.Add(file);
            Scripts.Add(script);
            Loads++;
            using var reply = context.CreateReplyWriter("i");
            reply.WriteInt32(Denied ? -1 : 7);
            context.Reply(reply.CreateMessage());
        }
        else if (member == "run")
        {
            if (context.Request.PathAsString != "/Scripting/Script7") throw new Exception("Wrong script object path");
            Runs++;
            Match destination = Regex.Match(script, "callDBus\\(\"([^\"]+)\"");
            if (!destination.Success) throw new Exception("Script does not callback on its own D-Bus connection");
            using var call = context.Connection.GetMessageWriter();
            call.WriteMethodCallHeader(destination: destination.Groups[1].Value, path: "/org/sharex/Capture",
                @interface: "org.sharex.Capture", member: "Result", signature: "s");
            call.WriteString(script.Contains("KWin.MaximizeArea") ?
                """{"outputs":[{"name":"DP-1","full":{"x":0,"y":0,"width":1920,"height":1080},"work":{"x":0,"y":44,"width":1920,"height":1036}}],"panels":[{"geometry":{"x":0,"y":0,"width":1920,"height":44},"keepAbove":false,"keepBelow":false}]}""" :
                script.Contains("workspace.cursorPos") ? "{\"x\":3100,\"y\":400}" :
                "{\"x\":0,\"y\":0,\"width\":4480,\"height\":1440,\"frame\":{\"x\":0,\"y\":0,\"width\":4480,\"height\":1440},\"noBorder\":true,\"keepAbove\":true,\"abovePanels\":true}");
            context.Connection.TrySendMessage(call.CreateMessage());
            using var reply = context.CreateReplyWriter(null);
            context.Reply(reply.CreateMessage());
        }
        else if (member == "unloadScript")
        {
            Unloads++;
            using var reply = context.CreateReplyWriter("b");
            reply.WriteBool(true);
            context.Reply(reply.CreateMessage());
        }
        else context.ReplyUnknownMethodError();
        return default;
    }
}
