// ShareX - GPL-3.0-or-later. Temporary app-lifetime seam until WP2's native hotkey host lands.
using ShareX.HelpersLib;
using System;

namespace ShareX.Linux;

internal sealed class ApplicationHost : IHotkeyHost
{
    public event HotkeyEventHandler HotkeyPress { add { } remove { } }
    public event EventHandler Closed;
    public event EventHandler<NativeWindowMessageEventArgs> NativeMessageReceived { add { } remove { } }
    public IntPtr Handle => IntPtr.Zero;
    public bool IsDisposed { get; private set; }
    public int HotkeyRepeatLimit { get; set; } = 1000;
    public void Initialize() => DebugHelper.WriteLine("Linux app host: native hotkeys await the WP2 backend; workflows remain available from menus and CLI.");
    public void RegisterHotkey(HotkeyInfo hotkeyInfo) => hotkeyInfo.Status = hotkeyInfo.IsValidHotkey ? HotkeyStatus.Failed : HotkeyStatus.NotConfigured;
    public bool UnregisterHotkey(HotkeyInfo hotkeyInfo) { hotkeyInfo.Status = HotkeyStatus.NotConfigured; return true; }
    public void Close() { if (!IsDisposed) { IsDisposed = true; Closed?.Invoke(this, EventArgs.Empty); } }
    public void Dispose() => Close();
}
