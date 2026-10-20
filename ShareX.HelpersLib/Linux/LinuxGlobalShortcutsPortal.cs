// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace ShareX.HelpersLib;

/// <summary>A session-scoped GlobalShortcuts portal client. Request subscriptions are installed before
/// method calls, so immediate portal responses cannot race subscription setup.</summary>
public sealed class LinuxGlobalShortcutsPortal : IDisposable
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string Path = "/org/freedesktop/portal/desktop";
    private const string Interface = "org.freedesktop.portal.GlobalShortcuts";
    private DBusConnection connection;
    private IDisposable activation;
    private string session;
    private bool disposed;
    public event Action<string> Activated;
    public event Action SessionClosed;
    private IDisposable closed;

    public string AppId { get; private set; }

    public async Task CreateAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        connection = new DBusConnection(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? throw new InvalidOperationException("No session D-Bus is available."));
        await connection.ConnectAsync();
        // Unsandboxed launches need an explicit identity (not inferred from a systemd application scope).
        // Flatpak provides its own identity and disallows this host-only method.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")))
        {
            string[] candidates = ["sharex", "io.github.ShareX.ShareX", "com.getsharex.ShareX", "sharex-sxcu", "sharex-sxie"];
            foreach (string candidate in candidates)
            {
                try
                {
                    await connection.CallMethodAsync(BuildRegistration(candidate));
                    AppId = candidate;
                    break;
                }
                catch (DBusErrorReplyException exception) when (exception.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface")
                {
                    break;
                }
                catch (DBusErrorReplyException)
                {
                    // Candidate app info not found on host, try next
                }
            }
        }
        activation = await connection.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Path = Path, Interface = Interface, Member = "Activated" },
            (message, _) => { var reader = message.GetBodyReader(); return (reader.ReadObjectPathAsString(), reader.ReadString()); },
            notification => { if (notification.HasValue && notification.Value.Item1 == session) Activated?.Invoke(notification.Value.Item2); }, emitOnCapturedContext: false);
        var response = await RequestAsync("CreateSession", null, []);
        session = response.TryGetValue("session_handle", out VariantValue handle) ? handle.GetString() : throw new InvalidOperationException("Portal returned no session handle.");
        closed = await connection.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Path = session, Interface = "org.freedesktop.portal.Session", Member = "Closed" },
            (_, _) => true, notification => { if (notification.HasValue) SessionClosed?.Invoke(); }, emitOnCapturedContext: false);
    }

    public async Task<HashSet<string>> BindAsync(IReadOnlyList<(string Id, HotkeyInfo Info)> hotkeys)
    {
        if (session == null) await CreateAsync();
        var result = await RequestAsync("BindShortcuts", session, hotkeys);
        HashSet<string> accepted = new();
        if (result.TryGetValue("shortcuts", out VariantValue shortcuts))
            for (int i = 0; i < shortcuts.Count; i++) accepted.Add(shortcuts.GetItem(i).GetItem(0).GetString());
        return accepted;
    }

    private async Task<Dictionary<string, VariantValue>> RequestAsync(string method, string sessionHandle, IReadOnlyList<(string Id, HotkeyInfo Info)> hotkeys)
    {
        string token = "sharex_" + Guid.NewGuid().ToString("N");
        string expected = Path + "/request/" + connection.UniqueName.TrimStart(':').Replace('.', '_') + "/" + token;
        TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Values)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // NOTE: Notification<T>.Exception throws on a notification that carries a value, so always test HasValue first.
        using IDisposable response = await connection.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Path = expected, Interface = "org.freedesktop.portal.Request", Member = "Response" },
            (message, _) => { var reader = message.GetBodyReader(); return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue()); },
            notification => { if (notification.HasValue) completion.TrySetResult(notification.Value); else completion.TrySetException(notification.Exception); }, flags: ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnReaderFailed, emitOnCapturedContext: false);
        string request = await connection.CallMethodAsync(BuildRequest(method, token, sessionHandle, hotkeys),
            (message, _) => message.GetBodyReader().ReadObjectPathAsString());
        if (request != expected) throw new InvalidOperationException("Portal returned an unexpected request path.");
        try
        {
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
            if (result.Code != 0) throw new InvalidOperationException(result.Code == 1 ? "Global shortcut registration was cancelled." : "Global shortcut registration was denied by the desktop portal.");
            return result.Values;
        }
        catch
        {
            await CloseObjectAsync(request, "org.freedesktop.portal.Request");
            throw;
        }
    }

    private MessageBuffer BuildRequest(string method, string token, string sessionHandle, IReadOnlyList<(string Id, HotkeyInfo Info)> hotkeys)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Destination, path: Path, @interface: Interface, member: method,
            signature: method == "CreateSession" ? "a{sv}" : "oa(sa{sv})sa{sv}");
        if (method == "BindShortcuts")
        {
            writer.WriteObjectPath(sessionHandle);
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach ((string id, HotkeyInfo hotkey) in hotkeys)
            {
                writer.WriteStructureStart();
                writer.WriteString(id);
                writer.WriteDictionary(new Dictionary<string, VariantValue> {
                    // Compositors list this text, so name the action, not just the keys.
                    ["description"] = "ShareX: " + (string.IsNullOrEmpty(hotkey.Description) ? hotkey.ToString() : hotkey.Description + " (" + hotkey + ")"),
                    ["preferred_trigger"] = LinuxKeyNames.PortalTrigger(hotkey)
                });
            }
            writer.WriteArrayEnd(array);
            writer.WriteString(""); // unparented portal dialog; Wayland requires an exported surface token, not a raw pointer
        }
        Dictionary<string, VariantValue> options = new() { ["handle_token"] = token };
        if (method == "CreateSession") options["session_handle_token"] = "sharex_" + Guid.NewGuid().ToString("N");
        writer.WriteDictionary(options);
        return writer.CreateMessage();
    }

    private async Task CloseObjectAsync(string path, string @interface)
    {
        try
        {
            await connection.CallMethodAsync(BuildClose(path, @interface)).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { }
    }

    private MessageBuffer BuildRegistration(string appId)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Destination, path: Path, @interface: "org.freedesktop.host.portal.Registry", member: "Register", signature: "sa{sv}");
        writer.WriteString(appId);
        writer.WriteDictionary(new Dictionary<string, VariantValue>());
        return writer.CreateMessage();
    }

    private MessageBuffer BuildClose(string path, string @interface)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Destination, path: path, @interface: @interface, member: "Close");
        return writer.CreateMessage();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        activation?.Dispose();
        closed?.Dispose();
        // Close the session explicitly: compositors (Hyprland) keep the session's shortcuts listed until told it is gone,
        // which showed up as stale `sharex:N` entries after every re-registration.
        if (session != null && connection != null)
        {
            try { Task.WhenAny(CloseObjectAsync(session, "org.freedesktop.portal.Session"), Task.Delay(800)).GetAwaiter().GetResult(); }
            catch { }
        }
        // Disconnecting also destroys all sessions and outstanding requests owned by this connection.
        connection?.Dispose();
        session = null;
    }
}
