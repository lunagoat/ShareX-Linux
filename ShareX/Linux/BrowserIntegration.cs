// ShareX - GPL-3.0-or-later. Per-user browser native messaging registration.
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShareX.Linux;

internal static class BrowserIntegration
{
    private static IEnumerable<string> Manifests(bool firefox)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        if (firefox) yield return Path.Combine(home, ".mozilla", "native-messaging-hosts", "ShareX.json");
        else
        {
            yield return Path.Combine(config, "google-chrome", "NativeMessagingHosts", "com.getsharex.sharex.json");
            yield return Path.Combine(config, "chromium", "NativeMessagingHosts", "com.getsharex.sharex.json");
        }
    }

    internal static bool IsRegistered(bool firefox) => Manifests(firefox).All(path =>
    {
        try
        {
            return File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["path"] == HostPath;
        }
        catch { return false; }
    });

    private static string HostPath => Path.Combine(AppContext.BaseDirectory, "ShareX_NativeMessagingHost");

    internal static void Register(bool firefox, bool enabled)
    {
        if (enabled && !File.Exists(HostPath)) throw new FileNotFoundException("ShareX browser host is missing.", HostPath);
        foreach (string path in Manifests(firefox))
        {
            if (!enabled) { if (File.Exists(path)) File.Delete(path); continue; }
            string template = Path.Combine(AppContext.BaseDirectory, firefox ? "host-manifest-firefox.json" : "host-manifest-chrome.json");
            JObject manifest = JObject.Parse(File.ReadAllText(template));
            manifest["path"] = HostPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, manifest.ToString());
        }
    }
}
