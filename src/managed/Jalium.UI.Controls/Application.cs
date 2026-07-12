using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Encapsulates a Jalium.UI application.
/// </summary>
[ContentProperty("Resources")]
public partial class Application : Jalium.UI.Threading.DispatcherObject, IQueryAmbient
{
    private static Application? _current;
    private static Assembly? _resourceAssembly;
    private readonly WorkingSetTrimController? _workingSetTrimController;
    private readonly IDictionary _properties = new Hashtable();
    private readonly WindowCollection _windows = new(Window.SnapshotOpenWindows);
    private bool _isActive;
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
    /// Occurs when the Windows session is ending (logoff or shutdown). The handler
    /// may cancel the end-session request by setting <see cref="SessionEndingCancelEventArgs.Cancel"/>.
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
        GpuPrewarmInitializer.Prewarm();

        _current = this;
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        // Initialize the dispatcher for the main UI thread
        Jalium.UI.Dispatcher.SetAsMainThread();
        // Install a SynchronizationContext so async/await resumes on the UI dispatcher thread.
        // WebView2 initialization relies on UI-thread affinity across awaits.
        SynchronizationContext.SetSynchronizationContext(
            new Jalium.UI.Threading.DispatcherSynchronizationContext(
                Jalium.UI.Dispatcher.MainDispatcher ?? Jalium.UI.Dispatcher.GetForCurrentThread()));

        // Initialize keyboard/focus system
        Keyboard.Initialize();

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
        ThemeManager.Initialize(this);

        // Initialize validation adorner integration
        ValidationAdornerIntegration.Initialize();

        // Force ToolTip static constructor to register show/hide delegates early
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ToolTip).TypeHandle);

        // Optional ultra-low visible memory mode (off by default).
        _workingSetTrimController = WorkingSetTrimController.TryCreateFromEnvironment();

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
        CallInitializeComponent();
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

        if (MainWindow is FrameworkElement root)
        {
            root.NotifyResourcesChangedFromRoot();
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

        OnStartup(new StartupEventArgs(args));

        var startupWindow = ResolveStartupWindow();
        if (startupWindow != null && startupWindow.Handle == nint.Zero)
        {
            startupWindow.Show();
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
                var dispatcher = Jalium.UI.Dispatcher.MainDispatcher ?? Jalium.UI.Dispatcher.GetForCurrentThread();
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
            PlatformFactory.QuitMessageLoop(exitCode);
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
