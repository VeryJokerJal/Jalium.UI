using System.Runtime.InteropServices;
using Jalium.UI.Controls;
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
            }

            return _resources;
        }
        set
        {
            if (_resources == value)
                return;

            if (_resources != null)
                _resources.Changed -= OnApplicationResourcesDictionaryChanged;

            _resources = value ?? new ResourceDictionary();
            _resources.Changed += OnApplicationResourcesDictionaryChanged;
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
    /// Occurs when the application is starting.
    /// </summary>
    public event EventHandler? Startup;

    /// <summary>
    /// Occurs when the application is exiting.
    /// </summary>
    public event EventHandler? Exit;

    /// <summary>
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    public Application()
    {
        if (_current != null)
        {
            throw new InvalidOperationException("Only one Application instance can be created.");
        }

        // Make the process DPI-aware before any framework HWND is created so
        // consumer apps launched from the NuGet quick-start path stay crisp on
        // scaled displays even without a custom app.manifest.
        _ = TryEnablePerMonitorDpiAwareness();

        _current = this;
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        // Initialize the dispatcher for the main UI thread
        Dispatcher.SetAsMainThread();
        // Install a SynchronizationContext so async/await resumes on the UI dispatcher thread.
        // WebView2 initialization relies on UI-thread affinity across awaits.
        SynchronizationContext.SetSynchronizationContext(
            new Jalium.UI.Threading.DispatcherSynchronizationContext(
                Dispatcher.MainDispatcher ?? Dispatcher.GetForCurrentThread()));

        // Initialize keyboard/focus system
        Keyboard.Initialize();

        // Register application resource lookup callback
        ResourceLookup.ApplicationResourceLookup = LookupApplicationResource;
        ResourceLookup.AncestorRedirectLookup = ResolveResourceAncestorRedirect;

        // Initialize default theme (loads default styles for all controls)
        ThemeManager.Initialize(this);

        // Initialize validation adorner integration
        ValidationAdornerIntegration.Initialize();

        // Force ToolTip static constructor to register show/hide delegates early
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ToolTip).TypeHandle);

        // Optional ultra-low visible memory mode (off by default).
        _workingSetTrimController = WorkingSetTrimController.TryCreateFromEnvironment();

        // Auto-call InitializeComponent() on derived classes to load their JALXAML resources.
        // This mirrors WPF behavior where Application subclass resources are loaded automatically.
        // The source generator emits InitializeComponent() as a private method, so use reflection.
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
        OnApplicationResourcesChanged();
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
    /// Starts the application message loop.
    /// </summary>
    public int Run()
    {
        Startup?.Invoke(this, EventArgs.Empty);

        var startupWindow = ResolveStartupWindow();
        if (startupWindow != null && startupWindow.Handle == nint.Zero)
        {
            startupWindow.Show();
        }

        var exitCode = 0;
        try
        {
            // Message loop
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
        finally
        {
            // Fire Exit event before cleanup so handlers can still access application state
            Exit?.Invoke(this, EventArgs.Empty);
            Cleanup();
        }

        return exitCode;
    }

    private void Cleanup()
    {
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
        PostQuitMessage(exitCode);
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
