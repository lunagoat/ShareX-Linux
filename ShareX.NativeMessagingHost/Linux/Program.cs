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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ShareX.HelpersLib;
using System.Diagnostics;
using System.Text;

namespace ShareX.NativeMessagingHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        string payload = null;
        try
        {
            HelpersLib.NativeMessagingHost host = new();
            string input = host.Read();
            if (string.IsNullOrEmpty(input)) return 0;
            string application = Path.Combine(AppContext.BaseDirectory, "ShareX");
            payload = Path.Combine(Path.GetTempPath(), $"sharex-browser-{Guid.NewGuid():N}.json");
            using (FileStream stream = new(payload, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
            using (StreamWriter writer = new(stream, new UTF8Encoding(false))) writer.Write(input);
            ProcessStartInfo start = new(application) { UseShellExecute = false };
            start.ArgumentList.Add("-NativeMessagingInput");
            start.ArgumentList.Add(payload);
            using Process process = Process.Start(start);
            host.Write(input);
            return 0;
        }
        catch (Exception exception)
        {
            if (payload != null) File.Delete(payload);
            Console.Error.WriteLine("ShareX browser integration: " + exception.Message);
            return 1;
        }
    }
}
