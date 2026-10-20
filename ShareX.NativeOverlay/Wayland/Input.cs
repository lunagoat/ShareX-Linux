// ShareX - GPL v3 or later. Copyright (c) 2007-2026 ShareX Team.
using System.Text;
using NWayland;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.CursorShapeV1;

namespace ShareX.NativeOverlay.Wayland;

public sealed partial class WaylandOverlaySession
{
    private WlPointer? pointer;
    private WlKeyboard? keyboard;
    private WpCursorShapeDeviceV1? cursorDevice;
    private Surface? focus;
    private double pointerX, pointerY;
    private uint enterSerial;
    private OverlayCursor cursor;
    private nint xkbContext, keymap, xkbState;
    private OverlayModifiers modifiers;
    private int repeatRate, repeatDelay;
    private uint? repeatKey;
    private long repeatAt;
    private readonly double[] axis = new double[2];
    private readonly double?[] discrete = new double?[2], value120 = new double?[2];

    private void SeatCapabilities(WlSeat seat, WlSeat.CapabilityEnum capabilities)
    {
        if ((capabilities & WlSeat.CapabilityEnum.Pointer) != 0 && pointer == null)
        {
            pointer = seat.GetPointer(new WlPointer.Listener.Relay
            {
                OnEnter = (_, serial, surface, x, y) =>
                {
                    focus = surfaces.FirstOrDefault(s => s.Proxy == surface);
                    enterSerial = serial;
                    Move(x, y); ApplyCursor();
                },
                OnLeave = (_, _, _) => { focus = null; enterSerial = 0; events.Writer.TryWrite(new PointerLeft()); },
                OnMotion = (_, _, x, y) => Move(x, y),
                OnButton = (_, _, _, button, state) =>
                {
                    int mapped = button switch { 0x110 => 1, 0x112 => 2, 0x111 => 3, 0x113 or 0x116 => 4, 0x114 or 0x115 => 5, _ => 0 };
                    if (mapped != 0) events.Writer.TryWrite(new PointerButton(mapped, state == WlPointer.ButtonStateEnum.Pressed, pointerX, pointerY, modifiers));
                },
                OnAxis = (_, _, direction, value) => { axis[(int)direction] += (double)value / 10; if (!pointer!.IsFrameEventAvailable) Wheel(); },
                OnAxisDiscrete = (_, direction, steps) => discrete[(int)direction] = steps,
                OnAxisValue120 = (_, direction, steps) => value120[(int)direction] = (value120[(int)direction] ?? 0) + steps / 120.0,
                OnFrame = _ => Wheel()
            }, null);
            cursorDevice = cursorManager?.GetPointer(pointer, null, null);
        }
        else if ((capabilities & WlSeat.CapabilityEnum.Pointer) == 0 && pointer != null)
        {
            cursorDevice?.Destroy(); cursorDevice = null;
            if (pointer.IsReleaseAvailable) pointer.Release();
            pointer = null; focus = null; enterSerial = 0;
        }
        if ((capabilities & WlSeat.CapabilityEnum.Keyboard) != 0 && keyboard == null)
        {
            keyboard = seat.GetKeyboard(new WlKeyboard.Listener.Relay
            {
                OnKeymap = (_, format, fd, size) => Keymap(format, fd, size),
                OnKey = (_, _, _, key, state) =>
                {
                    bool pressed = state == WlKeyboard.KeyStateEnum.Pressed;
                    EmitKey(key, pressed, false);
                    if (pressed && keymap != 0 && repeatRate > 0 && Native.xkb_keymap_key_repeats(keymap, key + 8) != 0)
                    { repeatKey = key; repeatAt = Environment.TickCount64 + repeatDelay; }
                    else if (!pressed && repeatKey == key) repeatKey = null;
                },
                OnModifiers = (_, _, depressed, latched, locked, group) =>
                {
                    if (xkbState != 0) Native.xkb_state_update_mask(xkbState, depressed, latched, locked, 0, 0, group);
                    UpdateModifiers();
                },
                OnRepeatInfo = (_, rate, delay) => { repeatRate = Math.Max(0, rate); repeatDelay = Math.Max(0, delay); if (rate <= 0) repeatKey = null; },
                OnLeave = (_, _, _) => { repeatKey = null; modifiers = OverlayModifiers.None; }
            }, null);
        }
        else if ((capabilities & WlSeat.CapabilityEnum.Keyboard) == 0 && keyboard != null)
        {
            if (keyboard.IsReleaseAvailable) keyboard.Release();
            keyboard = null; repeatKey = null; modifiers = OverlayModifiers.None;
        }
    }

    private void Move(WlFixed x, WlFixed y)
    {
        if (focus == null) return;
        pointerX = focus.Output.Rect.X + (double)x; pointerY = focus.Output.Rect.Y + (double)y;
        events.Writer.TryWrite(new PointerMoved(pointerX, pointerY, modifiers));
    }

    private void Wheel()
    {
        double y = value120[0] ?? discrete[0] ?? axis[0], x = value120[1] ?? discrete[1] ?? axis[1];
        if (x != 0 || y != 0) events.Writer.TryWrite(new PointerWheel(-x, -y, pointerX, pointerY, modifiers));
        Array.Clear(axis); Array.Clear(discrete); Array.Clear(value120);
    }

    public void SetCursor(OverlayCursor cursor) => Enqueue(() => { this.cursor = cursor; ApplyCursor(); });

    private void ApplyCursor()
    {
        if (pointer == null || focus == null) return;
        if (cursor == OverlayCursor.Hidden) { pointer.SetCursor(enterSerial, null, 0, 0); return; }
        cursorDevice ??= cursorManager?.GetPointer(pointer, null, null);
        cursorDevice?.SetShape(enterSerial, cursor switch
        {
            OverlayCursor.Crosshair => WpCursorShapeDeviceV1.ShapeEnum.Crosshair,
            OverlayCursor.Grab => WpCursorShapeDeviceV1.ShapeEnum.Grab,
            OverlayCursor.Grabbing => WpCursorShapeDeviceV1.ShapeEnum.Grabbing,
            OverlayCursor.Move => WpCursorShapeDeviceV1.ShapeEnum.Move,
            OverlayCursor.ResizeNS => WpCursorShapeDeviceV1.ShapeEnum.NsResize,
            OverlayCursor.ResizeEW => WpCursorShapeDeviceV1.ShapeEnum.EwResize,
            OverlayCursor.ResizeNWSE => WpCursorShapeDeviceV1.ShapeEnum.NwseResize,
            OverlayCursor.ResizeNESW => WpCursorShapeDeviceV1.ShapeEnum.NeswResize,
            OverlayCursor.Text => WpCursorShapeDeviceV1.ShapeEnum.Text,
            OverlayCursor.Pointer => WpCursorShapeDeviceV1.ShapeEnum.Pointer,
            _ => WpCursorShapeDeviceV1.ShapeEnum.Default
        });
    }

    private void Keymap(WlKeyboard.KeymapFormatEnum format, WaylandFd received, uint size)
    {
        int fd = received.Consume();
        nint map = -1;
        try
        {
            if (format != WlKeyboard.KeymapFormatEnum.XkbV1 || size == 0) return;
            map = Native.mmap(0, size, 1, 2, fd, 0);
            if (map == -1) throw new IOException("Could not map Wayland keyboard keymap.");
            xkbContext = xkbContext == 0 ? Native.xkb_context_new(0) : xkbContext;
            if (xkbContext == 0) throw new IOException("Could not create xkb context.");
            nint next = Native.xkb_keymap_new_from_string(xkbContext, map, 1, 0);
            if (next == 0) throw new IOException("Invalid Wayland xkb keymap.");
            if (xkbState != 0) Native.xkb_state_unref(xkbState);
            if (keymap != 0) Native.xkb_keymap_unref(keymap);
            keymap = next; xkbState = Native.xkb_state_new(keymap); repeatKey = null;
            if (xkbState == 0) throw new IOException("Could not create xkb state.");
            UpdateModifiers();
        }
        finally { if (map != -1) Native.munmap(map, size); Native.close(fd); }
    }

    private void UpdateModifiers()
    {
        modifiers = OverlayModifiers.None;
        if (xkbState == 0) return;
        foreach (var (name, flag) in new[] { ("Shift", OverlayModifiers.Shift), ("Control", OverlayModifiers.Control), ("Mod1", OverlayModifiers.Alt), ("Mod4", OverlayModifiers.Super), ("Lock", OverlayModifiers.CapsLock), ("Mod2", OverlayModifiers.NumLock) })
            if (Native.xkb_state_mod_name_is_active(xkbState, name, 8) > 0) modifiers |= flag;
    }

    private void EmitKey(uint key, bool pressed, bool repeat)
    {
        uint symbol = xkbState == 0 ? 0 : Native.xkb_state_key_get_one_sym(xkbState, key + 8);
        string? text = null;
        if (pressed && xkbState != 0)
        {
            var bytes = new byte[128];
            int length = Native.xkb_state_key_get_utf8(xkbState, key + 8, bytes, (nuint)bytes.Length);
            if (length > 0)
            {
                if (length >= bytes.Length) { bytes = new byte[length + 1]; Native.xkb_state_key_get_utf8(xkbState, key + 8, bytes, (nuint)bytes.Length); }
                var decoded = Encoding.UTF8.GetString(bytes, 0, length);
                if (!decoded.Any(char.IsControl)) text = decoded;
            }
        }
        events.Writer.TryWrite(new KeyEvent(key, symbol, pressed, repeat, text, modifiers));
    }

    private int RepeatTimeout() => repeatKey.HasValue ? (int)Math.Clamp(repeatAt - Environment.TickCount64, 0, 1000) : 1000;
    private void Repeat()
    {
        if (repeatKey is { } key && Environment.TickCount64 >= repeatAt)
        { EmitKey(key, true, true); repeatAt = Environment.TickCount64 + Math.Max(1, 1000 / Math.Max(1, repeatRate)); }
    }

    private void DestroyInput()
    {
        cursorDevice?.Destroy();
        if (pointer?.IsReleaseAvailable == true) pointer.Release();
        if (keyboard?.IsReleaseAvailable == true) keyboard.Release();
        if (seat?.IsReleaseAvailable == true) seat.Release();
        if (xkbState != 0) Native.xkb_state_unref(xkbState);
        if (keymap != 0) Native.xkb_keymap_unref(keymap);
        if (xkbContext != 0) Native.xkb_context_unref(xkbContext);
    }
}
