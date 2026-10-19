#!/usr/bin/python
# GPL-3.0-or-later. Private-session D-Bus desktop used by run.py.
import gi, json, os
from pathlib import Path
from gi.repository import Gio, GLib
root = Path(os.environ['SX_TEST_ROOT'])
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
for name in ['org.freedesktop.Notifications', 'org.freedesktop.FileManager1']:
    bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus', 'RequestName', GLib.Variant('(su)', (name, 0)), None, 0, 2000, None)
xml = '''<node><interface name="org.freedesktop.Notifications">
<method name="GetCapabilities"><arg type="as" direction="out"/></method>
<method name="GetServerInformation"><arg type="s" direction="out"/><arg type="s" direction="out"/><arg type="s" direction="out"/><arg type="s" direction="out"/></method>
<method name="Notify"><arg type="s" direction="in"/><arg type="u" direction="in"/><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="as" direction="in"/><arg type="a{sv}" direction="in"/><arg type="i" direction="in"/><arg type="u" direction="out"/></method>
<method name="CloseNotification"><arg type="u" direction="in"/></method>
<signal name="ActionInvoked"><arg type="u"/><arg type="s"/></signal>
<signal name="NotificationClosed"><arg type="u"/><arg type="u"/></signal>
</interface><interface name="org.freedesktop.FileManager1"><method name="ShowItems"><arg type="as" direction="in"/><arg type="s" direction="in"/></method></interface></node>'''
info = Gio.DBusNodeInfo.new_for_xml(xml)
def call(connection, sender, path, interface, method, params, invocation):
    value = params.unpack()
    if method == 'GetCapabilities': invocation.return_value(GLib.Variant('(as)', (['actions', 'body', 'body-markup'],)))
    elif method == 'GetServerInformation': invocation.return_value(GLib.Variant('(ssss)', ('ShareX test', 'ShareX', '1', '1.2')))
    elif method == 'Notify':
        (root / 'notification.json').write_text(json.dumps(value))
        invocation.return_value(GLib.Variant('(u)', (123,)))
        def emit():
            connection.emit_signal(None, path, interface, 'ActionInvoked', GLib.Variant('(us)', (123, 'folder')))
            connection.emit_signal(None, path, interface, 'NotificationClosed', GLib.Variant('(uu)', (123, 2)))
            return False
        GLib.timeout_add(150, emit)
    elif method == 'CloseNotification': invocation.return_value(None)
    elif method == 'ShowItems':
        if (root / 'deny-showitems').exists(): invocation.return_dbus_error('org.freedesktop.DBus.Error.Failed', 'test fallback')
        else:
            (root / 'showitems.json').write_text(json.dumps(value))
            invocation.return_value(None)
for interface in info.interfaces:
    path = '/org/freedesktop/Notifications' if interface.name.endswith('Notifications') else '/org/freedesktop/FileManager1'
    bus.register_object(path, interface, call, None, None)
(root / 'dbus-ready').write_text('ready')
GLib.MainLoop().run()
