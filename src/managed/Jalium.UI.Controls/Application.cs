using System.Runtime.InteropServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Encapsulates a Jalium.UI application.
/// </summary>
[ContentProperty("Resources")]
public partial class Application
{
    private static Application? _current;

    /// <summary>
    /// Framework-internal startup object loader registered by Jalium.UI.Xaml.
    /// </summary>
    internal static Func<Application, string, object?>? StartupObjectLoader { get; set; }

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application? Current => _current;

    /// <summary>
    /// Gets or sets the main window.
    /// </summary>
    public Window? MainWindow { get; set; }

    /// <summary>
    /// Gets or sets the startup URI used to load the initial window or visual root.
    /// Supports relative paths and pack-style paths (e.g. /Assembly;component/Path/File.xaml).
    /// </summary>
    public string? StartupUri { get; set; }

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

        _current = this;

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
    public void Run()
    {
        Startup?.Invoke(this, EventArgs.Empty);

        var startupWindow = ResolveStartupWindow();
        if (startupWindow != null && startupWindow.Handle == nint.Zero)
        {
            startupWindow.Show();
        }

        try
        {
            // Message loop
            while (GetMessage(out var msg, nint.Zero, 0, 0))
            {
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
    }

    private void Cleanup()
    {
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
    }

    /// <summary>
    /// Starts the application with the specified main window.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    public void Run(Window mainWindow)
    {
        MainWindow = mainWindow;
        Run();
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

        if (string.IsNullOrWhiteSpace(StartupUri))
            return null;

        if (StartupObjectLoader == null)
        {
            throw new InvalidOperationException(
                $"StartupUri '{StartupUri}' cannot be resolved because no startup loader is registered.");
        }

        var startupObject = StartupObjectLoader(this, StartupUri);
        if (startupObject == null)
        {
            throw new InvalidOperationException(
                $"StartupUri '{StartupUri}' resolved to null.");
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
            $"StartupUri '{StartupUri}' resolved to unsupported startup object type '{startupObject.GetType().FullName}'.");
    }

    /// <summary>
    /// Shuts down the application.
    /// </summary>
    public void Shutdown()
    {
        PostQuitMessage(0);
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

    #region Win32 Interop

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    #endregion
}
