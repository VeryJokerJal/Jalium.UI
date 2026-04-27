using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Rendering;
using Jalium.UI.Threading;
using Jalium.UI.Controls.DevTools;
using System.Diagnostics;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a window in the Jalium.UI framework.
/// </summary>
public partial class Window : ContentControl, IWindowHost, ILayoutManagerHost, IInputDispatcherHost, IAdornerLayerHost
{
    private static readonly bool ForceFullReplayForD3D12 = IsEnvironmentSwitchEnabled("JALIUM_D3D12_FORCE_FULL_REPLAY");
    private static readonly bool DebugRender = IsEnvironmentSwitchEnabled("JALIUM_DEBUG_RENDER");

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.WindowAutomationPeer(this);
    }

    /// <summary>
    /// Returns the <see cref="Window"/> that hosts the specified <see cref="DependencyObject"/>,
    /// by walking up the visual tree.
    /// </summary>
    /// <param name="dependencyObject">The element whose host window is to be found.</param>
    /// <returns>The hosting <see cref="Window"/>, or <c>null</c> if not found.</returns>
    public static Window? GetWindow(DependencyObject dependencyObject)
    {
        if (dependencyObject is Window w)
            return w;

        if (dependencyObject is Visual visual)
        {
            Visual? current = visual;
            while (current != null)
            {
                if (current is Window window)
                    return window;
                current = current.VisualParent;
            }
        }
        return null;
    }

    private readonly LayoutManager _layoutManager = new();
    private readonly WindowInputDispatcher _inputDispatcher;
    private double _dpiScale = 1.0;
    private Dispatcher? _dispatcher; // UI thread Dispatcher, captured in Show()

    // Android platform state
    private Thickness _safeAreaInsets;
    private bool _softKeyboardVisible;
    private double _softKeyboardHeight;
    private DeviceOrientation _deviceOrientation;
    // Render state machine — all flags packed into a single int for atomic access.
    // Prevents race conditions where multiple threads check-then-set individual bools.
    private int _renderState; // Bitfield of RenderStateFlags, accessed via Interlocked
    private const int RenderFlag_Scheduled      = 1 << 0; // A Dispatcher-based render is pending
    private const int RenderFlag_Rendering       = 1 << 1; // Inside RenderFrame execution
    private const int RenderFlag_Requested       = 1 << 2; // InvalidateWindow called during rendering
    private const int RenderFlag_DirtyBetween    = 1 << 3; // InvalidateWindow blocked between frames

    /// <summary>Atomically sets a flag if it was not already set. Returns true if this call set it.</summary>
    private bool TrySetRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            if ((prev & flag) != 0) return false;
            next = prev | flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
        return true;
    }

    /// <summary>Atomically sets a flag (unconditionally).</summary>
    private void SetRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = prev | flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    /// <summary>Atomically clears a flag.</summary>
    private void ClearRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = prev & ~flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    /// <summary>Reads whether a flag is currently set.</summary>
    private bool HasRenderFlag(int flag) => (Volatile.Read(ref _renderState) & flag) != 0;

    // ── Debug HUD ──
    private readonly RenderDebugHud _debugHud = new();
    private readonly DebugHudOverlay _debugHudOverlay = new();
    private sealed class RenderDebugHud
    {
        // ── Enabled flag (off by default, toggle with F3) ──
        public bool Enabled { get; set; }

        // ── Timing ──
        private readonly System.Diagnostics.Stopwatch _intervalSw = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _frameSw = new();
        private int _frameCount;
        private double _lastFrameMs, _worstFrameMs;
        private double _layoutMs, _renderMs, _presentMs;

        // ── Counters (accumulated per interval) ──
        private int _renderFrameCalls, _paintCalls, _processRenderCalls;
        private int _beginDrawFails, _resizeCount;
        private int _fullFrames, _partialFrames, _skippedFrames;
        private int _promotedFrames;      // Partial dirty region that exceeded the area threshold → promoted to full
        private int _capacityExceeded;    // Aggregator hit its soft capacity and performed a forced merge
        private int _dirtyElementCount;
        private int _dirtyRectCount;      // Rects submitted to the native RT on the most recent partial frame
        private double _dirtyCoverageRatio; // Real covered pixel ratio (0-1) of the most recent partial frame

        // ── Snapshot (displayed values, updated once per second) ──
        private double _dFps, _dWorstMs;
        private double _dLayoutMs, _dRenderMs, _dPresentMs;
        private int _dRenderFrameCalls, _dPaintCalls, _dProcessRenderCalls;
        private int _dBeginDrawFails, _dResizeCount;
        private int _dFullFrames, _dPartialFrames, _dSkippedFrames;
        private int _dPromotedFrames, _dCapacityExceeded;
        private int _dDirtyElements, _dDirtyRectCount;
        private double _dDirtyCoverageRatio;

        // ── State ──
        private string _renderPath = "—";
        private string _backendName = "?";
        private string _engineName = "?";
        private int _windowWidth, _windowHeight;
        private float _dpiScale;
        private Rect _dirtyRegion;
        private long _gcTotalBytes;
        private int _gcGen0, _gcGen1, _gcGen2;

        // ── Events ──
        public void OnRenderFrame() { _renderFrameCalls++; _frameSw.Restart(); }
        public void OnPaint() => _paintCalls++;
        public void OnProcessRender() => _processRenderCalls++;
        public void OnBeginFail() => _beginDrawFails++;
        public void OnResize() => _resizeCount++;
        public void OnSkipped() => _skippedFrames++;
        public void OnFull() { _fullFrames++; _renderPath = "Full"; }
        public void OnPartial() { _partialFrames++; _renderPath = "Partial"; }
        public void OnPromoted() { _promotedFrames++; _renderPath = "Promoted→Full"; }
        public void OnCapacityExceeded() => _capacityExceeded++;
        public void MarkLayout() => _layoutMs = _frameSw.Elapsed.TotalMilliseconds;
        public void MarkRender() => _renderMs = _frameSw.Elapsed.TotalMilliseconds;
        public void SetBackend(string b) => _backendName = b;
        public void SetEngine(string e) => _engineName = e;
        public void SetWindowSize(int w, int h) { _windowWidth = w; _windowHeight = h; }
        public void SetDpiScale(float s) => _dpiScale = s;
        public void SetDirtyInfo(int elementCount, Rect region) { _dirtyElementCount = elementCount; _dirtyRegion = region; }
        public void SetDirtyRegionStats(int rectCount, double coverageRatio)
        {
            _dirtyRectCount = rectCount;
            _dirtyCoverageRatio = coverageRatio;
        }

        public readonly Jalium.UI.Diagnostics.FrameHistory FrameHistory = new();

        public void OnEndDraw()
        {
            _presentMs = _frameSw.Elapsed.TotalMilliseconds;
            _lastFrameMs = _presentMs;
            if (_lastFrameMs > _worstFrameMs) _worstFrameMs = _lastFrameMs;
            _frameCount++;

            double layoutMs = _layoutMs;
            double renderMs = Math.Max(0, _renderMs - _layoutMs);
            double presentMs = Math.Max(0, _presentMs - _renderMs);
            FrameHistory.Push(new Jalium.UI.Diagnostics.FrameHistory.Sample(
                layoutMs, renderMs, presentMs, _presentMs, _dirtyElementCount));
        }

        private void FlushInterval()
        {
            if (_intervalSw.Elapsed.TotalSeconds < 1.0) return;

            _dFps = _frameCount / _intervalSw.Elapsed.TotalSeconds;
            _dWorstMs = _worstFrameMs;
            _dLayoutMs = _layoutMs;
            _dRenderMs = _renderMs;
            _dPresentMs = _presentMs;
            _dRenderFrameCalls = _renderFrameCalls;
            _dPaintCalls = _paintCalls;
            _dProcessRenderCalls = _processRenderCalls;
            _dBeginDrawFails = _beginDrawFails;
            _dResizeCount = _resizeCount;
            _dFullFrames = _fullFrames;
            _dPartialFrames = _partialFrames;
            _dSkippedFrames = _skippedFrames;
            _dPromotedFrames = _promotedFrames;
            _dCapacityExceeded = _capacityExceeded;
            _dDirtyElements = _dirtyElementCount;
            _dDirtyRectCount = _dirtyRectCount;
            _dDirtyCoverageRatio = _dirtyCoverageRatio;

            // Sample GC stats
            _gcTotalBytes = GC.GetTotalMemory(false);
            _gcGen0 = GC.CollectionCount(0);
            _gcGen1 = GC.CollectionCount(1);
            _gcGen2 = GC.CollectionCount(2);

            // Reset accumulators
            _frameCount = 0;
            _renderFrameCalls = 0;
            _paintCalls = 0;
            _processRenderCalls = 0;
            _beginDrawFails = 0;
            _resizeCount = 0;
            _fullFrames = 0;
            _partialFrames = 0;
            _skippedFrames = 0;
            _promotedFrames = 0;
            _capacityExceeded = 0;
            _worstFrameMs = 0;
            _intervalSw.Restart();
        }

        /// <summary>
        /// Readonly snapshot of the partial-render diagnostics.
        /// Exposed so DevTools / custom HUDs can render their own summary.
        /// </summary>
        public (int full, int partial, int promoted, int skipped, int capacityExceeded,
                int dirtyRects, double coverageRatio) PartialRenderSnapshot() =>
            (_dFullFrames, _dPartialFrames, _dPromotedFrames, _dSkippedFrames,
             _dCapacityExceeded, _dDirtyRectCount, _dDirtyCoverageRatio);

        public void UpdateOverlay(DebugHudOverlay overlay)
        {
            if (!Enabled) return;
            FlushInterval();

            double layoutMs = _dLayoutMs;
            double renderMs = Math.Max(0, _dRenderMs - _dLayoutMs);
            double presentMs = Math.Max(0, _dPresentMs - _dRenderMs);
            string dirtyStr = _dirtyRegion.IsEmpty ? "none"
                : $"{_dirtyRegion.X:F0},{_dirtyRegion.Y:F0} {_dirtyRegion.Width:F0}x{_dirtyRegion.Height:F0}";

            overlay.Update(
                _dFps, _dWorstMs,
                layoutMs, renderMs, presentMs, _dPresentMs,
                _renderPath, _backendName, _engineName,
                _dFullFrames, _dPartialFrames, _dSkippedFrames, _dBeginDrawFails,
                _dDirtyElements, dirtyStr,
                _windowWidth, _windowHeight, _dpiScale,
                _gcTotalBytes, _gcGen0, _gcGen1, _gcGen2,
                _dPromotedFrames, _dCapacityExceeded,
                _dDirtyRectCount, _dDirtyCoverageRatio);
        }
    }

    private bool _isFirstLayout = true;
    private bool _renderRecoveryInProgress;
    private DispatcherTimer? _renderRecoveryRetryTimer;
    private int _consecutiveRecoverableRenderFailures;
    private int _renderRecoveryRetryDelayMs = RenderRecoveryRetryInitialDelayMs;
    private RenderBackend _renderBackendOverride = RenderBackend.Auto;
    private bool _fullInvalidation = true;  // First frame is always full
    // FLIP_SEQUENTIAL with N buffers: buffer K's non-dirty area still has content
    // from frame K-N.  We must repaint the union of the last N-1 dirty regions
    // to cover all stale buffers.  With FrameCount=3, track 2 previous regions.
    // Store per-frame region snapshots (array of rects) instead of a single
    // bounding rect so that scattered small dirty patches across frames don't
    // ratchet the Union into a giant bounding box.
    private const int DirtyHistoryCount = 2;
    private readonly Rect[][] _dirtyHistory = new Rect[DirtyHistoryCount][];
    private int _dirtyHistoryIndex;
    private long _lastRenderTicks;          // Timestamp of last completed render (for rate-limiting)
    private Timer? _renderThrottleTimer;    // Deferred render when rate-limited or waiting for the GPU
    private long _suppressEscapeUntilTick;
    // Per-dirty-element state.  PreLayoutBounds captures where the element WAS
    // when AddDirtyElement was first called in this frame (before UpdateLayout).
    // PreciseLocalRects (optional) lets callers mark only a sub-rectangle of the
    // element dirty — caret blink, focus ring, progress-bar fill — instead of
    // the whole control. Post-layout bounds are computed at render time.
    private sealed class DirtyElementEntry
    {
        public Rect PreLayoutBounds;
        public List<Rect>? PreciseLocalRects;
    }
    private readonly Dictionary<UIElement, DirtyElementEntry> _dirtyElements = new();
    // Free-floating dirty rects in window (screen) coordinates. Populated by
    // AddDirtyRect — used when an animation / compositor system knows a region
    // changed but doesn't own a single UIElement.
    private readonly List<Rect> _dirtyFreeRects = new();
    private readonly object _dirtyLock = new(); // Protects _dirtyElements from cross-thread access
    private int _appliedDwmTopMarginPhysical = -1;
    private bool _attemptedAutoWindowIcon;
    private bool _isActive;
    private bool _contentRendered;
    private bool _isSyncingPosition;
    private Rect _restoreBounds;
    // Pre-fullscreen snapshot: bounds (screen px), WS style, and WindowState to restore.
    private bool _isFullScreen;
    private RECT _fullScreenSavedRect;
    private uint _fullScreenSavedStyle;
    private uint _fullScreenSavedExStyle;
    private WindowState _fullScreenPreviousState;
    private readonly List<Window> _ownedWindows = [];
    private const double DefaultTitleBarHeightDip = 32.0;
    private const int GpuBusyRetryDelayMs = 1;
    private const int RenderRecoveryRetryInitialDelayMs = 120;
    private const int RenderRecoveryRetryMaxDelayMs = 2000;
    private const int DeviceLostBackendFallbackThreshold = 2;
    private const string D3D12ForceWarpEnvironmentVariable = "JALIUM_D3D12_FORCE_WARP";

    private static bool IsEnvironmentSwitchEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the layout manager for this window.
    /// </summary>
    LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Window),
            new PropertyMetadata("Window", OnTitleChanged));

    /// <summary>
    /// Identifies the WindowState dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty WindowStateProperty =
        DependencyProperty.Register(nameof(WindowState), typeof(WindowState), typeof(Window),
            new PropertyMetadata(WindowState.Normal, OnWindowStateChanged));

    /// <summary>
    /// Identifies the TitleBarStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TitleBarStyleProperty =
        DependencyProperty.Register(nameof(TitleBarStyle), typeof(WindowTitleBarStyle), typeof(Window),
            new PropertyMetadata(WindowTitleBarStyle.Custom, OnTitleBarStyleChanged));

    /// <summary>
    /// Identifies the SystemBackdrop dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SystemBackdropProperty =
        DependencyProperty.Register(nameof(SystemBackdrop), typeof(WindowBackdropType), typeof(Window),
            new PropertyMetadata(WindowBackdropType.None, OnSystemBackdropChanged));

    /// <summary>
    /// Identifies the Topmost dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty TopmostProperty =
        DependencyProperty.Register(nameof(Topmost), typeof(bool), typeof(Window),
            new PropertyMetadata(false, OnTopmostChanged));

    /// <summary>
    /// Identifies the SizeToContent dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty SizeToContentProperty =
        DependencyProperty.Register(nameof(SizeToContent), typeof(SizeToContent), typeof(Window),
            new PropertyMetadata(SizeToContent.Manual));

    /// <summary>
    /// Identifies the ResizeMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ResizeModeProperty =
        DependencyProperty.Register(nameof(ResizeMode), typeof(ResizeMode), typeof(Window),
            new PropertyMetadata(ResizeMode.CanResize));

    /// <summary>
    /// Identifies the WindowStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty WindowStyleProperty =
        DependencyProperty.Register(nameof(WindowStyle), typeof(WindowStyle), typeof(Window),
            new PropertyMetadata(WindowStyle.SingleBorderWindow, OnWindowStyleChanged));

    /// <summary>
    /// Identifies the LeftWindowCommands dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftWindowCommandsProperty =
        DependencyProperty.Register(nameof(LeftWindowCommands), typeof(FrameworkElement), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the RightWindowCommands dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RightWindowCommandsProperty =
        DependencyProperty.Register(nameof(RightWindowCommands), typeof(FrameworkElement), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowIconProperty =
        DependencyProperty.Register(nameof(IsShowIcon), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowTitleProperty =
        DependencyProperty.Register(nameof(IsShowTitle), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowTitleBar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowTitleBarProperty =
        DependencyProperty.Register(nameof(IsShowTitleBar), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowMinimizeButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowMinimizeButtonProperty =
        DependencyProperty.Register(nameof(IsShowMinimizeButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowMaximizeButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowMaximizeButtonProperty =
        DependencyProperty.Register(nameof(IsShowMaximizeButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowCloseButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowCloseButtonProperty =
        DependencyProperty.Register(nameof(IsShowCloseButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the HasSystemMenu dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty HasSystemMenuProperty =
        DependencyProperty.Register(nameof(HasSystemMenu), typeof(bool), typeof(Window),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the TitleBarFontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TitleBarFontSizeProperty =
        DependencyProperty.Register(nameof(TitleBarFontSize), typeof(double), typeof(Window),
            new PropertyMetadata(14.0, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the TitleBarHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TitleBarHeightProperty =
        DependencyProperty.Register(nameof(TitleBarHeight), typeof(double), typeof(Window),
            new PropertyMetadata(DefaultTitleBarHeightDip, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the WindowIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty WindowIconProperty =
        DependencyProperty.Register(nameof(WindowIcon), typeof(ImageSource), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the Left dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.Register(nameof(Left), typeof(double), typeof(Window),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the Top dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.Register(nameof(Top), typeof(double), typeof(Window),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the WindowStartupLocation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WindowStartupLocationProperty =
        DependencyProperty.Register(nameof(WindowStartupLocation), typeof(WindowStartupLocation), typeof(Window),
            new PropertyMetadata(WindowStartupLocation.Manual));

    /// <summary>
    /// Identifies the ShowInTaskbar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowInTaskbarProperty =
        DependencyProperty.Register(nameof(ShowInTaskbar), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnShowInTaskbarChanged));

    /// <summary>
    /// Identifies the ShowActivated dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowActivatedProperty =
        DependencyProperty.Register(nameof(ShowActivated), typeof(bool), typeof(Window),
            new PropertyMetadata(true));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? "Window");
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the window state.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public WindowState WindowState
    {
        get => (WindowState)GetValue(WindowStateProperty)!;
        set => SetValue(WindowStateProperty, value);
    }

    /// <summary>
    /// Gets or sets the title bar style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public WindowTitleBarStyle TitleBarStyle
    {
        get => (WindowTitleBarStyle)(GetValue(TitleBarStyleProperty) ?? WindowTitleBarStyle.Custom);
        set => SetValue(TitleBarStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the system backdrop type for the window.
    /// This blurs content behind the window (desktop, other applications) using Windows DWM APIs.
    /// Requires Windows 11 22H2+ for Mica and Acrylic effects.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public WindowBackdropType SystemBackdrop
    {
        get => (WindowBackdropType)(GetValue(SystemBackdropProperty) ?? WindowBackdropType.None);
        set => SetValue(SystemBackdropProperty, value);
    }

    /// <summary>
    /// Gets the native window handle.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// The cross-platform window implementation (null on Windows, which uses
    /// the existing Win32 code path directly).
    /// </summary>
    private IPlatformWindow? _platformWindow;

    /// <summary>
    /// Gets the render target for this window.
    /// </summary>
    public RenderTarget? RenderTarget { get; private set; }

    /// <summary>
    /// Gets the DPI scale factor for this window (1.0 = 96 DPI = 100%).
    /// </summary>
    public double DpiScale => _dpiScale;

    /// <summary>
    /// Gets the safe area insets (in DIPs) for notch/cutout/status bar avoidance on mobile.
    /// </summary>
    public Thickness SafeAreaInsets => _safeAreaInsets;

    /// <summary>
    /// Gets whether the soft keyboard is currently visible.
    /// </summary>
    public bool IsSoftKeyboardVisible => _softKeyboardVisible;

    /// <summary>
    /// Gets the soft keyboard height in DIPs (0 when hidden).
    /// </summary>
    public double SoftKeyboardHeight => _softKeyboardVisible ? _softKeyboardHeight : 0;

    /// <summary>
    /// Gets the current device orientation.
    /// </summary>
    public DeviceOrientation DeviceOrientation => _deviceOrientation;

    /// <summary>Raised when safe area insets change.</summary>
    public event EventHandler? SafeAreaInsetsChanged;

    /// <summary>Raised when soft keyboard visibility or height changes.</summary>
    public event EventHandler? SoftKeyboardVisibilityChanged;

    /// <summary>Raised when device orientation changes.</summary>
    public event EventHandler? OrientationChanged;

    /// <summary>
    /// Gets the overlay layer for hosting popup content within the window's visual tree.
    /// </summary>
    internal OverlayLayer OverlayLayer { get; private set; }

    /// <summary>
    /// Gets the adorner layer that hosts keyboard focus indicators and other decorations
    /// targeting elements in this window. Positioned above all content but below popups.
    /// </summary>
    public AdornerLayer? AdornerLayer { get; private set; }

    /// <summary>
    /// Gets or sets the TaskbarItemInfo object that provides taskbar integration features.
    /// </summary>
    public TaskbarItemInfo? TaskbarItemInfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this window appears on top of all other windows.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool Topmost
    {
        get => (bool)GetValue(TopmostProperty)!;
        set => SetValue(TopmostProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window automatically sizes itself to fit the size of its content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public SizeToContent SizeToContent
    {
        get => (SizeToContent)GetValue(SizeToContentProperty)!;
        set => SetValue(SizeToContentProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window can be resized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ResizeMode ResizeMode
    {
        get => (ResizeMode)GetValue(ResizeModeProperty)!;
        set => SetValue(ResizeModeProperty, value);
    }

    /// <summary>
    /// Gets or sets a window's border style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public WindowStyle WindowStyle
    {
        get => (WindowStyle)GetValue(WindowStyleProperty)!;
        set => SetValue(WindowStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the content rendered on the left side of the title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FrameworkElement? LeftWindowCommands
    {
        get => (FrameworkElement?)GetValue(LeftWindowCommandsProperty);
        set => SetValue(LeftWindowCommandsProperty, value);
    }

    /// <summary>
    /// Gets or sets the content rendered on the right side of the title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FrameworkElement? RightWindowCommands
    {
        get => (FrameworkElement?)GetValue(RightWindowCommandsProperty);
        set => SetValue(RightWindowCommandsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the title bar icon is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowIcon
    {
        get => (bool)GetValue(IsShowIconProperty)!;
        set => SetValue(IsShowIconProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the title text is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowTitle
    {
        get => (bool)GetValue(IsShowTitleProperty)!;
        set => SetValue(IsShowTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the custom title bar is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowTitleBar
    {
        get => (bool)GetValue(IsShowTitleBarProperty)!;
        set => SetValue(IsShowTitleBarProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the minimize button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowMinimizeButton
    {
        get => (bool)GetValue(IsShowMinimizeButtonProperty)!;
        set => SetValue(IsShowMinimizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the maximize button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowMaximizeButton
    {
        get => (bool)GetValue(IsShowMaximizeButtonProperty)!;
        set => SetValue(IsShowMaximizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the close button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowCloseButton
    {
        get => (bool)GetValue(IsShowCloseButtonProperty)!;
        set => SetValue(IsShowCloseButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the system menu (right-click menu on title bar) is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool HasSystemMenu
    {
        get => (bool)GetValue(HasSystemMenuProperty)!;
        set => SetValue(HasSystemMenuProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size of the custom title bar text, in DIPs.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double TitleBarFontSize
    {
        get => (double)GetValue(TitleBarFontSizeProperty)!;
        set => SetValue(TitleBarFontSizeProperty, value);
    }

    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty)!;
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon displayed in the custom title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ImageSource? WindowIcon
    {
        get => (ImageSource?)GetValue(WindowIconProperty);
        set => SetValue(WindowIconProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the window's left edge, in DIPs, relative to the desktop.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Left
    {
        get => (double)GetValue(LeftProperty)!;
        set => SetValue(LeftProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the window's top edge, in DIPs, relative to the desktop.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Top
    {
        get => (double)GetValue(TopProperty)!;
        set => SetValue(TopProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the window when first shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public WindowStartupLocation WindowStartupLocation
    {
        get => (WindowStartupLocation)GetValue(WindowStartupLocationProperty)!;
        set => SetValue(WindowStartupLocationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window has a task bar button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool ShowInTaskbar
    {
        get => (bool)GetValue(ShowInTaskbarProperty)!;
        set => SetValue(ShowInTaskbarProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window is activated when first shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool ShowActivated
    {
        get => (bool)GetValue(ShowActivatedProperty)!;
        set => SetValue(ShowActivatedProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the window (0.0 to 1.0).
    /// When <see cref="AllowsTransparency"/> is <c>true</c>, this controls the native window opacity.
    /// </summary>
    public override double Opacity
    {
        get => base.Opacity;
        set
        {
            base.Opacity = value;
            ApplyLayeredWindowOpacity(value);
        }
    }

    /// <summary>
    /// Gets a value that indicates whether this window has been loaded (shown at least once).
    /// </summary>
    public bool IsLoaded => _contentRendered;

    /// <summary>
    /// Gets the size and location of a window before being either minimized or maximized.
    /// </summary>
    public Rect RestoreBounds => _restoreBounds;

    /// <summary>
    /// Gets a collection of windows that are owned by this window.
    /// </summary>
    public IReadOnlyList<Window> OwnedWindows => _ownedWindows;

    /// <summary>
    /// Gets or sets a value that indicates whether the window allows transparency.
    /// Must be set before the window is shown.
    /// </summary>
    public bool AllowsTransparency { get; set; }

    /// <summary>
    /// Gets or sets the window that owns this window.
    /// </summary>
    public Window? Owner
    {
        get => _owner;
        set
        {
            if (_owner == value) return;
            _owner?.RemoveOwnedWindow(this);
            _owner = value;
            _owner?.AddOwnedWindow(this);
        }
    }
    private Window? _owner;

    /// <summary>
    /// Gets or sets the dialog result value, which is the return value of the ShowDialog method.
    /// </summary>
    public bool? DialogResult { get; set; }

    #endregion

    #region Events

    public override event RoutedEventHandler? Loaded;
    public override event RoutedEventHandler? Unloaded;
    public override event SizeChangedEventHandler? SizeChanged;
    public event EventHandler<System.ComponentModel.CancelEventArgs>? Closing;
    public event EventHandler? Closed;
    public event EventHandler? LocationChanged;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler? StateChanged;
    public event EventHandler? ContentRendered;
    public event EventHandler? SourceInitialized;
    public event DpiChangedEventHandler? DpiChanged;
    public event EventHandler? SystemSettingsChanged;
    public event EventHandler<SessionEndingCancelEventArgs>? SessionEnding;
    public event EventHandler? Shown;
    public event EventHandler? Hiding;

    public bool IsActive => _isActive;

    #endregion

    #region Event Virtual Methods

    protected virtual void OnActivated(EventArgs e) => Activated?.Invoke(this, e);
    protected virtual void OnDeactivated(EventArgs e) => Deactivated?.Invoke(this, e);
    protected virtual void OnStateChanged(EventArgs e) => StateChanged?.Invoke(this, e);
    protected virtual void OnContentRendered(EventArgs e) => ContentRendered?.Invoke(this, e);
    protected virtual void OnSourceInitialized(EventArgs e) => SourceInitialized?.Invoke(this, e);
    protected virtual void OnLocationChanged(EventArgs e) => LocationChanged?.Invoke(this, e);
    protected virtual void OnClosing(System.ComponentModel.CancelEventArgs e) => Closing?.Invoke(this, e);
    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);
    protected virtual void OnDpiChanged(DpiChangedEventArgs e) => DpiChanged?.Invoke(this, e);
    protected virtual void OnSizeChanged(SizeChangedEventArgs e) => SizeChanged?.Invoke(this, e);
    protected virtual void OnSystemSettingsChanged(EventArgs e) => SystemSettingsChanged?.Invoke(this, e);
    protected virtual void OnSessionEnding(SessionEndingCancelEventArgs e) => SessionEnding?.Invoke(this, e);
    protected virtual void OnLoaded(RoutedEventArgs e) => Loaded?.Invoke(this, e);
    protected virtual void OnUnloaded(RoutedEventArgs e) => Unloaded?.Invoke(this, e);
    protected virtual void OnShown(EventArgs e) => Shown?.Invoke(this, e);
    protected virtual void OnHiding(EventArgs e) => Hiding?.Invoke(this, e);
    protected virtual bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) => false;
    protected virtual bool OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers) => false;
    protected virtual bool OnPreviewWindowMouseDown(MouseButton button, Point position, int clickCount) => false;
    protected virtual bool OnPreviewWindowMouseUp(MouseButton button, Point position) => false;
    protected virtual bool OnPreviewWindowMouseMove(Point position) => false;
    protected virtual bool OnPreviewWindowMouseWheel(int delta, Point position) => false;

    #endregion

    #region Base Class Overrides

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) => base.OnPropertyChanged(e);
    internal override object? GetLayoutClip() => null;
    public override string ToString() => $"Window: \"{Title}\" ({Width:F0}x{Height:F0})";

    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        InvalidateMeasure();
        RequestFullInvalidation();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        InvalidateMeasure();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (Handle != nint.Zero)
            EnableWindow(Handle, newValue);
    }

    protected override void OnDataContextChanged(object? oldValue, object? newValue)
    {
        base.OnDataContextChanged(oldValue, newValue);
        if (TitleBar != null)
            TitleBar.DataContext = newValue;
    }

    protected override void OnResourcesChanged()
    {
        base.OnResourcesChanged();
        if (Handle != nint.Zero)
        {
            EnsureImplicitStyles();
            InvalidateMeasure();
            RequestFullInvalidation();
        }
    }

    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        base.OnIsMouseOverChanged(oldValue, newValue);
        if (!newValue && TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();
    }

    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        RequestFullInvalidation();
    }

    public override Visibility Visibility
    {
        get => base.Visibility;
        set
        {
            base.Visibility = value;
            if (Handle == nint.Zero) return;
            if (_platformWindow != null)
            {
                if (value == Visibility.Visible) _platformWindow.Show();
                else _platformWindow.Hide();
            }
            else
            {
                _ = ShowWindow(Handle, value == Visibility.Visible
                    ? (ShowActivated ? SW_SHOW : SW_SHOWNOACTIVATE)
                    : SW_HIDE);
            }
        }
    }

    protected override void OnVisualChildrenChanged(Visual? visualAdded, Visual? visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        InvalidateMeasure();
    }

    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);
        RequestFullInvalidation();
    }

    protected override void OnIsFocusedChanged(bool oldValue, bool newValue)
    {
        base.OnIsFocusedChanged(oldValue, newValue);
        RequestFullInvalidation();
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();
    }

    protected override void OnBackdropEffectChanged(IBackdropEffect? oldValue, IBackdropEffect? newValue)
    {
        base.OnBackdropEffectChanged(oldValue, newValue);
        RequestFullInvalidation();
    }

    protected override void OnEffectChanged(object? oldValue, object? newValue)
    {
        base.OnEffectChanged(oldValue, newValue);
        RequestFullInvalidation();
    }

    #endregion


    /// <summary>
    /// Gets the title bar control. Only available when TitleBarStyle is Custom.
    /// </summary>
    public TitleBar? TitleBar { get; private set; }

    private const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, PointerStylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];
    private readonly RealTimeStylus _realTimeStylus;

    /// <summary>
    /// External popup windows that are currently open and owned by this window.
    /// Used for light-dismiss coordination.
    /// </summary>
    internal List<Popup> ActiveExternalPopups { get; } = [];

    /// <summary>
    /// Gets or sets the active modal content dialog hosted by this window (Popup mode only).
    /// </summary>
    internal ContentDialog? ActiveContentDialog { get; set; }

    /// <summary>
    /// Tracks in-place content dialogs that are currently open in this window.
    /// Multiple in-place dialogs can coexist because they each occupy their own
    /// position in the visual tree.
    /// </summary>
    internal List<ContentDialog> ActiveInPlaceDialogs { get; } = [];

    public Window()
    {
        _inputDispatcher = new WindowInputDispatcher(this);

        if (PlatformFactory.IsWindows)
            DragDropPlatform.EnsureInitialized();

        Width = 800;
        Height = 600;

        // Create adorner layer first so it sits below the popup layer in the visual order.
        // Adorners (including focus visuals) paint above content but must remain below
        // popups like dropdowns and ContextMenus.
        AdornerLayer = new AdornerLayer();
        AddVisualChild(AdornerLayer);

        // Create overlay layer for popup hosting (must be created before title bar)
        OverlayLayer = new OverlayLayer();
        AddVisualChild(OverlayLayer);

        // Debug HUD overlay (F3 to toggle, rendered as a normal control in the overlay layer)
        OverlayLayer.Children.Add(_debugHudOverlay);

        // Ensure keyboard focus visuals materialize as adorners whenever focus moves.
        FocusVisualManager.EnsureInitialized();

        _realTimeStylus = new RealTimeStylus(this);
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), handledEventsToo: true);
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), handledEventsToo: true);

        CreateTitleBar();
    }

    /// <summary>
    /// Recursively applies implicit styles to this window and all descendant elements
    /// that don't yet have a style applied. This ensures elements created before the
    /// theme was loaded (e.g., TitleBar) get properly styled before the window is shown.
    /// </summary>
    private void EnsureImplicitStyles()
    {
        ApplyImplicitStylesRecursive(this);

        static void ApplyImplicitStylesRecursive(Visual visual)
        {
            if (visual is FrameworkElement fe)
            {
                fe.ApplyImplicitStyleIfNeeded();
            }

            for (int i = 0; i < visual.VisualChildrenCount; i++)
            {
                var child = visual.GetVisualChild(i);
                if (child != null)
                    ApplyImplicitStylesRecursive(child);
            }
        }
    }

    private void CreateTitleBar()
    {
        if (TitleBarStyle != WindowTitleBarStyle.Custom)
        {
            return;
        }

        TitleBar = new TitleBar();
        ApplyTitleBarPresentation();

        TitleBar.MinimizeClicked += OnTitleBarMinimizeClicked;
        TitleBar.MaximizeRestoreClicked += OnTitleBarMaximizeRestoreClicked;
        TitleBar.CloseClicked += OnTitleBarCloseClicked;

        AddVisualChild(TitleBar);
    }

    private void RemoveTitleBar()
    {
        if (TitleBar != null)
        {
            TitleBar.MinimizeClicked -= OnTitleBarMinimizeClicked;
            TitleBar.MaximizeRestoreClicked -= OnTitleBarMaximizeRestoreClicked;
            TitleBar.CloseClicked -= OnTitleBarCloseClicked;

            RemoveVisualChild(TitleBar);
            TitleBar = null;
        }
    }

    private void ApplyTitleBarPresentation()
    {
        EnsureAutoWindowIcon();
        
        if (TitleBar == null)
        {
            return;
        }

        TitleBar.Height = GetEffectiveTitleBarHeightDip();
        TitleBar.FontSize = TitleBarFontSize;
        TitleBar.Title = Title;
        TitleBar.IsMaximized = WindowState == WindowState.Maximized;
        TitleBar.ShowMinimizeButton = IsShowMinimizeButton;
        TitleBar.ShowMaximizeButton = IsShowMaximizeButton;
        TitleBar.ShowCloseButton = IsShowCloseButton;
        TitleBar.LeftWindowCommands = LeftWindowCommands;
        TitleBar.RightWindowCommands = RightWindowCommands;
        TitleBar.IsShowIcon = IsShowIcon;
        TitleBar.IsShowTitle = IsShowTitle;
        TitleBar.WindowIcon = WindowIcon;
        TitleBar.Visibility = IsShowTitleBar ? Visibility.Visible : Visibility.Collapsed;

        if (!IsShowTitleBar)
        {
            ClearTitleBarInteractionState();
        }
    }

    private void ClearTitleBarInteractionState()
    {
        _inputDispatcher.ClearTitleBarInteractionState();
    }

    private void EnsureAutoWindowIcon()
    {
        if (WindowIcon != null || _attemptedAutoWindowIcon)
        {
            return;
        }

        _attemptedAutoWindowIcon = true;
        var icon = TryCreateDefaultWindowIcon();
        if (icon != null)
        {
            SetValue(WindowIconProperty, icon);
        }
    }

    private static ImageSource? TryCreateDefaultWindowIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !System.IO.File.Exists(processPath))
            {
                return null;
            }

            var pngBytes = Helpers.IconHelper.ExtractProcessIconAsPng(processPath);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return null;
            }

            return BitmapImage.FromBytes(pngBytes);
        }
        catch
        {
            return null;
        }
    }

    private void OnTitleBarMinimizeClicked(object? sender, EventArgs e)
    {
        if (Handle != nint.Zero)
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void OnTitleBarMaximizeRestoreClicked(object? sender, EventArgs e)
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnTitleBarCloseClicked(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Shows the system menu (right-click menu) at the specified screen coordinates.
    /// </summary>
    private void ShowSystemMenu(int screenX, int screenY)
    {
        if (!HasSystemMenu || Handle == nint.Zero)
            return;

        nint hMenu = GetSystemMenu(Handle, false);
        if (hMenu == nint.Zero)
            return;

        // Update menu item states to match current window state
        bool isMaximized = WindowState == WindowState.Maximized;
        bool isMinimized = WindowState == WindowState.Minimized;
        bool canResize = ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip;
        bool canMinimize = ResizeMode != ResizeMode.NoResize;

        EnableMenuItem(hMenu, SC_RESTORE, MF_BYCOMMAND | (isMaximized || isMinimized ? MF_ENABLED : MF_GRAYED));
        EnableMenuItem(hMenu, SC_MOVE, MF_BYCOMMAND | (isMaximized ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(hMenu, SC_SIZE, MF_BYCOMMAND | (isMaximized || !canResize ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(hMenu, SC_MINIMIZE, MF_BYCOMMAND | (canMinimize ? MF_ENABLED : MF_GRAYED));
        EnableMenuItem(hMenu, SC_MAXIMIZE, MF_BYCOMMAND | (!isMaximized && canResize ? MF_ENABLED : MF_GRAYED));

        // Set the default menu item (double-click action)
        SetMenuDefaultItem(hMenu, isMaximized ? SC_RESTORE : SC_MAXIMIZE, 0);

        int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_LEFTBUTTON, screenX, screenY, 0, Handle, nint.Zero);
        if (cmd != 0)
        {
            PostMessage(Handle, WM_SYSCOMMAND, (nint)cmd, nint.Zero);
        }
    }

    private int HandleNcHitTest(nint lParam)
    {
        // Get cursor position in screen coordinates (physical pixels)
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        // Check if window is maximized
        bool isMaximized = IsZoomed(Handle);

        // Convert screen coordinates to client-area coordinates (physical pixels).
        POINT pt = new() { X = screenX, Y = screenY };
        _ = ScreenToClient(Handle, ref pt);

        // Convert to DIPs for comparison with layout values.
        double x = pt.X / _dpiScale;
        double y = pt.Y / _dpiScale;

        return ComputeNcHitTestFromClientDip(x, y, isMaximized);
    }

    // Extracted for deterministic tests without a live HWND.
    internal int ComputeNcHitTestFromClientDip(double x, double y, bool isMaximized)
    {
        double windowWidth = Width;
        double windowHeight = Height;

        var titleBarHeight = GetCurrentTitleBarHeightDip();
        bool canResize = !isMaximized &&
            (ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip);
        const int resizeBorder = 6;

        bool isLeft = false;
        bool isRight = false;
        bool isTop = false;
        bool isBottom = false;

        if (canResize)
        {
            isLeft = x < resizeBorder;
            isRight = x >= windowWidth - resizeBorder;
            isTop = y < resizeBorder;
            isBottom = y >= windowHeight - resizeBorder;

            if (isTop && isLeft)
            {
                return HTTOPLEFT;
            }

            if (isTop && isRight)
            {
                return HTTOPRIGHT;
            }

            if (isBottom && isLeft)
            {
                return HTBOTTOMLEFT;
            }

            if (isBottom && isRight)
            {
                return HTBOTTOMRIGHT;
            }

            if (isLeft)
            {
                return HTLEFT;
            }

            if (isRight)
            {
                return HTRIGHT;
            }

            if (isTop)
            {
                return HTTOP;
            }

            if (isBottom)
            {
                return HTBOTTOM;
            }
        }

        if (!IsTitleBarVisible())
        {
            return HTCLIENT;
        }

        var button = GetTitleBarButtonAtPoint(new Point(x, y), windowWidth);
        if (button != null)
        {
            return button.Kind switch
            {
                TitleBarButtonKind.Minimize => HTMINBUTTON,
                TitleBarButtonKind.Maximize or TitleBarButtonKind.Restore => HTMAXBUTTON,
                TitleBarButtonKind.Close => HTCLOSE,
                _ => HTCLIENT
            };
        }

        if (IsTitleBarWindowCommandsHit(new Point(x, y)))
        {
            return HTCLIENT;
        }

        if (y < titleBarHeight)
        {
            return HTCAPTION;
        }

        return HTCLIENT;
    }

    private bool IsTitleBarVisible()
    {
        return TitleBarStyle == WindowTitleBarStyle.Custom &&
               IsShowTitleBar &&
               TitleBar != null &&
               TitleBar.Visibility == Visibility.Visible;
    }

    private double GetEffectiveTitleBarHeightDip()
    {
        double height = TitleBarHeight;
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
        {
            return DefaultTitleBarHeightDip;
        }

        return height;
    }

    private double GetCurrentTitleBarHeightDip()
    {
        if (!IsTitleBarVisible())
        {
            return 0;
        }

        if (TitleBar == null)
        {
            return GetEffectiveTitleBarHeightDip();
        }

        return GetElementHeightDip(TitleBar, GetEffectiveTitleBarHeightDip());
    }

    private bool IsTitleBarWindowCommandsHit(Point point)
    {
        if (!IsTitleBarVisible() || TitleBar == null)
        {
            return false;
        }

        var titleBarBounds = TitleBar.VisualBounds;
        var localPoint = new Point(point.X - titleBarBounds.X, point.Y - titleBarBounds.Y);
        return TitleBar.IsPointInWindowCommands(localPoint);
    }

    private TitleBarButton? GetTitleBarButtonAtPoint(Point point, double windowWidth = 0)
    {
        if (!IsTitleBarVisible() || TitleBar == null)
        {
            return null;
        }

        var titleBarBounds = TitleBar.VisualBounds;

        // Convert to TitleBar-local coordinates so they can be compared with button VisualBounds.
        var localPoint = new Point(point.X - titleBarBounds.X, point.Y - titleBarBounds.Y);

        var closeButton = GetTitleBarButtonByKind(TitleBarButtonKind.Close);
        if (IsTitleBarButtonHit(localPoint, closeButton))
        {
            return closeButton;
        }

        var maximizeButton = GetTitleBarButtonByKind(TitleBar.IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize);
        if (IsTitleBarButtonHit(localPoint, maximizeButton))
        {
            return maximizeButton;
        }

        var minimizeButton = GetTitleBarButtonByKind(TitleBarButtonKind.Minimize);
        if (IsTitleBarButtonHit(localPoint, minimizeButton))
        {
            return minimizeButton;
        }

        // Fallback for cases before first arrange: use current button widths,
        // not a hardcoded value, so hit-test math stays aligned with layout.
        var titleBarWidth = windowWidth > 0
            ? windowWidth
            : (TitleBar.ActualWidth > 0 ? TitleBar.ActualWidth : Width);

        return GetTitleBarButtonByWidthFallback(localPoint, titleBarWidth, closeButton, maximizeButton, minimizeButton);
    }

    private static bool IsTitleBarButtonHit(Point localPoint, TitleBarButton? button)
    {
        if (button == null || button.Visibility != Visibility.Visible)
        {
            return false;
        }

        var bounds = button.VisualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        // Use the largest known dimension so styled Width/Height can expand NC hit-testing
        // even when layout is temporarily constrained to native caption metrics.
        double width = Math.Max(bounds.Width, GetTitleBarButtonHitWidth(button));
        double height = Math.Max(bounds.Height, GetTitleBarButtonHitHeight(button));

        return localPoint.X >= bounds.X &&
               localPoint.X < bounds.X + width &&
               localPoint.Y >= bounds.Y &&
               localPoint.Y < bounds.Y + height;
    }

    private TitleBarButton? GetTitleBarButtonByWidthFallback(
        Point localPoint,
        double titleBarWidth,
        TitleBarButton? closeButton,
        TitleBarButton? maximizeButton,
        TitleBarButton? minimizeButton)
    {
        double buttonX = titleBarWidth;

        if (TitleBar!.ShowCloseButton && closeButton != null)
        {
            var closeWidth = GetTitleBarButtonHitWidth(closeButton);
            var closeHeight = GetTitleBarButtonHitHeight(closeButton);
            buttonX -= closeWidth;
            if (localPoint.X >= buttonX && localPoint.X < buttonX + closeWidth &&
                localPoint.Y >= 0 && localPoint.Y < closeHeight)
            {
                return closeButton;
            }
        }

        if (TitleBar.ShowMaximizeButton && maximizeButton != null)
        {
            var maximizeWidth = GetTitleBarButtonHitWidth(maximizeButton);
            var maximizeHeight = GetTitleBarButtonHitHeight(maximizeButton);
            buttonX -= maximizeWidth;
            if (localPoint.X >= buttonX && localPoint.X < buttonX + maximizeWidth &&
                localPoint.Y >= 0 && localPoint.Y < maximizeHeight)
            {
                return maximizeButton;
            }
        }

        if (TitleBar.ShowMinimizeButton && minimizeButton != null)
        {
            var minimizeWidth = GetTitleBarButtonHitWidth(minimizeButton);
            var minimizeHeight = GetTitleBarButtonHitHeight(minimizeButton);
            buttonX -= minimizeWidth;
            if (localPoint.X >= buttonX && localPoint.X < buttonX + minimizeWidth &&
                localPoint.Y >= 0 && localPoint.Y < minimizeHeight)
            {
                return minimizeButton;
            }
        }

        return null;
    }

    private static double GetTitleBarButtonHitWidth(TitleBarButton button)
    {
        double width = 0;

        if (button.ActualWidth > 0)
        {
            width = Math.Max(width, button.ActualWidth);
        }

        if (button.DesiredSize.Width > 0)
        {
            width = Math.Max(width, button.DesiredSize.Width);
        }

        if (!double.IsNaN(button.Width) && button.Width > 0)
        {
            width = Math.Max(width, button.Width);
        }

        return width > 0 ? width : 46.0;
    }

    private static double GetTitleBarButtonHitHeight(TitleBarButton button)
    {
        double height = 0;

        if (button.ActualHeight > 0)
        {
            height = Math.Max(height, button.ActualHeight);
        }

        if (button.DesiredSize.Height > 0)
        {
            height = Math.Max(height, button.DesiredSize.Height);
        }

        if (!double.IsNaN(button.Height) && button.Height > 0)
        {
            height = Math.Max(height, button.Height);
        }

        return height > 0 ? height : DefaultTitleBarHeightDip;
    }

    private TitleBarButton? GetTitleBarButtonByKind(TitleBarButtonKind kind)
    {
        return TitleBar?.GetButtonByKind(kind);
    }

    private TitleBarButton? GetTitleBarButtonByNcHit(int hitTest)
    {
        return hitTest switch
        {
            HTMINBUTTON => GetTitleBarButtonByKind(TitleBarButtonKind.Minimize),
            HTMAXBUTTON => GetTitleBarButtonByKind(TitleBar?.IsMaximized == true ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize),
            HTCLOSE => GetTitleBarButtonByKind(TitleBarButtonKind.Close),
            _ => null
        };
    }

    private static bool IsNcHitMatchingButtonKind(int hitTest, TitleBarButtonKind kind)
    {
        return (hitTest == HTMINBUTTON && kind == TitleBarButtonKind.Minimize) ||
               (hitTest == HTMAXBUTTON && (kind == TitleBarButtonKind.Maximize || kind == TitleBarButtonKind.Restore)) ||
               (hitTest == HTCLOSE && kind == TitleBarButtonKind.Close);
    }

    private static bool IsCaptionButtonNcHit(int hitTest)
    {
        return hitTest == HTMINBUTTON || hitTest == HTMAXBUTTON || hitTest == HTCLOSE;
    }

    private void OnNcMouseMove(nint wParam, nint lParam)
    {
        _ = wParam;

        if (!IsTitleBarVisible())
        {
            _inputDispatcher.UpdateTitleBarButtonHover(null);
            return;
        }

        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale));
        _inputDispatcher.UpdateTitleBarButtonHover(button);

        // Only request TME_LEAVE so the button hover state can be cleared when
        // the cursor exits the NC area. Do NOT request TME_HOVER here — that
        // would continually reset the DefWindowProc-owned hover timer that
        // Windows 11 uses to arm the Snap Layouts flyout. DefWindowProc will
        // register its own hover tracking via the standard message flow.
        TRACKMOUSEEVENT tme = new()
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE | TME_NONCLIENT,
            hwndTrack = Handle,
            dwHoverTime = HOVER_DEFAULT
        };
        _ = TrackMouseEvent(ref tme);
    }

    private void OnNcMouseLeave()
    {
        _inputDispatcher.UpdateTitleBarButtonHover(null);
    }

    private bool TryInjectSnapProxyNcMouseMessage(uint msg, nint lParam)
    {
        _ = msg;
        _ = lParam;
        // Disabled: synthetic NC proxy routing proved unstable
        // (button click regressions and resize jitter) and did not reliably
        // improve Snap flyout behavior across custom caption geometries.
        return false;
    }

    private bool TryBuildMaxButtonProxyLParam(
        nint lParam,
        out nint proxyLParam,
        out (int x, int y) realScreenPoint,
        out (int x, int y) proxyScreenPoint)
    {
        proxyLParam = nint.Zero;
        realScreenPoint = default;
        proxyScreenPoint = default;

        if (!ShouldUseWin11SnapNcRouting() || Handle == nint.Zero)
        {
            return false;
        }

        realScreenPoint = UnpackScreenPointFromLParam(lParam);
        if (!TryGetCustomMaxButtonScreenBounds(out var customMaxRect) ||
            !ContainsPoint(customMaxRect, realScreenPoint))
        {
            return false;
        }

        if (!TryGetDwmMaxButtonBounds(out var dwmMaxRect) ||
            !TryBuildMaxButtonProxyScreenPoint(realScreenPoint, customMaxRect, dwmMaxRect, out proxyScreenPoint))
        {
            return false;
        }

        proxyLParam = PackScreenPointToLParam(proxyScreenPoint);
        return true;
    }


    private bool TryGetDwmMaxButtonBounds(out (int left, int top, int right, int bottom) dwmMaxRect)
    {
        dwmMaxRect = default;
        if (!TryGetDwmCaptionButtonBounds(Handle, out var captionBounds))
        {
            return false;
        }

        bool showMinimize = TitleBar?.ShowMinimizeButton ?? true;
        bool showMaximize = TitleBar?.ShowMaximizeButton ?? true;
        bool showClose = TitleBar?.ShowCloseButton ?? true;
        return TryGetDwmMaxButtonBounds(captionBounds, showMinimize, showMaximize, showClose, out dwmMaxRect);
    }

    private bool TryGetCustomMaxButtonScreenBounds(out (int left, int top, int right, int bottom) customMaxRect)
    {
        customMaxRect = default;
        if (Handle == nint.Zero || TitleBarStyle != WindowTitleBarStyle.Custom || TitleBar == null)
        {
            return false;
        }

        var maxButton = GetTitleBarButtonByKind(TitleBar.IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize);
        if (!TryGetCustomMaxButtonClientBoundsDip(maxButton, out var customMaxClientRectDip))
        {
            return false;
        }

        POINT clientOrigin = new() { X = 0, Y = 0 };
        if (!ClientToScreen(Handle, ref clientOrigin))
        {
            return false;
        }

        return TryGetCustomMaxButtonScreenBounds(customMaxClientRectDip, _dpiScale, (clientOrigin.X, clientOrigin.Y), out customMaxRect);
    }

    private bool TryGetCustomMaxButtonClientBoundsDip(
        TitleBarButton? maxButton,
        out (double left, double top, double right, double bottom) clientRect)
    {
        clientRect = default;
        if (TitleBar == null || maxButton == null || maxButton.Visibility != Visibility.Visible)
        {
            return false;
        }

        var titleBarBounds = TitleBar.VisualBounds;
        var buttonBounds = maxButton.VisualBounds;
        double left;
        double top;
        double width;
        double height;

        if (buttonBounds.Width > 0 && buttonBounds.Height > 0)
        {
            left = titleBarBounds.X + buttonBounds.X;
            top = titleBarBounds.Y + buttonBounds.Y;
            width = buttonBounds.Width;
            height = buttonBounds.Height;
        }
        else
        {
            double titleBarWidth = TitleBar.ActualWidth > 0
                ? TitleBar.ActualWidth
                : (TitleBar.DesiredSize.Width > 0 ? TitleBar.DesiredSize.Width : Width);
            if (titleBarWidth <= 0)
            {
                return false;
            }

            double buttonX = titleBarBounds.X + titleBarWidth;
            if (TitleBar.ShowCloseButton)
            {
                var closeButton = GetTitleBarButtonByKind(TitleBarButtonKind.Close);
                if (closeButton != null && closeButton.Visibility == Visibility.Visible)
                {
                    buttonX -= GetTitleBarButtonHitWidth(closeButton);
                }
            }

            width = GetTitleBarButtonHitWidth(maxButton);
            height = GetTitleBarButtonHitHeight(maxButton);
            buttonX -= width;
            left = buttonX;
            top = titleBarBounds.Y;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        clientRect = (left, top, left + width, top + height);
        return true;
    }

    private static (int x, int y) UnpackScreenPointFromLParam(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return (x, y);
    }

    private static nint PackScreenPointToLParam((int x, int y) point)
    {
        ushort x = unchecked((ushort)(short)point.x);
        ushort y = unchecked((ushort)(short)point.y);
        int packed = x | (y << 16);
        return new nint(packed);
    }

    private static bool ContainsPoint((int left, int top, int right, int bottom) rect, (int x, int y) point)
    {
        return point.x >= rect.left && point.x < rect.right && point.y >= rect.top && point.y < rect.bottom;
    }

    private bool OnNcLButtonDown(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
        {
            return false;
        }

        // Use actual pointer position instead of relying on wParam hit-test.
        // During/after resize, NC hit-test values can drift while the button
        // geometry is still correct, which leads to hover working but clicks ignored.
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        int hitTest = (int)wParam.ToInt64();
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale)) ??
                     GetTitleBarButtonByNcHit(hitTest);
        if (button == null)
        {
            // Not on a custom caption button: let Windows handle caption drag/resize.
            return false;
        }

        _inputDispatcher.PressedTitleBarButtonField = button;
        button.SetIsPressed(true);
        return true;
    }

    private bool OnNcLButtonUp(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
            return false;

        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        int hitTest = (int)wParam.ToInt64();
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale)) ??
                     GetTitleBarButtonByNcHit(hitTest);

        var pressedButton = _inputDispatcher.PressedTitleBarButtonField;
        if (pressedButton != null)
        {
            pressedButton.SetIsPressed(false);

            bool isReleaseOnPressedButton = button == pressedButton ||
                                            (button == null && IsNcHitMatchingButtonKind(hitTest, pressedButton.Kind));
            if (isReleaseOnPressedButton)
            {
                switch (pressedButton.Kind)
                {
                    case TitleBarButtonKind.Minimize:
                        TitleBar?.RaiseMinimizeClicked();
                        break;
                    case TitleBarButtonKind.Maximize:
                    case TitleBarButtonKind.Restore:
                        TitleBar?.RaiseMaximizeRestoreClicked();
                        break;
                    case TitleBarButtonKind.Close:
                        TitleBar?.RaiseCloseClicked();
                        break;
                }
            }

            _inputDispatcher.PressedTitleBarButtonField = null;
            return true;
        }

        return false;
    }

    private bool OnNcLButtonDblClk(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
        {
            return false;
        }

        int hitTest = (int)wParam.ToInt64();

        // Get cursor position (physical pixels) 鈫?client 鈫?DIPs
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        // If over a button, don't handle double-click as caption maximize.
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale));
        if (button != null)
        {
            return true;
        }

        if (hitTest != HTCAPTION)
        {
            return false;
        }

        // Double-click on title bar (caption area) to maximize/restore
        if (hitTest == HTCAPTION && TitleBar != null && TitleBar.ShowMaximizeButton)
        {
            TitleBar.RaiseMaximizeRestoreClicked();
            return true;
        }

        return false;
    }

    private void UpdateTitleBarButtonHover(TitleBarButton? newHoveredButton)
    {
        _inputDispatcher.UpdateTitleBarButtonHover(newHoveredButton);
    }

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            int count = base.VisualChildrenCount;
            if (TitleBar != null) count++;
            count++; // AdornerLayer is always present
            count++; // OverlayLayer is always present
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        // Order: ContentElement(s) → TitleBar → AdornerLayer → OverlayLayer
        // (last = rendered on top, hit-tested first). Adorners paint above content but
        // below popups so that dropdowns and context menus naturally cover focus rects.
        int baseCount = base.VisualChildrenCount;

        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        int extra = index - baseCount;

        if (TitleBar != null)
        {
            if (extra == 0) return TitleBar;
            if (extra == 1) return AdornerLayer;
            if (extra == 2) return OverlayLayer;
        }
        else
        {
            if (extra == 0) return AdornerLayer;
            if (extra == 1) return OverlayLayer;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double titleBarHeight = 0;

        // Apply safe area insets and soft keyboard on mobile platforms
        double safeLeft = _safeAreaInsets.Left;
        double safeTop = _safeAreaInsets.Top;
        double safeRight = _safeAreaInsets.Right;
        double safeBottom = _safeAreaInsets.Bottom;
        if (_softKeyboardVisible && _softKeyboardHeight > safeBottom)
            safeBottom = _softKeyboardHeight;

        double contentWidth = Math.Max(0, availableSize.Width - safeLeft - safeRight);
        double contentHeight = Math.Max(0, availableSize.Height - safeTop - safeBottom);

        // Measure title bar
        if (IsTitleBarVisible() && TitleBar != null)
        {
            double effectiveTitleBarHeight = GetEffectiveTitleBarHeightDip();
            TitleBar.Measure(new Size(contentWidth, effectiveTitleBarHeight));
            titleBarHeight = GetElementHeightDip(TitleBar, effectiveTitleBarHeight);
        }

        // Measure content with remaining space
        var contentElement = ContentElement;
        if (contentElement != null)
        {
            Size contentAvailable = new(
                contentWidth,
                Math.Max(0, contentHeight - titleBarHeight));
            contentElement.Measure(contentAvailable);
        }

        // Measure adorner and overlay layers with full window size (they don't consume space)
        AdornerLayer?.Measure(availableSize);
        OverlayLayer?.Measure(availableSize);

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double titleBarHeight = 0;

        // Apply safe area insets and soft keyboard on mobile platforms
        double safeLeft = _safeAreaInsets.Left;
        double safeTop = _safeAreaInsets.Top;
        double safeRight = _safeAreaInsets.Right;
        double safeBottom = _safeAreaInsets.Bottom;
        if (_softKeyboardVisible && _softKeyboardHeight > safeBottom)
            safeBottom = _softKeyboardHeight;

        double contentWidth = Math.Max(0, finalSize.Width - safeLeft - safeRight);
        double contentHeight = Math.Max(0, finalSize.Height - safeTop - safeBottom);

        // Arrange title bar at top (offset by safe area)
        if (IsTitleBarVisible() && TitleBar != null)
        {
            titleBarHeight = GetCurrentTitleBarHeightDip();
            Rect titleBarRect = new(safeLeft, safeTop, contentWidth, titleBarHeight);
            TitleBar.Arrange(titleBarRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange content below title bar (offset by safe area)
        var contentElement = ContentElement;
        if (contentElement is FrameworkElement contentFe)
        {
            Rect contentRect = new(
                safeLeft,
                safeTop + titleBarHeight,
                contentWidth,
                Math.Max(0, contentHeight - titleBarHeight));
            contentFe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange adorner and overlay layers over the full window area. AdornerLayer is
        // forced to re-arrange every Window arrange pass because its adorners track
        // descendants whose positions can change (e.g. scrolling, animations) without
        // altering the AdornerLayer's own final rect. Without this invalidation, the
        // framework's "same rect, already valid → skip" short-circuit would leave focus
        // rings stranded at their old locations.
        AdornerLayer!.InvalidateArrange();
        AdornerLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        OverlayLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));

        // Keep DWM non-client hover tracking region aligned with current title bar/button geometry.
        UpdateCustomTitleBarFrameMargins();

        return finalSize;
    }

    #endregion

    /// <summary>
    /// Shows the window.
    /// </summary>
    public virtual void Show()
    {
        // Ensure implicit styles are applied to the entire visual tree.
        // This handles the case where elements (e.g., TitleBar) were created in the
        // Window constructor BEFORE the theme was loaded by the Xaml module initializer.
        // In non-AOT mode, the theme loads lazily when XamlReader is first accessed
        // (during InitializeComponent), but TitleBar is created earlier in Window().
        EnsureImplicitStyles();

        _dispatcher = Dispatcher.CurrentDispatcher;
        CompositionTarget.FrameStarting += OnFrameStarting;

        // Capture desired state before EnsureHandle, because Win32 calls inside
        // EnsureHandle (SetWindowPos for DPI / frame-change) can trigger WM_SIZE
        // which resets WindowState back to Normal.
        var desiredState = WindowState;

        EnsureHandle();

        // Detect monitor refresh rate and update CompositionTarget for adaptive frame rate
        var refreshRate = DetectMonitorRefreshRate();
        CompositionTarget.UpdateRefreshRate(refreshRate);

        // Apply startup location before showing
        ApplyWindowStartupLocation();

        var showCmd = desiredState switch
        {
            WindowState.Maximized => SW_MAXIMIZE,
            WindowState.Minimized => SW_MINIMIZE,
            WindowState.FullScreen => ShowActivated ? SW_SHOW : SW_SHOWNOACTIVATE,
            _ => ShowActivated ? SW_SHOW : SW_SHOWNOACTIVATE
        };

        // Restore the desired state in case EnsureHandle's WM_SIZE overwrote it.
        if (WindowState != desiredState)
        {
            _isSyncingWindowState = true;
            try { WindowState = desiredState; }
            finally { _isSyncingWindowState = false; }
        }
        if (_platformWindow != null)
        {
            _platformWindow.Show();
            if (desiredState == WindowState.Maximized || desiredState == WindowState.FullScreen)
                _platformWindow.SetState(WindowState.Maximized);
        }
        else
        {
            _ = ShowWindow(Handle, showCmd);
            // Fullscreen needs a second step on Win32: strip the frame + resize
            // to cover the monitor. Done AFTER ShowWindow so the HWND has valid
            // window rect / monitor assignment.
            if (desiredState == WindowState.FullScreen)
            {
                EnterFullScreen();
            }
        }

        // SWP_FRAMECHANGED for custom title bar was already applied in EnsureHandle
        // (combined with DPI adjustment), so no additional call is needed here.
        // Removing the duplicate saves a DWM roundtrip (~10-50ms).

        // Render the first frame. UpdateWindow is intentionally omitted —
        // ForceRenderFrame already performs a synchronous full render (layout +
        // draw + present), and the preceding ShowWindow triggers a WM_PAINT
        // that RenderFrame would handle.  Calling both caused two redundant
        // full renders, adding 50-200ms of blocking time.
        ForceRenderFrame();

        OnLoaded(new RoutedEventArgs());

        if (!_contentRendered)
        {
            _contentRendered = true;
            OnContentRendered(EventArgs.Empty);
        }

        OnShown(EventArgs.Empty);
    }

    /// <summary>
    /// Hides the window.
    /// </summary>
    public virtual void Hide()
    {
        OnHiding(EventArgs.Empty);

        if (Handle != nint.Zero)
        {
            if (_platformWindow != null)
                _platformWindow.Hide();
            else
                _ = ShowWindow(Handle, SW_HIDE);
        }
    }

    /// <summary>
    /// Attempts to bring the window to the foreground and activates it.
    /// </summary>
    /// <returns><c>true</c> if the window was successfully activated.</returns>
    public virtual bool Activate()
    {
        if (Handle == nint.Zero)
        {
            return false;
        }

        if (_platformWindow != null)
        {
            if (WindowState == WindowState.Minimized)
                _platformWindow.SetState(WindowState.Normal);
            _platformWindow.Show();
            return true;
        }

        // Win32 path
        if (WindowState == WindowState.Minimized)
        {
            _ = ShowWindow(Handle, SW_RESTORE);
        }

        return SetForegroundWindow(Handle);
    }

    /// <summary>
    /// Allows a window to be dragged by a mouse with its left button down over an exposed area of the window's client area.
    /// </summary>
    public virtual void DragMove()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        // Release mouse capture so the system can take over
        UIElement.ForceReleaseMouseCapture();

        if (_platformWindow != null)
        {
            // Cross-platform: drag not directly supported by native platform lib yet
            // TODO: Implement platform-specific drag move
            return;
        }

        _ = ReleaseCapture();
        // Send WM_NCLBUTTONDOWN with HTCAPTION to initiate a window drag
        _ = SendMessage(Handle, WM_NCLBUTTONDOWN, (nint)HTCAPTION, nint.Zero);
    }

    /// <summary>
    /// Centers the window on the screen of the current monitor.
    /// </summary>
    public void CenterOnScreen()
    {
        if (Handle == nint.Zero || _platformWindow != null) return;
        var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref monitorInfo))
        {
            int screenW = monitorInfo.rcWork.right - monitorInfo.rcWork.left;
            int screenH = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top;
            int winW = (int)(Width * _dpiScale);
            int winH = (int)(Height * _dpiScale);
            int x = monitorInfo.rcWork.left + (screenW - winW) / 2;
            int y = monitorInfo.rcWork.top + (screenH - winH) / 2;
            _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Centers the window relative to its owner window.
    /// </summary>
    public void CenterOnOwner()
    {
        if (Handle == nint.Zero || _platformWindow != null) return;
        nint ownerHwnd = Owner?.Handle ?? nint.Zero;
        if (ownerHwnd == nint.Zero) return;
        if (GetWindowRect(ownerHwnd, out RECT ownerRect))
        {
            int ownerW = ownerRect.right - ownerRect.left;
            int ownerH = ownerRect.bottom - ownerRect.top;
            int winW = (int)(Width * _dpiScale);
            int winH = (int)(Height * _dpiScale);
            int x = ownerRect.left + (ownerW - winW) / 2;
            int y = ownerRect.top + (ownerH - winH) / 2;
            _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    private bool _isModal;

    /// <summary>
    /// Opens a window and returns only when the newly opened window is closed.
    /// </summary>
    public virtual bool? ShowDialog()
    {
        DialogResult = null;

        if (_platformWindow != null)
        {
            // Cross-platform modal dialog: show window and block via Dispatcher
            Show();
            _isModal = true;
            try
            {
                while (_isModal && Handle != nint.Zero)
                {
                    // Poll platform events + process dispatcher queue
                    Interop.NativeMethods.PlatformPollEvents();
                    _dispatcher?.ProcessQueue();
                    Thread.Sleep(1);
                }
            }
            finally
            {
                _isModal = false;
            }
            return DialogResult;
        }

        // Win32 modal dialog path
        nint ownerHandle = DialogOwnerResolver.Resolve(Owner?.Handle ?? nint.Zero);
        if (ownerHandle == Handle)
        {
            ownerHandle = nint.Zero;
        }

        if (ownerHandle != nint.Zero)
        {
            EnableWindow(ownerHandle, false);
        }

        Show();

        _isModal = true;
        try
        {
            while (_isModal && Handle != nint.Zero)
            {
                if (GetMessage(out MSG msg, nint.Zero, 0, 0) > 0)
                {
                    _ = TranslateMessage(ref msg);
                    _ = DispatchMessage(ref msg);
                }
                else
                {
                    break;
                }

                if (Handle == nint.Zero)
                {
                    _isModal = false;
                    break;
                }
            }
        }
        finally
        {
            _isModal = false;

            if (ownerHandle != nint.Zero)
            {
                EnableWindow(ownerHandle, true);
                SetForegroundWindow(ownerHandle);
            }
        }

        return DialogResult;
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    private bool _isClosing;
    private bool _isSyncingWindowState;

    public virtual void Close()
    {
        if (_isClosing) return;
        _isClosing = true;

        // Exit modal loop if ShowDialog is waiting
        _isModal = false;

        CompositionTarget.FrameStarting -= OnFrameStarting;

        var closingArgs = new System.ComponentModel.CancelEventArgs();
        OnClosing(closingArgs);
        if (closingArgs.Cancel)
        {
            _isClosing = false;
            CompositionTarget.FrameStarting += OnFrameStarting;
            return;
        }

        StopRenderRecoveryRetry();

        if (ActiveContentDialog != null)
        {
            ActiveContentDialog.OnHostWindowClosed();
            ActiveContentDialog = null;
        }

        foreach (var inPlaceDialog in ActiveInPlaceDialogs.ToList())
        {
            inPlaceDialog.OnHostWindowClosed();
        }
        ActiveInPlaceDialogs.Clear();

        // Close all external popup windows
        foreach (var popup in ActiveExternalPopups.ToList())
            popup.IsOpen = false;
        ActiveExternalPopups.Clear();

        // Close all owned windows
        foreach (var owned in _ownedWindows.ToList())
            owned.Close();
        _ownedWindows.Clear();

        // Detach from owner
        _owner?.RemoveOwnedWindow(this);
        _owner = null;

        // Release large image resources before dropping the drawing context reference.
        // Avoid full ClearCache() here because text/brush teardown order can still be sensitive
        // during process shutdown.
        _drawingContext?.ClearBitmapCache();
        _drawingContext = null;

        // Dispose render target
        RenderTarget?.Dispose();
        RenderTarget = null;

        if (Handle != nint.Zero)
        {
            if (PlatformFactory.IsWindows)
                OleDropTarget.RevokeWindow(this);

            var handle = Handle;
            Handle = nint.Zero;
            // Remove from window map and destroy
            _windows.Remove(handle);

            if (_platformWindow != null)
            {
                _platformWindow.SetEventHandler(null);
                _platformWindow.Dispose();
                _platformWindow = null;
            }
            else
            {
                _ = DestroyWindow(handle);
            }

            // Let Application decide whether to shut down based on ShutdownMode
            if (Jalium.UI.Application.Current is { } app)
            {
                app.OnWindowClosed(this, _windows.Count);
            }
            else if (_windows.Count == 0)
            {
                // No Application instance — fall back to quit when no windows remain
                if (PlatformFactory.IsWindows)
                    PostQuitMessage(0);
                else
                    PlatformFactory.QuitMessageLoop(0);
            }
        }

        OnClosed(EventArgs.Empty);
        OnUnloaded(new RoutedEventArgs());
    }

    private void ApplyLayeredWindowOpacity(double opacity)
    {
        if (!AllowsTransparency || Handle == nint.Zero)
            return;
        byte alpha = (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
        _ = SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    private void CaptureRestoreBounds()
    {
        if (_platformWindow != null)
        {
            // Cross-platform: capture current size as restore bounds
            _restoreBounds = new Rect(Left, Top, Width, Height);
            return;
        }
        if (Handle != nint.Zero && GetWindowRect(Handle, out RECT rect))
        {
            double dpi = _dpiScale;
            _restoreBounds = new Rect(
                rect.left / dpi,
                rect.top / dpi,
                (rect.right - rect.left) / dpi,
                (rect.bottom - rect.top) / dpi);
        }
    }

    private void AddOwnedWindow(Window child)
    {
        if (!_ownedWindows.Contains(child))
            _ownedWindows.Add(child);
    }

    private void RemoveOwnedWindow(Window child) => _ownedWindows.Remove(child);

    private void ApplyWindowStartupLocation()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        // Cross-platform: skip Win32 monitor-based positioning
        if (_platformWindow != null)
        {
            // On Linux/Android, startup location is handled by the window manager
            // or defaults to (0,0). No monitor info APIs available.
            return;
        }

        switch (WindowStartupLocation)
        {
            case WindowStartupLocation.CenterScreen:
            {
                var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
                var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    int screenW = monitorInfo.rcWork.right - monitorInfo.rcWork.left;
                    int screenH = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top;
                    int winW = (int)(Width * _dpiScale);
                    int winH = (int)(Height * _dpiScale);
                    int x = monitorInfo.rcWork.left + (screenW - winW) / 2;
                    int y = monitorInfo.rcWork.top + (screenH - winH) / 2;
                    _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                }
                break;
            }
            case WindowStartupLocation.CenterOwner:
            {
                if (Owner?.Handle is nint ownerHwnd and not 0)
                {
                    if (GetWindowRect(ownerHwnd, out RECT ownerRect))
                    {
                        int ownerW = ownerRect.right - ownerRect.left;
                        int ownerH = ownerRect.bottom - ownerRect.top;
                        int winW = (int)(Width * _dpiScale);
                        int winH = (int)(Height * _dpiScale);
                        int x = ownerRect.left + (ownerW - winW) / 2;
                        int y = ownerRect.top + (ownerH - winH) / 2;
                        _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    }
                }
                break;
            }
            // WindowStartupLocation.Manual: use Left/Top as-is (already applied via OnPositionChanged)
        }
    }

    private void EnsureHandle()
    {
        if (Handle != nint.Zero)
        {
            return;
        }

        if (!PlatformFactory.IsWindows)
        {
            EnsureHandleCrossPlatform();
            return;
        }

        // ---- Windows code path (Win32) ----

        // Register window class if needed
        RegisterWindowClass();

        // Determine window style based on WindowStyle/ResizeMode/TitleBarStyle.
        // For custom title bar we keep standard caption style bits and remove the
        // visible caption via NCCALCSIZE; for WindowStyle=None we use WS_POPUP so
        // the window has no native frame or caption at all.
        uint dwStyle = WindowStyle == WindowStyle.None
            ? ComputeWin32WindowStyle(WindowStyle.None, ResizeMode)
            : WS_OVERLAPPEDWINDOW;

        uint dwExStyle = TitleBarStyle == WindowTitleBarStyle.Custom
            ? WS_EX_APPWINDOW
            : 0;

        if (!ShowInTaskbar)
        {
            dwExStyle |= WS_EX_TOOLWINDOW;
            dwExStyle &= ~WS_EX_APPWINDOW;
        }

        if (AllowsTransparency)
        {
            // Use WS_EX_NOREDIRECTIONBITMAP so the HWND has no redirection
            // surface and the render backend can present through DirectComposition
            // for real per-pixel transparency. WS_EX_LAYERED would only support
            // uniform GDI alpha and does not composite D3D12 swap chains, which
            // made layered fullscreen windows appear click-through.
            dwExStyle |= WS_EX_NOREDIRECTIONBITMAP;
        }

        // Query system DPI for initial window sizing (before HWND exists)
        uint systemDpi = GetDpiForSystem();
        _dpiScale = systemDpi / 96.0;
        FrameworkElement.LayoutDpiScale = _dpiScale;

        // CreateWindowEx takes physical pixel dimensions.
        // Width/Height are in DIPs — scale to physical pixels.
        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);

        PrepareTaskbarRelaunchIdentity();

        // Create the window
        // Use Left/Top if set, otherwise default placement
        int x = double.IsNaN(Left) ? CW_USEDEFAULT : (int)(Left * _dpiScale);
        int y = double.IsNaN(Top) ? CW_USEDEFAULT : (int)(Top * _dpiScale);

        Handle = CreateWindowEx(
            dwExStyle,
            WindowClassName,
            Title,
            dwStyle,
            x, y,
            physicalWidth, physicalHeight,
            Owner?.Handle ?? nint.Zero,
            nint.Zero,
            GetModuleHandle(null),
            nint.Zero);

        if (Handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create window.");
        }

        ApplyTaskbarRelaunchProperties();

        // Store reference for message handling
        _windows[Handle] = this;

        // Refine DPI from actual window monitor (may differ from system DPI).
        // For custom title bar, also apply SWP_FRAMECHANGED so NCCALCSIZE semantics
        // are active on the first displayed frame. Combining these into one SetWindowPos
        // call avoids an extra DWM roundtrip (~10-50ms).
        uint windowDpi = GetDpiForWindow(Handle);
        bool needsDpiAdjust = windowDpi != 0 && windowDpi != systemDpi;
        bool isCustomTitleBar = TitleBarStyle == WindowTitleBarStyle.Custom;
        bool isBorderless = WindowStyle == WindowStyle.None;
        // SWP_FRAMECHANGED is required for BOTH custom title bar and WindowStyle=None
        // so WM_NCCALCSIZE runs through our handler *after* _windows[Handle] has been
        // populated — the NCCALCSIZE fired during CreateWindowEx happens before that,
        // so the frame would otherwise stay at its DWM default (visible top strip).
        bool needsFrameChanged = isCustomTitleBar || isBorderless;

        if (isCustomTitleBar)
        {
            EnableRoundedCorners();
        }

        if (needsDpiAdjust || needsFrameChanged)
        {
            if (needsDpiAdjust)
            {
                _dpiScale = windowDpi / 96.0;
                FrameworkElement.LayoutDpiScale = _dpiScale;
                physicalWidth = (int)(Width * _dpiScale);
                physicalHeight = (int)(Height * _dpiScale);
            }

            uint flags = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE;
            if (needsFrameChanged)
                flags |= SWP_FRAMECHANGED;
            if (!needsDpiAdjust)
                flags |= SWP_NOMOVE | SWP_NOSIZE;

            _ = SetWindowPos(Handle, nint.Zero,
                0, 0,
                needsDpiAdjust ? physicalWidth : 0,
                needsDpiAdjust ? physicalHeight : 0,
                needsDpiAdjust ? (flags | SWP_NOMOVE) : flags);
        }

        // Create render target for this window.
        // During GPU switching this can fail transiently; defer to render-loop recovery.
        try
        {
            EnsureRenderTarget();
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            ScheduleRenderRecoveryRetry();
        }

        // Apply system backdrop after render target is ready
        if (SystemBackdrop != WindowBackdropType.None)
        {
            ApplySystemBackdrop(SystemBackdrop);
        }

        // Register OLE drop target for external drag-and-drop (e.g. files from Explorer)
        OleDropTarget.RegisterWindow(this);

        UpdateInputMethodAssociation();

        OnSourceInitialized(EventArgs.Empty);
    }

    /// <summary>
    /// Cross-platform window creation path (Linux X11 / Android).
    /// Uses the jalium.native.platform library for window management.
    /// </summary>
    private void EnsureHandleCrossPlatform()
    {
        // Initialize platform if needed
        PlatformFactory.InitializePlatform();

        // Map window style
        uint style = 0;
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
            style |= 0x01; // JALIUM_WINDOW_STYLE_BORDERLESS
        else
            style |= 0x04 | 0x08; // TITLEBAR | CLOSABLE

        if (ResizeMode != ResizeMode.NoResize && ResizeMode != ResizeMode.CanMinimize)
            style |= 0x02; // RESIZABLE

        style |= 0x10 | 0x20; // MINIMIZABLE | MAXIMIZABLE

        if (Topmost)
            style |= 0x40; // TOPMOST

        if (AllowsTransparency)
            style |= 0x100; // TRANSPARENT

        // DPI
        _dpiScale = NativeMethods.PlatformGetSystemDpiScale();
        FrameworkElement.LayoutDpiScale = _dpiScale;

        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);
        int x = double.IsNaN(Left) ? -1 : (int)(Left * _dpiScale);
        int y = double.IsNaN(Top) ? -1 : (int)(Top * _dpiScale);

        _platformWindow = PlatformFactory.CreateWindow(
            Title ?? string.Empty, x, y, physicalWidth, physicalHeight,
            style, Owner?.Handle ?? nint.Zero);

        if (_platformWindow == null)
            throw new InvalidOperationException("Failed to create platform window.");

        Handle = _platformWindow.NativeHandle;

        if (Handle == nint.Zero)
            throw new InvalidOperationException("Platform window returned null handle.");

        _windows[Handle] = this;

        // Connect platform event handler for input/resize/paint routing
        _platformWindow.SetEventHandler(OnPlatformEvent);

        // On Android/Linux the native window (OS surface) determines the actual size.
        // Always use the native surface dimensions on these platforms — any default
        // Window Width/Height (e.g. 800×600) is meaningless on a full-screen device.
        // On desktop platforms (macOS/other) only override when dimensions are missing.
        bool alwaysUseNativeSize = PlatformFactory.IsAndroid || PlatformFactory.IsLinux;
        if (alwaysUseNativeSize || physicalWidth <= 0 || physicalHeight <= 0 || double.IsNaN(Width) || double.IsNaN(Height))
        {
            int nativeW = _platformWindow.GetWidth();
            int nativeH = _platformWindow.GetHeight();
            if (nativeW > 0 && nativeH > 0)
            {
                physicalWidth = nativeW;
                physicalHeight = nativeH;
                if (_dpiScale > 0)
                {
                    Width = physicalWidth / _dpiScale;
                    Height = physicalHeight / _dpiScale;
                }
            }
        }

        // Create render target
        try
        {
            EnsureRenderTarget();
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] RenderTarget={RenderTarget != null} size={physicalWidth}x{physicalHeight}");
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] RenderPipelineException: {ex.Message}");
            ScheduleRenderRecoveryRetry();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] InvalidOperationException: {ex.Message}");
            // Rendering backend unavailable (e.g., on first Android launch before surface is ready).
            // Schedule recovery so rendering retries once the surface is established.
            ScheduleRenderRecoveryRetry();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] UNEXPECTED: {ex}");
            ScheduleRenderRecoveryRetry();
        }

        OnSourceInitialized(EventArgs.Empty);
    }

    /// <summary>
    /// Handles platform events from the native platform library (Linux/Android).
    /// Maps cross-platform events to the same internal handlers used by WndProc on Windows.
    /// </summary>
    private void OnPlatformEvent(PlatformEvent evt)
    {
        switch (evt.Type)
        {
            case PlatformEventType.CloseRequested:
                Close();
                break;

            case PlatformEventType.Destroyed:
                _ = _windows.Remove(Handle);
                break;

            case PlatformEventType.Resize:
                OnSizeChanged(evt.Width, evt.Height);
                break;

            case PlatformEventType.Paint:
                RenderFrame();
                break;

            case PlatformEventType.FocusGained:
                OnActivated(EventArgs.Empty);
                break;

            case PlatformEventType.FocusLost:
                OnDeactivated(EventArgs.Empty);
                _inputDispatcher.ClearMousePressedChain();
                break;

            case PlatformEventType.MouseMove:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                _inputDispatcher.HandleMouseMove(position, MouseButtonStates.AllReleased, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.MouseDown:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var button = MapPlatformMouseButton(evt.Button);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                var buttons = MouseButtonStates.AllReleased.WithButton(button, MouseButtonState.Pressed);
                _inputDispatcher.HandleMouseDown(button, position, buttons, modifiers, evt.ClickCount, Environment.TickCount);
                break;
            }

            case PlatformEventType.MouseUp:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var button = MapPlatformMouseButton(evt.Button);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                _inputDispatcher.HandleMouseUp(button, position, MouseButtonStates.AllReleased, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.MouseWheel:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                int delta = (int)(evt.WheelDeltaY * 120); // Match Win32 WHEEL_DELTA
                _inputDispatcher.HandleMouseWheel(position, delta, MouseButtonStates.AllReleased, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.KeyDown:
            {
                Key key = (Key)evt.KeyCode;
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                bool isRepeat = evt.IsRepeat != 0;
                _inputDispatcher.HandleKeyDown(key, modifiers, isRepeat, Environment.TickCount);
                break;
            }

            case PlatformEventType.KeyUp:
            {
                Key key = (Key)evt.KeyCode;
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                _inputDispatcher.HandleKeyUp(key, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.CharInput:
            {
                if (evt.Codepoint >= 0x20 && evt.Codepoint != 0x7F)
                {
                    _inputDispatcher.HandleCharInput(((char)evt.Codepoint).ToString(), Environment.TickCount);
                }
                break;
            }

            case PlatformEventType.DpiChanged:
            {
                _dpiScale = evt.DpiX / 96.0;
                FrameworkElement.LayoutDpiScale = _dpiScale;
                int physicalWidth = (int)(Width * _dpiScale);
                int physicalHeight = (int)(Height * _dpiScale);
                RenderTarget?.SetDpi((float)evt.DpiX, (float)evt.DpiY);
                TryResizeRenderTarget(physicalWidth, physicalHeight, "DpiChanged");
                RequestFullInvalidation();
                InvalidateMeasure();
                break;
            }

            case PlatformEventType.MouseLeave:
                _inputDispatcher.HandleMouseLeave();
                break;

            case PlatformEventType.Move:
            {
                _isSyncingPosition = true;
                try
                {
                    Left = evt.X / _dpiScale;
                    Top = evt.Y / _dpiScale;
                }
                finally
                {
                    _isSyncingPosition = false;
                }
                OnLocationChanged(EventArgs.Empty);
                CompositionTarget.UpdateRefreshRate(DetectMonitorRefreshRate());
                break;
            }

            case PlatformEventType.StateChanged:
            {
                _isSyncingWindowState = true;
                try
                {
                    var newState = evt.NewState switch
                    {
                        1 => WindowState.Minimized,
                        2 => WindowState.Maximized,
                        _ => WindowState.Normal,
                    };
                    if (WindowState != newState)
                    {
                        WindowState = newState;
                    }
                }
                finally
                {
                    _isSyncingWindowState = false;
                }
                break;
            }

            case PlatformEventType.PointerCancel:
            {
                var pointerData = BuildPointerInputData(evt);
                _inputDispatcher.HandlePointerCancel(pointerData, Environment.TickCount);
                break;
            }

            case PlatformEventType.AppPause:
                CompositionTarget.SuspendRendering();
                break;

            case PlatformEventType.AppResume:
                CompositionTarget.ResumeRendering();
                RequestFullInvalidation();
                InvalidateWindow();
                break;

            case PlatformEventType.AppDestroy:
                Application.Current?.Shutdown();
                break;

            case PlatformEventType.LowMemory:
                _drawingContext?.ClearBitmapCache();
                break;

            case PlatformEventType.SafeAreaChanged:
            {
                // Convert physical pixel insets to DIPs
                var insets = new Thickness(
                    evt.SafeAreaLeft / _dpiScale,
                    evt.SafeAreaTop / _dpiScale,
                    evt.SafeAreaRight / _dpiScale,
                    evt.SafeAreaBottom / _dpiScale);
                if (_safeAreaInsets != insets)
                {
                    _safeAreaInsets = insets;
                    SafeAreaInsetsChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateMeasure();
                }
                break;
            }

            case PlatformEventType.KeyboardChanged:
            {
                bool visible = evt.KeyboardVisible != 0;
                double heightDip = evt.KeyboardHeightPx / _dpiScale;
                if (_softKeyboardVisible != visible || _softKeyboardHeight != heightDip)
                {
                    _softKeyboardVisible = visible;
                    _softKeyboardHeight = heightDip;
                    SoftKeyboardVisibilityChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateMeasure();
                }
                break;
            }

            case PlatformEventType.OrientationChanged:
            {
                var newOrientation = (DeviceOrientation)evt.Orientation;
                if (_deviceOrientation != newOrientation)
                {
                    _deviceOrientation = newOrientation;
                    OrientationChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            }

            case PlatformEventType.Quit:
                Application.Current?.Shutdown();
                break;

            case PlatformEventType.PointerDown:
            case PlatformEventType.PointerUp:
            case PlatformEventType.PointerMove:
            {
                var pointerData = BuildPointerInputData(evt);
                bool isDown = evt.Type == PlatformEventType.PointerDown;
                bool isUp = evt.Type == PlatformEventType.PointerUp;
                _inputDispatcher.HandlePointerInput(pointerData, isDown, isUp, Environment.TickCount);
                break;
            }
        }
    }

    private static MouseButton MapPlatformMouseButton(int button) => button switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Right,
        2 => MouseButton.Middle,
        3 => MouseButton.XButton1,
        4 => MouseButton.XButton2,
        _ => MouseButton.Left,
    };

    private static ModifierKeys MapPlatformModifiers(int modifiers)
    {
        var result = ModifierKeys.None;
        if ((modifiers & 0x01) != 0) result |= ModifierKeys.Shift;
        if ((modifiers & 0x02) != 0) result |= ModifierKeys.Control;
        if ((modifiers & 0x04) != 0) result |= ModifierKeys.Alt;
        if ((modifiers & 0x08) != 0) result |= ModifierKeys.Windows;
        return result;
    }

    private PointerInputData BuildPointerInputData(Platform.PlatformEvent evt)
    {
        var position = new Point(evt.PointerX / _dpiScale, evt.PointerY / _dpiScale);
        var modifiers = MapPlatformModifiers(evt.Modifiers);
        bool isDown = evt.Type == Platform.PlatformEventType.PointerDown;
        bool isUp = evt.Type == Platform.PlatformEventType.PointerUp;
        bool isTouch = evt.PointerType == 1;

        var deviceType = isTouch ? PointerDeviceType.Touch
            : evt.PointerType == 0 ? PointerDeviceType.Mouse
            : PointerDeviceType.Pen;
        float pressure = evt.Pressure > 0 ? evt.Pressure : (isDown || !isUp ? 0.5f : 0f);
        var kind = evt.PointerType switch
        {
            0 => PointerInputKind.Mouse,
            1 => PointerInputKind.Touch,
            2 => PointerInputKind.Pen,
            _ => PointerInputKind.Unknown
        };

        var properties = new PointerPointProperties
        {
            IsLeftButtonPressed = isDown || !isUp,
            Pressure = pressure,
            XTilt = evt.TiltX,
            YTilt = evt.TiltY,
            Twist = evt.Twist,
            PointerUpdateKind = isDown ? PointerUpdateKind.LeftButtonPressed
                : isUp ? PointerUpdateKind.LeftButtonReleased
                : PointerUpdateKind.Other,
            IsPrimary = isTouch
        };

        var pointerPoint = new PointerPoint(
            evt.PointerId, position, deviceType,
            isDown || !isUp, properties, (ulong)Environment.TickCount, 0);

        var stylusPoints = new StylusPointCollection(
            [new StylusPoint(position.X, position.Y, pressure)]);

        return new PointerInputData(
            evt.PointerId, kind, pointerPoint, position, modifiers,
            IsInRange: true, IsCanceled: false, stylusPoints);
    }

    /// <summary>
    /// Tracks the first pointer that went down so we can synthesize mouse events
    /// for backward compatibility (controls that only handle Mouse* events).
    /// </summary>
    private uint? _primaryTouchPointerId;

    /// <summary>
    /// Handles PointerDown/Up/Move from the cross-platform path (Android, Linux).
    /// Routes through the full Touch → Stylus → Manipulation → Pointer pipeline,
    /// matching the behavior of <see cref="OnPointerMessage"/> on Win32.
    /// </summary>
    private void OnCrossPlatformPointerEvent(PlatformEvent evt)
    {
        // Mouse pointer type: route through the existing mouse event handlers.
        if (evt.PointerType == 0) // PointerTypeMouse
        {
            var fakeEvt = evt;
            fakeEvt.MouseX = evt.PointerX;
            fakeEvt.MouseY = evt.PointerY;
            fakeEvt.Modifiers = evt.Modifiers;
            switch (evt.Type)
            {
                case PlatformEventType.PointerDown:
                    fakeEvt.Type = PlatformEventType.MouseDown;
                    fakeEvt.Button = 0; // Left
                    fakeEvt.ClickCount = 1;
                    OnPlatformEvent(fakeEvt);
                    break;
                case PlatformEventType.PointerUp:
                    fakeEvt.Type = PlatformEventType.MouseUp;
                    fakeEvt.Button = 0;
                    OnPlatformEvent(fakeEvt);
                    break;
                case PlatformEventType.PointerMove:
                    fakeEvt.Type = PlatformEventType.MouseMove;
                    OnPlatformEvent(fakeEvt);
                    break;
            }
            return;
        }

        // Touch or Pen pointer: full pointer pipeline.
        bool isDown = evt.Type == PlatformEventType.PointerDown;
        bool isUp = evt.Type == PlatformEventType.PointerUp;
        int timestamp = Environment.TickCount;
        var position = new Point(evt.PointerX / _dpiScale, evt.PointerY / _dpiScale);
        var modifiers = MapPlatformModifiers(evt.Modifiers);
        bool isTouch = evt.PointerType == 1; // PointerTypeTouch
        bool isPen = evt.PointerType == 2;   // PointerTypePen

        // Build PointerPoint with correct device type.
        var deviceType = isTouch ? PointerDeviceType.Touch : PointerDeviceType.Pen;
        float pressure = evt.Pressure > 0 ? evt.Pressure : (isDown || !isUp ? 0.5f : 0f);
        bool isPrimary = isTouch && (_primaryTouchPointerId == null || _primaryTouchPointerId == evt.PointerId);

        var properties = new PointerPointProperties
        {
            IsLeftButtonPressed = isDown || !isUp,
            Pressure = pressure,
            XTilt = evt.TiltX,
            YTilt = evt.TiltY,
            Twist = evt.Twist,
            PointerUpdateKind = isDown ? PointerUpdateKind.LeftButtonPressed
                : isUp ? PointerUpdateKind.LeftButtonReleased
                : PointerUpdateKind.Other,
            IsPrimary = isPrimary
        };

        var pointerPoint = new PointerPoint(
            evt.PointerId,
            position,
            deviceType,
            isDown || !isUp, // isInContact
            properties,
            (ulong)timestamp,
            0);

        // Build StylusPointCollection for the stylus pipeline.
        var stylusPoints = new StylusPointCollection(
            new[] { new StylusPoint(position.X, position.Y, pressure) });

        var pointerData = new PointerInputData(
            evt.PointerId,
            isTouch ? PointerInputKind.Touch : PointerInputKind.Pen,
            pointerPoint,
            position,
            modifiers,
            IsInRange: true,
            IsCanceled: false,
            stylusPoints);

        // Track primary touch pointer for mouse synthesis.
        if (isTouch && isDown && _primaryTouchPointerId == null)
            _primaryTouchPointerId = evt.PointerId;

        // Hit test and target resolution.
        var captured = UIElement.MouseCapturedElement;
        var hitTarget = HitTestElement(position, "pointer-route");
        var fallbackTarget = captured ?? hitTarget ?? this;
        var target = isDown
            ? fallbackTarget
            : (_activePointerTargets.TryGetValue(evt.PointerId, out var existingTarget)
                ? existingTarget ?? fallbackTarget : fallbackTarget);

        _activePointerTargets[evt.PointerId] = target;
        _lastPointerPoints[evt.PointerId] = pointerPoint;

        // Dispatch source-level events (Touch or Stylus).
        bool sourceHandled = false;
        bool sourceCanceled = false;

        if (isTouch)
        {
            DispatchTouchSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }
        else if (isPen)
        {
            DispatchStylusSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }

        if (sourceCanceled)
        {
            CancelManipulationSession(evt.PointerId, timestamp);
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
            CleanupPointerSession(evt.PointerId);
            if (isTouch && _primaryTouchPointerId == evt.PointerId)
                _primaryTouchPointerId = null;
            return;
        }

        // Manipulation pipeline.
        DispatchManipulationPipeline(target, pointerData, isDown, isUp, sourceHandled, timestamp);

        // Pointer events.
        if (!sourceHandled)
        {
            if (isDown)
                RaisePointerDownPipeline(target, pointerPoint, modifiers, timestamp);
            else if (isUp)
                RaisePointerUpPipeline(target, pointerPoint, modifiers, timestamp);
            else
                RaisePointerMovePipeline(target, pointerPoint, modifiers, timestamp);
        }

        // Synthesize mouse events for the primary touch pointer so that
        // controls handling only Mouse* events (Button, ScrollViewer, etc.) work.
        if (isTouch && _primaryTouchPointerId == evt.PointerId && !sourceHandled)
        {
            SynthesizeMouseFromTouch(evt, position, modifiers, isDown, isUp, hitTarget, timestamp);
        }

        if (isUp)
        {
            CleanupPointerSession(evt.PointerId);
            if (isTouch && _primaryTouchPointerId == evt.PointerId)
                _primaryTouchPointerId = null;
        }
    }

    /// <summary>
    /// Handles PointerCancel from the cross-platform path.
    /// Cancels any active touch/manipulation session and raises pointer cancel events.
    /// </summary>
    private void OnCrossPlatformPointerCancel(PlatformEvent evt)
    {
        int timestamp = Environment.TickCount;
        var position = new Point(evt.PointerX / _dpiScale, evt.PointerY / _dpiScale);
        var modifiers = MapPlatformModifiers(evt.Modifiers);

        if (_activePointerTargets.TryGetValue(evt.PointerId, out var target) && target != null)
        {
            if (!_lastPointerPoints.TryGetValue(evt.PointerId, out var point))
            {
                var deviceType = evt.PointerType == 1 ? PointerDeviceType.Touch : PointerDeviceType.Pen;
                point = new PointerPoint(
                    evt.PointerId, position, deviceType, false,
                    new PointerPointProperties(), (ulong)timestamp);
            }

            // Deactivate touch device if this was a touch pointer.
            if (evt.PointerType == 1) // Touch
            {
                var touchDevice = Touch.GetDevice((int)evt.PointerId);
                if (touchDevice != null)
                {
                    touchDevice.Deactivate();
                    Touch.UnregisterTouchPoint((int)evt.PointerId);
                }
                _activeStylusDevices.Remove(evt.PointerId);
            }

            CancelManipulationSession(evt.PointerId, timestamp);
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }

        CleanupPointerSession(evt.PointerId);

        if (_primaryTouchPointerId == evt.PointerId)
        {
            _primaryTouchPointerId = null;
            // Synthesize mouse leave so controls reset hover state.
            _inputDispatcher.HandleMouseLeave();
        }
    }

    /// <summary>
    /// Synthesizes mouse events from the primary touch pointer so that controls
    /// that only handle Mouse* events continue to work on touch platforms.
    /// </summary>
    private void SynthesizeMouseFromTouch(
        PlatformEvent evt, Point position, ModifierKeys modifiers,
        bool isDown, bool isUp, UIElement? hitTarget, int timestamp)
    {
        var buttons = new MouseButtonStates
        {
            Left = isUp ? MouseButtonState.Released : MouseButtonState.Pressed
        };

        // Suppress mouse→pointer promotion: pointer events were already dispatched
        // directly from the touch pipeline with the correct PointerDeviceType.Touch.
        _inputDispatcher.SuppressMouseToPointerPromotion = true;
        try
        {
            if (isDown)
            {
                _inputDispatcher.HandleMouseDown(
                    MouseButton.Left, position, buttons, modifiers, clickCount: 1, timestamp);
            }
            else if (isUp)
            {
                _inputDispatcher.HandleMouseUp(MouseButton.Left, position, buttons, modifiers, timestamp);
            }
            else
            {
                _inputDispatcher.HandleMouseMove(position, buttons, modifiers, timestamp);
            }
        }
        finally
        {
            _inputDispatcher.SuppressMouseToPointerPromotion = false;
        }
    }

    private void EnableRoundedCorners()
    {
        if (Handle == nint.Zero || !PlatformFactory.IsWindows)
        {
            return;
        }

        // DWMWA_WINDOW_CORNER_PREFERENCE = 33
        // DWMWCP_ROUND = 2 (rounded corners)
        int cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        // Enable dark mode so DWM-owned UI (title bar, system menu, scrollbars) uses dark theme.
        int useDarkMode = 1;
        _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Enable dark mode for popup menus (TrackPopupMenu) via undocumented uxtheme APIs.
        _ = SetPreferredAppMode(PreferredAppMode.ForceDark);
        _ = AllowDarkModeForWindow(Handle, true);
        FlushMenuThemes();

        // Set DWM caption/border color to dark to prevent white flash during resize.
        // DWMWA_CAPTION_COLOR = 35, DWMWA_BORDER_COLOR = 34
        // COLORREF format: 0x00BBGGRR
        int darkColor = 0x00282828; // #282828 in BGR
        _ = DwmSetWindowAttribute(Handle, 35, ref darkColor, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 34, ref darkColor, sizeof(int));

        // Extend frame into client area covering the effective title bar/button hit-test height.
        UpdateCustomTitleBarFrameMargins();
    }

    private void UpdateCustomTitleBarFrameMargins()
    {
        if (!PlatformFactory.IsWindows || Handle == nint.Zero || TitleBarStyle != WindowTitleBarStyle.Custom)
        {
            return;
        }

        int topMarginPhysical = GetCustomTitleBarTopMarginPhysical();
        if (_appliedDwmTopMarginPhysical == topMarginPhysical)
        {
            return;
        }

        MARGINS margins = new() { Left = 0, Right = 0, Top = topMarginPhysical, Bottom = 0 };
        _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
        _appliedDwmTopMarginPhysical = topMarginPhysical;
    }

    // DWM uses the Top margin to decide where the caption frame starts and
    // whether to arm the Windows 11 Snap Layouts flyout over the maximize
    // button. If we pass 0/1 here the flyout stops appearing entirely; if
    // we pass the full custom title-bar height DWM still anchors the caption
    // button rect to the system height at the top of that region. So mirror
    // the title-bar height (or at least enough to cover it) to keep DWM's
    // caption semantics aligned with our custom chrome.
    private int GetCustomTitleBarTopMarginPhysical()
    {
        if (!IsTitleBarVisible())
        {
            return 0;
        }

        double titleBarHeightDip = GetEffectiveTitleBarHeightDip();
        if (TitleBar != null)
        {
            titleBarHeightDip = GetElementHeightDip(TitleBar, titleBarHeightDip);
        }

        return Math.Max((int)Math.Ceiling(titleBarHeightDip * _dpiScale), 1);
    }

    private static double GetElementHeightDip(FrameworkElement element, double fallback)
    {
        if (element.ActualHeight > 0)
        {
            return element.ActualHeight;
        }

        if (element.DesiredSize.Height > 0)
        {
            return element.DesiredSize.Height;
        }

        if (!double.IsNaN(element.Height) && element.Height > 0)
        {
            return element.Height;
        }

        return fallback;
    }

    private void ApplySystemBackdrop(WindowBackdropType backdropType)
    {
        if (Handle == nint.Zero || !PlatformFactory.IsWindows)
        {
            return; // System backdrops (Mica/Acrylic) only available on Windows
        }

        if (backdropType == WindowBackdropType.None)
        {
            // Disable system backdrop
            int none = DWMSBT_NONE;
            _ = DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

            // Restore frame extension covering title bar for Snap Layout support
            if (TitleBarStyle == WindowTitleBarStyle.Custom)
            {
                UpdateCustomTitleBarFrameMargins();
            }

            RequestFullInvalidation();
            InvalidateWindow();
            return;
        }

        // DwmExtendFrameIntoClientArea is already called by EnableRoundedCorners()
        // with title-bar-height margins. We intentionally avoid {-1,-1,-1,-1} here
        // because that makes DWM draw its own caption visuals over a custom title bar.

        // Step 1: Set dark mode for proper Mica tint (dark theme)
        int useDarkMode = 1;
        _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Step 3: Set the DWM system backdrop type (Windows 11 22H2+)
        int dwmBackdropType = backdropType switch
        {
            WindowBackdropType.Mica => DWMSBT_MAINWINDOW,
            WindowBackdropType.MicaAlt => DWMSBT_TABBEDWINDOW,
            WindowBackdropType.Acrylic => DWMSBT_TRANSIENTWINDOW,
            _ => DWMSBT_AUTO
        };
        int result = DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref dwmBackdropType, sizeof(int));

        if (result != 0)
        {
            // DWM system backdrop not supported (Windows 10 or older Windows 11).
            // Fallback: SetWindowCompositionAttribute for Acrylic blur.
            ApplyAccentPolicy(backdropType);
        }

        RequestFullInvalidation();
        InvalidateWindow();
    }

    /// <summary>
    /// Fallback for Windows 10: applies Acrylic blur via SetWindowCompositionAttribute.
    /// </summary>
    private void ApplyAccentPolicy(WindowBackdropType backdropType)
    {
        if (Handle == nint.Zero) return;

        int accentState = backdropType == WindowBackdropType.None
            ? ACCENT_DISABLED
            : ACCENT_ENABLE_ACRYLICBLURBEHIND;

        // GradientColor in ABGR format: alpha in high byte
        // Dark tint with ~80% opacity
        uint gradientColor = 0xCC1A1A1A; // ABGR: A=0xCC, B=0x1A, G=0x1A, R=0x1A

        var accent = new ACCENT_POLICY
        {
            AccentState = accentState,
            AccentFlags = 2, // ACCENT_FLAG_DRAW_ALL_BORDERS
            GradientColor = gradientColor,
            AnimationId = 0
        };

        int accentSize = Marshal.SizeOf<ACCENT_POLICY>();
        nint accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = accentPtr,
                DataSize = accentSize
            };
            _ = SetWindowCompositionAttribute(Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private void UpdateWindowStyle()
    {
        if (Handle == nint.Zero || _platformWindow != null)
        {
            return; // Win32 window styles not applicable on cross-platform
        }

        // While in fullscreen we don't want to re-apply the user's chosen frame
        // styles; they are re-applied on ExitFullScreen from the saved snapshot.
        if (_isFullScreen)
        {
            return;
        }

        long style = GetWindowLong(Handle, GWL_STYLE);
        long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);

        // Always clear the bits we manage so transitions between WindowStyle
        // values drop the previously-applied frame cleanly.
        const uint FrameMask = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
        style &= ~(long)FrameMask;

        if (WindowStyle == WindowStyle.None)
        {
            // Borderless popup. Do NOT set WS_CAPTION — the OS must not draw
            // a caption bar. Edge resize relies on WS_THICKFRAME when allowed.
            if (ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip)
            {
                style |= WS_THICKFRAME;
            }
        }
        else if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            // Keep WS_CAPTION so the OS preserves native caption semantics
            // (Snap, system menu, NC button behavior). We remove the visual
            // caption in WM_NCCALCSIZE instead.
            style |= WS_CAPTION | WS_SYSMENU;
            if (ResizeMode != ResizeMode.NoResize)
            {
                style |= WS_THICKFRAME;
                style |= WS_MINIMIZEBOX;
                if (ResizeMode != ResizeMode.CanMinimize)
                    style |= WS_MAXIMIZEBOX;
            }
            exStyle |= WS_EX_APPWINDOW;
            EnableRoundedCorners();
        }
        else
        {
            style |= WS_CAPTION | WS_SYSMENU;
            if (ResizeMode != ResizeMode.NoResize)
            {
                style |= WS_THICKFRAME;
                style |= WS_MINIMIZEBOX;
                if (ResizeMode != ResizeMode.CanMinimize)
                    style |= WS_MAXIMIZEBOX;
            }
        }

        _ = SetWindowLong(Handle, GWL_STYLE, style);
        _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        // Force window to redraw with new style
        _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Enters fullscreen mode: saves current bounds/style, removes the frame,
    /// and resizes the window to cover the containing monitor (including taskbar).
    /// </summary>
    private void EnterFullScreen()
    {
        if (_isFullScreen || Handle == nint.Zero || _platformWindow != null)
        {
            return;
        }

        // Snapshot current style + bounds so we can restore on exit.
        _fullScreenSavedStyle = (uint)GetWindowLong(Handle, GWL_STYLE);
        _fullScreenSavedExStyle = (uint)GetWindowLong(Handle, GWL_EXSTYLE);
        if (!GetWindowRect(Handle, out _fullScreenSavedRect))
        {
            return;
        }

        var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi))
        {
            return;
        }

        _isFullScreen = true;

        // Strip frame bits — leave WS_VISIBLE / WS_CLIPSIBLINGS / WS_CLIPCHILDREN untouched.
        const uint FrameMask = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_OVERLAPPEDWINDOW;
        long newStyle = (long)_fullScreenSavedStyle & ~(long)FrameMask;
        newStyle |= (long)WS_POPUP;
        _ = SetWindowLong(Handle, GWL_STYLE, newStyle);

        var rc = mi.rcMonitor;
        _ = SetWindowPos(
            Handle,
            HWND_TOP,
            rc.left,
            rc.top,
            rc.right - rc.left,
            rc.bottom - rc.top,
            SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Exits fullscreen mode, restoring the style and bounds captured by
    /// <see cref="EnterFullScreen"/>.
    /// </summary>
    private void ExitFullScreen()
    {
        if (!_isFullScreen || Handle == nint.Zero || _platformWindow != null)
        {
            return;
        }

        _isFullScreen = false;

        _ = SetWindowLong(Handle, GWL_STYLE, _fullScreenSavedStyle);
        _ = SetWindowLong(Handle, GWL_EXSTYLE, _fullScreenSavedExStyle);

        var rc = _fullScreenSavedRect;
        _ = SetWindowPos(
            Handle,
            nint.Zero,
            rc.left,
            rc.top,
            rc.right - rc.left,
            rc.bottom - rc.top,
            SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOOWNERZORDER);
    }

    private static uint GetWindowStyleForTitleBarStyle(WindowTitleBarStyle titleBarStyle)
    {
        // Legacy helper retained for compatibility. Defers to the richer computation
        // that considers WindowStyle/ResizeMode below.
        return ComputeWin32WindowStyle(WindowStyle.SingleBorderWindow, ResizeMode.CanResize);
    }

    /// <summary>
    /// Computes the Win32 WS_* style bits corresponding to the given
    /// <paramref name="windowStyle"/> and <paramref name="resizeMode"/>.
    /// For <see cref="WindowStyle.None"/> the result is a borderless popup
    /// (no caption, no system menu, no min/max buttons). When resizing is
    /// permitted, <c>WS_THICKFRAME</c> is included so the window can be
    /// resized via its edges.
    /// </summary>
    private static uint ComputeWin32WindowStyle(WindowStyle windowStyle, ResizeMode resizeMode)
    {
        if (windowStyle == WindowStyle.None)
        {
            uint style = WS_POPUP;
            if (resizeMode == ResizeMode.CanResize || resizeMode == ResizeMode.CanResizeWithGrip)
            {
                // WS_THICKFRAME lets the OS handle edge resize + Aero Snap.
                style |= WS_THICKFRAME;
            }
            return style;
        }

        // Borders + caption + sys menu + optional min/max buttons.
        uint baseStyle = WS_POPUP | WS_CAPTION | WS_SYSMENU;
        switch (resizeMode)
        {
            case ResizeMode.NoResize:
                break;
            case ResizeMode.CanMinimize:
                baseStyle |= WS_MINIMIZEBOX;
                break;
            case ResizeMode.CanResize:
            case ResizeMode.CanResizeWithGrip:
            default:
                baseStyle |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
                break;
        }
        return baseStyle;
    }

    private bool ShouldUseCompositionRenderTarget()
    {
        if (Handle == nint.Zero)
        {
            return false;
        }

        long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        return (exStyle & WS_EX_NOREDIRECTIONBITMAP) != 0;
    }

    private static bool IsBackendAvailable(RenderBackend backend)
        => backend != RenderBackend.Auto && NativeMethods.IsBackendAvailable(backend) != 0;

    private static ReadOnlySpan<RenderBackend> GetBackendFallbackOrder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return [RenderBackend.D3D12, RenderBackend.Software];
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return [RenderBackend.Metal, RenderBackend.Software];
        return [RenderBackend.Vulkan, RenderBackend.Software];
    }

    private static int GetBackendFallbackIndex(RenderBackend backend)
    {
        var order = GetBackendFallbackOrder();
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] == backend)
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryAdvanceRenderBackendFallback(RenderBackend failedBackend)
    {
        var order = GetBackendFallbackOrder();
        int index = GetBackendFallbackIndex(failedBackend);
        if (index < 0)
        {
            index = 0;
        }

        for (int i = index + 1; i < order.Length; i++)
        {
            var candidate = order[i];
            if (candidate != failedBackend && IsBackendAvailable(candidate))
            {
                _renderBackendOverride = candidate;
                return true;
            }
        }

        return false;
    }

    private void EnableD3D12WarpFallback()
    {
        _renderBackendOverride = RenderBackend.D3D12;
        Environment.SetEnvironmentVariable(D3D12ForceWarpEnvironmentVariable, "1");
    }

    private void EnsureRenderTarget(bool forceNewContext = false)
    {
        if (RenderTarget != null)
        {
            return;
        }

        // Swap chain uses physical pixel dimensions — bail if the window hasn't
        // been sized yet (0×0 swap chains are rejected by DXGI).
        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);
        if (physicalWidth <= 0 || physicalHeight <= 0 || Handle == nint.Zero)
        {
            Console.Error.WriteLine($"[EnsureRenderTarget] BAIL: pw={physicalWidth} ph={physicalHeight} handle=0x{Handle:X}");
            return;
        }

        var requestedBackend = _renderBackendOverride != RenderBackend.Auto
            ? _renderBackendOverride
            : RenderBackend.Auto;

        var context = RenderContext.GetOrCreateCurrent(requestedBackend, forceReplace: forceNewContext);

        try
        {
            if (_platformWindow != null)
            {
                // Cross-platform path: use surface descriptor from platform window
                var surface = _platformWindow.GetSurface();
                RenderTarget = context.CreateRenderTarget(surface, physicalWidth, physicalHeight);
            }
            else
            {
                // Win32 path: use HWND-based render target
                bool useComposition = ShouldUseCompositionRenderTarget();
                if (useComposition)
                {
                    RenderTarget = context.CreateRenderTargetForComposition(Handle, physicalWidth, physicalHeight);
                }
                else
                {
                    RenderTarget = context.CreateRenderTarget(Handle, physicalWidth, physicalHeight);
                }
            }
        }
        catch (Exception ex) when (context.GpuPreference != GpuPreference.Auto)
        {
            Console.Error.WriteLine($"[EnsureRenderTarget] GPU fallback: {ex.Message}");
            // The preferred GPU couldn't create a render target. Fall back to default GPU.
            context = RenderContext.GetOrCreateCurrent(requestedBackend, GpuPreference.Auto, forceReplace: true);
            RenderTarget = CreateRenderTargetForPlatform(context, physicalWidth, physicalHeight);
        }
        catch (Exception ex) when (requestedBackend != RenderBackend.Auto && TryAdvanceRenderBackendFallback(requestedBackend))
        {
            Console.Error.WriteLine($"[EnsureRenderTarget] backend fallback: {ex.Message}");
            var fallbackContext = RenderContext.GetOrCreateCurrent(_renderBackendOverride, GpuPreference.Auto, forceReplace: true);
            RenderTarget = CreateRenderTargetForPlatform(fallbackContext, physicalWidth, physicalHeight);
        }

        // Set D2D DPI so DIP coordinates map correctly to physical pixels
        float dpi = (float)(_dpiScale * 96.0);
        RenderTarget?.SetDpi(dpi, dpi);
    }

    /// <summary>
    /// Creates a render target using the appropriate method for the current platform.
    /// </summary>
    private RenderTarget CreateRenderTargetForPlatform(RenderContext context, int width, int height)
    {
        if (_platformWindow != null)
        {
            var surface = _platformWindow.GetSurface();
            return context.CreateRenderTarget(surface, width, height);
        }

        if (ShouldUseCompositionRenderTarget())
            return context.CreateRenderTargetForComposition(Handle, width, height);

        return context.CreateRenderTarget(Handle, width, height);
    }

    /// <summary>
    /// Ensures this window uses a composition render target that can host external visuals (for WebView composition controller).
    /// Recreates the render target if needed.
    /// </summary>
    internal bool EnsureCompositionRenderTargetForEmbeddedContent()
    {
        if (Handle == nint.Zero)
            return false;

        EnsureRenderTarget();
        if (RenderTarget == null || !RenderTarget.IsValid)
            return false;

        // Fast path: already supports composition child visuals.
        if (RenderTarget.TryCreateWebViewCompositionVisual(out var existingVisual) && existingVisual != nint.Zero)
        {
            RenderTarget.DestroyWebViewCompositionVisual(existingVisual);
            return true;
        }

        try
        {
            var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

            // Dispose the old render target BEFORE changing the window style.
            // SetWindowPos(SWP_FRAMECHANGED) can trigger a synchronous WM_PAINT,
            // which would call RenderFrame with the old non-composition swap chain
            // on a window that now has WS_EX_NOREDIRECTIONBITMAP — the old swap
            // chain's Present fails, dirty state is lost, and the stale frame
            // from before the transition stays on screen permanently.
            // Nulling the render target first causes the intermediate RenderFrame
            // to early-return (RenderTarget == null check at the top).
            var oldDrawingContext = _drawingContext;
            _drawingContext = null;
            oldDrawingContext?.ClearCache();

            var oldRenderTarget = RenderTarget;
            RenderTarget = null;
            oldRenderTarget?.Dispose();

            // Now safe to change window style — any WM_PAINT during SetWindowPos
            // will see RenderTarget == null and skip rendering.
            long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            if ((exStyle & WS_EX_NOREDIRECTIONBITMAP) == 0)
            {
                _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_NOREDIRECTIONBITMAP);
                _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
                    SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
            }

            int widthDip = ActualWidth > 0 ? (int)Math.Ceiling(ActualWidth) : (int)Math.Ceiling(Width);
            int heightDip = ActualHeight > 0 ? (int)Math.Ceiling(ActualHeight) : (int)Math.Ceiling(Height);
            int physicalWidth = Math.Max(1, (int)Math.Ceiling(widthDip * _dpiScale));
            int physicalHeight = Math.Max(1, (int)Math.Ceiling(heightDip * _dpiScale));

            RenderTarget = context.CreateRenderTargetForComposition(Handle, physicalWidth, physicalHeight);

            float dpi = (float)(_dpiScale * 96.0);
            RenderTarget.SetDpi(dpi, dpi);

            // Composition swap chains use DXGI_ALPHA_MODE_PREMULTIPLIED, so
            // any semi-transparent window Background becomes truly transparent
            // (DWM blends with the desktop).  When there is no system backdrop
            // this is almost certainly unintentional and causes ghost-image
            // artifacts (sidebar text doubled, desktop bleeding through).
            // Force the background to fully opaque to prevent the bleed-through.
            if (SystemBackdrop == WindowBackdropType.None &&
                Background is Media.SolidColorBrush bgBrush &&
                bgBrush.Color.A < 255)
            {
                var c = bgBrush.Color;
                Background = new Media.SolidColorBrush(Media.Color.FromArgb(255, c.R, c.G, c.B));
            }

            ForceRenderFrame();

            if (RenderTarget.TryCreateWebViewCompositionVisual(out var visualAfterSwitch) && visualAfterSwitch != nint.Zero)
            {
                RenderTarget.DestroyWebViewCompositionVisual(visualAfterSwitch);
                return true;
            }
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "EnsureCompositionRenderTargetForEmbeddedContent");

            // Ensure a full re-render is scheduled so the window doesn't stay
            // stuck with stale content from before the render target swap.
            lock (_dirtyLock)
            {
                _fullInvalidation = true;
            }
            if (RenderTarget != null && RenderTarget.IsValid)
            {
                ScheduleRenderAfterRecovery();
            }
        }

        return false;
    }

    #region Property Changed Callbacks

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            if (window.Handle != nint.Zero)
            {
                if (window._platformWindow != null)
                    window._platformWindow.SetTitle((string?)e.NewValue ?? "");
                else
                    _ = SetWindowText(window.Handle, (string?)e.NewValue ?? "");
            }

            window.ApplyTitleBarPresentation();
        }
    }

    private static void OnWindowStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && e.NewValue is WindowState newState)
        {
            var oldState = e.OldValue is WindowState os ? os : WindowState.Normal;

            // Capture restore bounds when leaving Normal state
            if (oldState == WindowState.Normal && newState != WindowState.Normal
                && window.Handle != nint.Zero)
            {
                window.CaptureRestoreBounds();
            }

            window.ApplyTitleBarPresentation();

            // Sync the native window state when set programmatically.
            // Skip if we're already syncing from WM_SIZE to avoid infinite loop.
            if (!window._isSyncingWindowState && window.Handle != nint.Zero)
            {
                if (window._platformWindow != null)
                {
                    // Cross-platform backend: fullscreen not yet supported — fall
                    // back to maximized so the request still produces a reasonable
                    // visual result.
                    var mapped = newState == WindowState.FullScreen
                        ? WindowState.Maximized
                        : newState;
                    window._platformWindow.SetState(mapped);
                }
                else
                {
                    // Leaving fullscreen: restore pre-fullscreen frame + bounds first.
                    if (oldState == WindowState.FullScreen && newState != WindowState.FullScreen)
                    {
                        window.ExitFullScreen();
                    }

                    if (newState == WindowState.FullScreen)
                    {
                        window._fullScreenPreviousState = oldState == WindowState.FullScreen
                            ? WindowState.Normal
                            : oldState;
                        // Ensure the window is visible and in a restored state before
                        // capturing bounds/style for fullscreen.
                        if (oldState == WindowState.Minimized)
                        {
                            _ = ShowWindow(window.Handle, SW_RESTORE);
                        }
                        else if (oldState == WindowState.Maximized)
                        {
                            _ = ShowWindow(window.Handle, SW_RESTORE);
                        }
                        window.EnterFullScreen();
                        _ = ShowWindow(window.Handle, SW_SHOW);
                    }
                    else
                    {
                        var cmd = newState switch
                        {
                            WindowState.Maximized => SW_MAXIMIZE,
                            WindowState.Minimized => SW_MINIMIZE,
                            _ => SW_RESTORE
                        };
                        _ = ShowWindow(window.Handle, cmd);
                    }
                }
            }

            window.OnStateChanged(EventArgs.Empty);
        }
    }

    private static void OnWindowStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero)
        {
            window.UpdateWindowStyle();
            window.ApplyTitleBarPresentation();
            window.InvalidateMeasure();
        }
    }

    private static void OnTitleBarStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            window.RemoveTitleBar();

            if (e.NewValue is WindowTitleBarStyle windowTitleBarStyle)
            {
                if (windowTitleBarStyle == WindowTitleBarStyle.Custom)
                {
                    window.CreateTitleBar();
                }
            }

            // Update window style if already created
            if (window.Handle != nint.Zero)
            {
                window.UpdateWindowStyle();
            }

            window.ApplyTitleBarPresentation();
            window.InvalidateMeasure();
            window.UpdateCustomTitleBarFrameMargins();
        }
    }

    private static void OnSystemBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && e.NewValue is WindowBackdropType backdropType)
        {
            window.ApplySystemBackdrop(backdropType);
        }
    }

    private static void OnTopmostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero)
        {
            // SetWindowPos with HWND_TOPMOST / HWND_NOTOPMOST
            bool topmost = e.NewValue is bool b && b;
            nint insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            _ = SetWindowPos(window.Handle, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private static void OnWindowTitleBarPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        if (e.Property == WindowIconProperty && e.NewValue == null)
        {
            window._attemptedAutoWindowIcon = false;
        }

        window.ApplyTitleBarPresentation();
        window.InvalidateMeasure();
        window.UpdateCustomTitleBarFrameMargins();
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero && !window._isSyncingPosition)
        {
            int x = double.IsNaN(window.Left) ? 0 : (int)(window.Left * window._dpiScale);
            int y = double.IsNaN(window.Top) ? 0 : (int)(window.Top * window._dpiScale);
            if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
            {
                return;
            }

            if (window._platformWindow != null)
            {
                window._platformWindow.Move(x, y);
            }
            else
            {
                uint flags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;
                _ = SetWindowPos(window.Handle, nint.Zero, x, y, 0, 0, flags);
            }
        }
    }

    private static void OnShowInTaskbarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero)
        {
            bool show = e.NewValue is true;
            long exStyle = GetWindowLong(window.Handle, GWL_EXSTYLE);
            if (show)
            {
                exStyle &= ~(long)WS_EX_TOOLWINDOW;
                exStyle |= WS_EX_APPWINDOW;
            }
            else
            {
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~(long)WS_EX_APPWINDOW;
            }
            _ = SetWindowLong(window.Handle, GWL_EXSTYLE, exStyle);
            // Force the shell to re-evaluate taskbar presence
            _ = SetWindowPos(window.Handle, nint.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    #endregion

    #region Native Window Management

    private const string WindowClassName = "JaliumUIWindow";
    private static bool _classRegistered;
    private static readonly Dictionary<nint, Window> _windows = [];
    private static WndProcDelegate? _wndProcDelegate;
    private bool _isSizing; // True during drag resize

    // Cursor cache - stores loaded cursor handles to avoid repeated LoadCursor calls
    private static readonly Dictionary<CursorType, nint> _cursorCache = [];

    internal static Window? TryGetOpenWindow(nint handle)
    {
        return handle != nint.Zero && _windows.TryGetValue(handle, out var window)
            ? window
            : null;
    }

    /// <summary>
    /// Gets the Windows cursor handle for a CursorType.
    /// </summary>
    private static nint GetCursorHandle(CursorType cursorType)
    {
        if (_cursorCache.TryGetValue(cursorType, out var handle))
        {
            return handle;
        }

        nint cursorId = cursorType switch
        {
            CursorType.Arrow => IDC_ARROW,
            CursorType.IBeam => IDC_IBEAM,
            CursorType.Wait => IDC_WAIT,
            CursorType.Cross => IDC_CROSS,
            CursorType.UpArrow => IDC_UPARROW,
            CursorType.SizeNWSE => IDC_SIZENWSE,
            CursorType.SizeNESW => IDC_SIZENESW,
            CursorType.SizeWE => IDC_SIZEWE,
            CursorType.SizeNS => IDC_SIZENS,
            CursorType.SizeAll => IDC_SIZEALL,
            CursorType.No => IDC_NO,
            CursorType.Hand => IDC_HAND,
            CursorType.AppStarting => IDC_APPSTARTING,
            CursorType.Help => IDC_HELP,
            CursorType.None => nint.Zero, // Will hide cursor
            _ => IDC_ARROW
        };

        handle = cursorId != nint.Zero ? LoadCursor(nint.Zero, cursorId) : nint.Zero;
        _cursorCache[cursorType] = handle;
        return handle;
    }

    /// <summary>
    /// Handles WM_SETCURSOR by finding the element under the cursor and setting the appropriate cursor.
    /// </summary>
    private bool OnSetCursor(nint lParam)
    {
        // Only handle if the cursor is in the client area
        int hitTest = (short)(lParam.ToInt64() & 0xFFFF);
        if (hitTest != HTCLIENT_SETCURSOR)
        {
            return false; // Let Windows handle non-client area cursors
        }

        // Get the current mouse position
        if (!GetCursorPos(out POINT screenPt))
        {
            return false;
        }

        _ = ScreenToClient(Handle, ref screenPt);
        // Convert physical client pixels to DIPs
        Point clientPos = new(screenPt.X / _dpiScale, screenPt.Y / _dpiScale);

        var hitResult = HitTestWithCache(clientPos);
        var element = hitResult?.VisualHit as UIElement;

        // Walk up the visual tree to find the first element with a non-null Cursor
        var cursor = ResolveCursor(element);

        // Set the cursor
        nint cursorHandle;
        if (cursor != null)
        {
            cursorHandle = GetCursorHandle(cursor.CursorType);
        }
        else
        {
            cursorHandle = GetCursorHandle(CursorType.Arrow);
        }

        if (cursorHandle != nint.Zero)
        {
            _ = SetCursor(cursorHandle);
            return true;
        }

        return false;
    }

    private static Cursor? ResolveCursor(UIElement? element)
    {
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.Cursor != null)
            {
                return fe.Cursor;
            }

            element = element.VisualParent as UIElement;
        }

        return null;
    }

    private DevToolsWindow? _devToolsWindow;
    internal DevToolsOverlay? DevToolsOverlay { get; set; }

    /// <summary>
    /// Gets whether this window can open DevTools.
    /// Default: reads <see cref="Jalium.UI.Hosting.DeveloperToolsOptions.EnableDevTools"/>
    /// from the application's DI container — apps must call
    /// <c>app.UseDevTools()</c> on the built <see cref="JaliumApp"/> to opt in.
    /// Without that call F12 is inert. Subclasses (e.g. <c>DevToolsWindow</c>)
    /// can still override to force a stricter policy (hard-disable regardless
    /// of the service flag).
    /// </summary>
    protected virtual bool CanOpenDevTools
        => Jalium.UI.Hosting.DeveloperToolsResolver.IsDevToolsEnabled;

    internal static (int left, int top, int right, int bottom) ComputeCustomNcCalcSizeRect(
        (int left, int top, int right, int bottom) originalRect,
        (int left, int top, int right, int bottom) defClientRect,
        bool isMaximized,
        (int left, int top, int right, int bottom)? workAreaRect)
    {
        if (isMaximized && workAreaRect.HasValue)
        {
            return workAreaRect.Value;
        }

        if (!IsValidRect(defClientRect))
        {
            return originalRect;
        }

        // ControlzEx-style NCCALCSIZE behavior:
        // keep DefWindowProc's side/bottom frame math, but restore original top
        // so the visual system caption is removed while preserving NC semantics.
        return (defClientRect.left, originalRect.top, defClientRect.right, defClientRect.bottom);
    }

    internal static bool TryGetDwmCaptionButtonBounds(
        nint hwnd,
        out (int left, int top, int right, int bottom) captionBounds)
    {
        captionBounds = default;
        if (hwnd == nint.Zero)
        {
            return false;
        }

        int hr = DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>());
        if (hr != 0)
        {
            return false;
        }

        // DWMWA_CAPTION_BUTTON_BOUNDS is window-relative; convert to screen coordinates
        // so it can be mapped against WM_NC* lParam points (also screen coordinates).
        if (!GetWindowRect(hwnd, out RECT windowRect))
        {
            return false;
        }

        var bounds = (
            rect.left + windowRect.left,
            rect.top + windowRect.top,
            rect.right + windowRect.left,
            rect.bottom + windowRect.top);

        if (!IsValidRect(bounds))
        {
            return false;
        }

        captionBounds = bounds;
        return true;
    }

    internal static bool TryGetDwmMaxButtonBounds(
        (int left, int top, int right, int bottom) captionBounds,
        bool showMinimizeButton,
        bool showMaximizeButton,
        bool showCloseButton,
        out (int left, int top, int right, int bottom) maxButtonBounds)
    {
        maxButtonBounds = default;
        if (!showMaximizeButton || !IsValidRect(captionBounds))
        {
            return false;
        }

        int visibleCount = (showMinimizeButton ? 1 : 0) + (showMaximizeButton ? 1 : 0) + (showCloseButton ? 1 : 0);
        if (visibleCount <= 0)
        {
            return false;
        }

        double totalWidth = captionBounds.right - captionBounds.left;
        double buttonWidth = totalWidth / visibleCount;
        if (buttonWidth <= 0)
        {
            return false;
        }

        double maxLeft = showMinimizeButton
            ? captionBounds.left + buttonWidth
            : captionBounds.left;
        double maxRight = maxLeft + buttonWidth;

        int left = Math.Clamp((int)Math.Floor(maxLeft), captionBounds.left, captionBounds.right - 1);
        int right = Math.Clamp((int)Math.Ceiling(maxRight), left + 1, captionBounds.right);
        maxButtonBounds = (left, captionBounds.top, right, captionBounds.bottom);
        return IsValidRect(maxButtonBounds);
    }

    internal static bool TryGetCustomMaxButtonScreenBounds(
        (double left, double top, double right, double bottom) customMaxClientRectDip,
        double dpiScale,
        (int x, int y) clientOriginScreenPoint,
        out (int left, int top, int right, int bottom) customMaxScreenRect)
    {
        customMaxScreenRect = default;
        if (dpiScale <= 0)
        {
            return false;
        }

        double widthDip = customMaxClientRectDip.right - customMaxClientRectDip.left;
        double heightDip = customMaxClientRectDip.bottom - customMaxClientRectDip.top;
        if (widthDip <= 0 || heightDip <= 0)
        {
            return false;
        }

        int left = clientOriginScreenPoint.x + (int)Math.Floor(customMaxClientRectDip.left * dpiScale);
        int top = clientOriginScreenPoint.y + (int)Math.Floor(customMaxClientRectDip.top * dpiScale);
        int right = clientOriginScreenPoint.x + (int)Math.Ceiling(customMaxClientRectDip.right * dpiScale);
        int bottom = clientOriginScreenPoint.y + (int)Math.Ceiling(customMaxClientRectDip.bottom * dpiScale);
        customMaxScreenRect = (left, top, right, bottom);
        return IsValidRect(customMaxScreenRect);
    }

    internal static bool TryBuildMaxButtonProxyScreenPoint(
        (int x, int y) realScreenPoint,
        (int left, int top, int right, int bottom) customMaxRect,
        (int left, int top, int right, int bottom) dwmMaxRect,
        out (int x, int y) proxyScreenPoint)
    {
        proxyScreenPoint = default;
        if (!IsValidRect(customMaxRect) || !IsValidRect(dwmMaxRect))
        {
            return false;
        }

        int safeLeft = dwmMaxRect.left + 1;
        int safeTop = dwmMaxRect.top + 1;
        int safeRight = dwmMaxRect.right - 2;
        int safeBottom = dwmMaxRect.bottom - 2;
        if (safeRight < safeLeft)
        {
            safeLeft = dwmMaxRect.left;
            safeRight = dwmMaxRect.right - 1;
        }

        if (safeBottom < safeTop)
        {
            safeTop = dwmMaxRect.top;
            safeBottom = dwmMaxRect.bottom - 1;
        }

        if (safeRight < safeLeft || safeBottom < safeTop)
        {
            return false;
        }

        double customWidth = customMaxRect.right - customMaxRect.left;
        if (customWidth <= 0)
        {
            return false;
        }

        double normalizedX = (realScreenPoint.x - customMaxRect.left) / customWidth;
        normalizedX = Math.Clamp(normalizedX, 0.0, 1.0);
        int proxyX = safeLeft + (int)Math.Round((safeRight - safeLeft) * normalizedX, MidpointRounding.AwayFromZero);
        proxyX = Math.Clamp(proxyX, safeLeft, safeRight);
        int proxyY = safeTop + ((safeBottom - safeTop) / 2);
        proxyScreenPoint = (proxyX, proxyY);
        return true;
    }

    private static bool IsValidRect((int left, int top, int right, int bottom) rect)
    {
        return rect.right > rect.left && rect.bottom > rect.top;
    }

    private bool ShouldUseWin11SnapNcRouting()
    {
        return IsTitleBarVisible() &&
               IsShowMaximizeButton &&
               OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    }

    private static bool IsSnapRelevantNcMessage(uint msg)
    {
        return msg == WM_NCHITTEST ||
               msg == WM_NCMOUSEMOVE ||
               msg == WM_NCMOUSEHOVER ||
               msg == WM_NCMOUSELEAVE ||
               msg == WM_NCLBUTTONDOWN ||
               msg == WM_NCLBUTTONUP ||
               msg == WM_NCLBUTTONDBLCLK;
    }

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private void RegisterWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        _wndProcDelegate = StaticWndProc;

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0008, // CS_DBLCLKS: receive WM_*BUTTONDBLCLK messages
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(nint.Zero, IDC_ARROW),
            hbrBackground = nint.Zero, // No background brush - we handle all painting
            lpszClassName = WindowClassName
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("Failed to register window class.");
        }

        _classRegistered = true;
    }

    private static nint StaticWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (_windows.TryGetValue(hWnd, out var window))
        {
            return window.WndProc(hWnd, msg, wParam, lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    protected virtual nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            return WndProcCore(hWnd, msg, wParam, lParam);
        }
        catch (Exception)
        {
            // Never allow managed exceptions to escape the native window procedure.
            // If they do, the OS callback chain can become unstable and future
            // messages may appear to stop reaching the window entirely.
            return hWnd == nint.Zero
                ? nint.Zero
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private nint WndProcCore(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        Window? window = null;
        if (!_windows.TryGetValue(hWnd, out window))
        {
            // Unit tests invoke WndProc via reflection with hWnd = 0.
            // In that path, process against this instance directly.
            if (hWnd == nint.Zero)
            {
                window = this;
            }
            else
            {
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }

        if (window != null)
        {
            switch (msg)
            {
                case WM_GETMINMAXINFO:
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    double dpi = window._dpiScale;
                    double minW = window.MinWidth;
                    double minH = window.MinHeight;
                    double maxW = window.MaxWidth;
                    double maxH = window.MaxHeight;
                    if (!double.IsNaN(minW) && minW > 0)
                        mmi.ptMinTrackSize.X = (int)(minW * dpi);
                    if (!double.IsNaN(minH) && minH > 0)
                        mmi.ptMinTrackSize.Y = (int)(minH * dpi);
                    if (!double.IsInfinity(maxW) && maxW > 0)
                        mmi.ptMaxTrackSize.X = (int)(maxW * dpi);
                    if (!double.IsInfinity(maxH) && maxH > 0)
                        mmi.ptMaxTrackSize.Y = (int)(maxH * dpi);
                    Marshal.StructureToPtr(mmi, lParam, true);
                    return nint.Zero;
                }

                case WM_GETOBJECT:
                {
                    if ((int)lParam == -25 /* UiaRootObjectId */)
                    {
                        var peer = window.GetAutomationPeer();
                        if (peer != null)
                        {
                            var provider = Automation.Uia.UiaAccessibilityBridge.GetOrCreateProvider(peer, hWnd);
                            var result = Automation.Uia.UiaNativeMethods.UiaReturnRawElementProvider(
                                hWnd, wParam, lParam, provider);
                            if (result > 0)
                                return result;
                        }
                    }
                    break;
                }

                case WM_CLOSE:
                    // Route through Close() so Closing event can cancel
                    window.Close();
                    return nint.Zero;

                case WM_DESTROY:
                    // Just clean up the window map; quit logic is handled by
                    // Close() 鈫?Application.OnWindowClosed() based on ShutdownMode.
                    // Do NOT call PostQuitMessage here 鈥?it would kill the app
                    // when closing any window in a multi-window scenario.
                    _ = _windows.Remove(hWnd);
                    return nint.Zero;

                case WM_NCCALCSIZE:
                    // WindowStyle=None: swallow the entire non-client area so there's
                    // no DWM-drawn frame strip (otherwise WS_THICKFRAME leaves a thin
                    // white bar at the top on Win11). Edge resize is still available
                    // via WM_NCHITTEST below.
                    if (window.WindowStyle == WindowStyle.None)
                    {
                        if (wParam == nint.Zero)
                        {
                            return nint.Zero;
                        }
                        // Returning 0 with the rect unchanged tells Windows the entire
                        // window rect is client area.
                        return nint.Zero;
                    }
                    // For custom title bar:
                    // 1) call DefWindowProc first to keep native NC contract intact
                    // 2) in normal state, use full original rect as client area
                    // 3) in maximized state, clamp to monitor work area
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        // Per WM_NCCALCSIZE contract, return 0 when wParam == FALSE.
                        if (wParam == nint.Zero)
                        {
                            return nint.Zero;
                        }

                        // Save pre-DefWindowProc rect before NC calculations mutate it.
                        var ncParams = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);
                        var originalRect = (ncParams.rgrc0.left, ncParams.rgrc0.top, ncParams.rgrc0.right, ncParams.rgrc0.bottom);

                        // Let DefWindowProc compute default non-client metrics first.
                        var defResult = DefWindowProc(hWnd, msg, wParam, lParam);
                        if (defResult != nint.Zero)
                        {
                            return defResult;
                        }

                        // Re-read DefWindowProc-computed rect and only apply minimal fixups.
                        ncParams = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);
                        var defClientRect = (ncParams.rgrc0.left, ncParams.rgrc0.top, ncParams.rgrc0.right, ncParams.rgrc0.bottom);
                        var maximizedWorkArea = ((int left, int top, int right, int bottom)?)null;
                        bool isMaximized = IsZoomed(hWnd);

                        if (isMaximized)
                        {
                            // When maximized, adjust to work area to respect the taskbar.
                            var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                            MONITORINFO monitorInfo = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                            if (GetMonitorInfo(monitor, ref monitorInfo))
                            {
                                maximizedWorkArea = (monitorInfo.rcWork.left, monitorInfo.rcWork.top, monitorInfo.rcWork.right, monitorInfo.rcWork.bottom);
                            }
                        }

                        var computedRect = ComputeCustomNcCalcSizeRect(originalRect, defClientRect, isMaximized, maximizedWorkArea);
                        ncParams.rgrc0.left = computedRect.left;
                        ncParams.rgrc0.top = computedRect.top;
                        ncParams.rgrc0.right = computedRect.right;
                        ncParams.rgrc0.bottom = computedRect.bottom;

                        Marshal.StructureToPtr(ncParams, lParam, false);
                        return nint.Zero;
                    }
                    break;

                case WM_NCHITTEST:
                    // WindowStyle=None (with a native title bar style): provide edge
                    // resize hit-testing ourselves since DefWindowProc won't — the
                    // frame area has been swallowed by WM_NCCALCSIZE.
                    if (window.WindowStyle == WindowStyle.None
                        && window.TitleBarStyle != WindowTitleBarStyle.Custom)
                    {
                        var hit = window.HandleNcHitTest(lParam);
                        return hit == HTNOWHERE ? HTCLIENT : hit;
                    }
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        // Let DWM first have a chance to resolve the NC hit. This is
                        // what primes the Windows 11 Snap Layouts state machine — DWM
                        // uses this pass to notice HTMAXBUTTON in subsequent NC mouse
                        // messages and arm the flyout timer. Without this call, the
                        // flyout never appears even when we return HTMAXBUTTON. If
                        // DWM claims the hit (typical for caption resize handles),
                        // honor its result; otherwise fall through to our custom
                        // button / caption / client logic.
                        if (DwmDefWindowProc(hWnd, msg, wParam, lParam, out nint dwmHit) && dwmHit != nint.Zero)
                        {
                            return dwmHit;
                        }

                        var customHitResult = window.HandleNcHitTest(lParam);
                        if (customHitResult != HTNOWHERE)
                        {
                            return customHitResult;
                        }
                    }
                    break;

                case WM_NCMOUSEMOVE:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        window.OnNcMouseMove(wParam, lParam);
                        // Let DefWindowProc handle NC hover tracking so Windows 11 can
                        // arm the Snap Layouts flyout timer. Do not swallow this message.
                    }
                    break;

                case WM_NCMOUSEHOVER:
                    // Let DefWindowProc forward this to DWM/Shell for Snap Layouts.
                    break;

                case WM_NCMOUSELEAVE:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        window.OnNcMouseLeave();
                    }
                    break;

                case WM_NCLBUTTONDOWN:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.OnNcLButtonDown(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCLBUTTONUP:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.OnNcLButtonUp(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCLBUTTONDBLCLK:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.OnNcLButtonDblClk(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCRBUTTONDOWN:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.ShouldUseWin11SnapNcRouting())
                        {
                            break;
                        }

                        int ncRbDownX = (short)(lParam.ToInt64() & 0xFFFF);
                        int ncRbDownY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        POINT ncRbDownPt = new() { X = ncRbDownX, Y = ncRbDownY };
                        _ = ScreenToClient(hWnd, ref ncRbDownPt);
                        if (window.GetTitleBarButtonAtPoint(new Point(ncRbDownPt.X / window._dpiScale, ncRbDownPt.Y / window._dpiScale)) != null)
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCRBUTTONUP:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        int ncRbX = (short)(lParam.ToInt64() & 0xFFFF);
                        int ncRbY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        POINT ncRbPt = new() { X = ncRbX, Y = ncRbY };
                        _ = ScreenToClient(hWnd, ref ncRbPt);
                        // Suppress right-click on title bar buttons
                        if (window.GetTitleBarButtonAtPoint(new Point(ncRbPt.X / window._dpiScale, ncRbPt.Y / window._dpiScale)) != null)
                        {
                            return nint.Zero;
                        }

                        // Show system menu on caption area right-click
                        int ncHitTest = (int)wParam.ToInt64();
                        if (ncHitTest == HTCAPTION || ncHitTest == HTSYSMENU)
                        {
                            window.ShowSystemMenu(ncRbX, ncRbY);
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_SIZE:
                    int sizeType = (int)wParam.ToInt64();
                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    // Synchronize WindowState with the actual window state
                    // This handles system-forced state changes (Win+Down, taskbar click, etc.)
                    // Use _isSyncingWindowState to prevent OnWindowStateChanged from
                    // calling ShowWindow again (which would cause infinite WM_SIZE loop).
                    window._isSyncingWindowState = true;
                    try
                    {
                        switch (sizeType)
                        {
                            case SIZE_MAXIMIZED:
                                if (window.WindowState != WindowState.Maximized)
                                {
                                    window.WindowState = WindowState.Maximized;
                                }
                                break;
                            case SIZE_RESTORED:
                                // While in fullscreen we intentionally resize to the
                                // monitor bounds, which produces a SIZE_RESTORED
                                // message. Do NOT drop the FullScreen state in that case.
                                if (!window._isFullScreen && window.WindowState != WindowState.Normal)
                                {
                                    window.WindowState = WindowState.Normal;
                                }
                                break;
                            case SIZE_MINIMIZED:
                                if (window.WindowState != WindowState.Minimized)
                                {
                                    window.WindowState = WindowState.Minimized;
                                }
                                return nint.Zero; // finally block ensures _isSyncingWindowState is reset
                        }
                    }
                    finally
                    {
                        window._isSyncingWindowState = false;
                    }

                    window.OnSizeChanged(width, height);

                    // For maximize/restore, post a deferred repaint message
                    if (sizeType is SIZE_MAXIMIZED or SIZE_RESTORED)
                    {
                        _ = PostMessage(hWnd, WM_APP_REPAINT, nint.Zero, nint.Zero);
                    }
                    return nint.Zero;

                case WM_MOVING:
                    break; // Allow default processing

                case WM_MOVE:
                {
                    // Sync Left/Top from native window rect (outer bounds)
                    if (GetWindowRect(hWnd, out RECT windowRect))
                    {
                        window._isSyncingPosition = true;
                        try
                        {
                            window.Left = windowRect.left / window._dpiScale;
                            window.Top = windowRect.top / window._dpiScale;
                        }
                        finally
                        {
                            window._isSyncingPosition = false;
                        }
                    }
                    window.OnLocationChanged(EventArgs.Empty);
                    // Re-detect refresh rate (window may have moved to a different monitor)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());
                    return nint.Zero;
                }

                case WM_DISPLAYCHANGE:
                    // Display settings changed (resolution, refresh rate, etc.)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());
                    return nint.Zero;

                case WM_DPICHANGED:
                {
                    // Per-monitor DPI change (window moved to different DPI monitor)
                    var oldDpiScale = window._dpiScale;
                    uint newDpi = (uint)((wParam.ToInt64() >> 16) & 0xFFFF);
                    window._dpiScale = newDpi / 96.0;
                    FrameworkElement.LayoutDpiScale = window._dpiScale;

                    // Update DPI BEFORE SetWindowPos: SetWindowPos triggers WM_SIZE
                    // synchronously, which calls Resize() → CreateSnapshotResources().
                    // Snapshot bitmaps bake DPI into their D2D1_BITMAP_PROPERTIES1,
                    // so dpiX_/dpiY_ must already reflect the new DPI at that point.
                    window.RenderTarget?.SetDpi((float)newDpi, (float)newDpi);

                    // Windows provides a suggested window rect at the new DPI
                    var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
                    _ = SetWindowPos(hWnd, nint.Zero,
                        suggestedRect.left, suggestedRect.top,
                        suggestedRect.right - suggestedRect.left,
                        suggestedRect.bottom - suggestedRect.top,
                        SWP_NOZORDER | SWP_NOACTIVATE);

                    // Keep DWM extended-frame hover region in sync with new DPI.
                    window.UpdateCustomTitleBarFrameMargins();

                    // Re-detect refresh rate (different monitor may have different rate)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());

                    window.OnDpiChanged(new DpiChangedEventArgs(
                        new DpiScale(oldDpiScale, oldDpiScale),
                        new DpiScale(window._dpiScale, window._dpiScale)));

                    return nint.Zero;
                }

                case WM_APP_REPAINT:
                    // Deferred repaint after size change
                    _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    return nint.Zero;

                case WM_SIZING:
                    // IMPORTANT: do not derive layout size from WM_SIZING RECT.
                    // WM_SIZING provides outer window bounds (includes non-client frame),
                    // while our layout uses client-area size (from WM_SIZE). Mixing these
                    // two sources causes width oscillation during drag resize and makes
                    // title bar content appear to shift left/right.
                    if (window._isSizing)
                    {
                        _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    }
                    break;

                case WM_ENTERSIZEMOVE:
                    window._isSizing = true;
                    // Disable VSync during resize for faster frame updates
                    window.RenderTarget?.SetVSyncEnabled(false);
                    return nint.Zero;

                case WM_EXITSIZEMOVE:
                    window._isSizing = false;
                    // Re-enable VSync after resize
                    window.RenderTarget?.SetVSyncEnabled(true);
                    // Do final resize to ensure correct buffer size (physical pixels)
                    int finalPhysW = (int)(window.Width * window._dpiScale);
                    int finalPhysH = (int)(window.Height * window._dpiScale);
                    window.TryResizeRenderTarget(finalPhysW, finalPhysH, "ExitSizeMoveResize");
                    // Force a final repaint with correct buffer size
                    _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    return nint.Zero;

                case WM_ERASEBKGND:
                    // Return 1 to tell Windows we've handled background erase
                    // This prevents flickering during resize
                    return 1;

                case WM_PAINT:
                    window.OnPaint();
                    return nint.Zero;

                // Keyboard input
                case WM_KEYDOWN:
                    if (IsShellReservedVirtualKey(wParam))
                    {
                        break;
                    }

                    bool keyDownHandled = window.OnKeyDown(wParam, lParam);
                    if (keyDownHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_KEYUP:
                    if (IsShellReservedVirtualKey(wParam))
                    {
                        break;
                    }

                    bool keyUpHandled = window.OnKeyUp(wParam, lParam);
                    if (keyUpHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_SYSKEYDOWN:
                    bool sysKeyDownHandled = window.OnKeyDown(wParam, lParam);
                    if (sysKeyDownHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_SYSKEYUP:
                    bool sysKeyUpHandled = window.OnKeyUp(wParam, lParam);
                    if (sysKeyUpHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_CHAR:
                    window.OnChar(wParam, lParam);
                    return nint.Zero;

                // IME input
                case WM_IME_STARTCOMPOSITION:
                    if (window.CanHandleImeMessages())
                    {
                        window.OnImeStartComposition();
                        return nint.Zero;
                    }

                    break;

                case WM_IME_ENDCOMPOSITION:
                    if (window.CanHandleImeMessages() || InputMethod.IsComposing)
                    {
                        window.OnImeEndComposition();
                        return nint.Zero;
                    }

                    break;

                case WM_IME_COMPOSITION:
                    if (window.CanHandleImeMessages() && window.OnImeComposition(lParam))
                    {
                        return nint.Zero;
                    }

                    break;

                case WM_IME_CHAR:
                    // IME character - let it fall through to default processing
                    // or handle specially if needed
                    break;

                // Cursor
                case WM_SETCURSOR:
                    if (window.OnSetCursor(lParam))
                    {
                        return 1; // Return TRUE to indicate we handled the message
                    }
                    break;

                // Mouse input
                case Win32PointerInterop.WM_POINTERDOWN:
                case Win32PointerInterop.WM_POINTERUPDATE:
                case Win32PointerInterop.WM_POINTERUP:
                    window.OnPointerMessage(msg, wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERWHEEL:
                case Win32PointerInterop.WM_POINTERHWHEEL:
                    window.OnPointerWheel(wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERCAPTURECHANGED:
                    window.OnPointerCaptureChanged(wParam);
                    return nint.Zero;

                case WM_MOUSEMOVE:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                    {
                        return nint.Zero;
                    }
                    window.OnMouseMove(wParam, lParam);
                    return nint.Zero;

                // NOTE: Do NOT filter promoted mouse messages here.
                // OnPointerMessage already returns early for Mouse-kind pointers,
                // so WM_xBUTTON* / WM_MOUSEWHEEL are the sole delivery path for
                // mouse clicks on systems with WM_POINTER support.
                case WM_LBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_LBUTTONDBLCLK:
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_LBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_RBUTTONDBLCLK:
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_RBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_MBUTTONDBLCLK:
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_MBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MOUSEWHEEL:
                    window.OnMouseWheel(wParam, lParam);
                    return nint.Zero;

                case WM_MOUSELEAVE:
                    window.OnMouseLeave();
                    return nint.Zero;

                case WM_CAPTURECHANGED:
                    window._inputDispatcher.HandleNativeCaptureChanged();
                    return nint.Zero;

                case WM_CANCELMODE:
                    window.OnCancelMode();
                    return nint.Zero;

                case WM_ACTIVATE:
                    int activateState = (int)(wParam.ToInt64() & 0xFFFF);
                    window.OnActivateChanged(activateState, lParam);
                    break;

                case WM_SETFOCUS:
                    window.OnSetFocus();
                    // Notify UIA that this window received focus so Narrator can announce it.
                    // Must be deferred — UIA COM calls cannot be made during
                    // input-synchronous messages (WM_SETFOCUS) or RPC_E_CANTCALLOUT occurs.
                    if (OperatingSystem.IsWindows())
                    {
                        window.Dispatcher.BeginInvoke(() =>
                        {
                            var focusPeer = window.GetAutomationPeer();
                            if (focusPeer != null)
                                Automation.Uia.UiaAccessibilityBridge.RaiseFocusChanged(focusPeer);
                        });
                    }
                    break;

                case WM_KILLFOCUS:
                    window.OnKillFocus(wParam);
                    break;

                case WM_SETTINGCHANGE:
                case WM_THEMECHANGED:
                    window.OnSystemSettingsChanged(EventArgs.Empty);
                    break;

                case WM_QUERYENDSESSION:
                {
                    // lParam bit 0 set = shutdown, else logoff
                    var reason = (lParam.ToInt64() & 1) != 0
                        ? ReasonSessionEnding.Shutdown
                        : ReasonSessionEnding.Logoff;
                    var args = new SessionEndingCancelEventArgs(reason);
                    // Window-level handler first, then application-level so either
                    // can cancel the end-session request.
                    window.OnSessionEnding(args);
                    Application.Current?.RaiseSessionEnding(args);
                    // Return 0 to prevent, non-zero to allow
                    return args.Cancel ? nint.Zero : 1;
                }

                case WM_ENDSESSION:
                    if (wParam != nint.Zero)
                    {
                        // Session is definitely ending — close gracefully
                        window.Close();
                    }
                    return nint.Zero;
            }
        }

        if (hWnd == nint.Zero)
        {
            return nint.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnMouseLeave()
    {
        _inputDispatcher.HandleMouseLeave();
    }

    /// <summary>
    /// Handles window size changes. Width/height are physical pixels from WM_SIZE.
    /// </summary>
    private void OnSizeChanged(int physicalWidth, int physicalHeight)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0)
        {
            return;
        }

        var previousWidth = Width;
        var previousHeight = Height;

        // Convert physical pixels to DIPs for layout
        Width = physicalWidth / _dpiScale;
        Height = physicalHeight / _dpiScale;

        // Always use WM_SIZE client dimensions as the single source of truth.
        // This keeps layout and swapchain size stable during drag resize.
        TryResizeRenderTarget(physicalWidth, physicalHeight, "OnSizeChanged");

        // After swap chain resize, DXGI discards all buffer contents.
        // Must request full invalidation so RenderFrame takes the full-render path
        // instead of partial dirty-rect rendering (which would leave stale/black areas).
        RequestFullInvalidation();
        InvalidateMeasure();

        bool widthChanged = previousWidth != Width;
        bool heightChanged = previousHeight != Height;
        if (widthChanged || heightChanged)
        {
            OnSizeChanged(new SizeChangedEventArgs(
                new SizeChangedInfo(this, new Size(previousWidth, previousHeight), widthChanged, heightChanged)));
        }
    }

    private void TryResizeRenderTarget(int physicalWidth, int physicalHeight, string stage)
    {
        var renderTarget = RenderTarget;
        if (renderTarget == null || !renderTarget.IsValid)
        {
            // Render target not yet created (e.g., first RESIZE event on Android arrives
            // before EnsureRenderTarget succeeded). Try to create it now that we have
            // valid dimensions — Width/Height are already updated by the caller.
            EnsureRenderTarget();
            renderTarget = RenderTarget;
            if (renderTarget == null || !renderTarget.IsValid)
                return;
        }

        try
        {
            _debugHud.OnResize(); renderTarget.Resize(physicalWidth, physicalHeight);
        }
        catch (RenderPipelineException ex)
        {
            if (TryRecoverFromRenderPipelineFailure(ex, stage))
            {
                return;
            }

            if (IsRecoverableRenderPipelineException(ex))
            {
                ScheduleRenderRecoveryRetry();
                return;
            }

            LogRenderFailure(ex, stage);
        }
    }

    private static bool IsRecoverableRenderPipelineException(RenderPipelineException exception)
        => exception.Result is JaliumResult.DeviceLost
            or JaliumResult.InvalidState
            or JaliumResult.ResourceCreationFailed
            || (exception.Result == JaliumResult.Unknown &&
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase));

    private static bool IsRecoverableRenderPipelineFailure(JaliumResult result, string stage)
        => result is JaliumResult.DeviceLost
            or JaliumResult.InvalidState
            or JaliumResult.ResourceCreationFailed
            || (result == JaliumResult.Unknown &&
                string.Equals(stage, "Create", StringComparison.OrdinalIgnoreCase));

    private bool TryRecoverFromRenderPipelineFailure(RenderPipelineException exception, string stage)
    {
        if (!IsRecoverableRenderPipelineException(exception) ||
            Handle == nint.Zero ||
            _isClosing ||
            _renderRecoveryInProgress)
        {
            return false;
        }

        _renderRecoveryInProgress = true;

        try
        {
            RenderBackend failedBackend = RenderTarget?.Backend ?? RenderBackend.Auto;
            if (failedBackend == RenderBackend.Auto &&
                Enum.TryParse<RenderBackend>(exception.Backend, ignoreCase: true, out var parsedBackend))
            {
                failedBackend = parsedBackend;
            }

            _drawingContext?.ClearCache();
            _drawingContext = null;

            RenderTarget?.Dispose();
            RenderTarget = null;

            if (exception.Result == JaliumResult.DeviceLost &&
                _consecutiveRecoverableRenderFailures >= DeviceLostBackendFallbackThreshold &&
                failedBackend != RenderBackend.Auto)
            {
                if (!TryAdvanceRenderBackendFallback(failedBackend) &&
                    failedBackend == RenderBackend.D3D12)
                {
                    EnableD3D12WarpFallback();
                }
            }

            bool forceNewContext = exception.Result == JaliumResult.DeviceLost ||
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase);
            EnsureRenderTarget(forceNewContext: forceNewContext);
            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                return false;
            }

            lock (_dirtyLock)
            {
                _dirtyElements.Clear();
                _dirtyFreeRects.Clear();
                _fullInvalidation = true;
            }

            InvalidateMeasure();
            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            return true;
        }
        catch (RenderPipelineException recoveryException) when (IsRecoverableRenderPipelineException(recoveryException))
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        catch (Exception recoveryException)
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        finally
        {
            _renderRecoveryInProgress = false;
        }
    }

    private bool TryRecoverFromRenderPipelineFailure(JaliumResult result, string stage)
    {
        if (!IsRecoverableRenderPipelineFailure(result, stage) ||
            Handle == nint.Zero ||
            _isClosing ||
            _renderRecoveryInProgress)
        {
            return false;
        }

        _renderRecoveryInProgress = true;

        try
        {
            RenderBackend failedBackend = RenderTarget?.Backend ?? RenderBackend.Auto;

            _drawingContext?.ClearCache();
            _drawingContext = null;

            RenderTarget?.Dispose();
            RenderTarget = null;

            if (result == JaliumResult.DeviceLost &&
                _consecutiveRecoverableRenderFailures >= DeviceLostBackendFallbackThreshold &&
                failedBackend != RenderBackend.Auto)
            {
                if (!TryAdvanceRenderBackendFallback(failedBackend) &&
                    failedBackend == RenderBackend.D3D12)
                {
                    EnableD3D12WarpFallback();
                }
            }

            bool forceNewContext = result == JaliumResult.DeviceLost ||
                string.Equals(stage, "Create", StringComparison.OrdinalIgnoreCase);
            EnsureRenderTarget(forceNewContext: forceNewContext);
            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                return false;
            }

            lock (_dirtyLock)
            {
                _dirtyElements.Clear();
                _dirtyFreeRects.Clear();
                _fullInvalidation = true;
            }

            InvalidateMeasure();
            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            return true;
        }
        catch (RenderPipelineException recoveryException) when (IsRecoverableRenderPipelineException(recoveryException))
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        catch (Exception recoveryException)
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        finally
        {
            _renderRecoveryInProgress = false;
        }
    }

    private void ScheduleRenderAfterRecovery()
    {
        if (Handle == nint.Zero || _dispatcher == null)
        {
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    private void MarkRecoverableRenderFailure()
    {
        _consecutiveRecoverableRenderFailures = Math.Min(_consecutiveRecoverableRenderFailures + 1, 8);
        if (_consecutiveRecoverableRenderFailures <= 1)
        {
            _renderRecoveryRetryDelayMs = RenderRecoveryRetryInitialDelayMs;
            return;
        }

        _renderRecoveryRetryDelayMs = Math.Min(RenderRecoveryRetryMaxDelayMs, _renderRecoveryRetryDelayMs * 2);
    }

    private void ResetRenderRecoveryBackoff()
    {
        _consecutiveRecoverableRenderFailures = 0;
        _renderRecoveryRetryDelayMs = RenderRecoveryRetryInitialDelayMs;
        _renderRecoveryRetryTimer?.Stop();
    }

    private bool HandleRecoverableRenderPipelineFailure(RenderPipelineException exception, string stage)
    {
        if (!IsRecoverableRenderPipelineException(exception))
        {
            return false;
        }

        // Preserve a full repaint request when a frame dies after we already
        // committed dirty state, so the eventual retry never resumes from a
        // partially-rendered dirty set.
        lock (_dirtyLock)
        {
            _fullInvalidation = true;
        }

        MarkRecoverableRenderFailure();

        if (TryRecoverFromRenderPipelineFailure(exception, stage))
        {
            return true;
        }

        ScheduleRenderRecoveryRetry(escalateBackoff: false);
        return true;
    }

    private bool CompleteEndDrawOrHandleFailure()
    {
        var renderTarget = RenderTarget;
        if (renderTarget == null || !renderTarget.IsValid)
        {
            return false;
        }

        JaliumResult endResult = renderTarget.TryEndDraw();
        if (endResult == JaliumResult.Ok)
        {
            _debugHud.OnEndDraw();
            // GPU resource snapshot (glyph atlas, path cache, textures) for
            // the Perf tab. Best-effort — a backend that hasn't implemented
            // the query just leaves LatestGpuSnapshot unchanged.
            if (renderTarget.TryQueryGpuStats(out var gpuStats))
            {
                Jalium.UI.Diagnostics.RenderDiagnostics.PublishGpuSnapshot(
                    gpuStats.GlyphSlotsUsed, gpuStats.GlyphSlotsTotal, gpuStats.GlyphBytes,
                    gpuStats.PathEntries, gpuStats.PathBytes,
                    gpuStats.TextureCount, gpuStats.TextureBytes);
            }
            _lastRenderTicks = Environment.TickCount64;
            ResetRenderRecoveryBackoff();
            return true;
        }

        if (IsRecoverableRenderPipelineFailure(endResult, "End"))
        {
            lock (_dirtyLock)
            {
                _fullInvalidation = true;
            }

            MarkRecoverableRenderFailure();

            if (!TryRecoverFromRenderPipelineFailure(endResult, "End"))
            {
                ScheduleRenderRecoveryRetry(escalateBackoff: false);
            }

            return false;
        }

        throw new RenderPipelineException(
            stage: "End",
            result: endResult,
            resultCode: (int)endResult,
            hwnd: Handle,
            width: renderTarget.Width,
            height: renderTarget.Height,
            dpiX: (float)(_dpiScale * 96.0),
            dpiY: (float)(_dpiScale * 96.0),
            backend: renderTarget.Backend.ToString());
    }

    private void ScheduleRenderRecoveryRetry(bool escalateBackoff = true)
    {
        if (Handle == nint.Zero || _dispatcher == null || _isClosing)
        {
            return;
        }

        if (escalateBackoff)
        {
            MarkRecoverableRenderFailure();
        }

        _renderRecoveryRetryTimer ??= CreateRenderRecoveryRetryTimer();
        _renderRecoveryRetryTimer.Interval = TimeSpan.FromMilliseconds(_renderRecoveryRetryDelayMs);
        if (!_renderRecoveryRetryTimer.IsEnabled)
        {
            _renderRecoveryRetryTimer.Start();
        }
    }

    private DispatcherTimer CreateRenderRecoveryRetryTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_renderRecoveryRetryDelayMs)
        };
        timer.Tick += OnRenderRecoveryRetryTimerTick;
        return timer;
    }

    private void OnRenderRecoveryRetryTimerTick(object? sender, EventArgs e)
    {
        _renderRecoveryRetryTimer?.Stop();

        if (Handle == nint.Zero || _isClosing)
        {
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            _dispatcher?.BeginInvokeCritical(ProcessRender);
        }
    }

    private void StopRenderRecoveryRetry()
    {
        if (_renderRecoveryRetryTimer == null)
        {
            return;
        }

        _renderRecoveryRetryTimer.Stop();
        _renderRecoveryRetryTimer.Tick -= OnRenderRecoveryRetryTimerTick;
        _renderRecoveryRetryTimer = null;
    }

    private RenderTargetDrawingContext? _drawingContext;
    private UIElement? _lastMouseOverElement;
    private UIElement? _lastHitTestElement;
    private readonly List<UIElement> _mousePressedChain = [];
    private readonly List<UIElement> _keyboardPressedChain = [];
    private nint _detachedImeContext;
    private bool _imeContextDetached;
    private bool _keyboardPressActive;
    private const int EscapeReactivateSuppressionMs = 250;

    /// <summary>
    /// WM_PAINT handler. Used for OS-initiated repaints (window uncovered, initial show, resize).
    /// Validates the update region via BeginPaint/EndPaint and delegates to RenderFrame.
    /// </summary>
    private void OnPaint()
    { _debugHud.OnPaint();
        PAINTSTRUCT ps = new();
        _ = BeginPaint(Handle, out ps);
        RenderFrame();
        EndPaint(Handle, ref ps);
    }

    /// <summary>
    /// Processes a scheduled render from the Dispatcher queue.
    /// This is the primary render path 鈥?called via Dispatcher.BeginInvokeCritical
    /// after InvalidateMeasure/InvalidateArrange/InvalidateVisual.
    ///
    /// WPF-style: rendering is a Dispatcher operation, not WM_PAINT.
    /// When DispatcherTimer ticks (animations) call BeginInvoke(RaiseTick),
    /// the tick handler invalidates elements which calls BeginInvokeCritical(ProcessRender).
    /// ProcessQueue drains all items in FIFO order, so ProcessRender runs
    /// immediately after all ticks in the same batch 鈥?no WM_PAINT starvation.
    /// </summary>
    private void ProcessRender()
    { _debugHud.OnProcessRender();
        ClearRenderFlag(RenderFlag_Scheduled);
        if (Handle == nint.Zero) return;

        // Dispose any pending throttle timer from a previous rate-limit cycle.
        var throttleTimer = _renderThrottleTimer;
        _renderThrottleTimer = null;
        throttleTimer?.Dispose();

        // No rate-limiting — render as fast as possible.
        RenderFrame();
    }

    private void ScheduleDeferredRender(int delayMs)
    {
        if (Handle == nint.Zero || _dispatcher == null)
        {
            return;
        }

        if (!TrySetRenderFlag(RenderFlag_Scheduled))
        {
            return;
        }

        var deferredTimer = new Timer(_ =>
        {
            _dispatcher?.BeginInvokeCritical(ProcessRender);
        }, null, Math.Max(1, delayMs), Timeout.Infinite);

        var previousTimer = Interlocked.Exchange(ref _renderThrottleTimer, deferredTimer);
        previousTimer?.Dispose();
    }

    private bool TryBeginDrawOrScheduleRetry()
    {
        if (RenderTarget?.TryBeginDraw() == true)
        {
            return true;
        }

        _debugHud.OnBeginFail();
        if (DebugRender) System.Diagnostics.Debug.WriteLine("[TryBeginDraw] FAIL: GPU busy");
        if (CompositionTarget.IsActive)
        {
            SetRenderFlag(RenderFlag_DirtyBetween);
        }
        else
        {
            ScheduleDeferredRender(GpuBusyRetryDelayMs);
        }

        return false;
    }

    /// <summary>
    /// Core rendering logic shared by both Dispatcher-based and WM_PAINT paths.
    /// Performs layout, submits dirty rects, and renders the visual tree.
    ///
    /// Retained mode rendering:
    /// - When nothing is dirty, skip the frame entirely (GPU idle).
    /// - When dirty elements exist, push an ALIASED D2D clip to the dirty region
    ///   and render only that area. ALIASED mode creates hard pixel boundaries
    ///   with no semi-transparent edge artifacts (unlike PER_PRIMITIVE mode).
    /// - Present1 dirty rects tell DWM which areas changed; FLIP_SEQUENTIAL
    ///   copies non-dirty areas from the previously presented buffer automatically.
    /// - Falls back to full render on first frame, resize, theme change, etc.
    /// - ProcessRender rate-limits to display refresh rate when no animation is active,
    ///   preventing GPU saturation from rapid input events (scrolling, mouse drag).
    /// </summary>
    private int _renderFrameLogCount;
    private void RenderFrame()
    { _debugHud.OnRenderFrame();
        if (HasRenderFlag(RenderFlag_Rendering)) return;
        SetRenderFlag(RenderFlag_Rendering);
        ClearRenderFlag(RenderFlag_Requested);

        try
        {
            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                EnsureRenderTarget();
            }

            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                if (_renderFrameLogCount++ < 3)
                    Console.Error.WriteLine($"[RenderFrame] SKIP: RT still null/invalid after ensure");
                return;
            }

            // Perform layout before rendering (queue-based: only dirty elements).
            // UpdateLayout may trigger further invalidations via AddDirtyElement.
            UpdateLayout();
            _debugHud.MarkLayout();

            // ── Compute dirty region from accumulated dirty elements ──
            // Check dirty AFTER UpdateLayout so layout-triggered invalidations are included.
            bool fullInvalidation;
            DirtyRegionAggregator? aggregator = null;
            // D3D12 now uses retained-mode dirty rects by default.
            // If a specific driver shows stale-buffer artifacts, the old behavior can be
            // restored with JALIUM_D3D12_FORCE_FULL_REPLAY=1.
            bool requiresFullReplay = RenderTarget.Backend == RenderBackend.D3D12 && ForceFullReplayForD3D12;
            lock (_dirtyLock)
            {
                // Idle frame skip: if nothing is dirty and no explicit full invalidation,
                // skip the frame entirely regardless of backend.  requiresFullReplay means
                // "when we DO render, repaint everything" — it must NOT prevent skipping
                // frames where there is genuinely nothing to render.
                if (!_fullInvalidation && _dirtyElements.Count == 0 && _dirtyFreeRects.Count == 0)
                {
                    _debugHud.OnSkipped();
                    if (DebugRender) System.Diagnostics.Debug.WriteLine("[RenderFrame] SKIP: no dirty, no fullInvalidation");
                    return;
                }

                fullInvalidation = _fullInvalidation || requiresFullReplay;
                if (DebugRender)
                {
                    var reason = _fullInvalidation ? "_fullInvalidation" : requiresFullReplay ? "forceFullReplay" : "dirty";
                    System.Diagnostics.Debug.WriteLine($"[RenderFrame] path={( fullInvalidation ? "FULL" : "PARTIAL")} reason={reason} dirtyCount={_dirtyElements.Count} fullInv={_fullInvalidation}");
                    if (_dirtyElements.Count > 0 && _dirtyElements.Count <= 10)
                    {
                        foreach (var (el, entry) in _dirtyElements)
                            System.Diagnostics.Debug.WriteLine($"  dirty: {el.GetType().Name} bounds={entry.PreLayoutBounds}");
                    }
                }

                if (!fullInvalidation)
                {
                    aggregator = ComputeDirtyRegions();
                }
                _debugHud.SetDirtyInfo(_dirtyElements.Count, aggregator?.GetBoundingBox() ?? Rect.Empty);

                // NOTE: do NOT clear _dirtyElements here.  BeginDraw may fail
                // (GPU still busy with the previous frame) and we need to preserve
                // dirty state so the next attempt can render them.
            }

            var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
            _drawingContext ??= new RenderTargetDrawingContext(RenderTarget, context);
            _drawingContext.SimplifyGpuEffects = _isSizing;
            _debugHud.SetBackend(RenderTarget.Backend.ToString());
            _debugHud.SetEngine(RenderTarget.RenderingEngine.ToString());
            _debugHud.SetWindowSize(RenderTarget.Width, RenderTarget.Height);
            _debugHud.SetDpiScale((float)_dpiScale);

            // VSync disabled — let the compositor / display handle pacing.
            // This minimizes input-to-display latency for all scenarios.
            RenderTarget.SetVSyncEnabled(false);

            var windowBounds = new Rect(0, 0, ActualWidth, ActualHeight);

            if (fullInvalidation)
            {
                // ── Full render path (first frame, resize, theme change) ──
                // Full render refreshes the CURRENT buffer only. The other N-1
                // swap chain buffers still have stale content. Seed the dirty
                // history with the full window rect so the next N-1 partial frames
                // repaint everything on those buffers.
                SeedDirtyHistoryFullWindow(windowBounds);
                RenderTarget.SetFullInvalidation();

                // TryBeginDraw: non-blocking.  If the GPU hasn't finished the
                // previous frame for this swap chain buffer, skip this frame
                // and let the UI thread process input messages instead.
                // Dirty elements are preserved and will be rendered next time.
                if (!TryBeginDrawOrScheduleRetry())
                {
                    return;
                }

                // GPU is ready — commit dirty state now.
                lock (_dirtyLock) { _dirtyElements.Clear(); _dirtyFreeRects.Clear(); _fullInvalidation = false; }

                try
                {
                    _debugHud.OnFull();
                    ClearBackground();
                    _drawingContext.Offset = Point.Zero;
                    Render(_drawingContext);
                    _debugHud.MarkRender();
                    DevToolsOverlay?.DrawOverlay(_drawingContext);
                    OnRender(RenderTarget);
                    if (!CompleteEndDrawOrHandleFailure()) { return; }
                    _debugHud.UpdateOverlay(_debugHudOverlay);
                }
                catch (RenderPipelineException ex)
                {
                    if (RenderTarget.IsDrawing)
                        try { _ = RenderTarget.TryEndDraw(); } catch { }
                    if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
                    {
                        return;
                    }

                    throw;
                }
                catch
                {
                    if (RenderTarget.IsDrawing)
                        try { RenderTarget.EndDraw(); } catch { }
                    throw;
                }
            }
            else if (aggregator == null || aggregator.IsEmpty)
            {
                // Dirty elements exist but their visible bounds are outside the window
                // (e.g., ProgressBar animating off-screen). Nothing to render — GPU idle.
                lock (_dirtyLock) { _dirtyElements.Clear(); _dirtyFreeRects.Clear(); _fullInvalidation = false; }
                return;
            }
            else
            {
                // ── Retained mode partial render ──
                // Capture this frame's raw rects BEFORE folding in history — we
                // store the raw snapshot (what actually changed this frame) in
                // the ring buffer; history folding is applied to the working
                // aggregator only, so we don't compound history indefinitely.
                var rawSnapshot = aggregator.Rects.ToArray();

                // Fold in dirty regions from the last N-1 frames so that every
                // FLIP_SEQUENTIAL buffer has its stale pixels repainted. Because
                // aggregator absorbs redundant rects the fold is idempotent for
                // regions that haven't changed.
                for (int h = 0; h < DirtyHistoryCount; h++)
                {
                    var history = _dirtyHistory[h];
                    if (history == null) continue;
                    foreach (var r in history) aggregator.Add(r);
                }
                _dirtyHistory[_dirtyHistoryIndex] = rawSnapshot;
                _dirtyHistoryIndex = (_dirtyHistoryIndex + 1) % DirtyHistoryCount;

                // DPI-aware margin. Anti-aliased edges on high-density displays
                // can exceed 2 device pixels; scale the DIP margin accordingly
                // to avoid subpixel leaks outside the clip.
                double margin = Math.Max(2.0, 2.0 * Math.Max(1.0, _dpiScale));
                aggregator.Inflate(margin, windowBounds);

                if (aggregator.IsEmpty)
                {
                    lock (_dirtyLock) { _dirtyElements.Clear(); _dirtyFreeRects.Clear(); _fullInvalidation = false; }
                    return;
                }

                // ── 50 % area check, now measured against the TRUE covered
                //    pixel area rather than the bounding box. A caret at (10,10)
                //    + a progress bar at (600,400) used to balloon the bounding
                //    box to the whole window; with union-area it measures only
                //    the two small regions. Promotion fires only when partial
                //    redraw would actually touch > half the pixels of a full frame.
                double windowArea = ActualWidth * ActualHeight;
                double realArea = aggregator.ComputeRealArea();
                bool promoteToFull = windowArea > 0 && realArea > windowArea * 0.5;
                _debugHud.SetDirtyRegionStats(
                    aggregator.Count,
                    windowArea > 0 ? realArea / windowArea : 0);

                if (promoteToFull)
                {
                    // Promoted to full render. Seed history with full window
                    // (same rationale as the main full invalidation path).
                    SeedDirtyHistoryFullWindow(windowBounds);
                    RenderTarget.SetFullInvalidation();
                    if (!TryBeginDrawOrScheduleRetry()) { return; }
                    lock (_dirtyLock) { _dirtyElements.Clear(); _dirtyFreeRects.Clear(); _fullInvalidation = false; }
                    try
                    {
                        _debugHud.OnPromoted();
                        ClearBackground();
                        _drawingContext.Offset = Point.Zero;
                        Render(_drawingContext);
                        _debugHud.MarkRender();
                        DevToolsOverlay?.DrawOverlay(_drawingContext);
                        _debugHud.UpdateOverlay(_debugHudOverlay);
                        OnRender(RenderTarget);
                        if (!CompleteEndDrawOrHandleFailure()) { return; }
                    }
                    catch (RenderPipelineException ex)
                    {
                        if (RenderTarget.IsDrawing)
                            try { _ = RenderTarget.TryEndDraw(); } catch { }
                        if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
                        {
                            return;
                        }

                        throw;
                    }
                    catch
                    {
                        if (RenderTarget.IsDrawing)
                            try { RenderTarget.EndDraw(); } catch { }
                        throw;
                    }
                }
                else
                {
                    // Submit every rect to the native RT — D3D12 uses them for
                    // Present1 DirtyRects (DWM copies the rest from the previous
                    // buffer), and the bounding box is still used as the D2D
                    // scissor clip because D2D clip stack takes a single rect.
                    foreach (var r in aggregator.EnumerateRects())
                    {
                        RenderTarget.AddDirtyRect(
                            (float)r.X, (float)r.Y,
                            (float)r.Width, (float)r.Height);
                    }

                    var clipRegion = aggregator.GetBoundingBox().Intersect(windowBounds);
                    if (clipRegion.IsEmpty)
                    {
                        lock (_dirtyLock) { _dirtyElements.Clear(); _dirtyFreeRects.Clear(); _fullInvalidation = false; }
                        return;
                    }

                    if (!TryBeginDrawOrScheduleRetry()) { return; }
                    lock (_dirtyLock) { _dirtyElements.Clear(); _dirtyFreeRects.Clear(); _fullInvalidation = false; }
                    try
                    {
                        _debugHud.OnPartial();
                        _drawingContext.Offset = Point.Zero;
                        _drawingContext.PushDirtyRegionClip(clipRegion);

                        ClearBackground(clipRegion);
                        Render(_drawingContext);
                        _debugHud.MarkRender();

                        _drawingContext.PopDirtyRegionClip();
                        DevToolsOverlay?.DrawOverlay(_drawingContext);
                        _debugHud.UpdateOverlay(_debugHudOverlay);
                        OnRender(RenderTarget);
                        if (!CompleteEndDrawOrHandleFailure()) { return; }
                    }
                    catch (RenderPipelineException ex)
                    {
                        if (RenderTarget.IsDrawing)
                            try { _ = RenderTarget.TryEndDraw(); } catch { }
                        if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
                        {
                            return;
                        }

                        throw;
                    }
                    catch
                    {
                        if (RenderTarget.IsDrawing)
                            try { RenderTarget.EndDraw(); } catch { }
                        throw;
                    }
                }
            }

            _drawingContext?.TrimCacheIfNeeded();

        }
        catch (RenderPipelineException ex)
        {
            if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
            {
                return;
            }

            LogRenderFailure(ex, "RenderFrame");
            throw;
        }
        catch (Exception ex)
        {
            // Restore full invalidation so dirty elements aren't permanently lost.
            // Without this, the stale error frame stays on screen because the dirty
            // tracking was already cleared before rendering began.
            lock (_dirtyLock)
            {
                _fullInvalidation = true;
            }
            ScheduleRenderAfterRecovery();
            LogRenderFailure(ex, "RenderFrame");
            throw;
        }
        finally
        {
            ClearRenderFlag(RenderFlag_Rendering);
        }

        // If something requested a render during our rendering
        // (e.g., UpdateLayout triggered further invalidation),
        // schedule another render cycle.
        if (HasRenderFlag(RenderFlag_Requested))
        {
            ClearRenderFlag(RenderFlag_Requested);
            if (!CompositionTarget.IsActive)
            {
                InvalidateWindow();
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="DirtyRegionAggregator"/> containing every dirty region for
    /// this frame. Combines per-element pre-layout and post-layout bounds (so
    /// elements that moved during UpdateLayout repaint both old and new positions),
    /// precise sub-rects from <see cref="AddDirtyElement(UIElement, Rect)"/>,
    /// and free-floating rects from <see cref="AddDirtyRect"/>. Clamped to the
    /// window client area.
    /// Must be called under <see cref="_dirtyLock"/>.
    /// </summary>
    private DirtyRegionAggregator ComputeDirtyRegions()
    {
        var agg = new DirtyRegionAggregator(capacity: 32);
        var windowBounds = new Rect(0, 0, ActualWidth, ActualHeight);

        foreach (var (element, entry) in _dirtyElements)
        {
            if (entry.PreciseLocalRects is { Count: > 0 } preciseLocal)
            {
                // Translate local sub-rects into screen space using the CURRENT
                // screen offset (post-layout).  The pre-layout bounds are still
                // submitted below so vacated pixels get repainted even when a
                // precise list is also present.
                var postBounds = element.GetScreenBounds();
                foreach (var local in preciseLocal)
                {
                    var screenRect = new Rect(
                        postBounds.X + local.X,
                        postBounds.Y + local.Y,
                        local.Width,
                        local.Height);
                    var clipped = screenRect.Intersect(windowBounds);
                    if (!clipped.IsEmpty) agg.Add(clipped);
                }
            }
            else
            {
                // Post-layout bounds: where the element IS now.
                var postLayoutBounds = element.GetScreenBounds().Intersect(windowBounds);
                if (!postLayoutBounds.IsEmpty) agg.Add(postLayoutBounds);
            }

            // Pre-layout bounds: where the element WAS before UpdateLayout.
            // Always submitted (even for precise-rect callers) so that elements
            // which moved or resized leave no stale pixels behind.
            if (!entry.PreLayoutBounds.IsEmpty)
            {
                var clipped = entry.PreLayoutBounds.Intersect(windowBounds);
                if (!clipped.IsEmpty) agg.Add(clipped);
            }
        }

        foreach (var free in _dirtyFreeRects)
        {
            var clipped = free.Intersect(windowBounds);
            if (!clipped.IsEmpty) agg.Add(clipped);
        }

        return agg;
    }

    /// <summary>
    /// Legacy accessor returning the bounding box of every dirty region.
    /// Still used by code paths that only need the outer extent (e.g. HUD display).
    /// Must be called under <see cref="_dirtyLock"/>.
    /// </summary>
    private Rect ComputeDirtyRegion()
    {
        return ComputeDirtyRegions().GetBoundingBox();
    }

    /// <summary>
    /// Seeds every dirty-history slot with the full window rect, then resets
    /// the write index. Used after a full-render path so that the next N-1
    /// FLIP_SEQUENTIAL buffers get their stale content repainted.
    /// </summary>
    private void SeedDirtyHistoryFullWindow(Rect windowBounds)
    {
        var seed = new[] { windowBounds };
        for (int h = 0; h < DirtyHistoryCount; h++) _dirtyHistory[h] = seed;
        _dirtyHistoryIndex = 0;
    }

    /// <summary>
    /// Clears the render target with the window background color.
    /// When a D2D clip is active (retained mode), only the clipped area is cleared.
    /// </summary>
    private void ClearBackground()
    {
        ClearBackground(clipRegion: null);
    }

    /// <summary>
    /// Clears the window background.  When <paramref name="clipRegion"/> is non-null
    /// a clip-aware fill is used instead of <c>D2D1::Clear</c>, which ignores D2D clips
    /// and would destroy transparent punch-through areas (e.g. WebView composition holes)
    /// outside the dirty region.
    /// </summary>
    private void ClearBackground(Rect? clipRegion)
    {
        if (clipRegion == null)
        {
            // Full render — D2D Clear is safe because the entire surface is redrawn.
            if (Background is SolidColorBrush solidFull)
            {
                var c = solidFull.Color;
                RenderTarget!.Clear(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
            }
            else if (SystemBackdrop != WindowBackdropType.None)
            {
                RenderTarget!.Clear(0.0f, 0.0f, 0.0f, 0.0f);
            }
            else
            {
                RenderTarget!.Clear(0.0f, 0.0f, 0.0f, 1.0f);
            }
            return;
        }

        // Partial render — use clip-aware operations to avoid destroying content
        // outside the dirty region (D2D Clear ignores all clips).
        var r = clipRegion.Value;
        if (Background is SolidColorBrush solidPartial)
        {
            // For opaque backgrounds, FillRectangle is clip-aware and equivalent
            // to Clear within the clip region.  For semi-transparent backgrounds
            // PunchTransparentRect (D2D1_PRIMITIVE_BLEND_COPY) is needed to
            // overwrite rather than blend.
            if (solidPartial.Color.A == 255)
            {
                var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
                using var brush = context.CreateSolidBrush(
                    solidPartial.Color.R / 255f,
                    solidPartial.Color.G / 255f,
                    solidPartial.Color.B / 255f,
                    solidPartial.Color.A / 255f);
                RenderTarget!.FillRectangle(
                    (float)r.X, (float)r.Y,
                    (float)r.Width, (float)r.Height,
                    brush);
            }
            else
            {
                RenderTarget!.PunchTransparentRect(
                    (float)r.X, (float)r.Y,
                    (float)r.Width, (float)r.Height);
                if (solidPartial.Color.A > 0)
                {
                    var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
                    using var brush = context.CreateSolidBrush(
                        solidPartial.Color.R / 255f,
                        solidPartial.Color.G / 255f,
                        solidPartial.Color.B / 255f,
                        solidPartial.Color.A / 255f);
                    RenderTarget!.FillRectangle(
                        (float)r.X, (float)r.Y,
                        (float)r.Width, (float)r.Height,
                        brush);
                }
            }
        }
        else if (SystemBackdrop != WindowBackdropType.None)
        {
            RenderTarget!.PunchTransparentRect(
                (float)r.X, (float)r.Y,
                (float)r.Width, (float)r.Height);
        }
        else
        {
            var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
            using var brush = context.CreateSolidBrush(0.0f, 0.0f, 0.0f, 1.0f);
            RenderTarget!.FillRectangle(
                (float)r.X, (float)r.Y,
                (float)r.Width, (float)r.Height,
                brush);
        }
    }

    private sealed class PointerManipulationSession
    {
        public PointerManipulationSession(UIElement target, Point origin, int timestamp)
        {
            Target = target;
            Origin = origin;
            LastPoint = origin;
            LastTimestamp = timestamp;
            CumulativeTranslation = Vector.Zero;
            LastVelocity = Vector.Zero;
        }

        public UIElement Target { get; }
        public Point Origin { get; }
        public Point LastPoint { get; set; }
        public int LastTimestamp { get; set; }
        public Vector CumulativeTranslation { get; set; }
        public Vector LastVelocity { get; set; }
    }

    private void LogRenderFailure(Exception exception, string fallbackStage)
    {
        _ = exception;
        _ = fallbackStage;
    }

    /// <summary>
    /// Updates the layout of all elements in this window.
    /// Uses LayoutManager for queue-based processing: only dirty elements are re-measured/re-arranged.
    /// </summary>
    private void UpdateLayout()
    {
        Size availableSize = new(Width, Height);

        if (_isFirstLayout)
        {
            _isFirstLayout = false;
            Measure(availableSize);
            Arrange(new Rect(0, 0, availableSize.Width, availableSize.Height));
            return;
        }

        _layoutManager.UpdateLayout(this, availableSize);
    }

    /// <summary>
    /// Detects the refresh rate of the monitor displaying this window.
    /// </summary>
    /// <returns>The refresh rate in Hz (e.g., 60, 120, 144), or 60 as fallback.</returns>
    private int DetectMonitorRefreshRate()
    {
        if (Handle == nint.Zero) return 60;

        // Cross-platform path
        if (_platformWindow != null)
            return _platformWindow.GetMonitorRefreshRate();

        // Win32 path
        var hMonitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        MONITORINFOEX monitorInfoEx = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };

        if (GetMonitorInfoEx(hMonitor, ref monitorInfoEx))
        {
            DEVMODE devMode = new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(monitorInfoEx.szDevice, ENUM_CURRENT_SETTINGS, ref devMode))
            {
                return (int)devMode.dmDisplayFrequency;
            }
        }

        return 60;
    }

    /// <summary>
    /// Called by CompositionTarget.FrameStarting at the start of each frame,
    /// BEFORE animation handlers run. If any InvalidateWindow calls were blocked
    /// between frames (hover changes, mouse tracking, property updates), this
    /// ensures a render is scheduled for the current frame so those dirty elements
    /// get painted along with the animation updates.
    /// </summary>
    private void OnFrameStarting()
    {
        if (HasRenderFlag(RenderFlag_DirtyBetween))
        {
            ClearRenderFlag(RenderFlag_DirtyBetween);
            if (TrySetRenderFlag(RenderFlag_Scheduled))
            {
                _dispatcher?.BeginInvokeCritical(ProcessRender);
            }
        }
    }

    /// <summary>
    /// Schedules a render via Dispatcher.BeginInvokeCritical (WPF-style).
    /// Implements IWindowHost.InvalidateWindow.
    ///
    /// Unlike InvalidateRect 鈫?WM_PAINT (which is low-priority and gets starved
    /// by posted messages from DispatcherTimer), this enqueues a render directly
    /// in the Dispatcher queue. ProcessQueue drains all items, so the render
    /// runs right after animation ticks in the same batch.
    ///
    /// iGPU optimization: when CompositionTarget is active (animations running),
    /// only allow renders triggered during the Rendering event phase.
    /// Mouse/interaction-triggered renders between frames are suppressed 鈥?
    /// dirty elements are batched into the next CompositionTarget frame.
    /// This prevents render storms on slow GPUs (200ms render + immediate
    /// mouse render + immediate timer render = frozen UI).
    /// </summary>
    public void InvalidateWindow()
    {
        if (Handle == nint.Zero) return;

        // During rendering, don't schedule 鈥?just flag for re-render after current frame
        if (HasRenderFlag(RenderFlag_Rendering))
        {
            SetRenderFlag(RenderFlag_Requested);
            return;
        }

        // When the centralized frame timer is active, only allow renders triggered
        // during CompositionTarget.Rendering (animation handlers). Between frames,
        // mouse drags / property changes just mark elements dirty via AddDirtyElement 鈥?
        // they'll be rendered in the next animation frame via FrameStarting.
        // This ensures exactly ONE render per frame interval, leaving gaps for
        // the message pump to process input.
        if (CompositionTarget.IsActive && !CompositionTarget.IsInRenderingPhase)
        {
            SetRenderFlag(RenderFlag_DirtyBetween);
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            // Use stored UI thread Dispatcher 鈥?NOT Dispatcher.CurrentDispatcher,
            // which returns null on thread-pool threads (System.Threading.Timer callbacks,
            // Storyboard ticks, etc.) and would silently drop the render request.
            _dispatcher?.BeginInvokeCritical(ProcessRender);
        }
    }

    /// <summary>
    /// Adds a dirty element for partial rendering via native dirty rects.
    /// The element's full screen bounds are used.
    /// </summary>
    public void AddDirtyElement(UIElement element)
    {
        // Thread-safe: background threads (System.Threading.Timer callbacks from
        // ProgressBar, Storyboard, caret timers) call InvalidateVisual → AddDirtyElement.
        lock (_dirtyLock)
        {
            // Only capture pre-layout bounds on first registration per frame.
            // This preserves the true "old" position before UpdateLayout moves things.
            if (_dirtyElements.TryGetValue(element, out var entry))
            {
                // Already registered. A caller now wants full-element dirty, which
                // supersedes any precise sub-rect list we had.
                entry.PreciseLocalRects = null;
                return;
            }
            _dirtyElements[element] = new DirtyElementEntry
            {
                PreLayoutBounds = element.GetScreenBounds(),
                PreciseLocalRects = null,
            };
        }
    }

    /// <summary>
    /// Adds a dirty element with a precise sub-rectangle in the element's local
    /// coordinate space. Multiple calls accumulate (each rect is stored), so
    /// several independent local regions can be marked dirty without promoting
    /// to the full element bounds.
    /// </summary>
    public void AddDirtyElement(UIElement element, Rect localDirtyRect)
    {
        if (localDirtyRect.IsEmpty)
        {
            AddDirtyElement(element);
            return;
        }

        lock (_dirtyLock)
        {
            if (!_dirtyElements.TryGetValue(element, out var entry))
            {
                entry = new DirtyElementEntry
                {
                    PreLayoutBounds = element.GetScreenBounds(),
                    PreciseLocalRects = new List<Rect>(2),
                };
                _dirtyElements[element] = entry;
            }

            // If AddDirtyElement(element) was previously called in this frame we
            // are already tracking the full element — no point adding a sub-rect.
            if (entry.PreciseLocalRects == null) return;

            entry.PreciseLocalRects.Add(localDirtyRect);
        }
    }

    /// <summary>
    /// Adds a free-floating dirty rectangle in window (screen) coordinates.
    /// Used by animation / compositor systems that know what pixels changed
    /// but don't own a single <see cref="UIElement"/>.
    /// </summary>
    public void AddDirtyRect(Rect screenRect)
    {
        if (screenRect.IsEmpty) return;
        lock (_dirtyLock) { _dirtyFreeRects.Add(screenRect); }
    }

    /// <summary>
    /// Requests a full invalidation of the window (e.g., after layout changes).
    /// </summary>
    public void RequestFullInvalidation()
    {
        _fullInvalidation = true;
    }

    /// <summary>
    /// Calls Win32 SetCapture so the window receives mouse messages even when the cursor is outside.
    /// </summary>
    public void SetNativeCapture()
    {
        if (Handle == nint.Zero) return;

        if (_platformWindow != null)
        {
            // Cross-platform: mouse capture managed at the framework level.
            // Native capture is a Win32 concept; on Linux/Android, pointer
            // events continue delivery to the focused window automatically.
            return;
        }

        SetCapture(Handle);
    }

    /// <summary>
    /// Calls Win32 ReleaseCapture to stop capturing mouse messages outside the window.
    /// </summary>
    public void ReleaseNativeCapture()
    {
        if (_platformWindow != null)
            return; // Cross-platform: no native capture to release

        _ = ReleaseCapture();
    }

    /// <summary>
    /// Forces an immediate synchronous render of the window.
    /// Used for offline frame-by-frame rendering (e.g., video production).
    /// </summary>
    public void ForceRenderFrame()
    {
        RequestFullInvalidation();
        RenderFrame();
    }

    /// <summary>
    /// Called to render the window content.
    /// </summary>
    /// <param name="renderTarget">The render target to draw on.</param>
    protected virtual void OnRender(RenderTarget renderTarget)
    {
        // Base implementation renders nothing
        // Derived classes can override to add custom rendering
    }

    #endregion

    #region Input Handling

    private static bool IsShellReservedVirtualKey(nint wParam)
    {
        int virtualKey = (int)wParam;
        return virtualKey is VK_LWIN or VK_RWIN;
    }

    private bool OnKeyDown(nint wParam, nint lParam)
    {
        Key key = (Key)(int)wParam;
        var modifiers = GetModifierKeys();
        bool isRepeat = ((lParam.ToInt64() >> 30) & 1) != 0;
        return _inputDispatcher.HandleKeyDown(key, modifiers, isRepeat, Environment.TickCount);
    }

    private UIElement GetKeyboardEventTarget()
    {
        var focusedElement = Keyboard.FocusedElement as UIElement;
        var dialogRoot = ActiveContentDialog;

        // Keep keyboard routing inside the active modal dialog whenever focus escaped it.
        return dialogRoot != null && (focusedElement == null || !IsDescendantOf(focusedElement, dialogRoot))
            ? dialogRoot
            : focusedElement ?? this;
    }

    private ContentDialog? FindContainingInPlaceDialog()
    {
        var focused = Keyboard.FocusedElement as Visual;
        for (var current = focused; current != null; current = current.VisualParent)
        {
            if (current is ContentDialog dialog && ActiveInPlaceDialogs.Contains(dialog))
            {
                return dialog;
            }
        }

        return null;
    }

    private UIElement? GetTextInputTarget()
    {
        var focusedElement = Keyboard.FocusedElement as UIElement;
        var dialogRoot = ActiveContentDialog;

        if (dialogRoot != null)
        {
            return focusedElement != null && IsDescendantOf(focusedElement, dialogRoot)
                ? focusedElement
                : dialogRoot;
        }

        return focusedElement;
    }

    private static bool IsDescendantOf(UIElement descendant, UIElement ancestor)
    {
        int depthGuard = 0;
        for (Visual? current = descendant; current != null && depthGuard++ < 4096; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static Button? FindButton(UIElement root, Func<Button, bool> predicate)
    {
        if (root is Button btn && predicate(btn))
            return btn;

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                var found = FindButton(child, predicate);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Toggles the DevTools window for this window.
    /// Press F12 to open/close DevTools in DEBUG builds.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevToolsWindow includes a REPL and inspector that reflect on user types.")]
    public void ToggleDevTools()
    {
        if (_devToolsWindow != null)
        {
            // Close existing DevTools
            _devToolsWindow.CloseDevTools();
            _devToolsWindow = null;
            DevToolsOverlay = null;
        }
        else
        {
            // Open new DevTools
            _devToolsWindow = new DevToolsWindow(this);
            _devToolsWindow.Closed += (_, _) =>
            {
                _devToolsWindow = null;
                DevToolsOverlay = null;
            };
            _devToolsWindow.Show();
        }
    }

    /// <summary>
    /// Opens the DevTools window for this window.
    /// </summary>
    public void OpenDevTools()
    {
        if (_devToolsWindow == null)
        {
            ToggleDevTools();
        }
    }

    /// <summary>
    /// Closes the DevTools window for this window.
    /// </summary>
    public void CloseDevTools()
    {
        if (_devToolsWindow != null)
        {
            _devToolsWindow.CloseDevTools();
            _devToolsWindow = null;
            DevToolsOverlay = null;
        }
    }

    private bool OnKeyUp(nint wParam, nint lParam)
    {
        Key key = (Key)(int)wParam;
        var modifiers = GetModifierKeys();
        return _inputDispatcher.HandleKeyUp(key, modifiers, Environment.TickCount);
    }

    private void OnActivateChanged(int activateState, nint newForegroundWindow)
    {
        if (activateState == WA_INACTIVE)
        {
            if (_isActive)
            {
                _isActive = false;
                OnDeactivated(EventArgs.Empty);
            }
            // Match WPF semantics: a window losing activation must NOT drop the logical
            // keyboard focus. The focused element stays focused so that re-activation
            // restores input naturally, and so that transient activation flickers
            // (briefly-shown tooltips, IME windows, system popups stealing focus for a
            // single message pump cycle) don't visibly tear down the focus visual or
            // strand the user mid-Tab. Clearing focus here was the root cause of focus
            // rings disappearing one frame after each Tab landed on a NavigationViewItem.
            _inputDispatcher.HandleWindowDeactivated(newForegroundWindow, clearKeyboardFocus: false);
            return;
        }

        if (!_isActive)
        {
            _isActive = true;
            OnActivated(EventArgs.Empty);
        }
        _inputDispatcher.ArmEscapeSuppressionIfNeeded();
        WakeRenderPipeline();
    }

    private void OnCancelMode()
    {
        _inputDispatcher.HandleCancelMode();
    }

    private void OnKillFocus(nint newFocusWindow)
    {
        // Same rationale as OnActivateChanged: WM_KILLFOCUS arrives on activation
        // transitions and on transient focus thefts (popup windows, IME). Preserve
        // the logical keyboard focus so the next WM_SETFOCUS resumes seamlessly.
        _inputDispatcher.HandleWindowDeactivated(newFocusWindow, clearKeyboardFocus: false);
    }

    private void OnSetFocus()
    {
        _inputDispatcher.HandleSetFocus();
    }

    private void HandleWindowDeactivated(nint newForegroundWindow, bool clearKeyboardFocus)
    {
        CloseLightDismissPopupsOnDeactivate(newForegroundWindow);
        ResetTransientInputStateOnDeactivate();

        if (clearKeyboardFocus)
        {
            Keyboard.ClearFocus();
        }

        UpdateInputMethodAssociation();
        WakeRenderPipeline();
    }

    private void CloseLightDismissPopupsOnDeactivate(nint newForegroundWindow)
    {
        if (PopupWindow.IsPopupWindow(newForegroundWindow))
        {
            return;
        }

        _ = OverlayLayer.CloseLightDismissPopups();

        if (ActiveExternalPopups.Count == 0)
        {
            return;
        }

        var popupsToClose = ActiveExternalPopups
            .Where(p => !p.StaysOpen)
            .ToList();
        foreach (var popup in popupsToClose)
        {
            popup.IsOpen = false;
        }
    }

    private void ResetTransientInputStateOnDeactivate()
    {
        UIElement.ForceReleaseMouseCapture();
        ClearPressedChains();
        _lastHitTestElement = null;

        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            ClearTitleBarInteractionState();
        }
    }

    private void ArmEscapeSuppressionIfNeeded()
    {
        _suppressEscapeUntilTick = IsVirtualKeyDown(VK_ESCAPE)
            ? Environment.TickCount64 + EscapeReactivateSuppressionMs
            : 0;
    }

    private void WakeRenderPipeline()
    {
        RequestFullInvalidation();

        if (Handle == nint.Zero || _dispatcher == null || _isClosing)
        {
            return;
        }

        if (HasRenderFlag(RenderFlag_Rendering))
        {
            SetRenderFlag(RenderFlag_Requested);
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    private bool ShouldSuppressReactivatedEscape(Key key, bool isKeyDown)
    {
        if (key != Key.Escape)
        {
            return false;
        }

        long suppressUntilTick = _suppressEscapeUntilTick;
        if (suppressUntilTick == 0)
        {
            return false;
        }

        if (Environment.TickCount64 > suppressUntilTick)
        {
            _suppressEscapeUntilTick = 0;
            return false;
        }

        if (!isKeyDown)
        {
            _suppressEscapeUntilTick = 0;
        }

        return true;
    }

    private void OnChar(nint wParam, nint lParam)
    {
        char c = (char)(int)wParam;
        if (char.IsControl(c) && c != '\r' && c != '\t')
            return;

        _inputDispatcher.HandleCharInput(c.ToString(), Environment.TickCount);
    }

    #region IME Handling

    private void OnImeStartComposition()
    {
        if (!TryGetImeTarget(out _, out _))
        {
            return;
        }

        InputMethod.StartComposition();

        // Position the IME composition window near the caret
        UpdateImeCompositionWindow();
    }

    private void OnImeEndComposition()
    {
        InputMethod.EndComposition();
        UpdateInputMethodAssociation();
    }

    private bool OnImeComposition(nint lParam)
    {
        var hImc = ImmNativeMethods.ImmGetContext(Handle);
        if (hImc == nint.Zero)
        {
            return false;
        }

        try
        {
            int flags = (int)lParam;

            // Check for result string (final committed text)
            if ((flags & ImmNativeMethods.GCS_RESULTSTR) != 0)
            {
                string resultStr = GetCompositionString(hImc, ImmNativeMethods.GCS_RESULTSTR);
                if (!string.IsNullOrEmpty(resultStr))
                {
                    var target = GetTextInputTarget();
                    if (target != null)
                    {
                        TextCompositionEventArgs args = new(TextInputEvent, resultStr, Environment.TickCount);
                        target.RaiseEvent(args);
                    }
                    InputMethod.EndComposition(resultStr);
                }
            }

            // Check for composition string (in-progress text)
            if ((flags & ImmNativeMethods.GCS_COMPSTR) != 0)
            {
                string compStr = GetCompositionString(hImc, ImmNativeMethods.GCS_COMPSTR);
                int cursor = 0;

                if ((flags & ImmNativeMethods.GCS_CURSORPOS) != 0)
                {
                    cursor = ImmNativeMethods.ImmGetCompositionString(hImc, ImmNativeMethods.GCS_CURSORPOS, null, 0);
                }

                InputMethod.UpdateComposition(compStr, cursor);
            }

            return true;
        }
        finally
        {
            _ = ImmNativeMethods.ImmReleaseContext(Handle, hImc);
        }
    }

    private static string GetCompositionString(nint hImc, int dwIndex)
    {
        int len = ImmNativeMethods.ImmGetCompositionString(hImc, dwIndex, null, 0);
        if (len <= 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[len];
        _ = ImmNativeMethods.ImmGetCompositionString(hImc, dwIndex, buffer, len);

        // IME returns UTF-16LE encoded string
        return System.Text.Encoding.Unicode.GetString(buffer);
    }

    private void OnWindowKeyboardFocusChanged(object? sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateInputMethodAssociation();

        // Notify UIA of focus change — deferred to avoid RPC_E_CANTCALLOUT
        if (OperatingSystem.IsWindows() && e.NewFocus is UIElement focused)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var peer = focused.GetAutomationPeer();
                if (peer != null)
                    Automation.Uia.UiaAccessibilityBridge.RaiseFocusChanged(peer);
            });
        }
    }

    private bool CanHandleImeMessages()
        => TryGetImeTarget(out _, out _);

    private bool TryGetImeTarget(out UIElement? target, out IImeSupport? imeSupport)
    {
        target = Keyboard.FocusedElement as UIElement;
        if (target is not IImeSupport support)
        {
            imeSupport = null;
            return false;
        }

        if (!InputMethod.GetIsInputMethodEnabled(target))
        {
            imeSupport = null;
            return false;
        }

        imeSupport = support;
        return true;
    }

    private void UpdateInputMethodAssociation()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        bool shouldEnableIme = CanHandleImeMessages();
        if (!shouldEnableIme && InputMethod.IsComposing)
        {
            InputMethod.CancelComposition();
        }

        if (shouldEnableIme)
        {
            if (!_imeContextDetached)
            {
                return;
            }

            if (_detachedImeContext != nint.Zero)
            {
                _ = ImmNativeMethods.ImmAssociateContext(Handle, _detachedImeContext);
            }
            else
            {
                _ = ImmNativeMethods.ImmAssociateContextEx(Handle, nint.Zero, IACE_DEFAULT);
            }

            _detachedImeContext = nint.Zero;
            _imeContextDetached = false;
            return;
        }

        if (_imeContextDetached)
        {
            return;
        }

        _detachedImeContext = ImmNativeMethods.ImmAssociateContext(Handle, nint.Zero);
        _imeContextDetached = true;
    }

    /// <summary>
    /// Updates the IME composition window position to be near the caret.
    /// </summary>
    public void UpdateImeCompositionWindow()
    {
        var target = Keyboard.FocusedElement as UIElement;
        if (target == null || target is not IImeSupport imeSupport)
        {
            return;
        }

        var hImc = ImmNativeMethods.ImmGetContext(Handle);
        if (hImc == nint.Zero)
        {
            return;
        }

        try
        {
            // Convert focused element local caret position (DIPs) to client-area physical pixels.
            Point caretPosDip = imeSupport.GetImeCaretPosition();
            if (target is FrameworkElement frameworkElement)
            {
                var targetOriginDip = frameworkElement.TransformToAncestor(null);
                caretPosDip = new Point(targetOriginDip.X + caretPosDip.X, targetOriginDip.Y + caretPosDip.Y);
            }

            int caretX = (int)Math.Round(caretPosDip.X * _dpiScale);
            int caretY = (int)Math.Round(caretPosDip.Y * _dpiScale);

            ImmNativeMethods.COMPOSITIONFORM form = new()
            {
                dwStyle = ImmNativeMethods.CFS_POINT,
                ptCurrentPos = new ImmNativeMethods.POINT { x = caretX, y = caretY }
            };

            _ = ImmNativeMethods.ImmSetCompositionWindow(hImc, ref form);

            // Also set candidate window position
            ImmNativeMethods.CANDIDATEFORM candidate = new()
            {
                dwIndex = 0,
                dwStyle = ImmNativeMethods.CFS_CANDIDATEPOS,
                ptCurrentPos = new ImmNativeMethods.POINT { x = caretX, y = caretY }
            };

            _ = ImmNativeMethods.ImmSetCandidateWindow(hImc, ref candidate);
        }
        finally
        {
            _ = ImmNativeMethods.ImmReleaseContext(Handle, hImc);
        }
    }

    #endregion

    private void OnMouseMove(nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseMove(position, buttons, modifiers, Environment.TickCount);
    }

    private void RaiseMouseLeaveChain(UIElement oldElement, UIElement? newElement, int timestamp)
    {
        // Build the ancestor chain of the new element for comparison
        HashSet<UIElement> newAncestors = [];
        Visual? current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                _ = newAncestors.Add(uiElement);
            }

            current = current.VisualParent;
        }

        // Raise MouseLeave for elements that are no longer under the mouse
        current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (newAncestors.Contains(uiElement))
                {
                    break; // Stop at common ancestor
                }

                uiElement.SetIsMouseOver(false);
                MouseEventArgs args = new(MouseLeaveEvent) { Source = uiElement };
                uiElement.RaiseEvent(args);
            }
            current = current.VisualParent;
        }
    }

    private void RaiseMouseEnterChain(UIElement newElement, UIElement? oldElement, int timestamp)
    {
        // Build the ancestor chain of the old element for comparison
        HashSet<UIElement> oldAncestors = [];
        Visual? current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                _ = oldAncestors.Add(uiElement);
            }

            current = current.VisualParent;
        }

        // Collect elements that need MouseEnter (in reverse order, from ancestor to descendant)
        List<UIElement> enterElements = [];
        current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (oldAncestors.Contains(uiElement))
                {
                    break; // Stop at common ancestor
                }

                enterElements.Add(uiElement);
            }
            current = current.VisualParent;
        }

        // Raise MouseEnter from ancestor to descendant
        for (int i = enterElements.Count - 1; i >= 0; i--)
        {
            var uiElement = enterElements[i];
            uiElement.SetIsMouseOver(true);
            MouseEventArgs args = new(MouseEnterEvent) { Source = uiElement };
            uiElement.RaiseEvent(args);
        }
    }

    private static void BuildAncestorChain(UIElement start, List<UIElement> chain)
    {
        chain.Clear();

        UIElement? current = start;
        while (current != null)
        {
            chain.Add(current);
            current = current.VisualParent as UIElement;
        }
    }

    private static void ApplyPressedState(List<UIElement> chain, bool isPressed)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            chain[i].SetIsPressed(isPressed);
        }
    }

    private void ActivateMousePressedChain(UIElement target)
    {
        ClearMousePressedChain();
        BuildAncestorChain(target, _mousePressedChain);
        ApplyPressedState(_mousePressedChain, isPressed: true);
    }

    private void ClearMousePressedChain()
    {
        if (_mousePressedChain.Count == 0)
        {
            return;
        }

        ApplyPressedState(_mousePressedChain, isPressed: false);
        _mousePressedChain.Clear();
    }

    private void ActivateKeyboardPressedChain(UIElement target)
    {
        ClearKeyboardPressedChain();
        BuildAncestorChain(target, _keyboardPressedChain);
        ApplyPressedState(_keyboardPressedChain, isPressed: true);
        _keyboardPressActive = true;
    }

    private void ClearKeyboardPressedChain()
    {
        if (_keyboardPressedChain.Count == 0 && !_keyboardPressActive)
        {
            return;
        }

        ApplyPressedState(_keyboardPressedChain, isPressed: false);
        _keyboardPressedChain.Clear();
        _keyboardPressActive = false;
    }

    private void ClearPressedChains()
    {
        ClearMousePressedChain();
        ClearKeyboardPressedChain();
    }

    private void OnMouseButtonDown(MouseButton button, nint wParam, nint lParam, int clickCount = 1)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseDown(button, position, buttons, modifiers, clickCount, Environment.TickCount);
    }

    private void OnMouseButtonUp(MouseButton button, nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseUp(button, position, buttons, modifiers, Environment.TickCount);
    }

    private void UpdateMouseOverState(UIElement? newMouseOverElement, int timestamp)
    {
        if (newMouseOverElement == _lastMouseOverElement)
        {
            return;
        }

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement, timestamp);
        }

        if (newMouseOverElement != null)
        {
            RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement, timestamp);
        }

        _lastMouseOverElement = newMouseOverElement;
    }

    private MenuItem? HitTopLevelMenuItemBehindOverlay(Point windowPosition)
    {
        var hitElement = HitIgnoringOverlay(windowPosition)?.VisualHit as UIElement;
        return FindTopLevelMenuItemAncestor(hitElement);
    }

    private HitTestResult? HitIgnoringOverlay(Point windowPosition)
    {
        // Window.GetVisualChild order is topmost-last, so iterate reverse to preserve hit-test priority.
        for (int i = VisualChildrenCount - 1; i >= 0; i--)
        {
            if (GetVisualChild(i) is not FrameworkElement fe || fe == OverlayLayer)
            {
                continue;
            }

            var hit = fe.HitTest(windowPosition);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    private UIElement? HitTestElement(Point windowPosition, string source = "hit-test")
    {
        var hitElement = HitTestWithCache(windowPosition)?.VisualHit as UIElement;
        _lastHitTestElement = hitElement;
        return hitElement;
    }

    private HitTestResult? HitTestWithCache(Point windowPosition)
    {
        if (ActiveContentDialog != null || ActiveInPlaceDialogs.Count > 0 || OverlayLayer.HasModalRoots || OverlayLayer.HasLightDismissPopups || OverlayLayer.HasPopupRoots)
        {
            return HitTest(windowPosition);
        }

        var cachedHit = TryHitTestCachedSubtree(windowPosition);
        if (cachedHit != null)
        {
            return cachedHit;
        }

        return HitTest(windowPosition);
    }

    private HitTestResult? TryHitTestCachedSubtree(Point windowPosition)
    {
        var current = _lastHitTestElement;
        if (current == null)
            return null;

        if (!IsElementAttachedToThisWindow(current))
        {
            _lastHitTestElement = null;
            return null;
        }

        // The fast path is only safe when the point is inside *every* ancestor's
        // bounds and layout clip, AND no higher-z sibling of the cached element or
        // any ancestor could intercept the click. Without either guard, clicks leak
        // through to scrolled-off or visually clipped content that the user cannot
        // see — e.g. clicking a title bar above a ScrollViewer should hit the title
        // bar button, not whichever ScrollViewer child was hit most recently, and
        // clicking a ScrollBar overlaying the content should hit the ScrollBar, not
        // the content behind it.
        if (current is not FrameworkElement fe
            || !fe.IsHitTestVisible
            || fe.Visibility != Visibility.Visible)
        {
            return null;
        }

        if (!IsCachedSubtreeSafeForPoint(fe, windowPosition))
        {
            return null;
        }

        var parent = fe.VisualParent as UIElement;
        var pointInParent = parent == null
            ? windowPosition
            : new Point(
                windowPosition.X - parent.GetScreenBounds().X,
                windowPosition.Y - parent.GetScreenBounds().Y);

        var subtreeHit = fe.HitTest(pointInParent);
        if (subtreeHit != null)
        {
            return subtreeHit;
        }

        return null;
    }

    // Walks from the cached element up to the window, confirming at each level that
    // the point is inside the element and its layout clip. At the cached element's
    // immediate parent we also verify no higher-z sibling shadows the cached branch
    // (the scrollbar-over-content case). Checking only the direct parent avoids
    // false positives from transparent overlays that always span the window (e.g.
    // OverlayLayer), while still catching the most common occlusion pattern where
    // a sibling widget renders on top of the cached subtree.
    private static bool IsCachedSubtreeSafeForPoint(UIElement cached, Point windowPosition)
    {
        var parent = cached.VisualParent;
        if (parent != null && TryFindHigherZSiblingThatContains(parent, cached, windowPosition))
        {
            return false;
        }

        Visual? node = cached;
        while (node != null && node is not IWindowHost)
        {
            if (node is not UIElement ui)
            {
                node = node.VisualParent;
                continue;
            }

            var screenBounds = ui.GetScreenBounds();
            if (!screenBounds.Contains(windowPosition))
            {
                return false;
            }

            var localPoint = new Point(
                windowPosition.X - screenBounds.X,
                windowPosition.Y - screenBounds.Y);
            if (!ui.IsPointInsideLayoutClip(localPoint))
            {
                return false;
            }

            node = ui.VisualParent;
        }

        return true;
    }

    private static bool TryFindHigherZSiblingThatContains(Visual parent, Visual child, Point windowPosition)
    {
        int count = parent.VisualChildrenCount;

        // Find the child's index.
        int childIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ReferenceEquals(parent.GetVisualChild(i), child))
            {
                childIndex = i;
                break;
            }
        }

        if (childIndex < 0)
        {
            return false;
        }

        // Siblings at higher indices render on top of the cached branch; if one of
        // them contains the point, it would win in a full hit test, so the cache
        // cannot be trusted to produce the same answer.
        for (int i = childIndex + 1; i < count; i++)
        {
            if (parent.GetVisualChild(i) is UIElement sibling
                && sibling.IsHitTestVisible
                && sibling.Visibility == Visibility.Visible
                && sibling.GetScreenBounds().Contains(windowPosition))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsElementAttachedToThisWindow(UIElement element)
    {
        Visual? current = element;
        while (current != null)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }

            current = current.VisualParent;
        }

        return false;
    }

    private static MenuItem? FindTopLevelMenuItemAncestor(UIElement? element)
    {
        var current = element;
        while (current != null)
        {
            if (current is MenuItem menuItem
                && menuItem.VisualParent is Panel panel
                && panel.VisualParent is Menu)
            {
                return menuItem;
            }

            current = current.VisualParent as UIElement;
        }

        return null;
    }

    // Light dismiss is now handled by OverlayLayer.TryHandleLightDismiss()

    private void OnMouseWheel(nint wParam, nint lParam)
    {
        // WM_MOUSEWHEEL lParam contains SCREEN coordinates (physical pixels).
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = screenX, Y = screenY };
        _ = ScreenToClient(Handle, ref pt);
        Point position = new(pt.X / _dpiScale, pt.Y / _dpiScale);

        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseWheel(position, delta, buttons, modifiers, Environment.TickCount);
    }

    private void OnPointerMessage(uint msg, nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(Handle, wParam, _dpiScale, out var pointerData))
            return;
        if (pointerData.Kind == PointerInputKind.Mouse)
            return;

        bool isDown = msg == Win32PointerInterop.WM_POINTERDOWN;
        bool isUp = msg == Win32PointerInterop.WM_POINTERUP;
        _inputDispatcher.HandlePointerInput(pointerData, isDown, isUp, Environment.TickCount);
    }

    private void OnPointerWheel(nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(Handle, wParam, _dpiScale, out var pointerData))
            return;
        if (pointerData.Kind == PointerInputKind.Mouse)
            return;
        _inputDispatcher.HandlePointerWheel(pointerData, Environment.TickCount);
    }

    private void OnPointerCaptureChanged(nint wParam)
    {
        uint pointerId = Win32PointerInterop.GetPointerId(wParam);
        _inputDispatcher.HandlePointerCaptureChanged(pointerId, Environment.TickCount);
    }

    private void DispatchTouchSourcePipeline(
        UIElement target,
        PointerInputData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        TouchDevice touchDevice = isDown
            ? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target)
            : Touch.GetDevice((int)pointerData.PointerId) ?? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target);

        touchDevice.UpdatePosition(pointerData.Position);
        touchDevice.DirectlyOver = target;

        // --- Touch → Stylus promotion ---
        // Feed touch input through the RealTimeStylus / StylusPlugIn pipeline so that
        // controls like InkCanvas (which rely on StylusPlugIns) work with touch.
        PromoteTouchToStylus(target, pointerData, isDown, isUp, timestamp);

        RoutedEvent previewEvent = isDown ? PreviewTouchDownEvent : (isUp ? PreviewTouchUpEvent : PreviewTouchMoveEvent);
        RoutedEvent bubbleEvent = isDown ? TouchDownEvent : (isUp ? TouchUpEvent : TouchMoveEvent);

        TouchEventArgs previewArgs = new(touchDevice, timestamp) { RoutedEvent = previewEvent };
        target.RaiseEvent(previewArgs);

        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel;

        if (!previewArgs.Handled)
        {
            TouchEventArgs bubbleArgs = new(touchDevice, timestamp) { RoutedEvent = bubbleEvent };
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        if (isUp || sourceCanceled)
        {
            touchDevice.Deactivate();
            Touch.UnregisterTouchPoint((int)pointerData.PointerId);

            // Clean up the promoted stylus device.
            _activeStylusDevices.Remove(pointerData.PointerId);
        }
    }

    /// <summary>
    /// Promotes a touch pointer into the Stylus pipeline so that StylusPlugIns
    /// (DynamicRenderer, InkCollectionStylusPlugIn, etc.) receive the input.
    /// </summary>
    private void PromoteTouchToStylus(
        UIElement target,
        PointerInputData pointerData,
        bool isDown,
        bool isUp,
        int timestamp)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new PointerStylusDevice((int)pointerData.PointerId, $"Touch{pointerData.PointerId}");
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        var properties = pointerData.Point.Properties;
        float pressure = properties.Pressure;

        stylusDevice.UpdateState(
            pointerData.Position,
            pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: false,
            inRange: pointerData.IsInRange,
            barrelPressed: false,
            eraserPressed: false,
            directlyOver: target);

        StylusInputAction inputAction = ResolveStylusInputAction(isDown, isUp, pointerData.Point.IsInContact);
        RealTimeStylusProcessResult processResult = _realTimeStylus.Process(
            pointerData.PointerId,
            target,
            inputAction,
            stylusDevice.GetStylusPoints(target),
            timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: pointerData.IsCanceled);

        // Update stylus device with any modifications made by plugins.
        stylusDevice.UpdateState(
            pointerData.Position,
            processResult.RawStylusInput.GetStylusPoints(),
            inAir: !pointerData.Point.IsInContact,
            inverted: false,
            inRange: pointerData.IsInRange,
            barrelPressed: false,
            eraserPressed: false,
            directlyOver: target);

        // Raise Stylus RoutedEvents (Preview + Bubble) so handlers see touch as stylus.
        RoutedEvent previewEvent = isDown ? PreviewStylusDownEvent : (isUp ? PreviewStylusUpEvent : PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? StylusDownEvent : (isUp ? StylusUpEvent : StylusMoveEvent);

        StylusEventArgs previewArgs = CreateStylusEventArgs(stylusDevice, timestamp, previewEvent, isDown);
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled && !processResult.Canceled)
        {
            StylusEventArgs bubbleArgs = CreateStylusEventArgs(stylusDevice, timestamp, bubbleEvent, isDown);
            target.RaiseEvent(bubbleArgs);
        }

        _realTimeStylus.QueueProcessedCallbacks(processResult);
    }

    private void DispatchStylusSourcePipeline(
        UIElement target,
        PointerInputData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new PointerStylusDevice((int)pointerData.PointerId);
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        Tablet.CurrentStylusDevice = stylusDevice;

        var properties = pointerData.Point.Properties;
        stylusDevice.UpdateState(
            pointerData.Position,
            pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: pointerData.IsInRange,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        StylusInputAction inputAction = ResolveStylusInputAction(isDown, isUp, pointerData.Point.IsInContact);
        RealTimeStylusProcessResult processResult = _realTimeStylus.Process(
            pointerData.PointerId,
            target,
            inputAction,
            stylusDevice.GetStylusPoints(target),
            timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            inverted: properties.IsInverted,
            pointerCanceled: pointerData.IsCanceled);

        stylusDevice.UpdateState(
            pointerData.Position,
            processResult.RawStylusInput.GetStylusPoints(),
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: pointerData.IsInRange,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        RaiseStylusExtendedEvents(target, stylusDevice, timestamp, inputAction, processResult);

        RoutedEvent previewEvent = isDown ? PreviewStylusDownEvent : (isUp ? PreviewStylusUpEvent : PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? StylusDownEvent : (isUp ? StylusUpEvent : StylusMoveEvent);

        StylusEventArgs previewArgs = CreateStylusEventArgs(stylusDevice, timestamp, previewEvent, isDown);
        target.RaiseEvent(previewArgs);

        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel || processResult.Canceled;

        if (!previewArgs.Handled && !processResult.Canceled)
        {
            StylusEventArgs bubbleArgs = CreateStylusEventArgs(stylusDevice, timestamp, bubbleEvent, isDown);
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        _realTimeStylus.QueueProcessedCallbacks(processResult);

        if (isUp || sourceCanceled || processResult.SessionEnded)
        {
            _activeStylusDevices.Remove(pointerData.PointerId);
            if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
            {
                Tablet.CurrentStylusDevice = null;
            }
        }
    }

    private static StylusInputAction ResolveStylusInputAction(bool isDown, bool isUp, bool isInContact)
    {
        if (isDown)
        {
            return StylusInputAction.Down;
        }

        if (isUp)
        {
            return StylusInputAction.Up;
        }

        return isInContact ? StylusInputAction.Move : StylusInputAction.InAirMove;
    }

    private static StylusEventArgs CreateStylusEventArgs(StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent, bool isDown)
    {
        StylusEventArgs args = isDown
            ? new StylusDownEventArgs(stylusDevice, timestamp)
            : new StylusEventArgs(stylusDevice, timestamp);
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static StylusButton? GetBarrelButton(StylusDevice stylusDevice)
    {
        foreach (var button in stylusDevice.StylusButtons)
        {
            if (button.Name.Equals("Barrel", StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return stylusDevice.StylusButtons.Count > 0 ? stylusDevice.StylusButtons[0] : null;
    }

    private static void RaiseStylusSimpleEvent(UIElement target, StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent)
    {
        var args = new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusSystemGestureEvent(UIElement target, StylusDevice stylusDevice, int timestamp, SystemGesture gesture)
    {
        var args = new StylusSystemGestureEventArgs(stylusDevice, timestamp, gesture)
        {
            RoutedEvent = StylusSystemGestureEvent
        };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusButtonEvent(UIElement target, StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent)
    {
        StylusButton? button = GetBarrelButton(stylusDevice);
        if (button == null)
        {
            return;
        }

        var args = new StylusButtonEventArgs(stylusDevice, timestamp, button)
        {
            RoutedEvent = routedEvent
        };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusExtendedEvents(
        UIElement target,
        StylusDevice stylusDevice,
        int timestamp,
        StylusInputAction inputAction,
        RealTimeStylusProcessResult processResult)
    {
        if (processResult.LeftElement && processResult.PreviousTarget != null)
        {
            RaiseStylusSimpleEvent(processResult.PreviousTarget, stylusDevice, timestamp, StylusLeaveEvent);
        }

        if (processResult.EnteredElement)
        {
            RaiseStylusSimpleEvent(target, stylusDevice, timestamp, StylusEnterEvent);
        }

        if (processResult.EnteredRange)
        {
            RaiseStylusSimpleEvent(target, stylusDevice, timestamp, StylusInRangeEvent);
            RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoverEnter);
        }

        if (processResult.ExitedRange)
        {
            RaiseStylusSimpleEvent(processResult.PreviousTarget ?? target, stylusDevice, timestamp, StylusOutOfRangeEvent);
            RaiseStylusSystemGestureEvent(processResult.PreviousTarget ?? target, stylusDevice, timestamp, SystemGesture.HoverLeave);
        }

        if (processResult.BarrelButtonDown)
        {
            RaiseStylusButtonEvent(target, stylusDevice, timestamp, StylusButtonDownEvent);
        }

        if (processResult.BarrelButtonUp)
        {
            RaiseStylusButtonEvent(target, stylusDevice, timestamp, StylusButtonUpEvent);
        }

        switch (inputAction)
        {
            case StylusInputAction.Down:
                RaiseStylusSystemGestureEvent(
                    target,
                    stylusDevice,
                    timestamp,
                    stylusDevice.StylusButtons.Count > 0 && stylusDevice.StylusButtons[0].StylusButtonState == StylusButtonState.Down
                        ? SystemGesture.RightTap
                        : SystemGesture.Tap);
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldEnter);
                break;

            case StylusInputAction.Move:
                RaiseStylusSystemGestureEvent(
                    target,
                    stylusDevice,
                    timestamp,
                    stylusDevice.StylusButtons.Count > 0 && stylusDevice.StylusButtons[0].StylusButtonState == StylusButtonState.Down
                        ? SystemGesture.RightDrag
                        : SystemGesture.Drag);
                break;

            case StylusInputAction.InAirMove:
                RaiseStylusSimpleEvent(target, stylusDevice, timestamp, StylusInAirMoveEvent);
                break;

            case StylusInputAction.Up:
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldLeave);
                break;
        }
    }

    private void DispatchManipulationPipeline(
        UIElement target,
        PointerInputData pointerData,
        bool isDown,
        bool isUp,
        bool sourceHandled,
        int timestamp)
    {
        PointerManipulationSession? existingSession = null;
        if (!isDown && !_activeManipulationSessions.TryGetValue(pointerData.PointerId, out existingSession))
            return;

        if (isDown)
        {
            if (sourceHandled || !target.IsManipulationEnabled)
                return;

            if (!RaiseManipulationStartingPipeline(target))
                return;

            RaiseManipulationStartedPipeline(target, pointerData.Point.Position, timestamp);
            _activeManipulationSessions[pointerData.PointerId] = new PointerManipulationSession(target, pointerData.Point.Position, timestamp);
            return;
        }

        if (existingSession == null)
            return;

        if (isUp)
        {
            RaiseManipulationInertiaStartingPipeline(existingSession, timestamp);
            RaiseManipulationCompletedPipeline(existingSession, isInertial: false, timestamp);
            _activeManipulationSessions.Remove(pointerData.PointerId);
            return;
        }

        if (sourceHandled)
            return;

        RaiseManipulationDeltaPipeline(existingSession, pointerData.Point.Position, timestamp);
    }

    private bool RaiseManipulationStartingPipeline(UIElement target)
    {
        ManipulationStartingEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationStartingEvent,
            ManipulationContainer = target,
            Mode = ManipulationModes.All,
            Cancel = false
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
            return false;

        if (!previewArgs.Handled)
        {
            ManipulationStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartingEvent,
                ManipulationContainer = previewArgs.ManipulationContainer ?? target,
                Mode = previewArgs.Mode,
                Pivot = previewArgs.Pivot,
                IsSingleTouchEnabled = previewArgs.IsSingleTouchEnabled,
                Cancel = false
            };
            target.RaiseEvent(bubbleArgs);
            if (bubbleArgs.Cancel)
                return false;
        }

        return true;
    }

    private static void RaiseManipulationStartedPipeline(UIElement target, Point origin, int timestamp)
    {
        ManipulationStartedEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationStartedEvent,
            ManipulationContainer = target,
            ManipulationOrigin = origin
        };
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationStartedEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartedEvent,
                ManipulationContainer = target,
                ManipulationOrigin = origin
            };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaiseManipulationDeltaPipeline(PointerManipulationSession session, Point currentPoint, int timestamp)
    {
        Vector deltaTranslation = currentPoint - session.LastPoint;
        int dt = Math.Max(1, timestamp - session.LastTimestamp);
        Vector velocity = new(deltaTranslation.X / dt, deltaTranslation.Y / dt);
        Vector cumulative = session.CumulativeTranslation + deltaTranslation;

        ManipulationDelta delta = CreateManipulationDelta(deltaTranslation);
        ManipulationDelta cumulativeDelta = CreateManipulationDelta(cumulative);
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = velocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationDeltaEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationDeltaEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            DeltaManipulation = delta,
            CumulativeManipulation = cumulativeDelta,
            Velocities = velocities,
            IsInertial = false
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationDeltaEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationDeltaEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                DeltaManipulation = delta,
                CumulativeManipulation = cumulativeDelta,
                Velocities = velocities,
                IsInertial = false
            };
            session.Target.RaiseEvent(bubbleArgs);
        }

        session.LastPoint = currentPoint;
        session.LastTimestamp = timestamp;
        session.CumulativeTranslation = cumulative;
        session.LastVelocity = velocity;
    }

    private static void RaiseManipulationInertiaStartingPipeline(PointerManipulationSession session, int timestamp)
    {
        if (session.LastVelocity.Length <= 0.01)
            return;

        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationInertiaStartingEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationInertiaStartingEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            InitialVelocities = velocities
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationInertiaStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationInertiaStartingEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                InitialVelocities = velocities,
                TranslationBehavior = previewArgs.TranslationBehavior,
                RotationBehavior = previewArgs.RotationBehavior,
                ExpansionBehavior = previewArgs.ExpansionBehavior
            };
            session.Target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaiseManipulationCompletedPipeline(PointerManipulationSession session, bool isInertial, int timestamp)
    {
        ManipulationDelta total = CreateManipulationDelta(session.CumulativeTranslation);
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationCompletedEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationCompletedEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            TotalManipulation = total,
            FinalVelocities = velocities,
            IsInertial = isInertial
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationCompletedEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationCompletedEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                TotalManipulation = total,
                FinalVelocities = velocities,
                IsInertial = isInertial
            };
            session.Target.RaiseEvent(bubbleArgs);
        }
    }

    private void CancelManipulationSession(uint pointerId, int timestamp)
    {
        if (!_activeManipulationSessions.TryGetValue(pointerId, out var session))
            return;

        ManipulationBoundaryFeedbackEventArgs previewBoundary = new()
        {
            RoutedEvent = PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = session.Target,
            BoundaryFeedback = CreateManipulationDelta(Vector.Zero)
        };
        session.Target.RaiseEvent(previewBoundary);

        if (!previewBoundary.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleBoundary = new()
            {
                RoutedEvent = ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = session.Target,
                BoundaryFeedback = previewBoundary.BoundaryFeedback
            };
            session.Target.RaiseEvent(bubbleBoundary);
        }

        RaiseManipulationCompletedPipeline(session, isInertial: false, timestamp);
        _activeManipulationSessions.Remove(pointerId);
    }

    private static ManipulationDelta CreateManipulationDelta(Vector translation)
    {
        return new ManipulationDelta
        {
            Translation = translation,
            Rotation = 0,
            Scale = new Vector(1, 1),
            Expansion = Vector.Zero
        };
    }

    private void RaisePointerDownPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerDownEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerDownEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerDownEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerDownEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerPressedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerPressedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerMovePipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerMoveEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerMoveEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerMoveEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerMoveEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerMovedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerMovedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerUpPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerUpEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerUpEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerUpEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerUpEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerReleasedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerReleasedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerCancelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerCancelEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerCancelEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            PointerCancelEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerCancelEvent };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaisePointerWheelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerWheelChangedEventArgs args = new(point, modifiers, timestamp) { RoutedEvent = PointerEvents.PointerWheelChangedEvent };
        target.RaiseEvent(args);
    }

    private void CleanupPointerSession(uint pointerId)
    {
        _activePointerTargets.Remove(pointerId);
        _lastPointerPoints.Remove(pointerId);
        if (_activeStylusDevices.TryGetValue(pointerId, out var stylusDevice))
        {
            _activeStylusDevices.Remove(pointerId);
            if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
            {
                Tablet.CurrentStylusDevice = null;
            }
        }

        _realTimeStylus.CancelSession(pointerId);
        _activeManipulationSessions.Remove(pointerId);

        TouchDevice? touchDevice = Touch.GetDevice((int)pointerId);
        if (touchDevice != null)
        {
            touchDevice.Deactivate();
            Touch.UnregisterTouchPoint((int)pointerId);
        }
    }

    private static PointerPoint CreateMousePointerPoint(
        Point position,
        MouseButtonState left,
        MouseButtonState middle,
        MouseButtonState right,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys modifiers,
        int timestamp,
        PointerUpdateKind updateKind,
        int mouseWheelDelta = 0)
    {
        PointerPointProperties properties = new()
        {
            IsLeftButtonPressed = left == MouseButtonState.Pressed,
            IsMiddleButtonPressed = middle == MouseButtonState.Pressed,
            IsRightButtonPressed = right == MouseButtonState.Pressed,
            IsXButton1Pressed = xButton1 == MouseButtonState.Pressed,
            IsXButton2Pressed = xButton2 == MouseButtonState.Pressed,
            MouseWheelDelta = mouseWheelDelta,
            PointerUpdateKind = updateKind,
            IsPrimary = true
        };

        bool isInContact = properties.IsLeftButtonPressed ||
                           properties.IsMiddleButtonPressed ||
                           properties.IsRightButtonPressed ||
                           properties.IsXButton1Pressed ||
                           properties.IsXButton2Pressed;

        return new PointerPoint(
            MousePointerId,
            position,
            PointerDeviceType.Mouse,
            isInContact,
            properties,
            (ulong)timestamp,
            0);
    }

    private static PointerUpdateKind MapMouseButtonToPointerUpdateKind(MouseButton button, bool isPressed)
    {
        return (button, isPressed) switch
        {
            (MouseButton.Left, true) => PointerUpdateKind.LeftButtonPressed,
            (MouseButton.Left, false) => PointerUpdateKind.LeftButtonReleased,
            (MouseButton.Right, true) => PointerUpdateKind.RightButtonPressed,
            (MouseButton.Right, false) => PointerUpdateKind.RightButtonReleased,
            (MouseButton.Middle, true) => PointerUpdateKind.MiddleButtonPressed,
            (MouseButton.Middle, false) => PointerUpdateKind.MiddleButtonReleased,
            (MouseButton.XButton1, true) => PointerUpdateKind.XButton1Pressed,
            (MouseButton.XButton1, false) => PointerUpdateKind.XButton1Released,
            (MouseButton.XButton2, true) => PointerUpdateKind.XButton2Pressed,
            (MouseButton.XButton2, false) => PointerUpdateKind.XButton2Released,
            _ => PointerUpdateKind.Other
        };
    }

    /// <summary>
    /// Extracts mouse position from lParam and converts from physical pixels to DIPs.
    /// For client-area messages (WM_MOUSEMOVE, WM_LBUTTONDOWN, etc.).
    /// </summary>
    private Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return new Point(x / _dpiScale, y / _dpiScale);
    }

    private static (MouseButtonState left, MouseButtonState middle, MouseButtonState right, MouseButtonState xButton1, MouseButtonState xButton2) GetMouseButtonStates(nint wParam)
    {
        int flags = (int)wParam.ToInt64();
        return (
            (flags & MK_LBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_MBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_RBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_XBUTTON1) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_XBUTTON2) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released
        );
    }

    private static ModifierKeys GetModifierKeys()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (IsVirtualKeyDown(VK_SHIFT))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsVirtualKeyDown(VK_CONTROL))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsVirtualKeyDown(VK_MENU))
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }

    internal static void SetKeyStateProviderForTesting(Func<int, short>? provider)
    {
        s_getKeyStateProvider = provider ?? GetKeyState;
    }

    private static bool IsVirtualKeyDown(int nVirtKey)
    {
        return (s_getKeyStateProvider(nVirtKey) & 0x8000) != 0;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    #endregion

    #region Win32 Interop

    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_EX_APPWINDOW = 0x00040000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint LWA_ALPHA = 0x02;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = new(-1);
    private static readonly nint HWND_NOTOPMOST = new(-2);
    private static readonly nint HWND_TOP = nint.Zero;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int SW_SHOW = 5;
    private const int SW_SHOWNOACTIVATE = 8;
    private const int SW_HIDE = 0;
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_GETOBJECT = 0x003D;
    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_THEMECHANGED = 0x031A;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_MOVE = 0x0003;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_PAINT = 0x000F;

    // Hit test results
    private const int HTNOWHERE = 0;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTSYSMENU = 3;
    private const int HTMINBUTTON = 8;
    private const int HTMAXBUTTON = 9;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTCLOSE = 20;

    // System command constants
    private const int SC_SIZE = 0xF000;
    private const int SC_MOVE = 0xF010;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_CLOSE = 0xF060;
    private const int SC_RESTORE = 0xF120;

    // TrackPopupMenu flags
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTBUTTON = 0x0000;

    // Menu item flags
    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_ENABLED = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;

    // DWM window corner preference
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    // DWM system backdrop type (Windows 11 22H2+)
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
    // DWM_SYSTEMBACKDROP_TYPE values
    private const int DWMSBT_AUTO = 0;           // Auto
    private const int DWMSBT_NONE = 1;           // None
    private const int DWMSBT_MAINWINDOW = 2;     // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4;   // Mica Alt
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // SetWindowCompositionAttribute (undocumented, Win10+ fallback for Acrylic)
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // ABGR format
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public nint Data;
        public int DataSize;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    private const uint WM_MOVING = 0x0216;
    private const uint WM_SIZING = 0x0214;
    private const uint WM_DPICHANGED = 0x02E0;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const uint CS_HREDRAW = 0x0002;
    private const uint CS_VREDRAW = 0x0001;
    private const int COLOR_WINDOW = 5;
    private const nint IDC_ARROW = 32512;
    private const nint IDC_IBEAM = 32513;
    private const nint IDC_WAIT = 32514;
    private const nint IDC_CROSS = 32515;
    private const nint IDC_UPARROW = 32516;
    private const nint IDC_SIZE = 32640;      // Same as IDC_SIZEALL
    private const nint IDC_SIZENWSE = 32642;
    private const nint IDC_SIZENESW = 32643;
    private const nint IDC_SIZEWE = 32644;
    private const nint IDC_SIZENS = 32645;
    private const nint IDC_SIZEALL = 32646;
    private const nint IDC_NO = 32648;
    private const nint IDC_HAND = 32649;
    private const nint IDC_APPSTARTING = 32650;
    private const nint IDC_HELP = 32651;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint WM_USER = 0x0400;
    private const uint WM_APP_REPAINT = WM_USER + 1;
    private const int SIZE_RESTORED = 0;
    private const int SIZE_MINIMIZED = 1;
    private const int SIZE_MAXIMIZED = 2;

    // Input messages
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_RBUTTONDBLCLK = 0x0206;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint WM_MBUTTONDBLCLK = 0x0209;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_XBUTTONDOWN = 0x020B;
    private const uint WM_XBUTTONUP = 0x020C;
    private const uint WM_MOUSELEAVE = 0x02A3;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint WM_CANCELMODE = 0x001F;
    private const uint WM_ACTIVATE = 0x0006;
    private const uint WM_SETFOCUS = 0x0007;
    private const uint WM_KILLFOCUS = 0x0008;
    private const int WA_INACTIVE = 0;

    // Non-client mouse messages
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_NCMOUSEHOVER = 0x02A0;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    private const uint WM_NCRBUTTONDOWN = 0x00A4;
    private const uint WM_NCRBUTTONUP = 0x00A5;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_NCMOUSELEAVE = 0x02A2;

    // TrackMouseEvent flags
    private const uint TME_HOVER = 0x00000001;
    private const uint TME_LEAVE = 0x00000002;
    private const uint TME_NONCLIENT = 0x00000010;
    private const uint HOVER_DEFAULT = 0xFFFFFFFF;

    // Cursor message
    private const uint WM_SETCURSOR = 0x0020;
    private const int HTCLIENT_SETCURSOR = 1;

    // IME messages
    private const uint WM_IME_STARTCOMPOSITION = 0x010D;
    private const uint WM_IME_ENDCOMPOSITION = 0x010E;
    private const uint WM_IME_COMPOSITION = 0x010F;
    private const uint WM_IME_SETCONTEXT = 0x0281;
    private const uint WM_IME_NOTIFY = 0x0282;
    private const uint WM_IME_CHAR = 0x0286;
    private const int IACE_DEFAULT = 0x0010;

    // Virtual key codes
    private static Func<int, short> s_getKeyStateProvider = GetKeyState;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;  // Alt key

    // Mouse button state flags in wParam
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const int MK_SHIFT = 0x0004;
    private const int MK_CONTROL = 0x0008;
    private const int MK_MBUTTON = 0x0010;
    private const int MK_XBUTTON1 = 0x0020;
    private const int MK_XBUTTON2 = 0x0040;

    // Monitor constants
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[]? rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowText(nint hWnd, string lpString);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint hCursor);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RedrawWindow(nint hWnd, nint lprcUpdate, nint hrgnUpdate, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial long GetWindowLong(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial long SetWindowLong(nint hWnd, int nIndex, long dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DwmDefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam, out nint plResult);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoEx(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS
    {
        public RECT rgrc0;
        public RECT rgrc1;
        public RECT rgrc2;
        public nint lppos;
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

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableMenuItem(nint hMenu, int uIDEnableItem, uint uEnable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(nint hMenu, int uItem, uint fByPos);

    // Undocumented uxtheme APIs for dark-mode popup menus (used by Explorer, Notepad, Terminal, etc.)
    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    private static partial int SetPreferredAppMode(PreferredAppMode mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowDarkModeForWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    private static partial void FlushMenuThemes();

    private enum PreferredAppMode
    {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3,
    }

    #endregion

    #region IInputDispatcherHost

    Window IInputDispatcherHost.Self => this;
    nint IInputDispatcherHost.Handle => Handle;
    double IInputDispatcherHost.DpiScale => _dpiScale;

    UIElement? IInputDispatcherHost.HitTestElement(Point windowPosition, string tag) => HitTestElement(windowPosition, tag);
    HitTestResult? IInputDispatcherHost.HitIgnoringOverlay(Point windowPosition) => HitIgnoringOverlay(windowPosition);

    OverlayLayer IInputDispatcherHost.OverlayLayer => OverlayLayer;
    IReadOnlyList<Popup> IInputDispatcherHost.ActiveExternalPopups => ActiveExternalPopups;
    ContentDialog? IInputDispatcherHost.ActiveContentDialog => ActiveContentDialog;
    IReadOnlyList<ContentDialog> IInputDispatcherHost.ActiveInPlaceDialogs => ActiveInPlaceDialogs;

    bool IInputDispatcherHost.IsTitleBarVisible() => IsTitleBarVisible();
    TitleBarButton? IInputDispatcherHost.GetTitleBarButtonAtPoint(Point point, double windowWidth) => GetTitleBarButtonAtPoint(point, windowWidth);
    WindowTitleBarStyle IInputDispatcherHost.TitleBarStyle => TitleBarStyle;
    TitleBar? IInputDispatcherHost.TitleBar => TitleBar;

    UIElement IInputDispatcherHost.GetKeyboardEventTarget() => GetKeyboardEventTarget();
    UIElement? IInputDispatcherHost.GetTextInputTarget() => GetTextInputTarget();
    ContentDialog? IInputDispatcherHost.FindContainingInPlaceDialog() => FindContainingInPlaceDialog();
    Button? IInputDispatcherHost.FindButton(UIElement root, Func<Button, bool> predicate) => FindButton(root, predicate);

    bool IInputDispatcherHost.CanOpenDevTools => CanOpenDevTools;
    void IInputDispatcherHost.ToggleDevTools() => ToggleDevTools();
    void IInputDispatcherHost.OpenDevTools() => OpenDevTools();
    void IInputDispatcherHost.ActivateDevToolsPicker() => _devToolsWindow?.ActivatePicker();

    bool IInputDispatcherHost.CanToggleDebugHud
        => Jalium.UI.Hosting.DeveloperToolsResolver.IsDebugHudEnabled;

    bool IInputDispatcherHost.DebugHudEnabled
    {
        get => _debugHud.Enabled;
        set => _debugHud.Enabled = value;
    }

    Visibility IInputDispatcherHost.DebugHudOverlayVisibility
    {
        set => _debugHudOverlay.Visibility = value;
    }

    /// <summary>
    /// Gets the per-window frame history ring buffer used by DevTools for trend plots.
    /// Populated on every completed frame by the render HUD.
    /// </summary>
    public Jalium.UI.Diagnostics.FrameHistory FrameHistory => _debugHud.FrameHistory;

    /// <summary>
    /// Hot-switches the rendering engine (Vello/Impeller/Auto) for this window's render target.
    /// Falls through silently if the render target is not yet created.
    /// </summary>
    public void SetRenderingEngineOverride(Jalium.UI.Interop.RenderingEngine engine)
    {
        RenderTarget?.SetRenderingEngine(engine);
        RequestFullInvalidation();
        InvalidateWindow();
    }

    /// <summary>
    /// Returns the active rendering engine for this window, or Auto if no target is bound.
    /// </summary>
    public Jalium.UI.Interop.RenderingEngine CurrentRenderingEngine
        => RenderTarget?.RenderingEngine ?? Jalium.UI.Interop.RenderingEngine.Auto;

    /// <summary>
    /// Returns the active graphics backend (D3D12/Vulkan/Metal/Software).
    /// </summary>
    public Jalium.UI.Interop.RenderBackend CurrentRenderBackend
        => RenderTarget?.Backend ?? Jalium.UI.Interop.RenderBackend.Auto;

    bool IInputDispatcherHost.OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) => OnPreviewWindowKeyDown(key, modifiers, isRepeat);
    bool IInputDispatcherHost.OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers) => OnPreviewWindowKeyUp(key, modifiers);
    bool IInputDispatcherHost.OnPreviewWindowMouseDown(MouseButton button, Point position, int clickCount) => OnPreviewWindowMouseDown(button, position, clickCount);
    bool IInputDispatcherHost.OnPreviewWindowMouseUp(MouseButton button, Point position) => OnPreviewWindowMouseUp(button, position);
    bool IInputDispatcherHost.OnPreviewWindowMouseMove(Point position) => OnPreviewWindowMouseMove(position);
    bool IInputDispatcherHost.OnPreviewWindowMouseWheel(int delta, Point position) => OnPreviewWindowMouseWheel(delta, position);

    void IInputDispatcherHost.InvalidateWindow() => InvalidateWindow();
    void IInputDispatcherHost.RequestFullInvalidation() => RequestFullInvalidation();

    void IInputDispatcherHost.RequestTrackMouseLeave()
    {
        if (PlatformFactory.IsWindows && Handle != nint.Zero)
        {
            TRACKMOUSEEVENT tme = new()
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE,
                hwndTrack = Handle,
                dwHoverTime = 0
            };
            _ = TrackMouseEvent(ref tme);
        }
    }

    void IInputDispatcherHost.SetPlatformCursor(int cursorType)
    {
        _platformWindow?.SetCursor(cursorType);
    }

    void IInputDispatcherHost.UpdateInputMethodAssociation() => UpdateInputMethodAssociation();

    bool IInputDispatcherHost.IsPopupWindow(nint hwnd) => Primitives.PopupWindow.IsPopupWindow(hwnd);
    bool IInputDispatcherHost.IsVirtualKeyDown(int nVirtKey) => IsVirtualKeyDown(nVirtKey);
    void IInputDispatcherHost.WakeRenderPipeline() => WakeRenderPipeline();

    Jalium.UI.Input.StylusPlugIns.RealTimeStylus IInputDispatcherHost.RealTimeStylus => _realTimeStylus;

    #endregion
}

/// <summary>
/// Specifies the state of a window.
/// </summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
    /// <summary>
    /// The window occupies the entire screen with no borders or title bar.
    /// </summary>
    FullScreen
}

/// <summary>
/// Specifies the title bar style for a window.
/// </summary>
public enum WindowTitleBarStyle
{
    /// <summary>
    /// Use the native Windows title bar.
    /// </summary>
    Native,

    /// <summary>
    /// Use a custom title bar rendered by the application.
    /// </summary>
    Custom
}

/// <summary>
/// Specifies whether a window sizes itself to fit the size of its content.
/// </summary>
public enum SizeToContent
{
    /// <summary>
    /// The window does not automatically size itself to fit the size of its content.
    /// </summary>
    Manual,

    /// <summary>
    /// The window automatically sizes itself to fit the width of its content, but not the height.
    /// </summary>
    Width,

    /// <summary>
    /// The window automatically sizes itself to fit the height of its content, but not the width.
    /// </summary>
    Height,

    /// <summary>
    /// The window automatically sizes itself to fit both the width and height of its content.
    /// </summary>
    WidthAndHeight
}

/// <summary>
/// Specifies whether a window can be resized and whether it has minimize and maximize buttons.
/// </summary>
public enum ResizeMode
{
    /// <summary>
    /// A window cannot be resized. The Minimize and Maximize buttons are not displayed.
    /// </summary>
    NoResize,

    /// <summary>
    /// A window can only be minimized and restored. The Minimize button is displayed and enabled.
    /// </summary>
    CanMinimize,

    /// <summary>
    /// A window can be resized. The Minimize and Maximize buttons are displayed and enabled.
    /// </summary>
    CanResize,

    /// <summary>
    /// A window can be resized, with a resize grip displayed in the lower-right corner.
    /// </summary>
    CanResizeWithGrip
}

/// <summary>
/// Specifies the type of border that a Window has.
/// </summary>
public enum WindowStyle
{
    /// <summary>
    /// Only the client area is visible 鈥?the title bar and border are not shown.
    /// </summary>
    None,

    /// <summary>
    /// A window with a single border. This is the default value.
    /// </summary>
    SingleBorderWindow,

    /// <summary>
    /// A window with a 3-D border.
    /// </summary>
    ThreeDBorderWindow,

    /// <summary>
    /// A fixed tool window.
    /// </summary>
    ToolWindow
}

/// <summary>
/// Specifies the position that a Window will be shown in when it is first opened.
/// </summary>
public enum WindowStartupLocation
{
    /// <summary>
    /// The startup location of a Window is set from code, or defers to the default Windows position.
    /// </summary>
    Manual,

    /// <summary>
    /// The startup location of a Window is the center of the screen.
    /// </summary>
    CenterScreen,

    /// <summary>
    /// The startup location of a Window is the center of the Window that owns it.
    /// </summary>
    CenterOwner
}

/// <summary>
/// Specifies the reason a user session is ending.
/// </summary>
public enum ReasonSessionEnding
{
    /// <summary>
    /// The user is logging off.
    /// </summary>
    Logoff,

    /// <summary>
    /// The operating system is shutting down.
    /// </summary>
    Shutdown
}

/// <summary>
/// Provides data for the <see cref="Window.SessionEnding"/> event.
/// </summary>
public sealed class SessionEndingCancelEventArgs : System.ComponentModel.CancelEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEndingCancelEventArgs"/> class.
    /// </summary>
    public SessionEndingCancelEventArgs(ReasonSessionEnding reasonSessionEnding)
    {
        ReasonSessionEnding = reasonSessionEnding;
    }

    /// <summary>
    /// Gets the reason the session is ending.
    /// </summary>
    public ReasonSessionEnding ReasonSessionEnding { get; }
}

