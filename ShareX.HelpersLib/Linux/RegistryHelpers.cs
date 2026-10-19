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

using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace ShareX.HelpersLib;

/// <summary>File-backed legacy preference seam. Desktop integration uses XDG directly.</summary>
public static class RegistryHelpers
{
    private static readonly object Sync = new();
    private static string StorePath => Path.Combine(LinuxPaths.ConfigDirectory, "PlatformOptions.json");
    private static string Key(string path, string name, RegistryHive root) => $"{root}/{path}/{name}";
    private static JObject Read() => File.Exists(StorePath) ? JObject.Parse(File.ReadAllText(StorePath)) : new JObject();
    private static void Set(string path, string name, object value, RegistryHive root)
    {
        if (LinuxPaths.Ephemeral) return;
        lock (Sync)
        {
            JObject data = Read();
            data[Key(path, name, root)] = JToken.FromObject(value);
            LinuxPaths.WritePrivateFile(StorePath, data.ToString());
        }
    }
    public static void CreateRegistry(string path, string value, RegistryHive root = RegistryHive.CurrentUser) => Set(path, null, value, root);
    public static void CreateRegistry(string path, string name, string value, RegistryHive root = RegistryHive.CurrentUser) => Set(path, name, value, root);
    public static void CreateRegistry(string path, int value, RegistryHive root = RegistryHive.CurrentUser) => Set(path, null, value, root);
    public static void CreateRegistry(string path, string name, int value, RegistryHive root = RegistryHive.CurrentUser) => Set(path, name, value, root);
    public static object GetValue(string path, string name = null, RegistryHive root = RegistryHive.CurrentUser, RegistryView view = RegistryView.Default)
    {
        lock (Sync)
        {
            JToken value = Read()[Key(path, name, root)];
            return value is JValue scalar ? scalar.Value : null;
        }
    }
    public static string GetValueString(string path, string name = null, RegistryHive root = RegistryHive.CurrentUser, RegistryView view = RegistryView.Default) => GetValue(path, name, root, view) as string;
    public static int? GetValueDWord(string path, string name = null, RegistryHive root = RegistryHive.CurrentUser, RegistryView view = RegistryView.Default) => GetValue(path, name, root, view) is object value ? Convert.ToInt32(value) : null;
    public static bool CheckStringValue(string path, string name = null, string value = null, RegistryHive root = RegistryHive.CurrentUser, RegistryView view = RegistryView.Default) => GetValueString(path, name, root, view) is string actual && (value == null || actual.Equals(value, StringComparison.Ordinal));
    public static void RemoveRegistry(string path, RegistryHive root = RegistryHive.CurrentUser)
    {
        if (LinuxPaths.Ephemeral || string.IsNullOrEmpty(path)) return;
        lock (Sync)
        {
            JObject data = Read();
            string prefix = Key(path, null, root);
            foreach (var property in System.Linq.Enumerable.ToArray(data.Properties()))
                if (property.Name.StartsWith(prefix, StringComparison.Ordinal) || property.Name.StartsWith($"{root}/{path}\\", StringComparison.Ordinal)) property.Remove();
            LinuxPaths.WritePrivateFile(StorePath, data.ToString());
        }
    }
    public static string SearchProgramPath(string fileName) => LinuxDesktop.FindCommand(Path.GetFileNameWithoutExtension(fileName));
}
