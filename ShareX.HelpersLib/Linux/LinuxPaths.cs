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

namespace ShareX.HelpersLib;

/// <summary>XDG locations shared by settings, desktop integration and credentials.</summary>
public static class LinuxPaths
{
    public static string Home => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string ConfigHome => Xdg("XDG_CONFIG_HOME", ".config");
    public static string DataHome => Xdg("XDG_DATA_HOME", ".local/share");
    public static string CacheHome => Xdg("XDG_CACHE_HOME", ".cache");
    public static string ConfigDirectory => Path.Combine(ConfigHome, "ShareX");
    public static string DataDirectory => Path.Combine(DataHome, "ShareX");
    public static string CredentialDirectory { get; set; }
    public static bool Ephemeral { get; set; }

    private static string Xdg(string variable, string fallback)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrEmpty(value) && Path.IsPathFullyQualified(value) ? value : Path.Combine(Home, fallback);
    }

    // Called before anything caches Environment.SpecialFolder or reads settings.
    public static void InitializeSandbox()
    {
        string sandbox = Environment.GetEnvironmentVariable("SX_SANDBOX");
        if (string.IsNullOrWhiteSpace(sandbox)) return;
        sandbox = Path.GetFullPath(sandbox);
        foreach (var entry in new[] { ("HOME", "home"), ("XDG_CONFIG_HOME", "config"),
            ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state") })
        {
            string directory = Path.Combine(sandbox, entry.Item2);
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable(entry.Item1, directory);
        }
    }

    public static void WritePrivateFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, new FileStreamOptions { Mode = FileMode.CreateNew,
                Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
            using (StreamWriter writer = new(stream)) writer.Write(content);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
