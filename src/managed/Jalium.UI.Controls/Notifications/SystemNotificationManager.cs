using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Notifications;

/// <summary>
/// Cross-platform system notification manager for Jalium.UI.
/// <para>
/// Backends are supplied by platform packages via
/// <see cref="BackendFactory"/>:
/// </para>
/// <list type="bullet">
///   <item><c>Jalium.UI.Desktop</c> registers the WinRT Toast backend on Windows.</item>
///   <item><c>Jalium.UI.Android</c> registers the NotificationManager-via-JNI backend on Android.</item>
///   <item>libnotify (freedesktop) remains in-tree so non-packaged Linux apps work out of the box.</item>
/// </list>
/// <para>
/// On the first access of <see cref="Current"/> the manager reflectively
/// loads the matching platform package's <c>Bootstrap.Initialize()</c> if
/// it hasn't been loaded yet — same pattern used by
/// <c>ThemeManager.EnsurePlatformIntegrationLoaded</c>.
/// </para>
/// </summary>
public sealed class SystemNotificationManager : IDisposable
{
    private const string DesktopAssemblyName = "Jalium.UI.Desktop";
    private const string DesktopBootstrapTypeName = "Jalium.UI.Desktop.DesktopBootstrap";
    private const string AndroidAssemblyName = "Jalium.UI.Android";
    private const string AndroidBootstrapTypeName = "Jalium.UI.AndroidBootstrap";

    private static SystemNotificationManager? s_current;
    private static readonly object s_lock = new();

    private readonly INotificationBackend _backend;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Optional platform-supplied factory that constructs the
    /// <see cref="INotificationBackend"/> used by this manager. Platform
    /// integration packages (<c>Jalium.UI.Desktop</c>, <c>Jalium.UI.Android</c>)
    /// set this from their bootstrap so the cross-platform Controls assembly
    /// never has to reference Win32/JNI APIs directly. If it stays null, the
    /// manager falls back to libnotify on Linux or to a no-op backend.
    /// </summary>
    public static Func<INotificationBackend>? BackendFactory { get; set; }

    /// <summary>
    /// Gets the global <see cref="SystemNotificationManager"/> singleton.
    /// </summary>
    public static SystemNotificationManager Current
    {
        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "EnsurePlatformBackendLoaded only resolves the desktop/android Bootstrap types and their public Initialize() method, both of which are preserved by the [DynamicDependency(PublicMethods, ...)] attributes declared on EnsurePlatformBackendLoaded itself. The trimmer therefore cannot remove the reflected targets, so this first-access call site is safe.")]
        get
        {
            if (s_current != null) return s_current;
            lock (s_lock)
            {
                if (s_current == null)
                {
                    EnsurePlatformBackendLoaded();
                    s_current = new SystemNotificationManager();
                }
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

    /// <summary>Raises the <see cref="Error"/> event from a backend implementation.</summary>
    internal void RaiseError(Exception ex) => Error?.Invoke(this, ex);

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
        // Platform-supplied factory wins. This is the path for Windows/Android
        // once the corresponding bootstrap has run.
        var factory = BackendFactory;
        if (factory != null)
        {
            try
            {
                var fromFactory = factory();
                if (fromFactory != null) return fromFactory;
            }
            catch
            {
                // Fall through to defaults below.
            }
        }

        // Linux backend stays in-tree for now: libnotify has no native Win32/JNI
        // entanglement, so there's no architectural win in moving it out.
        if (PlatformFactory.IsLinux)
            return new LinuxNotificationBackend();

        return new NullNotificationBackend();
    }

    /// <summary>
    /// Reflectively loads the matching platform integration package and runs
    /// its bootstrap. Best-effort: if the package isn't deployed we silently
    /// fall back to <see cref="NullNotificationBackend"/>.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods,
        DesktopBootstrapTypeName, DesktopAssemblyName)]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods,
        AndroidBootstrapTypeName, AndroidAssemblyName)]
    [RequiresUnreferencedCode("Reflectively resolves the desktop/android bootstrap type and its public Initialize() method via Assembly.GetType. The types are preserved by the DynamicDependency attributes.")]
    private static void EnsurePlatformBackendLoaded()
    {
        if (BackendFactory != null) return;

        if (PlatformFactory.IsWindows)
            TryRunBootstrap(DesktopAssemblyName, DesktopBootstrapTypeName);
        else if (PlatformFactory.IsAndroid)
            TryRunBootstrap(AndroidAssemblyName, AndroidBootstrapTypeName);
    }

    [RequiresUnreferencedCode("Reflectively resolves a bootstrap type and its public Initialize() method via Assembly.GetType.")]
    private static void TryRunBootstrap(string assemblyName, string bootstrapTypeName)
    {
        Assembly? asm = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));

        if (asm == null)
        {
            try { asm = Assembly.Load(new AssemblyName(assemblyName)); }
            catch { return; }
        }

        try
        {
            var bootstrapType = asm.GetType(bootstrapTypeName, throwOnError: false);
            var initialize = bootstrapType?.GetMethod(
                "Initialize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            initialize?.Invoke(null, null);
        }
        catch
        {
            // Best-effort.
        }
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
