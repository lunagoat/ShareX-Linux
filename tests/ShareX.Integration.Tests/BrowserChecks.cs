// ShareX Linux integration checks. GPL-3.0-or-later.
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using ShareX;
using static NetworkChecks;

internal static class BrowserChecks
{
    internal static async Task RunAsync(string sandbox)
    {
        Assert(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).StartsWith(sandbox), "Browser tests require sandbox HOME");
        // Register manifests through the same settings callbacks, using the test output directory.
        string hostMarker = Path.Combine(AppContext.BaseDirectory, "ShareX_NativeMessagingHost");
        if (!File.Exists(hostMarker)) await File.WriteAllTextAsync(hostMarker, "test fixture");
        IntegrationHelpers.CreateChromeExtensionSupport(true);
        IntegrationHelpers.CreateFirefoxAddonSupport(true);
        Assert(IntegrationHelpers.CheckChromeExtensionSupport() && IntegrationHelpers.CheckFirefoxAddonSupport(), "browser manifests registered");
        string chrome = Path.Combine(sandbox, "config/chromium/NativeMessagingHosts/com.getsharex.sharex.json");
        Assert(Path.IsPathFullyQualified((string)JObject.Parse(File.ReadAllText(chrome))["path"]!), "manifest absolute host path");
        IntegrationHelpers.CreateChromeExtensionSupport(false);
        IntegrationHelpers.CreateFirefoxAddonSupport(false);
        Assert(!IntegrationHelpers.CheckChromeExtensionSupport() && !IntegrationHelpers.CheckFirefoxAddonSupport(), "browser manifests removed");
        Console.WriteLine("PASS Chromium, Chrome and Firefox native messaging manifest registration/removal");

        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string binaries = Path.Combine(root, "ShareX/bin/Debug/net10.0");
        string fixture = Path.Combine(sandbox, "native-host"); Directory.CreateDirectory(fixture);
        foreach (string source in Directory.EnumerateFiles(binaries))
        {
            string name = Path.GetFileName(source);
            if (name == "ShareX") continue;
            string destination = Path.Combine(fixture, name);
            if (File.Exists(destination)) File.Delete(destination);
            File.Copy(source, destination);
        }
        string app = Path.Combine(fixture, "ShareX");
        await File.WriteAllTextAsync(app, "#!/bin/sh\ncp -- \"$2\" \"$(dirname -- \"$0\")/received.json\"\nrm -- \"$2\"\n");
        File.SetUnixFileMode(app, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using Process process = new() { StartInfo = new ProcessStartInfo(Path.Combine(fixture, "ShareX_NativeMessagingHost"))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add("chrome-extension://local-test/");
        process.Start();
        string json = "{\"Action\":\"UploadText\",\"Text\":\"native ü payload\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await process.StandardInput.BaseStream.WriteAsync(BitConverter.GetBytes(bytes.Length));
        await process.StandardInput.BaseStream.WriteAsync(bytes);
        process.StandardInput.Close();
        byte[] size = new byte[4]; await process.StandardOutput.BaseStream.ReadExactlyAsync(size);
        byte[] echoed = new byte[BitConverter.ToInt32(size)]; await process.StandardOutput.BaseStream.ReadExactlyAsync(echoed);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert(process.ExitCode == 0 && Encoding.UTF8.GetString(echoed) == json, "native messaging framing/UTF8: " + await process.StandardError.ReadToEndAsync());
        for (int i = 0; i < 40 && !File.Exists(Path.Combine(fixture, "received.json")); i++) await Task.Delay(25);
        Assert(File.ReadAllText(Path.Combine(fixture, "received.json")) == json, "native messaging forwarded payload");
        Console.WriteLine("PASS native messaging binary stdin/stdout framing and subprocess payload handoff");
    }
}
