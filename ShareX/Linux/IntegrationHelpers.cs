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

using Newtonsoft.Json.Linq;
using ShareX.HelpersLib;
using System;
using System.IO;
using System.Linq;

namespace ShareX;

public static class IntegrationHelpers
{
    private static string Applications => Path.Combine(LinuxPaths.DataHome, "applications");
    private static string Services => Path.Combine(LinuxPaths.DataHome, "kio", "servicemenus");
    private static string Scripts => Path.Combine(LinuxPaths.DataHome, "nautilus", "scripts");
    private static string Target => DesktopEntry.Quote(Environment.ProcessPath);
    private static string Entry(string name, string arguments, string extra = "") =>
        $"[Desktop Entry]\nType=Application\nName={name}\nExec={Target} {arguments}\nIcon=sharex\nTerminal=false\n{extra}";
    private static void SetFile(string path, bool create, string text)
    {
        if (LinuxPaths.Ephemeral) return;
        if (create) LinuxPaths.WritePrivateFile(path, text);
        else File.Delete(path);
    }

    public static bool CheckCustomUploaderExtension() => File.Exists(Path.Combine(Applications, "sharex-sxcu.desktop"));
    public static bool CheckImageEffectExtension() => File.Exists(Path.Combine(Applications, "sharex-sxie.desktop"));
    public static void CreateCustomUploaderExtension(bool create) => SetAssociation("sxcu", "CustomUploader", "custom uploader", create);
    public static void CreateImageEffectExtension(bool create) => SetAssociation("sxie", "ImageEffect", "image effect", create);
    private static void SetAssociation(string extension, string command, string description, bool create)
    {
        SetFile(Path.Combine(Applications, $"sharex-{extension}.desktop"), create,
            Entry($"ShareX {description}", $"-{command} %f", $"NoDisplay=true\nMimeType=application/x-sharex-{extension};\n"));
        string mime = Path.Combine(LinuxPaths.DataHome, "mime");
        SetFile(Path.Combine(mime, "packages", $"sharex-{extension}.xml"), create,
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\"><mime-type type=\"application/x-sharex-{extension}\"><comment>ShareX {description}</comment><glob pattern=\"*.{extension}\"/></mime-type></mime-info>\n");
        if (LinuxPaths.Ephemeral) return;
        if (Directory.Exists(mime)) LinuxDesktop.RunAsync("update-mime-database", new[] { mime }).GetAwaiter().GetResult();
        if (Directory.Exists(Applications)) LinuxDesktop.RunAsync("update-desktop-database", new[] { Applications }).GetAwaiter().GetResult();
    }

    public static bool CheckShellContextMenuButton() => File.Exists(Path.Combine(Services, "sharex-upload.desktop")) && File.Exists(Path.Combine(Scripts, "Upload with ShareX"));
    public static bool CheckEditShellContextMenuButton() => File.Exists(Path.Combine(Services, "sharex-edit.desktop"));
    public static void CreateShellContextMenuButton(bool create)
    {
        SetService("upload", "Upload with ShareX", "%F", "all/all;", create);
        string path = Path.Combine(Scripts, "Upload with ShareX");
        SetFile(path, create, "#!/bin/sh\nexec " + ShellQuote(Environment.ProcessPath) + " \"$@\"\n");
        if (create && !LinuxPaths.Ephemeral) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public static void CreateEditShellContextMenuButton(bool create) => SetService("edit", "Edit with ShareX", "-ImageEditor %f", "image/*;", create);
    private static string ShellQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";
    private static void SetService(string id, string name, string args, string mime, bool create) =>
        SetFile(Path.Combine(Services, $"sharex-{id}.desktop"), create,
            $"[Desktop Entry]\nType=Service\nMimeType={mime}\nActions=ShareX;\nX-KDE-ServiceTypes=KonqPopupMenu/Plugin\n[Desktop Action ShareX]\nName={name}\nIcon=sharex\nExec={Target} {args}\n");

    // Native-messaging host itself belongs to WP6; register only a built Linux host.
    private static string[] BrowserManifests(bool firefox) => firefox
        ? new[] { Path.Combine(LinuxPaths.Home, ".mozilla", "native-messaging-hosts", "ShareX.json") }
        : new[] { Path.Combine(LinuxPaths.ConfigHome, "chromium", "NativeMessagingHosts", "com.getsharex.sharex.json"),
                  Path.Combine(LinuxPaths.ConfigHome, "google-chrome", "NativeMessagingHosts", "com.getsharex.sharex.json") };
    public static bool CheckChromeExtensionSupport() => BrowserManifests(false).All(File.Exists);
    public static bool CheckFirefoxAddonSupport() => BrowserManifests(true).All(File.Exists);
    public static void CreateChromeExtensionSupport(bool create) => SetBrowser(false, create);
    public static void CreateFirefoxAddonSupport(bool create) => SetBrowser(true, create);
    private static void SetBrowser(bool firefox, bool create)
    {
        string host = FileHelpers.GetAbsolutePath("ShareX_NativeMessagingHost");
        if (!File.Exists(host)) host = FileHelpers.GetAbsolutePath("ShareX.NativeMessagingHost");
        if (create && !File.Exists(host)) throw new FileNotFoundException("Build and install the Linux ShareX.NativeMessagingHost before enabling browser support.", host);
        foreach (string path in BrowserManifests(firefox))
        {
            string manifest = "";
            if (create)
            {
                JObject json = JObject.Parse(File.ReadAllText(FileHelpers.GetAbsolutePath(firefox ? "host-manifest-firefox.json" : "host-manifest-chrome.json")));
                json["path"] = host;
                manifest = json.ToString();
            }
            SetFile(path, create, manifest);
        }
    }
    public static bool CheckSendToMenuButton() => CheckShellContextMenuButton();
    public static bool CreateSendToMenuButton(bool create) { CreateShellContextMenuButton(create); return true; }
    public static bool CheckSteamShowInApp() => false;
    public static void SteamShowInApp(bool showInApp) { }
    public static void Uninstall()
    {
        StartupManager.State = StartupState.Disabled;
        CreateShellContextMenuButton(false);
        CreateEditShellContextMenuButton(false);
        CreateCustomUploaderExtension(false);
        CreateImageEffectExtension(false);
        SetBrowser(false, false);
        SetBrowser(true, false);
    }
}
