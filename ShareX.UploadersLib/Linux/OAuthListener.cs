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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ShareX.HelpersLib;
using ShareX.UploadersLib.Properties;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ShareX.UploadersLib;

public class OAuthListener : IDisposable
{
    public IOAuth2Loopback OAuth { get; }
    private HttpListener listener;

    public OAuthListener(IOAuth2Loopback oauth)
    {
        OAuth = oauth;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref listener, null)?.Close();
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Dispose();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        CancellationToken token = timeout.Token;
        HttpListener active = new();
        listener = active;
        string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            // The port probe is inherently racy; retry binding before publishing the redirect.
            for (int attempt = 0; ; attempt++)
            {
                string redirect = $"http://127.0.0.1:{WebHelpers.GetRandomUnusedPort()}/";
                active.Prefixes.Clear();
                active.Prefixes.Add(redirect);
                try { active.Start(); OAuth.RedirectURI = redirect; break; }
                catch (HttpListenerException) when (attempt < 9) { }
            }
            OAuth.State = state;
            string url = await OAuth.GetAuthorizationURLAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(url)) return false;
            URLHelpers.OpenURL(url);
            // Never log the authorization URL: it can contain credentials or PKCE state.
            while (true)
            {
                HttpListenerContext context = await active.GetContextAsync().WaitAsync(token).ConfigureAwait(false);
                string code = context.Request.QueryString["code"];
                bool validState = context.Request.QueryString["state"] == state;
                bool callback = context.Request.Url.AbsolutePath == "/" && validState;
                using (HttpListenerResponse response = context.Response)
                {
                    response.StatusCode = callback ? 200 : 400;
                    response.ContentType = "text/html; charset=utf-8";
                    string status = !callback ? Localization.Strings.OAuthListener_Invalid_state_parameter :
                        !string.IsNullOrEmpty(code) ? Localization.Strings.OAuthListener_Authorization_completed_successfully :
                        Localization.Strings.OAuthListener_Authorization_did_not_succeed;
                    byte[] buffer = Encoding.UTF8.GetBytes(Resources.OAuthCallbackPage.Replace("{0}", WebUtility.HtmlEncode(status)));
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, token).ConfigureAwait(false);
                }
                if (callback)
                {
                    return !string.IsNullOrEmpty(code) && await OAuth.GetAccessTokenAsync(code, token).ConfigureAwait(false);
                }
            }
        }
        catch (ObjectDisposedException) { return false; }
        catch (HttpListenerException) when (!active.IsListening) { return false; }
        finally { Dispose(); }
    }
}
