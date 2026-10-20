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

using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace ShareX.Tools;

public sealed class InspectWindowListItem : IDisposable
{
    public IntPtr Handle { get; }
    public string Title { get; }
    public string ProcessName { get; }
    public AvaloniaBitmap? Icon { get; }
    public bool HasIcon => Icon != null;
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName) ? Title : $"{Title}  —  {ProcessName}";

    public InspectWindowListItem(IntPtr handle, string title, string processName, AvaloniaBitmap? icon)
    {
        Handle = handle;
        Title = title;
        ProcessName = processName;
        Icon = icon;
    }

    public void Dispose()
    {
        Icon?.Dispose();
    }

    public override string ToString() => Title;
}

public static class InspectWindowService
{
    public static string SupportMessage => Linux.DesktopTools.WindowSupport;
    public static IntPtr GetWindowAtPoint(int x, int y, bool topLevel) =>
        Linux.DesktopTools.GetWindows().LastOrDefault(w => w.Rectangle.Contains(x, y))?.Handle ?? IntPtr.Zero;

    public static IReadOnlyList<InspectWindowListItem> GetVisibleWindows(IntPtr ignoredHandle)
    {
        try
        {
            return Linux.DesktopTools.GetWindows().Where(w => w.Handle != ignoredHandle && !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => new InspectWindowListItem(w.Handle, w.Text, w.ProcessName, null))
                .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            ToolsDiagnostics.ReportWarning(nameof(InspectWindowService), "Window enumeration is unavailable.", ex);
            return [];
        }
    }
    public static AvaloniaBitmap? GetWindowIcon(IntPtr handle) => null;
}
