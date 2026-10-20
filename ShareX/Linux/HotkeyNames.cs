// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Collections.Generic;

namespace ShareX;

/// <summary>
/// Gives every hotkey a stable, human readable id derived from the action it runs. The ids are what compositor
/// binds refer to (<c>global, sharex:RectangleRegion</c> in Hyprland), so they must not change between launches or
/// when hotkeys are re-registered. The same action bound twice gets a numeric suffix in list order.
/// </summary>
internal static class HotkeyNames
{
    public static void Assign(IEnumerable<HotkeySettings> hotkeys)
    {
        Dictionary<string, int> seen = new();

        foreach (HotkeySettings hotkey in hotkeys)
        {
            if (hotkey?.HotkeyInfo == null || hotkey.TaskSettings == null || hotkey.TaskSettings.Job == HotkeyType.None)
            {
                continue;
            }

            string job = hotkey.TaskSettings.Job.ToString();
            seen.TryGetValue(job, out int count);
            seen[job] = ++count;

            hotkey.HotkeyInfo.Name = count == 1 ? job : job + "-" + count;
            hotkey.HotkeyInfo.Description = hotkey.TaskSettings.ToString();
        }
    }
}
