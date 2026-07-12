using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Automation.AtSpi;

internal static class AtSpiNative
{
    private const string Gio = "libgio-2.0.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const int SessionBus = 2;
    private const int AuthenticationClient = 1 << 0;
    private const int MessageBusConnection = 1 << 3;

    private static readonly MethodCallCallback s_methodCall = OnMethodCall;
    private static readonly GetPropertyCallback s_getProperty = OnGetProperty;
    private static readonly SetPropertyCallback s_setProperty = OnSetProperty;
    private static readonly nint s_vtable = CreateVTable();
    private static readonly Lazy<bool> s_available = new(ProbeAvailability);

    internal static bool IsAvailable => s_available.Value;

    internal static nint OpenAccessibilityBus(out string uniqueName, out string? errorMessage)
    {
        uniqueName = string.Empty;
        errorMessage = null;
        nint session = 0;
        nint reply = 0;
        nint addressVariant = 0;
        nint connection = 0;
        nint error = 0;
        try
        {
            session = g_bus_get_sync(SessionBus, 0, out error);
            if (session == 0)
            {
                errorMessage = ReadError(error);
                return 0;
            }

            reply = g_dbus_connection_call_sync(
                session,
                "org.a11y.Bus",
                "/org/a11y/bus",
                "org.a11y.Bus",
                "GetAddress",
                0,
                0,
                0,
                3000,
                0,
                out error);
            if (reply == 0)
            {
                errorMessage = ReadError(error);
                return 0;
            }

            addressVariant = g_variant_get_child_value(reply, 0);
            string address = GetString(addressVariant);
            if (string.IsNullOrWhiteSpace(address))
            {
                errorMessage = "org.a11y.Bus returned an empty accessibility bus address.";
                return 0;
            }

            connection = g_dbus_connection_new_for_address_sync(
                address,
                AuthenticationClient | MessageBusConnection,
                0,
                0,
                out error);
            if (connection == 0)
            {
                errorMessage = ReadError(error);
                return 0;
            }

            g_dbus_connection_set_exit_on_close(connection, false);
            uniqueName = Marshal.PtrToStringUTF8(g_dbus_connection_get_unique_name(connection)) ?? string.Empty;
            if (uniqueName.Length == 0)
            {
                errorMessage = "The accessibility bus did not assign a unique name.";
                UnrefObject(ref connection);
                return 0;
            }

            return connection;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            UnrefObject(ref connection);
            return 0;
        }
        finally
        {
            FreeError(ref error);
            UnrefVariant(ref addressVariant);
            UnrefVariant(ref reply);
            UnrefObject(ref session);
        }
    }

    internal static nint ParseNodeInfo(string xml, out string? errorMessage)
    {
        errorMessage = null;
        nint error = 0;
        try
        {
            var result = g_dbus_node_info_new_for_xml(xml, out error);
            if (result == 0)
                errorMessage = ReadError(error);
            return result;
        }
        finally
        {
            FreeError(ref error);
        }
    }

    internal static nint LookupInterface(nint nodeInfo, string interfaceName) =>
        g_dbus_node_info_lookup_interface(nodeInfo, interfaceName);

    internal static uint RegisterObject(
        nint connection,
        string objectPath,
        nint interfaceInfo,
        nint userData,
        out string? errorMessage)
    {
        errorMessage = null;
        nint error = 0;
        try
        {
            uint id = g_dbus_connection_register_object(
                connection, objectPath, interfaceInfo, s_vtable, userData, 0, out error);
            if (id == 0)
                errorMessage = ReadError(error);
            return id;
        }
        finally
        {
            FreeError(ref error);
        }
    }

    internal static void UnregisterObject(nint connection, uint id)
    {
        if (connection != 0 && id != 0)
            _ = g_dbus_connection_unregister_object(connection, id);
    }

    internal static void Flush(nint connection)
    {
        if (connection == 0)
            return;
        nint error = 0;
        try
        {
            if (!g_dbus_connection_flush_sync(connection, 0, out error))
                AtSpiTrace.Log($"connection flush failed: {ReadError(error)}");
        }
        finally
        {
            FreeError(ref error);
        }
    }

    internal static bool EmbedApplication(
        nint connection,
        string uniqueName,
        string rootPath,
        out string? errorMessage)
    {
        errorMessage = null;
        nint parameters = 0;
        nint reply = 0;
        nint error = 0;
        try
        {
            parameters = ParseVariant($"({AtSpiModel.ObjectReference(uniqueName, rootPath)},)", out errorMessage);
            if (parameters == 0)
                return false;

            reply = g_dbus_connection_call_sync(
                connection,
                "org.a11y.atspi.Registry",
                "/org/a11y/atspi/accessible/root",
                "org.a11y.atspi.Socket",
                "Embed",
                parameters,
                0,
                0,
                5000,
                0,
                out error);
            if (reply == 0)
            {
                errorMessage = ReadError(error);
                return false;
            }

            return true;
        }
        finally
        {
            FreeError(ref error);
            UnrefVariant(ref reply);
            UnrefVariant(ref parameters);
        }
    }

    internal static void ReturnValue(nint invocation, string variantText)
    {
        nint value = ParseVariant(variantText, out var error);
        if (value == 0)
        {
            ReturnError(invocation, error ?? "Failed to encode the AT-SPI response.");
            return;
        }

        g_dbus_method_invocation_return_value(invocation, value);
        // g_dbus_method_invocation_return_value consumes a floating reference. g_variant_parse
        // returns a full reference, so release our copy after GDBus has taken its own reference.
        UnrefVariant(ref value);
    }

    internal static void ReturnError(nint invocation, string message) =>
        g_dbus_method_invocation_return_dbus_error(
            invocation,
            "org.a11y.atspi.Error.Failed",
            string.IsNullOrWhiteSpace(message) ? "AT-SPI request failed." : message);

    internal static nint CreatePropertyVariant(string variantText)
    {
        nint value = ParseVariant(variantText, out var error);
        if (value == 0)
            AtSpiTrace.Log($"property variant encode failed: {error}");
        return value;
    }

    internal static bool EmitSignal(
        nint connection,
        string objectPath,
        string interfaceName,
        string signalName,
        string parametersText)
    {
        nint parameters = ParseVariant(parametersText, out var parseError);
        if (parameters == 0)
        {
            AtSpiTrace.Log($"signal {interfaceName}.{signalName} encode failed: {parseError}");
            return false;
        }

        nint error = 0;
        try
        {
            bool emitted = g_dbus_connection_emit_signal(
                connection, null, objectPath, interfaceName, signalName, parameters, out error);
            if (!emitted)
                AtSpiTrace.Log($"signal {interfaceName}.{signalName} failed: {ReadError(error)}");
            return emitted;
        }
        finally
        {
            FreeError(ref error);
            UnrefVariant(ref parameters);
        }
    }

    internal static int GetInt32(nint tuple, int index)
    {
        nint child = g_variant_get_child_value(tuple, (nuint)index);
        try { return child == 0 ? 0 : g_variant_get_int32(child); }
        finally { UnrefVariant(ref child); }
    }

    internal static uint GetUInt32(nint tuple, int index)
    {
        nint child = g_variant_get_child_value(tuple, (nuint)index);
        try { return child == 0 ? 0 : g_variant_get_uint32(child); }
        finally { UnrefVariant(ref child); }
    }

    internal static int GetDirectInt32(nint value) => value == 0 ? 0 : g_variant_get_int32(value);

    internal static bool GetBoolean(nint tuple, int index)
    {
        nint child = g_variant_get_child_value(tuple, (nuint)index);
        try { return child != 0 && g_variant_get_boolean(child); }
        finally { UnrefVariant(ref child); }
    }

    internal static string GetString(nint tuple, int index)
    {
        nint child = g_variant_get_child_value(tuple, (nuint)index);
        try { return GetString(child); }
        finally { UnrefVariant(ref child); }
    }

    internal static void IterateMainContext() => _ = g_main_context_iteration(0, true);

    private static string GetString(nint variant)
    {
        if (variant == 0)
            return string.Empty;
        nint value = g_variant_get_string(variant, out _);
        return value == 0 ? string.Empty : Marshal.PtrToStringUTF8(value) ?? string.Empty;
    }

    private static nint ParseVariant(string text, out string? errorMessage)
    {
        errorMessage = null;
        nint error = 0;
        try
        {
            nint value = g_variant_parse(0, text, 0, 0, out error);
            if (value == 0)
                errorMessage = ReadError(error);
            return value;
        }
        finally
        {
            FreeError(ref error);
        }
    }

    private static bool ProbeAvailability()
    {
        try
        {
            if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad(Gio, out var handle))
                return false;
            NativeLibrary.Free(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static nint CreateVTable()
    {
        if (!OperatingSystem.IsLinux())
            return 0;

        var value = new InterfaceVTable
        {
            MethodCall = Marshal.GetFunctionPointerForDelegate(s_methodCall),
            GetProperty = Marshal.GetFunctionPointerForDelegate(s_getProperty),
            SetProperty = Marshal.GetFunctionPointerForDelegate(s_setProperty),
        };
        nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<InterfaceVTable>());
        Marshal.StructureToPtr(value, memory, false);
        return memory;
    }

    private static void OnMethodCall(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint methodName,
        nint parameters,
        nint invocation,
        nint userData)
    {
        try
        {
            AtSpiAccessibilityBridge.HandleMethodCall(
                userData,
                Marshal.PtrToStringUTF8(interfaceName) ?? string.Empty,
                Marshal.PtrToStringUTF8(methodName) ?? string.Empty,
                parameters,
                invocation);
        }
        catch (Exception ex)
        {
            AtSpiTrace.Log($"method callback failed: {ex}");
            ReturnError(invocation, ex.Message);
        }
    }

    private static nint OnGetProperty(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint propertyName,
        nint error,
        nint userData)
    {
        try
        {
            return AtSpiAccessibilityBridge.HandleGetProperty(
                userData,
                Marshal.PtrToStringUTF8(interfaceName) ?? string.Empty,
                Marshal.PtrToStringUTF8(propertyName) ?? string.Empty);
        }
        catch (Exception ex)
        {
            AtSpiTrace.Log($"get-property callback failed: {ex}");
            return 0;
        }
    }

    private static bool OnSetProperty(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint propertyName,
        nint value,
        nint error,
        nint userData)
    {
        try
        {
            return AtSpiAccessibilityBridge.HandleSetProperty(
                userData,
                Marshal.PtrToStringUTF8(interfaceName) ?? string.Empty,
                Marshal.PtrToStringUTF8(propertyName) ?? string.Empty,
                value);
        }
        catch (Exception ex)
        {
            AtSpiTrace.Log($"set-property callback failed: {ex}");
            return false;
        }
    }

    private static string ReadError(nint error)
    {
        if (error == 0)
            return "unknown GIO error";
        nint message = Marshal.ReadIntPtr(error, 8);
        return message == 0 ? "unknown GIO error" : Marshal.PtrToStringUTF8(message) ?? "unknown GIO error";
    }

    private static void FreeError(ref nint error)
    {
        if (error == 0)
            return;
        g_error_free(error);
        error = 0;
    }

    private static void UnrefVariant(ref nint variant)
    {
        if (variant == 0)
            return;
        g_variant_unref(variant);
        variant = 0;
    }

    internal static void UnrefObject(ref nint value)
    {
        if (value == 0)
            return;
        g_object_unref(value);
        value = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceVTable
    {
        internal nint MethodCall;
        internal nint GetProperty;
        internal nint SetProperty;
        internal nint Padding0;
        internal nint Padding1;
        internal nint Padding2;
        internal nint Padding3;
        internal nint Padding4;
        internal nint Padding5;
        internal nint Padding6;
        internal nint Padding7;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MethodCallCallback(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint methodName,
        nint parameters,
        nint invocation,
        nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetPropertyCallback(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint propertyName,
        nint error,
        nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool SetPropertyCallback(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint propertyName,
        nint value,
        nint error,
        nint userData);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_bus_get_sync(int busType, nint cancellable, out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_connection_new_for_address_sync(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string address,
        int flags,
        nint observer,
        nint cancellable,
        out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_connection_set_exit_on_close(
        nint connection,
        [MarshalAs(UnmanagedType.Bool)] bool exitOnClose);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_connection_get_unique_name(nint connection);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_connection_call_sync(
        nint connection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string busName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string methodName,
        nint parameters,
        nint replyType,
        int flags,
        int timeoutMilliseconds,
        nint cancellable,
        out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_node_info_new_for_xml(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string xml,
        out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_node_info_lookup_interface(
        nint nodeInfo,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint g_dbus_connection_register_object(
        nint connection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
        nint interfaceInfo,
        nint vtable,
        nint userData,
        nint userDataFreeFunction,
        out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_dbus_connection_unregister_object(nint connection, uint registrationId);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_dbus_connection_flush_sync(
        nint connection,
        nint cancellable,
        out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_method_invocation_return_value(nint invocation, nint parameters);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_method_invocation_return_dbus_error(
        nint invocation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string errorName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string errorMessage);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_dbus_connection_emit_signal(
        nint connection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? destinationBusName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signalName,
        nint parameters,
        out nint error);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_parse(
        nint type,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        nint limit,
        nint endPointer,
        out nint error);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_get_child_value(nint value, nuint index);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int g_variant_get_int32(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint g_variant_get_uint32(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_variant_get_boolean(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_get_string(nint value, out nuint length);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_variant_unref(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_main_context_iteration(
        nint context,
        [MarshalAs(UnmanagedType.Bool)] bool mayBlock);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(nint error);

    [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_object_unref(nint value);
}

internal static class AtSpiTrace
{
    private static readonly bool s_enabled = IsEnabled();

    internal static bool Enabled => s_enabled;

    internal static void Log(string message)
    {
        if (s_enabled)
            Console.Error.WriteLine($"[Jalium.AT-SPI2] {message}");
    }

    private static bool IsEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("JALIUM_ATSPI_TRACE");
        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }
}
