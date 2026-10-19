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
using System.Diagnostics;
using System.Drawing;

namespace ShareX.HelpersLib
{
    public class WindowInfo
    {
        public IntPtr Handle { get; }

        public bool IsHandleCreated => Handle != IntPtr.Zero;

        public string Text => NativeMethods.GetWindowText(Handle);

        public string ClassName => NativeMethods.GetClassName(Handle);

        public Process Process => NativeMethods.GetProcessByWindowHandle(Handle);

        public string ProcessName
        {
            get
            {
                using (Process process = Process)
                {
                    return process?.ProcessName;
                }
            }
        }

        public string ProcessFilePath
        {
            get
            {
                using (Process process = Process)
                {
                    try { return process?.MainModule?.FileName; } catch { return null; }
                }
            }
        }

        public string ProcessFileName => FileHelpers.GetFileNameSafe(ProcessFilePath);

        public int ProcessId
        {
            get
            {
                using (Process process = Process)
                {
                    return process?.Id ?? 0;
                }
            }
        }

        public IntPtr Parent => NativeMethods.GetParent(Handle);

        public Rectangle Rectangle => CaptureHelpers.GetWindowRectangle(Handle);

        public Rectangle ClientRectangle => NativeMethods.GetClientRect(Handle);

        // Windows style bits have no compositor equivalent. Changes require compositor rules.
        public WindowStyles Style { get; set; }
        public WindowStyles ExStyle { get; set; }
        public bool Layered { get; set; }
        public bool TopMost { get; set; }
        public byte Opacity { get; set; } = 255;
        public Icon Icon => null;
        public SkiaSharp.SKBitmap GetIconBitmap() => LinuxApplicationIcon.Load(ClassName);
        public bool IsMaximized => LinuxWindowSystem.Find(Handle)?.Maximized == true;
        public bool IsMinimized => LinuxWindowSystem.Find(Handle)?.Minimized == true;
        public bool IsVisible => LinuxWindowSystem.Find(Handle)?.Visible == true;
        public bool IsCloaked => !IsVisible;
        public bool IsActive => LinuxWindowSystem.ActiveWindow == Handle;

        public WindowInfo(IntPtr handle)
        {
            Handle = handle;
        }

        public void Activate()
        {
            if (IsHandleCreated)
            {
                NativeMethods.SetForegroundWindow(Handle);
            }
        }

        public void BringToFront()
        {
            if (IsHandleCreated)
            {
                SetWindowPos(SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE);
            }
        }

        public void Restore()
        {
            if (IsHandleCreated)
            {
                Activate();
            }
        }

        public void SetWindowPos(SetWindowPosFlags flags)
        {
            SetWindowPos((IntPtr)NativeConstants.HWND_TOP, 0, 0, 0, 0, flags);
        }

        public void SetWindowPos(Rectangle rect, SetWindowPosFlags flags)
        {
            SetWindowPos((IntPtr)NativeConstants.HWND_TOP, rect.X, rect.Y, rect.Width, rect.Height, flags);
        }

        public void SetWindowPos(IntPtr insertAfter, SetWindowPosFlags flags)
        {
            SetWindowPos(insertAfter, 0, 0, 0, 0, flags);
        }

        public void SetWindowPos(IntPtr insertAfter, int x, int y, int width, int height, SetWindowPosFlags flags)
        {
            // Wayland clients cannot position other windows. Focus is supported through compositor IPC.
            if (!flags.HasFlag(SetWindowPosFlags.SWP_NOACTIVATE)) Activate();
        }

        public override string ToString()
        {
            return Text;
        }
    }
}