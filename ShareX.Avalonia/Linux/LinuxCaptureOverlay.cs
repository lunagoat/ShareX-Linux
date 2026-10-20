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
    /// <summary>
    /// niri keeps every window inside its scrolling layout on one monitor, so the overlay cannot be positioned: ask niri to make it fullscreen
    /// on the focused output, where it covers the bar and lines up pixel for pixel with the frozen screenshot of that output.
    /// </summary>
    private static async Task PlaceNiriAsync(Window window)
    {
        string title = window.Title ?? "";
        for (int attempt = 0; attempt < 30 && window.IsVisible; attempt++)
        {
            long? id = await Task.Run(() =>
            {
                try
                {
                    using JsonDocument windows = JsonDocument.Parse(LinuxCaptureProcess.Text("niri", "msg", "--json", "windows"));
                    foreach (JsonElement w in windows.RootElement.EnumerateArray())
                    {
                        if (w.TryGetProperty("title", out var t) && t.GetString() == title) return (long?)w.GetProperty("id").GetInt64();
                    }
                }
                catch (Exception) { }
                return null;
            });

            if (id != null)
            {
                await Task.Run(() => LinuxCaptureProcess.Run("niri", "msg", "action", "fullscreen-window", "--id", id.Value.ToString()));
                await Task.Delay(200); // let the compositor resize the surface before the overlay measures itself
                return;
            }

            await Task.Delay(40);
        }
    }

    private static long? FindSwayContainer(string title)
    {
        try
        {
            using JsonDocument tree = JsonDocument.Parse(LinuxCaptureProcess.Text("swaymsg", "-t", "get_tree", "-r"));
            long? found = null;
            void Visit(JsonElement node)
            {
                if (found != null) return;
                if (node.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == title && node.TryGetProperty("pid", out _))
                {
                    found = node.GetProperty("id").GetInt64();
                    return;
                }
                foreach (string child in new[] { "nodes", "floating_nodes" })
                    if (node.TryGetProperty(child, out var children)) foreach (JsonElement item in children.EnumerateArray()) Visit(item);
            }
            Visit(tree.RootElement);
            return found;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// sway floats a window with a fixed size by itself but centres it on one output; a floating window may span outputs, so place it
    /// at the exact desktop rectangle, then measure and correct it like the Hyprland path does.
    /// </summary>
    private static async Task PlaceSwayAsync(Window window, Rectangle bounds)
    {
        string title = window.Title ?? "";
        for (int attempt = 0; attempt < 30 && window.IsVisible; attempt++)
        {
            long? id = await Task.Run(() => FindSwayContainer(title));
            if (id == null)
            {
                await Task.Delay(40);
                continue;
            }

            int width = bounds.Width, height = bounds.Height, x = bounds.X, y = bounds.Y;
            for (int correction = 0; correction < 4 && window.IsVisible; correction++)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        LinuxCaptureProcess.Run("swaymsg", $"[con_id={id}] floating enable, border none, resize set width {width} px height {height} px, move absolute position {x} px {y} px");
                    }
                    catch (Exception) { }
                });
                await Task.Delay(150);

                Rectangle? actual = await Task.Run(() =>
                {
                    try
                    {
                        using JsonDocument tree = JsonDocument.Parse(LinuxCaptureProcess.Text("swaymsg", "-t", "get_tree", "-r"));
                        Rectangle? rect = null;
                        void Visit(JsonElement node)
                        {
                            if (rect != null) return;
                            if (node.TryGetProperty("id", out var nid) && nid.GetInt64() == id)
                            {
                                var r = node.GetProperty("rect");
                                rect = new Rectangle(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(), r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32());
                                return;
                            }
                            foreach (string child in new[] { "nodes", "floating_nodes" })
                                if (node.TryGetProperty(child, out var children)) foreach (JsonElement item in children.EnumerateArray()) Visit(item);
                        }
                        Visit(tree.RootElement);
                        return rect;
                    }
                    catch (Exception) { return null; }
                });

                if (actual == null || actual.Value.Width <= 0 || actual.Value.Height <= 0) break;
                if (actual.Value.Location == bounds.Location && Math.Abs(actual.Value.Width - bounds.Width) <= 1 && Math.Abs(actual.Value.Height - bounds.Height) <= 1) break;

                width = (int)Math.Round((double)width * bounds.Width / actual.Value.Width);
                height = (int)Math.Round((double)height * bounds.Height / actual.Value.Height);
                x += bounds.X - actual.Value.X;
                y += bounds.Y - actual.Value.Y;
            }

            return;
        }
    }

    public static async Task PlaceAsync(Window window, Rectangle bounds)
    {
        if (NiriIpc.IsAvailable)
        {
            await PlaceNiriAsync(window);
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            await PlaceSwayAsync(window, bounds);
            return;
        }

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
