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

using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>Positions only our own transient capture window, without persistent compositor rules.</summary>
public static class LinuxCaptureOverlay
{
    public static async Task PlaceAsync(Window window, Rectangle bounds)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return;
        await Task.Delay(40); // Opened precedes IsVisible becoming true on Avalonia X11.
        string title = window.Title ?? "";
        for (int attempt = 0; attempt < 10 && window.IsVisible; attempt++)
        {
            string json = await Task.Run(() => LinuxCaptureProcess.Text("hyprctl", "clients", "-j"));
            using JsonDocument clients = JsonDocument.Parse(json);
            string? address = clients.RootElement.EnumerateArray()
                .Where(c => c.GetProperty("title").GetString() == title)
                .Select(c => c.GetProperty("address").GetString()).FirstOrDefault();
            if (address != null)
            {
                string selector = "address:" + address;
                await Task.Run(() => HyprlandCaptureIpc.Dispatch("setfloating", selector, $"hl.dsp.window.float({{action=\"enable\",window={HyprlandCaptureIpc.Quote(selector)}}})"));
                int width = bounds.Width, height = bounds.Height, x = bounds.X, y = bounds.Y;
                // With force_zero_scaling, Hyprland sizes XWayland surfaces in native pixels.
                // Measure the result instead of assuming Avalonia's X11 scale equals the output scale.
                for (int correction = 0; correction < 4 && window.IsVisible; correction++)
                {
                    await Task.Run(() =>
                    {
                        string target = HyprlandCaptureIpc.Quote(selector);
                        HyprlandCaptureIpc.Dispatch("resizewindowpixel", $"exact {width} {height},{selector}", $"hl.dsp.window.resize({{x={width},y={height},relative=false,window={target}}})");
                        HyprlandCaptureIpc.Dispatch("movewindowpixel", $"exact {x} {y},{selector}", $"hl.dsp.window.move({{x={x},y={y},relative=false,window={target}}})");
                        HyprlandCaptureIpc.Dispatch("alterzorder", $"top,{selector}", $"hl.dsp.window.alter_zorder({{mode=\"top\",window={target}}})");
                    });
                    await Task.Delay(180);
                    var actual = LinuxCaptureWindows.GetWindows().FirstOrDefault(w => w.Handle.ToInt64().ToString("x") == address[2..]);
                    if (actual == null) break;
                    if (actual.Rectangle.Location == bounds.Location &&
                        actual.Rectangle.Width >= bounds.Width && actual.Rectangle.Width <= bounds.Width + 1 &&
                        actual.Rectangle.Height >= bounds.Height && actual.Rectangle.Height <= bounds.Height + 1) break;
                    if (actual.Rectangle.Width <= 0 || actual.Rectangle.Height <= 0) break;
                    width = (int)Math.Round((double)width * bounds.Width / actual.Rectangle.Width);
                    height = (int)Math.Round((double)height * bounds.Height / actual.Rectangle.Height);
                    x += bounds.X - actual.Rectangle.X;
                    y += bounds.Y - actual.Rectangle.Y;
                }
                return;
            }
            await Task.Delay(30);
        }
    }
}
