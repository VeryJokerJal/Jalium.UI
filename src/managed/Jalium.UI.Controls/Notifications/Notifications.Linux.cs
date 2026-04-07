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
    private bool _disposed;
    private readonly Dictionary<uint, NotificationHandle> _activeNotifications = new();
    private uint _nextId;

    public bool IsSupported
    {
        get
        {
            try
            {
                // Probe for libnotify
                return LibNotify.IsAvailable;
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
        if (!LibNotify.IsAvailable) return;

        if (!LibNotify.notify_init(appName))
            throw new InvalidOperationException("Failed to initialize libnotify.");

        _initialized = true;
    }

    public NotificationHandle Show(NotificationContent content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || !LibNotify.IsAvailable)
            throw new InvalidOperationException("Linux notification backend not initialized or libnotify unavailable.");

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
                notification, action.Id, action.Label,
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

        foreach (var kv in _activeNotifications)
        {
            if (kv.Value.NativeHandle != 0)
                LibNotify.g_object_unref(kv.Value.NativeHandle);
        }
        _activeNotifications.Clear();

        if (_initialized && LibNotify.IsAvailable)
            LibNotify.notify_uninit();
    }
}

/// <summary>
/// P/Invoke bindings for libnotify (freedesktop.org desktop notification library).
/// </summary>
internal static class LibNotify
{
    private const string Lib = "libnotify.so.4";

    private static readonly Lazy<bool> s_available = new(() =>
    {
        try
        {
            return notify_is_initted() || true; // The call succeeding means the library is loaded
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    });

    public static bool IsAvailable => s_available.Value;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool notify_init([MarshalAs(UnmanagedType.LPUTF8Str)] string appName);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void notify_uninit();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool notify_is_initted();

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

    // Keep a static delegate to prevent GC
    public static readonly NotifyActionCallback ActionCallbackDelegate = OnActionCallback;

    private static void OnActionCallback(nint notification, nint action, nint userData)
    {
        // Action callbacks are delivered on the GLib main loop thread.
        // For now, the callback is a no-op placeholder; advanced routing
        // can be added via a concurrent dictionary keyed by notification pointer.
    }

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

    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_object_unref(nint obj);
}
