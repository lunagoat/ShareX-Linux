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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ShareX.AvaloniaUI.Integration;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

internal static class LinuxPrintDialog
{
    public static bool Show(ref string printer, ref int copies)
    {
        string initialPrinter = printer;
        var result = DesktopServices.Run(async () =>
        {
            var available = await LinuxDesktop.RunAsync("lpstat", new[] { "-a" }).ConfigureAwait(true);
            string[] names = available.Success ? available.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' ', 2)[0]).ToArray() : Array.Empty<string>();
            ComboBox destinations = new() { ItemsSource = new[] { "System default" }.Concat(names).ToArray(), SelectedIndex = 0, MinWidth = 280 };
            int selected = Array.IndexOf(names, initialPrinter);
            if (selected >= 0) destinations.SelectedIndex = selected + 1;
            NumericUpDown count = new() { Minimum = 1, Maximum = 999, Value = 1 };
            Button print = new() { Content = "Print" }, cancel = new() { Content = "Cancel" };
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(print); buttons.Children.Add(cancel);
            StackPanel panel = new() { Margin = new Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "CUPS printer" }); panel.Children.Add(destinations);
            panel.Children.Add(new TextBlock { Text = "Copies" }); panel.Children.Add(count); panel.Children.Add(buttons);
            Window window = new() { Title = "ShareX — Print", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false };
            bool accepted = false;
            print.Click += (_, _) => { accepted = true; window.Close(); };
            cancel.Click += (_, _) => window.Close();
            Window owner = DesktopServices.GetWindow();
            if (owner.IsVisible) await window.ShowDialog<bool>(owner);
            else
            {
                TaskCompletionSource done = new();
                window.Closed += (_, _) => done.TrySetResult();
                window.Show(); await done.Task;
            }
            return (accepted, destinations.SelectedIndex > 0 ? (string)destinations.SelectedItem : "", (int)(count.Value ?? 1));
        });
        printer = result.Item2; copies = result.Item3;
        return result.Item1;
    }
}
