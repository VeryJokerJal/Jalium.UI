using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using Jalium.UI.Navigation;
using Jalium.UI.Resources;
using NavigationEventArgs = Jalium.UI.Navigation.NavigationEventArgs;
using NavigatingCancelEventArgs = Jalium.UI.Navigation.NavigatingCancelEventArgs;

namespace Jalium.UI;

/// <summary>
/// Encapsulates a Jalium.UI application.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Resources")]
public partial class Application : Jalium.UI.Threading.DispatcherObject, IQueryAmbient
{
    private static Application? _current;
    private static Assembly? _resourceAssembly;
    private readonly WorkingSetTrimController? _workingSetTrimController;
    private readonly IDictionary _properties = new Hashtable();
    private readonly WindowCollection _windows = new(Window.SnapshotOpenWindows);
    private bool _isActive;
    private IDisposable? _linuxSessionMonitor;
    private int _cleanupStarted;
    private int _lastSystemColorScheme;
#pragma warning disable WPF0001 // Backing storage for the experimental public ThemeMode API.
    private ThemeMode _themeMode = ThemeMode.None;
#pragma warning restore WPF0001

    // Keep type initialization safe for headless tooling. Generated JALXAML module
    // initializers touch Application.StartupObjectLoader before Main runs, so starting a
    // Linux display connection here would make commands such as --diagnostics-only fail
    // before they can inspect the native payload. Windows DPI awareness is process-wide
    // and does not require a display connection, so it remains safe to establish here.
    static Application()
    {
        UIElement.AutomaticTransitionsEnabledProvider = static () =>
            SystemParameters.ClientAreaAnimation && SystemParameters.UIEffects;

        SystemParameters.StaticPropertyChanged += static (_, _) =>
            Current?.ApplyWindowsSystemThemePreference();

        if (PlatformFactory.IsWindows)
        {
            _ = TryEnablePerMonitorDpiAwareness();
        }
    }
    
    /// <summary>
    /// Framework-internal startup object loader registered by Jalium.UI.Xaml.
    /// </summary>
    internal static Func<Application, Uri, object?>? StartupObjectLoader { get; set; }

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application? Current => _current;

    /// <summary>
    /// Gets the application-scoped property bag.
    /// </summary>
    public IDictionary Properties => _properties;

    /// <summary>
    /// Gets or sets the assembly used to resolve application resources.
    /// </summary>
    public static Assembly ResourceAssembly
    {
        get => _resourceAssembly ??= Assembly.GetEntryAssembly() ?? typeof(Application).Assembly;
        set => _resourceAssembly = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the theme mode requested by the application.
    /// </summary>
    [Experimental("WPF0001")]
    public ThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (_themeMode == value)
            {
                return;
            }

            _themeMode = value;

            if (value == Jalium.UI.ThemeMode.Light)
            {
                ThemeManager.ApplyTheme(ThemeVariant.Light);
            }
            else if (value == Jalium.UI.ThemeMode.Dark)
            {
                ThemeManager.ApplyTheme(ThemeVariant.Dark);
            }
            else if (value == Jalium.UI.ThemeMode.System)
            {
                if (OperatingSystem.IsWindows())
                {
                    ApplyWindowsSystemThemePreference();
                }
                else
                {
                    ApplyCachedSystemThemePreference();
                }
            }
            else if (value == Jalium.UI.ThemeMode.None && !OperatingSystem.IsWindows())
            {
                ApplyCachedSystemThemePreference();
            }
        }
    }

    private void ApplyCachedSystemThemePreference()
    {
        var scheme = Volatile.Read(ref _lastSystemColorScheme);
        if (scheme is 1 or 2)
        {
            ThemeManager.ApplyTheme(scheme == 1 ? ThemeVariant.Dark : ThemeVariant.Light);
        }
    }

    private void ApplyWindowsSystemThemePreference()
    {
        if (!OperatingSystem.IsWindows() || _themeMode != ThemeMode.System)
        {
            return;
        }

        var scheme = ReadWindowsSystemColorScheme();
        if (scheme is not (1 or 2))
        {
            return;
        }

        Volatile.Write(ref _lastSystemColorScheme, scheme);
        ThemeManager.ApplyTheme(scheme == 1 ? ThemeVariant.Dark : ThemeVariant.Light);
    }

    private static int ReadWindowsSystemColorScheme()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme
                ? appsUseLightTheme == 0 ? 1 : 2
                : 0;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return 0;
        }
    }

    /// <summary>
    /// Gets a live collection of the application's open windows.
    /// </summary>
    public WindowCollection Windows => _windows;

    /// <summary>
    /// Occurs when the current application instance changes.
    /// </summary>
    internal static event EventHandler? CurrentChanged;

    private Window? _mainWindow;

    /// <summary>
    /// Gets or sets the main window.
    /// </summary>
    public Window? MainWindow
    {
        get => _mainWindow;
        set
        {
            if (ReferenceEquals(_mainWindow, value))
                return;

            _mainWindow = value;
            if (value != null)
            {
                StartupDiagnostics.Mark("MainWindowAssigned", blocksUiThread: true);
            }
            MainWindowChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets or sets the startup URI used to load the initial window or visual root.
    /// Supports relative paths and pack-style paths (e.g. /Assembly;component/Path/File.xaml).
    /// </summary>
    public Uri? StartupUri { get; set; }

    /// <summary>
    /// Gets or sets the shutdown mode of the application.
    /// </summary>
    public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnLastWindowClose;

    private ResourceDictionary? _resources;

    /// <summary>
    /// Gets or sets the application-level resources.
    /// </summary>
    [Ambient]
    public ResourceDictionary Resources
    {
        get
        {
            if (_resources == null)
            {
                _resources = new ResourceDictionary();
                _resources.Changed += OnApplicationResourcesDictionaryChanged;
                _resources.ChangedWithKeys += OnApplicationResourcesChangedWithKeys;
                Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterOwner(
                    _resources,
                    this,
                    Diagnostics.ResourceDictionaryOwnerKind.Application);
            }

            return _resources;
        }
        set
        {
            if (_resources == value)
                return;

            if (_resources != null)
            {
                _resources.Changed -= OnApplicationResourcesDictionaryChanged;
                _resources.ChangedWithKeys -= OnApplicationResourcesChangedWithKeys;
                Diagnostics.ResourceDictionaryDiagnosticsStore.UnregisterOwner(_resources, this);
            }

            _resources = value;
            if (_resources != null)
            {
                _resources.Changed += OnApplicationResourcesDictionaryChanged;
                _resources.ChangedWithKeys += OnApplicationResourcesChangedWithKeys;
                Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterOwner(
                    _resources,
                    this,
                    Diagnostics.ResourceDictionaryOwnerKind.Application);
            }
            OnApplicationResourcesChanged();
        }
    }

    bool IQueryAmbient.IsAmbientPropertyAvailable(string propertyName)
        => propertyName == nameof(Resources) && _resources != null;

    /// <summary>
    /// Searches application, theme, and system resources for the specified key.
    /// </summary>
    /// <exception cref="ResourceReferenceKeyNotFoundException">
    /// No application, theme, or system resource has the supplied key.
    /// </exception>
    public object FindResource(object resourceKey)
    {
        var resource = TryFindResource(resourceKey);
        if (resource == null)
        {
            throw new ResourceReferenceKeyNotFoundException(
                $"Resource '{resourceKey}' was not found.",
                resourceKey);
        }

        return resource;
    }

    /// <summary>
    /// Searches application, theme, and system resources for the specified key.
    /// </summary>
    /// <returns>The resource, or <see langword="null"/> when no matching resource exists.</returns>
    public object? TryFindResource(object resourceKey)
    {
        ArgumentNullException.ThrowIfNull(resourceKey);
        return TryFindApplicationOrSystemResource(resourceKey);
    }

    /// <summary>
    /// Occurs when application-level resources change.
    /// </summary>
    public event EventHandler? ResourcesChanged;

    /// <summary>
    /// Occurs when the main window reference changes.
    /// </summary>
    internal event EventHandler? MainWindowChanged;

    /// <summary>
    /// Occurs when the application is starting, before the startup window is shown.
    /// </summary>
    public event StartupEventHandler? Startup;

    /// <summary>
    /// Occurs when the application becomes active.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Occurs when the application ceases to be active.
    /// </summary>
    public event EventHandler? Deactivated;

    /// <summary>
    /// Forwards unhandled exceptions raised by this application's dispatcher.
    /// </summary>
    public event Jalium.UI.Threading.DispatcherUnhandledExceptionEventHandler DispatcherUnhandledException
    {
        add => Dispatcher.Invoke(() => Dispatcher.UnhandledException += value);
        remove => Dispatcher.Invoke(() => Dispatcher.UnhandledException -= value);
    }

    /// <summary>
    /// Occurs after the message loop exits and before the application cleans up.
    /// Handlers may observe / override <see cref="ExitEventArgs.ApplicationExitCode"/>.
    /// </summary>
    public event ExitEventHandler? Exit;

    /// <summary>
    /// Occurs when the operating-system session is ending (logoff or shutdown).
    /// The handler may request cancellation by setting
    /// <see cref="SessionEndingCancelEventArgs.Cancel"/>. Linux desktop sessions
    /// honor this as a logind delay request up to the configured inhibitor timeout.
    /// </summary>
    public event SessionEndingCancelEventHandler? SessionEnding;

    /// <summary>
    /// Raises the <see cref="Startup"/> event. Override to perform application-level
    /// initialization (service registration, logging, etc.) before the startup window
    /// is shown.
    /// </summary>
    protected virtual void OnStartup(StartupEventArgs e)
    {
        Startup?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="Activated"/> event.
    /// </summary>
    protected virtual void OnActivated(EventArgs e)
    {
        VerifyAccess();
        Activated?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="Deactivated"/> event.
    /// </summary>
    protected virtual void OnDeactivated(EventArgs e)
    {
        VerifyAccess();
        Deactivated?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="Exit"/> event. Override to perform application-level
    /// cleanup after the message loop terminates. The exit code can be changed via
    /// <see cref="ExitEventArgs.ApplicationExitCode"/>.
    /// </summary>
    protected virtual void OnExit(ExitEventArgs e)
    {
        Exit?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SessionEnding"/> event.
    /// </summary>
    protected virtual void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        SessionEnding?.Invoke(this, e);
    }

    /// <summary>
    /// Internal entry point called by <see cref="Window"/> when it receives
    /// WM_QUERYENDSESSION, so application-level handlers get a chance to cancel.
    /// </summary>
    internal bool RaiseSessionEnding(SessionEndingCancelEventArgs e)
    {
        OnSessionEnding(e);
        return !e.Cancel;
    }

    /// <summary>
    /// Updates application activation state from a platform-wide activation notification.
    /// Multiple top-level windows receive the same native notification, so transitions are
    /// deliberately de-duplicated here.
    /// </summary>
    internal void SetPlatformActivationState(bool isActive)
    {
        VerifyAccess();

        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        if (isActive)
        {
            OnActivated(EventArgs.Empty);
        }
        else
        {
            OnDeactivated(EventArgs.Empty);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    public Application()
    {
        if (_current != null)
        {
            throw new InvalidOperationException("Only one Application instance can be created.");
        }

        using var construction = StartupDiagnostics.Begin(
            "Application.Construct",
            blocksUiThread: true);

        if (!PlatformFactory.IsWindows)
        {
            // DispatcherObject's base constructor has already captured the thread
            // dispatcher. Its first native-wake attempt may have preceded platform
            // initialization, so initialize the display backend and repair the wake
            // handle before application services can enqueue work.
            PlatformFactory.InitializePlatform();
            Dispatcher.EnsureNativeWake();
        }

        // Touch native GPU state only when an actual application is being created.
        // This still overlaps device creation with theme and MainWindow construction,
        // while build tools and managed-only hosts can load the framework safely.
        using (StartupDiagnostics.Begin("Application.ScheduleGpuPrewarm", blocksUiThread: true))
        {
            GpuPrewarmInitializer.Prewarm();
        }

        _current = this;
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        // Initialize the dispatcher for the main UI thread
        Dispatcher.SetAsMainThread();
        StartupDiagnostics.NotifyUiThreadRegistered();
        // Install a SynchronizationContext so async/await resumes on the UI dispatcher thread.
        // WebView2 initialization relies on UI-thread affinity across awaits.
        SynchronizationContext.SetSynchronizationContext(
            new Jalium.UI.Threading.DispatcherSynchronizationContext(
                Dispatcher.MainDispatcher ?? Dispatcher.CurrentDispatcher));

        // Initialize keyboard/focus system
        using (StartupDiagnostics.Begin("Application.KeyboardInitialize", blocksUiThread: true))
        {
            Keyboard.Initialize();
        }

        // Subscribe to Android lifecycle events
        if (PlatformFactory.IsAndroid)
        {
            AndroidActivityBridge.Paused += OnAndroidPause;
            AndroidActivityBridge.Resumed += OnAndroidResume;
            AndroidActivityBridge.Destroying += OnAndroidDestroy;
            AndroidActivityBridge.LowMemory += OnAndroidLowMemory;
        }

        // Register application resource lookup callback
        ResourceLookup.ApplicationResourceLookup = LookupApplicationResource;
        ResourceLookup.ApplicationResourceLookupWithSource = LookupApplicationResourceWithSource;
        ResourceLookup.AncestorRedirectLookup = ResolveResourceAncestorRedirect;

        // Initialize default theme (loads default styles for all controls)
        using (StartupDiagnostics.Begin("Application.ThemeInitialize", blocksUiThread: true))
        {
            ThemeManager.Initialize(this);
        }

        // Initialize validation adorner integration
        using (StartupDiagnostics.Begin("Application.ControlServicesInitialize", blocksUiThread: true))
        {
            ValidationAdornerIntegration.Initialize();

            // Force ToolTip static constructor to register show/hide delegates early
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ToolTip).TypeHandle);

            // ContextMenuService opens context menus from class handlers registered
            // in its static constructor. Nothing in a code-only app ever touches the
            // type (FrameworkElement.ContextMenu is the property's real owner), so
            // without this right-click/press-and-hold menus silently never open.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ContextMenuService).TypeHandle);
        }

        // Follow the desktop light/dark preference on Linux
        // (org.freedesktop.portal.Settings). Off the UI thread: the portal read
        // is a D-Bus round-trip with a 3s ceiling and must not stall startup.
        if (PlatformFactory.IsLinux)
        {
            var dispatcher = Dispatcher;
            void ApplySystemScheme(uint scheme)
            {
                Volatile.Write(ref _lastSystemColorScheme, (int)scheme);

                // Explicit Light/Dark modes pin the theme; None and System follow the desktop.
                if ((_themeMode != ThemeMode.None && _themeMode != ThemeMode.System) ||
                    scheme is not (1 or 2))
                    return;

                var variant = scheme == 1 ? ThemeVariant.Dark : ThemeVariant.Light;
                dispatcher?.BeginInvoke(() => ThemeManager.ApplyTheme(variant));
            }

            _ = Task.Run(() =>
            {
                try
                {
                    if (LinuxDesktopPortal.TryReadColorScheme() is { } scheme)
                        ApplySystemScheme(scheme);
                    _ = LinuxDesktopPortal.TrySubscribeColorSchemeChanged(ApplySystemScheme);
                }
                catch
                {
                    // Theme following is best-effort; never take down startup.
                }
            });

            _ = Task.Run(() =>
            {
                IDisposable? monitor = null;
                try
                {
                    monitor = LinuxSessionLifecycleMonitor.TryCreate(
                        HandleLinuxSessionEnding,
                        HandleLinuxSessionDie);
                    if (monitor == null ||
                        Volatile.Read(ref _cleanupStarted) != 0 ||
                        !ReferenceEquals(_current, this))
                    {
                        monitor?.Dispose();
                        return;
                    }

                    if (Interlocked.CompareExchange(
                            ref _linuxSessionMonitor, monitor, null) != null)
                    {
                        monitor.Dispose();
                        return;
                    }

                    // Close the narrow race where Cleanup started immediately
                    // after the pre-publication check.
                    if (Volatile.Read(ref _cleanupStarted) != 0)
                        Interlocked.Exchange(ref _linuxSessionMonitor, null)?.Dispose();
                }
                catch
                {
                    monitor?.Dispose();
                    // logind is optional in containers/minimal Linux sessions.
                }
            });
        }

        // Optional ultra-low visible memory mode (off by default).
        using (StartupDiagnostics.Begin("Application.WorkingSetPolicyInitialize", blocksUiThread: true))
        {
            _workingSetTrimController = WorkingSetTrimController.TryCreateFromEnvironment();
        }

        // UIA accessibility bridge is registered lazily on the first
        // WM_GETOBJECT (UiaRootObjectId) — see Window.WndProc. Constructing
        // UiaAutomationEventSink here would JIT UiaNativeMethods, whose first
        // [DllImport("uiautomationcore.dll")] reference triggers
        // UIAutomationCore.dll's first-touch load (and, transitively, oleacc/
        // TextInputFramework). Most processes never have a UIA client connect,
        // so paying that cost during Application ctor is pure startup waste.
        // RaiseAutomationEvent / RaisePropertyChangedEvent / OnFocusChanged
        // already null-skip when EventSink is null, so deferring registration
        // to the moment a real UIA client requests the root provider is safe.

        // Auto-call InitializeComponent() on derived classes to load their JALXAML resources.
        // This mirrors WPF behavior where Application subclass resources are loaded automatically.
        // The source generator emits InitializeComponent() as a private method, so use reflection.
        using (StartupDiagnostics.Begin("Application.InitializeComponent", blocksUiThread: true))
        {
            CallInitializeComponent();
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode",
        Justification = "The reflected 'InitializeComponent' is the private method emitted by the JALXAML source generator onto the Application subclass codebehind. The generator pins it via [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods, typeof(<className>))] on that class's [ModuleInitializer] (JalxamlSourceGenerator emits this so the trimmer keeps all instance method metadata of the codebehind). Since the registration ModuleInitializer is always reachable, the target method survives trimming. The runtime Type here comes from object.GetType() and cannot carry the matching DynamicallyAccessedMembers annotation, so the preservation is guaranteed by that named DynamicDependency rather than by flow analysis at this site.")]
    private void CallInitializeComponent()
    {
        var initMethod = GetType().GetMethod("InitializeComponent",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            Type.EmptyTypes);
        if (initMethod != null && initMethod.DeclaringType != typeof(Application))
        {
            initMethod.Invoke(this, null);
        }
    }

    private static object? LookupApplicationResource(object resourceKey)
    {
        return _current?.TryFindApplicationOrSystemResource(resourceKey);
    }

    private static (object? Value, ResourceDictionary? Dictionary)
        LookupApplicationResourceWithSource(object resourceKey)
    {
        if (_current is null)
        {
            return (null, null);
        }

        return _current.TryFindApplicationOrSystemResourceWithSource(resourceKey);
    }

    private (object? Value, ResourceDictionary? Dictionary)
        TryFindApplicationOrSystemResourceWithSource(object resourceKey)
    {
        if (_resources != null &&
            _resources.TryGetValue(
                resourceKey,
                out var value,
                out var dictionary) &&
            value != null &&
            !ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return (value, dictionary);
        }

        if (SystemColors.TryGetResource(resourceKey, out var systemResource))
        {
            return (systemResource, null);
        }

        return SystemFonts.TryGetResource(resourceKey, out systemResource)
            ? (systemResource, null)
            : (null, null);
    }

    private object? TryFindApplicationOrSystemResource(object resourceKey)
    {
        // ThemeManager installs its dictionaries into Application.Resources.MergedDictionaries,
        // so ResourceDictionary.TryGetValue performs the application -> active theme portion of
        // the WPF lookup chain in the correct override order.
        if (_resources != null &&
            _resources.TryGetValue(resourceKey, out var value) &&
            value != null &&
            !ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return value;
        }

        if (SystemColors.TryGetResource(resourceKey, out var systemResource))
        {
            return systemResource;
        }

        return SystemFonts.TryGetResource(resourceKey, out systemResource)
            ? systemResource
            : null;
    }

    private static FrameworkElement? ResolveResourceAncestorRedirect(FrameworkElement element)
    {
        // External Popup windows are detached from the owning window's visual tree.
        // Bridge lookup to PlacementTarget first so window/page-level custom resources
        // (for example OnePopup*) still resolve in popup content.
        if (element is PopupRoot popupRoot && popupRoot.VisualParent is PopupWindow)
        {
            if (popupRoot.OwnerPopup.PlacementTarget is FrameworkElement placementTarget)
            {
                return placementTarget;
            }

            return popupRoot.OwnerPopup;
        }

        // Popup itself is often not in the visual tree (e.g., ContextMenu/Flyout internals).
        // Continue lookup from PlacementTarget so implicit styles/resources follow host context.
        if (element is Popup popup && popup.VisualParent == null && popup.PlacementTarget is FrameworkElement target)
        {
            return target;
        }

        return null;
    }

    private void OnApplicationResourcesDictionaryChanged(object? sender, EventArgs e)
    {
        // Legacy handler — the full refresh is driven by ChangedWithKeys below.
    }

    private void OnApplicationResourcesChangedWithKeys(object? sender, ResourceDictionary.ResourcesChangedEventArgs e)
    {
        if (e.ChangedKeys != null)
        {
            // Targeted refresh — only update DynamicResource bindings whose key changed
            DynamicResourceBindingOperations.RefreshForKeys(e.ChangedKeys);
        }
        else
        {
            // Full refresh (merged dictionary replacement, theme switch, etc.)
            OnApplicationResourcesChanged();
        }
    }

    private void OnApplicationResourcesChanged()
    {
        ResourceLookup.InvalidateResourceCache();
        ResourcesChanged?.Invoke(this, EventArgs.Empty);

        // A full refresh (theme swap replacing merged dictionaries) invalidates implicit
        // styles everywhere, so the re-evaluation walk must start from every live visual
        // root — not just MainWindow. Live secondary windows and external popup surfaces
        // (ContextMenu/ToolTip/Flyout hosts) are detached roots that would otherwise keep
        // the retired theme's templates. Closed-but-alive popups hang off their host
        // window's logical tree and are reached by the per-window walk below. A window
        // that is not yet shown is unreachable here (it has no HWND and is absent from
        // the live-window registry); it self-heals against the current theme version in
        // Window.Show instead.
        //
        // Each root is refreshed in isolation: this runs synchronously inside the
        // dictionary mutation that raised the change (ThemeManager.ReplaceManagedDictionary),
        // so a handler or implicit-style Apply that throws in one window/popup must not
        // propagate out — that would abort the switch for the remaining roots and leave
        // ThemeManager's dictionary fields desynced from Application.Resources.
        var mainWindow = MainWindow;
        if (mainWindow is not FrameworkElement root)
            return;

        // The native-window registries are process-wide. A newly constructed
        // Application with no MainWindow must not broadcast into windows left by a
        // previous/test Application instance; doing so can also materialize this
        // application's lazy Resources dictionary while it is being set to null.
        SafeNotifyResourcesChangedFromRoot(root);
        foreach (var window in Window.SnapshotOpenWindows())
        {
            if (!ReferenceEquals(window, mainWindow))
            {
                SafeNotifyResourcesChangedFromRoot(window);
            }
        }

        foreach (var popupWindow in PopupWindow.SnapshotOpenPopupWindows())
        {
            SafeNotifyResourcesChangedFromRoot(popupWindow);
        }

        static void SafeNotifyResourcesChangedFromRoot(FrameworkElement root)
        {
            try
            {
                root.NotifyResourcesChangedFromRoot();
            }
            catch (Exception ex)
            {
                // Isolate a faulty subtree and keep broadcasting to the rest. Swallowing
                // here (rather than propagating) is what preserves the ThemeManager
                // field/collection invariant described above.
                System.Diagnostics.Debug.WriteLine(
                    $"Jalium.UI: resources-changed broadcast to a window root was isolated after an exception: {ex}");
            }
        }
    }

    /// <summary>
    /// Publishes a theme-palette change without reapplying implicit styles. Theme dictionaries
    /// keep the same Style and ControlTemplate instances across variants, but controls still
    /// need their ResourcesChanged hooks (for manually cached brushes/state) and retained
    /// drawing commands refreshed.
    /// </summary>
    internal void NotifyThemeResourcesChanged()
    {
        ResourceLookup.InvalidateResourceCache();
        ResourcesChanged?.Invoke(this, EventArgs.Empty);

        var mainWindow = MainWindow;
        if (mainWindow is FrameworkElement root)
        {
            SafeNotifyThemeResourcesChangedFromRoot(root);
        }

        foreach (var window in Window.SnapshotOpenWindows())
        {
            if (!ReferenceEquals(window, mainWindow))
            {
                SafeNotifyThemeResourcesChangedFromRoot(window);
            }
        }

        foreach (var popupWindow in PopupWindow.SnapshotOpenPopupWindows())
        {
            SafeNotifyThemeResourcesChangedFromRoot(popupWindow);
        }

        static void SafeNotifyThemeResourcesChangedFromRoot(FrameworkElement root)
        {
            try
            {
                root.NotifyThemeResourcesChangedFromRoot();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Jalium.UI: theme-resources broadcast to a window root was isolated after an exception: {ex}");
            }
        }
    }

    /// <summary>
    /// Starts the application message loop using the command-line arguments
    /// supplied by the operating system.
    /// </summary>
    public int Run() => Run(Environment.GetCommandLineArgs().Skip(1).ToArray());

    /// <summary>
    /// Starts the application message loop with an explicit set of startup arguments.
    /// Use this overload from tests or custom hosts that do not want to expose
    /// <see cref="Environment.GetCommandLineArgs"/>.
    /// </summary>
    public int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using (StartupDiagnostics.Begin("Application.StartupHandlers", blocksUiThread: true))
        {
            OnStartup(new StartupEventArgs(args));
        }

        Window? startupWindow;
        using (StartupDiagnostics.Begin("Application.ResolveStartupWindow", blocksUiThread: true))
        {
            startupWindow = ResolveStartupWindow();
        }
        if (startupWindow != null)
        {
            if (startupWindow.Handle == nint.Zero)
            {
                using (StartupDiagnostics.Begin("Application.ShowMainWindow", blocksUiThread: true))
                {
                    startupWindow.Show();
                }

                StartupDiagnostics.Mark("MainWindowShowReturned", blocksUiThread: true);
            }

            if (StartupDiagnostics.IsEnabled)
            {
                _ = startupWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () => StartupDiagnostics.Mark(
                        "MainWindowFirstInputReady",
                        blocksUiThread: false));
            }
        }

        var exitCode = 0;
        try
        {
            if (PlatformFactory.IsWindows)
            {
                // Input-first Win32 pump owned by the dispatcher (see
                // Dispatcher.RunMainMessageLoop). Unlike the classic GetMessage loop
                // this replaces, a posted dispatcher wake (WM_DISPATCHER_INVOKE) no
                // longer outranks hardware input, so continuous rendering/animation
                // cannot starve mouse/keyboard input. Returns the WM_QUIT exit code.
                var dispatcher = Dispatcher.MainDispatcher ?? Dispatcher.CurrentDispatcher;
                exitCode = dispatcher.RunMainMessageLoop();
            }
            else
            {
                // Cross-platform message loop (Linux X11 / Android)
                exitCode = PlatformFactory.RunMessageLoop();
            }
        }
        finally
        {
            // Fire Exit event before cleanup so handlers can still access application
            // state. Handlers may override the final exit code via ExitEventArgs.
            var exitArgs = new ExitEventArgs(exitCode);
            OnExit(exitArgs);
            exitCode = exitArgs.ApplicationExitCode;
            Cleanup();
        }

        return exitCode;
    }

    #region Android Lifecycle

    private static void OnAndroidPause()
    {
        CompositionTarget.SuspendRendering();
    }

    private static void OnAndroidResume()
    {
        CompositionTarget.ResumeRendering();

        // Request a full re-render of all windows
        if (_current?.MainWindow is Window mainWindow)
        {
            mainWindow.RequestFullInvalidation();
            mainWindow.InvalidateWindow();
        }
    }

    private static void OnAndroidDestroy()
    {
        _current?.Shutdown();
    }

    private static void OnAndroidLowMemory()
    {
        // Trim bitmap and render caches to free memory
        TextMeasurement.ClearCache();
    }

    #endregion

    private void Cleanup()
    {
        Volatile.Write(ref _cleanupStarted, 1);
        Interlocked.Exchange(ref _linuxSessionMonitor, null)?.Dispose();

        // Unsubscribe Android lifecycle events
        if (PlatformFactory.IsAndroid)
        {
            AndroidActivityBridge.Paused -= OnAndroidPause;
            AndroidActivityBridge.Resumed -= OnAndroidResume;
            AndroidActivityBridge.Destroying -= OnAndroidDestroy;
            AndroidActivityBridge.LowMemory -= OnAndroidLowMemory;
        }

        _workingSetTrimController?.Dispose();

        // Stop all active animations
        Storyboard.StopAll();

        // Stop all tooltip timers
        ToolTipService.Cleanup();

        // Clear static text format cache before RenderContext is destroyed,
        // otherwise finalizers may try to destroy native resources after the
        // DWrite factory is already gone, causing StackOverflowException.
        TextMeasurement.ClearCache();

        // Dispose the render context (destroys native DWrite factory, D3D12 device, etc.)
        RenderContext.Current?.Dispose();

        // Release host-provided references (Services/Configuration/Environment) so nothing
        // keeps them alive after Run() returns. JaliumApp.Dispose() disposes the underlying IHost.
        DetachHost();

        // Clear application reference
        _current = null;
        CurrentChanged?.Invoke(null, EventArgs.Empty);
    }

    private bool HandleLinuxSessionEnding(ReasonSessionEnding reason)
    {
        bool allowShutdown = true;
        void RaiseEvents()
        {
            var args = new SessionEndingCancelEventArgs(reason);
            foreach (Window window in Window.SnapshotOpenWindows())
                window.RaisePlatformSessionEnding(args);
            RaiseSessionEnding(args);
            allowShutdown = !args.Cancel;
        }

        var dispatcher = Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.Invoke(RaiseEvents);
        else
            RaiseEvents();
        return allowShutdown;
    }

    private void HandleLinuxSessionDie()
    {
        void ShutdownFromSessionManager() => Shutdown();
        var dispatcher = Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            _ = dispatcher.BeginInvoke(ShutdownFromSessionManager);
        else
            ShutdownFromSessionManager();
    }

    /// <summary>
    /// Starts the application with the specified main window.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    public int Run(Window mainWindow)
    {
        MainWindow = mainWindow;
        return Run();
    }

    /// <summary>
    /// Starts the application with the specified main window and startup arguments.
    /// </summary>
    public int Run(Window mainWindow, string[] args)
    {
        MainWindow = mainWindow;
        return Run(args);
    }

    /// <summary>
    /// Resolves <see cref="MainWindow"/> from <see cref="StartupUri"/> when needed.
    /// </summary>
    /// <remarks>
    /// Internal for tests so startup behavior can be validated without entering the message loop.
    /// </remarks>
    internal Window? ResolveStartupWindow()
    {
        if (MainWindow != null)
            return MainWindow;

        if (StartupUri == null)
            return null;

        if (StartupObjectLoader == null)
        {
            throw new InvalidOperationException(
                $"StartupUri '{StartupUri.OriginalString}' cannot be resolved because no startup loader is registered.");
        }

        var startupObject = StartupObjectLoader(this, StartupUri);
        if (startupObject == null)
        {
            throw new InvalidOperationException(
                $"StartupUri '{StartupUri.OriginalString}' resolved to null.");
        }

        if (startupObject is Window startupWindow)
        {
            MainWindow = startupWindow;
            return MainWindow;
        }

        if (startupObject is FrameworkElement startupRoot)
        {
            MainWindow = new Window
            {
                Content = startupRoot
            };
            return MainWindow;
        }

        throw new InvalidOperationException(
            $"StartupUri '{StartupUri.OriginalString}' resolved to unsupported startup object type '{startupObject.GetType().FullName}'.");
    }

    /// <summary>
    /// Shuts down the application.
    /// </summary>
    public void Shutdown()
    {
        Shutdown(0);
    }

    /// <summary>
    /// Shuts down the application with the specified exit code.
    /// </summary>
    /// <param name="exitCode">The process exit code returned by <see cref="Run()"/>.</param>
    public void Shutdown(int exitCode)
    {
        if (PlatformFactory.IsWindows)
            PostQuitMessage(exitCode);
        else
        {
            if (PlatformFactory.IsAndroid)
            {
                // Close the Android Surface/Window transaction while this UI
                // dispatcher is still alive. Once PlatformQuit returns the
                // native loop may stop pumping immediately, so lifecycle calls
                // must already see Stopping and must not Invoke this dispatcher.
                if (!AndroidActivityBridge.MarkUiThreadStopping())
                {
                    Console.Error.WriteLine(
                        "[Application] Android shutdown was deferred because Surface/Window teardown did not complete.");
                    return;
                }
            }
            PlatformFactory.QuitMessageLoop(exitCode);
        }
    }

    /// <summary>
    /// Called by Window when it is closed. Determines whether the application should shut down
    /// based on the current <see cref="ShutdownMode"/>.
    /// </summary>
    internal void OnWindowClosed(Window window, int remainingWindowCount)
    {
        var shouldShutdown = ShutdownMode switch
        {
            ShutdownMode.OnLastWindowClose => remainingWindowCount == 0,
            ShutdownMode.OnMainWindowClose => window == MainWindow,
            ShutdownMode.OnExplicitShutdown => false,
            _ => false
        };

        if (shouldShutdown)
        {
            Shutdown();
        }
    }

    internal static bool TryEnablePerMonitorDpiAwareness(
        Func<nint, bool>? setProcessDpiAwarenessContext = null,
        Func<int>? getLastError = null,
        Func<ProcessDpiAwareness, int>? setProcessDpiAwareness = null,
        Func<bool>? setProcessDpiAware = null)
    {
        setProcessDpiAwarenessContext ??= SetProcessDpiAwarenessContext;
        getLastError ??= Marshal.GetLastWin32Error;
        setProcessDpiAwareness ??= SetProcessDpiAwareness;
        setProcessDpiAware ??= SetProcessDPIAware;

        if (setProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        {
            return true;
        }

        var error = getLastError();
        if (error == ERROR_ACCESS_DENIED)
        {
            // The host process or manifest already configured DPI awareness.
            return true;
        }

        var shcoreResult = setProcessDpiAwareness(ProcessDpiAwareness.ProcessPerMonitorDpiAware);
        if (shcoreResult is S_OK or E_ACCESSDENIED)
        {
            return true;
        }

        return setProcessDpiAware();
    }

    #region Win32 Interop

    internal enum ProcessDpiAwareness
    {
        ProcessDpiUnaware = 0,
        ProcessSystemDpiAware = 1,
        ProcessPerMonitorDpiAware = 2
    }

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint dpiContext);

    [LibraryImport("shcore.dll", SetLastError = true)]
    private static partial int SetProcessDpiAwareness(ProcessDpiAwareness awareness);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDPIAware();

    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int S_OK = 0;
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);

    #endregion

    #region Host

    private IServiceProvider? _services;
    private IConfiguration? _configuration;
    private IHostEnvironment? _hostEnvironment;

    /// <summary>
    /// Gets the root <see cref="IServiceProvider"/> produced by the <see cref="AppBuilder"/>.
    /// Returns <see langword="null"/> when the application was not created via
    /// <see cref="AppBuilder"/>. Resolve application-scoped services through this provider
    /// (per-window or per-operation scopes should be created with <see cref="IServiceScopeFactory"/>).
    /// </summary>
    public IServiceProvider? Services => _services;

    /// <summary>
    /// Gets the configuration built by the <see cref="AppBuilder"/>. Exposes the same
    /// <see cref="IConfiguration"/> tree registered in <see cref="Services"/>.
    /// </summary>
    public IConfiguration? Configuration => _configuration;

    /// <summary>
    /// Gets the hosting environment (<c>Development</c>/<c>Staging</c>/<c>Production</c>,
    /// application name, content root) produced by the <see cref="AppBuilder"/>.
    /// </summary>
    public IHostEnvironment? HostEnvironment => _hostEnvironment;

    internal void AttachHost(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        _services = services;
        _configuration = configuration;
        _hostEnvironment = environment;
    }

    internal void DetachHost()
    {
        _services = null;
        _configuration = null;
        _hostEnvironment = null;
    }

    #endregion

    #region ResourcesNavigation

    private const string ComponentSeparator = ";component/";
    private static readonly CookieContainer s_cookieContainer = new();
    // Generated XAML module initializers assign the loader hooks below before the
    // Android NativeAOT host has attached Java.Interop to the VM. Constructing an
    // HttpClientHandler here would eagerly instantiate AndroidMessageHandler, which
    // queries Android.OS.Build.VERSION and fails because no JniRuntime exists yet.
    // Keep the handler lazy so type initialization remains platform-neutral.
    private static readonly Lazy<HttpClient> s_remoteClient = new(CreateRemoteClient);

    /// <summary>
    /// Runtime hook installed by Jalium.UI.Xaml for loading XAML into an existing object.
    /// It keeps the Controls assembly independent of the XAML runtime assembly.
    /// </summary>
    internal static Action<object, Uri>? ComponentLoader { get; set; }

    /// <summary>
    /// Runtime hook installed by Jalium.UI.Xaml for materializing a XAML root object.
    /// </summary>
    internal static Func<Uri, object?>? ComponentObjectLoader { get; set; }

    /// <summary>
    /// Occurs when navigation to a fragment is requested anywhere in the application.
    /// </summary>
    public event FragmentNavigationEventHandler? FragmentNavigation;

    /// <summary>
    /// Occurs when navigation content has finished loading.
    /// </summary>
    public event LoadCompletedEventHandler? LoadCompleted;

    /// <summary>
    /// Occurs after a navigator finds and displays its target content.
    /// </summary>
    public event NavigatedEventHandler? Navigated;

    /// <summary>
    /// Occurs before a navigator starts changing content.
    /// </summary>
    public event NavigatingCancelEventHandler? Navigating;

    /// <summary>
    /// Occurs when a navigation cannot be completed.
    /// </summary>
    public event NavigationFailedEventHandler? NavigationFailed;

    /// <summary>
    /// Occurs as navigation data is read.
    /// </summary>
    public event NavigationProgressEventHandler? NavigationProgress;

    /// <summary>
    /// Occurs when an in-progress navigation is stopped.
    /// </summary>
    public event NavigationStoppedEventHandler? NavigationStopped;

    /// <summary>
    /// Returns a stream for a loose application content file.
    /// </summary>
    public static StreamResourceInfo? GetContentStream(Uri uriContent)
    {
        ArgumentNullException.ThrowIfNull(uriContent);
        if (uriContent.IsAbsoluteUri)
        {
            throw new ArgumentException("Content URIs must be relative.", nameof(uriContent));
        }

        var relativePath = GetPathWithoutQueryOrFragment(uriContent.OriginalString);
        foreach (var baseDirectory in EnumerateContentBaseDirectories())
        {
            var fullPath = TryResolveLooseFile(baseDirectory, relativePath);
            if (fullPath != null && File.Exists(fullPath))
            {
                return new StreamResourceInfo(
                    new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read),
                    GetContentType(fullPath));
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a stream for a resource embedded in the application or a referenced assembly.
    /// </summary>
    public static StreamResourceInfo GetResourceStream(Uri uriResource)
    {
        ArgumentNullException.ThrowIfNull(uriResource);
        if (uriResource.IsAbsoluteUri && !IsApplicationPackUri(uriResource))
        {
            throw new ArgumentException(
                "Resource URIs must be relative or use the pack://application:,,,/ form.",
                nameof(uriResource));
        }

        if (!TryResolveResourceUri(uriResource, out var assembly, out var resourcePath))
        {
            throw new IOException($"The resource URI '{uriResource}' could not be resolved.");
        }

        var stream = TryOpenManifestResource(assembly, resourcePath, out var resolvedName);
        if (stream == null)
        {
            throw new IOException(
                $"The resource '{resourcePath}' was not found in assembly '{assembly.GetName().Name}'.");
        }

        return new StreamResourceInfo(stream, GetContentType(resolvedName ?? resourcePath));
    }

    /// <summary>
    /// Returns a stream for a site-of-origin or remote resource.
    /// </summary>
    public static StreamResourceInfo? GetRemoteStream(Uri uriRemote)
    {
        ArgumentNullException.ThrowIfNull(uriRemote);

        if (!uriRemote.IsAbsoluteUri)
        {
            return GetContentStream(uriRemote);
        }

        if (IsSiteOfOriginPackUri(uriRemote))
        {
            var path = ExtractPackPath(uriRemote.OriginalString);
            return GetContentStream(new Uri(path, UriKind.Relative));
        }

        if (uriRemote.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Remote URIs must be relative, HTTP(S), or use the pack://siteoforigin:,,,/ form.",
                nameof(uriRemote));
        }

        using var response = s_remoteClient.Value.GetAsync(uriRemote, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter()
            .GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var responseStream = response.Content.ReadAsStream();
        var payload = new MemoryStream();
        responseStream.CopyTo(payload);
        payload.Position = 0;

        var contentType = response.Content.Headers.ContentType?.ToString();
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = GetContentType(uriRemote.AbsolutePath);
        }

        return new StreamResourceInfo(payload, contentType);
    }

    /// <summary>
    /// Loads XAML into an existing component instance.
    /// </summary>
    public static void LoadComponent(object component, Uri resourceLocator)
    {
        ArgumentNullException.ThrowIfNull(component);
        ValidateComponentUri(resourceLocator);

        var loader = ComponentLoader;
        if (loader == null)
        {
            throw new InvalidOperationException(
                "No XAML component loader is registered. Reference Jalium.UI.Xaml before calling LoadComponent.");
        }

        loader(component, resourceLocator);
    }

    /// <summary>
    /// Loads and returns the root object declared by a XAML resource.
    /// </summary>
    public static object LoadComponent(Uri resourceLocator)
    {
        ValidateComponentUri(resourceLocator);

        var loaded = ComponentObjectLoader?.Invoke(resourceLocator);
        if (loaded == null && Current != null && StartupObjectLoader != null)
        {
            loaded = StartupObjectLoader(Current, resourceLocator);
        }

        return loaded ?? throw new InvalidOperationException(
            $"No XAML component could be loaded from '{resourceLocator}'.");
    }

    /// <summary>
    /// Returns the cookies associated with an absolute URI.
    /// </summary>
    public static string GetCookie(Uri uri)
    {
        ValidateCookieUri(uri);
        return s_cookieContainer.GetCookieHeader(uri);
    }

    /// <summary>
    /// Stores one or more Set-Cookie values for an absolute URI.
    /// </summary>
    public static void SetCookie(Uri uri, string value)
    {
        ValidateCookieUri(uri);
        ArgumentNullException.ThrowIfNull(value);
        s_cookieContainer.SetCookies(uri, value);
    }

    /// <summary>Raises <see cref="FragmentNavigation"/>.</summary>
    protected virtual void OnFragmentNavigation(FragmentNavigationEventArgs e)
        => FragmentNavigation?.Invoke(this, e);

    /// <summary>Raises <see cref="LoadCompleted"/>.</summary>
    protected virtual void OnLoadCompleted(NavigationEventArgs e)
        => LoadCompleted?.Invoke(this, e);

    /// <summary>Raises <see cref="Navigated"/>.</summary>
    protected virtual void OnNavigated(NavigationEventArgs e)
        => Navigated?.Invoke(this, e);

    /// <summary>Raises <see cref="Navigating"/>.</summary>
    protected virtual void OnNavigating(NavigatingCancelEventArgs e)
        => Navigating?.Invoke(this, e);

    /// <summary>Raises <see cref="NavigationFailed"/>.</summary>
    protected virtual void OnNavigationFailed(NavigationFailedEventArgs e)
        => NavigationFailed?.Invoke(this, e);

    /// <summary>Raises <see cref="NavigationProgress"/>.</summary>
    protected virtual void OnNavigationProgress(NavigationProgressEventArgs e)
        => NavigationProgress?.Invoke(this, e);

    /// <summary>Raises <see cref="NavigationStopped"/>.</summary>
    protected virtual void OnNavigationStopped(NavigationEventArgs e)
        => NavigationStopped?.Invoke(this, e);

    internal void RaiseFragmentNavigation(FragmentNavigationEventArgs e) => OnFragmentNavigation(e);
    internal void RaiseLoadCompleted(NavigationEventArgs e) => OnLoadCompleted(e);
    internal void RaiseNavigated(NavigationEventArgs e) => OnNavigated(e);
    internal void RaiseNavigating(NavigatingCancelEventArgs e) => OnNavigating(e);
    internal void RaiseNavigationFailed(NavigationFailedEventArgs e) => OnNavigationFailed(e);
    internal void RaiseNavigationProgress(NavigationProgressEventArgs e) => OnNavigationProgress(e);
    internal void RaiseNavigationStopped(NavigationEventArgs e) => OnNavigationStopped(e);

    private static HttpClient CreateRemoteClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = s_cookieContainer,
            UseCookies = true
        };
        return new HttpClient(handler, disposeHandler: true);
    }

    private static void ValidateComponentUri(Uri resourceLocator)
    {
        ArgumentNullException.ThrowIfNull(resourceLocator);
        if (resourceLocator.IsAbsoluteUri)
        {
            throw new ArgumentException("Component resource locators must be relative.", nameof(resourceLocator));
        }
    }

    private static void ValidateCookieUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("Cookie URIs must be absolute.", nameof(uri));
        }
    }

    private static bool TryResolveResourceUri(Uri uri, out Assembly assembly, out string resourcePath)
    {
        var text = uri.OriginalString;
        if (uri.IsAbsoluteUri)
        {
            text = ExtractPackPath(text);
        }

        text = GetPathWithoutQueryOrFragment(text).TrimStart('/');
        var separator = text.IndexOf(ComponentSeparator, StringComparison.OrdinalIgnoreCase);
        if (separator >= 0)
        {
            var assemblyName = text[..separator].TrimStart('/');
            resourcePath = text[(separator + ComponentSeparator.Length)..];
            assembly = ResolveAssembly(assemblyName) ?? ResourceAssembly;
            return !string.IsNullOrWhiteSpace(resourcePath) &&
                   string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase);
        }

        assembly = ResourceAssembly;
        resourcePath = text;
        return !string.IsNullOrWhiteSpace(resourcePath);
    }

    private static Assembly? ResolveAssembly(string assemblyName)
    {
        if (string.Equals(ResourceAssembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceAssembly;
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (loaded != null)
        {
            return loaded;
        }

        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
    }

    private static Stream? TryOpenManifestResource(Assembly assembly, string resourcePath, out string? resolvedName)
    {
        var normalized = resourcePath.Replace('\\', '/').TrimStart('/');
        var dotted = normalized.Replace('/', '.');
        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var paths = new List<string> { normalized };
        if (normalized.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(normalized[..^".xaml".Length] + ".jalxaml");
        }

        var manifestNames = assembly.GetManifestResourceNames();
        foreach (var path in paths)
        {
            var pathDotted = path.Replace('/', '.');
            foreach (var candidate in new[] { path, pathDotted, $"{assemblyName}.{path}", $"{assemblyName}.{pathDotted}" })
            {
                var stream = assembly.GetManifestResourceStream(candidate);
                if (stream != null)
                {
                    resolvedName = candidate;
                    return stream;
                }

                var actual = manifestNames.FirstOrDefault(name =>
                    string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
                if (actual != null)
                {
                    stream = assembly.GetManifestResourceStream(actual);
                    if (stream != null)
                    {
                        resolvedName = actual;
                        return stream;
                    }
                }
            }

            var suffix = $".{pathDotted}";
            var suffixMatch = manifestNames.FirstOrDefault(name =>
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (suffixMatch != null)
            {
                var stream = assembly.GetManifestResourceStream(suffixMatch);
                if (stream != null)
                {
                    resolvedName = suffixMatch;
                    return stream;
                }
            }
        }

        resolvedName = null;
        return null;
    }

    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "ResourceAssembly.Location is optional; AppContext.BaseDirectory is the authoritative single-file and NativeAOT fallback.")]
    private static IEnumerable<string> EnumerateContentBaseDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? resourceDirectory = null;
        try
        {
            resourceDirectory = Path.GetDirectoryName(ResourceAssembly.Location);
        }
        catch (NotSupportedException)
        {
            // Dynamic assemblies do not expose a physical location.
        }

        foreach (var directory in new[]
        {
            AppContext.BaseDirectory,
            resourceDirectory,
            Environment.CurrentDirectory
        })
        {
            if (!string.IsNullOrWhiteSpace(directory) && seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    private static string? TryResolveLooseFile(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        var candidate = Path.GetFullPath(Path.Combine(
            fullBase,
            Uri.UnescapeDataString(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullBase, candidate);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? candidate
            : null;
    }

    private static string GetPathWithoutQueryOrFragment(string uriText)
    {
        var end = uriText.Length;
        var query = uriText.IndexOf('?');
        if (query >= 0)
        {
            end = Math.Min(end, query);
        }

        var fragment = uriText.IndexOf('#');
        if (fragment >= 0)
        {
            end = Math.Min(end, fragment);
        }

        return uriText[..end];
    }

    private static bool IsApplicationPackUri(Uri uri)
        => uri.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase) &&
           uri.OriginalString.StartsWith("pack://application:,,,/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSiteOfOriginPackUri(Uri uri)
        => uri.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase) &&
           uri.OriginalString.StartsWith("pack://siteoforigin:,,,/", StringComparison.OrdinalIgnoreCase);

    private static string ExtractPackPath(string text)
    {
        var marker = text.IndexOf(",,,/", StringComparison.Ordinal);
        return marker >= 0 ? text[(marker + 4)..] : text.TrimStart('/');
    }

    private static string GetContentType(string resourceName)
        => Path.GetExtension(GetPathWithoutQueryOrFragment(resourceName)).ToLowerInvariant() switch
        {
            ".bmp" => "image/bmp",
            ".css" => "text/css",
            ".gif" => "image/gif",
            ".htm" or ".html" => "text/html",
            ".ico" => "image/x-icon",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".jalxaml" or ".xaml" => "application/xaml+xml",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };

    #endregion
}

/// <summary>
/// Event data for <see cref="Application.Startup"/>, providing the command-line
/// arguments passed to the application.
/// </summary>
public class StartupEventArgs : EventArgs
{
    /// <summary>
    /// Gets the command-line arguments that were passed to the application.
    /// Never <see langword="null"/> — an empty array is used when no arguments
    /// were supplied.
    /// </summary>
    public string[] Args { get; }

    public StartupEventArgs() : this(Array.Empty<string>())
    {
    }

    public StartupEventArgs(string[] args)
    {
        Args = args ?? Array.Empty<string>();
    }
}

/// <summary>
/// Event data for <see cref="Application.Exit"/>. Handlers may override the
/// process exit code returned by <see cref="Application.Run()"/> by setting
/// <see cref="ApplicationExitCode"/>.
/// </summary>
public class ExitEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the exit code that will be returned from
    /// <see cref="Application.Run()"/>. Defaults to the value supplied by the
    /// framework when the message loop exited.
    /// </summary>
    public int ApplicationExitCode { get; set; }

    public ExitEventArgs() : this(0)
    {
    }

    public ExitEventArgs(int applicationExitCode)
    {
        ApplicationExitCode = applicationExitCode;
    }
}
