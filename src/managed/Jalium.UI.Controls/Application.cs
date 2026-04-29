using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media.Animation;

namespace Jalium.UI;

/// <summary>
/// Encapsulates a Jalium.UI application.
/// </summary>
[ContentProperty("Resources")]
public partial class Application
{
    private static Application? _current;
    private readonly WorkingSetTrimController? _workingSetTrimController;

    /// <summary>
    /// Framework-internal startup object loader registered by Jalium.UI.Xaml.
    /// </summary>
    internal static Func<Application, Uri, object?>? StartupObjectLoader { get; set; }

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application? Current => _current;

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
    public ResourceDictionary Resources
    {
        get
        {
            if (_resources == null)
            {
                _resources = new ResourceDictionary();
                _resources.Changed += OnApplicationResourcesDictionaryChanged;
                _resources.ChangedWithKeys += OnApplicationResourcesChangedWithKeys;
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
            }

            _resources = value ?? new ResourceDictionary();
            _resources.Changed += OnApplicationResourcesDictionaryChanged;
            _resources.ChangedWithKeys += OnApplicationResourcesChangedWithKeys;
            OnApplicationResourcesChanged();
        }
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
    public event EventHandler<StartupEventArgs>? Startup;

    /// <summary>
    /// Occurs after the message loop exits and before the application cleans up.
    /// Handlers may observe / override <see cref="ExitEventArgs.ApplicationExitCode"/>.
    /// </summary>
    public event EventHandler<ExitEventArgs>? Exit;

    /// <summary>
    /// Occurs when the Windows session is ending (logoff or shutdown). The handler
    /// may cancel the end-session request by setting <see cref="SessionEndingCancelEventArgs.Cancel"/>.
    /// </summary>
    public event EventHandler<SessionEndingCancelEventArgs>? SessionEnding;

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
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    /// <summary>
    /// Stopwatch timestamp captured at the very first instruction of the
    /// Application constructor.  Used by Window.Show's startup trace to
    /// compute the "user Main + AppBuilder + builder.Build" interval.
    /// </summary>
    internal static long ApplicationCtorEnterTimestamp;

    /// <summary>
    /// Stopwatch timestamp captured at the last instruction of the Application
    /// constructor.  The gap between this and Window.Show's entry timestamp
    /// is the user-app cost of constructing the main window — typically the
    /// new MainWindow() call plus DI service instantiation, ViewModel construction,
    /// and the user-supplied ConfigureApplication callback.
    /// </summary>
    internal static long ApplicationCtorExitTimestamp;

    public Application()
    {
        // Capture ctor entry timestamp BEFORE any user-observable work so the
        // startup trace can attribute the gap between Jalium.UI.Controls's
        // module load and this point to the user's Main / AppBuilder pipeline.
        if (ApplicationCtorEnterTimestamp == 0)
            ApplicationCtorEnterTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        // Optional startup tracing — set JALIUM_STARTUP_TRACE=1 to print one summary
        // line per major phase to Console.Error.  Off by default, no production cost.
        bool trace = Environment.GetEnvironmentVariable("JALIUM_STARTUP_TRACE") == "1";
        long t0 = trace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long tDpi = 0, tDispatcher = 0, tKeyboard = 0, tTheme = 0, tToolTip = 0, tInitComponent = 0;

        if (_current != null)
        {
            throw new InvalidOperationException("Only one Application instance can be created.");
        }

        // Make the process DPI-aware before any framework HWND is created so
        // consumer apps launched from the NuGet quick-start path stay crisp on
        // scaled displays even without a custom app.manifest.
        if (PlatformFactory.IsWindows)
        {
            _ = TryEnablePerMonitorDpiAwareness();
        }
        else
        {
            // Initialize the cross-platform native platform library
            PlatformFactory.InitializePlatform();
        }
        if (trace) tDpi = System.Diagnostics.Stopwatch.GetTimestamp();

        // Note: render-context (D3D12/Vulkan device) creation is kicked off on a
        // background thread by GpuPrewarmInitializer's [ModuleInitializer] when
        // Jalium.UI.Controls is loaded — earlier than this constructor — so the
        // ~200–400 ms native device init runs in parallel with AppBuilder.Build,
        // ThemeManager.Initialize below, and the user's MainWindow construction.

        _current = this;
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        // Initialize the dispatcher for the main UI thread
        Dispatcher.SetAsMainThread();
        // Install a SynchronizationContext so async/await resumes on the UI dispatcher thread.
        // WebView2 initialization relies on UI-thread affinity across awaits.
        SynchronizationContext.SetSynchronizationContext(
            new Jalium.UI.Threading.DispatcherSynchronizationContext(
                Dispatcher.MainDispatcher ?? Dispatcher.GetForCurrentThread()));
        if (trace) tDispatcher = System.Diagnostics.Stopwatch.GetTimestamp();

        // Initialize keyboard/focus system
        Keyboard.Initialize();
        if (trace) tKeyboard = System.Diagnostics.Stopwatch.GetTimestamp();

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
        ResourceLookup.AncestorRedirectLookup = ResolveResourceAncestorRedirect;

        // Initialize default theme (loads default styles for all controls)
        ThemeManager.Initialize(this);
        if (trace) tTheme = System.Diagnostics.Stopwatch.GetTimestamp();

        // Initialize validation adorner integration
        ValidationAdornerIntegration.Initialize();

        // Force ToolTip static constructor to register show/hide delegates early
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ToolTip).TypeHandle);
        if (trace) tToolTip = System.Diagnostics.Stopwatch.GetTimestamp();

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
        if (trace) tInitComponent = System.Diagnostics.Stopwatch.GetTimestamp();

        // Capture ctor-exit timestamp so Window.Show can attribute the gap
        // between here and Show() entry to user-app MainWindow construction
        // (host.Run → ConfigureApplication callback → new MainWindow + DI).
        ApplicationCtorExitTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        if (trace)
        {
            static long Ms(long start, long end) =>
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
            Console.Error.WriteLine(
                $"[Jalium.UI startup] Application ctor: total {Ms(t0, tInitComponent)}ms " +
                $"(dpi {Ms(t0, tDpi)}ms, dispatcher {Ms(tDpi, tDispatcher)}ms, " +
                $"keyboard {Ms(tDispatcher, tKeyboard)}ms, theme {Ms(tKeyboard, tTheme)}ms, " +
                $"tooltip+uia {Ms(tTheme, tToolTip)}ms, init-component {Ms(tToolTip, tInitComponent)}ms)");
        }
    }

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
        if (_current?._resources != null)
        {
            if (_current._resources.TryGetValue(resourceKey, out var value))
            {
                return value;
            }
        }
        return null;
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
                // Win32 message loop
                while (true)
                {
                    var messageResult = GetMessage(out var msg, nint.Zero, 0, 0);
                    if (messageResult == 0)
                    {
                        exitCode = unchecked((int)msg.wParam);
                        break;
                    }

                    if (messageResult < 0)
                    {
                        throw new InvalidOperationException("The application message loop failed while retrieving a window message.");
                    }

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref MSG lpMsg);

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
