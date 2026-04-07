using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Notifications;

/// <summary>
/// Cross-platform system notification manager for Jalium.UI.
/// Automatically selects the appropriate backend:
/// <list type="bullet">
///   <item>Windows 10+ – WinRT Toast Notifications via COM vtable</item>
///   <item>Linux – libnotify (freedesktop)</item>
///   <item>Android – NotificationManager via JNI (jalium.native.platform)</item>
/// </list>
/// </summary>
public sealed class SystemNotificationManager : IDisposable
{
    private static SystemNotificationManager? s_current;
    private static readonly object s_lock = new();

    private readonly INotificationBackend _backend;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets the global <see cref="SystemNotificationManager"/> singleton.
    /// </summary>
    public static SystemNotificationManager Current
    {
        get
        {
            if (s_current != null) return s_current;
            lock (s_lock)
            {
                s_current ??= new SystemNotificationManager();
            }
            return s_current;
        }
    }

    private SystemNotificationManager()
    {
        _backend = CreateBackend();
    }

    /// <summary>
    /// Initializes the notification manager. Should be called once at application startup.
    /// </summary>
    /// <param name="appId">
    /// Application identifier. On Windows this is the AUMID; on Android the package name.
    /// </param>
    /// <param name="appName">Human-readable application name.</param>
    public void Initialize(string appId, string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        if (_initialized) return;
        _backend.Initialize(appId, appName);
        _initialized = true;
    }

    /// <summary>
    /// Gets whether the current platform supports system notifications.
    /// </summary>
    public bool IsSupported => _backend.IsSupported;

    /// <summary>
    /// Shows a system notification with the specified title and optional body.
    /// </summary>
    public NotificationHandle Show(string title, string? body = null)
    {
        return Show(new NotificationContent { Title = title, Body = body });
    }

    /// <summary>
    /// Shows a system notification with full content control.
    /// </summary>
    public NotificationHandle Show(NotificationContent content)
    {
        EnsureInitialized();
        return _backend.Show(content);
    }

    /// <summary>
    /// Hides a previously shown notification.
    /// </summary>
    public void Hide(NotificationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _backend.Hide(handle);
    }

    /// <summary>
    /// Removes all notifications for this application.
    /// </summary>
    public void ClearAll() => _backend.ClearAll();

    /// <summary>
    /// Removes notifications matching the specified tag and optional group.
    /// </summary>
    public void Remove(string tag, string? group = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        _backend.Remove(tag, group);
    }

    /// <summary>
    /// Occurs when the notification manager encounters an error.
    /// </summary>
    public event EventHandler<Exception>? Error;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();

        if (ReferenceEquals(s_current, this))
            s_current = null;
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException(
                "SystemNotificationManager has not been initialized. Call Initialize(appId, appName) first.");
    }

    private static INotificationBackend CreateBackend()
    {
        if (PlatformFactory.IsWindows)
            return new WindowsNotificationBackend();

        if (PlatformFactory.IsAndroid)
            return new AndroidNotificationBackend();

        if (PlatformFactory.IsLinux)
            return new LinuxNotificationBackend();

        return new NullNotificationBackend();
    }
}

/// <summary>
/// Fallback backend for unsupported platforms. All operations are no-ops.
/// </summary>
internal sealed class NullNotificationBackend : INotificationBackend
{
    public bool IsSupported => false;

    public void Initialize(string appId, string appName) { }

    public NotificationHandle Show(NotificationContent content)
    {
        return new NotificationHandle { Tag = content.Tag, Group = content.Group };
    }

    public void Hide(NotificationHandle handle) { }
    public void ClearAll() { }
    public void Remove(string tag, string? group = null) { }
    public void Dispose() { }
}
