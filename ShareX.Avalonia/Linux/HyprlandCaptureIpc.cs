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

using System.Text.Json;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>Hyprland 0.56 uses Lua dispatchers; retain the legacy protocol for older installations.</summary>
public static class HyprlandCaptureIpc
{
    private static bool? _lua;
    private static bool UsesLua
    {
        get
        {
            if (_lua.HasValue) return _lua.Value;
            try { LinuxCaptureProcess.Run("hyprctl", "eval", "assert(hl.dsp.window.float)"); _lua = true; }
            catch { _lua = false; }
            return _lua.Value;
        }
    }
    public static string Quote(string text) => JsonSerializer.Serialize(text);

    public static void Dispatch(string legacy, string arguments, string lua)
    {
        string output = UsesLua ? LinuxCaptureProcess.Text("hyprctl", "dispatch", lua) : LinuxCaptureProcess.Text("hyprctl", "dispatch", legacy, arguments);
        if (output.Contains("error", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Hyprland: " + output.Trim());
    }
}
