#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <unistd.h>
#include <string.h>
#include <stdio.h>

static const gchar introspection_xml[] =
    "<node>"
    " <interface name='org.freedesktop.portal.Print'>"
    "  <property name='version' type='u' access='read'/>"
    "  <method name='PreparePrint'>"
    "   <arg type='s' direction='in'/><arg type='s' direction='in'/>"
    "   <arg type='a{sv}' direction='in'/><arg type='a{sv}' direction='in'/>"
    "   <arg type='a{sv}' direction='in'/><arg type='o' direction='out'/>"
    "  </method>"
    "  <method name='Print'>"
    "   <arg type='s' direction='in'/><arg type='s' direction='in'/>"
    "   <arg type='h' direction='in'/><arg type='a{sv}' direction='in'/>"
    "   <arg type='o' direction='out'/>"
    "  </method>"
    " </interface>"
    "</node>";

typedef struct
{
    GDBusConnection* connection;
    gchar* path;
    gboolean prepare;
} PendingResponse;

static gboolean emit_response(gpointer data)
{
    PendingResponse* pending = data;
    GVariantBuilder builder;
    g_variant_builder_init(&builder, G_VARIANT_TYPE("a{sv}"));
    if (pending->prepare)
        g_variant_builder_add(
            &builder, "{sv}", "token", g_variant_new_uint32(777));

    GError* error = NULL;
    g_dbus_connection_emit_signal(
        pending->connection,
        NULL,
        pending->path,
        "org.freedesktop.portal.Request",
        "Response",
        g_variant_new("(u@a{sv})", 0u, g_variant_builder_end(&builder)),
        &error);
    if (error)
    {
        g_printerr("emit failed: %s\n", error->message);
        g_error_free(error);
    }

    g_object_unref(pending->connection);
    g_free(pending->path);
    g_free(pending);
    return G_SOURCE_REMOVE;
}

static gchar* build_request_path(
    GDBusMethodInvocation* invocation,
    GVariant* options)
{
    const gchar* token = NULL;
    if (!g_variant_lookup(options, "handle_token", "&s", &token) || !token)
        token = "missing_token";

    const gchar* sender = g_dbus_method_invocation_get_sender(invocation);
    gchar* safe_sender = g_strdup(
        sender && sender[0] == ':' ? sender + 1 : sender);
    for (gchar* current = safe_sender; current && *current; ++current)
    {
        if (*current == '.')
            *current = '_';
    }

    gchar* path = g_strdup_printf(
        "/org/freedesktop/portal/desktop/request/%s/%s",
        safe_sender,
        token);
    g_free(safe_sender);
    return path;
}

static gboolean validate_pdf_descriptor(
    GDBusMethodInvocation* invocation,
    GVariant* parameters)
{
    gint32 handle = -1;
    g_variant_get_child(parameters, 2, "h", &handle);
    GDBusMessage* message = g_dbus_method_invocation_get_message(invocation);
    GUnixFDList* descriptors = g_dbus_message_get_unix_fd_list(message);
    GError* error = NULL;
    gint descriptor = descriptors
        ? g_unix_fd_list_get(descriptors, handle, &error)
        : -1;
    char header[4] = { 0 };
    ssize_t count = descriptor >= 0
        ? read(descriptor, header, sizeof(header))
        : -1;
    if (descriptor >= 0)
        close(descriptor);
    if (error)
        g_error_free(error);
    return count == 4 && memcmp(header, "%PDF", 4) == 0;
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
    (void)interface_name;
    (void)user_data;

    GVariant* options = NULL;
    gboolean prepare = g_strcmp0(method_name, "PreparePrint") == 0;
    if (prepare)
    {
        options = g_variant_get_child_value(parameters, 4);
    }
    else if (g_strcmp0(method_name, "Print") == 0)
    {
        options = g_variant_get_child_value(parameters, 3);
        if (!validate_pdf_descriptor(invocation, parameters))
        {
            g_dbus_method_invocation_return_dbus_error(
                invocation,
                "org.freedesktop.portal.Error.Failed",
                "The transferred file descriptor did not contain a PDF document.");
            g_variant_unref(options);
            return;
        }
        g_print("FAKE_PORTAL_PDF_FD_OK\n");
        fflush(stdout);
    }
    else
    {
        g_dbus_method_invocation_return_dbus_error(
            invocation,
            "org.freedesktop.DBus.Error.UnknownMethod",
            "Unknown fake portal method.");
        return;
    }

    gchar* path = build_request_path(invocation, options);
    g_variant_unref(options);
    g_dbus_method_invocation_return_value(
        invocation, g_variant_new("(o)", path));

    PendingResponse* pending = g_new0(PendingResponse, 1);
    pending->connection = g_object_ref(connection);
    pending->path = g_strdup(path);
    pending->prepare = prepare;
    g_timeout_add(20, emit_response, pending);
    g_free(path);
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
    return g_strcmp0(property_name, "version") == 0
        ? g_variant_new_uint32(1)
        : NULL;
}

static const GDBusInterfaceVTable interface_vtable =
{
    method_call,
    get_property,
    NULL,
    { 0 }
};

static gboolean request_portal_name(GDBusConnection* connection)
{
    GError* error = NULL;
    GVariant* reply = g_dbus_connection_call_sync(
        connection,
        "org.freedesktop.DBus",
        "/org/freedesktop/DBus",
        "org.freedesktop.DBus",
        "RequestName",
        g_variant_new("(su)", "org.freedesktop.portal.Desktop", 0u),
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
    GDBusConnection* connection =
        g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, &error);
    if (!connection)
    {
        g_printerr("bus connection failed: %s\n", error->message);
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

    guint registration = g_dbus_connection_register_object(
        connection,
        "/org/freedesktop/portal/desktop",
        info->interfaces[0],
        &interface_vtable,
        NULL,
        NULL,
        &error);
    if (!registration || !request_portal_name(connection))
    {
        if (error)
        {
            g_printerr("object registration failed: %s\n", error->message);
            g_error_free(error);
        }
        g_dbus_node_info_unref(info);
        g_object_unref(connection);
        return 4;
    }

    g_print("FAKE_PORTAL_READY\n");
    fflush(stdout);
    GMainLoop* loop = g_main_loop_new(NULL, FALSE);
    g_main_loop_run(loop);

    g_dbus_connection_unregister_object(connection, registration);
    g_main_loop_unref(loop);
    g_dbus_node_info_unref(info);
    g_object_unref(connection);
    return 0;
}
