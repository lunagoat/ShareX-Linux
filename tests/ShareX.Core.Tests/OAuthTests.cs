using ShareX.UploadersLib;
using HttpMethod = ShareX.UploadersLib.HttpMethod;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

internal static class OAuthTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("OAuth RSA-SHA1 signature verification", RsaSignature));
        tests.Add(("OAuth encoded ordinal parameter sorting", ParameterSorting));
    }

    private static Task RsaSignature()
    {
        using var rsa = RSA.Create(2048);
        var oauth = new OAuthInfo
        {
            ConsumerKey = "consumer",
            ConsumerPrivateKey = rsa.ToXmlString(true),
            SignatureMethod = OAuthInfo.OAuthInfoSignatureMethod.RSA_SHA1
        };
        OAuthManager.GenerateQuery("https://unit.invalid/resource", null, HttpMethod.GET, oauth, out var parameters);
        byte[] signature = Convert.FromBase64String(parameters["oauth_signature"]);
        parameters.Remove("oauth_signature");
        string normalized = string.Join("&", parameters.Select(p => (Key: Uri.EscapeDataString(p.Key), Value: Uri.EscapeDataString(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Value, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        string signatureBase = "GET&" + Uri.EscapeDataString("https://unit.invalid/resource") + "&" + Uri.EscapeDataString(normalized);
        Program.Check(rsa.VerifyData(Encoding.ASCII.GetBytes(signatureBase), signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1),
            "OAuth signature does not verify as RSA-SHA1.");
        return Task.CompletedTask;
    }

    private static Task ParameterSorting()
    {
        var normalize = typeof(OAuthManager).GetMethod("NormalizeParameters", BindingFlags.NonPublic | BindingFlags.Static);
        var parameters = new Dictionary<string, string> { ["z"] = "a", ["a b"] = "c", ["a~"] = "x", ["a!"] = "y", ["Z"] = "last" };
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "tr-TR", "sv-SE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string normalized = (string)normalize.Invoke(null, [parameters]);
                Program.Check(normalized == "Z=last&a%20b=c&a%21=y&a~=x&z=a", "OAuth parameters incorrectly encoded/sorted under " + culture + ": " + normalized);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
        return Task.CompletedTask;
    }
}
