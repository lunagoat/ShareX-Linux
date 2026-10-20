#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using SkiaSharp;
using Tmds.DBus.Protocol;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>Uses one D-Bus connection for the request and its response (CLI calls cannot do this).</summary>
public static class ScreenshotPortal
{
    public static async Task<SKBitmap> CaptureAsync()
    {
        using DBusConnection connection = new(DBusAddress.Session!);
        await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        string token = "sharex_" + Guid.NewGuid().ToString("N");
        string path = "/org/freedesktop/portal/desktop/request/" + connection.UniqueName!.TrimStart(':').Replace('.', '_') + "/" + token;
        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = await connection.WatchSignalAsync(
            sender: "org.freedesktop.portal.Desktop", path: path,
            @interface: "org.freedesktop.portal.Request", signal: "Response",
            reader: (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                uint response = reader.ReadUInt32();
                var values = reader.ReadDictionaryOfStringToVariantValue();
                return (response, values.TryGetValue("uri", out var uri) ? uri.GetString() : "");
            },
            handler: (Notification<(uint Code, string Uri)> notification) =>
            {
                if (!notification.HasValue) completion.TrySetException(notification.Exception);
                else if (notification.Value.Code == 0 && !string.IsNullOrEmpty(notification.Value.Uri)) completion.TrySetResult(notification.Value.Uri);
                else completion.TrySetException(new OperationCanceledException("Screen capture was cancelled or denied by the desktop portal."));
            }, state: null, emitOnCapturedContext: false, flags: ObserverFlags.None).ConfigureAwait(false);
        await connection.CallMethodAsync(CreateRequest()).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        string uri = await completion.Task.WaitAsync(TimeSpan.FromSeconds(55)).ConfigureAwait(false);
        Uri file = new(uri);
        if (!file.IsFile) throw new InvalidOperationException("Screenshot portal did not return a local file.");
        return SKBitmap.Decode(file.LocalPath) ?? throw new InvalidOperationException("Cannot decode the screenshot portal image.");

        MessageBuffer CreateRequest()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.freedesktop.portal.Desktop", path: "/org/freedesktop/portal/desktop",
                @interface: "org.freedesktop.portal.Screenshot", member: "Screenshot", signature: "sa{sv}");
            writer.WriteString("");
            writer.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token, ["interactive"] = false, ["modal"] = false });
            return writer.CreateMessage();
        }
    }
}
