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
    private static readonly bool s_isScrollBarAutoHideEnabledByDefault =
        DetermineDefaultScrollBarAutoHide(
            Environment.GetEnvironmentVariable(ScrollBarAutoHideEnvironmentVariable));

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
    // Last committed Auto scroll-bar decisions. Auto visibility is a threshold on a
    // continuous quantity, so a page whose content is exactly viewport-sized sits on
    // the knife edge and can flip every frame while a window is being resized. Remembering
    // the previous answer turns the single threshold into a dead band and keeps the
    // scroll-bar visual state stable.
    private bool _verticalScrollBarShown;
    private bool _horizontalScrollBarShown;
    // Set once layout has settled an Auto decision from real content. Gates every
    // non-layout path down to re-asserting that decision instead of re-deriving it.
    private bool _hasCommittedScrollBarVisibility;
    private double _viewportWidth;
    private double _viewportHeight;

    // The viewport the last completed Arrange actually used. Measure publishes the new
    // viewport into the fields above as soon as it knows it, so by the time Arrange runs
    // they already describe this pass — comparing them against themselves there detects
    // nothing. These keep the previous pass's value so the comparison has something to
    // be about. NaN until the first arrange, which correctly reads as "changed".
    private double _lastArrangedViewportWidth = double.NaN;
    private double _lastArrangedViewportHeight = double.NaN;
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
    /// Identifies the FillViewport attached property. Set it on the CONTENT of a
    /// <see cref="ScrollViewer"/>.
    /// </summary>
    /// <remarks>
    /// A scrollable axis is normally measured with an unbounded constraint, because
    /// the whole point of scrolling is to let content be as big as it wants. Content
    /// that instead wants to be exactly as tall as the viewport and scroll internally
    /// — a page with its own list and detail panes — has no way to express that: it
    /// only learns the viewport size after being arranged, so anything it derives
    /// from it describes the PREVIOUS layout pass. Sizing itself from that stale
    /// value leaves it smaller than the box it is arranged into, and a Stretch
    /// alignment that cannot stretch centres it, which looks like the whole page
    /// drifting down the screen while a window is resized.
    /// <para>
    /// Marking an axis here makes the viewer measure that axis with the real viewport
    /// instead, so the content is handed its size as a constraint, in the same pass,
    /// with nothing to read back and nothing to lag. That axis then never overflows,
    /// so the viewer shows no scroll bar for it — the content is expected to scroll
    /// itself.
    /// </para>
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty FillViewportProperty =
        DependencyProperty.RegisterAttached(
            "FillViewport",
            typeof(FillViewportMode),
            typeof(ScrollViewer),
            new PropertyMetadata(FillViewportMode.None, OnFillViewportChanged));

    private static void OnFillViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // The value changes what constraint the content is measured with, so the
        // viewer that owns it has to measure again.
        if (d is UIElement element)
        {
            element.InvalidateMeasure();
            (element.VisualParent as UIElement)?.InvalidateMeasure();
        }
    }

    /// <summary>Gets the <see cref="FillViewportProperty"/> value.</summary>
    public static FillViewportMode GetFillViewport(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FillViewportMode)element.GetValue(FillViewportProperty)!;
    }

    /// <summary>Sets the <see cref="FillViewportProperty"/> value.</summary>
    public static void SetFillViewport(DependencyObject element, FillViewportMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FillViewportProperty, value);
    }

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
    /// Overlay scroll bars render as compact edge indicators. Scroll bars are laid
    /// out over the content on every platform, so this property changes their visual
    /// presentation rather than the size of the content viewport. The default is
    /// enabled on mobile operating systems.
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
    /// Gets or sets whether scroll bars use the compact mobile edge-indicator style.
    /// Scroll bars always overlay the content and do not reduce the content viewport.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOverlayScrollBarEnabled
    {
        get => (bool)GetValue(IsOverlayScrollBarEnabledProperty)!;
        set => SetValue(IsOverlayScrollBarEnabledProperty, value);
    }

    internal static bool DetermineDefaultScrollBarAutoHide(string? environmentValue)
    {
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

        // Auto-hide is the documented default for every host. Applications that
        // need persistent scroll bars can opt out per viewer, or process-wide via
        // JALIUM_SCROLLBAR_AUTOHIDE=0. Inferring policy from a process/command name
        // made the same control behave differently inside Gallery applications.
        return true;
    }

    /// <inheritdoc />
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        // New content gets a fresh Auto decision — carrying the previous page's
        // dead-band state over would stick a scroll bar on (or off) for content
        // with completely different dimensions.
        _verticalScrollBarShown = false;
        _horizontalScrollBarShown = false;
        _hasCommittedScrollBarVisibility = false;
        ClearEndAnchor(isVertical: true);
        ClearEndAnchor(isVertical: false);
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
    /// <summary>
    /// Tolerance for "does the content overflow the viewport" comparisons. Layout
    /// sizes are continuous doubles, so a page sized to exactly fill its viewport
    /// would otherwise toggle its Auto scroll bar on sub-pixel noise.
    /// </summary>
    private const double LayoutEpsilon = 0.5;
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
    private bool _smoothVerticalEndAnchorPending;
    private bool _smoothHorizontalEndAnchorPending;
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
    private bool _deferredScrollIsVertical;
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
    private bool _pendingDragVerticalEndAnchor;
    private bool _pendingDragHorizontalEndAnchor;
    private double _pendingDragVerticalOffset;
    private double _pendingDragHorizontalOffset;

    // A virtualizing IScrollInfo can refine its extent after the thumb has already reached the
    // maximum that was frozen at drag start. Keep that endpoint intent inside ScrollViewer and
    // re-apply only finite live maxima until layout settles. Passing a non-finite sentinel through
    // arbitrary IScrollInfo proxies is unsafe: some valid implementations coerce it to zero.
    private bool _verticalEndAnchorActive;
    private bool _horizontalEndAnchorActive;
    private double _verticalEndAnchorMaximum = double.NaN;
    private double _horizontalEndAnchorMaximum = double.NaN;
    private bool _isApplyingEndAnchor;

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

    // Committed offsets are quantized to whole device pixels, but a slow pan can move
    // less than half a pixel per frame. Delta-based panning would lose each such
    // increment to the rounding and the finger would appear stuck; these carry the
    // quantization remainder into the next packet so slow pans keep integrating.
    private double _pointerPanningResidualX;
    private double _pointerPanningResidualY;
    private bool _pointerPanningYieldedToAncestor;
    private bool _isCompletingLifecycleCleanup;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollViewer"/> class.
    /// </summary>
    public ScrollViewer()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
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
        AddHandler(MouseEnterEvent, new Input.MouseEventHandler(OnAutoHideRegionMouseEnter));
        AddHandler(MouseLeaveEvent, new Input.MouseEventHandler(OnAutoHideRegionMouseLeave));
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

        // A presentation host can unload a subtree without first changing this element's
        // immediate visual parent. Pair this with OnVisualParentChanged so every lifecycle exit
        // retires frame-paced timers and transient input state.
        Unloaded += OnScrollViewerUnloaded;
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (oldParent != null && VisualParent == null)
        {
            CompleteLifecycleCleanup();
        }
    }

    private void OnScrollViewerUnloaded(object sender, RoutedEventArgs e)
    {
        CompleteLifecycleCleanup();
    }

    private void CompleteLifecycleCleanup()
    {
        if (_isCompletingLifecycleCleanup)
            return;

        _isCompletingLifecycleCleanup = true;
        try
        {
            // Stop the global frame subscriptions first, while preserving their pending state.
            // Capture-loss callbacks below can still commit a final thumb value and may restart
            // an idle timer, so the finally block stops all five timers once more.
            StopLifecycleTimers();
            ReleaseLifecycleInputCaptures();

            CompleteDeferredScrollForLifecycle();
            FlushPendingDragScroll();
            StopDragScrollCoalesce();

            _pointerPanningCoalesceTimer?.Stop();
            CompletePendingPointerPanningDelta();
            ResetPointerPanningState();

            CompleteSmoothScrollForLifecycle();
        }
        finally
        {
            ResetLifecycleTransitionState();
            StopLifecycleTimers();
            _isCompletingLifecycleCleanup = false;
        }
    }

    private void StopLifecycleTimers()
    {
        _smoothScrollTimer?.Stop();
        _scrollBarAutoHideTimer?.Stop();
        _dragScrollCoalesceTimer?.Stop();
        _pointerPanningCoalesceTimer?.Stop();
        _bounceTimer?.Stop();
        _verticalScrollBar?.CompleteInteractionForDetach();
        _horizontalScrollBar?.CompleteInteractionForDetach();
    }

    private void ReleaseLifecycleInputCaptures()
    {
        var mouseCapture = UIElement.MouseCapturedElement;
        if (mouseCapture != null && IsVisualDescendantOf(mouseCapture, this))
        {
            mouseCapture.ReleaseMouseCapture();
        }

        // Touch capture is per contact. Snapshot before releasing because each capture-loss
        // event mutates the global capture table and can finish a Thumb drag synchronously.
        foreach (var touchDevice in TouchesCapturedWithin.ToArray())
        {
            touchDevice.Capture(null);
        }

        if (Stylus.Captured is UIElement stylusCapture &&
            IsVisualDescendantOf(stylusCapture, this))
        {
            Stylus.Capture(null);
        }
    }

    private void CompleteDeferredScrollForLifecycle()
    {
        if (!_isDeferredScrolling)
            return;

        var isVertical = _deferredScrollIsVertical;
        var deferredValue = isVertical ? _deferredVerticalOffset : _deferredHorizontalOffset;
        var interactionMaximum = isVertical ? _verticalScrollBar.Maximum : _horizontalScrollBar.Maximum;
        _isDeferredScrolling = false;

        if (IsScrollBarRangeEnd(deferredValue, interactionMaximum))
        {
            ApplyEndAnchorOffset(isVertical);
        }
        else
        {
            ApplyScrollOffset(isVertical, deferredValue);
        }
    }

    private void CompleteSmoothScrollForLifecycle()
    {
        if (!_isSmoothScrolling)
            return;

        var targetY = _smoothVerticalEndAnchorPending
            ? ScrollableHeight
            : Math.Clamp(_smoothTargetY, 0, ScrollableHeight);
        var targetX = _smoothHorizontalEndAnchorPending
            ? ScrollableWidth
            : Math.Clamp(_smoothTargetX, 0, ScrollableWidth);
        var anchorVerticalEnd = _smoothVerticalEndAnchorPending;
        var anchorHorizontalEnd = _smoothHorizontalEndAnchorPending;

        _isApplyingSmoothScrollStep = true;
        try
        {
            ScrollToVerticalOffset(targetY);
            ScrollToHorizontalOffset(targetX);
        }
        finally
        {
            _isApplyingSmoothScrollStep = false;
        }

        anchorVerticalEnd &= IsAtScrollEnd(isVertical: true);
        anchorHorizontalEnd &= IsAtScrollEnd(isVertical: false);
        _smoothScrollTimer?.Stop();
        _isSmoothScrolling = false;
        _smoothVerticalEndAnchorPending = false;
        _smoothHorizontalEndAnchorPending = false;

        if (anchorVerticalEnd)
            ApplyEndAnchorOffset(isVertical: true);
        if (anchorHorizontalEnd)
            ApplyEndAnchorOffset(isVertical: false);
    }

    private void ResetLifecycleTransitionState()
    {
        _isSmoothScrolling = false;
        _isApplyingSmoothScrollStep = false;
        _smoothVerticalEndAnchorPending = false;
        _smoothHorizontalEndAnchorPending = false;
        _lastSmoothTickTime = 0;
        _smoothTargetX = _horizontalOffset;
        _smoothTargetY = _verticalOffset;

        _scrollBarAutoHideDeadlineTick = 0;
        _areAutoHideScrollBarsRevealed = false;
        _hasInitializedOverlayAutoHide = false;

        _isDeferredScrolling = false;
        _deferredScrollIsVertical = false;
        _deferredVerticalOffset = _verticalOffset;
        _deferredHorizontalOffset = _horizontalOffset;

        StopDragScrollCoalesce();
        _pendingDragVerticalOffset = _verticalOffset;
        _pendingDragHorizontalOffset = _horizontalOffset;
        _isDraggingVerticalThumb = false;
        _dragStartMouseY = 0;
        _dragStartVerticalOffset = _verticalOffset;

        ResetPointerPanningState();

        _bounceStartTicks = 0;
        _bounceFromX = 0;
        _bounceFromY = 0;
        _overscrollX = 0;
        _overscrollY = 0;

        _isApplyingEndAnchor = false;
        ClearEndAnchor(isVertical: true);
        ClearEndAnchor(isVertical: false);
    }

    private ScrollBar CreateScrollBar(Orientation orientation)
    {
        var scrollBar = new ScrollBar
        {
            // This viewer creates, owns and positions this bar: ArrangeOverride puts
            // it at a strip derived purely from the arranged size, never from the
            // bar's own DesiredSize. Showing, hiding or
            // animating it therefore cannot change anything outside this viewer, so
            // its layout invalidations must not travel up the tree. A ScrollBar that
            // came from a template is an ordinary child and is deliberately left
            // un-isolated.
            IsHostOwnedLayout = true,
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
        ClearEndAnchor(isVertical: true);
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
        if (newOffset >= ScrollableHeight - 0.01)
        {
            ApplyEndAnchorOffset(isVertical: true);
        }
        else
        {
            ScrollToVerticalOffset(newOffset);
        }
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

    private void OnAutoHideRegionMouseEnter(object sender, Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, this))
            return;

        // Entering content reveals only the collapsed/slim presentation. The
        // ScrollBar's own MouseEnter handler is the exclusive expansion trigger.
        ApplyScrollBarAutoHideVisualState();
    }

    private void OnScrollBarMouseEnter(object sender, Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _verticalScrollBar) && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        // A direct ScrollBar MouseEnter means hit testing selected the bar rather
        // than the content behind/beside it. Content hover alone must not expand.
        RevealAutoHideScrollBarsTemporarily();
    }

    private void OnScrollBarMouseLeave(object sender, Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _verticalScrollBar) && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        // Moving from the bar into the content remains inside the overall viewer,
        // but it resumes the normal idle countdown instead of staying expanded.
        RestartScrollBarAutoHideTimer();
    }

    private void OnAutoHideRegionMouseLeave(object sender, Input.MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, this))
            return;

        // The complete ScrollViewer is the outside boundary. Leaving it bypasses the
        // idle delay and starts fading.
        HideAutoHideScrollBarsIfEligible();
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
        _pointerPanningResidualX = 0;
        _pointerPanningResidualY = 0;
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
        Jalium.UI.Diagnostics.ScrollDiagnostics.RecordOverscroll(
            Name, "bounce-start", _overscrollX, _overscrollY);
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
            Jalium.UI.Diagnostics.ScrollDiagnostics.RecordOverscroll(Name, "bounce-end", 0, 0);
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
            double effectiveDelta = horizontalDelta + _pointerPanningResidualX;
            double newOffset = Math.Clamp(_horizontalOffset + effectiveDelta, 0, ScrollableWidth);
            double consumed = newOffset - _horizontalOffset;
            if (!AreClose(newOffset, _horizontalOffset))
            {
                ScrollToHorizontalOffset(newOffset);
                moved = true;
            }
            // Whatever the quantized commit did not take stays in the residual so a
            // slow pan integrates across packets instead of stalling below the
            // half-pixel rounding threshold.
            _pointerPanningResidualX = newOffset - _horizontalOffset;
            // Any movement not absorbed by the scroll offset feeds the rubber-
            // band overscroll. We negate here because horizontalDelta is the
            // scroll-offset delta (opposite the finger direction); overscroll
            // is rendered as a content translation that follows the finger.
            double remaining = effectiveDelta - consumed;
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
            double effectiveDelta = verticalDelta + _pointerPanningResidualY;
            double newOffset = Math.Clamp(_verticalOffset + effectiveDelta, 0, ScrollableHeight);
            double consumed = newOffset - _verticalOffset;
            if (!AreClose(newOffset, _verticalOffset))
            {
                ScrollToVerticalOffset(newOffset);
                moved = true;
            }
            _pointerPanningResidualY = newOffset - _verticalOffset;
            double remaining = effectiveDelta - consumed;
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
        _pointerPanningResidualX = 0;
        _pointerPanningResidualY = 0;
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
            scrollBar.StartAutoHideVisibilityTransition(0.0);
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
            scrollBar.StartAutoHideVisibilityTransition(1.0);
            return;
        }

        if (scrollBar.Visibility != Visibility.Visible)
        {
            scrollBar.Visibility = Visibility.Visible;
        }

        bool keepExpanded =
            _areAutoHideScrollBarsRevealed ||
            ShouldKeepAutoHideScrollBarVisible(scrollBar);
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

        // Shape and visibility are independent on desktop:
        // outside = hidden, content = visible/slim, scrollbar = visible/expanded.
        // Overlay indicators retain their existing scroll-driven visibility model.
        var targetVisibility = scrollBar.IsOverlayStyle || IsMouseOver ? 1.0 : 0.0;
        scrollBar.StartAutoHideVisibilityTransition(targetVisibility);
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
    /// Quantizes a committed scroll offset to whole physical pixels. A scroll offset
    /// translates the entire content subtree, and the renderer pixel-snaps text runs
    /// and axis-aligned strokes in DEVICE space: under a fractional translation each
    /// primitive crosses its integer pixel boundary on a different frame, so during a
    /// smooth scroll neighbouring text and paths visibly jitter against each other by
    /// 1px (some appear to hold still while others move). Whole-device-pixel offsets
    /// keep every primitive's sub-pixel phase constant, so snapped and unsnapped
    /// content translate in lockstep — the same policy browsers and WPF's pixel-based
    /// scrolling apply. Element animations are unaffected: this quantizes only the
    /// scroll translation, not arranged element origins.
    ///
    /// The extremes stay exact: 0 and <paramref name="maxOffset"/> are resting
    /// positions (no motion, so no jitter), and rounding the bottom stop would
    /// otherwise leave a sub-pixel gap that keeps IsAtVerticalEnd false forever.
    /// </summary>
    internal static double SnapScrollOffsetToDevicePixels(double offset, double maxOffset, double dpiScale)
    {
        if (!double.IsFinite(offset))
        {
            return offset;
        }

        if (offset <= 0)
        {
            return 0;
        }

        // Only the KNOWN end stop is preserved exactly. With maxOffset still 0 (metrics
        // not yet synced from the IScrollInfo, or genuinely unscrollable) the offset is
        // handed onward rounded-but-unclamped so the existing clamp sites keep owning
        // the range decision, exactly as they did before quantization.
        if (maxOffset > 0 && double.IsFinite(maxOffset) && offset >= maxOffset)
        {
            return maxOffset;
        }

        if (!(dpiScale > 0.0) || !double.IsFinite(dpiScale))
        {
            return offset;
        }

        return Math.Round(offset * dpiScale, MidpointRounding.AwayFromZero) / dpiScale;
    }

    /// <summary>
    /// Scrolls to the specified horizontal offset.
    /// </summary>
    /// <param name="offset">The horizontal offset.</param>
    public void ScrollToHorizontalOffset(double offset)
    {
        offset = ValidateScrollOffset(offset, nameof(offset));
        if (!_isApplyingEndAnchor)
        {
            ClearEndAnchor(isVertical: false);
        }
        offset = SnapScrollOffsetToDevicePixels(offset, ScrollableWidth, FrameworkElement.LayoutDpiScale);

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
            if (!TryTranslatePhysicalContent())
            {
                InvalidateArrange();
            }
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
        var rawOffset = offset;
        offset = ValidateScrollOffset(offset, nameof(offset));
        if (!_isApplyingEndAnchor)
        {
            ClearEndAnchor(isVertical: true);
        }
        offset = SnapScrollOffsetToDevicePixels(offset, ScrollableHeight, FrameworkElement.LayoutDpiScale);
        TraceVerticalState(
            "set-vertical-request",
            $"raw={rawOffset:R} snapped={offset:R} applyingEndAnchor={_isApplyingEndAnchor} applyingSmooth={_isApplyingSmoothScrollStep}");

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
            TraceVerticalState(
                "set-vertical-provider-commit",
                $"raw={rawOffset:R} sent={offset:R} old={oldOffset:R}");

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
        TraceVerticalState(
            "set-vertical-physical-commit",
            $"raw={rawOffset:R} sent={offset:R} old={oldOff:R}");

        if (oldOff != _verticalOffset)
        {
            RevealOverlayIndicatorForScrollMovement();
            if (!TryTranslatePhysicalContent())
            {
                InvalidateArrange();
            }
            RaiseScrollChanged();
            UpdateScrollBarMetrics();
        }
    }

    /// <summary>
    /// Applies a non-<see cref="IScrollInfo"/> offset as a pure translation when
    /// the viewer and its content already have valid geometry.
    /// </summary>
    /// <remarks>
    /// A smooth wheel animation can sample at the monitor refresh rate. Invalidating
    /// this entire <see cref="ScrollViewer"/> for every sample needlessly sends the
    /// page through <see cref="ArrangeOverride"/> even though only the content origin
    /// changed. <see cref="UIElement.Arrange(Rect)"/> already has a retained-cache
    /// translation fast path, so drive that path directly and leave the viewer's
    /// otherwise unchanged layout valid. A pending resize, remeasure, rounded layout,
    /// or changed content size falls back to the normal layout-manager path.
    /// </remarks>
    private bool TryTranslatePhysicalContent()
    {
        if (_scrollInfo != null ||
            ContentElement is not FrameworkElement content ||
            !IsMeasureValid ||
            !IsArrangeValid ||
            !content.IsMeasureValid ||
            !content.IsArrangeValid ||
            content.UseLayoutRounding)
        {
            return false;
        }

        var contentWidth = HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
            ? _viewportWidth
            : Math.Max(_extentWidth, _viewportWidth);
        var contentHeight = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
            ? _viewportHeight
            : Math.Max(_extentHeight, _viewportHeight);
        var targetRect = new Rect(
            -_horizontalOffset + _overscrollX,
            -_verticalOffset + _overscrollY,
            contentWidth,
            contentHeight);
        var previousRect = content.PreviousFinalRect;

        // Calling Arrange with a changed size would legitimately recurse through
        // the page. Let the regular ScrollViewer layout path own that case.
        if (previousRect.Width != targetRect.Width ||
            previousRect.Height != targetRect.Height)
        {
            return false;
        }

        content.Arrange(targetRect);
        return content.IsMeasureValid && content.IsArrangeValid;
    }

    /// <summary>
    /// Scrolls up by one line.
    /// </summary>
    public void LineUp()
    {
        ClearEndAnchor(isVertical: true);
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
        ClearEndAnchor(isVertical: true);
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
        ClearEndAnchor(isVertical: false);
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
        ClearEndAnchor(isVertical: false);
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
        ClearEndAnchor(isVertical: true);
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
        ClearEndAnchor(isVertical: true);
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
        ClearEndAnchor(isVertical: false);
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
        ClearEndAnchor(isVertical: false);
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
        MaintainEndAnchors(allowCompletion: false);
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
        ClearEndAnchor(isVertical: false);
        ApplyEndAnchorOffset(isVertical: false);
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
        ClearEndAnchor(isVertical: true);
        ApplyEndAnchorOffset(isVertical: true);
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

        var finiteContentAvailable = new Size(
            Math.Max(0, availableSize.Width),
            Math.Max(0, availableSize.Height));

        // Match WPF's non-IScrollInfo contract: a scrollable axis is measured with
        // an infinite constraint from the first pass. A finite probe followed by an
        // overflow correction changes the child's constraint whenever content swaps
        // between short and tall pages. That defeats retained layout caches and can
        // synchronously remeasure a large page twice during navigation.
        //
        // FillViewport opts an axis out of that: the content asked to be sized to the
        // viewport, so hand it the viewport as a constraint. That is the whole point
        // — it gets its size in this pass instead of reading back a viewport that
        // describes the previous one.
        var fillMode = GetFillViewport(ContentElement);
        var fillsHorizontally = fillMode is FillViewportMode.Horizontal or FillViewportMode.Both;
        var fillsVertically = fillMode is FillViewportMode.Vertical or FillViewportMode.Both;

        var contentMeasureAvailable = _scrollInfo == null
            ? new Size(
                HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled || fillsHorizontally
                    ? finiteContentAvailable.Width
                    : double.PositiveInfinity,
                VerticalScrollBarVisibility == ScrollBarVisibility.Disabled || fillsVertically
                    ? finiteContentAvailable.Height
                    : double.PositiveInfinity)
            : finiteContentAvailable;

        ContentElement.Measure(contentMeasureAvailable);
        var contentDesired = ContentElement.DesiredSize;

        // Update extent from IScrollInfo or from content desired size
        if (_scrollInfo != null)
        {
            SyncExtentFromScrollInfo();
        }
        else
        {
            _extentWidth = contentDesired.Width;
            _extentHeight = contentDesired.Height;
        }

        // Publish the viewport now rather than waiting for arrange. Scroll bars are
        // overlays, so the viewport is the full available size and does not change
        // when a bar appears or disappears. Content that sizes itself from
        // ViewportWidth/Height — a page that wants to fill the shell and scroll
        // internally — would otherwise read last pass's numbers and settle on a size
        // meant for the previous viewport; being smaller than the box it is arranged
        // into, it then gets centred, which reads as the whole page sitting too low.
        // An unbounded constraint carries no viewport information, so keep the last
        // real one in that case.
        if (double.IsFinite(availableSize.Width))
        {
            _viewportWidth = Math.Max(0, availableSize.Width);
        }

        if (double.IsFinite(availableSize.Height))
        {
            _viewportHeight = Math.Max(0, availableSize.Height);
        }

        MaintainEndAnchors(allowCompletion: false);

        // Settle the Auto decision here, from this pass's own numbers: the extent was
        // just produced above and the viewport is the full availableSize because the
        // scroll bars overlay it. Deciding in Arrange instead compared an
        // extent from one pass against a size from a later one, which is what made a
        // shrinking window read as "overflowing". Committing before the bars are
        // measured below also means a bar that just turned visible gets measured in
        // this same pass.
        CommitScrollBarVisibility(
            ResolveScrollBarShown(
                VerticalScrollBarVisibility, _verticalScrollBarShown,
                _extentHeight, finiteContentAvailable.Height),
            ResolveScrollBarShown(
                HorizontalScrollBarVisibility, _horizontalScrollBarShown,
                _extentWidth, finiteContentAvailable.Width));

        // Scroll bars overlay the content, so they never contribute to desired size.
        var resultWidth = Math.Min(contentDesired.Width, availableSize.Width);
        var resultHeight = Math.Min(contentDesired.Height, availableSize.Height);

        var scrollBarLayoutSize = IsOverlayScrollBarEnabled
            ? OverlayScrollBarLayoutSize
            : ScrollBarSize;
        _verticalScrollBar.Measure(new Size(scrollBarLayoutSize, Math.Max(0, availableSize.Height)));
        _horizontalScrollBar.Measure(new Size(Math.Max(0, availableSize.Width), scrollBarLayoutSize));

        return new Size(resultWidth, resultHeight);
    }

    /// <summary>
    /// Resolves an Auto scroll bar with hysteresis: showing needs the content to
    /// clear the viewport by <see cref="LayoutEpsilon"/>, hiding needs it to fall
    /// back under by the same margin, and anything in between keeps the previous
    /// answer. Without the dead band, content that exactly fills its viewport
    /// toggles the bar on every layout pass.
    /// </summary>
    /// <summary>
    /// Decides whether an Auto bar is shown, with a dead band so sub-pixel churn in the
    /// extent cannot make it flicker.
    /// </summary>
    /// <remarks>
    /// The band sits entirely above the viewport: showing takes a clear overflow, while
    /// staying shown only takes any overflow at all. Content that comes out exactly the
    /// size of its viewport therefore resolves to "no bar" — which is the whole point,
    /// because a page that sizes itself to the viewport lands on exactly equal every
    /// time. Widening the band downwards instead would make equal-fit read as "still
    /// overflowing" and pin the bar on for good, needing content to shrink below the
    /// viewport before it would ever go away.
    /// </remarks>
    private static bool ResolveAutoScrollBar(bool wasShown, double extent, double viewport)
        => wasShown
            ? extent > viewport
            : extent > viewport + LayoutEpsilon;

    /// <summary>
    /// The single rule for "is this scroll bar shown", replacing the separate tests
    /// that layout, <see cref="UpdateScrollBarMetrics"/> and the Computed*
    /// properties each used to apply.
    /// </summary>
    private static bool ResolveScrollBarShown(
        ScrollBarVisibility mode,
        bool wasShown,
        double extent,
        double viewport)
        => mode switch
        {
            ScrollBarVisibility.Visible => true,
            // An unbounded viewport cannot be overflowed — the parent is asking how
            // big we want to be, not offering a box to fit into.
            ScrollBarVisibility.Auto => !double.IsInfinity(viewport)
                                        && ResolveAutoScrollBar(wasShown, extent, viewport),
            _ => false,
        };

    /// <summary>
    /// Records the scroll-bar decision and reflects it onto the bars. The only place
    /// that writes <see cref="UIElement.Visibility"/> on them, so every other path
    /// can re-assert the decision but never re-derive it.
    /// </summary>
    /// <remarks>
    /// Call only from <see cref="MeasureOverride"/> / <see cref="ArrangeOverride"/>:
    /// a bar turned visible outside layout would never be measured or arranged.
    /// The equality checks matter — writing Visibility invalidates measure on the
    /// bar AND on this viewer, which propagates to the window root, so a redundant
    /// write costs a whole extra layout round.
    /// </remarks>
    private void CommitScrollBarVisibility(bool showVertical, bool showHorizontal)
    {
        _verticalScrollBarShown = showVertical;
        _horizontalScrollBarShown = showHorizontal;
        // Only set once real content has been measured: a content-less viewer used
        // as an IScrollInfo owner must keep deriving visibility the old way.
        _hasCommittedScrollBarVisibility = true;

        var verticalTarget = showVertical ? Visibility.Visible : Visibility.Collapsed;
        if (_verticalScrollBar.Visibility != verticalTarget)
        {
            _verticalScrollBar.Visibility = verticalTarget;
        }

        var horizontalTarget = showHorizontal ? Visibility.Visible : Visibility.Collapsed;
        if (_horizontalScrollBar.Visibility != horizontalTarget)
        {
            _horizontalScrollBar.Visibility = horizontalTarget;
        }

        UpdateScrollMetricDependencyProperties();
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

        // Scroll bars are visual overlays, so the content viewport always occupies
        // the full arranged size. Showing or hiding a bar therefore cannot reflow the
        // content or create a visibility/layout feedback loop.
        _viewportWidth = Math.Max(0, finalSize.Width);
        _viewportHeight = Math.Max(0, finalSize.Height);

        // Content is arranged at max(extent, viewport) below. While the extent is the
        // larger of the two, shrinking the viewport leaves that rect byte-identical,
        // so Arrange short-circuits and the content's ArrangeOverride never runs —
        // content that derives its own size from ViewportWidth/Height therefore never
        // learns the viewport moved, keeps reporting the size it picked for the OLD
        // viewport, and that stale extent then reads as overflow and strands a scroll
        // bar that should not be there. Tell it the viewport changed.
        //
        // The comparison is against the last completed Arrange, not against the fields
        // just written: Measure publishes the viewport as soon as it knows it, so those
        // fields already hold this pass's value and would only ever equal themselves.
        var viewportMoved =
            _lastArrangedViewportWidth != _viewportWidth ||
            _lastArrangedViewportHeight != _viewportHeight;
        _lastArrangedViewportWidth = _viewportWidth;
        _lastArrangedViewportHeight = _viewportHeight;

        if (ContentElement != null && _scrollInfo == null && viewportMoved)
        {
            ContentElement.InvalidateArrange();
        }

        if (_scrollInfo == null)
        {
            // Clamp scroll offsets
            _horizontalOffset = HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                ? 0
                : Math.Clamp(_horizontalOffset, 0, Math.Max(0, _extentWidth - _viewportWidth));
            _verticalOffset = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                ? 0
                : Math.Clamp(_verticalOffset, 0, Math.Max(0, _extentHeight - _viewportHeight));
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
                // Arrange content with offset. Scroll bars are overlaid on this area.
                // Disabled is a constrained axis, not merely a hidden scroll bar.
                // A fixed-width descendant may still report a wider desired size,
                // but using that extent to arrange the content would stretch every
                // sibling to the overflow width. Wrapped text elsewhere on the page
                // would then reflow for an area that is outside the viewport.
                var contentWidth = HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                    ? _viewportWidth
                    : Math.Max(_extentWidth, _viewportWidth);
                var contentHeight = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                    ? _viewportHeight
                    : Math.Max(_extentHeight, _viewportHeight);

                var arrangeRect = new Rect(
                    -_horizontalOffset + _overscrollX,
                    -_verticalOffset + _overscrollY,
                    contentWidth,
                    contentHeight);

                Jalium.UI.Diagnostics.ScrollDiagnostics.RecordArrange(
                    Name, finalSize.Height, _viewportHeight, _extentHeight,
                    _verticalOffset, _overscrollY, arrangeRect.Y);

                ContentElement.Arrange(arrangeRect);
            }
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange is the first safe point at which a stable provider can release the temporary
        // endpoint anchor. If virtualization queued another measure, IsMeasureValid is false and
        // the anchor survives to follow the next refined finite maximum.
        MaintainEndAnchors(allowCompletion: true);

        // Re-check against the viewport we actually got. Measure decided from
        // availableSize, which a parent that over-constrains us (a star grid row, a
        // fixed height) does not honour.
        //
        // Gated on the extent still being meaningful: content that re-sizes itself
        // during its own arrange has just invalidated its measure, so the extent in
        // hand describes the PREVIOUS viewport. Committing "overflow" from it is
        // exactly the phantom bar this rewrite exists to remove — the pending
        // re-measure settles it a moment later with real numbers.
        var extentTrustworthy = _scrollInfo != null
                                || ContentElement is null
                                || ContentElement.IsMeasureValid;
        if (extentTrustworthy)
        {
            CommitScrollBarVisibility(
                ResolveScrollBarShown(
                    VerticalScrollBarVisibility, _verticalScrollBarShown,
                    _extentHeight, _viewportHeight),
                ResolveScrollBarShown(
                    HorizontalScrollBarVisibility, _horizontalScrollBarShown,
                    _extentWidth, _viewportWidth));
        }

        var needsVerticalScrollBar = _verticalScrollBarShown;
        var needsHorizontalScrollBar = _horizontalScrollBarShown;

        UpdateScrollBarMetrics();

        // Visibility itself is owned by CommitScrollBarVisibility; here we only place
        // the bars. Writing it again would be a redundant DP write on the hottest
        // layout path, and each one costs an ancestor-chain invalidation.
        if (needsVerticalScrollBar)
        {
            var scrollBarLayoutSize = IsOverlayScrollBarEnabled
                ? OverlayScrollBarLayoutSize
                : ScrollBarSize;
            var oppositeBarInset = !IsOverlayScrollBarEnabled && needsHorizontalScrollBar
                ? ScrollBarSize
                : 0.0;
            _verticalScrollBar.Arrange(new Rect(
                Math.Max(0, finalSize.Width - scrollBarLayoutSize),
                0,
                scrollBarLayoutSize,
                Math.Max(0, finalSize.Height - oppositeBarInset)));
        }
        else
        {
            _verticalScrollBar.Arrange(default);
        }

        if (needsHorizontalScrollBar)
        {
            var scrollBarLayoutSize = IsOverlayScrollBarEnabled
                ? OverlayScrollBarLayoutSize
                : ScrollBarSize;
            var oppositeBarInset = !IsOverlayScrollBarEnabled && needsVerticalScrollBar
                ? ScrollBarSize
                : 0.0;
            _horizontalScrollBar.Arrange(new Rect(
                0,
                Math.Max(0, finalSize.Height - scrollBarLayoutSize),
                Math.Max(0, finalSize.Width - oppositeBarInset),
                scrollBarLayoutSize));
        }
        else
        {
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

        bool scrollingTowardStart = e.Delta > 0;
        bool scrollingTowardEnd = e.Delta < 0;
        if (scrollingTowardStart || e.Delta == 0)
        {
            ClearEndAnchor(isVertical: true);
            ClearEndAnchor(isVertical: false);
            _smoothVerticalEndAnchorPending = false;
            _smoothHorizontalEndAnchorPending = false;
        }

        bool useSmoothWheelInertia = IsScrollInertiaEnabled && GetEffectiveScrollInertiaDurationMs() > 0;
        TraceVerticalState(
            "wheel-enter",
            $"delta={e.Delta} useSmooth={useSmoothWheelInertia} " +
            $"source={e.Source?.GetType().FullName ?? "null"}");

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
            if (scrollingTowardEnd && atBottom)
            {
                // A true, settled boundary still bubbles to an ancestor. An active endpoint
                // intent means this is only the old estimated boundary; keep the event here
                // while layout resolves the next finite maximum.
                if (_verticalEndAnchorActive || _smoothVerticalEndAnchorPending)
                {
                    MaintainEndAnchors(allowCompletion: false);
                    e.Handled = true;
                }
                TraceVerticalState(
                    "wheel-provider-boundary",
                    $"delta={e.Delta} action={(e.Handled ? "retain" : "bubble")}");
                return;
            }

            if ((scrollingTowardStart && atTop) || e.Delta == 0)
            {
                TraceVerticalState(
                    "wheel-provider-boundary",
                    $"delta={e.Delta} action=bubble");
                return;
            }

            if (useSmoothWheelInertia)
            {
                // Smooth animated scroll through IScrollInfo
                var delta = ComputeMouseWheelDelta(e.Delta, LineScrollAmount, _viewportHeight);

                InitializeSmoothScrollTargetsIfNeeded();
                var nextTarget = _smoothTargetY + delta;
                _smoothVerticalEndAnchorPending = scrollingTowardEnd &&
                                                  ScrollableHeight > 0 &&
                                                  nextTarget >= ScrollableHeight - 0.01;
                _smoothTargetY = Math.Clamp(nextTarget, 0, ScrollableHeight);
                TraceVerticalState(
                    "wheel-smooth-target",
                    $"delta={e.Delta} computedDelta={delta:R} rawTarget={nextTarget:R}");
                StartSmoothScroll();
            }
            else
            {
                // Immediate scroll
                CancelSmoothScroll();
                if (scrollingTowardStart)
                    _scrollInfo.MouseWheelUp();
                else if (scrollingTowardEnd)
                    _scrollInfo.MouseWheelDown();
                SyncFromScrollInfo();
                if (scrollingTowardEnd && IsAtScrollEnd(isVertical: true))
                {
                    ApplyEndAnchorOffset(isVertical: true);
                }
                TraceVerticalState(
                    "wheel-provider-immediate",
                    $"delta={e.Delta}");
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

            if ((scrollingTowardStart && atTop) || (scrollingTowardEnd && atBottom))
            {
                // At boundary: don't handle, let parent ScrollViewer process it
                TraceVerticalState(
                    "wheel-physical-boundary",
                    $"delta={e.Delta} action=bubble");
            }
            else
            {
                var delta = ComputeMouseWheelDelta(e.Delta, LineScrollAmount, _viewportHeight);

                if (useSmoothWheelInertia)
                {
                    // Smooth animated scroll: accumulate target, animate toward it
                    InitializeSmoothScrollTargetsIfNeeded();
                    var nextTarget = _smoothTargetY + delta;
                    _smoothVerticalEndAnchorPending = scrollingTowardEnd &&
                                                      ScrollableHeight > 0 &&
                                                      nextTarget >= ScrollableHeight - 0.01;
                    _smoothTargetY = Math.Clamp(nextTarget, 0, ScrollableHeight);
                    TraceVerticalState(
                        "wheel-smooth-target",
                        $"delta={e.Delta} computedDelta={delta:R} rawTarget={nextTarget:R}");
                    StartSmoothScroll();
                }
                else
                {
                    CancelSmoothScroll();
                    ScrollToVerticalOffset(_verticalOffset + delta);
                    TraceVerticalState(
                        "wheel-physical-immediate",
                        $"delta={e.Delta} computedDelta={delta:R}");
                }

                e.Handled = true;
            }
        }
        else if (CanScrollHorizontally)
        {
            bool atLeft = _horizontalOffset <= 0;
            bool atRight = _horizontalOffset >= ScrollableWidth;

            if ((scrollingTowardStart && atLeft) || (scrollingTowardEnd && atRight))
            {
                // At boundary: don't handle, let parent ScrollViewer process it
            }
            else
            {
                var delta = ComputeMouseWheelDelta(e.Delta, LineScrollAmount, _viewportWidth);

                if (useSmoothWheelInertia)
                {
                    InitializeSmoothScrollTargetsIfNeeded();
                    var nextTarget = _smoothTargetX + delta;
                    _smoothHorizontalEndAnchorPending = scrollingTowardEnd &&
                                                        ScrollableWidth > 0 &&
                                                        nextTarget >= ScrollableWidth - 0.01;
                    _smoothTargetX = Math.Clamp(nextTarget, 0, ScrollableWidth);
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

    /// <summary>
    /// Starts a new smooth-scroll session from the currently committed position on
    /// both axes. Every animation tick advances both targets, even when the wheel
    /// changed only one axis, so leaving the other target at its default/stale value
    /// would pull that axis back toward zero.
    /// </summary>
    private void InitializeSmoothScrollTargetsIfNeeded()
    {
        if (_isSmoothScrolling)
            return;

        _smoothTargetX = _horizontalOffset;
        _smoothTargetY = _verticalOffset;
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

            var anchorVerticalEnd = _smoothVerticalEndAnchorPending && IsAtScrollEnd(isVertical: true);
            var anchorHorizontalEnd = _smoothHorizontalEndAnchorPending && IsAtScrollEnd(isVertical: false);
            StopSmoothScroll();
            if (anchorVerticalEnd)
                ApplyEndAnchorOffset(isVertical: true);
            if (anchorHorizontalEnd)
                ApplyEndAnchorOffset(isVertical: false);
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
        _smoothVerticalEndAnchorPending = false;
        _smoothHorizontalEndAnchorPending = false;
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

        TraceVerticalState(
            "smooth-tick-enter",
            $"elapsedMs={elapsedMs}");

        _smoothTargetY = _smoothVerticalEndAnchorPending
            ? ScrollableHeight
            : Math.Clamp(_smoothTargetY, 0, ScrollableHeight);
        _smoothTargetX = _smoothHorizontalEndAnchorPending
            ? ScrollableWidth
            : Math.Clamp(_smoothTargetX, 0, ScrollableWidth);
        if (elapsedMs <= 0)
            elapsedMs = Math.Max(1, SmoothScrollIntervalMs);

        double dtSeconds = Math.Min(elapsedMs / 1000.0, SmoothScrollMaxDeltaTimeSeconds);
        double alpha = ComputeSmoothAlpha(dtSeconds);
        var dpiScale = FrameworkElement.LayoutDpiScale;
        var oneDevicePixel = dpiScale > 0 && double.IsFinite(dpiScale)
            ? 1.0 / dpiScale
            : 1.0;
        // Every committed scroll offset is device-pixel snapped. At high refresh rates the
        // time-based minimum can be less than half a device pixel, so a legitimate 10px+ tail
        // repeatedly rounds back to the current offset and is mistaken for completion. One
        // physical pixel is the smallest step that can always make observable progress in both
        // directions after snapping.
        double minStep = Math.Max(
            SmoothScrollMinSpeedPixelsPerSecond * dtSeconds,
            oneDevicePixel);

        bool moved = false;
        var offsetXBefore = _horizontalOffset;
        var offsetYBefore = _verticalOffset;

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

        // Realization can grow the extent synchronously inside the setter above. Keep an
        // endpoint-targeted wheel animation alive and retarget it before the no-progress test
        // mistakes the old maximum for a completed scroll.
        bool endTargetExpanded = false;
        if (_smoothVerticalEndAnchorPending && _smoothTargetY < ScrollableHeight - 0.01)
        {
            _smoothTargetY = ScrollableHeight;
            endTargetExpanded = true;
        }
        if (_smoothHorizontalEndAnchorPending && _smoothTargetX < ScrollableWidth - 0.01)
        {
            _smoothTargetX = ScrollableWidth;
            endTargetExpanded = true;
        }

        TraceVerticalState(
            "smooth-tick-commit",
            $"elapsedMs={elapsedMs} moved={moved} endTargetExpanded={endTargetExpanded}");

        // The committed offsets are quantized to whole device pixels, so a
        // sub-pixel closing step can round back onto the pixel the offset is
        // already on. StepSmoothAxis still reports intent to move in that case;
        // judging progress by the committed offsets is what guarantees the timer
        // stops instead of spinning on a target it can never get closer to.
        if (!endTargetExpanded &&
            (!moved || (_horizontalOffset == offsetXBefore && _verticalOffset == offsetYBefore)))
        {
            var anchorVerticalEnd = _smoothVerticalEndAnchorPending && IsAtScrollEnd(isVertical: true);
            var anchorHorizontalEnd = _smoothHorizontalEndAnchorPending && IsAtScrollEnd(isVertical: false);
            TraceVerticalState(
                "smooth-complete",
                $"anchorVertical={anchorVerticalEnd} anchorHorizontal={anchorHorizontalEnd}");
            StopSmoothScroll();
            if (anchorVerticalEnd)
                ApplyEndAnchorOffset(isVertical: true);
            if (anchorHorizontalEnd)
                ApplyEndAnchorOffset(isVertical: false);
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

        var scrollBar = isVertical ? _verticalScrollBar : _horizontalScrollBar;
        HandleScrollBarValueChange(
            e,
            isVertical ? ScrollableHeight : ScrollableWidth,
            scrollBar.Maximum,
            isVertical);
    }

    private void HandleScrollBarValueChange(
        ScrollEventArgs e,
        double maxValue,
        double interactionMaximum,
        bool isVertical)
    {
        var clampedValue = Math.Clamp(e.NewValue, 0, Math.Max(0, maxValue));

        // Deferred scrolling (opt-in): keep the content still while the thumb is being
        // dragged and apply the offset only once, when the thumb is released.
        if (IsDeferredScrollingEnabled && e.ScrollEventType == ScrollEventType.ThumbTrack)
        {
            ClearEndAnchor(isVertical);
            _isDeferredScrolling = true;
            _deferredScrollIsVertical = isVertical;
            if (isVertical)
                _deferredVerticalOffset = clampedValue;
            else
                _deferredHorizontalOffset = clampedValue;
            UpdateDeferredRequestedOffset(isVertical, clampedValue);
            return;
        }

        if (IsDeferredScrollingEnabled && e.ScrollEventType == ScrollEventType.EndScroll && _isDeferredScrolling)
        {
            var deferredValue = isVertical ? _deferredVerticalOffset : _deferredHorizontalOffset;
            var deferredScrollToRangeEnd = IsScrollBarRangeEnd(deferredValue, interactionMaximum);
            StopDragScrollCoalesce();
            _isDeferredScrolling = false;
            _deferredScrollIsVertical = false;
            if (deferredScrollToRangeEnd)
            {
                ApplyEndAnchorOffset(isVertical);
            }
            else
            {
                ApplyScrollOffset(isVertical, deferredValue);
            }
            // The scrollbar mapping is intentionally frozen for the lifetime of thumb capture.
            // Thaw it even when the final content offset did not change (and therefore did not
            // naturally call UpdateScrollBarMetrics).
            UpdateScrollBarMetrics();
            return;
        }

        _isDeferredScrolling = false;
        _deferredScrollIsVertical = false;

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
            // Maximum is intentionally frozen while the thumb owns capture. Reaching that
            // frozen endpoint is nevertheless an endpoint intent, not a request for its stale
            // numeric value. Keep the intent beside the finite value so it never crosses an
            // arbitrary IScrollInfo boundary as a non-finite sentinel.
            var thumbTracksRangeEnd = IsScrollBarRangeEnd(e.NewValue, interactionMaximum);
            if (thumbTracksRangeEnd)
            {
                ActivateEndAnchor(isVertical);
            }
            else
            {
                ClearEndAnchor(isVertical);
            }
            ScheduleCoalescedDragScroll(isVertical, clampedValue, thumbTracksRangeEnd);
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
        var scrollToRangeEnd = e.ScrollEventType == ScrollEventType.EndScroll &&
                               (IsScrollBarRangeEnd(e.NewValue, interactionMaximum) ||
                                IsScrollBarRangeEnd(finalValue, interactionMaximum) ||
                                (isVertical
                                    ? _pendingDragVerticalEndAnchor
                                    : _pendingDragHorizontalEndAnchor));
        StopDragScrollCoalesce();
        if (scrollToRangeEnd)
        {
            ApplyEndAnchorOffset(isVertical);
        }
        else
        {
            ApplyScrollOffset(isVertical, finalValue);
        }
        if (e.ScrollEventType == ScrollEventType.EndScroll)
        {
            UpdateScrollBarMetrics();
        }
    }

    private static bool IsScrollBarRangeEnd(double value, double interactionMaximum)
    {
        if (double.IsNaN(value) || !double.IsFinite(interactionMaximum) || interactionMaximum <= 0)
            return false;

        return value >= interactionMaximum - 0.01;
    }

    private bool IsAtScrollEnd(bool isVertical)
    {
        if (_scrollInfo != null)
        {
            var extent = isVertical ? _scrollInfo.ExtentHeight : _scrollInfo.ExtentWidth;
            var viewport = isVertical ? _scrollInfo.ViewportHeight : _scrollInfo.ViewportWidth;
            var offset = isVertical ? _scrollInfo.VerticalOffset : _scrollInfo.HorizontalOffset;
            var maximum = Math.Max(0, extent - viewport);
            return maximum > 0 && offset >= maximum - 0.5;
        }

        var physicalMaximum = isVertical ? ScrollableHeight : ScrollableWidth;
        var physicalOffset = isVertical ? _verticalOffset : _horizontalOffset;
        return physicalMaximum > 0 && physicalOffset >= physicalMaximum - 0.5;
    }

    private void TraceVerticalState(string phase, string details = "")
    {
        if (!Jalium.UI.Diagnostics.ScrollDiagnostics.Enabled)
            return;

        var info = _scrollInfo;
        var infoType = info?.GetType().FullName ?? "<physical>";
        var infoOffset = info?.VerticalOffset ?? double.NaN;
        var infoExtent = info?.ExtentHeight ?? double.NaN;
        var infoViewport = info?.ViewportHeight ?? double.NaN;
        var infoMaximum = info == null
            ? double.NaN
            : Math.Max(0, infoExtent - infoViewport);
        var contentMeasureValid = ContentElement?.IsMeasureValid;
        var payload =
            $"{details} requested={_requestedVerticalOffset:R} committed={_verticalOffset:R} " +
            $"extent={_extentHeight:R} viewport={_viewportHeight:R} max={ScrollableHeight:R} " +
            $"barValue={_verticalScrollBar.Value:R} barMax={_verticalScrollBar.Maximum:R} " +
            $"barDragging={_verticalScrollBar.IsThumbDragging} smooth={_isSmoothScrolling} " +
            $"smoothTarget={_smoothTargetY:R} smoothEnd={_smoothVerticalEndAnchorPending} " +
            $"anchor={_verticalEndAnchorActive} anchorMax={_verticalEndAnchorMaximum:R} " +
            $"measureValid={contentMeasureValid?.ToString() ?? "null"} provider={infoType} " +
            $"providerOffset={infoOffset:R} providerExtent={infoExtent:R} " +
            $"providerViewport={infoViewport:R} providerMax={infoMaximum:R}";
        Jalium.UI.Diagnostics.ScrollDiagnostics.RecordEvent(
            Name,
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this),
            phase,
            payload);
    }

    private void ActivateEndAnchor(bool isVertical)
    {
        if (_scrollInfo == null)
            return;

        if (isVertical)
        {
            if (!_verticalEndAnchorActive)
            {
                _verticalEndAnchorActive = true;
                _verticalEndAnchorMaximum = double.NaN;
                TraceVerticalState("anchor-activate");
            }
        }
        else if (!_horizontalEndAnchorActive)
        {
            _horizontalEndAnchorActive = true;
            _horizontalEndAnchorMaximum = double.NaN;
        }
    }

    private void ClearEndAnchor(bool isVertical)
    {
        if (isVertical)
        {
            if (_verticalEndAnchorActive || _pendingDragVerticalEndAnchor)
            {
                TraceVerticalState("anchor-clear");
            }
            _verticalEndAnchorActive = false;
            _verticalEndAnchorMaximum = double.NaN;
            _pendingDragVerticalEndAnchor = false;
        }
        else
        {
            _horizontalEndAnchorActive = false;
            _horizontalEndAnchorMaximum = double.NaN;
            _pendingDragHorizontalEndAnchor = false;
        }
    }

    private void ApplyEndAnchorOffset(bool isVertical)
    {
        ActivateEndAnchor(isVertical);

        var liveEnd = Math.Max(0, isVertical ? ScrollableHeight : ScrollableWidth);
        var previousEnd = isVertical ? _verticalEndAnchorMaximum : _horizontalEndAnchorMaximum;
        // A virtualized measure may briefly publish a zero/smaller extent while replacing its
        // realization window. Never follow that transient contraction from an input callback;
        // a completed stable Arrange is the only place allowed to commit a smaller endpoint.
        var finiteEnd = !double.IsNaN(previousEnd) && liveEnd < previousEnd
            ? previousEnd
            : liveEnd;
        if (isVertical)
        {
            TraceVerticalState(
                "anchor-apply-request",
                $"liveEnd={liveEnd:R} previousEnd={previousEnd:R} finiteEnd={finiteEnd:R}");
        }
        if (isVertical)
            _verticalEndAnchorMaximum = finiteEnd;
        else
            _horizontalEndAnchorMaximum = finiteEnd;

        _isApplyingEndAnchor = true;
        try
        {
            ApplyScrollOffset(isVertical, finiteEnd);
        }
        finally
        {
            _isApplyingEndAnchor = false;
        }

        // Exact, non-virtualizing providers generally need only Arrange and already know their
        // final extent. A virtualizing provider invalidates Measure when it accepts the jump;
        // keep the anchor only in that case so later extent refinements are followed.
        if (!IsEndAnchorInteractionActive(isVertical) &&
            ContentElement is { IsMeasureValid: true })
        {
            ClearEndAnchor(isVertical);
        }
    }

    private void MaintainEndAnchors(bool allowCompletion)
    {
        if (_isApplyingEndAnchor || (!_verticalEndAnchorActive && !_horizontalEndAnchorActive))
            return;

        if (_scrollInfo == null)
        {
            ClearEndAnchor(isVertical: true);
            ClearEndAnchor(isVertical: false);
            return;
        }

        _horizontalOffset = _scrollInfo.HorizontalOffset;
        _verticalOffset = _scrollInfo.VerticalOffset;
        SyncExtentFromScrollInfo();

        MaintainEndAnchor(isVertical: true, allowCompletion);
        MaintainEndAnchor(isVertical: false, allowCompletion);
        UpdateRequestedOffsetsFromContent();
    }

    private void MaintainEndAnchor(bool isVertical, bool allowCompletion)
    {
        var isActive = isVertical ? _verticalEndAnchorActive : _horizontalEndAnchorActive;
        if (!isActive || _scrollInfo == null)
            return;

        var finiteEnd = Math.Max(0, isVertical ? ScrollableHeight : ScrollableWidth);
        var previousMaximum = isVertical ? _verticalEndAnchorMaximum : _horizontalEndAnchorMaximum;
        var interactionActive = IsEndAnchorInteractionActive(isVertical);
        var canCommitShrink = allowCompletion &&
                              !interactionActive &&
                              ContentElement is { IsMeasureValid: true };
        if (!double.IsNaN(previousMaximum) &&
            finiteEnd < previousMaximum &&
            !canCommitShrink)
        {
            if (isVertical)
            {
                TraceVerticalState(
                    "anchor-ignore-shrink",
                    $"liveEnd={finiteEnd:R} previousEnd={previousMaximum:R} allowCompletion={allowCompletion} interaction={interactionActive}");
            }
            return;
        }
        var rangeChanged = double.IsNaN(previousMaximum) || !AreClose(previousMaximum, finiteEnd);
        var currentOffset = isVertical ? _scrollInfo.VerticalOffset : _scrollInfo.HorizontalOffset;

        if (isVertical)
            _verticalEndAnchorMaximum = finiteEnd;
        else
            _horizontalEndAnchorMaximum = finiteEnd;

        if (rangeChanged || !AreClose(currentOffset, finiteEnd))
        {
            if (isVertical)
            {
                TraceVerticalState(
                    "anchor-maintain-request",
                    $"finiteEnd={finiteEnd:R} previousEnd={previousMaximum:R} current={currentOffset:R} rangeChanged={rangeChanged} allowCompletion={allowCompletion}");
            }
            _isApplyingEndAnchor = true;
            try
            {
                if (isVertical)
                {
                    _scrollInfo.SetVerticalOffset(finiteEnd);
                    _verticalOffset = _scrollInfo.VerticalOffset;
                }
                else
                {
                    _scrollInfo.SetHorizontalOffset(finiteEnd);
                    _horizontalOffset = _scrollInfo.HorizontalOffset;
                }
            }
            finally
            {
                _isApplyingEndAnchor = false;
            }
            if (isVertical)
            {
                TraceVerticalState("anchor-maintain-commit");
            }
        }

        if (allowCompletion &&
            !rangeChanged &&
            !interactionActive &&
            ContentElement is { IsMeasureValid: true })
        {
            ClearEndAnchor(isVertical);
        }
    }

    private bool IsEndAnchorInteractionActive(bool isVertical)
    {
        if (_isDeferredScrolling)
            return true;

        return isVertical
            ? _verticalScrollBar.IsThumbDragging || _isDraggingVerticalThumb || _hasPendingDragVerticalScroll
            : _horizontalScrollBar.IsThumbDragging || _hasPendingDragHorizontalScroll;
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

    private void ScheduleCoalescedDragScroll(bool isVertical, double value, bool anchorsEnd = false)
    {
        if (isVertical)
        {
            _pendingDragVerticalOffset = value;
            _hasPendingDragVerticalScroll = true;
            _pendingDragVerticalEndAnchor = anchorsEnd;
        }
        else
        {
            _pendingDragHorizontalOffset = value;
            _hasPendingDragHorizontalScroll = true;
            _pendingDragHorizontalEndAnchor = anchorsEnd;
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
            if (_pendingDragVerticalEndAnchor)
            {
                ApplyEndAnchorOffset(isVertical: true);
            }
            else
            {
                ScrollToVerticalOffset(_pendingDragVerticalOffset);
            }
            _pendingDragVerticalEndAnchor = false;
            applied = true;
        }

        if (_hasPendingDragHorizontalScroll)
        {
            _hasPendingDragHorizontalScroll = false;
            if (_pendingDragHorizontalEndAnchor)
            {
                ApplyEndAnchorOffset(isVertical: false);
            }
            else
            {
                ScrollToHorizontalOffset(_pendingDragHorizontalOffset);
            }
            _pendingDragHorizontalEndAnchor = false;
            applied = true;
        }

        return applied;
    }

    private void StopDragScrollCoalesce()
    {
        _hasPendingDragVerticalScroll = false;
        _hasPendingDragHorizontalScroll = false;
        _pendingDragVerticalEndAnchor = false;
        _pendingDragHorizontalEndAnchor = false;
        _dragScrollCoalesceTimer?.Stop();
    }

    /// <summary>
    /// Once layout has settled an Auto decision, the metric refreshes driven by
    /// scrolling must only re-assert it. They used to apply their own rule
    /// (<c>maxOffset &gt; 0</c>, no dead band), so a virtualizing panel whose
    /// estimated extent wobbled around the threshold could flip the bar outside
    /// layout — and every flip invalidated the whole ancestor chain.
    /// </summary>
    private ScrollBarVisibility EffectiveScrollBarMode(ScrollBarVisibility mode, bool shown)
        => _hasCommittedScrollBarVisibility && mode == ScrollBarVisibility.Auto
            ? (shown ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden)
            : mode;

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
                EffectiveScrollBarMode(VerticalScrollBarVisibility, _verticalScrollBarShown),
                canScroll: CanScrollVertically);

            ConfigureScrollBar(
                _horizontalScrollBar,
                ScrollableWidth + hOver,
                _viewportWidth,
                _horizontalOffset + Math.Max(0, -_overscrollX),
                EffectiveScrollBarMode(HorizontalScrollBarVisibility, _horizontalScrollBarShown),
                canScroll: CanScrollHorizontally);
        }
        finally
        {
            _isUpdatingScrollBars = false;
        }

        ApplyScrollBarAutoHideVisualState();
        UpdateScrollMetricDependencyProperties();
        TraceVerticalState("scrollbar-metrics");
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
        TraceVerticalState("invalidate-scroll-info-enter");

        _horizontalOffset = _scrollInfo.HorizontalOffset;
        _verticalOffset = _scrollInfo.VerticalOffset;
        SyncExtentFromScrollInfo();
        MaintainEndAnchors(allowCompletion: false);
        if (!_isDeferredScrolling)
        {
            UpdateRequestedOffsetsFromContent();
        }
        TraceVerticalState(
            "invalidate-scroll-info-sync",
            $"oldOffset={oldVertical:R} oldExtent={oldExtentHeight:R}");

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

        var anchorVerticalEnd = _smoothVerticalEndAnchorPending && IsAtScrollEnd(isVertical: true);
        var anchorHorizontalEnd = _smoothHorizontalEndAnchorPending && IsAtScrollEnd(isVertical: false);
        StopSmoothScroll();
        if (anchorVerticalEnd)
            ApplyEndAnchorOffset(isVertical: true);
        if (anchorHorizontalEnd)
            ApplyEndAnchorOffset(isVertical: false);
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
        // Report what the bars actually do, not a third independent derivation of it.
        // Before layout has committed a decision there is nothing to report yet, so
        // fall back to the scrollability test.
        SetValue(
            ComputedHorizontalScrollBarVisibilityPropertyKey,
            GetComputedScrollBarVisibility(
                HorizontalScrollBarVisibility,
                _hasCommittedScrollBarVisibility ? _horizontalScrollBarShown : CanScrollHorizontally));
        SetValue(
            ComputedVerticalScrollBarVisibilityPropertyKey,
            GetComputedScrollBarVisibility(
                VerticalScrollBarVisibility,
                _hasCommittedScrollBarVisibility ? _verticalScrollBarShown : CanScrollVertically));
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
/// Axes on which a <see cref="ScrollViewer"/>'s content should be sized to the
/// viewport instead of to its own natural size. See
/// <see cref="ScrollViewer.FillViewportProperty"/>.
/// </summary>
public enum FillViewportMode
{
    /// <summary>Both axes keep the normal behaviour: scrollable axes are measured
    /// unbounded, so the content may be as large as it likes and the viewer
    /// scrolls it.</summary>
    None,

    /// <summary>The content is measured with the viewport's height, so it is
    /// exactly as tall as the viewer and is expected to scroll itself.</summary>
    Vertical,

    /// <summary>The content is measured with the viewport's width.</summary>
    Horizontal,

    /// <summary>The content is measured with the viewport on both axes.</summary>
    Both,
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
