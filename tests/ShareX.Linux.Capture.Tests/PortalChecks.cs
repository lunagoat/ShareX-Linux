using ShareX.AvaloniaUI.Linux;
using SkiaSharp;
using Tmds.DBus.Protocol;

internal sealed class PortalChecks : IPathMethodHandler
{
    public string Path => "/org/freedesktop/portal/desktop";
    public bool HandlesChildPaths => false;
    private readonly string _uri;
    public uint Response { get; set; }
    private PortalChecks(string uri) => _uri = uri;

    public static async Task RunAsync()
    {
        Directory.CreateDirectory("/tmp/sx-wp3/evidence");
        using (SKBitmap bitmap = new(31, 17))
        {
            bitmap.Erase(SKColors.Coral);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes("/tmp/sx-wp3/evidence/portal-fixture.png", png.ToArray());
        }
        using DBusConnection service = new(DBusAddress.Session!);
        await service.ConnectAsync();
        await service.RequestNameAsync("org.freedesktop.portal.Desktop");
        PortalChecks portal = new(new Uri("/tmp/sx-wp3/evidence/portal-fixture.png").AbsoluteUri);
        service.AddMethodHandler(portal);
        using (var image = await ScreenshotPortal.CaptureAsync())
        {
            if (image.Width != 31 || image.Height != 17 || image.GetPixel(10, 10) != SKColors.Coral)
                throw new Exception("Portal response did not decode the expected pixels.");
            Console.WriteLine("PASS: real D-Bus portal request/Response signal/PNG URI decoding");
        }
        portal.Response = 1;
        try { using var image = await ScreenshotPortal.CaptureAsync(); throw new Exception("Portal cancellation was ignored."); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: portal denial/cancellation returns a controlled exception"); }
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.Request.MemberAsString != "Screenshot") { context.ReplyUnknownMethodError(); return default; }
        var reader = context.Request.GetBodyReader();
        reader.ReadString();
        var options = reader.ReadDictionaryOfStringToVariantValue();
        string token = options["handle_token"].GetString();
        string sender = context.Request.SenderAsString!;
        string path = "/org/freedesktop/portal/desktop/request/" + sender.TrimStart(':').Replace('.', '_') + "/" + token;
        using (var reply = context.CreateReplyWriter("o"))
        {
            reply.WriteObjectPath(path);
            context.Reply(reply.CreateMessage());
        }
        using (var signal = context.Connection.GetMessageWriter())
        {
            signal.WriteSignalHeader(destination: sender, path: path, @interface: "org.freedesktop.portal.Request", member: "Response", signature: "ua{sv}");
            signal.WriteUInt32(Response);
            signal.WriteDictionary(new Dictionary<string, VariantValue> { ["uri"] = _uri });
            context.Connection.TrySendMessage(signal.CreateMessage());
        }
        return default;
    }
}
