#include <gio/gio.h>
#include <glib.h>
#include <stdio.h>
#include <string.h>

static const gchar introspection_xml[] =
    "<node>"
    " <interface name='org.freedesktop.StatusNotifierWatcher'>"
    "  <method name='RegisterStatusNotifierItem'><arg type='s' direction='in'/></method>"
    "  <method name='RegisterStatusNotifierHost'><arg type='s' direction='in'/></method>"
    "  <property name='RegisteredStatusNotifierItems' type='as' access='read'/>"
    "  <property name='IsStatusNotifierHostRegistered' type='b' access='read'/>"
    "  <property name='ProtocolVersion' type='i' access='read'/>"
    "  <signal name='StatusNotifierItemRegistered'><arg type='s'/></signal>"
    "  <signal name='StatusNotifierItemUnregistered'><arg type='s'/></signal>"
    "  <signal name='StatusNotifierHostRegistered'/>"
    " </interface>"
    " <interface name='org.kde.StatusNotifierWatcher'>"
    "  <method name='RegisterStatusNotifierItem'><arg type='s' direction='in'/></method>"
    "  <method name='RegisterStatusNotifierHost'><arg type='s' direction='in'/></method>"
    "  <property name='RegisteredStatusNotifierItems' type='as' access='read'/>"
    "  <property name='IsStatusNotifierHostRegistered' type='b' access='read'/>"
    "  <property name='ProtocolVersion' type='i' access='read'/>"
    "  <signal name='StatusNotifierItemRegistered'><arg type='s'/></signal>"
    "  <signal name='StatusNotifierItemUnregistered'><arg type='s'/></signal>"
    "  <signal name='StatusNotifierHostRegistered'/>"
    " </interface>"
    " <interface name='org.freedesktop.Notifications'>"
    "  <method name='GetCapabilities'><arg type='as' direction='out'/></method>"
    "  <method name='Notify'>"
    "   <arg type='s' direction='in'/><arg type='u' direction='in'/>"
    "   <arg type='s' direction='in'/><arg type='s' direction='in'/>"
    "   <arg type='s' direction='in'/><arg type='as' direction='in'/>"
    "   <arg type='a{sv}' direction='in'/><arg type='i' direction='in'/>"
    "   <arg type='u' direction='out'/>"
    "  </method>"
    "  <method name='CloseNotification'><arg type='u' direction='in'/></method>"
    "  <method name='GetServerInformation'>"
    "   <arg type='s' direction='out'/><arg type='s' direction='out'/>"
    "   <arg type='s' direction='out'/><arg type='s' direction='out'/>"
    "  </method>"
    "  <signal name='NotificationClosed'><arg type='u'/><arg type='u'/></signal>"
    "  <signal name='ActionInvoked'><arg type='u'/><arg type='s'/></signal>"
    " </interface>"
    "</node>";

typedef struct
{
    GDBusConnection* connection;
    guint32 id;
} PendingNotification;

static guint32 next_notification_id = 100;

static gboolean emit_notification_closed(gpointer data)
{
    PendingNotification* pending = data;
    GError* error = NULL;
    g_dbus_connection_emit_signal(
        pending->connection,
        NULL,
        "/org/freedesktop/Notifications",
        "org.freedesktop.Notifications",
        "NotificationClosed",
        g_variant_new("(uu)", pending->id, 2u),
        &error);
    if (error)
    {
        g_printerr("NotificationClosed emit failed: %s\n", error->message);
        g_error_free(error);
    }
    else
    {
        g_print("FAKE_NOTIFICATION_CLOSED id=%u\n", pending->id);
        fflush(stdout);
    }

    g_object_unref(pending->connection);
    g_free(pending);
    return G_SOURCE_REMOVE;
}

static gboolean emit_notification_action(gpointer data)
{
    PendingNotification* pending = data;
    GError* error = NULL;
    g_dbus_connection_emit_signal(
        pending->connection,
        NULL,
        "/org/freedesktop/Notifications",
        "org.freedesktop.Notifications",
        "ActionInvoked",
        g_variant_new("(us)", pending->id, "default"),
        &error);
    if (error)
    {
        g_printerr("ActionInvoked emit failed: %s\n", error->message);
        g_error_free(error);
    }
    else
    {
        g_print("FAKE_NOTIFICATION_ACTION id=%u action=default\n", pending->id);
        fflush(stdout);
    }

    g_timeout_add(100, emit_notification_closed, pending);
    return G_SOURCE_REMOVE;
}

static gboolean actions_contain_default(GVariant* parameters)
{
    GVariant* actions = g_variant_get_child_value(parameters, 5);
    gboolean found = FALSE;
    for (gsize index = 0; index < g_variant_n_children(actions); ++index)
    {
        const gchar* action = NULL;
        g_variant_get_child(actions, index, "&s", &action);
        if (g_strcmp0(action, "default") == 0)
        {
            found = TRUE;
            break;
        }
    }
    g_variant_unref(actions);
    return found;
}

static void handle_watcher_method(
    GDBusConnection* connection,
    const gchar* interface_name,
    const gchar* method_name,
    GVariant* parameters,
    GDBusMethodInvocation* invocation)
{
    if (g_strcmp0(method_name, "RegisterStatusNotifierItem") == 0)
    {
        const gchar* service = NULL;
        g_variant_get(parameters, "(&s)", &service);
        const gchar* flavour = g_str_has_prefix(interface_name, "org.kde.")
            ? "kde"
            : "freedesktop";
        g_print("FAKE_SNI_REGISTER %s %s\n", flavour, service);
        fflush(stdout);
        g_dbus_method_invocation_return_value(invocation, NULL);

        GError* error = NULL;
        g_dbus_connection_emit_signal(
            connection,
            NULL,
            "/StatusNotifierWatcher",
            interface_name,
            "StatusNotifierItemRegistered",
            g_variant_new("(s)", service),
            &error);
        if (error)
        {
            g_printerr("registration signal failed: %s\n", error->message);
            g_error_free(error);
        }
        return;
    }

    if (g_strcmp0(method_name, "RegisterStatusNotifierHost") == 0)
    {
        g_dbus_method_invocation_return_value(invocation, NULL);
        return;
    }

    g_dbus_method_invocation_return_dbus_error(
        invocation,
        "org.freedesktop.DBus.Error.UnknownMethod",
        "Unknown fake StatusNotifierWatcher method.");
}

static void handle_notification_method(
    GDBusConnection* connection,
    const gchar* method_name,
    GVariant* parameters,
    GDBusMethodInvocation* invocation)
{
    if (g_strcmp0(method_name, "GetCapabilities") == 0)
    {
        const gchar* capabilities[] = { "actions", "body", NULL };
        g_dbus_method_invocation_return_value(
            invocation,
            g_variant_new("(@as)", g_variant_new_strv(capabilities, -1)));
        return;
    }

    if (g_strcmp0(method_name, "GetServerInformation") == 0)
    {
        g_dbus_method_invocation_return_value(
            invocation,
            g_variant_new("(ssss)",
                "Jalium fake notifications", "Jalium.UI", "1.0", "1.2"));
        return;
    }

    if (g_strcmp0(method_name, "Notify") == 0)
    {
        const gchar* summary = NULL;
        g_variant_get_child(parameters, 3, "&s", &summary);
        gboolean has_default_action = actions_contain_default(parameters);
        guint32 id = ++next_notification_id;
        g_print(
            "FAKE_NOTIFICATION_NOTIFY id=%u action=%s summary=%s\n",
            id,
            has_default_action ? "default" : "missing",
            summary ? summary : "");
        fflush(stdout);
        g_dbus_method_invocation_return_value(invocation, g_variant_new("(u)", id));

        if (has_default_action)
        {
            PendingNotification* pending = g_new0(PendingNotification, 1);
            pending->connection = g_object_ref(connection);
            pending->id = id;
            g_timeout_add(300, emit_notification_action, pending);
        }
        return;
    }

    if (g_strcmp0(method_name, "CloseNotification") == 0)
    {
        g_dbus_method_invocation_return_value(invocation, NULL);
        return;
    }

    g_dbus_method_invocation_return_dbus_error(
        invocation,
        "org.freedesktop.DBus.Error.UnknownMethod",
        "Unknown fake notification method.");
}

static void method_call(
    GDBusConnection* connection,
    const gchar* sender,
    const gchar* object_path,
    const gchar* interface_name,
    const gchar* method_name,
    GVariant* parameters,
    GDBusMethodInvocation* invocation,
    gpointer user_data)
{
    (void)sender;
    (void)object_path;
    (void)user_data;
    if (g_strcmp0(interface_name, "org.freedesktop.Notifications") == 0)
    {
        handle_notification_method(connection, method_name, parameters, invocation);
        return;
    }
    handle_watcher_method(connection, interface_name, method_name, parameters, invocation);
}

static GVariant* get_property(
    GDBusConnection* connection,
    const gchar* sender,
    const gchar* object_path,
    const gchar* interface_name,
    const gchar* property_name,
    GError** error,
    gpointer user_data)
{
    (void)connection;
    (void)sender;
    (void)object_path;
    (void)interface_name;
    (void)error;
    (void)user_data;
    if (g_strcmp0(property_name, "RegisteredStatusNotifierItems") == 0)
        return g_variant_new_strv(NULL, 0);
    if (g_strcmp0(property_name, "IsStatusNotifierHostRegistered") == 0)
        return g_variant_new_boolean(TRUE);
    if (g_strcmp0(property_name, "ProtocolVersion") == 0)
        return g_variant_new_int32(0);
    return NULL;
}

static const GDBusInterfaceVTable interface_vtable =
{
    method_call,
    get_property,
    NULL,
    { 0 }
};

static gboolean request_name(GDBusConnection* connection, const gchar* name)
{
    GError* error = NULL;
    GVariant* reply = g_dbus_connection_call_sync(
        connection,
        "org.freedesktop.DBus",
        "/org/freedesktop/DBus",
        "org.freedesktop.DBus",
        "RequestName",
        g_variant_new("(su)", name, 0u),
        G_VARIANT_TYPE("(u)"),
        G_DBUS_CALL_FLAGS_NONE,
        5000,
        NULL,
        &error);
    if (!reply)
    {
        g_printerr("RequestName %s failed: %s\n", name, error->message);
        g_error_free(error);
        return FALSE;
    }
    guint32 result = 0;
    g_variant_get(reply, "(u)", &result);
    g_variant_unref(reply);
    return result == 1 || result == 4;
}

int main(void)
{
    GError* error = NULL;
    GDBusConnection* connection =
        g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, &error);
    if (!connection)
    {
        g_printerr("session bus connection failed: %s\n", error->message);
        g_error_free(error);
        return 2;
    }

    GDBusNodeInfo* info =
        g_dbus_node_info_new_for_xml(introspection_xml, &error);
    if (!info)
    {
        g_printerr("introspection failed: %s\n", error->message);
        g_error_free(error);
        g_object_unref(connection);
        return 3;
    }

    guint registrations[3] = { 0, 0, 0 };
    registrations[0] = g_dbus_connection_register_object(
        connection,
        "/StatusNotifierWatcher",
        info->interfaces[0],
        &interface_vtable,
        NULL,
        NULL,
        &error);
    registrations[1] = g_dbus_connection_register_object(
        connection,
        "/StatusNotifierWatcher",
        info->interfaces[1],
        &interface_vtable,
        NULL,
        NULL,
        &error);
    registrations[2] = g_dbus_connection_register_object(
        connection,
        "/org/freedesktop/Notifications",
        info->interfaces[2],
        &interface_vtable,
        NULL,
        NULL,
        &error);

    gboolean ready =
        registrations[0] != 0 && registrations[1] != 0 && registrations[2] != 0 &&
        request_name(connection, "org.freedesktop.StatusNotifierWatcher") &&
        request_name(connection, "org.kde.StatusNotifierWatcher") &&
        request_name(connection, "org.freedesktop.Notifications");
    if (!ready)
    {
        if (error)
        {
            g_printerr("object registration failed: %s\n", error->message);
            g_error_free(error);
        }
        for (guint index = 0; index < G_N_ELEMENTS(registrations); ++index)
        {
            if (registrations[index] != 0)
                g_dbus_connection_unregister_object(connection, registrations[index]);
        }
        g_dbus_node_info_unref(info);
        g_object_unref(connection);
        return 4;
    }

    g_print("FAKE_DESKTOP_PROTOCOLS_READY\n");
    fflush(stdout);
    GMainLoop* loop = g_main_loop_new(NULL, FALSE);
    g_main_loop_run(loop);

    for (guint index = 0; index < G_N_ELEMENTS(registrations); ++index)
        g_dbus_connection_unregister_object(connection, registrations[index]);
    g_main_loop_unref(loop);
    g_dbus_node_info_unref(info);
    g_object_unref(connection);
    return 0;
}
