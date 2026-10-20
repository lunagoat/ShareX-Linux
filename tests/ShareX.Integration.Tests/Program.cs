// ShareX Linux integration checks. GPL-3.0-or-later.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ShareX;
using ShareX.AvaloniaUI.Integration;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.UploadersLib;

internal static class Program
{
    private static int Main()
    {
        if (Environment.GetEnvironmentVariable("SX_SANDBOX") is not { Length: > 0 } sandbox || !sandbox.StartsWith("/tmp/sx-wp6"))
            throw new InvalidOperationException("Run with SX_SANDBOX=/tmp/sx-wp6-tests.");
        foreach (var (key, dir) in new[] { ("HOME", "home"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache") })
        {
            var path = Path.Combine(sandbox, dir);
            Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable(key, path);
        }
        AppBuilder.Configure<ShareXAvaloniaApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var context = new AvaloniaSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            DebugHelper.Init(Path.Combine(sandbox, "checks.log"));
            ApplicationCommandLine.Initialize([]);
            SettingManager.LoadInitialSettings();
            SettingManager.WaitUploadersConfig();
            SettingManager.WaitHotkeysConfig();
            ApplicationState.Settings.AutoCheckUpdate = false;
            ApplicationState.UpdateManager = new ShareXUpdateManager();
            HelpersOptions.ShareXSpecialFolders["ShareXImageEffects"] = AppPaths.ImageEffectsFolder;
            CheckWindows();
            Console.WriteLine("PASS settings windows and pages");
            Wait(NetworkChecks.RunAsync(sandbox));
            Wait(WorkflowChecks.RunAsync(sandbox));
            Wait(StorageChecks.RunAsync(sandbox));
            Wait(BrowserChecks.RunAsync(sandbox));
            Wait(MediaChecks.RunAsync(sandbox));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { ApplicationState.HistoryManager?.Dispose(); }
    }

    private static void CheckWindows()
    {
        var start = new StartScreenWindow(); Show(start); start.Close();
        var app = new ApplicationSettingsWindow(); Show(app);
        var vm = (ApplicationSettingsViewModel)app.DataContext!;
        foreach (var page in vm.NavigationItems) { vm.SelectedNavigationItem = page; Pump(); Console.WriteLine("  app/" + page.Id); }
        // Check concrete setting types through their production view model and JSON persistence.
        vm.ShowTray = false;
        var settingsPath = Path.Combine(Environment.GetEnvironmentVariable("SX_SANDBOX")!, "roundtrip.json");
        Console.WriteLine("  save settings");
        if (!ApplicationState.Settings.Save(settingsPath)) throw new Exception("Settings save failed");
        Console.WriteLine("  load settings");
        var restored = ApplicationConfig.Load(settingsPath);
        Console.WriteLine("  loaded settings");
        if (restored.ShowTray) throw new Exception("Boolean setting failed to round-trip");
        Console.WriteLine("  close settings");
        app.Close();
        Console.WriteLine("  closed settings");
        foreach (bool defaults in new[] { true, false })
        {
            var task = new TaskSettingsWindow(ApplicationState.DefaultTaskSettings, defaults); Show(task);
            var taskVm = (TaskSettingsViewModel)task.DataContext!;
            foreach (var page in taskVm.NavigationItems.SelectMany(p => p.Children.Prepend(p)))
            { taskVm.SelectPage(page.Id); Pump(); Console.WriteLine("  task/" + page.Id); }
            task.Close();
        }
        var destinations = new DestinationSettingsWindow(ApplicationState.UploadersConfig); Show(destinations); destinations.Close();
        var history = new HistoryWindow(); Show(history); history.Close();
        var images = new ImageHistoryWindow(); Show(images); images.Close();
        using var tray = new TestTray();
        var main = new MainWindow(tray); Show(main); main.CloseFromHost();
    }
    internal static void Wait(System.Threading.Tasks.Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Integration check timed out");
        task.GetAwaiter().GetResult();
    }
    private static void Show(Window window) { window.Show(); Pump(); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); }
}

internal sealed class TestTray : ITrayIconService
{
    public event Action? RightButtonDown { add { } remove { } }
    public event Action? RightButtonUp { add { } remove { } }
    public bool Visible { get; set; }
    public string ToolTipText { get; set; } = "ShareX";
    public void SetIcon(byte[] icon) { }
    public void Dispose() { }
}
