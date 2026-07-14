#define _GNU_SOURCE
#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <glib-unix.h>
#include <fcntl.h>
#include <errno.h>
#include <unistd.h>
#include <stdio.h>

static GDBusConnection* session_connection;
static gint inhibitor_read_fd = -1;

static const gchar introspection_xml[] =
    "<node>"
    " <interface name='org.freedesktop.login1.Manager'>"
    "  <method name='Inhibit'>"
    "   <arg type='s' direction='in'/><arg type='s' direction='in'/>"
    "   <arg type='s' direction='in'/><arg type='s' direction='in'/>"
    "   <arg type='h' direction='out'/>"
    "  </method>"
    "  <signal name='PrepareForShutdown'><arg type='b'/></signal>"
    " </interface>"
    "</node>";

static gboolean emit_prepare_for_shutdown(gpointer data)
{
    (void)data;
    GError* error = NULL;
    g_dbus_connection_emit_signal(
        session_connection,
        NULL,
        "/org/freedesktop/login1",
        "org.freedesktop.login1.Manager",
        "PrepareForShutdown",
        g_variant_new("(b)", TRUE),
        &error);
    if (error)
    {
        g_printerr("PrepareForShutdown emit failed: %s\n", error->message);
        g_error_free(error);
        return G_SOURCE_REMOVE;
    }
    g_print("FAKE_LOGIND_PREPARE_EMITTED\n");
    fflush(stdout);
    return G_SOURCE_REMOVE;
}

static gboolean inhibitor_closed(gint fd, GIOCondition condition, gpointer data)
{
    (void)condition;
    (void)data;
    char value = 0;
    ssize_t result = read(fd, &value, 1);
    if (result == 0)
    {
        g_print("FAKE_LOGIND_INHIBITOR_RELEASED\n");
        fflush(stdout);
        close(fd);
        inhibitor_read_fd = -1;
        return G_SOURCE_REMOVE;
    }
    if (result < 0 && (errno == EAGAIN || errno == EWOULDBLOCK))
        return G_SOURCE_CONTINUE;
    return G_SOURCE_CONTINUE;
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
    (void)connection;
    (void)sender;
    (void)object_path;
    (void)interface_name;
    (void)parameters;
    (void)user_data;
    if (g_strcmp0(method_name, "Inhibit") != 0)
    {
        g_dbus_method_invocation_return_dbus_error(
            invocation,
            "org.freedesktop.DBus.Error.UnknownMethod",
            "Unknown fake logind method.");
        return;
    }

    int descriptors[2];
    if (pipe2(descriptors, O_CLOEXEC | O_NONBLOCK) != 0)
    {
        g_dbus_method_invocation_return_dbus_error(
            invocation,
            "org.freedesktop.login1.Error.Failed",
            "Unable to allocate inhibitor pipe.");
        return;
    }
    inhibitor_read_fd = descriptors[0];

    GError* error = NULL;
    GUnixFDList* list = g_unix_fd_list_new();
    gint handle = g_unix_fd_list_append(list, descriptors[1], &error);
    close(descriptors[1]);
    if (handle < 0)
    {
        close(inhibitor_read_fd);
        inhibitor_read_fd = -1;
        g_dbus_method_invocation_return_gerror(invocation, error);
        g_error_free(error);
        g_object_unref(list);
        return;
    }

    g_unix_fd_add(
        inhibitor_read_fd,
        G_IO_IN | G_IO_HUP | G_IO_ERR,
        inhibitor_closed,
        NULL);
    g_dbus_method_invocation_return_value_with_unix_fd_list(
        invocation,
        g_variant_new("(h)", handle),
        list);
    g_object_unref(list);

    g_print("FAKE_LOGIND_INHIBITOR_OK\n");
    fflush(stdout);
    g_timeout_add(50, emit_prepare_for_shutdown, NULL);
}

static const GDBusInterfaceVTable interface_vtable =
{
    method_call,
    NULL,
    NULL,
    { 0 }
};

static gboolean request_logind_name(GDBusConnection* connection)
{
    GError* error = NULL;
    GVariant* reply = g_dbus_connection_call_sync(
        connection,
        "org.freedesktop.DBus",
        "/org/freedesktop/DBus",
        "org.freedesktop.DBus",
        "RequestName",
        g_variant_new("(su)", "org.freedesktop.login1", 0u),
        G_VARIANT_TYPE("(u)"),
        G_DBUS_CALL_FLAGS_NONE,
        5000,
        NULL,
        &error);
    if (!reply)
    {
        g_printerr("RequestName failed: %s\n", error->message);
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
    session_connection = g_bus_get_sync(G_BUS_TYPE_SYSTEM, NULL, &error);
    if (!session_connection)
    {
        g_printerr("system bus connection failed: %s\n", error->message);
        g_error_free(error);
        return 2;
    }

    GDBusNodeInfo* info =
        g_dbus_node_info_new_for_xml(introspection_xml, &error);
    if (!info)
    {
        g_printerr("introspection failed: %s\n", error->message);
        g_error_free(error);
        g_object_unref(session_connection);
        return 3;
    }

    guint registration = g_dbus_connection_register_object(
        session_connection,
        "/org/freedesktop/login1",
        info->interfaces[0],
        &interface_vtable,
        NULL,
        NULL,
        &error);
    if (!registration || !request_logind_name(session_connection))
    {
        if (error)
        {
            g_printerr("object registration failed: %s\n", error->message);
            g_error_free(error);
        }
        g_dbus_node_info_unref(info);
        g_object_unref(session_connection);
        return 4;
    }

    g_print("FAKE_LOGIND_READY\n");
    fflush(stdout);
    GMainLoop* loop = g_main_loop_new(NULL, FALSE);
    g_main_loop_run(loop);

    if (inhibitor_read_fd >= 0)
        close(inhibitor_read_fd);
    g_dbus_connection_unregister_object(session_connection, registration);
    g_main_loop_unref(loop);
    g_dbus_node_info_unref(info);
    g_object_unref(session_connection);
    return 0;
}
