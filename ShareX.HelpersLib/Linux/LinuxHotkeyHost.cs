// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

/// <summary>Portal shortcuts on Wayland, XGrabKey on Xorg. CLI/compositor bindings remain usable
/// when the desktop portal is absent or the user declines a registration request.</summary>
public sealed class LinuxHotkeyHost : IHotkeyHost
{
    private readonly Dictionary<ushort, HotkeyInfo> hotkeys = new();
    private readonly Action<Action> dispatch;
    private LinuxX11Hotkeys x11;
    private LinuxGlobalShortcutsPortal portal;
    private CancellationTokenSource pending;
    private ushort nextId;
    private long lastActivation = long.MinValue;
    private bool closing;
    public event HotkeyEventHandler HotkeyPress;
    public event EventHandler Closed;
    // Kept for the shared host contract. Linux has no HWND broadcast messages.
    public event EventHandler<NativeWindowMessageEventArgs> NativeMessageReceived { add { } remove { } }
    public event EventHandler RegistrationChanged;
    public string StatusMessage { get; private set; } = "Global shortcuts use the desktop portal on Wayland; compositor CLI bindings are also supported.";
    public IntPtr Handle => IntPtr.Zero;
    public bool IsDisposed { get; private set; }
    public int HotkeyRepeatLimit { get; set; } = 1000;

    public LinuxHotkeyHost() : this(action => Dispatcher.UIThread.Post(action)) { }
    public LinuxHotkeyHost(Action<Action> dispatch) => this.dispatch = dispatch;
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!LinuxWindowSystem.IsWayland && x11 == null)
        {
            x11 = new LinuxX11Hotkeys(HotkeyActivated);
            StatusMessage = x11.Available ? "X11 global shortcuts are available." : "Unable to open X11 display. Use sharex -TaskName from desktop shortcuts.";
        }
    }
    public void RegisterHotkey(HotkeyInfo hotkey)
    {
        ObjectDisposedException.ThrowIf(IsDisposed || closing, this);
        if (hotkey == null || hotkey.Status == HotkeyStatus.Registered) return;
        if (!hotkey.IsValidHotkey) { hotkey.Status = HotkeyStatus.NotConfigured; return; }
        Initialize();
        if (hotkey.ID != 0) UnregisterHotkey(hotkey);
        do { nextId++; } while (nextId == 0 || hotkeys.ContainsKey(nextId));
        hotkey.ID = nextId;
        hotkeys.Add(hotkey.ID, hotkey);
        hotkey.Status = HotkeyStatus.NotConfigured;
        if (LinuxWindowSystem.IsWayland) SchedulePortal();
        else
        {
            hotkey.Status = x11.Register(hotkey) ? HotkeyStatus.Registered : HotkeyStatus.Failed;
            RegistrationChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool UnregisterHotkey(HotkeyInfo hotkey)
    {
        if (hotkey == null || hotkey.ID == 0) return false;
        bool removed = hotkeys.Remove(hotkey.ID);
        if (removed && !LinuxWindowSystem.IsWayland) x11?.Unregister(hotkey);
        hotkey.ID = 0;
        hotkey.Status = HotkeyStatus.NotConfigured;
        if (removed && LinuxWindowSystem.IsWayland && !closing) SchedulePortal();
        return removed;
    }
    private void SchedulePortal()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = new CancellationTokenSource();
        var token = pending.Token;
        _ = UpdatePortalAsync(token);
    }
    private async Task UpdatePortalAsync(CancellationToken token)
    {
        try { await Task.Delay(250, token); }
        catch (OperationCanceledException) { return; }
        // All mutation of the registration dictionary is serialized with the application's UI thread.
        dispatch(() => StartPortalRegistration(token));
    }
    private async void StartPortalRegistration(CancellationToken token)
    {
        if (token.IsCancellationRequested || closing || IsDisposed) return;
        portal?.Dispose();
        portal = null;
        HotkeyInfo[] snapshot = hotkeys.Values.ToArray();
        if (snapshot.Length == 0) return;
        LinuxGlobalShortcutsPortal current = new();
        portal = current;
        using var cancelled = token.Register(current.Dispose);
        current.Activated += id => dispatch(() => { if (ushort.TryParse(id, out ushort value) && hotkeys.TryGetValue(value, out HotkeyInfo info)) HotkeyActivated(info); });
        current.SessionClosed += () => dispatch(() =>
        {
            if (portal != current) return;
            foreach (HotkeyInfo info in snapshot) info.Status = HotkeyStatus.Failed;
            StatusMessage = "The desktop closed the shortcut session. Use compositor bindings or reopen hotkey settings to retry.";
            RegistrationChanged?.Invoke(this, EventArgs.Empty);
        });
        try
        {
            var accepted = await current.BindAsync(snapshot);
            dispatch(() =>
            {
                if (token.IsCancellationRequested || portal != current || IsDisposed) return;
                foreach (HotkeyInfo info in snapshot) info.Status = accepted.Contains(info.ID.ToString()) ? HotkeyStatus.Registered : HotkeyStatus.Failed;
                StatusMessage = "Desktop portal shortcuts are active. The desktop may assign a different trigger than the requested keys.";
                RegistrationChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception exception)
        {
            current.Dispose();
            dispatch(() =>
            {
                if (token.IsCancellationRequested || portal != current || IsDisposed) return;
                foreach (HotkeyInfo info in snapshot) info.Status = HotkeyStatus.Failed;
                StatusMessage = "Global shortcuts unavailable: " + exception.Message + " Use the Hyprland/sway bind lines in hotkey settings.";
                DebugHelper.WriteLine(StatusMessage);
                RegistrationChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }
    private void HotkeyActivated(HotkeyInfo hotkey)
    {
        dispatch(() =>
        {
            if (IsDisposed || hotkey.Status != HotkeyStatus.Registered) return;
            long now = Environment.TickCount64;
            if (lastActivation != long.MinValue && now - lastActivation < HotkeyRepeatLimit) return;
            lastActivation = now;
            HotkeyPress?.Invoke(hotkey.ID, hotkey.KeyCode, hotkey.ModifiersEnum);
        });
    }
    public void Close()
    {
        if (IsDisposed || closing) return;
        closing = true;
        pending?.Cancel();
        portal?.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
        x11?.Dispose();
        foreach (HotkeyInfo hotkey in hotkeys.Values) { hotkey.ID = 0; hotkey.Status = HotkeyStatus.NotConfigured; }
        hotkeys.Clear();
        IsDisposed = true;
    }
    public void Dispose() { Close(); pending?.Dispose(); }
}
