using System.Runtime.InteropServices;
using System.Text;
using NWayland;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.Wlr.WlrVirtualPointerUnstableV1;

internal sealed class Injection : IDisposable
{
    private readonly WlDisplay display;
    private readonly ZwlrVirtualPointerV1 pointer;
    private readonly VirtualKeyboard keyboard;
    public Injection()
    {
        string socket = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
        if (!socket.StartsWith("sx-", StringComparison.Ordinal) || Path.GetFileName(socket) != socket)
            throw new InvalidOperationException("Refusing virtual input outside sx- compositor.");
        display = WlDisplay.Connect(null, new WlDisplayOptions());
        WlSeat? seat = null;
        VirtualKeyboardManager? kb = null;
        ZwlrVirtualPointerManagerV1? pm = null;
        display.GetRegistry(new WlRegistry.Listener.Relay { OnGlobal = (r, id, iface, version) =>
        {
            switch (iface)
            {
                case "wl_seat": seat ??= r.Bind<WlSeat>(id, Math.Min(version, 8), null, null); break;
                case "zwp_virtual_keyboard_manager_v1": kb = r.Bind<VirtualKeyboardManager>(id, 1, null, null); break;
                case "zwlr_virtual_pointer_manager_v1": pm = r.Bind<ZwlrVirtualPointerManagerV1>(id, Math.Min(version, 2), null, null); break;
            }
        } }, null);
        display.Roundtrip();
        if (seat == null || kb == null || pm == null) throw new Exception("Nested compositor must advertise virtual pointer and virtual keyboard.");
        pointer = pm.CreateVirtualPointer(seat, null, null);
        keyboard = kb.Create(seat);
        var map = Encoding.UTF8.GetBytes(TestTools.Run("xkbcli", "compile-keymap", "--layout", "us") + "\0");
        int fd = memfd_create("sharex-test-keymap", 1);
        if (fd < 0) throw new Exception("memfd_create failed");
        try
        {
            if (write(fd, map, (nuint)map.Length) != map.Length) throw new Exception("keymap write failed");
            keyboard.Keymap(fd, (uint)map.Length);
            display.Roundtrip();
        }
        finally { close(fd); }
    }
    public void Move(int x, int y, int width, int height)
    {
        pointer.MotionAbsolute(unchecked((uint)Environment.TickCount), (uint)x, (uint)y, (uint)width, (uint)height);
        pointer.Frame(); display.Roundtrip();
    }
    public void Button(bool pressed)
    {
        pointer.Button(unchecked((uint)Environment.TickCount), 0x110, pressed ? WlPointer.ButtonStateEnum.Pressed : WlPointer.ButtonStateEnum.Released);
        pointer.Frame(); display.Roundtrip();
    }
    public void Scroll(int notches)
    {
        pointer.AxisSource(WlPointer.AxisSourceEnum.Wheel);
        pointer.AxisDiscrete(unchecked((uint)Environment.TickCount), WlPointer.AxisEnum.VerticalScroll, new WlFixed(notches * 10), notches);
        pointer.Frame(); display.Roundtrip();
    }
    public void Key(uint code, bool down) { keyboard.Key(code, down); display.Roundtrip(); }
    public void Modifiers(uint depressed, uint locked = 0) { keyboard.Modifiers(depressed, locked); display.Roundtrip(); }
    public void Dispose()
    {
        keyboard.Modifiers(0); keyboard.Destroy(); pointer.Destroy(); display.Flush(); display.Dispose();
    }
    [DllImport("libc")] private static extern int memfd_create(string name, uint flags);
    [DllImport("libc")] private static extern nint write(int fd, byte[] bytes, nuint count);
    [DllImport("libc")] private static extern int close(int fd);
}
