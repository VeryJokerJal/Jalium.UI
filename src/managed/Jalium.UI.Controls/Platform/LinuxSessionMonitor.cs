using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Bridges systemd-logind's shutdown preparation signal to the managed
/// application lifecycle. A delay inhibitor is held while the application is
/// running so SessionEnding handlers have time to persist state before logind
/// continues the shutdown transaction.
/// </summary>
internal sealed class LinuxSessionMonitor : IDisposable
{
    private const string LoginDestination = "org.freedesktop.login1";
    private const string LoginObjectPath = "/org/freedesktop/login1";
    private const string LoginManagerInterface = "org.freedesktop.login1.Manager";

    private static readonly Native.SignalCallback s_prepareForShutdownCallback =
        OnPrepareForShutdown;

    private readonly object _gate = new();
    private readonly Func<ReasonSessionEnding, bool> _sessionEnding;
    private readonly Action? _sessionEndingCancelled;
    private nint _connection;
    private uint _subscription;
    private GCHandle _selfHandle;
    private Thread? _pumpThread;
    private volatile bool _stopPump;
    private int _inhibitorFileDescriptor = -1;
    private int _preparingForShutdown;
    private int _disposed;

    private LinuxSessionMonitor(
        Func<ReasonSessionEnding, bool> sessionEnding,
        Action? sessionEndingCancelled)
    {
        _sessionEnding = sessionEnding;
        _sessionEndingCancelled = sessionEndingCancelled;
    }

    internal static LinuxSessionMonitor? TryCreate(
        Func<ReasonSessionEnding, bool> sessionEnding,
        Action? sessionEndingCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(sessionEnding);
        if (!OperatingSystem.IsLinux() || !Native.IsAvailable)
            return null;

        var monitor = new LinuxSessionMonitor(sessionEnding, sessionEndingCancelled);
        return monitor.TryStart() ? monitor : null;
    }

    internal static string BuildInhibitParameters() =>
        "('shutdown', 'Jalium.UI', " +
        "'Deliver SessionEnding and save application state', 'delay')";

    internal static bool TryParsePrepareForShutdown(string? printedVariant, out bool preparing)
    {
        string value = printedVariant?.Trim() ?? string.Empty;
        if (value.StartsWith('(') && value.EndsWith(')'))
            value = value[1..^1].Trim().TrimEnd(',').Trim();

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            preparing = true;
            return true;
        }
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            preparing = false;
            return true;
        }

        preparing = false;
        return false;
    }

    private bool TryStart()
    {
        nint error = 0;
        try
        {
            _connection = Native.OpenSystemBus(out error);
            if (_connection == 0)
                return false;

            _selfHandle = GCHandle.Alloc(this);
            _subscription = Native.SubscribePrepareForShutdown(
                _connection,
                s_prepareForShutdownCallback,
                GCHandle.ToIntPtr(_selfHandle));
            if (_subscription == 0)
                return false;

            // Acquiring the delay inhibitor is best effort. The signal remains
            // useful on minimal/container sessions whose policy denies Inhibit.
            _inhibitorFileDescriptor = Native.TryAcquireDelayInhibitor(_connection);

            _pumpThread = new Thread(Pump)
            {
                IsBackground = true,
                Name = "Jalium.LogindSessionPump",
            };
            _pumpThread.Start();
            return true;
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or EntryPointNotFoundException or
                  BadImageFormatException)
        {
            return false;
        }
        finally
        {
            Native.FreeError(ref error);
            if (_subscription == 0)
                Dispose();
        }
    }

    private void Pump()
    {
        while (!_stopPump)
        {
            try
            {
                while (Native.IterateMainContext())
                {
                }
            }
            catch (Exception ex) when (
                ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void OnPrepareForShutdown(
        nint connection,
        nint senderName,
        nint objectPath,
        nint interfaceName,
        nint signalName,
        nint parameters,
        nint userData)
    {
        try
        {
            if (userData == 0 ||
                !TryParsePrepareForShutdown(Native.PrintVariant(parameters), out bool preparing))
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is LinuxSessionMonitor monitor)
                monitor.HandlePrepareForShutdown(preparing);
        }
        catch
        {
            // Never let managed exceptions cross the unmanaged signal callback.
        }
    }

    private void HandlePrepareForShutdown(bool preparing)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        if (!preparing)
        {
            Interlocked.Exchange(ref _preparingForShutdown, 0);
            _sessionEndingCancelled?.Invoke();
            lock (_gate)
            {
                if (_inhibitorFileDescriptor < 0 && _connection != 0)
                    _inhibitorFileDescriptor = Native.TryAcquireDelayInhibitor(_connection);
            }
            return;
        }

        if (Interlocked.Exchange(ref _preparingForShutdown, 1) != 0)
            return;

        bool allowShutdown;
        try
        {
            allowShutdown = _sessionEnding(ReasonSessionEnding.Shutdown);
        }
        catch
        {
            // A failing lifecycle handler must not hold the machine shutdown
            // until InhibitDelayMaxSec expires.
            allowShutdown = true;
        }

        if (!allowShutdown)
        {
            // A delay inhibitor cannot permanently veto a Linux shutdown. Keep
            // it until logind's configured deadline so Cancel still requests
            // the strongest non-privileged delay available to desktop apps.
            return;
        }

        lock (_gate)
            CloseInhibitorLocked();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stopPump = true;
        var pump = _pumpThread;
        if (pump != null && pump != Thread.CurrentThread)
            _ = pump.Join(TimeSpan.FromSeconds(1));
        _pumpThread = null;

        lock (_gate)
        {
            CloseInhibitorLocked();
            if (_connection != 0 && _subscription != 0)
                Native.Unsubscribe(_connection, _subscription);
            _subscription = 0;
            Native.UnrefObject(ref _connection);
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private void CloseInhibitorLocked()
    {
        int descriptor = _inhibitorFileDescriptor;
        _inhibitorFileDescriptor = -1;
        if (descriptor >= 0)
            _ = Native.Close(descriptor);
    }

    private static class Native
    {
        private const string Gio = "libgio-2.0.so.0";
        private const string Glib = "libglib-2.0.so.0";
        private const string GObject = "libgobject-2.0.so.0";
        // The logical libc name is resolved by .NET on both glibc and musl;
        // hard-coding libc.so.6 would break Alpine packages.
        private const string LibC = "libc";

        private static readonly Lazy<bool> s_available = new(() =>
        {
            try
            {
                if (!NativeLibrary.TryLoad(Gio, out nint handle))
                    return false;
                NativeLibrary.Free(handle);
                return true;
            }
            catch
            {
                return false;
            }
        });

        internal static bool IsAvailable => s_available.Value;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void SignalCallback(
            nint connection,
            nint senderName,
            nint objectPath,
            nint interfaceName,
            nint signalName,
            nint parameters,
            nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DestroyNotify(nint userData);

        internal static nint OpenSystemBus(out nint error) =>
            g_bus_get_sync(1, 0, out error); // G_BUS_TYPE_SYSTEM

        internal static uint SubscribePrepareForShutdown(
            nint connection,
            SignalCallback callback,
            nint userData) =>
            g_dbus_connection_signal_subscribe(
                connection,
                LoginDestination,
                LoginManagerInterface,
                "PrepareForShutdown",
                LoginObjectPath,
                null,
                0,
                callback,
                userData,
                null!);

        internal static int TryAcquireDelayInhibitor(nint connection)
        {
            nint parameters = 0;
            nint reply = 0;
            nint returnedFileDescriptors = 0;
            nint error = 0;
            try
            {
                parameters = g_variant_parse(0, BuildInhibitParameters(), 0, 0, out error);
                if (parameters == 0)
                    return -1;
                FreeError(ref error);

                reply = g_dbus_connection_call_with_unix_fd_list_sync(
                    connection,
                    LoginDestination,
                    LoginObjectPath,
                    LoginManagerInterface,
                    "Inhibit",
                    parameters,
                    0,
                    0,
                    5000,
                    0,
                    out returnedFileDescriptors,
                    0,
                    out error);
                if (reply == 0 || returnedFileDescriptors == 0)
                    return -1;

                return g_unix_fd_list_get(returnedFileDescriptors, 0, out error);
            }
            catch (Exception ex) when (
                ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return -1;
            }
            finally
            {
                FreeError(ref error);
                UnrefVariant(ref reply);
                UnrefVariant(ref parameters);
                UnrefObject(ref returnedFileDescriptors);
            }
        }

        internal static bool IterateMainContext() =>
            g_main_context_iteration(0, false);

        internal static string PrintVariant(nint variant)
        {
            if (variant == 0)
                return string.Empty;
            nint printed = g_variant_print(variant, true);
            if (printed == 0)
                return string.Empty;
            try
            {
                return Marshal.PtrToStringUTF8(printed) ?? string.Empty;
            }
            finally
            {
                g_free(printed);
            }
        }

        internal static void Unsubscribe(nint connection, uint subscription) =>
            g_dbus_connection_signal_unsubscribe(connection, subscription);

        internal static void FreeError(ref nint error)
        {
            if (error == 0)
                return;
            g_error_free(error);
            error = 0;
        }

        internal static void UnrefVariant(ref nint variant)
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

        internal static int Close(int fileDescriptor) => close(fileDescriptor);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_bus_get_sync(int busType, nint cancellable, out nint error);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint g_dbus_connection_signal_subscribe(
            nint connection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sender,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string member,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? arg0,
            int flags,
            SignalCallback callback,
            nint userData,
            DestroyNotify userDataFreeFunction);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_dbus_connection_signal_unsubscribe(
            nint connection,
            uint subscriptionId);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_dbus_connection_call_with_unix_fd_list_sync(
            nint connection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string busName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string methodName,
            nint parameters,
            nint replyType,
            int flags,
            int timeoutMilliseconds,
            nint fileDescriptorList,
            out nint returnedFileDescriptorList,
            nint cancellable,
            out nint error);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern int g_unix_fd_list_get(
            nint fileDescriptorList,
            int index,
            out nint error);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_variant_parse(
            nint type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            nint limit,
            nint endPointer,
            out nint error);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_variant_print(
            nint variant,
            [MarshalAs(UnmanagedType.Bool)] bool typeAnnotate);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_variant_unref(nint variant);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool g_main_context_iteration(
            nint context,
            [MarshalAs(UnmanagedType.Bool)] bool mayBlock);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_error_free(nint error);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_free(nint value);

        [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_object_unref(nint value);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl)]
        private static extern int close(int fileDescriptor);
    }
}
