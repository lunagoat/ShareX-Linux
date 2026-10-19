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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ShareX;

internal static class NativeNotificationPresenter
{
    internal static async Task ShowAsync(NotificationWindowConfig config, Action fallback,
        Action<ToastClickAction> execute, Func<ToastClickAction, bool> canExecute)
    {
        string temporary = null;
        try
        {
            string icon = null;
            if (config.Image != null)
            {
                string directory = Path.Combine(LinuxPaths.CacheHome, "ShareX", "notifications");
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
                using var stream = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.CreateNew,
                    Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
                config.Image.Save(stream, SkiaSharp.SKEncodedImageFormat.Png);
                icon = temporary;
            }
            else if (FileHelpers.IsImageFile(config.FilePath) && File.Exists(config.FilePath)) icon = Path.GetFullPath(config.FilePath);
            Dictionary<string, string> actions = new();
            if (canExecute(config.LeftClickAction)) actions["default"] = "Open";
            foreach (NotificationActionButton definition in config.ActionButtons ?? [])
                if (canExecute(definition.Action)) actions[definition.Action.ToString()] = definition.Label ?? definition.Action.GetLocalizedDescription();
            bool shown = await LinuxNotifications.ShowAsync(config.Title, config.Text ?? Path.GetFileName(config.FilePath), icon,
                config.Duration + config.FadeDuration, actions,
                action => execute(action == "default" ? config.LeftClickAction : Enum.Parse<ToastClickAction>(action))).ConfigureAwait(false);
            if (shown) config.Dispose();
            else fallback();
        }
        catch (Exception ex) { DebugHelper.WriteException(ex); fallback(); }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
    }
}
