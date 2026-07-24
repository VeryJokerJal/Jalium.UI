using System.Diagnostics;
using System.Runtime.InteropServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a scrollable area that can contain other visible elements.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Content")]
public partial class ScrollViewer : ContentControl
{
    private const string ScrollBarAutoHideEnvironmentVariable = "JALIUM_SCROLLBAR_AUTOHIDE";
    private static readonly bool s_isScrollBarAutoHideEnabledByDefault = DetermineDefaultScrollBarAutoHide();

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ScrollViewerAutomationPeer(this);
    }

    #region Fields

    private IScrollInfo? _scrollInfo;
    private Pen? _borderPenCached;
    private Brush? _borderPenBrush;
    private double _borderPenThickness;
    private RectangleGeometry? _layoutClipCache;
    private double _horizontalOffset;
    private double _verticalOffset;
    private double _requestedHorizontalOffset;
    private double _requestedVerticalOffset;
    private double _extentWidth;
    private double _extentHeight;
    // Non-IScrollInfo content needs a finite probe before its first overflow measure,
    // but once an axis is known to overflow, keep that axis unconstrained on the next
    // layout pass. This avoids measuring a large card page twice on every WM_SIZE.
    private bool _lastMeasureOverflowedHorizontally;
    private bool _lastMeasureOverflowedVertically;
    private double _viewportWidth;
    private double _viewportHeight;
    private double _lastNotifiedExtentWidth;
    private double _lastNotifiedExtentHeight;
    private double _lastNotifiedViewportWidth;
    private double _lastNotifiedViewportHeight;
    private double _lastNotifiedHorizontalOffset;
    private double _lastNotifiedVerticalOffset;
    private readonly ScrollBar _verticalScrollBar;
    private readonly ScrollBar _horizontalScrollBar;
    private bool _isUpdatingScrollBars;

    /// <summary>
    /// Default line scroll amount in pixels.
    /// </summary>
    public const double LineScrollAmount = 16.0;

    /// <summary>
    /// Special value indicating "scroll one page per wheel notch".
    /// </summary>
    private const uint WHEEL_PAGESCROLL = 0xFFFFFFFF;
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.RegisterAttached(
            nameof(HorizontalScrollBarVisibility),
            typeof(ScrollBarVisibility),
            typeof(ScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Disabled, OnScrollBarVisibilityChanged),
            IsValidScrollBarVisibility);

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.RegisterAttached(
            nameof(VerticalScrollBarVisibility),
            typeof(ScrollBarVisibility),
            typeof(ScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollBarVisibilityChanged),
            IsValidScrollBarVisibility);

    /// <summary>
    /// Identifies the CanContentScroll dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanContentScrollProperty =
        DependencyProperty.RegisterAttached(nameof(CanContentScroll), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the PanningMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PanningModeProperty =
        DependencyProperty.RegisterAttached(nameof(PanningMode), typeof(PanningMode), typeof(ScrollViewer),
            // Default to VerticalFirst (WinUI parity): a touch contact that
            // drifts > threshold along the y-axis locks vertical scrolling;
            // crossing the horizontal threshold first lets the gesture pan
            // sideways. Apps that ship desktop-only (mouse) UX can opt out
            // with `<ScrollViewer PanningMode="None"/>`.
            new PropertyMetadata(PanningMode.VerticalFirst, OnPanningModeChanged));

    private static void OnPanningModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Mirror PanningMode != None into IsManipulationEnabled so user
        // ManipulationDelta handlers attached to the ScrollViewer fire as
        // expected even though the built-in pan path consumes pointer events
        // directly. Only auto-set; never auto-clear, to avoid clobbering an
        // app-level opt-in to IsManipulationEnabled.
        if (d is ScrollViewer sv && e.NewValue is PanningMode mode && mode != PanningMode.None)
        {
            sv.IsManipulationEnabled = true;
        }
    }

    /// <summary>
    /// Identifies the PanningDeceleration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PanningDecelerationProperty =
        DependencyProperty.RegisterAttached(
            nameof(PanningDeceleration),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(DefaultPanningDeceleration, OnPanningParametersChanged),
            IsFiniteNonNegative);

    /// <summary>
    /// Identifies the PanningRatio dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PanningRatioProperty =
        DependencyProperty.RegisterAttached(
            nameof(PanningRatio),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(DefaultPanningRatio, OnPanningParametersChanged),
            IsFiniteNonNegative);

    /// <summary>
    /// Identifies the IsScrollInertiaEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsScrollInertiaEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollInertiaEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(true, OnScrollInertiaEnabledChanged));

    /// <summary>
    /// Identifies the ScrollInertiaDurationMs dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ScrollInertiaDurationMsProperty =
        DependencyProperty.Register(nameof(ScrollInertiaDurationMs), typeof(double), typeof(ScrollViewer),
            new PropertyMetadata(DefaultScrollInertiaDurationMs, OnScrollInertiaDurationChanged));

    /// <summary>
    /// Identifies the IsDeferredScrollingEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDeferredScrollingEnabledProperty =
        DependencyProperty.RegisterAttached(nameof(IsDeferredScrollingEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsScrollBarAutoHideEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsScrollBarAutoHideEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollBarAutoHideEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(s_isScrollBarAutoHideEnabledByDefault, OnScrollBarAutoHideEnabledChanged));

    /// <summary>
    /// Identifies the IsOverlayScrollBarEnabled dependency property.
    /// Overlay scroll bars render as compact edge indicators and do not reserve
    /// content space. The default is enabled on mobile operating systems.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOverlayScrollBarEnabledProperty =
        DependencyProperty.Register(nameof(IsOverlayScrollBarEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(
                OperatingSystem.IsAndroid() || OperatingSystem.IsIOS(),
                OnOverlayScrollBarEnabledChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the horizontal scroll bar visibility.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll bar visibility.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the content can scroll by items rather than pixels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanContentScroll
    {
        get => (bool)GetValue(CanContentScrollProperty)!;
        set => SetValue(CanContentScrollProperty, value);
    }

    /// <summary>
    /// Gets or sets the panning mode for touch interaction.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public PanningMode PanningMode
    {
        get => (PanningMode)(GetValue(PanningModeProperty) ?? PanningMode.None);
        set => SetValue(PanningModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the deceleration used to project touch/stylus panning inertia.
    /// Unit is DIPs per ms^2.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double PanningDeceleration
    {
        get => (double)GetValue(PanningDecelerationProperty)!;
        set => SetValue(PanningDecelerationProperty, value);
    }

    /// <summary>
    /// Gets or sets the translation ratio between pointer movement and scroll delta.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double PanningRatio
    {
        get => (double)GetValue(PanningRatioProperty)!;
        set => SetValue(PanningRatioProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether scroll inertia is enabled.
    /// Enabled by default for ScrollViewer so wheel and touch panning use smooth deceleration.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsScrollInertiaEnabled
    {
        get => (bool)GetValue(IsScrollInertiaEnabledProperty)!;
        set => SetValue(IsScrollInertiaEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the smooth wheel inertia duration in milliseconds.
    /// Larger values feel softer/slower. Values less than or equal to 0 disable wheel inertia.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double ScrollInertiaDurationMs
    {
        get => (double)GetValue(ScrollInertiaDurationMsProperty)!;
        set => SetValue(ScrollInertiaDurationMsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether deferred scrolling is enabled.
    /// When enabled, content position updates only when the scrollbar thumb is released,
    /// rather than continuously during thumb dragging.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDeferredScrollingEnabled
    {
        get => (bool)GetValue(IsDeferredScrollingEnabledProperty)!;
        set => SetValue(IsDeferredScrollingEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether scroll bars auto-hide when not being interacted with.
    /// Matches WinUI-style behavior and is enabled by default.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsScrollBarAutoHideEnabled
    {
        get => (bool)GetValue(IsScrollBarAutoHideEnabledProperty)!;
        set => SetValue(IsScrollBarAutoHideEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether scroll bars overlay the content as compact mobile
    /// edge indicators instead of reserving a desktop scroll-bar gutter.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOverlayScrollBarEnabled
    {
        get => (bool)GetValue(IsOverlayScrollBarEnabledProperty)!;
        set => SetValue(IsOverlayScrollBarEnabledProperty, value);
    }

    private static bool DetermineDefaultScrollBarAutoHide()
    {
        var environmentValue = Environment.GetEnvironmentVariable(ScrollBarAutoHideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            switch (environmentValue.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
            }
        }

        // Mobile scroll bars are transient edge indicators. Keep that platform
        // default even for Gallery/sample process names; the environment variable
        // above remains the explicit override for diagnostics.
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            return true;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            if (!string.IsNullOrWhiteSpace(process.ProcessName) &&
                process.ProcessName.Contains("gallery", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch
        {
            // Ignore and fall back to command line probing.
        }

        return !Environment.CommandLine.Contains("gallery", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        _lastMeasureOverflowedHorizontally = false;
        _lastMeasureOverflowedVertically = false;
        ScrollInfo = null;
        base.OnContentChanged(oldContent, newContent);
        ScrollInfo = ContentElement as IScrollInfo;
    }

    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset => _requestedHorizontalOffset;

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset => _requestedVerticalOffset;

    /// <summary>
    /// Gets the width of the scrollable content.
    /// </summary>
    public double ExtentWidth => _extentWidth;

    /// <summary>
    /// Gets the height of the scrollable content.
    /// </summary>
    public double ExtentHeight => _extentHeight;

    /// <summary>
    /// Gets the width of the viewport (visible area).
    /// </summary>
    public double ViewportWidth => _viewportWidth;

    /// <summary>
    /// Gets the height of the viewport (visible area).
    /// </summary>
    public double ViewportHeight => _viewportHeight;

    /// <summary>
    /// Gets a value indicating whether the horizontal scroll bar is at the maximum position.
    /// </summary>
    public bool IsAtHorizontalEnd => _horizontalOffset >= _extentWidth - _viewportWidth;

    /// <summary>
    /// Gets a value indicating whether the vertical scroll bar is at the maximum position.
    /// </summary>
    public bool IsAtVerticalEnd => _verticalOffset >= _extentHeight - _viewportHeight;

    /// <summary>
    /// Gets the maximum horizontal scroll offset.
    /// </summary>
    public double ScrollableWidth => Math.Max(0, _extentWidth - _viewportWidth);

    /// <summary>
    /// Gets the maximum vertical scroll offset.
    /// </summary>
    public double ScrollableHeight => Math.Max(0, _extentHeight - _viewportHeight);

    /// <summary>
    /// Gets a value indicating whether the content can be scrolled horizontally.
    /// </summary>
    public bool CanScrollHorizontally => HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && _extentWidth > _viewportWidth;

    /// <summary>
    /// Gets a value indicating whether the content can be scrolled vertically.
    /// </summary>
    public bool CanScrollVertically => VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && _extentHeight > _viewportHeight;

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ScrollChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ScrollChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ScrollChanged), RoutingStrategy.Bubble,
            typeof(ScrollChangedEventHandler), typeof(ScrollViewer));

    /// <summary>
    /// Occurs when the scroll position changes.
    /// </summary>
    public event ScrollChangedEventHandler ScrollChanged
    {
        add => AddHandler(ScrollChangedEvent, value);
        remove => RemoveHandler(ScrollChangedEvent, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// The default scroll bar width/height.
    /// </summary>
    private const double ScrollBarSize = 12.0;
    private const double OverlayScrollBarLayoutSize = 40.0;
    private const double OverlayScrollBarIndicatorThickness = 2.0;
    private const double OverlayScrollBarIndicatorEdgeInset = 2.0;

    // Smooth scroll animation fields
    private DispatcherTimer? _smoothScrollTimer;
    private DispatcherTimer? _scrollBarAutoHideTimer;
    private long _scrollBarAutoHideDeadlineTick;
    private double _smoothTargetX;
    private double _smoothTargetY;
    private bool _isSmoothScrolling;
    private bool _isApplyingSmoothScrollStep;
    private bool _areAutoHideScrollBarsRevealed;
    private bool _hasInitializedOverlayAutoHide;
    private long _lastSmoothTickTime;
    private const double DefaultScrollInertiaDurationMs = 300.0;
    private const double DefaultScrollBarAutoHideDelayMs = 3000.0;
    private const double OverlayScrollBarAutoHideDelayMs = 2000.0;
    private const int ScrollBarAutoHidePollIntervalMs = 100;
    private const double SmoothScrollDurationTailRatio = 0.05;
    private const double SmoothScrollSnapThreshold = 0.5;
    private const double SmoothScrollMinSpeedPixelsPerSecond = 60.0;
    private const double SmoothScrollMaxDeltaTimeSeconds = 0.1;
    private static int SmoothScrollIntervalMs => CompositionTarget.FrameIntervalMs;
    // Match WPF's dependency-property metadata default exactly. Applications that
    // prefer a shorter touch fling can opt into a larger value explicitly.
    private const double DefaultPanningDeceleration = 0.001;
    private const double DefaultPanningRatio = 1.0;
    private const double MaxPanningVelocityDipsPerMs = 4.0;
    private const double PointerPanningLockThreshold = 8.0;

    // Deferred scrolling fields
    private bool _isDeferredScrolling;
    private double _deferredVerticalOffset;
    private double _deferredHorizontalOffset;

    // Live thumb-drag coalescing. A thumb drag raises ScrollBar.Scroll(ThumbTrack) once per
    // physical mouse-move (a 125–1000 Hz hardware rate, not the frame rate). Applying the
    // offset on every event would trigger a full virtualized realize per move (flushed
    // synchronously by the next move's hit-test), so the most recent thumb-mapped offset is
    // stashed here and applied at most once per rendered frame — the same frame pacing the
    // wheel's smooth-scroll timer uses, which is why wheel scrolling is already smooth.
    private DispatcherTimer? _dragScrollCoalesceTimer;
    private bool _hasPendingDragVerticalScroll;
    private bool _hasPendingDragHorizontalScroll;
    private double _pendingDragVerticalOffset;
    private double _pendingDragHorizontalOffset;

    // Direct viewer-level thumb drag fallback (used by synthetic input paths in tests)
    private bool _isDraggingVerticalThumb;
    private double _dragStartMouseY;
    private double _dragStartVerticalOffset;
    private const double InputThumbHitWidth = 16.0;
    private const double InputScrollButtonSize = 16.0;
    private const double OverlayScrollBarEndInset = 3.0;
    private const double OverlayScrollBarMinThumbLength = 40.0;

    // Touch/stylus panning state
    private bool _isPointerPanningActive;
    private bool _hasPointerPanningMoved;
    private uint _activePanningPointerId;
    private Point _pointerPanningStartPoint;
    private Point _pointerPanningLastPoint;
    private long _pointerPanningLastTimestamp;
    private double _pointerPanningVelocityX;
    private double _pointerPanningVelocityY;
    private DispatcherTimer? _pointerPanningCoalesceTimer;
    private bool _hasPendingPointerPanningDelta;
    private double _pendingPointerPanningHorizontalDelta;
    private double _pendingPointerPanningVerticalDelta;

    // iOS-style over-scroll: when a finger drags past the scroll bounds the
    // content rubber-bands beyond the viewport and springs back on release.
    // Values are content-space pixel offsets, positive == content drifted
    // toward the +x/+y direction beyond the edge.
    private double _overscrollX;
    private double _overscrollY;
    private const double MaxOverscrollDips = 120.0;
    private Threading.DispatcherTimer? _bounceTimer;
    private long _bounceStartTicks;
    private double _bounceFromX, _bounceFromY;
    private const double BounceDurationMs = 320.0;
    private bool _pointerPanningAxisResolved;
    private bool _pointerPanningAllowHorizontal;
    private bool _pointerPanningAllowVertical;
    private bool _pointerPanningYieldedToAncestor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollViewer"/> class.
    /// </summary>
    public ScrollViewer()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        _verticalScrollBar = CreateScrollBar(Orientation.Vertical);
        _horizontalScrollBar = CreateScrollBar(Orientation.Horizontal);
        AddVisualChild(_verticalScrollBar);
        AddVisualChild(_horizontalScrollBar);

        // Mirror the DP default — touch panning is on by default so finger
        // drags inside the viewport scroll without needing to drag the thumb.
        // Manipulation must be enabled for ScrollViewer to host the panning
        // pipeline; OnPanningModeChanged only fires on explicit SetValue,
        // so default-value initialisation is wired up here in the ctor.
        IsManipulationEnabled = true;

        // ScrollViewer clips content by default
        ClipToBounds = true;

        // Register for input events
        AddHandler(MouseWheelEvent, new Input.MouseWheelEventHandler(HandleMouseWheel));
        AddHandler(MouseDownEvent, new Input.MouseButtonEventHandler(HandleMouseDown));
        AddHandler(MouseMoveEvent, new Input.MouseEventHandler(HandleMouseMove));
        AddHandler(MouseUpEvent, new Input.MouseButtonEventHandler(HandleMouseUp));
        AddHandler(PointerDownEvent, new Input.PointerDownEventHandler(HandlePointerDown));
        // Nested viewers all see PointerDown and become gesture candidates. An
        // ancestor must still observe handled moves to keep its baseline in
        // sync for a boundary hand-off, and must always see Up/Cancel so a
        // descendant cannot leave its candidate state active indefinitely.
        AddHandler(PointerMoveEvent, new Input.PointerMoveEventHandler(HandlePointerMove), handledEventsToo: true);
        AddHandler(PointerUpEvent, new Input.PointerUpEventHandler(HandlePointerUp), handledEventsToo: true);
        AddHandler(PointerCancelEvent, new Input.PointerCancelEventHandler(HandlePointerCancel), handledEventsToo: true);

        // Register keyboard handler
        AddHandler(KeyDownEvent, new Input.KeyEventHandler(HandleKeyDown));

        // Register for BringIntoView requests
        AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(HandleRequestBringIntoView));
    }

    private ScrollBar CreateScrollBar(Orientation orientation)
    {
        var scrollBar = new ScrollBar
        {
            Orientation = orientation,
            IsOverlayStyle = IsOverlayScrollBarEnabled,
            Focusable = false,
            Cursor = Jalium.UI.Input.Cursors.Arrow,
            Visibility = Visibility.Collapsed,
            SmallChange = LineScrollAmount
        };
        scrollBar.Scroll += OnScrollBarScroll;
        scrollBar.AddHandler(MouseEnterEvent, new Input.MouseEventHandler(OnScrollBarMouseEnter));
        scrollBar.AddHandler(MouseLeaveEvent, new Input.MouseEventHandler(OnScrollBarMouseLeave));
        return scrollBar;
    }

    private void HandleKeyDown(object sender, Input.KeyEventArgs e)
    {
        OnKeyDown(e);
    }

    private void HandleRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is FrameworkElement targetElement)
        {
            MakeVisible(targetElement, e.TargetRect);
            e.Handled = true;
        }
    }

    private void HandleMouseWheel(object sender, Input.MouseWheelEventArgs e)
    {
        OnMouseWheel(e);
    }

    private void HandleMouseDown(object sender, Input.MouseButtonEventArgs e)
    {
        // Only apply this fallback for direct viewer-originated input.
        // Real pointer input normally targets ScrollBar/Thumb directly.
        if (!ReferenceEquals(e.OriginalSource, this))
            return;

        if (e.ChangedButton != MouseButton.Left ||
            !CanScrollVertically ||
            ScrollableHeight <= 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (IsOverlayScrollBarEnabled)
        {
            var indicatorEnd = RenderSize.Width - OverlayScrollBarIndicatorEdgeInset;
            var indicatorStart = indicatorEnd - OverlayScrollBarIndicatorThickness;
            if (point.X < indicatorStart || point.X > indicatorEnd)
                return;
        }
        else if (point.X < RenderSize.Width - InputThumbHitWidth)
        {
            return;
        }

        var metrics = GetInputVerticalThumbMetrics();
        if (metrics.ScrollRange <= 0)
            return;

        if (point.Y < metrics.ThumbTop || point.Y > metrics.ThumbTop + metrics.ThumbHeight)
            return;

        CancelSmoothScroll();
        _isDraggingVerticalThumb = true;
        _dragStartMouseY = point.Y;
        _dragStartVerticalOffset = VerticalOffset;
        CaptureMouse();
        e.Handled = true;
    }

    private void HandleMouseMove(object sender, Input.MouseEventArgs e)
    {
        if (!_isDraggingVerticalThumb)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var metrics = GetInputVerticalThumbMetrics();
        if (metrics.ScrollRange <= 0)
            return;

        var point = e.GetPosition(this);
        var deltaY = point.Y - _dragStartMouseY;
        var newOffset = _dragStartVerticalOffset + (deltaY / metrics.ScrollRange) * ScrollableHeight;

        CancelSmoothScroll();
        ScrollToVerticalOffset(newOffset);
        e.Handled = true;
    }

    private void HandleMouseUp(object sender, Input.MouseButtonEventArgs e)
    {
        if (!_isDraggingVerticalThumb)
            return;

        if (e.ChangedButton != MouseButton.Left)
            return;

        _isDraggingVerticalThumb = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void HandlePointerDown(object sender, Input.PointerDownEventArgs e)
    {
        OnPointerDown(e);
    }

    private void HandlePointerMove(object sender, Input.PointerMoveEventArgs e)
    {
        if (e.Handled)
        {
            ObserveHandledDescendantPointerMove(e);
            return;
        }

        OnPointerMove(e);
    }

    private void HandlePointerUp(object sender, Input.PointerUpEventArgs e)
    {
        OnPointerUp(e);
    }

    private void HandlePointerCancel(object sender, Input.PointerCancelEventArgs e)
    {
        // handledEventsToo is used for nested-candidate cleanup. Do not let an
        // unrelated descendant cancellation stop this viewer's independent
        // wheel/inertia animation when it has no matching pan candidate.
        if (e.Handled &&
            (!_isPointerPanningActive || e.Pointer.PointerId != _activePanningPointerId))
        {
            return;
        }

        OnPointerCancel(e);
    }

    private void OnScrollBarMouseEnter(object sender, Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _verticalScrollBar) && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        RevealAutoHideScrollBarsTemporarily();
    }

    private void OnScrollBarMouseLeave(object sender, Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _verticalScrollBar) && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        // Keep WinUI-like timing: leaving the bar starts the idle countdown,
        // then slim mode is applied when the auto-hide timer elapses.
        RestartScrollBarAutoHideTimer();
    }

    private void OnPointerDown(PointerDownEventArgs e)
    {
        // Thumb owns touch dragging. The platform intentionally raises pointer
        // events even after Thumb handled TouchDown, so without this guard a
        // finger on the mobile indicator would drag both thumb and content.
        if (IsThumbInteractionSource(e.OriginalSource ?? e.Source))
            return;

        if (!CanStartPointerPanning(e.Pointer))
            return;

        // A second contact must not steal an in-flight pan or discard its
        // coalesced delta. The active contact owns this viewer until Up/Cancel.
        if (_isPointerPanningActive)
            return;

        CancelSmoothScroll();
        CancelBounceAnimation();
        DiscardPendingPointerPanningDelta();

        _isPointerPanningActive = true;
        _hasPointerPanningMoved = false;
        _activePanningPointerId = e.Pointer.PointerId;
        _pointerPanningStartPoint = e.Pointer.Position;
        _pointerPanningLastPoint = e.Pointer.Position;
        _pointerPanningLastTimestamp = e.Timestamp;
        _pointerPanningVelocityX = 0;
        _pointerPanningVelocityY = 0;
        _pointerPanningYieldedToAncestor = false;

        InitializePointerPanningAxes();
    }

    private static bool IsThumbInteractionSource(object? source)
    {
        for (Visual? current = source as Visual; current != null; current = current.VisualParent)
        {
            if (current is Thumb)
                return true;
        }

        return false;
    }

    private void OnPointerMove(PointerMoveEventArgs e)
    {
        if (!_isPointerPanningActive || e.Pointer.PointerId != _activePanningPointerId)
            return;

        var currentPoint = e.Pointer.Position;
        long currentTimestamp = e.Timestamp;
        long dt = Math.Max(1, currentTimestamp - _pointerPanningLastTimestamp);

        double deltaX = currentPoint.X - _pointerPanningLastPoint.X;
        double deltaY = currentPoint.Y - _pointerPanningLastPoint.Y;

        // Gesture ownership stays with the ancestor after a hand-off. This
        // prevents a direction reversal from reactivating the child while the
        // parent still has coalesced movement from the same contact.
        if (_pointerPanningYieldedToAncestor)
        {
            _pointerPanningLastPoint = currentPoint;
            _pointerPanningLastTimestamp = currentTimestamp;
            return;
        }

        // A horizontal child (carousel/list) inside a vertically scrolling
        // page must not consume a clearly vertical drag merely because the
        // contact contained a small horizontal wobble, and vice versa.
        if (ShouldYieldDominantPointerAxisToAncestor(currentPoint))
        {
            _pointerPanningYieldedToAncestor = true;
            _pointerPanningAxisResolved = true;
            _pointerPanningAllowHorizontal = false;
            _pointerPanningAllowVertical = false;
            _pointerPanningLastPoint = currentPoint;
            _pointerPanningLastTimestamp = currentTimestamp;
            return;
        }

        if (!_pointerPanningAxisResolved)
        {
            ResolvePointerPanningAxes(currentPoint);
            if (!_pointerPanningAxisResolved)
            {
                _pointerPanningLastPoint = currentPoint;
                _pointerPanningLastTimestamp = currentTimestamp;
                return;
            }
        }

        if (!_pointerPanningAllowHorizontal)
            deltaX = 0;
        if (!_pointerPanningAllowVertical)
            deltaY = 0;

        if (Math.Abs(deltaX) <= double.Epsilon && Math.Abs(deltaY) <= double.Epsilon)
        {
            _pointerPanningLastPoint = currentPoint;
            _pointerPanningLastTimestamp = currentTimestamp;
            return;
        }

        double ratio = GetEffectivePanningRatio();
        double horizontalDelta = -deltaX * ratio;
        double verticalDelta = -deltaY * ratio;

        // Split a packet that crosses the inner boundary. The inner viewer
        // keeps only the distance up to its edge; the same routed packet then
        // reaches the ancestor with a baseline adjusted to the exact remainder.
        if (TryHandoffPointerPanningRemainder(
                horizontalDelta,
                verticalDelta,
                currentPoint,
                currentTimestamp))
        {
            return;
        }

        ScheduleCoalescedPointerPanningDelta(horizontalDelta, verticalDelta);

        // Velocity remains input-time based rather than frame-time based. MOVE packets can be
        // coalesced for layout without losing their real timestamps or changing fling distance.
        double blend = 0.35;
        // Clamp the instantaneous velocity: jittery WM_POINTER packets
        // can occasionally report a 0.1 ms dt with a 5 DIP delta, blowing
        // the velocity to absurd values that the inertia integrator then
        // turns into a multi-screen fling.
        double instantVelocityX = Math.Clamp(deltaX / dt, -MaxPanningVelocityDipsPerMs, MaxPanningVelocityDipsPerMs);
        double instantVelocityY = Math.Clamp(deltaY / dt, -MaxPanningVelocityDipsPerMs, MaxPanningVelocityDipsPerMs);
        _pointerPanningVelocityX = (_pointerPanningVelocityX * (1 - blend)) + (instantVelocityX * blend);
        _pointerPanningVelocityY = (_pointerPanningVelocityY * (1 - blend)) + (instantVelocityY * blend);
        e.Handled = true;

        _pointerPanningLastPoint = currentPoint;
        _pointerPanningLastTimestamp = currentTimestamp;
    }

    private void OnPointerUp(PointerUpEventArgs e)
    {
        if (!_isPointerPanningActive || e.Pointer.PointerId != _activePanningPointerId)
            return;

        // UP is a queue boundary. Commit every MOVE delta before projecting inertia so the
        // release position is exact even when no CompositionTarget frame occurred in between.
        _pointerPanningCoalesceTimer?.Stop();
        CompletePendingPointerPanningDelta();

        bool restartOverlayIdleTimer = _hasPointerPanningMoved && IsOverlayScrollBarEnabled;
        if (_hasPointerPanningMoved)
        {
            // iOS behaviour: rubber-band overscroll trumps inertia — when the
            // finger lifts past the edge, spring back first and skip the fling.
            if (Math.Abs(_overscrollX) > 0.5 || Math.Abs(_overscrollY) > 0.5)
            {
                StartBounceAnimation();
            }
            else
            {
                StartPointerPanningInertia();
            }
            e.Handled = true;
        }

        ResetPointerPanningState();
        if (restartOverlayIdleTimer)
        {
            RevealAutoHideScrollBarsTemporarily();
        }
    }

    // ── Bounce-back animation ──────────────────────────────────────

    private void StartBounceAnimation()
    {
        _bounceFromX = _overscrollX;
        _bounceFromY = _overscrollY;
        _bounceStartTicks = Environment.TickCount64;
        if (_bounceTimer == null)
        {
            _bounceTimer = new Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Render,
                OnBounceTick,
                Dispatcher);
        }
        _bounceTimer.IsEnabled = true;
    }

    private void OnBounceTick(object? sender, EventArgs e)
    {
        double elapsed = Environment.TickCount64 - _bounceStartTicks;
        double t = Math.Clamp(elapsed / BounceDurationMs, 0, 1);
        // Ease-out cubic: 1 - (1-t)^3 — fast spring back, gentle settle.
        double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
        _overscrollX = _bounceFromX * (1.0 - eased);
        _overscrollY = _bounceFromY * (1.0 - eased);
        InvalidateArrange();
        UpdateScrollBarMetrics();
        if (t >= 1.0)
        {
            _overscrollX = 0;
            _overscrollY = 0;
            _bounceTimer!.IsEnabled = false;
            InvalidateArrange();
            UpdateScrollBarMetrics();
            RevealOverlayIndicatorForScrollMovement();
        }
    }

    private void CancelBounceAnimation()
    {
        if (_bounceTimer is { IsEnabled: true } t)
        {
            t.IsEnabled = false;
            RevealOverlayIndicatorForScrollMovement();
        }
    }

    private void OnPointerCancel(PointerCancelEventArgs e)
    {
        bool isActivePointer = _isPointerPanningActive && e.Pointer.PointerId == _activePanningPointerId;
        if (!isActivePointer && !_isSmoothScrolling)
            return;

        bool restartOverlayIdleTimer = isActivePointer && _hasPointerPanningMoved && IsOverlayScrollBarEnabled;
        CancelSmoothScroll();
        ResetPointerPanningState();
        if (restartOverlayIdleTimer)
        {
            RevealAutoHideScrollBarsTemporarily();
        }
        if (isActivePointer)
        {
            e.Handled = true;
        }
    }

    private bool CanStartPointerPanning(PointerPoint pointer)
    {
        if (PanningMode == PanningMode.None)
            return false;

        if (pointer.PointerDeviceType != PointerDeviceType.Touch &&
            pointer.PointerDeviceType != PointerDeviceType.Pen)
        {
            return false;
        }

        bool canPanHorizontally = CanScrollHorizontally &&
                                  (PanningMode == PanningMode.Both ||
                                   PanningMode == PanningMode.HorizontalOnly ||
                                   PanningMode == PanningMode.HorizontalFirst ||
                                   PanningMode == PanningMode.VerticalFirst);
        bool canPanVertically = CanScrollVertically &&
                                (PanningMode == PanningMode.Both ||
                                 PanningMode == PanningMode.VerticalOnly ||
                                 PanningMode == PanningMode.HorizontalFirst ||
                                 PanningMode == PanningMode.VerticalFirst);

        return canPanHorizontally || canPanVertically;
    }

    private void InitializePointerPanningAxes()
    {
        _pointerPanningAxisResolved = true;

        switch (PanningMode)
        {
            case PanningMode.HorizontalOnly:
                _pointerPanningAllowHorizontal = CanScrollHorizontally;
                _pointerPanningAllowVertical = false;
                break;
            case PanningMode.VerticalOnly:
                _pointerPanningAllowHorizontal = false;
                _pointerPanningAllowVertical = CanScrollVertically;
                break;
            case PanningMode.Both:
                _pointerPanningAllowHorizontal = CanScrollHorizontally;
                _pointerPanningAllowVertical = CanScrollVertically;
                break;
            case PanningMode.HorizontalFirst:
            case PanningMode.VerticalFirst:
                _pointerPanningAllowHorizontal = false;
                _pointerPanningAllowVertical = false;
                _pointerPanningAxisResolved = false;
                break;
            default:
                _pointerPanningAllowHorizontal = false;
                _pointerPanningAllowVertical = false;
                break;
        }
    }

    private void ResolvePointerPanningAxes(Point currentPoint)
    {
        if (PanningMode != PanningMode.HorizontalFirst && PanningMode != PanningMode.VerticalFirst)
        {
            _pointerPanningAxisResolved = true;
            return;
        }

        double totalDeltaX = currentPoint.X - _pointerPanningStartPoint.X;
        double totalDeltaY = currentPoint.Y - _pointerPanningStartPoint.Y;
        double absX = Math.Abs(totalDeltaX);
        double absY = Math.Abs(totalDeltaY);
        if (absX < PointerPanningLockThreshold && absY < PointerPanningLockThreshold)
            return;

        if (PanningMode == PanningMode.HorizontalFirst)
        {
            bool chooseHorizontal = absX >= absY || absY < PointerPanningLockThreshold;
            _pointerPanningAllowHorizontal = chooseHorizontal && CanScrollHorizontally;
            _pointerPanningAllowVertical = !chooseHorizontal && CanScrollVertically;
        }
        else
        {
            bool chooseVertical = absY >= absX || absX < PointerPanningLockThreshold;
            _pointerPanningAllowHorizontal = !chooseVertical && CanScrollHorizontally;
            _pointerPanningAllowVertical = chooseVertical && CanScrollVertically;
        }

        _pointerPanningAxisResolved = true;
    }

    private void ObserveHandledDescendantPointerMove(PointerMoveEventArgs e)
    {
        if (!_isPointerPanningActive || e.Pointer.PointerId != _activePanningPointerId)
            return;

        // Do not apply the descendant's packet twice. Keeping the last sample
        // current means that, if the descendant yields at its boundary, this
        // viewer consumes only the first unhandled packet rather than the
        // entire distance since PointerDown.
        _pointerPanningLastPoint = e.Pointer.Position;
        _pointerPanningLastTimestamp = e.Timestamp;
    }

    private bool ShouldYieldDominantPointerAxisToAncestor(Point currentPoint)
    {
        var totalDeltaX = currentPoint.X - _pointerPanningStartPoint.X;
        var totalDeltaY = currentPoint.Y - _pointerPanningStartPoint.Y;
        var absX = Math.Abs(totalDeltaX);
        var absY = Math.Abs(totalDeltaY);
        if (Math.Max(absX, absY) < PointerPanningLockThreshold)
            return false;

        var horizontalDominant = absX > absY;
        var canPanDominantAxis = horizontalDominant
            ? AllowsHorizontalPointerPanning() && CanScrollHorizontally
            : AllowsVerticalPointerPanning() && CanScrollVertically;
        if (canPanDominantAxis)
            return false;

        var ratio = GetEffectivePanningRatio();
        return horizontalDominant
            ? HasScrollableAncestorForPointerDelta(-totalDeltaX * ratio, 0)
            : HasScrollableAncestorForPointerDelta(0, -totalDeltaY * ratio);
    }

    private bool TryHandoffPointerPanningRemainder(
        double horizontalDelta,
        double verticalDelta,
        Point currentPoint,
        long currentTimestamp)
    {
        var localHorizontalDelta = _pointerPanningAllowHorizontal
            ? GetLocallyConsumableHorizontalDelta(horizontalDelta)
            : 0;
        var localVerticalDelta = _pointerPanningAllowVertical
            ? GetLocallyConsumableVerticalDelta(verticalDelta)
            : 0;
        var remainingHorizontalDelta = horizontalDelta - localHorizontalDelta;
        var remainingVerticalDelta = verticalDelta - localVerticalDelta;

        if (Math.Abs(remainingHorizontalDelta) <= double.Epsilon &&
            Math.Abs(remainingVerticalDelta) <= double.Epsilon)
        {
            return false;
        }

        var ancestor = FindScrollableAncestorForPointerDelta(
            remainingHorizontalDelta,
            remainingVerticalDelta);
        if (ancestor == null)
            return false;

        if (Math.Abs(localHorizontalDelta) > double.Epsilon ||
            Math.Abs(localVerticalDelta) > double.Epsilon)
        {
            ScheduleCoalescedPointerPanningDelta(localHorizontalDelta, localVerticalDelta);
        }

        // PointerPoint.Position is shared window space. Rebase the receiving
        // ancestor so its normal OnPointerMove path observes only the physical
        // finger distance left after this viewer consumed its edge portion.
        var ratio = GetEffectivePanningRatio();
        ancestor._pointerPanningLastPoint = new Point(
            Math.Abs(remainingHorizontalDelta) > double.Epsilon
                ? currentPoint.X + (remainingHorizontalDelta / ratio)
                : currentPoint.X,
            Math.Abs(remainingVerticalDelta) > double.Epsilon
                ? currentPoint.Y + (remainingVerticalDelta / ratio)
                : currentPoint.Y);

        _pointerPanningYieldedToAncestor = true;
        _pointerPanningLastPoint = currentPoint;
        _pointerPanningLastTimestamp = currentTimestamp;
        return true;
    }

    private bool CanConsumeHorizontalPointerDelta(double delta)
    {
        return Math.Abs(GetLocallyConsumableHorizontalDelta(delta)) > double.Epsilon;
    }

    private double GetLocallyConsumableHorizontalDelta(double delta)
    {
        var pendingDelta = _hasPendingPointerPanningDelta
            ? _pendingPointerPanningHorizontalDelta
            : 0;
        var projectedOffset = Math.Clamp(_horizontalOffset + pendingDelta, 0, ScrollableWidth);
        return delta > 0
            ? Math.Min(delta, Math.Max(0, ScrollableWidth - projectedOffset))
            : Math.Max(delta, -projectedOffset);
    }

    private bool CanConsumeVerticalPointerDelta(double delta)
    {
        return Math.Abs(GetLocallyConsumableVerticalDelta(delta)) > double.Epsilon;
    }

    private double GetLocallyConsumableVerticalDelta(double delta)
    {
        var pendingDelta = _hasPendingPointerPanningDelta
            ? _pendingPointerPanningVerticalDelta
            : 0;
        var projectedOffset = Math.Clamp(_verticalOffset + pendingDelta, 0, ScrollableHeight);
        return delta > 0
            ? Math.Min(delta, Math.Max(0, ScrollableHeight - projectedOffset))
            : Math.Max(delta, -projectedOffset);
    }

    private bool HasScrollableAncestorForPointerDelta(double horizontalDelta, double verticalDelta)
    {
        return FindScrollableAncestorForPointerDelta(horizontalDelta, verticalDelta) != null;
    }

    private ScrollViewer? FindScrollableAncestorForPointerDelta(double horizontalDelta, double verticalDelta)
    {
        for (Visual? current = this; current != null; current = current.VisualParent)
        {
            if (!ReferenceEquals(current, this) &&
                current is ScrollViewer viewer &&
                viewer._isPointerPanningActive &&
                viewer._activePanningPointerId == _activePanningPointerId &&
                !viewer._pointerPanningYieldedToAncestor &&
                viewer.CanConsumeNestedPointerDelta(horizontalDelta, verticalDelta))
            {
                return viewer;
            }
        }

        return null;
    }

    private bool CanConsumeNestedPointerDelta(double horizontalDelta, double verticalDelta)
    {
        if (PanningMode == PanningMode.None)
            return false;

        if (AllowsHorizontalPointerPanning() &&
            CanScrollHorizontally &&
            Math.Abs(horizontalDelta) > double.Epsilon &&
            CanConsumeHorizontalPointerDelta(horizontalDelta))
        {
            return true;
        }

        return AllowsVerticalPointerPanning() &&
               CanScrollVertically &&
               Math.Abs(verticalDelta) > double.Epsilon &&
               CanConsumeVerticalPointerDelta(verticalDelta);
    }

    private bool AllowsHorizontalPointerPanning()
    {
        return PanningMode == PanningMode.Both ||
               PanningMode == PanningMode.HorizontalOnly ||
               PanningMode == PanningMode.HorizontalFirst ||
               PanningMode == PanningMode.VerticalFirst;
    }

    private bool AllowsVerticalPointerPanning()
    {
        return PanningMode == PanningMode.Both ||
               PanningMode == PanningMode.VerticalOnly ||
               PanningMode == PanningMode.HorizontalFirst ||
               PanningMode == PanningMode.VerticalFirst;
    }

    private bool ApplyPointerPanningDelta(double horizontalDelta, double verticalDelta)
    {
        bool moved = false;

        if (_pointerPanningAllowHorizontal && CanScrollHorizontally && Math.Abs(horizontalDelta) > double.Epsilon)
        {
            double newOffset = Math.Clamp(_horizontalOffset + horizontalDelta, 0, ScrollableWidth);
            double consumed = newOffset - _horizontalOffset;
            if (!AreClose(newOffset, _horizontalOffset))
            {
                ScrollToHorizontalOffset(newOffset);
                moved = true;
            }
            // Any movement not absorbed by the scroll offset feeds the rubber-
            // band overscroll. We negate here because horizontalDelta is the
            // scroll-offset delta (opposite the finger direction); overscroll
            // is rendered as a content translation that follows the finger.
            double remaining = horizontalDelta - consumed;
            if (Math.Abs(remaining) > double.Epsilon)
            {
                // In a nested chain the next packet is handed to an ancestor.
                // Drop this packet's sub-frame residual instead of showing an
                // inner rubber-band while the parent begins scrolling.
                if (!HasScrollableAncestorForPointerDelta(remaining, 0))
                {
                    ApplyOverscrollDelta(ref _overscrollX, -remaining);
                    moved = true;
                    InvalidateArrange();
                    UpdateScrollBarMetrics();
                }
            }
        }

        if (_pointerPanningAllowVertical && CanScrollVertically && Math.Abs(verticalDelta) > double.Epsilon)
        {
            double newOffset = Math.Clamp(_verticalOffset + verticalDelta, 0, ScrollableHeight);
            double consumed = newOffset - _verticalOffset;
            if (!AreClose(newOffset, _verticalOffset))
            {
                ScrollToVerticalOffset(newOffset);
                moved = true;
            }
            double remaining = verticalDelta - consumed;
            if (Math.Abs(remaining) > double.Epsilon)
            {
                if (!HasScrollableAncestorForPointerDelta(0, remaining))
                {
                    ApplyOverscrollDelta(ref _overscrollY, -remaining);
                    moved = true;
                    InvalidateArrange();
                    UpdateScrollBarMetrics();
                }
            }
        }

        return moved;
    }

    // Direct panning can receive substantially more MOVE packets than the display can present.
    // Accumulate (rather than replace) their deltas and apply the sum once per rendered frame;
    // replacing with the latest delta would make scroll distance depend on input sample rate.
    private void ScheduleCoalescedPointerPanningDelta(double horizontalDelta, double verticalDelta)
    {
        _pendingPointerPanningHorizontalDelta += horizontalDelta;
        _pendingPointerPanningVerticalDelta += verticalDelta;
        _hasPendingPointerPanningDelta = true;

        // Preserve gesture semantics independently of the frame batch. Two MOVE packets that
        // cancel to a zero net frame delta still constitute a pan and retain the last sampled
        // velocity for PointerUp inertia, just as they did before coalescing.
        _hasPointerPanningMoved = true;
        if (IsOverlayScrollBarEnabled)
        {
            RevealAutoHideScrollBarsTemporarily();
        }

        if (_pointerPanningCoalesceTimer == null)
        {
            _pointerPanningCoalesceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SmoothScrollIntervalMs)
            };
            _pointerPanningCoalesceTimer.Tick += OnPointerPanningCoalesceTick;
        }

        if (!_pointerPanningCoalesceTimer.IsEnabled)
            _pointerPanningCoalesceTimer.Start();
    }

    private void OnPointerPanningCoalesceTick(object? sender, EventArgs e)
    {
        if (!_hasPendingPointerPanningDelta)
        {
            _pointerPanningCoalesceTimer?.Stop();
            return;
        }

        CompletePendingPointerPanningDelta();
    }

    private void CompletePendingPointerPanningDelta()
    {
        if (!_hasPendingPointerPanningDelta)
            return;

        double horizontalDelta = _pendingPointerPanningHorizontalDelta;
        double verticalDelta = _pendingPointerPanningVerticalDelta;
        _hasPendingPointerPanningDelta = false;
        _pendingPointerPanningHorizontalDelta = 0;
        _pendingPointerPanningVerticalDelta = 0;

        ApplyPointerPanningDelta(horizontalDelta, verticalDelta);
    }

    private void DiscardPendingPointerPanningDelta()
    {
        _hasPendingPointerPanningDelta = false;
        _pendingPointerPanningHorizontalDelta = 0;
        _pendingPointerPanningVerticalDelta = 0;
        _pointerPanningCoalesceTimer?.Stop();
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to the overscroll accumulator with rubber-band damping.
    /// Closer to the cap, smaller fraction of each additional delta is admitted, so the
    /// content asymptotes toward MaxOverscrollDips and never escapes it.
    /// </summary>
    private static void ApplyOverscrollDelta(ref double overscroll, double delta)
    {
        double magnitude = Math.Abs(overscroll);
        double resistance = 1.0 - Math.Min(0.95, magnitude / MaxOverscrollDips);
        overscroll = Math.Clamp(overscroll + delta * resistance, -MaxOverscrollDips, MaxOverscrollDips);
    }

    private void StartPointerPanningInertia()
    {
        if (!IsScrollInertiaEnabled || GetEffectiveScrollInertiaDurationMs() <= 0)
            return;

        double deceleration = GetEffectivePanningDeceleration();
        if (deceleration <= 0)
            return;

        double ratio = GetEffectivePanningRatio();
        double scrollVelocityX = -_pointerPanningVelocityX * ratio;
        double scrollVelocityY = -_pointerPanningVelocityY * ratio;

        bool hasTarget = false;

        _smoothTargetX = _horizontalOffset;
        _smoothTargetY = _verticalOffset;

        if (_pointerPanningAllowHorizontal && CanScrollHorizontally && Math.Abs(scrollVelocityX) >= 0.01)
        {
            double distance = (scrollVelocityX * Math.Abs(scrollVelocityX)) / (2 * deceleration);
            _smoothTargetX = Math.Clamp(_horizontalOffset + distance, 0, ScrollableWidth);
            hasTarget |= !AreClose(_smoothTargetX, _horizontalOffset);
        }

        if (_pointerPanningAllowVertical && CanScrollVertically && Math.Abs(scrollVelocityY) >= 0.01)
        {
            double distance = (scrollVelocityY * Math.Abs(scrollVelocityY)) / (2 * deceleration);
            _smoothTargetY = Math.Clamp(_verticalOffset + distance, 0, ScrollableHeight);
            hasTarget |= !AreClose(_smoothTargetY, _verticalOffset);
        }

        if (hasTarget)
        {
            StartSmoothScroll();
        }
    }

    private void ResetPointerPanningState()
    {
        // CANCEL/capture-loss/reset must never replay a stale drag on a later frame.
        DiscardPendingPointerPanningDelta();
        _isPointerPanningActive = false;
        _hasPointerPanningMoved = false;
        _activePanningPointerId = 0;
        _pointerPanningStartPoint = Point.Zero;
        _pointerPanningLastPoint = Point.Zero;
        _pointerPanningLastTimestamp = 0;
        _pointerPanningVelocityX = 0;
        _pointerPanningVelocityY = 0;
        _pointerPanningAxisResolved = false;
        _pointerPanningAllowHorizontal = false;
        _pointerPanningAllowVertical = false;
        _pointerPanningYieldedToAncestor = false;
    }

    private double GetEffectivePanningRatio()
    {
        double ratio = PanningRatio;
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
            return DefaultPanningRatio;
        return ratio;
    }

    private double GetEffectivePanningDeceleration()
    {
        double deceleration = PanningDeceleration;
        if (double.IsNaN(deceleration) || double.IsInfinity(deceleration))
            return DefaultPanningDeceleration;
        return Math.Max(0, deceleration);
    }

    private (double ThumbTop, double ThumbHeight, double ScrollRange) GetInputVerticalThumbMetrics()
    {
        var trackInset = IsOverlayScrollBarEnabled
            ? OverlayScrollBarEndInset
            : InputScrollButtonSize;
        var trackHeight = Math.Max(0, RenderSize.Height - (trackInset * 2));
        if (trackHeight <= 0 || ExtentHeight <= 0 || ScrollableHeight <= 0)
            return (0, 0, 0);

        var minimumThumbLength = IsOverlayScrollBarEnabled
            ? OverlayScrollBarMinThumbLength
            : 20.0;
        var thumbHeight = Math.Min(
            trackHeight,
            Math.Max(minimumThumbLength, (ViewportHeight / ExtentHeight) * trackHeight));
        var scrollRange = Math.Max(0, trackHeight - thumbHeight);
        var thumbTop = trackInset + (VerticalOffset / ScrollableHeight) * scrollRange;
        return (thumbTop, thumbHeight, scrollRange);
    }

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        var result = base.HitTestCore(point);
        if (!IsOverlayScrollBarEnabled ||
            result?.VisualHit is not Visual visualHit ||
            ContentElement is not FrameworkElement content ||
            (!IsVisualDescendantOf(visualHit, _verticalScrollBar) &&
             !IsVisualDescendantOf(visualHit, _horizontalScrollBar)))
        {
            return result;
        }

        // Overlay bars are arranged after content, so normal reverse-Z hit
        // testing reaches the parent's bar first. If a nested viewer has a
        // Thumb at the exact same 2-DIP edge location, prefer the deepest bar;
        // otherwise the inner mobile scrollbar can never receive its Hold.
        var localPoint = new Point(point.X - VisualBounds.X, point.Y - VisualBounds.Y);
        var contentResult = content.HitTest(localPoint);
        if (contentResult?.VisualHit is Visual contentHit &&
            FindOverlayScrollBarAncestor(contentHit) is { } nestedScrollBar &&
            !ReferenceEquals(nestedScrollBar, _verticalScrollBar) &&
            !ReferenceEquals(nestedScrollBar, _horizontalScrollBar))
        {
            return contentResult;
        }

        // FrameworkElement uses a thread-local reusable HitTestResult. The
        // content probe above mutates that instance, so preserve the original
        // overlay hit explicitly when no nested Thumb supersedes it.
        return new HitTestResult(visualHit);
    }

    private static bool IsVisualDescendantOf(Visual visual, Visual ancestor)
    {
        for (Visual? current = visual; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static ScrollBar? FindOverlayScrollBarAncestor(Visual visual)
    {
        for (Visual? current = visual; current != null; current = current.VisualParent)
        {
            if (current is ScrollBar { IsOverlayStyle: true } scrollBar)
                return scrollBar;
        }

        return null;
    }

    /// <inheritdoc />
    internal override bool IsPointInsideLayoutClip(Point localPoint)
    {
        var clip = GetLayoutClip();
        if (clip == null)
        {
            return true;
        }

        if (clip is Media.Geometry geometry)
        {
            // Honor rounded-rect / custom-shape viewport clips so hit-testing
            // matches the pixels the user can actually see.
            return geometry.FillContains(localPoint);
        }

        return true;
    }

    internal bool IsContentDescendant(UIElement element)
    {
        for (UIElement? current = element; current != null && !ReferenceEquals(current, this); current = current.VisualParent as UIElement)
        {
            var parent = current.VisualParent as UIElement;
            if (ReferenceEquals(parent, this))
            {
                return ReferenceEquals(current, ContentElement);
            }
        }

        return false;
    }

    internal bool IsPointWithinContentViewport(Point point)
    {
        double viewportWidth = _viewportWidth > 0 ? _viewportWidth : RenderSize.Width;
        double viewportHeight = _viewportHeight > 0 ? _viewportHeight : RenderSize.Height;

        return point.X >= 0 &&
               point.Y >= 0 &&
               point.X <= viewportWidth &&
               point.Y <= viewportHeight;
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        _isDraggingVerticalThumb = false;
        if (IsScrollBarAutoHideEnabled)
        {
            RestartScrollBarAutoHideTimer();
        }
    }

    private void RevealAutoHideScrollBarsTemporarily()
    {
        if (!IsScrollBarAutoHideEnabled || !HasAutoHideScrollBarCandidates())
            return;

        if (!_areAutoHideScrollBarsRevealed)
        {
            _areAutoHideScrollBarsRevealed = true;
            ApplyScrollBarAutoHideVisualState();
        }
        RestartScrollBarAutoHideTimer();
    }

    private void RevealOverlayIndicatorForScrollMovement()
    {
        if (IsOverlayScrollBarEnabled)
        {
            RevealAutoHideScrollBarsTemporarily();
        }
    }

    private void InitializeOverlayAutoHideIfNeeded()
    {
        if (!IsOverlayScrollBarEnabled || !IsScrollBarAutoHideEnabled)
        {
            return;
        }

        if (!HasAutoHideScrollBarCandidates())
        {
            // A later content/viewport change may introduce a scrollbar. Treat
            // that as a fresh first appearance and show it for the initial delay.
            _hasInitializedOverlayAutoHide = false;
            return;
        }

        if (_hasInitializedOverlayAutoHide)
            return;

        // Show the initial 2-DIP indicator, then let the normal two-second
        // idle countdown hide it. Without this first-use state it would enter
        // the already-hidden progress on its first layout.
        _hasInitializedOverlayAutoHide = true;
        _areAutoHideScrollBarsRevealed = true;
        RestartScrollBarAutoHideTimer();
    }

    private void HideAutoHideScrollBarsIfEligible()
    {
        if (!IsScrollBarAutoHideEnabled)
            return;

        if (ShouldKeepAnyAutoHideScrollBarVisible())
        {
            RestartScrollBarAutoHideTimer();
            return;
        }

        _areAutoHideScrollBarsRevealed = false;
        StopScrollBarAutoHideTimer();
        ApplyScrollBarAutoHideVisualState();
    }

    private bool HasAutoHideScrollBarCandidates()
    {
        bool verticalCandidate = SupportsAutoHide(VerticalScrollBarVisibility) &&
                                 _verticalScrollBar.Visibility != Visibility.Collapsed;
        bool horizontalCandidate = SupportsAutoHide(HorizontalScrollBarVisibility) &&
                                   _horizontalScrollBar.Visibility != Visibility.Collapsed;
        return verticalCandidate || horizontalCandidate;
    }

    private bool ShouldKeepAnyAutoHideScrollBarVisible()
    {
        if (!HasAutoHideScrollBarCandidates())
            return false;

        return ShouldKeepAutoHideScrollBarVisible(_verticalScrollBar) ||
               ShouldKeepAutoHideScrollBarVisible(_horizontalScrollBar);
    }

    private bool ShouldKeepAutoHideScrollBarVisible(ScrollBar scrollBar)
    {
        if (scrollBar.Visibility == Visibility.Collapsed)
            return false;

        // Keep expanded only for direct interaction with this scrollbar.
        // Avoid using ScrollViewer.IsMouseCaptureWithin here because unrelated
        // captures inside the viewer can keep bars expanded indefinitely.
        if (scrollBar.IsMouseCaptured)
            return true;

        if (ReferenceEquals(scrollBar, _verticalScrollBar) && _isDraggingVerticalThumb)
            return true;

        if (scrollBar.IsThumbDragging)
            return true;

        if (IsOverlayScrollBarEnabled &&
            (_isPointerPanningActive || _isSmoothScrolling || _bounceTimer is { IsEnabled: true }))
        {
            return true;
        }

        return scrollBar.IsMouseOver;
    }

    private void RestartScrollBarAutoHideTimer()
    {
        if (!IsScrollBarAutoHideEnabled || !HasAutoHideScrollBarCandidates())
            return;

        _scrollBarAutoHideDeadlineTick = Environment.TickCount64 + (long)GetScrollBarAutoHideDelayMs();

        if (_scrollBarAutoHideTimer == null)
        {
            _scrollBarAutoHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ScrollBarAutoHidePollIntervalMs)
            };
            _scrollBarAutoHideTimer.Tick += OnScrollBarAutoHideTimerTick;
        }

        if (!_scrollBarAutoHideTimer.IsEnabled)
        {
            _scrollBarAutoHideTimer.Start();
        }
    }

    private void StopScrollBarAutoHideTimer()
    {
        _scrollBarAutoHideTimer?.Stop();
    }

    private void OnScrollBarAutoHideTimerTick(object? sender, EventArgs e)
    {
        if (Environment.TickCount64 < _scrollBarAutoHideDeadlineTick)
            return;

        if (ShouldKeepAnyAutoHideScrollBarVisible())
        {
            _scrollBarAutoHideDeadlineTick = Environment.TickCount64 + (long)GetScrollBarAutoHideDelayMs();
            return;
        }

        _areAutoHideScrollBarsRevealed = false;
        StopScrollBarAutoHideTimer();
        ApplyScrollBarAutoHideVisualState();
    }

    private double GetScrollBarAutoHideDelayMs()
    {
        return IsOverlayScrollBarEnabled
            ? OverlayScrollBarAutoHideDelayMs
            : DefaultScrollBarAutoHideDelayMs;
    }

    internal double EffectiveScrollBarAutoHideDelayMs => GetScrollBarAutoHideDelayMs();

    private void ApplyScrollBarAutoHideVisualState()
    {
        ApplyScrollBarAutoHideVisualState(_verticalScrollBar, VerticalScrollBarVisibility);
        ApplyScrollBarAutoHideVisualState(_horizontalScrollBar, HorizontalScrollBarVisibility);
    }

    private void ApplyScrollBarAutoHideVisualState(ScrollBar scrollBar, ScrollBarVisibility visibilityMode)
    {
        if (scrollBar.Visibility == Visibility.Collapsed)
        {
            if (scrollBar.IsThumbSlim)
            {
                scrollBar.IsThumbSlim = false;
            }

            scrollBar.StartAutoHideVisualTransition(0.0);
            return;
        }

        if (!IsScrollBarAutoHideEnabled || !SupportsAutoHide(visibilityMode))
        {
            if (scrollBar.Visibility != Visibility.Visible)
            {
                scrollBar.Visibility = Visibility.Visible;
            }

            if (scrollBar.IsThumbSlim)
            {
                scrollBar.IsThumbSlim = false;
            }

            scrollBar.StartAutoHideVisualTransition(0.0);
            return;
        }

        if (scrollBar.Visibility != Visibility.Visible)
        {
            scrollBar.Visibility = Visibility.Visible;
        }

        bool keepExpanded = _areAutoHideScrollBarsRevealed || ShouldKeepAutoHideScrollBarVisible(scrollBar);
        bool shouldUseSlimThumb = !keepExpanded;
        var targetProgress = shouldUseSlimThumb ? 1.0 : 0.0;

        // Update the logical flag (without triggering the property-change callback animation,
        // since we drive the transition directly below).
        if (scrollBar.IsThumbSlim != shouldUseSlimThumb)
        {
            scrollBar.IsThumbSlim = shouldUseSlimThumb;
        }

        // Always request a visual transition to the target progress.
        // StartAutoHideVisualTransition is idempotent — it early-exits when already at target.
        scrollBar.StartAutoHideVisualTransition(targetProgress);
    }

    private static bool SupportsAutoHide(ScrollBarVisibility visibilityMode)
    {
        return visibilityMode == ScrollBarVisibility.Auto;
    }

    /// <summary>
    /// Returns a clip geometry for the viewport area.
    /// Respects the ClipToBounds property - when false, no clipping is applied.
    /// </summary>
    internal override Geometry? GetLayoutClip()
    {
        var clipEdges = ClipToBoundsEdges;
        if (!ClipToBounds || clipEdges == ClipEdges.None)
        {
            return null;
        }

        // Keep the full control unclipped so visual-child scrollbars remain visible.
        // Content clipping is handled by layout offsets and child layering.
        var clipRect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var geometryRect = ExpandBoundsClip(clipRect, clipEdges);
        if (_layoutClipCache is null ||
            _layoutClipCache.Rect != geometryRect ||
            _layoutClipCache.BoundsClipRect != clipRect ||
            _layoutClipCache.BoundsClipEdges != clipEdges)
        {
            var geometry = new RectangleGeometry(geometryRect)
            {
                BoundsClipEdges = clipEdges,
                BoundsClipRect = clipRect
            };
            geometry.Freeze();
            _layoutClipCache = geometry;
        }

        return _layoutClipCache;
    }

    #endregion

    #region Scroll Methods

    /// <summary>
    /// Scrolls to the specified horizontal offset.
    /// </summary>
    /// <param name="offset">The horizontal offset.</param>
    public void ScrollToHorizontalOffset(double offset)
    {
        offset = ValidateScrollOffset(offset, nameof(offset));

        if (!_isApplyingSmoothScrollStep)
        {
            CancelSmoothScroll();
        }

        if (_scrollInfo != null)
        {
            var oldOffset = _scrollInfo.HorizontalOffset;
            _scrollInfo.SetHorizontalOffset(offset);
            _horizontalOffset = _scrollInfo.HorizontalOffset;
            UpdateRequestedOffsetsFromContent();

            if (oldOffset != _horizontalOffset)
            {
                RevealOverlayIndicatorForScrollMovement();
                InvalidateArrange();
                RaiseScrollChanged();
                UpdateScrollBarMetrics();
            }
            return;
        }

        var oldOff = _horizontalOffset;
        _horizontalOffset = Math.Clamp(offset, 0, ScrollableWidth);
        UpdateRequestedOffsetsFromContent();

        if (oldOff != _horizontalOffset)
        {
            RevealOverlayIndicatorForScrollMovement();
            InvalidateArrange();
            RaiseScrollChanged();
            UpdateScrollBarMetrics();
        }
    }

    /// <summary>
    /// Scrolls to the specified vertical offset.
    /// </summary>
    /// <param name="offset">The vertical offset.</param>
    public void ScrollToVerticalOffset(double offset)
    {
        offset = ValidateScrollOffset(offset, nameof(offset));

        if (!_isApplyingSmoothScrollStep)
        {
            CancelSmoothScroll();
        }

        if (_scrollInfo != null)
        {
            var oldOffset = _scrollInfo.VerticalOffset;
            _scrollInfo.SetVerticalOffset(offset);
            _verticalOffset = _scrollInfo.VerticalOffset;
            UpdateRequestedOffsetsFromContent();

            if (oldOffset != _verticalOffset)
            {
                RevealOverlayIndicatorForScrollMovement();
                InvalidateArrange();
                RaiseScrollChanged();
                UpdateScrollBarMetrics();
            }
            return;
        }

        var oldOff = _verticalOffset;
        _verticalOffset = Math.Clamp(offset, 0, ScrollableHeight);
        UpdateRequestedOffsetsFromContent();

        if (oldOff != _verticalOffset)
        {
            RevealOverlayIndicatorForScrollMovement();
            InvalidateArrange();
            RaiseScrollChanged();
            UpdateScrollBarMetrics();
        }
    }

    /// <summary>
    /// Scrolls up by one line.
    /// </summary>
    public void LineUp()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.LineUp();
            SyncFromScrollInfo();
            return;
        }
        ScrollToVerticalOffset(_verticalOffset - LineScrollAmount);
    }

    /// <summary>
    /// Scrolls down by one line.
    /// </summary>
    public void LineDown()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.LineDown();
            SyncFromScrollInfo();
            return;
        }
        ScrollToVerticalOffset(_verticalOffset + LineScrollAmount);
    }

    /// <summary>
    /// Scrolls left by one line.
    /// </summary>
    public void LineLeft()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.LineLeft();
            SyncFromScrollInfo();
            return;
        }
        ScrollToHorizontalOffset(_horizontalOffset - LineScrollAmount);
    }

    /// <summary>
    /// Scrolls right by one line.
    /// </summary>
    public void LineRight()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.LineRight();
            SyncFromScrollInfo();
            return;
        }
        ScrollToHorizontalOffset(_horizontalOffset + LineScrollAmount);
    }

    /// <summary>
    /// Scrolls up by one page.
    /// </summary>
    public void PageUp()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.PageUp();
            SyncFromScrollInfo();
            return;
        }
        ScrollToVerticalOffset(_verticalOffset - _viewportHeight);
    }

    /// <summary>
    /// Scrolls down by one page.
    /// </summary>
    public void PageDown()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.PageDown();
            SyncFromScrollInfo();
            return;
        }
        ScrollToVerticalOffset(_verticalOffset + _viewportHeight);
    }

    /// <summary>
    /// Scrolls left by one page.
    /// </summary>
    public void PageLeft()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.PageLeft();
            SyncFromScrollInfo();
            return;
        }
        ScrollToHorizontalOffset(_horizontalOffset - _viewportWidth);
    }

    /// <summary>
    /// Scrolls right by one page.
    /// </summary>
    public void PageRight()
    {
        CancelSmoothScroll();

        if (_scrollInfo != null)
        {
            _scrollInfo.PageRight();
            SyncFromScrollInfo();
            return;
        }
        ScrollToHorizontalOffset(_horizontalOffset + _viewportWidth);
    }

    /// <summary>
    /// Synchronizes the ScrollViewer's offset/extent/viewport from the IScrollInfo provider
    /// after a scroll operation, and raises the ScrollChanged event if needed.
    /// </summary>
    private void SyncFromScrollInfo()
    {
        if (_scrollInfo == null)
            return;

        var oldHorizontalOffset = _horizontalOffset;
        var oldVerticalOffset = _verticalOffset;
        var oldExtentWidth = _extentWidth;
        var oldExtentHeight = _extentHeight;

        _horizontalOffset = _scrollInfo.HorizontalOffset;
        _verticalOffset = _scrollInfo.VerticalOffset;
        SyncExtentFromScrollInfo();
        if (!_isDeferredScrolling)
        {
            UpdateRequestedOffsetsFromContent();
        }
        // Note: Do NOT sync _viewportWidth/_viewportHeight here; those are authoritatively
        // computed in ArrangeOverride based on finalSize and scrollbar visibility.
        // The IScrollInfo.ViewportWidth/Height reflects the Measure constraint, which can
        // differ from the final Arrange size and cause scrollbar visibility to flicker.

        bool scrollOffsetChanged = oldHorizontalOffset != _horizontalOffset || oldVerticalOffset != _verticalOffset;
        if (scrollOffsetChanged)
        {
            RevealOverlayIndicatorForScrollMovement();
        }

        if (scrollOffsetChanged ||
            oldExtentWidth != _extentWidth || oldExtentHeight != _extentHeight)
        {
            InvalidateArrange();
        }

        RaiseScrollChanged();
        UpdateScrollBarMetrics();
    }

    /// <summary>
    /// Scrolls to the beginning (left edge).
    /// </summary>
    public void ScrollToHome()
    {
        ScrollToHorizontalOffset(0);
    }

    /// <summary>
    /// Scrolls to the end (right edge).
    /// </summary>
    public void ScrollToEnd()
    {
        ScrollToHorizontalOffset(ScrollableWidth);
    }

    /// <summary>
    /// Scrolls to the top.
    /// </summary>
    public void ScrollToTop()
    {
        ScrollToVerticalOffset(0);
    }

    /// <summary>
    /// Scrolls to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        ScrollToVerticalOffset(ScrollableHeight);
    }

    /// <summary>
    /// Scrolls to make the specified element visible.
    /// </summary>
    /// <param name="element">The element to scroll into view.</param>
    public void ScrollToElement(UIElement element)
    {
        if (element == null || ContentElement == null)
            return;

        if (element is FrameworkElement fe)
        {
            MakeVisible(fe, new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
        }
    }

    /// <summary>
    /// Scrolls the viewport to make the specified rectangle of the target element visible.
    /// </summary>
    /// <param name="element">The element to make visible.</param>
    /// <param name="targetRect">The rectangle within the element to make visible.</param>
    public void MakeVisible(FrameworkElement element, Rect targetRect)
    {
        if (element == null || ContentElement == null)
            return;

        // Calculate the element's position relative to the content
        var elementPosition = CalculatePositionRelativeToContent(element);
        if (!elementPosition.HasValue)
            return;

        // Calculate the rectangle to bring into view in content coordinates
        var rectInContent = new Rect(
            elementPosition.Value.X + targetRect.X,
            elementPosition.Value.Y + targetRect.Y,
            targetRect.Width,
            targetRect.Height);

        // Calculate the new scroll offsets needed to bring the rectangle into view
        var newHorizontalOffset = _horizontalOffset;
        var newVerticalOffset = _verticalOffset;

        // Horizontal scrolling
        if (CanScrollHorizontally)
        {
            var viewportLeft = _horizontalOffset;
            var viewportRight = _horizontalOffset + _viewportWidth;

            if (rectInContent.Left < viewportLeft)
            {
                // Element is to the left of the viewport - scroll left
                newHorizontalOffset = rectInContent.Left;
            }
            else if (rectInContent.Right > viewportRight)
            {
                // Element is to the right of the viewport - scroll right
                // Try to show the entire element, but if it's larger than viewport, show the left edge
                if (rectInContent.Width <= _viewportWidth)
                {
                    newHorizontalOffset = rectInContent.Right - _viewportWidth;
                }
                else
                {
                    newHorizontalOffset = rectInContent.Left;
                }
            }
        }

        // Vertical scrolling
        if (CanScrollVertically)
        {
            var viewportTop = _verticalOffset;
            var viewportBottom = _verticalOffset + _viewportHeight;

            if (rectInContent.Top < viewportTop)
            {
                // Element is above the viewport - scroll up
                newVerticalOffset = rectInContent.Top;
            }
            else if (rectInContent.Bottom > viewportBottom)
            {
                // Element is below the viewport - scroll down
                // Try to show the entire element, but if it's larger than viewport, show the top edge
                if (rectInContent.Height <= _viewportHeight)
                {
                    newVerticalOffset = rectInContent.Bottom - _viewportHeight;
                }
                else
                {
                    newVerticalOffset = rectInContent.Top;
                }
            }
        }

        // Apply the new scroll offsets
        if (newHorizontalOffset != _horizontalOffset)
        {
            ScrollToHorizontalOffset(newHorizontalOffset);
        }
        if (newVerticalOffset != _verticalOffset)
        {
            ScrollToVerticalOffset(newVerticalOffset);
        }
    }

    /// <summary>
    /// Calculates the position of an element relative to the content of this ScrollViewer.
    /// </summary>
    private Point? CalculatePositionRelativeToContent(FrameworkElement element)
    {
        if (ContentElement == null)
            return null;

        // Walk up the visual tree from the element to find the content root
        double x = 0;
        double y = 0;

        Visual? current = element;
        while (current != null)
        {
            if (current == ContentElement)
            {
                // When the content implements IScrollInfo (e.g. StackPanel doing its own
                // physical scrolling), its ArrangeOverride already bakes the negative
                // scroll offset into each child's _visualBounds. The accumulated y here
                // therefore represents the child's CURRENT viewport-applied position,
                // not its logical position in the content's full extent. MakeVisible
                // expects logical content coordinates so it can compare against the
                // viewport rect and compute the correct delta — add the scroll offset
                // back to undo the bake-in. Without this, BringIntoView under-scrolls
                // by exactly _scrollInfo.VerticalOffset on every call, leaving the
                // focused element below the viewport while the focus-visual adorner
                // (which uses raw _visualBounds) ends up drawn down in the footer
                // region of the window.
                if (_scrollInfo != null)
                {
                    x += _scrollInfo.HorizontalOffset;
                    y += _scrollInfo.VerticalOffset;
                }
                return new Point(x, y);
            }

            if (current is FrameworkElement fe)
            {
                x += fe.VisualBounds.X;
                y += fe.VisualBounds.Y;
            }

            current = current.VisualParent;
        }

        // Element is not a descendant of our content
        return null;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (ContentElement == null)
        {
            _extentWidth = 0;
            _extentHeight = 0;
            _viewportWidth = 0;
            _viewportHeight = 0;
            _verticalScrollBar.Measure(default);
            _horizontalScrollBar.Measure(default);
            return default(Size);
        }

        // Desktop scroll bars reserve a gutter. Mobile overlay indicators are
        // arranged above the content and therefore leave the full viewport usable.
        var reserveVertical = !IsOverlayScrollBarEnabled &&
                              (VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                               VerticalScrollBarVisibility == ScrollBarVisibility.Auto);
        var reserveHorizontal = !IsOverlayScrollBarEnabled &&
                                (HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                 HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);

        // Calculate available space for content (accounting for potential scrollbars)
        var contentAvailableWidth = availableSize.Width - (reserveVertical ? ScrollBarSize : 0);
        var contentAvailableHeight = availableSize.Height - (reserveHorizontal ? ScrollBarSize : 0);

        // The first pass for new/non-overflowing content is finite. Once a non-IScrollInfo
        // axis has already proven that it overflows, begin the next pass with that axis at
        // Infinity: the prior implementation always repeated finite -> Infinity on every
        // resize, so a page containing 100 off-screen cards was fully measured twice.
        // If the content later shrinks below the viewport, the transition check below runs
        // one finite correction pass and clears the hint.
        var finiteContentAvailable = new Size(
            Math.Max(0, contentAvailableWidth),
            Math.Max(0, contentAvailableHeight));

        bool canOverflowHorizontally =
            _scrollInfo == null &&
            HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            !double.IsInfinity(finiteContentAvailable.Width);
        bool canOverflowVertically =
            _scrollInfo == null &&
            VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            !double.IsInfinity(finiteContentAvailable.Height);
        bool startedWithHorizontalOverflow =
            canOverflowHorizontally && _lastMeasureOverflowedHorizontally;
        bool startedWithVerticalOverflow =
            canOverflowVertically && _lastMeasureOverflowedVertically;

        var initialContentAvailable = new Size(
            startedWithHorizontalOverflow ? double.PositiveInfinity : finiteContentAvailable.Width,
            startedWithVerticalOverflow ? double.PositiveInfinity : finiteContentAvailable.Height);

        ContentElement.Measure(initialContentAvailable);
        var contentDesired = ContentElement.DesiredSize;

        // Update extent from IScrollInfo or from content desired size
        if (_scrollInfo != null)
        {
            _lastMeasureOverflowedHorizontally = false;
            _lastMeasureOverflowedVertically = false;
            SyncExtentFromScrollInfo();
        }
        else
        {
            _extentWidth = contentDesired.Width;
            _extentHeight = contentDesired.Height;

            // Only fall back to an unconstrained measure on an axis if the finite pass already
            // reported overflow there. This preserves correct viewport-based form layout while
            // still allowing naturally oversized content to report its scroll extent.
            var needsHorizontalOverflowMeasure =
                canOverflowHorizontally &&
                contentDesired.Width > finiteContentAvailable.Width + 0.5;

            var needsVerticalOverflowMeasure =
                canOverflowVertically &&
                contentDesired.Height > finiteContentAvailable.Height + 0.5;

            if (needsHorizontalOverflowMeasure != startedWithHorizontalOverflow ||
                needsVerticalOverflowMeasure != startedWithVerticalOverflow)
            {
                var overflowContentAvailable = new Size(
                    needsHorizontalOverflowMeasure ? double.PositiveInfinity : finiteContentAvailable.Width,
                    needsVerticalOverflowMeasure ? double.PositiveInfinity : finiteContentAvailable.Height);

                ContentElement.Measure(overflowContentAvailable);
                contentDesired = ContentElement.DesiredSize;
                _extentWidth = contentDesired.Width;
                _extentHeight = contentDesired.Height;

                // The correction pass can change the other axis through wrapping.
                needsHorizontalOverflowMeasure =
                    canOverflowHorizontally &&
                    contentDesired.Width > finiteContentAvailable.Width + 0.5;
                needsVerticalOverflowMeasure =
                    canOverflowVertically &&
                    contentDesired.Height > finiteContentAvailable.Height + 0.5;
            }

            _lastMeasureOverflowedHorizontally = needsHorizontalOverflowMeasure;
            _lastMeasureOverflowedVertically = needsVerticalOverflowMeasure;
        }

        // Return the smaller of content size and available size
        var resultWidth = Math.Min(contentDesired.Width + (reserveVertical ? ScrollBarSize : 0), availableSize.Width);
        var resultHeight = Math.Min(contentDesired.Height + (reserveHorizontal ? ScrollBarSize : 0), availableSize.Height);

        var scrollBarLayoutSize = IsOverlayScrollBarEnabled
            ? OverlayScrollBarLayoutSize
            : ScrollBarSize;
        _verticalScrollBar.Measure(new Size(scrollBarLayoutSize, Math.Max(0, availableSize.Height)));
        _horizontalScrollBar.Measure(new Size(Math.Max(0, availableSize.Width), scrollBarLayoutSize));

        return new Size(resultWidth, resultHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double oldHorizontalOffset = _horizontalOffset;
        double oldVerticalOffset = _verticalOffset;

        // Sync extent/offset from IScrollInfo FIRST so scrollbar visibility uses fresh values
        if (_scrollInfo != null)
        {
            _horizontalOffset = _scrollInfo.HorizontalOffset;
            _verticalOffset = _scrollInfo.VerticalOffset;
            SyncExtentFromScrollInfo();
            if (!_isDeferredScrolling)
            {
                UpdateRequestedOffsetsFromContent();
            }
        }

        // Calculate if scrollbars are needed (now using up-to-date extent values)
        var needsVerticalScrollBar = VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                      (VerticalScrollBarVisibility == ScrollBarVisibility.Auto && _extentHeight > finalSize.Height);
        var needsHorizontalScrollBar = HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                        (HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && _extentWidth > finalSize.Width);

        // Overlay indicators float above content; desktop bars consume their gutter.
        var reservedVerticalGutter =
            !IsOverlayScrollBarEnabled && needsVerticalScrollBar ? ScrollBarSize : 0;
        var reservedHorizontalGutter =
            !IsOverlayScrollBarEnabled && needsHorizontalScrollBar ? ScrollBarSize : 0;

        // A live native resize can temporarily make the arranged surface smaller than
        // the fixed desktop scroll-bar gutter. The viewport is the remaining layout
        // space, so its lower bound is zero; a negative viewport is not meaningful and
        // cannot be passed to the content's Arrange rect.
        _viewportWidth = Math.Max(0, finalSize.Width - reservedVerticalGutter);
        _viewportHeight = Math.Max(0, finalSize.Height - reservedHorizontalGutter);

        if (_scrollInfo == null)
        {
            // Clamp scroll offsets
            _horizontalOffset = Math.Clamp(_horizontalOffset, 0, Math.Max(0, _extentWidth - _viewportWidth));
            _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, _extentHeight - _viewportHeight));
        }

        if (ContentElement != null)
        {
            if (_scrollInfo != null)
            {
                // IScrollInfo manages its own scrolling; arrange at full viewport size.
                // Overscroll is added as a layout offset so the content visually drifts
                // past the edge during a rubber-band gesture.
                var arrangeRect = new Rect(_overscrollX, _overscrollY, _viewportWidth, _viewportHeight);
                ContentElement.Arrange(arrangeRect);
            }
            else
            {
                // Arrange content with offset (content area excludes scrollbar space)
                var contentWidth = Math.Max(_extentWidth, _viewportWidth);
                var contentHeight = Math.Max(_extentHeight, _viewportHeight);

                var arrangeRect = new Rect(
                    -_horizontalOffset + _overscrollX,
                    -_verticalOffset + _overscrollY,
                    contentWidth,
                    contentHeight);

                ContentElement.Arrange(arrangeRect);
            }
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        UpdateScrollBarMetrics();

        if (needsVerticalScrollBar)
        {
            var scrollBarLayoutSize = IsOverlayScrollBarEnabled
                ? OverlayScrollBarLayoutSize
                : ScrollBarSize;
            _verticalScrollBar.Visibility = Visibility.Visible;
            _verticalScrollBar.Arrange(new Rect(
                Math.Max(0, finalSize.Width - scrollBarLayoutSize),
                0,
                scrollBarLayoutSize,
                Math.Max(0, _viewportHeight)));
        }
        else
        {
            _verticalScrollBar.Visibility = Visibility.Collapsed;
            _verticalScrollBar.Arrange(default);
        }

        if (needsHorizontalScrollBar)
        {
            var scrollBarLayoutSize = IsOverlayScrollBarEnabled
                ? OverlayScrollBarLayoutSize
                : ScrollBarSize;
            _horizontalScrollBar.Visibility = Visibility.Visible;
            _horizontalScrollBar.Arrange(new Rect(
                0,
                Math.Max(0, finalSize.Height - scrollBarLayoutSize),
                Math.Max(0, _viewportWidth),
                scrollBarLayoutSize));
        }
        else
        {
            _horizontalScrollBar.Visibility = Visibility.Collapsed;
            _horizontalScrollBar.Arrange(default);
        }

        InitializeOverlayAutoHideIfNeeded();
        ApplyScrollBarAutoHideVisualState();

        RaiseScrollChanged();

        return finalSize;
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    protected override int VisualChildrenCount => ContentElement != null ? 3 : 2;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (ContentElement != null)
        {
            return index switch
            {
                0 => ContentElement,
                1 => _verticalScrollBar,
                2 => _horizontalScrollBar,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        return index switch
        {
            0 => _verticalScrollBar,
            1 => _horizontalScrollBar,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var dc = drawingContext;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.Left > 0)
        {
            if (_borderPenCached == null || _borderPenBrush != BorderBrush || _borderPenThickness != BorderThickness.Left)
            {
                _borderPenBrush = BorderBrush;
                _borderPenThickness = BorderThickness.Left;
                _borderPenCached = new Pen(BorderBrush, BorderThickness.Left);
            }
            dc.DrawRectangle(null, _borderPenCached, bounds);
        }

        // Content and scrollbars render through the visual tree.
    }

    #endregion
    #region Input Handling

    /// <summary>
    /// Handles mouse wheel events for scrolling.
    /// </summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        bool useSmoothWheelInertia = IsScrollInertiaEnabled && GetEffectiveScrollInertiaDurationMs() > 0;

        // Delegate to IScrollInfo if available
        if (_scrollInfo != null)
        {
            SyncFromScrollInfo();

            // Match the non-IScrollInfo nested-scroll behavior below: when this
            // viewport is already at the requested boundary, leave the routed
            // event untouched so an ancestor ScrollViewer can consume it.
            // IScrollInfo owns its viewport metrics. ScrollViewer's arranged viewport can
            // intentionally differ (scrollbar chrome, margins, or a test/provider that has not
            // been arranged yet), so using ScrollableHeight here can swallow a wheel event at
            // the provider's real boundary.
            var providerMaxOffset = Math.Max(
                0,
                _scrollInfo.ExtentHeight - _scrollInfo.ViewportHeight);
            bool atTop = _scrollInfo.VerticalOffset <= 0.5;
            bool atBottom = _scrollInfo.VerticalOffset >= providerMaxOffset - 0.5;
            bool scrollingUp = e.Delta > 0;
            bool scrollingDown = e.Delta < 0;
            if ((scrollingUp && atTop) || (scrollingDown && atBottom) || e.Delta == 0)
            {
                return;
            }

            if (useSmoothWheelInertia)
            {
                // Smooth animated scroll through IScrollInfo
                var delta = ComputeMouseWheelDelta(e.Delta, LineScrollAmount, _viewportHeight);

                if (!_isSmoothScrolling)
                    _smoothTargetY = _verticalOffset;
                _smoothTargetY = Math.Clamp(_smoothTargetY + delta, 0, ScrollableHeight);
                StartSmoothScroll();
            }
            else
            {
                // Immediate scroll
                CancelSmoothScroll();
                if (e.Delta > 0)
                    _scrollInfo.MouseWheelUp();
                else if (e.Delta < 0)
                    _scrollInfo.MouseWheelDown();
                SyncFromScrollInfo();
            }

            e.Handled = true;
            return;
        }

        if (CanScrollVertically)
        {
            // Only consume the event if we can actually scroll in the requested direction.
            // This allows nested ScrollViewers to bubble the event to the parent when at bounds.
            bool atTop = _verticalOffset <= 0;
            bool atBottom = _verticalOffset >= ScrollableHeight;
            bool scrollingUp = e.Delta > 0;
            bool scrollingDown = e.Delta < 0;

            if ((scrollingUp && atTop) || (scrollingDown && atBottom))
            {
                // At boundary: don't handle, let parent ScrollViewer process it
            }
            else
            {
                var delta = ComputeMouseWheelDelta(e.Delta, LineScrollAmount, _viewportHeight);

                if (useSmoothWheelInertia)
                {
                    // Smooth animated scroll: accumulate target, animate toward it
                    if (!_isSmoothScrolling)
                        _smoothTargetY = _verticalOffset;
                    _smoothTargetY = Math.Clamp(_smoothTargetY + delta, 0, ScrollableHeight);
                    StartSmoothScroll();
                }
                else
                {
                    CancelSmoothScroll();
                    ScrollToVerticalOffset(_verticalOffset + delta);
                }

                e.Handled = true;
            }
        }
        else if (CanScrollHorizontally)
        {
            bool atLeft = _horizontalOffset <= 0;
            bool atRight = _horizontalOffset >= ScrollableWidth;
            bool scrollingLeft = e.Delta > 0;
            bool scrollingRight = e.Delta < 0;

            if ((scrollingLeft && atLeft) || (scrollingRight && atRight))
            {
                // At boundary: don't handle, let parent ScrollViewer process it
            }
            else
            {
                var delta = ComputeMouseWheelDelta(e.Delta, LineScrollAmount, _viewportWidth);

                if (useSmoothWheelInertia)
                {
                    if (!_isSmoothScrolling)
                        _smoothTargetX = _horizontalOffset;
                    _smoothTargetX = Math.Clamp(_smoothTargetX + delta, 0, ScrollableWidth);
                    StartSmoothScroll();
                }
                else
                {
                    CancelSmoothScroll();
                    ScrollToHorizontalOffset(_horizontalOffset + delta);
                }

                e.Handled = true;
            }
        }
    }

    private void StartSmoothScroll()
    {
        if (!IsScrollInertiaEnabled || GetEffectiveScrollInertiaDurationMs() <= 0)
        {
            _isApplyingSmoothScrollStep = true;
            try
            {
                ScrollToVerticalOffset(_smoothTargetY);
                ScrollToHorizontalOffset(_smoothTargetX);
            }
            finally
            {
                _isApplyingSmoothScrollStep = false;
            }

            StopSmoothScroll();
            return;
        }

        _isSmoothScrolling = true;

        if (_smoothScrollTimer == null)
        {
            _smoothScrollTimer = new DispatcherTimer();
            _smoothScrollTimer.Interval = TimeSpan.FromMilliseconds(SmoothScrollIntervalMs);
            _smoothScrollTimer.Tick += OnSmoothScrollTick;
        }

        if (!_smoothScrollTimer.IsEnabled)
        {
            _lastSmoothTickTime = Environment.TickCount64;
            _smoothScrollTimer.Start();
        }
    }

    private void StopSmoothScroll()
    {
        _smoothScrollTimer?.Stop();
        _isSmoothScrolling = false;
        if (IsScrollBarAutoHideEnabled)
        {
            RestartScrollBarAutoHideTimer();
        }
    }

    private void OnSmoothScrollTick(object? sender, EventArgs e)
    {
        if (!_isSmoothScrolling)
            return;

        long now = Environment.TickCount64;
        long elapsedMs = now - _lastSmoothTickTime;
        if (elapsedMs <= 0)
        {
            elapsedMs = Math.Max(1, SmoothScrollIntervalMs);
        }
        _lastSmoothTickTime = now;
        AdvanceSmoothScrollByMilliseconds(elapsedMs);
    }

    private void AdvanceSmoothScrollByMilliseconds(long elapsedMs)
    {
        if (!_isSmoothScrolling)
            return;

        _smoothTargetY = Math.Clamp(_smoothTargetY, 0, ScrollableHeight);
        _smoothTargetX = Math.Clamp(_smoothTargetX, 0, ScrollableWidth);
        if (elapsedMs <= 0)
            elapsedMs = Math.Max(1, SmoothScrollIntervalMs);

        double dtSeconds = Math.Min(elapsedMs / 1000.0, SmoothScrollMaxDeltaTimeSeconds);
        double alpha = ComputeSmoothAlpha(dtSeconds);
        double minStep = SmoothScrollMinSpeedPixelsPerSecond * dtSeconds;

        bool moved = false;

        _isApplyingSmoothScrollStep = true;
        try
        {
            moved |= StepSmoothAxis(_smoothTargetY, _verticalOffset, ScrollToVerticalOffset, alpha, minStep);
            moved |= StepSmoothAxis(_smoothTargetX, _horizontalOffset, ScrollToHorizontalOffset, alpha, minStep);
        }
        finally
        {
            _isApplyingSmoothScrollStep = false;
        }

        if (!moved)
        {
            StopSmoothScroll();
        }
    }

    /// <summary>
    /// Handles key down events for keyboard scrolling.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (TemplatedParent is Control { HandlesScrolling: true })
            return;

        switch (e.Key)
        {
            case Key.Up:
                LineUp();
                e.Handled = true;
                break;
            case Key.Down:
                LineDown();
                e.Handled = true;
                break;
            case Key.Left:
                LineLeft();
                e.Handled = true;
                break;
            case Key.Right:
                LineRight();
                e.Handled = true;
                break;
            case Key.PageUp:
                PageUp();
                e.Handled = true;
                break;
            case Key.PageDown:
                PageDown();
                e.Handled = true;
                break;
            case Key.Home:
                if (e.IsControlDown)
                    ScrollToTop();
                else
                    ScrollToHome();
                e.Handled = true;
                break;
            case Key.End:
                if (e.IsControlDown)
                    ScrollToBottom();
                else
                    ScrollToEnd();
                e.Handled = true;
                break;
        }
    }

    private void OnScrollBarScroll(object sender, ScrollEventArgs e)
    {
        if (_isUpdatingScrollBars)
            return;

        bool isVertical = ReferenceEquals(sender, _verticalScrollBar);
        if (!isVertical && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        // Mouse-wheel input on the scrollbar track bubbles up to ScrollViewer.OnMouseWheel
        // (ScrollBar no longer handles the wheel itself), so every Scroll event that reaches
        // this handler comes from a direct interaction — thumb drag, page click, or line click.
        // Any in-flight wheel-driven smooth scroll must be cancelled so the direct interaction
        // does not compete with an animation.
        CancelSmoothScroll();
        RevealOverlayIndicatorForScrollMovement();

        HandleScrollBarValueChange(e, isVertical ? ScrollableHeight : ScrollableWidth, isVertical);
    }

    private void HandleScrollBarValueChange(ScrollEventArgs e, double maxValue, bool isVertical)
    {
        var clampedValue = Math.Clamp(e.NewValue, 0, Math.Max(0, maxValue));

        // Deferred scrolling (opt-in): keep the content still while the thumb is being
        // dragged and apply the offset only once, when the thumb is released.
        if (IsDeferredScrollingEnabled && e.ScrollEventType == ScrollEventType.ThumbTrack)
        {
            _isDeferredScrolling = true;
            if (isVertical)
                _deferredVerticalOffset = clampedValue;
            else
                _deferredHorizontalOffset = clampedValue;
            UpdateDeferredRequestedOffset(isVertical, clampedValue);
            return;
        }

        if (IsDeferredScrollingEnabled && e.ScrollEventType == ScrollEventType.EndScroll && _isDeferredScrolling)
        {
            StopDragScrollCoalesce();
            ApplyScrollOffset(isVertical, isVertical ? _deferredVerticalOffset : _deferredHorizontalOffset);
            _isDeferredScrolling = false;
            // The scrollbar mapping is intentionally frozen for the lifetime of thumb capture.
            // Thaw it even when the final content offset did not change (and therefore did not
            // naturally call UpdateScrollBarMetrics).
            UpdateScrollBarMetrics();
            return;
        }

        _isDeferredScrolling = false;

        // Live thumb drag (default path): coalesce the content-offset apply to one update per
        // rendered frame, mirroring the wheel's smooth-scroll frame pacing.
        //
        // A thumb drag raises ThumbTrack once per physical mouse-move (a 125–1000 Hz hardware
        // rate, not the frame rate). Applying the offset on every event schedules a layout /
        // virtualization pass that the *next* move's hit-test flushes synchronously
        // (WindowInputDispatcher.HandleMouseMove → Window.HitTestElement →
        // EnsureLayoutValidForInput → UpdateLayout), so a virtualized list realizes a full
        // viewport of containers many times per frame → visible jank. The wheel stays smooth
        // precisely because its DispatcherTimer is frame-paced (one realize per frame).
        // Coalescing the drag onto the same per-frame cadence removes the multiplication while
        // the thumb itself keeps following the cursor on every move.
        if (e.ScrollEventType == ScrollEventType.ThumbTrack)
        {
            ScheduleCoalescedDragScroll(isVertical, clampedValue);
            return;
        }

        // Line/page clicks and thumb release (EndScroll) apply immediately. During a live drag,
        // the content offset intentionally trails the thumb by up to one frame. If release lands
        // before that frame is committed, e.NewValue can still contain the last applied content
        // offset; the pending ThumbTrack value is the authoritative pointer position.
        var finalValue = clampedValue;
        if (e.ScrollEventType == ScrollEventType.EndScroll)
        {
            var appliedOffset = isVertical ? VerticalOffset : HorizontalOffset;
            var endValueIsStaleAppliedOffset = Math.Abs(clampedValue - appliedOffset) <= 0.01;
            if (endValueIsStaleAppliedOffset && isVertical && _hasPendingDragVerticalScroll)
                finalValue = Math.Clamp(_pendingDragVerticalOffset, 0, Math.Max(0, maxValue));
            else if (endValueIsStaleAppliedOffset && !isVertical && _hasPendingDragHorizontalScroll)
                finalValue = Math.Clamp(_pendingDragHorizontalOffset, 0, Math.Max(0, maxValue));
        }

        // Stop any in-flight drag coalescing first so discrete clicks stay instant and a stale
        // timer tick cannot overwrite the just-committed final position.
        StopDragScrollCoalesce();
        ApplyScrollOffset(isVertical, finalValue);
        if (e.ScrollEventType == ScrollEventType.EndScroll)
        {
            UpdateScrollBarMetrics();
        }
    }

    private void ApplyScrollOffset(bool isVertical, double value)
    {
        if (isVertical)
            ScrollToVerticalOffset(value);
        else
            ScrollToHorizontalOffset(value);
    }

    // ── Live thumb-drag coalescing ─────────────────────────────────────
    // Stash the most recent thumb-mapped offset and apply it at most once per rendered frame.
    // The timer interval is the frame interval, which folds the timer onto
    // CompositionTarget.Rendering (see DispatcherTimer.ShouldUseCompositionTarget) — the exact
    // mechanism StartSmoothScroll uses for the wheel, so drag and wheel share the same
    // one-realize-per-frame guarantee. The thumb visual still tracks the cursor on every move
    // (ScrollBar.Value is set per DragDelta); only the expensive content realize is throttled.

    private void ScheduleCoalescedDragScroll(bool isVertical, double value)
    {
        if (isVertical)
        {
            _pendingDragVerticalOffset = value;
            _hasPendingDragVerticalScroll = true;
        }
        else
        {
            _pendingDragHorizontalOffset = value;
            _hasPendingDragHorizontalScroll = true;
        }

        if (_dragScrollCoalesceTimer == null)
        {
            _dragScrollCoalesceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SmoothScrollIntervalMs)
            };
            _dragScrollCoalesceTimer.Tick += OnDragScrollCoalesceTick;
        }

        if (!_dragScrollCoalesceTimer.IsEnabled)
            _dragScrollCoalesceTimer.Start();
    }

    private void OnDragScrollCoalesceTick(object? sender, EventArgs e)
    {
        // Apply the latest thumb position once this frame. When the user stops moving the
        // thumb no fresh value arrives, so the next idle tick stops the timer.
        if (!FlushPendingDragScroll())
            _dragScrollCoalesceTimer?.Stop();
    }

    private bool FlushPendingDragScroll()
    {
        bool applied = false;

        if (_hasPendingDragVerticalScroll)
        {
            _hasPendingDragVerticalScroll = false;
            ScrollToVerticalOffset(_pendingDragVerticalOffset);
            applied = true;
        }

        if (_hasPendingDragHorizontalScroll)
        {
            _hasPendingDragHorizontalScroll = false;
            ScrollToHorizontalOffset(_pendingDragHorizontalOffset);
            applied = true;
        }

        return applied;
    }

    private void StopDragScrollCoalesce()
    {
        _hasPendingDragVerticalScroll = false;
        _hasPendingDragHorizontalScroll = false;
        _dragScrollCoalesceTimer?.Stop();
    }

    private void UpdateScrollBarMetrics()
    {
        _isUpdatingScrollBars = true;
        try
        {
            // Overscroll grows the *effective extent* so the thumb shrinks
            // to visually reflect "there's content beyond the edge"
            // (iOS / WinUI rubber-band feedback). The thumb stays pinned to
            // the edge that's being pulled past.
            double vOver = Math.Abs(_overscrollY);
            double hOver = Math.Abs(_overscrollX);

            ConfigureScrollBar(
                _verticalScrollBar,
                ScrollableHeight + vOver,
                _viewportHeight,
                _verticalOffset + Math.Max(0, -_overscrollY),
                VerticalScrollBarVisibility,
                canScroll: CanScrollVertically);

            ConfigureScrollBar(
                _horizontalScrollBar,
                ScrollableWidth + hOver,
                _viewportWidth,
                _horizontalOffset + Math.Max(0, -_overscrollX),
                HorizontalScrollBarVisibility,
                canScroll: CanScrollHorizontally);
        }
        finally
        {
            _isUpdatingScrollBars = false;
        }

        ApplyScrollBarAutoHideVisualState();
        UpdateScrollMetricDependencyProperties();
    }

    private void SyncExtentFromScrollInfo()
    {
        if (_scrollInfo == null)
            return;

        var margin = GetContentMargin();
        _extentWidth = Math.Max(0, _scrollInfo.ExtentWidth + margin.Width);
        _extentHeight = Math.Max(0, _scrollInfo.ExtentHeight + margin.Height);
    }

    /// <summary>
    /// Re-pulls offset and extent from the <see cref="IScrollInfo"/> content after the content changed
    /// them WITHOUT a setter call (e.g. a virtualizing panel re-coercing its committed offset at the
    /// end of measure when the estimated extent shifted, or a +Infinity scroll-to-end resolving as
    /// more rows realize). Raises ScrollChanged for any offset delta and refreshes the scroll bars.
    /// </summary>
    public void InvalidateScrollInfo()
    {
        if (_scrollInfo == null)
            return;

        var oldHorizontal = _horizontalOffset;
        var oldVertical = _verticalOffset;
        var oldExtentWidth = _extentWidth;
        var oldExtentHeight = _extentHeight;

        _horizontalOffset = _scrollInfo.HorizontalOffset;
        _verticalOffset = _scrollInfo.VerticalOffset;
        SyncExtentFromScrollInfo();
        if (!_isDeferredScrolling)
        {
            UpdateRequestedOffsetsFromContent();
        }

        if (oldHorizontal != _horizontalOffset || oldVertical != _verticalOffset ||
            oldExtentWidth != _extentWidth || oldExtentHeight != _extentHeight)
        {
            InvalidateArrange();
        }

        RaiseScrollChanged();
        UpdateScrollBarMetrics();
    }

    private (double Width, double Height) GetContentMargin()
    {
        if (ContentElement is not FrameworkElement frameworkElement)
            return default;

        var margin = frameworkElement.Margin;
        // The per-axis sums can legitimately be negative (negative margins are valid
        // and shrink the scrollable extent), so they must not round-trip through
        // Size — its constructor throws on negative dimensions. The extent addition
        // in SyncExtentFromScrollInfo clamps the combined result to zero.
        return (
            CoerceFiniteMargin(margin.Left) + CoerceFiniteMargin(margin.Right),
            CoerceFiniteMargin(margin.Top) + CoerceFiniteMargin(margin.Bottom));
    }

    private static double CoerceFiniteMargin(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        return value;
    }

    private static void ConfigureScrollBar(
        ScrollBar scrollBar,
        double maxOffset,
        double viewportSize,
        double offset,
        ScrollBarVisibility visibilityMode,
        bool canScroll)
    {
        static double CoerceFiniteNonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return 0;
            }

            return value;
        }

        var safeMaxOffset = CoerceFiniteNonNegative(maxOffset);
        var safeViewportSize = CoerceFiniteNonNegative(viewportSize);
        var safeOffset = CoerceFiniteNonNegative(offset);

        // Maximum, ViewportSize, Value and Visibility form one mapping from pointer distance to
        // scroll offset. Keep the whole mapping immutable while the Thumb owns capture. A
        // transient virtualized measure can otherwise shrink Maximum (or collapse an Auto bar),
        // which immediately coerces Value and arranges Track at zero length. Every subsequent
        // drag delta would then map to the same value and the captured thumb appears frozen.
        // EndScroll runs after ScrollBar clears IsThumbDragging and synchronizes the latest
        // metrics in one atomic-looking update.
        if (scrollBar.IsThumbDragging)
        {
            return;
        }

        scrollBar.Minimum = 0;
        scrollBar.Maximum = safeMaxOffset;
        scrollBar.ViewportSize = safeViewportSize;
        scrollBar.LargeChange = Math.Max(1.0, safeViewportSize);

        var visibility = visibilityMode switch
        {
            ScrollBarVisibility.Disabled => Visibility.Collapsed,
            ScrollBarVisibility.Hidden => Visibility.Collapsed,
            ScrollBarVisibility.Visible => Visibility.Visible,
            ScrollBarVisibility.Auto => (canScroll && safeMaxOffset > 0) ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed
        };

        if (scrollBar.Visibility != visibility)
        {
            scrollBar.Visibility = visibility;
        }

        var clampedValue = Math.Clamp(safeOffset, 0, safeMaxOffset);
        if (Math.Abs(scrollBar.Value - clampedValue) > double.Epsilon)
        {
            scrollBar.Value = clampedValue;
        }
    }

    #endregion

    #region Private Methods

    private static void OnPanningParametersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        // Updating parameters while actively panning should restart velocity accumulation
        // so inertia projection stays stable.
        if (!scrollViewer._isPointerPanningActive)
            return;

        scrollViewer._pointerPanningVelocityX = 0;
        scrollViewer._pointerPanningVelocityY = 0;
    }

    private static void OnScrollInertiaDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        scrollViewer.SnapPendingSmoothScrollIfDisabled();
    }

    private static void OnScrollInertiaEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        scrollViewer.SnapPendingSmoothScrollIfDisabled();
    }

    private void CancelSmoothScroll()
    {
        if (!_isSmoothScrolling)
            return;

        StopSmoothScroll();
        _smoothTargetX = _horizontalOffset;
        _smoothTargetY = _verticalOffset;
    }

    private double GetEffectiveScrollInertiaDurationMs()
    {
        var durationMs = ScrollInertiaDurationMs;
        if (double.IsNaN(durationMs) || double.IsInfinity(durationMs))
            return DefaultScrollInertiaDurationMs;
        return durationMs;
    }

    private void SnapPendingSmoothScrollIfDisabled()
    {
        if ((IsScrollInertiaEnabled && GetEffectiveScrollInertiaDurationMs() > 0) || !_isSmoothScrolling)
            return;

        _isApplyingSmoothScrollStep = true;
        try
        {
            ScrollToVerticalOffset(_smoothTargetY);
            ScrollToHorizontalOffset(_smoothTargetX);
        }
        finally
        {
            _isApplyingSmoothScrollStep = false;
        }

        StopSmoothScroll();
    }

    private double ComputeSmoothAlpha(double dtSeconds)
    {
        var durationMs = GetEffectiveScrollInertiaDurationMs();
        if (durationMs <= 0 || dtSeconds <= 0)
            return 1.0;

        var durationSeconds = durationMs / 1000.0;
        var decay = -Math.Log(SmoothScrollDurationTailRatio) / durationSeconds;
        var alpha = 1.0 - Math.Exp(-decay * dtSeconds);
        return Math.Clamp(alpha, 0.0, 1.0);
    }

    private static bool StepSmoothAxis(double target, double current, Action<double> setter, double alpha, double minStep)
    {
        var remaining = target - current;

        if (Math.Abs(remaining) <= 0.01)
            return false;

        if (Math.Abs(remaining) <= SmoothScrollSnapThreshold)
        {
            setter(target);
            return true;
        }

        var step = remaining * alpha;
        if (Math.Abs(step) < minStep)
            step = Math.Sign(remaining) * minStep;
        if (Math.Abs(step) > Math.Abs(remaining))
            step = remaining;

        setter(current + step);
        return true;
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 0.001;
    }

    private void RaiseScrollChanged()
    {
        double horizontalChange = _horizontalOffset - _lastNotifiedHorizontalOffset;
        double verticalChange = _verticalOffset - _lastNotifiedVerticalOffset;
        double extentWidthChange = _extentWidth - _lastNotifiedExtentWidth;
        double extentHeightChange = _extentHeight - _lastNotifiedExtentHeight;
        double viewportWidthChange = _viewportWidth - _lastNotifiedViewportWidth;
        double viewportHeightChange = _viewportHeight - _lastNotifiedViewportHeight;

        if (AreClose(horizontalChange, 0)
            && AreClose(verticalChange, 0)
            && AreClose(extentWidthChange, 0)
            && AreClose(extentHeightChange, 0)
            && AreClose(viewportWidthChange, 0)
            && AreClose(viewportHeightChange, 0))
        {
            return;
        }

        var e = new ScrollChangedEventArgs(
            ScrollChangedEvent,
            this,
            horizontalChange,
            verticalChange,
            _horizontalOffset,
            _verticalOffset,
            _viewportWidth,
            _viewportHeight,
            _extentWidth,
            _extentHeight,
            extentWidthChange,
            extentHeightChange,
            viewportWidthChange,
            viewportHeightChange);

        _lastNotifiedExtentWidth = _extentWidth;
        _lastNotifiedExtentHeight = _extentHeight;
        _lastNotifiedViewportWidth = _viewportWidth;
        _lastNotifiedViewportHeight = _viewportHeight;
        _lastNotifiedHorizontalOffset = _horizontalOffset;
        _lastNotifiedVerticalOffset = _verticalOffset;

        OnScrollChanged(e);
    }

    private static void OnScrollBarVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            // Update IScrollInfo scroll capabilities when visibility changes
            if (scrollViewer._scrollInfo != null)
            {
                scrollViewer._scrollInfo.CanHorizontallyScroll =
                    scrollViewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
                scrollViewer._scrollInfo.CanVerticallyScroll =
                    scrollViewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            }

            scrollViewer.InvalidateMeasure();
            scrollViewer.ApplyScrollBarAutoHideVisualState();
        }
    }

    private static void OnScrollBarAutoHideEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        if (!scrollViewer.IsScrollBarAutoHideEnabled)
        {
            scrollViewer._hasInitializedOverlayAutoHide = false;
            scrollViewer._areAutoHideScrollBarsRevealed = true;
            scrollViewer.StopScrollBarAutoHideTimer();
        }
        else
        {
            scrollViewer._hasInitializedOverlayAutoHide = false;
            if (scrollViewer.IsOverlayScrollBarEnabled)
            {
                scrollViewer._areAutoHideScrollBarsRevealed = false;
                scrollViewer.InitializeOverlayAutoHideIfNeeded();
            }
            else
            {
                scrollViewer._areAutoHideScrollBarsRevealed = false;
                scrollViewer.RestartScrollBarAutoHideTimer();
            }
        }

        scrollViewer.ApplyScrollBarAutoHideVisualState();
    }

    private static void OnOverlayScrollBarEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        var enabled = e.NewValue is true;
        scrollViewer._hasInitializedOverlayAutoHide = false;
        scrollViewer._verticalScrollBar.IsOverlayStyle = enabled;
        scrollViewer._horizontalScrollBar.IsOverlayStyle = enabled;

        if (enabled)
        {
            scrollViewer._areAutoHideScrollBarsRevealed = false;
            scrollViewer.InitializeOverlayAutoHideIfNeeded();
        }
        else if (scrollViewer.IsScrollBarAutoHideEnabled)
        {
            scrollViewer.RestartScrollBarAutoHideTimer();
        }

        scrollViewer.InvalidateMeasure();
        scrollViewer.InvalidateArrange();
        scrollViewer.ApplyScrollBarAutoHideVisualState();
    }

    internal static double ComputeMouseWheelDelta(int wheelDelta, double lineStep, double pageStep)
    {
        double safeLineStep = double.IsFinite(lineStep) && lineStep > 0
            ? lineStep
            : 1.0;
        double safePageStep = double.IsFinite(pageStep) && pageStep > 0
            ? pageStep
            : safeLineStep;

        double notches = -wheelDelta / 120.0;
        if (Math.Abs(notches) <= double.Epsilon)
            return 0;

        var scrollLines = GetSystemWheelScrollLines();
        if (scrollLines == WHEEL_PAGESCROLL)
            return notches * safePageStep;

        return notches * scrollLines * safeLineStep;
    }

    private static uint GetSystemWheelScrollLines()
    {
        // user32 is only reachable on Windows; Linux/Android wheel events flow
        // through the same code path, so an unguarded P/Invoke here turned the
        // first wheel tick on Linux into a DllNotFoundException.
        if (OperatingSystem.IsLinux())
            return (uint)Math.Max(0, global::Jalium.UI.SystemParameters.WheelScrollLines);

        if (OperatingSystem.IsWindows() &&
            SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, out uint lines, 0))
        {
            return lines;
        }

        return 3;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);

    #endregion

    #region WpfParity

    private static readonly DependencyPropertyKey ComputedHorizontalScrollBarVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ComputedHorizontalScrollBarVisibility),
            typeof(Visibility),
            typeof(ScrollViewer),
            new PropertyMetadata(Visibility.Visible));

    private static readonly DependencyPropertyKey ComputedVerticalScrollBarVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ComputedVerticalScrollBarVisibility),
            typeof(Visibility),
            typeof(ScrollViewer),
            new PropertyMetadata(Visibility.Visible));

    private static readonly DependencyPropertyKey ContentHorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ContentHorizontalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ContentVerticalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ContentVerticalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ExtentHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ExtentHeight),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ExtentWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ExtentWidth),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey HorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HorizontalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ScrollableHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ScrollableHeight),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ScrollableWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ScrollableWidth),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey VerticalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(VerticalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ViewportHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ViewportHeight),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ViewportWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ViewportWidth),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the computed horizontal-scrollbar visibility property.
    /// </summary>
    public static readonly DependencyProperty ComputedHorizontalScrollBarVisibilityProperty =
        ComputedHorizontalScrollBarVisibilityPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the computed vertical-scrollbar visibility property.
    /// </summary>
    public static readonly DependencyProperty ComputedVerticalScrollBarVisibilityProperty =
        ComputedVerticalScrollBarVisibilityPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the content horizontal-offset property.
    /// </summary>
    public static readonly DependencyProperty ContentHorizontalOffsetProperty =
        ContentHorizontalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the content vertical-offset property.
    /// </summary>
    public static readonly DependencyProperty ContentVerticalOffsetProperty =
        ContentVerticalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the extent-height property.
    /// </summary>
    public static readonly DependencyProperty ExtentHeightProperty =
        ExtentHeightPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the extent-width property.
    /// </summary>
    public static readonly DependencyProperty ExtentWidthProperty =
        ExtentWidthPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the horizontal-offset property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        HorizontalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the scrollable-height property.
    /// </summary>
    public static readonly DependencyProperty ScrollableHeightProperty =
        ScrollableHeightPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the scrollable-width property.
    /// </summary>
    public static readonly DependencyProperty ScrollableWidthProperty =
        ScrollableWidthPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the vertical-offset property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        VerticalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the viewport-height property.
    /// </summary>
    public static readonly DependencyProperty ViewportHeightProperty =
        ViewportHeightPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the viewport-width property.
    /// </summary>
    public static readonly DependencyProperty ViewportWidthProperty =
        ViewportWidthPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets the current computed horizontal-scrollbar visibility.
    /// </summary>
    public Visibility ComputedHorizontalScrollBarVisibility =>
        (Visibility)GetValue(ComputedHorizontalScrollBarVisibilityProperty)!;

    /// <summary>
    /// Gets the current computed vertical-scrollbar visibility.
    /// </summary>
    public Visibility ComputedVerticalScrollBarVisibility =>
        (Visibility)GetValue(ComputedVerticalScrollBarVisibilityProperty)!;

    /// <summary>
    /// Gets the horizontal position where content is visually located. During
    /// deferred thumb tracking this remains at the committed content position.
    /// </summary>
    public double ContentHorizontalOffset =>
        (double)GetValue(ContentHorizontalOffsetProperty)!;

    /// <summary>
    /// Gets the vertical position where content is visually located. During
    /// deferred thumb tracking this remains at the committed content position.
    /// </summary>
    public double ContentVerticalOffset =>
        (double)GetValue(ContentVerticalOffsetProperty)!;

    /// <inheritdoc />
    protected internal override bool HandlesScrolling => true;

    /// <summary>
    /// Gets or sets the scrolling provider used for extent, viewport, offset,
    /// and line/page commands.
    /// </summary>
    protected internal IScrollInfo? ScrollInfo
    {
        get => _scrollInfo;
        set
        {
            if (ReferenceEquals(_scrollInfo, value))
            {
                return;
            }

            if (_scrollInfo?.ScrollOwner == this)
            {
                _scrollInfo.ScrollOwner = null;
            }

            _scrollInfo = value;
            if (_scrollInfo != null)
            {
                _scrollInfo.ScrollOwner = this;
                _scrollInfo.CanHorizontallyScroll =
                    HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
                _scrollInfo.CanVerticallyScroll =
                    VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            }

            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    /// <summary>
    /// Gets the attached CanContentScroll value.
    /// </summary>
    public static bool GetCanContentScroll(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(CanContentScrollProperty)!;
    }

    /// <summary>
    /// Sets the attached CanContentScroll value.
    /// </summary>
    public static void SetCanContentScroll(DependencyObject element, bool canContentScroll)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CanContentScrollProperty, canContentScroll);
    }

    /// <summary>
    /// Gets the attached horizontal-scrollbar visibility value.
    /// </summary>
    public static ScrollBarVisibility GetHorizontalScrollBarVisibility(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ScrollBarVisibility)element.GetValue(HorizontalScrollBarVisibilityProperty)!;
    }

    /// <summary>
    /// Sets the attached horizontal-scrollbar visibility value.
    /// </summary>
    public static void SetHorizontalScrollBarVisibility(
        DependencyObject element,
        ScrollBarVisibility horizontalScrollBarVisibility)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HorizontalScrollBarVisibilityProperty, horizontalScrollBarVisibility);
    }

    /// <summary>
    /// Gets the attached deferred-scrolling value.
    /// </summary>
    public static bool GetIsDeferredScrollingEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsDeferredScrollingEnabledProperty)!;
    }

    /// <summary>
    /// Sets the attached deferred-scrolling value.
    /// </summary>
    public static void SetIsDeferredScrollingEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsDeferredScrollingEnabledProperty, value);
    }

    /// <summary>
    /// Gets the attached panning-deceleration value.
    /// </summary>
    public static double GetPanningDeceleration(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(PanningDecelerationProperty)!;
    }

    /// <summary>
    /// Sets the attached panning-deceleration value.
    /// </summary>
    public static void SetPanningDeceleration(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningDecelerationProperty, value);
    }

    /// <summary>
    /// Gets the attached panning-mode value.
    /// </summary>
    public static PanningMode GetPanningMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (PanningMode)element.GetValue(PanningModeProperty)!;
    }

    /// <summary>
    /// Sets the attached panning-mode value.
    /// </summary>
    public static void SetPanningMode(DependencyObject element, PanningMode panningMode)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningModeProperty, panningMode);
    }

    /// <summary>
    /// Gets the attached panning-ratio value.
    /// </summary>
    public static double GetPanningRatio(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(PanningRatioProperty)!;
    }

    /// <summary>
    /// Sets the attached panning-ratio value.
    /// </summary>
    public static void SetPanningRatio(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningRatioProperty, value);
    }

    /// <summary>
    /// Gets the attached vertical-scrollbar visibility value.
    /// </summary>
    public static ScrollBarVisibility GetVerticalScrollBarVisibility(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ScrollBarVisibility)element.GetValue(VerticalScrollBarVisibilityProperty)!;
    }

    /// <summary>
    /// Sets the attached vertical-scrollbar visibility value.
    /// </summary>
    public static void SetVerticalScrollBarVisibility(
        DependencyObject element,
        ScrollBarVisibility verticalScrollBarVisibility)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VerticalScrollBarVisibilityProperty, verticalScrollBarVisibility);
    }

    /// <summary>
    /// Raises the routed <see cref="ScrollChanged"/> event. Overrides should call
    /// the base implementation to preserve routed-event delivery.
    /// </summary>
    protected virtual void OnScrollChanged(ScrollChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    private void UpdateRequestedOffsetsFromContent()
    {
        _requestedHorizontalOffset = _horizontalOffset;
        _requestedVerticalOffset = _verticalOffset;
        UpdateScrollMetricDependencyProperties();
    }

    private void UpdateDeferredRequestedOffset(bool isVertical, double value)
    {
        if (isVertical)
        {
            _requestedVerticalOffset = value;
        }
        else
        {
            _requestedHorizontalOffset = value;
        }

        UpdateScrollMetricDependencyProperties();
    }

    private void UpdateScrollMetricDependencyProperties()
    {
        SetValue(HorizontalOffsetPropertyKey, _requestedHorizontalOffset);
        SetValue(VerticalOffsetPropertyKey, _requestedVerticalOffset);
        SetValue(ContentHorizontalOffsetPropertyKey, _horizontalOffset);
        SetValue(ContentVerticalOffsetPropertyKey, _verticalOffset);
        SetValue(ExtentWidthPropertyKey, Math.Max(0.0, _extentWidth));
        SetValue(ExtentHeightPropertyKey, Math.Max(0.0, _extentHeight));
        SetValue(ViewportWidthPropertyKey, Math.Max(0.0, _viewportWidth));
        SetValue(ViewportHeightPropertyKey, Math.Max(0.0, _viewportHeight));
        SetValue(ScrollableWidthPropertyKey, Math.Max(0.0, _extentWidth - _viewportWidth));
        SetValue(ScrollableHeightPropertyKey, Math.Max(0.0, _extentHeight - _viewportHeight));
        SetValue(
            ComputedHorizontalScrollBarVisibilityPropertyKey,
            GetComputedScrollBarVisibility(HorizontalScrollBarVisibility, CanScrollHorizontally));
        SetValue(
            ComputedVerticalScrollBarVisibilityPropertyKey,
            GetComputedScrollBarVisibility(VerticalScrollBarVisibility, CanScrollVertically));
    }

    private static Visibility GetComputedScrollBarVisibility(
        ScrollBarVisibility visibility,
        bool canScroll)
    {
        return visibility switch
        {
            ScrollBarVisibility.Visible => Visibility.Visible,
            ScrollBarVisibility.Auto when canScroll => Visibility.Visible,
            _ => Visibility.Collapsed,
        };
    }

    private static bool IsValidScrollBarVisibility(object? value) =>
        value is ScrollBarVisibility visibility &&
        visibility is >= ScrollBarVisibility.Disabled and <= ScrollBarVisibility.Visible;

    private static bool IsFiniteNonNegative(object? value) =>
        value is double number && !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0.0;

    private static double ValidateScrollOffset(double offset, string parameterName)
    {
        if (double.IsNaN(offset))
        {
            throw new ArgumentOutOfRangeException(parameterName, offset, $"'{parameterName}' parameter value cannot be NaN.");
        }

        return offset;
    }

    #endregion
}


/// <summary>
/// Provides data for the ScrollChanged event.
/// </summary>
public class ScrollChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the horizontal offset change.
    /// </summary>
    public double HorizontalChange { get; }

    /// <summary>
    /// Gets the vertical offset change.
    /// </summary>
    public double VerticalChange { get; }

    /// <summary>
    /// Gets the current horizontal offset.
    /// </summary>
    public double HorizontalOffset { get; }

    /// <summary>
    /// Gets the current vertical offset.
    /// </summary>
    public double VerticalOffset { get; }

    /// <summary>
    /// Gets the viewport width.
    /// </summary>
    public double ViewportWidth { get; }

    /// <summary>
    /// Gets the viewport height.
    /// </summary>
    public double ViewportHeight { get; }

    /// <summary>
    /// Gets the extent width.
    /// </summary>
    public double ExtentWidth { get; }

    /// <summary>
    /// Gets the extent height.
    /// </summary>
    public double ExtentHeight { get; }

    /// <summary>Gets the change in extent width since the previous event.</summary>
    public double ExtentWidthChange { get; }

    /// <summary>Gets the change in extent height since the previous event.</summary>
    public double ExtentHeightChange { get; }

    /// <summary>Gets the change in viewport width since the previous event.</summary>
    public double ViewportWidthChange { get; }

    /// <summary>Gets the change in viewport height since the previous event.</summary>
    public double ViewportHeightChange { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollChangedEventArgs"/> class.
    /// </summary>
    internal ScrollChangedEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
    }

    internal ScrollChangedEventArgs(
        RoutedEvent routedEvent,
        object source,
        double horizontalChange,
        double verticalChange,
        double horizontalOffset,
        double verticalOffset,
        double viewportWidth,
        double viewportHeight,
        double extentWidth,
        double extentHeight,
        double extentWidthChange,
        double extentHeightChange,
        double viewportWidthChange,
        double viewportHeightChange)
        : base(routedEvent, source)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        ExtentWidth = extentWidth;
        ExtentHeight = extentHeight;
        ExtentWidthChange = extentWidthChange;
        ExtentHeightChange = extentHeightChange;
        ViewportWidthChange = viewportWidthChange;
        ViewportHeightChange = viewportHeightChange;
    }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ScrollChangedEventHandler scrollChangedHandler)
        {
            scrollChangedHandler(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// Delegate for handling ScrollChanged events.
/// </summary>
public delegate void ScrollChangedEventHandler(object sender, ScrollChangedEventArgs e);

/// <summary>
/// Specifies how touch panning works in a ScrollViewer.
/// </summary>
public enum PanningMode
{
    /// <summary>
    /// Panning is disabled.
    /// </summary>
    None,

    /// <summary>
    /// Horizontal panning only.
    /// </summary>
    HorizontalOnly,

    /// <summary>
    /// Vertical panning only.
    /// </summary>
    VerticalOnly,

    /// <summary>
    /// Both horizontal and vertical panning.
    /// </summary>
    Both,

    /// <summary>
    /// First determines the panning direction from the initial touch.
    /// </summary>
    HorizontalFirst,

    /// <summary>
    /// First determines the panning direction from the initial touch.
    /// </summary>
    VerticalFirst
}

/// <summary>
/// Specifies the visibility of a scroll bar.
/// </summary>
public enum ScrollBarVisibility
{
    /// <summary>
    /// The scroll bar is disabled and not visible.
    /// </summary>
    Disabled,

    /// <summary>
    /// The scroll bar appears only when needed.
    /// </summary>
    Auto,

    /// <summary>
    /// The scroll bar is never visible.
    /// </summary>
    Hidden,

    /// <summary>
    /// The scroll bar is always visible.
    /// </summary>
    Visible
}
