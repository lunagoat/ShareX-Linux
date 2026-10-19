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
        Button copy = new() { Content = "Copy bindings", HorizontalAlignment = HorizontalAlignment.Right };
        copy.Click += (_, _) => ClipboardHelpers.CopyText(bindings.Text);
        TextBlock note = new() { Text = "Hyprland never binds keys by itself: ShareX registers its shortcuts with the desktop portal, but you still have to add a bind (below) for each key. The same lines are available from a terminal with: sharex --print-binds. Do not assign the same shortcut through both methods. Wayland blocks arbitrary simulated input; scrolling capture needs X11 or an approved remote desktop service. Custom workflows can also run with sharex -workflow \"workflow name\".", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Grid layout = new() { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"), RowSpacing = 12 };
        layout.Children.Add(status);
        Grid.SetRow(note, 1); layout.Children.Add(note);
        Grid.SetRow(compositor, 2); layout.Children.Add(compositor);
        Grid.SetRow(method, 3); layout.Children.Add(method);
        Grid.SetRow(bindings, 4); layout.Children.Add(bindings);
        Grid.SetRow(copy, 5); layout.Children.Add(copy);
        Content = layout;
    }
}
