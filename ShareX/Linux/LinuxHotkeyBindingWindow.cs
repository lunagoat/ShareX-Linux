// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using System;
using System.Linq;

namespace ShareX;

internal sealed class LinuxHotkeyBindingWindow : Window
{
    public LinuxHotkeyBindingWindow()
    {
        Title = "Linux global shortcuts";
        Width = 780;
        Height = 500;
        MinWidth = 500;
        MinHeight = 300;
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        ComboBox compositor = new() { ItemsSource = new[] { "Hyprland (hyprland.lua)", "Hyprland (hyprland.conf)", "sway" }, SelectedIndex = LinuxWindowSystem.Backend == "sway" ? 2 : 0 };
        ComboBox method = new() { ItemsSource = new[] { "Run sharex commands (works everywhere)", "Trigger portal shortcuts (Hyprland: global dispatcher)" }, SelectedIndex = 0 };
        TextBox bindings = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("monospace"), VerticalAlignment = VerticalAlignment.Stretch };
        TextBlock status = new() { Text = ApplicationState.HotkeyManager?.BackendStatus ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        void Update()
        {
            BindDialect dialect = compositor.SelectedIndex switch { 1 => BindDialect.HyprlandConf, 2 => BindDialect.Sway, _ => BindDialect.HyprlandLua };
            method.IsEnabled = dialect != BindDialect.Sway;
            bindings.Text = HotkeyBindText.Build(ApplicationState.HotkeysConfigOrNull?.Hotkeys ?? [], dialect, method.SelectedIndex == 1 && method.IsEnabled);
        }
        compositor.SelectionChanged += (_, _) => Update();
        method.SelectionChanged += (_, _) => Update();
        Update();
        Button copy = new() { Content = "Copy bindings" };
        copy.Click += (_, _) => ClipboardHelpers.CopyText(bindings.Text);
        Button install = new() { Content = "Add to my config" };
        install.Click += (_, _) =>
        {
            BindDialect dialect = compositor.SelectedIndex switch { 1 => BindDialect.HyprlandConf, 2 => BindDialect.Sway, _ => BindDialect.HyprlandLua };
            string launcher = System.IO.File.Exists(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "sharex")) ? "~/.local/bin/sharex" : "sharex";
            status.Text = CompositorBinds.Install(dialect, ApplicationState.HotkeysConfigOrNull?.Hotkeys ?? [], method.SelectedIndex == 1 && method.IsEnabled, launcher);
        };
        Button remove = new() { Content = "Remove from my config" };
        remove.Click += (_, _) =>
        {
            BindDialect dialect = compositor.SelectedIndex switch { 1 => BindDialect.HyprlandConf, 2 => BindDialect.Sway, _ => BindDialect.HyprlandLua };
            status.Text = CompositorBinds.Remove(dialect);
        };
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { remove, copy, install } };
        TextBlock note = new() { Text = "Hyprland never binds keys by itself: ShareX registers its shortcuts with the desktop portal, but you still have to add a bind (below) for each key. \"Add to my config\" writes them into your compositor configuration for you (backup first, keys you already use are skipped). Terminal equivalents: sharex --print-binds, sharex --install-binds, sharex --remove-binds. Do not assign the same shortcut through both methods. Wayland blocks arbitrary simulated input; scrolling capture needs X11 or an approved remote desktop service. Custom workflows can also run with sharex -workflow \"workflow name\".", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Grid layout = new() { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"), RowSpacing = 12 };
        layout.Children.Add(status);
        Grid.SetRow(note, 1); layout.Children.Add(note);
        Grid.SetRow(compositor, 2); layout.Children.Add(compositor);
        Grid.SetRow(method, 3); layout.Children.Add(method);
        Grid.SetRow(bindings, 4); layout.Children.Add(bindings);
        Grid.SetRow(buttons, 5); layout.Children.Add(buttons);
        Content = layout;
    }
}
