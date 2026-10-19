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

using System;
using System.IO;
using System.Linq;

namespace ShareX.HelpersLib;

public static class ShortcutHelpers
{
    public static bool SetShortcut(bool create, Environment.SpecialFolder folder, string name, string target, string arguments = "") =>
        SetShortcut(create, ShortcutPath(folder, name), target, arguments);
    public static bool SetShortcut(bool create, string path, string target, string arguments = "")
    {
        if (LinuxPaths.Ephemeral) return false;
        try
        {
            if (!create) { File.Delete(path); return true; }
            if (!File.Exists(target)) return false;
            string name = Path.GetFileNameWithoutExtension(path).Replace('\n', ' ').Replace('\r', ' ');
            LinuxPaths.WritePrivateFile(path, $"[Desktop Entry]\nType=Application\nName={name}\nExec={DesktopEntry.Quote(Path.GetFullPath(target))} {arguments}\nIcon=sharex\nTerminal=false\nX-ShareX-Target={Path.GetFullPath(target).Replace("\\", "\\\\")}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return true;
        }
        catch (Exception ex) { DebugHelper.WriteException(ex); return false; }
    }
    public static bool CheckShortcut(Environment.SpecialFolder folder, string name, string target) => CheckShortcut(ShortcutPath(folder, name), target);
    public static bool CheckShortcut(string path, string target) => !string.IsNullOrEmpty(target) && File.Exists(path) &&
        File.ReadLines(path).Contains("X-ShareX-Target=" + Path.GetFullPath(target).Replace("\\", "\\\\"), StringComparer.Ordinal);
    private static string ShortcutPath(Environment.SpecialFolder folder, string name)
    {
        string directory = folder == Environment.SpecialFolder.Startup ? Path.Combine(LinuxPaths.ConfigHome, "autostart") :
            folder == Environment.SpecialFolder.Desktop || folder == Environment.SpecialFolder.DesktopDirectory
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : Path.Combine(LinuxPaths.DataHome, "applications");
        if (string.IsNullOrEmpty(directory)) directory = Path.Combine(LinuxPaths.Home, "Desktop");
        return Path.Combine(directory, Path.ChangeExtension(name, ".desktop"));
    }
}
