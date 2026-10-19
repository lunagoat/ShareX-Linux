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

#nullable enable

using ShareX.HelpersLib;
using System;
using System.IO;

namespace ShareX;

internal static class AppPaths
{
    internal const string HistoryFileName = "History.db";
    internal const string HistoryFileNameOld = "History.json";
    internal const string LogsFolderName = "Logs";

    internal static readonly string DefaultPersonalFolder =
        LinuxPaths.ConfigDirectory;
    // On Linux the executable itself is named "ShareX", so the portable data folder cannot use that name.
    internal static readonly string PortablePersonalFolder = FileHelpers.GetAbsolutePath(ApplicationInfo.Name + "-Data");
    internal static readonly string PortableCheckFilePath = FileHelpers.GetAbsolutePath("Portable");
    internal static readonly string SteamInAppFilePath = FileHelpers.GetAbsolutePath("Steam");

    internal static string PersonalFolder => string.IsNullOrEmpty(PersonalPathManager.CustomPersonalPath)
        ? DefaultPersonalFolder
        : FileHelpers.ExpandFolderVariables(PersonalPathManager.CustomPersonalPath);

    internal static string DataFolder => string.IsNullOrEmpty(PersonalPathManager.CustomPersonalPath)
        ? LinuxPaths.DataDirectory : PersonalFolder;

    internal static string? HistoryFilePath => StartupOptions.Sandbox ? null : Path.Combine(DataFolder, HistoryFileName);
    internal static string? HistoryFilePathOld => StartupOptions.Sandbox ? null : Path.Combine(DataFolder, HistoryFileNameOld);
    internal static string LogsFolder => Path.Combine(DataFolder, LogsFolderName);

    internal static string? LogsFilePath
    {
        get
        {
            if (StartupOptions.Sandbox || SystemOptions.DisableLogging)
            {
                return null;
            }

            string fileName = $"ShareX-Log-{DateTime.Now:yyyy-MM}.txt";
            return Path.Combine(LogsFolder, fileName);
        }
    }

    internal static string ScreenshotsParentFolder
    {
        get
        {
            if (ApplicationState.SettingsOrNull is { UseCustomScreenshotsPath: true } settings)
            {
                string primaryPath = settings.CustomScreenshotsPath;
                string fallbackPath = settings.CustomScreenshotsPath2;

                if (!string.IsNullOrEmpty(primaryPath))
                {
                    primaryPath = FileHelpers.ExpandFolderVariables(primaryPath);
                    if (string.IsNullOrEmpty(fallbackPath) || Directory.Exists(primaryPath))
                    {
                        return primaryPath;
                    }
                }

                if (!string.IsNullOrEmpty(fallbackPath))
                {
                    fallbackPath = FileHelpers.ExpandFolderVariables(fallbackPath);
                    if (Directory.Exists(fallbackPath))
                    {
                        return fallbackPath;
                    }
                }
            }

            return Path.Combine(DataFolder, "Screenshots");
        }
    }

    internal static string ImageEffectsFolder => Path.Combine(DataFolder, "ImageEffects");
    internal static string ModelsFolder => Path.Combine(DataFolder, "Models");
}
