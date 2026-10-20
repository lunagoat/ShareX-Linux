// ShareX Linux Startup & Settings UI Test Suite. GPL v3.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShareX;
using ShareX.AvaloniaUI.Integration;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.UploadersLib;

internal static class Program
{
    private static readonly string Sandbox = Environment.GetEnvironmentVariable("SX_SANDBOX") ?? "/tmp/sx-startup-test";
    private static readonly string ScreenshotsDir = Path.Combine(Sandbox, "screenshots");
    private static readonly List<string> Failures = [];
    private static readonly List<string> WindowsWordingWarnings = [];

    public static int Main(string[] args)
    {
        Console.WriteLine($"Starting ShareX UI Test Suite in {Sandbox}");
        Directory.CreateDirectory(Sandbox);
        Directory.CreateDirectory(ScreenshotsDir);

        foreach (var (key, dir) in new[] { ("HOME", "home"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache") })
        {
            var path = Path.Combine(Sandbox, dir);
            Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable(key, path);
        }

        AppBuilder.Configure<ShareXAvaloniaApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .SetupWithoutStarting();

        ThemeManager.Refresh();
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());

        DebugHelper.Init(Path.Combine(Sandbox, "startup-ui.log"));
        ApplicationCommandLine.Initialize([]);
        SettingManager.LoadInitialSettings();
        SettingManager.WaitUploadersConfig();
        SettingManager.WaitHotkeysConfig();
        ApplicationState.Settings.AutoCheckUpdate = false;
        ApplicationState.UpdateManager = new ShareXUpdateManager();

        using CancellationTokenSource stop = new();
        var task = RunAllUiChecksAsync();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(stop.Cancel));
        Dispatcher.UIThread.MainLoop(stop.Token);

        try { task.GetAwaiter().GetResult(); }
        catch (Exception ex) { Failures.Add("Fatal UI test runner error: " + ex); }

        Console.WriteLine("\n--- UI TEST SUMMARY ---");
        Console.WriteLine($"Screenshots saved to: {ScreenshotsDir}");
        Console.WriteLine($"Failures: {Failures.Count}");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  FAIL: " + failure);
        }
        Console.WriteLine($"Windows-only wording warnings: {WindowsWordingWarnings.Count}");
        foreach (var warning in WindowsWordingWarnings)
        {
            Console.WriteLine("  WARNING: " + warning);
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    private static async Task RunAllUiChecksAsync()
    {
        await Task.Yield();

        // 1. StartScreenWindow
        Console.WriteLine("Testing StartScreenWindow...");
        var startScreen = new StartScreenWindow();
        RenderAndAuditWindow(startScreen, "StartScreen");
        startScreen.Close();

        // 2. MainWindow
        Console.WriteLine("Testing MainWindow...");
        using var tray = new FakeTray();
        var mainWindow = new MainWindow(tray);
        RenderAndAuditWindow(mainWindow, "MainWindow");
        mainWindow.CloseFromHost();

        // 3. ApplicationSettingsWindow (All 12 Pages)
        Console.WriteLine("Testing ApplicationSettingsWindow (all pages)...");
        var appSettings = new ApplicationSettingsWindow();
        appSettings.Show();
        Pump();
        var appVm = (ApplicationSettingsViewModel)appSettings.DataContext!;
        foreach (var navItem in appVm.NavigationItems)
        {
            appVm.SelectedNavigationItem = navItem;
            Pump();
            Console.WriteLine($"  ApplicationSettings -> {navItem.Id} ({navItem.Title})");
            RenderAndAuditWindow(appSettings, $"ApplicationSettings_{navItem.Id}");
        }
        appSettings.Close();

        // 4. TaskSettingsWindow (Default Task Pages)
        Console.WriteLine("Testing TaskSettingsWindow (Default Tasks)...");
        var defaultTaskSettingsWindow = new TaskSettingsWindow(ApplicationState.DefaultTaskSettings, isDefault: true);
        defaultTaskSettingsWindow.Show();
        Pump();
        var defaultTaskVm = (TaskSettingsViewModel)defaultTaskSettingsWindow.DataContext!;
        var allDefaultPages = defaultTaskVm.NavigationItems.SelectMany(p => p.Children.Count > 0 ? p.Children : [p]).ToList();
        foreach (var page in allDefaultPages)
        {
            defaultTaskVm.SelectPage(page.Id);
            Pump();
            Console.WriteLine($"  TaskSettings (Default) -> {page.Id} ({page.Title})");
            RenderAndAuditWindow(defaultTaskSettingsWindow, $"TaskSettings_Default_{page.Id}");
        }
        defaultTaskSettingsWindow.Close();

        // 5. TaskSettingsWindow (Custom Task Pages including 'task' page)
        Console.WriteLine("Testing TaskSettingsWindow (Custom Task)...");
        var customTask = new TaskSettings();
        var customTaskSettingsWindow = new TaskSettingsWindow(customTask, isDefault: false);
        customTaskSettingsWindow.Show();
        Pump();
        var customTaskVm = (TaskSettingsViewModel)customTaskSettingsWindow.DataContext!;
        var allCustomPages = customTaskVm.NavigationItems.SelectMany(p => p.Children.Count > 0 ? p.Children : [p]).ToList();
        foreach (var page in allCustomPages)
        {
            customTaskVm.SelectPage(page.Id);
            Pump();
            Console.WriteLine($"  TaskSettings (Custom) -> {page.Id} ({page.Title})");
            RenderAndAuditWindow(customTaskSettingsWindow, $"TaskSettings_Custom_{page.Id}");
        }
        customTaskSettingsWindow.Close();

        // 6. HotkeySettingsWindow
        Console.WriteLine("Testing HotkeySettingsWindow...");
        var hotkeyHost = new LinuxHotkeyHost();
        var hotkeyManager = new HotkeyManager(hotkeyHost);
        hotkeyManager.UpdateHotkeys(ApplicationState.HotkeysConfig.Hotkeys, false);
        var hotkeySettingsWindow = new HotkeySettingsWindow(new HotkeySettingsAvaloniaService(hotkeyManager, () => { }));
        RenderAndAuditWindow(hotkeySettingsWindow, "HotkeySettings");
        hotkeySettingsWindow.Close();

        // 7. DestinationSettingsWindow (All Categories & All Pages)
        Console.WriteLine("Testing DestinationSettingsWindow (all categories & pages)...");
        var destinationSettingsWindow = new DestinationSettingsWindow(ApplicationState.UploadersConfig);
        destinationSettingsWindow.Show();
        Pump();
        var destVm = (DestinationSettingsViewModel)destinationSettingsWindow.DataContext!;
        foreach (var category in destVm.NavigationItems)
        {
            foreach (var page in category.Children)
            {
                destVm.SelectedNavigationItem = page;
                Pump();
                Console.WriteLine($"  DestinationSettings -> [{category.Title}] {page.Id} ({page.Title})");
                RenderAndAuditWindow(destinationSettingsWindow, $"DestinationSettings_{category.Id}_{page.Id}");
            }
        }
        destinationSettingsWindow.Close();

        // 8. CustomUploaderSettingsWindow
        Console.WriteLine("Testing CustomUploaderSettingsWindow...");
        var customUploaderWindow = new CustomUploaderSettingsWindow(ApplicationState.UploadersConfig);
        RenderAndAuditWindow(customUploaderWindow, "CustomUploaderSettings");
        customUploaderWindow.Close();

        // 9. AboutWindow
        Console.WriteLine("Testing AboutWindow...");
        var aboutWindow = new AboutWindow();
        RenderAndAuditWindow(aboutWindow, "About");
        aboutWindow.Close();

        // 10. LinuxHotkeyBindingWindow
        Console.WriteLine("Testing LinuxHotkeyBindingWindow...");
        var linuxHotkeyWindow = new LinuxHotkeyBindingWindow();
        RenderAndAuditWindow(linuxHotkeyWindow, "LinuxHotkeyBinding");
        linuxHotkeyWindow.Close();

        // 11. NotificationWindow (Toast notification)
        Console.WriteLine("Testing NotificationWindow...");
        var notificationWindow = new NotificationWindow();
        var notifConfig = new NotificationWindowConfig
        {
            Title = "ShareX Test Notification",
            Text = "Capture saved to /tmp/screenshot.png",
            Duration = 5,
            FadeDuration = 1
        };
        notificationWindow.LoadConfig(notifConfig);
        RenderAndAuditWindow(notificationWindow, "NotificationWindow");
        notificationWindow.Close();
        notifConfig.Dispose();

        Console.WriteLine("All window and page checks finished!");
    }

    private static void RenderAndAuditWindow(Window window, string screenshotName)
    {
        window.Show();
        Pump();

        // Ensure window has layout size
        if (double.IsNaN(window.Width) || window.Width <= 0) window.Width = 1000;
        if (double.IsNaN(window.Height) || window.Height <= 0) window.Height = 720;

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        Pump();

        int width = Math.Max(100, (int)window.Bounds.Width);
        int height = Math.Max(100, (int)window.Bounds.Height);

        string screenshotPath = Path.Combine(ScreenshotsDir, $"{screenshotName}.png");
        try
        {
            using RenderTargetBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(screenshotPath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        catch (Exception ex)
        {
            Failures.Add($"Failed to render screenshot for {screenshotName}: {ex.Message}");
        }

        // Audit visual tree for Windows-only wording and empty/broken content
        AuditVisualTree(window, screenshotName);
    }

    private static void AuditVisualTree(Visual root, string context)
    {
        int elementCount = 0;
        foreach (var visual in root.GetVisualDescendants())
        {
            if (!visual.IsEffectivelyVisible)
            {
                continue;
            }

            elementCount++;
            if (visual is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                CheckTextForWindowsWording(tb.Text, context, tb.GetType().Name);
            }
            else if (visual is Button btn && btn.Content is string btnText && !string.IsNullOrWhiteSpace(btnText))
            {
                CheckTextForWindowsWording(btnText, context, "Button");
            }
            else if (visual is CheckBox cb && cb.Content is string cbText && !string.IsNullOrWhiteSpace(cbText))
            {
                CheckTextForWindowsWording(cbText, context, "CheckBox");
            }
        }

        if (elementCount < 5)
        {
            Failures.Add($"{context}: Window visual tree appears blank or empty ({elementCount} elements).");
        }
    }

    private static void CheckTextForWindowsWording(string text, string context, string controlType)
    {
        string lower = text.ToLowerInvariant();
        if (lower.Contains("windows explorer"))
        {
            WindowsWordingWarnings.Add($"{context} [{controlType}]: Contains 'Windows Explorer': \"{text}\"");
        }
        else if (lower.Contains("start with windows") || lower.Contains("when windows starts"))
        {
            WindowsWordingWarnings.Add($"{context} [{controlType}]: Contains 'Windows startup wording': \"{text}\"");
        }
        else if (lower.Contains("built for windows"))
        {
            WindowsWordingWarnings.Add($"{context} [{controlType}]: Contains 'Built for Windows': \"{text}\"");
        }
        else if (lower.Contains("windows print dialog"))
        {
            WindowsWordingWarnings.Add($"{context} [{controlType}]: Contains 'Windows print dialog': \"{text}\"");
        }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}

internal sealed class FakeTray : ITrayIconService
{
    public event Action? RightButtonDown { add { } remove { } }
    public event Action? RightButtonUp { add { } remove { } }
    public bool Visible { get; set; } = true;
    public string ToolTipText { get; set; } = "ShareX";
    public void SetIcon(byte[] icon) { }
    public void Dispose() { }
}
