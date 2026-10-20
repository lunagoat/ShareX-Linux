using NWayland;
using NWayland.Interop;
using NWayland.Protocols.Wayland;
using A = NWayland.Interop.WlMessageArgumentDescription;

// NWayland 0.12.5 does not ship virtual-keyboard bindings. These small test-only
// descriptors use its public extension API and the standard virtual-keyboard-v1 wire protocol.
internal sealed class VirtualKeyboardManager(WlProxyCreationContext context) : WlProxy(context), IWlProxyTypeDescriptorProvider
{
    public static WlProxyTypeDescriptor ProxyType { get; } = new(
        WlInterfaceDescription.Create("zwp_virtual_keyboard_manager_v1", 1)
            .AddMethod(WlMessageDescription.Create("create_virtual_keyboard").Add(A.Object(WlSeat.ProxyType)).Add(A.NewId(VirtualKeyboard.ProxyType)).Build()).Build(),
        typeof(VirtualKeyboardManager), c => new VirtualKeyboardManager(c), true);

    public VirtualKeyboard Create(WlSeat seat)
    {
        using var call = WaylandCallBuilder.Create(this, 0);
        call.Arg(seat); call.ArgNewId();
        return call.InvokeNewId<VirtualKeyboard>(null, null);
    }
}

internal sealed class VirtualKeyboard(WlProxyCreationContext context) : WlProxy(context), IWlProxyTypeDescriptorProvider
{
    public static WlProxyTypeDescriptor ProxyType { get; } = new(
        WlInterfaceDescription.Create("zwp_virtual_keyboard_v1", 1)
            .AddMethod(WlMessageDescription.Create("keymap").Add(A.UInt32).Add(A.Fd).Add(A.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("key").Add(A.UInt32).Add(A.UInt32).Add(A.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("modifiers").Add(A.UInt32).Add(A.UInt32).Add(A.UInt32).Add(A.UInt32).Build())
            .AddMethod(WlMessageDescription.Create("destroy").IsDestructor().Build()).Build(),
        typeof(VirtualKeyboard), c => new VirtualKeyboard(c), true);
    public void Keymap(int fd, uint size)
    {
        using var call = WaylandCallBuilder.Create(this, 0);
        call.Arg(1u); call.Arg(fd); call.Arg(size); call.Invoke();
    }
    public void Key(uint key, bool pressed)
    {
        using var call = WaylandCallBuilder.Create(this, 1);
        call.Arg(unchecked((uint)Environment.TickCount)); call.Arg(key); call.Arg(pressed ? 1u : 0u); call.Invoke();
    }
    public void Modifiers(uint depressed, uint locked = 0)
    {
        using var call = WaylandCallBuilder.Create(this, 2);
        call.Arg(depressed); call.Arg(0u); call.Arg(locked); call.Arg(0u); call.Invoke();
    }
    public void Destroy() { using var call = WaylandCallBuilder.Create(this, 3); call.Invoke(); }
}
