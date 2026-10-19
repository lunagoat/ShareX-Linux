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

using ShareX.HelpersLib;
using System;
using System.IO;

namespace ShareX;

public static class StartupManager
{
    public static string StartupTargetPath => Environment.ProcessPath;
    public static string AutostartPath => Path.Combine(LinuxPaths.ConfigHome, "autostart", "sharex.desktop");
    public static StartupState State
    {
        get => File.Exists(AutostartPath) && !File.ReadAllText(AutostartPath).Contains("Hidden=true", StringComparison.OrdinalIgnoreCase)
            ? StartupState.Enabled : StartupState.Disabled;
        set
        {
            if (LinuxPaths.Ephemeral) return;
            if (value == StartupState.Enabled)
            {
                LinuxPaths.WritePrivateFile(AutostartPath, "[Desktop Entry]\nType=Application\nName=ShareX\nExec=" +
                    DesktopEntry.Quote(StartupTargetPath) + " -silent\nIcon=sharex\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
            }
            else if (value == StartupState.Disabled) File.Delete(AutostartPath);
            else throw new NotSupportedException();
        }
    }
}
