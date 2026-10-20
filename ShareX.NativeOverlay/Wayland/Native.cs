// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Runtime.InteropServices;

namespace ShareX.NativeOverlay.Wayland;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct PollFd { public int Fd; public short Events, Revents; }
    [DllImport("libc", SetLastError = true)] internal static extern int poll([In, Out] PollFd[] fds, nuint count, int timeout);
    [DllImport("libc", SetLastError = true)] internal static extern int eventfd(uint value, int flags);
    [DllImport("libc", SetLastError = true)] internal static extern nint read(int fd, out ulong value, nuint count);
    [DllImport("libc", SetLastError = true)] internal static extern nint write(int fd, ref ulong value, nuint count);
    [DllImport("libc", SetLastError = true)] internal static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] internal static extern int memfd_create(string name, uint flags);
    [DllImport("libc", SetLastError = true)] internal static extern int ftruncate(int fd, long size);
    [DllImport("libc", SetLastError = true)] internal static extern nint mmap(nint address, nuint size, int prot, int flags, int fd, long offset);
    [DllImport("libc", SetLastError = true)] internal static extern int munmap(nint address, nuint size);
    [DllImport("libxkbcommon.so.0")] internal static extern nint xkb_context_new(int flags);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_context_unref(nint context);
    [DllImport("libxkbcommon.so.0")] internal static extern nint xkb_keymap_new_from_string(nint context, nint text, int format, int flags);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_keymap_unref(nint keymap);
    [DllImport("libxkbcommon.so.0")] internal static extern nint xkb_state_new(nint keymap);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_state_unref(nint state);
    [DllImport("libxkbcommon.so.0")] internal static extern uint xkb_state_update_mask(nint state, uint depressed, uint latched, uint locked, uint depLayout, uint latchLayout, uint lockedLayout);
    [DllImport("libxkbcommon.so.0")] internal static extern uint xkb_state_key_get_one_sym(nint state, uint key);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_state_key_get_utf8(nint state, uint key, [Out] byte[] text, nuint size);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_state_mod_name_is_active(nint state, string name, uint component);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_keymap_key_repeats(nint keymap, uint key);
}
