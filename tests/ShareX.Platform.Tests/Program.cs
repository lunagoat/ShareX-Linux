// ShareX platform regression harness, GPL-3.0-or-later.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Newtonsoft.Json;
using ShareX;
using ShareX.HelpersLib;
using ShareX.AvaloniaUI.Integration;
using SkiaSharp;
using System.Reflection;
using System.Security.Cryptography;

LinuxPaths.InitializeSandbox();
string root = Environment.GetEnvironmentVariable("SX_TEST_ROOT") ?? "/tmp/sx-wp1-platform";
Directory.CreateDirectory(root);
LinuxPaths.CredentialDirectory = Path.Combine(LinuxPaths.ConfigDirectory, "test-credentials");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
if (args.FirstOrDefault() == "--ipc-server")
{
    using SingleInstanceManager instance = new("platform-test", "platform-test", Array.Empty<string>());
    Check(instance.IsFirstInstance, "IPC server owns profile");
    TaskCompletionSource<string[]> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    instance.ArgumentsReceived += arguments => received.TrySetResult(arguments);
    File.WriteAllText(Path.Combine(root, "ipc-ready"), "ready");
    string[] forwarded = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
    File.WriteAllText(Path.Combine(root, "ipc-received.json"), System.Text.Json.JsonSerializer.Serialize(forwarded));
    return;
}
if (args.FirstOrDefault() == "--ipc-client")
{
    using SingleInstanceManager instance = new("platform-test", "platform-test", args.Skip(1).ToArray());
    Check(!instance.IsFirstInstance, "IPC client forwards to first instance");
    return;
}
if (args.FirstOrDefault() == "--security-read")
{
    Check(DPAPI.Decrypt(File.ReadAllText(Path.Combine(root, "ciphertext")), "entropy") == "password 雪\n\"'", "credentials decrypt across processes");
    return;
}
if (args.FirstOrDefault() == "--notify")
{
    string selected = null;
    bool shown = await LinuxNotifications.ShowAsync("ShareX platform test", "<test> & body", null, 1000,
        new Dictionary<string, string> { ["default"] = "Open", ["folder"] = "Folder" }, action => selected = action);
    Check(shown && selected == "folder", "freedesktop notification action routed to callback");
    LinuxNotifications.CloseAll();
    return;
}
if (args.FirstOrDefault() == "--ui")
{
    AppBuilder.Configure<ShareXAvaloniaApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
    var appAssembly = typeof(ApplicationConfig).Assembly;
    void Invoke(string type, string method) => appAssembly.GetType("ShareX." + type).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, null);
    var state = appAssembly.GetType("ShareX.ApplicationState");
    state.GetProperty("Settings", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, new ApplicationConfig { ShowStartScreen = false });
    using ApplicationSettingsViewModel vm = new();
    ApplicationSettingsWindow window = new();
    window.Show();
    foreach (var page in vm.NavigationItems)
    {
        vm.SelectedNavigationItem = page;
        window.DataContext = vm;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Check(window.Content != null, "settings page " + page.Id);
    }
    vm.SelectedNavigationItem = vm.NavigationItems.First(page => page.Id == "integration");
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame();
    frame?.Save(Path.Combine(root, "settings-integration.png"));
    window.Close();
    return;
}
Check(LinuxPaths.ConfigDirectory.StartsWith(root, StringComparison.Ordinal), "settings stay in test XDG config");
Check(FileHelpers.GetPathRoot("/tmp/file") == "/", "Unix root preserved");
Check(FileHelpers.GetFileNameSafe("/tmp/雪/file.png") == "file.png", "Unix filename extraction");
Check(FileHelpers.SanitizePath("/tmp/a:b?c") == "/tmp/a:b?c", "legal Linux filename characters preserved");
Check(FileHelpers.ExpandFolderVariables("~/test") == Path.Combine(LinuxPaths.Home, "test"), "tilde expansion");
Environment.SetEnvironmentVariable("SX_PATH_TEST", "CaseSensitive");
Check(FileHelpers.ExpandFolderVariables("$SX_PATH_TEST/${SX_PATH_TEST}") == "CaseSensitive/CaseSensitive", "shell environment variable expansion");
Check(!FileHelpers.GetVariableFolderPath("/tmp/Test").Contains("%%"), "empty special folders do not corrupt paths");
Check(!Helpers.IsWindows7OrGreater() && !TaskbarManager.IsPlatformSupported, "Linux kernel is not mistaken for Windows");
using (DWMManager dwm = new()) Check(!dwm.AutoDisable(), "DWM safe no-op");
Check(!DesktopIconManager.SetDesktopIconsVisibility(false), "desktop icons safe no-op");
string encrypted = DPAPI.Encrypt("password 雪\n\"'", "entropy");
File.WriteAllText(Path.Combine(root, "ciphertext"), encrypted);
Check(DPAPI.Decrypt(encrypted, "entropy") == "password 雪\n\"'", "AES-GCM round-trip");
Check(DPAPI.Encrypt("password 雪\n\"'", "entropy") != encrypted, "random encryption nonce");
byte[] tampered = Convert.FromBase64String(encrypted[4..]); tampered[^1] ^= 1;
bool rejected = false;
try { DPAPI.Decrypt("SX1:" + Convert.ToBase64String(tampered), "entropy"); } catch (CryptographicException) { rejected = true; }
Check(rejected, "tampered ciphertext rejected");
rejected = false;
try { DPAPI.Decrypt(encrypted, "wrong"); } catch (CryptographicException) { rejected = true; }
Check(rejected, "wrong entropy rejected");
string keyPath = Path.Combine(LinuxPaths.CredentialDirectory, "settings.key");
if (File.Exists(keyPath)) Check(File.GetUnixFileMode(keyPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "key permissions are 0600");
ProbeSettings settings = new() { Password = "sensitive", SupportDPAPIEncryption = true };
string settingsPath = Path.Combine(root, "probe.json");
Check(settings.Save(settingsPath), "encrypted settings save");
Check(!File.ReadAllText(settingsPath).Contains("sensitive"), "JSON does not leak plaintext");
Check(ProbeSettings.Load(settingsPath).Password == "sensitive", "encrypted JSON reload");
Check(File.GetUnixFileMode(settingsPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "settings permissions are 0600");
RegistryHelpers.CreateRegistry("platform-test", "text", "雪");
RegistryHelpers.CreateRegistry("platform-test", "number", 42);
Check(RegistryHelpers.GetValueString("platform-test", "text") == "雪" && RegistryHelpers.GetValueDWord("platform-test", "number") == 42, "file-based platform preference round-trip");
RegistryHelpers.RemoveRegistry("platform-test");
Check(RegistryHelpers.GetValueString("platform-test", "text") == null, "platform preferences removed");
StartupManager.State = StartupState.Enabled;
Check(StartupManager.State == StartupState.Enabled && File.ReadAllText(StartupManager.AutostartPath).Contains("-silent"), "XDG autostart enable");
StartupManager.State = StartupState.Disabled;
Check(StartupManager.State == StartupState.Disabled, "XDG autostart disable");
IntegrationHelpers.CreateCustomUploaderExtension(true);
IntegrationHelpers.CreateImageEffectExtension(true);
Check(IntegrationHelpers.CheckCustomUploaderExtension() && IntegrationHelpers.CheckImageEffectExtension(), "MIME integration installed");
IntegrationHelpers.CreateShellContextMenuButton(true); IntegrationHelpers.CreateEditShellContextMenuButton(true);
Check(IntegrationHelpers.CheckShellContextMenuButton() && IntegrationHelpers.CheckEditShellContextMenuButton(), "Dolphin and Nautilus integration installed");
string shortcut = Path.Combine(root, "test.desktop");
Check(ShortcutHelpers.SetShortcut(true, shortcut, Environment.ProcessPath, "--security-read"), "desktop shortcut created");
Check(ShortcutHelpers.CheckShortcut(shortcut, Environment.ProcessPath), "desktop shortcut target read");
string odd = Path.Combine(root, "folder with spaces 雪 '$; #"); Directory.CreateDirectory(odd);
string file = Path.Combine(odd, "test ' \"; $(false).txt"); File.WriteAllText(file, "hello");
Check(FileHelpers.OpenFolder(odd), "xdg-open opens folder without backslash");
Check(FileHelpers.OpenFile(file), "xdg-open passes filename as one argument");
Check(FileHelpers.OpenFolderWithFile(file), "FileManager1.ShowItems selects escaped URI");
using SKBitmap bitmap = new(80, 40); bitmap.Erase(SKColors.CornflowerBlue);
using PrintHelper print = new(bitmap) { Settings = new PrintSettings { ShowPrintDialog = false, DefaultPrinterOverride = "fake-printer" } };
string pdf = Path.Combine(root, "image-print.pdf"); print.SavePdf(pdf);
Check(File.ReadAllText(pdf).StartsWith("%PDF"), "image print produces PDF through Skia");
Check(print.Print(), "CUPS submission with fake lp");
using PrintHelper textPrint = new(string.Join('\n', Enumerable.Range(0, 150).Select(i => "Line " + i))) { Settings = new PrintSettings() };
textPrint.SavePdf(Path.Combine(root, "text-print.pdf"));
Check(new FileInfo(Path.Combine(root, "text-print.pdf")).Length > 1000, "multi-page text PDF rendered");
IntegrationHelpers.CreateShellContextMenuButton(false); IntegrationHelpers.CreateEditShellContextMenuButton(false);
Check(!IntegrationHelpers.CheckShellContextMenuButton() && !IntegrationHelpers.CheckEditShellContextMenuButton(), "shell integration removed");
Console.WriteLine("All platform assertions passed.");

public sealed class ProbeSettings : SettingsBase<ProbeSettings>
{
    [JsonEncrypt] public string Password { get; set; }
}
