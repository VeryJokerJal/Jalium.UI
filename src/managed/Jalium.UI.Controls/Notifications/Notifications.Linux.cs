using System.Runtime.InteropServices;

namespace Jalium.UI.Notifications;

/// <summary>
/// Linux notification backend using libnotify (the standard freedesktop notification library).
/// Falls back gracefully if libnotify is not installed.
/// </summary>
internal sealed class LinuxNotificationBackend : INotificationBackend
{
    private string _appName = string.Empty;
    private bool _initialized;
    private bool _daemonAvailable;
    private string? _initializationError;
    private volatile bool _disposed;
    private readonly Dictionary<uint, NotificationHandle> _activeNotifications = new();
    private uint _nextId;
    private Thread? _glibPump;

    /// <summary>
    /// libnotify delivers action/closed signals through the default GLib main
    /// context; without anyone iterating it the daemon shows the buttons but
    /// clicks never reach the app. A background thread polls non-blockingly
    /// (50 ms) so it can coexist with LinuxDesktopPortal, which temporarily
    /// iterates the same context during a file-dialog request.
    /// </summary>
    private void EnsureGlibPump()
    {
        if (_glibPump != null)
            return;

        var pump = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    while (LibNotify.g_main_context_iteration(0, 0) != 0)
                    {
                    }
                }
                catch (DllNotFoundException)
                {
                    return;
                }

                Thread.Sleep(50);
            }
        })
        {
            IsBackground = true,
            Name = "Jalium.LibNotifyPump",
        };
        _glibPump = pump;
        pump.Start();
    }

    public bool IsSupported
    {
        get
        {
            if (_disposed)
                return false;

            if (_initialized)
                return _daemonAvailable;

            try
            {
                return LibNotify.TryProbeServer(
                    string.IsNullOrWhiteSpace(_appName) ? "Jalium.UI" : _appName,
                    out _);
            }
            catch
            {
                return false;
            }
        }
    }

    public void Initialize(string appId, string appName)
    {
        _appName = appName;
        if (!LibNotify.IsAvailable)
        {
            _initializationError = "libnotify.so.4 is not installed or could not be loaded.";
            return;
        }

        if (!LibNotify.TryInitializeAndVerify(appName, out var failure))
        {
            _initializationError = failure;
            throw new InvalidOperationException(
                $"Failed to initialize Linux notifications: {failure}");
        }

        _initialized = true;
        _daemonAvailable = true;
        _initializationError = null;
    }

    public NotificationHandle Show(NotificationContent content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || !LibNotify.IsAvailable)
            throw new InvalidOperationException(
                _initializationError ??
                "Linux notification backend is not initialized or the notification daemon is unavailable.");

        // Resolve icon ImageSource to a file path for libnotify
        var iconPath = NotificationImageHelper.ResolveToPath(content.Icon) ?? string.Empty;

        // Create notification
        nint notification = LibNotify.notify_notification_new(
            content.Title,
            content.Body ?? string.Empty,
            iconPath);

        if (notification == 0)
            throw new InvalidOperationException("Failed to create notification.");

        // Set urgency
        int urgency = content.Priority switch
        {
            NotificationPriority.Low => 0,    // NOTIFY_URGENCY_LOW
            NotificationPriority.High => 2,   // NOTIFY_URGENCY_CRITICAL
            _ => 1                            // NOTIFY_URGENCY_NORMAL
        };
        LibNotify.notify_notification_set_urgency(notification, urgency);

        // Set timeout
        if (content.Expiration.HasValue)
        {
            int timeoutMs = (int)content.Expiration.Value.TotalMilliseconds;
            LibNotify.notify_notification_set_timeout(notification, timeoutMs);
        }

        // Set hero/inline image if provided
        var imagePath = NotificationImageHelper.ResolveToPath(content.Image);
        if (!string.IsNullOrEmpty(imagePath))
        {
            LibNotify.notify_notification_set_image_from_pixbuf(notification, imagePath);
        }

        // Add actions
        foreach (var action in content.Actions)
        {
            LibNotify.notify_notification_add_action(
                notification, action.Id ?? string.Empty, action.Label ?? string.Empty,
                LibNotify.ActionCallbackDelegate, nint.Zero, nint.Zero);
        }

        // Show
        nint errorPtr = 0;
        bool success = LibNotify.notify_notification_show(notification, ref errorPtr);

        if (!success)
        {
            string errorMsg = "Failed to show notification.";
            if (errorPtr != 0)
            {
                // GError: domain(int) + code(int) + message(char*)
                nint msgPtr = Marshal.ReadIntPtr(errorPtr, 2 * sizeof(int));
                if (msgPtr != 0)
                    errorMsg = Marshal.PtrToStringUTF8(msgPtr) ?? errorMsg;
                LibNotify.g_error_free(errorPtr);
            }
            LibNotify.g_object_unref(notification);
            throw new InvalidOperationException(errorMsg);
        }

        uint id = ++_nextId;
        var handle = new NotificationHandle
        {
            NativeHandle = notification,
            Tag = content.Tag,
            Group = content.Group,
            PlatformId = id
        };
        _activeNotifications[id] = handle;

        // Route daemon signals (action clicks, dismissal) back to this handle
        // and make sure someone is pumping the GLib context they arrive on.
        LibNotify.LiveNotifications[notification] = handle;
        LibNotify.ConnectClosedSignal(notification);
        EnsureGlibPump();
        return handle;
    }

    public void Hide(NotificationHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (handle.NativeHandle == 0 || !LibNotify.IsAvailable) return;

        nint errorPtr = 0;
        LibNotify.notify_notification_close(handle.NativeHandle, ref errorPtr);
        if (errorPtr != 0)
            LibNotify.g_error_free(errorPtr);

        LibNotify.LiveNotifications.TryRemove(handle.NativeHandle, out _);
        _activeNotifications.Remove(handle.PlatformId);
    }

    public void ClearAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var kv in _activeNotifications)
        {
            if (kv.Value.NativeHandle != 0)
            {
                nint errorPtr = 0;
                LibNotify.notify_notification_close(kv.Value.NativeHandle, ref errorPtr);
                if (errorPtr != 0) LibNotify.g_error_free(errorPtr);
                LibNotify.g_object_unref(kv.Value.NativeHandle);
            }
        }
        _activeNotifications.Clear();
    }

    public void Remove(string tag, string? group = null)
    {
        // libnotify doesn't have tag-based removal; remove by matching stored tag.
        var toRemove = new List<uint>();
        foreach (var kv in _activeNotifications)
        {
            if (kv.Value.Tag == tag && (group == null || kv.Value.Group == group))
            {
                nint errorPtr = 0;
                LibNotify.notify_notification_close(kv.Value.NativeHandle, ref errorPtr);
                if (errorPtr != 0) LibNotify.g_error_free(errorPtr);
                LibNotify.g_object_unref(kv.Value.NativeHandle);
                toRemove.Add(kv.Key);
            }
        }
        foreach (var id in toRemove)
            _activeNotifications.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _glibPump?.Join(TimeSpan.FromMilliseconds(200));
        _glibPump = null;

        foreach (var kv in _activeNotifications)
        {
            if (kv.Value.NativeHandle != 0)
            {
                LibNotify.LiveNotifications.TryRemove(kv.Value.NativeHandle, out _);
                LibNotify.g_object_unref(kv.Value.NativeHandle);
            }
        }
        _activeNotifications.Clear();

        if (_initialized && LibNotify.IsAvailable)
            LibNotify.Uninitialize();

        _initialized = false;
        _daemonAvailable = false;
    }
}

/// <summary>
/// P/Invoke bindings for libnotify (freedesktop.org desktop notification library).
/// </summary>
internal static class LibNotify
{
    private const string Lib = "libnotify.so.4";
    private static readonly object s_probeLock = new();

    private static readonly Lazy<bool> s_available = new(() =>
    {
        try
        {
            if (!NativeLibrary.TryLoad(Lib, out var handle))
                return false;

            NativeLibrary.Free(handle);
            return true;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsAvailable => s_available.Value;

    internal static bool TryProbeServer(string appName, out string failure)
    {
        failure = string.Empty;
        if (!IsAvailable)
        {
            failure = "libnotify.so.4 is not installed or could not be loaded.";
            return false;
        }

        lock (s_probeLock)
        {
            var alreadyInitialized = false;
            var initializedForProbe = false;
            try
            {
                alreadyInitialized = notify_is_initted();
                if (!alreadyInitialized)
                {
                    if (!notify_init(appName))
                    {
                        failure = "libnotify initialization failed; a D-Bus user session may be unavailable.";
                        return false;
                    }

                    initializedForProbe = true;
                }

                return TryGetServerInfo(out failure);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                failure = $"libnotify capability probing is unavailable: {ex.Message}";
                return false;
            }
            finally
            {
                if (initializedForProbe)
                    notify_uninit();
            }
        }
    }

    internal static bool TryInitializeAndVerify(string appName, out string failure)
    {
        failure = string.Empty;
        if (!IsAvailable)
        {
            failure = "libnotify.so.4 is not installed or could not be loaded.";
            return false;
        }

        lock (s_probeLock)
        {
            var initialized = false;
            try
            {
                if (!notify_init(appName))
                {
                    failure = "libnotify initialization failed; a D-Bus user session may be unavailable.";
                    return false;
                }

                initialized = true;

                if (TryGetServerInfo(out failure))
                    return true;

                notify_uninit();
                initialized = false;
                return false;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                if (initialized)
                    notify_uninit();
                failure = $"libnotify capability probing is unavailable: {ex.Message}";
                return false;
            }
        }
    }

    internal static void Uninitialize()
    {
        lock (s_probeLock)
            notify_uninit();
    }

    private static bool TryGetServerInfo(out string failure)
    {
        nint name = 0;
        nint vendor = 0;
        nint version = 0;
        nint specVersion = 0;
        try
        {
            if (notify_get_server_info(out name, out vendor, out version, out specVersion))
            {
                failure = string.Empty;
                return true;
            }

            failure = "no org.freedesktop.Notifications daemon answered on the D-Bus user session.";
            return false;
        }
        finally
        {
            if (name != 0) g_free(name);
            if (vendor != 0) g_free(vendor);
            if (version != 0) g_free(version);
            if (specVersion != 0) g_free(specVersion);
        }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool notify_init([MarshalAs(UnmanagedType.LPUTF8Str)] string appName);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void notify_uninit();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool notify_is_initted();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool notify_get_server_info(
        out nint returnName,
        out nint returnVendor,
        out nint returnVersion,
        out nint returnSpecVersion);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint notify_notification_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string summary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string body,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string icon);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool notify_notification_show(nint notification, ref nint error);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool notify_notification_close(nint notification, ref nint error);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void notify_notification_set_urgency(nint notification, int urgency);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void notify_notification_set_timeout(nint notification, int timeout);

    // notify_notification_set_image_from_pixbuf requires GdkPixbuf;
    // For simplicity, use the "image-path" hint via notify_notification_set_hint_string.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void notify_notification_set_hint_string(
        nint notification,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    public static void notify_notification_set_image_from_pixbuf(nint notification, string imagePath)
    {
        // Use the "image-path" hint which is supported by most notification daemons
        notify_notification_set_hint_string(notification, "image-path", imagePath);
    }

    // Action callback: void (*NotifyActionCallback)(NotifyNotification*, char* action, gpointer user_data)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NotifyActionCallback(nint notification, nint action, nint userData);

    // Closed signal: void (*)(NotifyNotification*, gpointer)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NotifyClosedCallback(nint notification, nint userData);

    /// <summary>
    /// Live notifications by NotifyNotification pointer. Callbacks arrive on
    /// the GLib pump thread and route to the managed handle's events.
    /// </summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, NotificationHandle>
        LiveNotifications = new();

    // Keep static delegates to prevent GC
    public static readonly NotifyActionCallback ActionCallbackDelegate = OnActionCallback;
    public static readonly NotifyClosedCallback ClosedCallbackDelegate = OnClosedCallback;

    private static void OnActionCallback(nint notification, nint action, nint userData)
    {
        if (!LiveNotifications.TryGetValue(notification, out var handle))
            return;

        var actionId = action != 0 ? Marshal.PtrToStringUTF8(action) : null;
        // "default" is the freedesktop body-click action; surface it as a
        // body activation (null ActionId) like the Windows toast backend.
        if (string.Equals(actionId, "default", StringComparison.Ordinal))
            actionId = null;

        handle.RaiseActivated(new NotificationActivatedEventArgs { ActionId = actionId });
    }

    private static void OnClosedCallback(nint notification, nint userData)
    {
        if (!LiveNotifications.TryRemove(notification, out var handle))
            return;

        // The backend keeps the sole reference (released in Hide/Dispose);
        // this callback must not unref.
        handle.RaiseDismissed(new NotificationDismissedEventArgs
        {
            Reason = NotificationDismissReason.UserCanceled,
        });
    }

    public static void ConnectClosedSignal(nint notification)
    {
        _ = g_signal_connect_data(
            notification, "closed",
            Marshal.GetFunctionPointerForDelegate(ClosedCallbackDelegate),
            nint.Zero, nint.Zero, 0);
    }

    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint g_signal_connect_data(
        nint instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        nint callback,
        nint data,
        nint destroyData,
        int connectFlags);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    public static extern int g_main_context_iteration(nint context, int mayBlock);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void notify_notification_add_action(
        nint notification,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string action,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        NotifyActionCallback callback,
        nint userData,
        nint freeFunc);

    // GLib helpers
    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_error_free(nint error);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_free(nint memory);

    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_object_unref(nint obj);
}
