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
        ComboBox compositor = new() { ItemsSource = new[] { "Hyprland", "sway" }, SelectedIndex = LinuxWindowSystem.Backend == "sway" ? 1 : 0 };
        TextBox bindings = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("monospace"), VerticalAlignment = VerticalAlignment.Stretch };
        TextBlock status = new() { Text = ApplicationState.HotkeyManager?.BackendStatus ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        void Update()
        {
            bool sway = compositor.SelectedIndex == 1;
            var hotkeys = ApplicationState.HotkeysConfigOrNull?.Hotkeys;
            bindings.Text = "# Add these lines to your compositor configuration, then reload it.\n# Ensure the sharex command is on PATH.\n" +
                string.Join("\n", hotkeys?.Where(x => x.TaskSettings.Job != HotkeyType.None && x.HotkeyInfo.IsValidHotkey)
                    .Select(x => LinuxKeyNames.BindLine(x.HotkeyInfo, x.TaskSettings.Job.ToString(), sway)) ?? []);
        }
        compositor.SelectionChanged += (_, _) => Update();
        Update();
        Button copy = new() { Content = "Copy bindings", HorizontalAlignment = HorizontalAlignment.Right };
        copy.Click += (_, _) => ClipboardHelpers.CopyText(bindings.Text);
        TextBlock note = new() { Text = "The desktop portal may ask you to approve shortcuts. If it is unavailable, bind ShareX task commands in your compositor. Do not assign the same shortcut through both methods. Wayland blocks arbitrary simulated input; scrolling capture needs X11 or an approved remote desktop service. Custom workflows can also run with sharex -workflow \"workflow name\".", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Grid layout = new() { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = 12 };
        layout.Children.Add(status);
        Grid.SetRow(note, 1); layout.Children.Add(note);
        Grid.SetRow(compositor, 2); layout.Children.Add(compositor);
        Grid.SetRow(bindings, 3); layout.Children.Add(bindings);
        Grid.SetRow(copy, 4); layout.Children.Add(copy);
        Content = layout;
    }
}
