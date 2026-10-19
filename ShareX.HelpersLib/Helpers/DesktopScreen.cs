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

using ShareX.AvaloniaUI.Integration;
using System;
using System.Drawing;
using System.Linq;

namespace ShareX.HelpersLib;

/// <summary>A display snapshot in physical pixels, matching screenshot coordinates.</summary>
public sealed class DesktopScreen
{
    public Rectangle Bounds { get; }
    public Rectangle WorkingArea { get; }
    public bool Primary { get; }

    private DesktopScreen(Avalonia.Platform.Screen screen)
    {
        Bounds = new Rectangle(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        WorkingArea = new Rectangle(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
        Primary = screen.IsPrimary;
    }

    private DesktopScreen(ShareX.AvaloniaUI.Linux.LinuxCaptureMonitor monitor)
    {
        Bounds = monitor.Bounds;
        WorkingArea = monitor.Bounds;
        Primary = monitor.Primary;
    }

    public static DesktopScreen[] AllScreens => OperatingSystem.IsLinux()
        ? ShareX.AvaloniaUI.Linux.LinuxScreenCapture.GetMonitors().Select(x => new DesktopScreen(x)).ToArray()
        : DesktopServices.Run(() =>
        DesktopServices.GetWindow().Screens.All.Select(x => new DesktopScreen(x)).ToArray());

    public static DesktopScreen PrimaryScreen => AllScreens.FirstOrDefault(x => x.Primary) ?? AllScreens.FirstOrDefault();

    public static DesktopScreen FromPoint(Point point) =>
        AllScreens.FirstOrDefault(x => x.Bounds.Contains(point)) ?? PrimaryScreen;

    public static DesktopScreen FromRectangle(Rectangle bounds) =>
        AllScreens.OrderByDescending(x => { var r = Rectangle.Intersect(x.Bounds, bounds); return (long)r.Width * r.Height; }).FirstOrDefault();

    public static DesktopScreen FromHandle(IntPtr handle) =>
        FromRectangle(NativeMethods.GetWindowRect(handle)) ?? PrimaryScreen;
}
