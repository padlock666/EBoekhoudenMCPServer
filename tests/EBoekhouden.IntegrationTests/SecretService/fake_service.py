"""Synthetic Secret Service on an explicitly supplied private, non-activating bus.
Only disposable credentials; never discovers a desktop session or opens keyring files.
"""
import json
import sys
import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

ADDRESS = sys.argv[1]
NAME = "org.freedesktop.secrets"
ROOT = "/org/freedesktop/secrets"
ITEM = ROOT + "/collection/synthetic/item1"
ATTRS = {"application": "EBoekhoudenMcp", "account": "api"}
config = {"scenario": "valid", "pause": "", "secret": "synthetic-api-secret-PHASE6", "attributes": ATTRS}
pending = []
XML = """<node><interface name='org.freedesktop.Secret.Service'>
<method name='OpenSession'><arg type='s' direction='in'/><arg type='v' direction='in'/><arg type='v' direction='out'/><arg type='o' direction='out'/></method>
<method name='SearchItems'><arg type='a{ss}' direction='in'/><arg type='ao' direction='out'/><arg type='ao' direction='out'/></method>
<method name='GetSecrets'><arg type='ao' direction='in'/><arg type='o' direction='in'/><arg type='a{o(oayays)}' direction='out'/></method>
<method name='Unlock'><arg type='ao' direction='in'/><arg type='ao' direction='out'/><arg type='o' direction='out'/></method>
</interface><interface name='org.freedesktop.Secret.Session'><method name='Close'/></interface>
<interface name='org.freedesktop.DBus.Properties'><method name='GetAll'><arg type='s' direction='in'/><arg type='a{sv}' direction='out'/></method></interface>
</node>"""
info = Gio.DBusNodeInfo.new_for_xml(XML)
flags = Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT | Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION
connection = Gio.DBusConnection.new_for_address_sync(ADDRESS, flags, None, None)
connection.set_exit_on_close(False)


def event(value):
    print(value, flush=True)


def respond(path, iface, method, parameters, invocation):
    args = parameters.unpack()
    scenario = config["scenario"]
    if scenario == "denied" or (scenario == "denied_load" and method == "GetSecrets"):
        invocation.return_dbus_error("org.freedesktop.DBus.Error.AccessDenied", "synthetic denied detail")
    elif method == "GetAll":
        invocation.return_value(GLib.Variant("(a{sv})", ({"Collections": GLib.Variant("ao", [])},)))
    elif method == "SearchItems":
        # The null-schema API must send only the two documented attributes.
        assert set(args[0]) == {"application", "account"}
        found = args[0] == config["attributes"] and scenario != "missing"
        invocation.return_value(GLib.Variant("(aoao)",
            ([ITEM] if found and scenario != "locked" else [], [ITEM] if found and scenario == "locked" else [])))
    elif method == "Unlock":
        # Simulates an item that remains inaccessible, with no graphical prompt available.
        invocation.return_value(GLib.Variant("(aoo)", ([], "/")))
    elif method == "OpenSession":
        if args[0] != "plain":
            invocation.return_dbus_error("org.freedesktop.DBus.Error.NotSupported", "synthetic service supports plain session only")
        else:
            invocation.return_value(GLib.Variant("(vo)", (GLib.Variant("s", ""), ROOT + "/session/synthetic")))
    elif method == "GetSecrets":
        value = config["secret"].encode("utf-8")
        if scenario == "invalid_utf8": value = b"\xc0\xaf"
        if scenario == "empty": value = b""
        if scenario == "oversize": value = b"X" * 4097
        if scenario == "control": value = b"synthetic\x7fsecret"
        if scenario == "newline": value = b"synthetic\r\nsecret"
        if scenario == "blank": value = " \u2003 ".encode("utf-8")
        content_type = "application/octet-stream" if scenario == "binary" else "text/plain"
        values = {} if scenario == "deleted" else {ITEM: (args[1], [], list(value), content_type)}
        invocation.return_value(GLib.Variant("(a{o(oayays)})", (values,)))
    elif method == "Close":
        invocation.return_value(GLib.Variant("()", ()))
    else:
        event("FORBIDDEN:" + method)
        invocation.return_dbus_error("org.freedesktop.DBus.Error.UnknownMethod", "Unsupported synthetic operation")


def dispatch(conn, sender, path, iface, method, parameters, invocation):
    stage = {"SearchItems": "search", "GetSecrets": "load"}.get(method, "metadata")
    event(stage + ":" + method)
    if config["pause"] == stage:
        pending.append((path, iface, method, parameters, invocation))
    else:
        respond(path, iface, method, parameters, invocation)


registrations = []
for path in [ROOT, ROOT + "/session/synthetic"]:
    for interface in info.interfaces:
        registrations.append(connection.register_object(path, interface, dispatch, None, None))


def stdin_ready(source, condition):
    line = sys.stdin.readline()
    if not line:
        loop.quit()
        return False
    command = json.loads(line)
    config.update(command.get("config", {}))
    if command.get("release"):
        waiting = list(pending)
        pending.clear()
        for args in waiting: respond(*args)
    event("ack")
    return True


connection.call_sync("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName",
                     GLib.Variant("(su)", (NAME, 4)), None, Gio.DBusCallFlags.NO_AUTO_START, 3000, None)
GLib.io_add_watch(sys.stdin, GLib.IO_IN | GLib.IO_HUP, stdin_ready)
loop = GLib.MainLoop()
event("ready")
try:
    loop.run()
finally:
    connection.close_sync(None)
