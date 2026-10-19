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

using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace ShareX.AvaloniaUI.Linux;

/// <summary>Capture-local window seam until WP2's WindowInfo backend is integrated.</summary>
public sealed record LinuxCaptureWindow(nint Handle, Rectangle Rectangle, string Title, string ClassName, bool Active);

public static class LinuxCaptureWindows
{
    public static nint ActiveHandle => GetWindows().FirstOrDefault(w => w.Active)?.Handle ?? 0;
    public static Rectangle GetRectangle(nint handle) => GetWindows().FirstOrDefault(w => w.Handle == handle)?.Rectangle ?? Rectangle.Empty;

    public static LinuxCaptureWindow[] GetWindows()
    {
        if (LinuxScreenCapture.IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
        {
            using JsonDocument doc = JsonDocument.Parse(LinuxCaptureProcess.Text("hyprctl", "clients", "-j"));
            return doc.RootElement.EnumerateArray()
                .Where(w => w.GetProperty("mapped").GetBoolean() && !w.GetProperty("hidden").GetBoolean() &&
                    (!w.TryGetProperty("visible", out var visible) || visible.GetBoolean()))
                .OrderBy(w => w.TryGetProperty("focusHistoryID", out var f) ? f.GetInt32() : int.MaxValue)
                .Select(w => new LinuxCaptureWindow((nint)long.Parse(w.GetProperty("address").GetString()![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    new Rectangle(w.GetProperty("at")[0].GetInt32(), w.GetProperty("at")[1].GetInt32(), w.GetProperty("size")[0].GetInt32(), w.GetProperty("size")[1].GetInt32()),
                    w.GetProperty("title").GetString() ?? "", w.GetProperty("class").GetString() ?? "", w.GetProperty("focusHistoryID").GetInt32() == 0)).ToArray();
        }
        if (LinuxScreenCapture.IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            using JsonDocument doc = JsonDocument.Parse(LinuxCaptureProcess.Text("swaymsg", "-t", "get_tree", "-r"));
            List<LinuxCaptureWindow> windows = [];
            void Visit(JsonElement node)
            {
                if (node.TryGetProperty("app_id", out var app) && app.ValueKind == JsonValueKind.String || node.TryGetProperty("window", out var xwindow) && xwindow.ValueKind == JsonValueKind.Number)
                {
                    var r = node.GetProperty("rect");
                    windows.Add(new LinuxCaptureWindow((nint)node.GetProperty("id").GetInt64(), new Rectangle(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(), r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32()),
                        node.GetProperty("name").GetString() ?? "", app.ValueKind == JsonValueKind.String ? app.GetString()! : "", node.GetProperty("focused").GetBoolean()));
                }
                foreach (string child in new[] { "nodes", "floating_nodes" })
                    if (node.TryGetProperty(child, out var children)) foreach (var item in children.EnumerateArray()) Visit(item);
            }
            Visit(doc.RootElement);
            return windows.OrderByDescending(w => w.Active).ToArray();
        }
        if (!LinuxScreenCapture.IsWayland) return X11CaptureWindows.GetWindows();
        return [];
    }

    public static void Activate(nint handle)
    {
        if (handle == 0) return;
        if (LinuxScreenCapture.IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            HyprlandCaptureIpc.Dispatch("focuswindow", "address:0x" + handle.ToInt64().ToString("x", CultureInfo.InvariantCulture),
                "hl.dsp.focus({window=\"address:0x" + handle.ToInt64().ToString("x", CultureInfo.InvariantCulture) + "\"})");
        else if (LinuxScreenCapture.IsWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
            LinuxCaptureProcess.Run("swaymsg", "[con_id=" + handle + "]", "focus");
        else if (!LinuxScreenCapture.IsWayland) LinuxCaptureProcess.Run("xdotool", "windowactivate", "--sync", handle.ToString());
    }
}
