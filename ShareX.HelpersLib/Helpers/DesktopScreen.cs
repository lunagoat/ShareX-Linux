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
    /// <summary>Windows device name, such as \\.\DISPLAY2; null on other platforms.</summary>
    public string DeviceName { get; }
    /// <summary>Monitor model name; several monitors can share it.</summary>
    public string DisplayName { get; }

    private DesktopScreen(Avalonia.Platform.Screen screen)
    {
        Bounds = new Rectangle(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        WorkingArea = new Rectangle(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
        Primary = screen.IsPrimary;
        DeviceName = GetDeviceName(screen);
        DisplayName = screen.DisplayName;
    }

    private DesktopScreen(ShareX.AvaloniaUI.Linux.LinuxCaptureMonitor monitor)
    {
        Bounds = monitor.Bounds;
        WorkingArea = monitor.Bounds;
        Primary = monitor.Primary;
        // The compositor's output name (DP-1, HDMI-A-1, ...) is stable across restarts, like \\.\DISPLAY2 on Windows.
        DeviceName = monitor.Name;
        DisplayName = monitor.Name;
    }

    /// <summary>Windows: the GDI device name. Linux: the compositor output name of the monitor the screen covers most.
    /// Avalonia runs under XWayland, whose output names (XWAYLAND0, ...) do not match the compositor's, so screens are
    /// matched by geometry; both use desktop coordinates.</summary>
    public static string GetDeviceName(Avalonia.Platform.Screen screen)
    {
        if (!OperatingSystem.IsLinux())
            return NativeMethods.GetMonitorDeviceName(screen.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

        Rectangle bounds = new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        return (TryGetLinuxMonitors() ?? [])
            .Select(x => (x.Name, Overlap: Rectangle.Intersect(x.Bounds, bounds)))
            .Where(x => !x.Overlap.IsEmpty)
            .OrderByDescending(x => (long)x.Overlap.Width * x.Overlap.Height)
            .Select(x => x.Name)
            .FirstOrDefault() ?? screen.DisplayName ?? "";
    }

    /// <summary>Linux: the compositor's monitors, or Avalonia's (XWayland/X11) screens when they cannot be read,
    /// for example on a desktop without compositor IPC and without xrandr, so callers such as settings pages keep working.</summary>
    public static DesktopScreen[] AllScreens => OperatingSystem.IsLinux() && TryGetLinuxMonitors() is { Length: > 0 } monitors
        ? monitors.Select(x => new DesktopScreen(x)).ToArray()
        : DesktopServices.Run(() =>
        DesktopServices.GetWindow().Screens.All.Select(x => new DesktopScreen(x)).ToArray());

    private static ShareX.AvaloniaUI.Linux.LinuxCaptureMonitor[]? TryGetLinuxMonitors()
    {
        try
        {
            return ShareX.AvaloniaUI.Linux.LinuxScreenCapture.GetMonitors();
        }
        catch (PlatformNotSupportedException e)
        {
            DebugHelper.WriteLine("Monitor discovery failed, using the toolkit's screens: " + e.Message);
            return null;
        }
    }

    public static DesktopScreen PrimaryScreen => AllScreens.FirstOrDefault(x => x.Primary) ?? AllScreens.FirstOrDefault();

    public static DesktopScreen FromPoint(Point point) =>
        AllScreens.FirstOrDefault(x => x.Bounds.Contains(point)) ?? PrimaryScreen;

    public static DesktopScreen FromRectangle(Rectangle bounds) =>
        AllScreens.OrderByDescending(x => { var r = Rectangle.Intersect(x.Bounds, bounds); return (long)r.Width * r.Height; }).FirstOrDefault();

    public static DesktopScreen FromHandle(IntPtr handle) =>
        FromRectangle(NativeMethods.GetWindowRect(handle)) ?? PrimaryScreen;
}
