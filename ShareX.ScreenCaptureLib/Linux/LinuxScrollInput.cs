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

using ShareX.AvaloniaUI.Linux;
using System;
using System.Globalization;

namespace ShareX.ScreenCaptureLib;

internal static class LinuxScrollInput
{
    public static void EnsureSupported()
    {
        if (LinuxScreenCapture.IsWayland && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            throw new PlatformNotSupportedException("Automatic scrolling requires Hyprland compositor IPC or an X11 session. On other Wayland desktops use browser full-page capture, or enable a RemoteDesktop input portal backend.");
    }

    public static void Key(nint handle, string key, bool control = false)
    {
        EnsureSupported();
        if (LinuxScreenCapture.IsWayland)
        {
            string selector = "address:0x" + handle.ToInt64().ToString("x", CultureInfo.InvariantCulture);
            HyprlandCaptureIpc.Dispatch("sendshortcut", (control ? "CTRL" : "") + "," + key + "," + selector,
                $"hl.dsp.send_shortcut({{mods={HyprlandCaptureIpc.Quote(control ? "CTRL" : "")},key={HyprlandCaptureIpc.Quote(key)},window={HyprlandCaptureIpc.Quote(selector)}}})");
        }
        else LinuxCaptureProcess.Run("xdotool", "key", "--window", handle.ToString(), (control ? "ctrl+" : "") + key);
    }

    public static void Scroll(nint handle, ScrollMethod method, int amount)
    {
        amount = Math.Clamp(amount, 1, 100);
        if (method == ScrollMethod.PageDown) { Key(handle, "Next"); return; }
        if (method == ScrollMethod.MouseWheel && !LinuxScreenCapture.IsWayland)
        {
            LinuxCaptureProcess.Run("xdotool", "click", "--window", handle.ToString(), "--repeat", amount.ToString(CultureInfo.InvariantCulture), "5");
            return;
        }
        // Wayland cannot synthesize a wheel through keyboard IPC. Equivalent targeted
        // Down keys avoid global input injection and preserve the requested scroll amount.
        for (int i = 0; i < amount * (method == ScrollMethod.MouseWheel ? 3 : 1); i++) Key(handle, "Down");
    }
}
