using System.Runtime.InteropServices;

namespace Jalium.UI.Notifications;

/// <summary>
/// Android notification backend using JNI calls to <c>android.app.NotificationManager</c>.
/// Requires a native helper (jalium.native.platform) that exposes the JNI calls.
/// </summary>
internal sealed class AndroidNotificationBackend : INotificationBackend
{
    private string _appId = string.Empty;
    private string _appName = string.Empty;
    private bool _initialized;
    private bool _disposed;
    private readonly Dictionary<uint, NotificationHandle> _activeNotifications = new();
    private uint _nextId;
    private const string DefaultChannelId = "jalium_default";

    public bool IsSupported => AndroidNotify.IsAvailable;

    public void Initialize(string appId, string appName)
    {
        _appId = appId;
        _appName = appName;

        if (!AndroidNotify.IsAvailable) return;

        // Create default notification channel (required on Android 8+)
        AndroidNotify.jalium_notification_init(appId, appName, DefaultChannelId);
        _initialized = true;
    }

    public NotificationHandle Show(NotificationContent content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("Android notification backend not initialized.");

        int priority = content.Priority switch
        {
            NotificationPriority.Low => -1,    // PRIORITY_LOW
            NotificationPriority.High => 1,    // PRIORITY_HIGH
            _ => 0                             // PRIORITY_DEFAULT
        };

        uint id = ++_nextId;

        var iconPath = NotificationImageHelper.ResolveToPath(content.Icon) ?? string.Empty;
        var imagePath = NotificationImageHelper.ResolveToPath(content.Image) ?? string.Empty;

        int result = AndroidNotify.jalium_notification_show(
            (int)id,
            content.Title,
            content.Body ?? string.Empty,
            iconPath,
            imagePath,
            DefaultChannelId,
            priority,
            content.Silent ? 1 : 0,
            content.Tag ?? string.Empty,
            content.Group ?? string.Empty);

        if (result < 0)
            throw new InvalidOperationException($"Failed to show Android notification (error {result}).");

        var handle = new NotificationHandle
        {
            PlatformId = id,
            Tag = content.Tag,
            Group = content.Group
        };
        _activeNotifications[id] = handle;

        // Register action callbacks
        foreach (var action in content.Actions)
        {
            AndroidNotify.jalium_notification_add_action(
                (int)id, action.Id, action.Label);
        }

        return handle;
    }

    public void Hide(NotificationHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AndroidNotify.jalium_notification_cancel((int)handle.PlatformId, handle.Tag ?? string.Empty);
        _activeNotifications.Remove(handle.PlatformId);
    }

    public void ClearAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AndroidNotify.jalium_notification_cancel_all();
        _activeNotifications.Clear();
    }

    public void Remove(string tag, string? group = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var toRemove = new List<uint>();
        foreach (var kv in _activeNotifications)
        {
            if (kv.Value.Tag == tag && (group == null || kv.Value.Group == group))
            {
                AndroidNotify.jalium_notification_cancel((int)kv.Key, tag);
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
        _activeNotifications.Clear();
    }
}

/// <summary>
/// P/Invoke bindings for the native Android notification helper in jalium.native.platform.
/// These functions bridge to Java <c>NotificationManager</c> via JNI.
/// </summary>
internal static partial class AndroidNotify
{
    private const string Lib = "jalium.native.platform";

    private static readonly Lazy<bool> s_available = new(() =>
    {
        try
        {
            jalium_notification_is_available();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    });

    public static bool IsAvailable => s_available.Value;

    /// <summary>
    /// Initialize the notification subsystem: create NotificationChannel, cache JNI refs.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "jalium_notification_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void jalium_notification_init(string appId, string appName, string channelId);

    /// <summary>
    /// Show a notification. Returns 0 on success.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "jalium_notification_show", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int jalium_notification_show(
        int id, string title, string body, string iconPath, string imagePath,
        string channelId, int priority, int silent, string tag, string group);

    /// <summary>
    /// Add an action button to an existing notification (rebuilds and re-posts).
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "jalium_notification_add_action", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void jalium_notification_add_action(int id, string actionId, string label);

    /// <summary>
    /// Cancel a notification by ID and optional tag.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "jalium_notification_cancel", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void jalium_notification_cancel(int id, string tag);

    /// <summary>
    /// Cancel all notifications for this app.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "jalium_notification_cancel_all")]
    public static partial void jalium_notification_cancel_all();

    /// <summary>
    /// Probe: returns 1 if notification subsystem is available.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "jalium_notification_is_available")]
    public static partial int jalium_notification_is_available();
}
