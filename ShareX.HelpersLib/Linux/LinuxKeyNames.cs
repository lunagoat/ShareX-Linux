// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Collections.Generic;

namespace ShareX.HelpersLib;

public static class LinuxKeyNames
{
    // InputKey retains upstream's virtual-key values for settings compatibility. Convert to XKB names at the boundary.
    public static string KeySym(InputKey key)
    {
        if (key >= InputKey.A && key <= InputKey.Z) return key.ToString().ToLowerInvariant();
        if (key >= InputKey.D0 && key <= InputKey.D9) return ((int)key - (int)InputKey.D0).ToString();
        if (key >= InputKey.F1 && key <= InputKey.F24) return key.ToString();
        if (key >= InputKey.NumPad0 && key <= InputKey.NumPad9) return "KP_" + ((int)key - (int)InputKey.NumPad0);
        return key switch
        {
            InputKey.PrintScreen => "Print", InputKey.Back => "BackSpace", InputKey.Return => "Return", InputKey.Escape => "Escape",
            InputKey.Tab => "Tab", InputKey.Space => "space", InputKey.Prior => "Page_Up", InputKey.Next => "Page_Down",
            InputKey.Home => "Home", InputKey.End => "End", InputKey.Left => "Left", InputKey.Right => "Right", InputKey.Up => "Up", InputKey.Down => "Down",
            InputKey.Insert => "Insert", InputKey.Delete => "Delete", InputKey.Pause => "Pause", InputKey.Scroll => "Scroll_Lock", InputKey.Capital => "Caps_Lock",
            InputKey.NumLock => "Num_Lock", InputKey.Multiply => "KP_Multiply", InputKey.Add => "KP_Add", InputKey.Subtract => "KP_Subtract", InputKey.Divide => "KP_Divide", InputKey.Decimal => "KP_Decimal",
            InputKey.ControlKey or InputKey.LControlKey => "Control_L", InputKey.RControlKey => "Control_R",
            InputKey.ShiftKey or InputKey.LShiftKey => "Shift_L", InputKey.RShiftKey => "Shift_R",
            InputKey.Menu or InputKey.LMenu => "Alt_L", InputKey.RMenu => "Alt_R", InputKey.LWin => "Super_L", InputKey.RWin => "Super_R",
            InputKey.OemSemicolon => "semicolon", InputKey.Oemplus => "equal", InputKey.Oemcomma => "comma", InputKey.OemMinus => "minus", InputKey.OemPeriod => "period",
            InputKey.OemQuestion => "slash", InputKey.Oemtilde => "grave", InputKey.OemOpenBrackets => "bracketleft", InputKey.OemPipe => "backslash",
            InputKey.OemCloseBrackets => "bracketright", InputKey.OemQuotes => "apostrophe", InputKey.OemBackslash => "less",
            InputKey.VolumeMute => "XF86AudioMute", InputKey.VolumeDown => "XF86AudioLowerVolume", InputKey.VolumeUp => "XF86AudioRaiseVolume",
            InputKey.MediaNextTrack => "XF86AudioNext", InputKey.MediaPreviousTrack => "XF86AudioPrev", InputKey.MediaStop => "XF86AudioStop", InputKey.MediaPlayPause => "XF86AudioPlay",
            _ => null
        };
    }

    public static string PortalTrigger(HotkeyInfo hotkey)
    {
        List<string> parts = new();
        if (hotkey.Control) parts.Add("CTRL");
        if (hotkey.Alt) parts.Add("ALT");
        if (hotkey.Shift) parts.Add("SHIFT");
        if (hotkey.Win) parts.Add("LOGO");
        parts.Add(KeySym(hotkey.KeyCode) ?? hotkey.KeyCode.ToString());
        return string.Join("+", parts);
    }

    public static string BindLine(HotkeyInfo hotkey, string command, bool sway = false)
    {
        if (!hotkey.IsValidHotkey || KeySym(hotkey.KeyCode) is not string key) return "# Select a supported key for " + command;
        List<string> modifiers = new();
        if (hotkey.Win) modifiers.Add(sway ? "Mod4" : "SUPER");
        if (hotkey.Control) modifiers.Add(sway ? "Ctrl" : "CTRL");
        if (hotkey.Alt) modifiers.Add(sway ? "Mod1" : "ALT");
        if (hotkey.Shift) modifiers.Add(sway ? "Shift" : "SHIFT");
        // Commands come from task enum names; do not splice arbitrary user strings into compositor config.
        foreach (char c in command) if (!char.IsAsciiLetterOrDigit(c) && c != '_') throw new ArgumentException("Expected a task command name.", nameof(command));
        return sway ? "bindsym " + string.Join("+", modifiers) + (modifiers.Count > 0 ? "+" : "") + key + " exec sharex -" + command :
            "bind = " + string.Join(" ", modifiers) + ", " + key + ", exec, sharex -" + command;
    }
}
