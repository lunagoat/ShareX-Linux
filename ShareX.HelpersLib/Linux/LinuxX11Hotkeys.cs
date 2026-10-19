// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

internal sealed class LinuxX11Hotkeys : IDisposable
{
    [DllImport(LinuxX11.Library)] private static extern int XGrabKey(IntPtr d, int key, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(LinuxX11.Library)] private static extern int XUngrabKey(IntPtr d, int key, uint modifiers, IntPtr window);
    [DllImport(LinuxX11.Library)] internal static extern IntPtr XStringToKeysym(string name);
    [DllImport(LinuxX11.Library)] internal static extern byte XKeysymToKeycode(IntPtr d, IntPtr symbol);
    [DllImport(LinuxX11.Library)] private static extern int XPending(IntPtr d);
    [DllImport(LinuxX11.Library)] private static extern int XNextEvent(IntPtr d, out KeyEvent e);
    [DllImport(LinuxX11.Library)] private static extern IntPtr XGetModifierMapping(IntPtr d);
    [DllImport(LinuxX11.Library)] private static extern int XFreeModifiermap(IntPtr map);
    [StructLayout(LayoutKind.Sequential)] private struct ModifierMap { public int MaxKeys; public IntPtr Codes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display, Window, Root, Subwindow;
        public nuint Time;
        public int X, Y, RootX, RootY;
        public uint State, Keycode;
        public int SameScreen;
        private nint pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7, pad8;
    }

    private readonly BlockingCollection<Action> commands = new();
    private readonly Dictionary<HotkeyInfo, (int Code, uint Mask)> grabs = new();
    private readonly Action<HotkeyInfo> activated;
    private readonly Task loop;
    private IntPtr display, root;
    private uint numLock;
    private volatile bool stopping;
    public bool Available { get; private set; }

    public LinuxX11Hotkeys(Action<HotkeyInfo> activated)
    {
        this.activated = activated;
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loop = Task.Factory.StartNew(() => Run(ready), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        ready.Task.GetAwaiter().GetResult();
    }

    private void Run(TaskCompletionSource ready)
    {
        IDisposable errors = null;
        try
        {
            display = LinuxX11.XOpenDisplay(null);
            if (display == IntPtr.Zero) { ready.SetResult(); return; }
            errors = LinuxX11.TrackErrors(display);
            root = LinuxX11.XDefaultRootWindow(display);
            IntPtr mapPointer = XGetModifierMapping(display);
            if (mapPointer != IntPtr.Zero)
            {
                ModifierMap map = Marshal.PtrToStructure<ModifierMap>(mapPointer);
                byte code = XKeysymToKeycode(display, XStringToKeysym("Num_Lock"));
                for (int group = 0; group < 8; group++)
                    for (int key = 0; key < map.MaxKeys; key++)
                        if (code != 0 && Marshal.ReadByte(map.Codes, group * map.MaxKeys + key) == code) numLock |= 1u << group;
                XFreeModifiermap(mapPointer);
            }
            Available = true;
            ready.SetResult();
            while (!stopping)
            {
                if (commands.TryTake(out Action command, 20)) command();
                while (!stopping && XPending(display) > 0)
                {
                    XNextEvent(display, out KeyEvent e);
                    if (e.Type != 2) continue;
                    uint mask = e.State & ~(2u | numLock);
                    HotkeyInfo match = grabs.FirstOrDefault(x => x.Value.Code == e.Keycode && x.Value.Mask == mask).Key;
                    if (match != null) activated(match);
                }
            }
        }
        catch (Exception exception) { ready.TrySetResult(); DebugHelper.WriteException(exception, "X11 hotkey host unavailable."); }
        finally
        {
            Available = false;
            if (display != IntPtr.Zero) LinuxX11.XCloseDisplay(display);
            errors?.Dispose();
        }
    }

    public bool Register(HotkeyInfo hotkey) => Invoke(() =>
    {
        string name = LinuxKeyNames.KeySym(hotkey.KeyCode);
        int code = name == null ? 0 : XKeysymToKeycode(display, XStringToKeysym(name));
        if (code == 0) return false;
        uint mask = (hotkey.Shift ? 1u : 0) | (hotkey.Control ? 4u : 0) | (hotkey.Alt ? 8u : 0) | (hotkey.Win ? 64u : 0);
        if (grabs.Values.Contains((code, mask))) return false;
        LinuxX11.ClearError(display);
        foreach (uint locks in new[] { 0u, 2u, numLock, 2u | numLock }.Distinct()) XGrabKey(display, code, mask | locks, root, false, 1, 1);
        LinuxX11.XSync(display, false);
        if (LinuxX11.GetError(display) != 0)
        {
            foreach (uint locks in new[] { 0u, 2u, numLock, 2u | numLock }.Distinct()) XUngrabKey(display, code, mask | locks, root);
            LinuxX11.XFlush(display);
            return false;
        }
        grabs.Add(hotkey, (code, mask));
        return true;
    });

    public bool Unregister(HotkeyInfo hotkey) => Invoke(() =>
    {
        if (!grabs.Remove(hotkey, out var grab)) return false;
        foreach (uint locks in new[] { 0u, 2u, numLock, 2u | numLock }.Distinct()) XUngrabKey(display, grab.Code, grab.Mask | locks, root);
        LinuxX11.XFlush(display);
        return true;
    });

    private bool Invoke(Func<bool> action)
    {
        if (!Available || stopping) return false;
        TaskCompletionSource<bool> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Add(() => { try { result.TrySetResult(action()); } catch (Exception ex) { result.TrySetException(ex); } });
        return result.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    }
    public void Dispose()
    {
        stopping = true;
        loop.Wait(TimeSpan.FromSeconds(3));
        commands.Dispose();
    }
}
