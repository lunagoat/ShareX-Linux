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

using Newtonsoft.Json.Linq;
using ShareX.HelpersLib;
using System.IO;

namespace ShareX;

public static class SystemOptions
{
    // Distribution updates are managed by the Linux package manager.
    public static bool DisableUpdateCheck => true;
    public static bool DisableUpload { get; private set; }
    public static bool DisableLogging { get; private set; }
    public static string PersonalPath { get; private set; }

    public static void UpdateSystemOptions()
    {
        JObject system = Read("/etc/xdg/ShareX/SystemOptions.json");
        JObject user = Read(Path.Combine(LinuxPaths.ConfigDirectory, "SystemOptions.json"));
        DisableUpload = (bool?)(system["DisableUpload"] ?? user["DisableUpload"]) ?? false;
        DisableLogging = (bool?)(system["DisableLogging"] ?? user["DisableLogging"]) ?? false;
        PersonalPath = (string)(system["PersonalPath"] ?? user["PersonalPath"]);
    }

    private static JObject Read(string path)
    {
        try { return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject(); }
        catch (System.Exception ex) { DebugHelper.WriteException(ex); return new JObject(); }
    }
}
