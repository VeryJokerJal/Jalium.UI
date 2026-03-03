using System.Runtime.InteropServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a scrollable area that can contain other visible elements.
/// </summary>
[ContentProperty("Content")]
public sealed partial class ScrollViewer : Control
{
    #region Fields

    private UIElement? _content;
    private IScrollInfo? _scrollInfo;
    private double _horizontalOffset;
    private double _verticalOffset;
    private double _extentWidth;
    private double _extentHeight;
    private double _viewportWidth;
    private double _viewportHeight;
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
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Disabled, OnScrollBarVisibilityChanged));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollBarVisibilityChanged));

    /// <summary>
    /// Identifies the CanContentScroll dependency property.
    /// </summary>
    public static readonly DependencyProperty CanContentScrollProperty =
        DependencyProperty.Register(nameof(CanContentScroll), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the PanningMode dependency property.
    /// </summary>
    public static readonly DependencyProperty PanningModeProperty =
        DependencyProperty.Register(nameof(PanningMode), typeof(PanningMode), typeof(ScrollViewer),
            new PropertyMetadata(PanningMode.None));

    /// <summary>
    /// Identifies the PanningDeceleration dependency property.
    /// </summary>
    public static readonly DependencyProperty PanningDecelerationProperty =
        DependencyProperty.Register(nameof(PanningDeceleration), typeof(double), typeof(ScrollViewer),
            new PropertyMetadata(DefaultPanningDeceleration, OnPanningParametersChanged));

    /// <summary>
    /// Identifies the PanningRatio dependency property.
    /// </summary>
    public static readonly DependencyProperty PanningRatioProperty =
        DependencyProperty.Register(nameof(PanningRatio), typeof(double), typeof(ScrollViewer),
            new PropertyMetadata(DefaultPanningRatio, OnPanningParametersChanged));

    /// <summary>
    /// Identifies the IsScrollInertiaEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsScrollInertiaEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollInertiaEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the ScrollInertiaDurationMs dependency property.
    /// </summary>
    public static readonly DependencyProperty ScrollInertiaDurationMsProperty =
        DependencyProperty.Register(nameof(ScrollInertiaDurationMs), typeof(double), typeof(ScrollViewer),
            new PropertyMetadata(DefaultScrollInertiaDurationMs, OnScrollInertiaDurationChanged));

    /// <summary>
    /// Identifies the IsDeferredScrollingEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDeferredScrollingEnabledProperty =
        DependencyProperty.Register(nameof(IsDeferredScrollingEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsScrollBarAutoHideEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsScrollBarAutoHideEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollBarAutoHideEnabled), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(true, OnScrollBarAutoHideEnabledChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the horizontal scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the content can scroll by items rather than pixels.
    /// </summary>
    public bool CanContentScroll
    {
        get => (bool)GetValue(CanContentScrollProperty)!;
        set => SetValue(CanContentScrollProperty, value);
    }

    /// <summary>
    /// Gets or sets the panning mode for touch interaction.
    /// </summary>
    public PanningMode PanningMode
    {
        get => (PanningMode)(GetValue(PanningModeProperty) ?? PanningMode.None);
        set => SetValue(PanningModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the deceleration used to project touch/stylus panning inertia.
    /// Unit is DIPs per ms^2.
    /// </summary>
    public double PanningDeceleration
    {
        get => (double)GetValue(PanningDecelerationProperty)!;
        set => SetValue(PanningDecelerationProperty, value);
    }

    /// <summary>
    /// Gets or sets the translation ratio between pointer movement and scroll delta.
    /// </summary>
    public double PanningRatio
    {
        get => (double)GetValue(PanningRatioProperty)!;
        set => SetValue(PanningRatioProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether scroll inertia is enabled.
    /// When enabled, mouse wheel scrolling will have momentum with smooth deceleration.
    /// </summary>
    public bool IsScrollInertiaEnabled
    {
        get => (bool)GetValue(IsScrollInertiaEnabledProperty)!;
        set => SetValue(IsScrollInertiaEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the smooth wheel inertia duration in milliseconds.
    /// Larger values feel softer/slower. Values less than or equal to 0 disable wheel inertia.
    /// </summary>
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
    public bool IsDeferredScrollingEnabled
    {
        get => (bool)GetValue(IsDeferredScrollingEnabledProperty)!;
        set => SetValue(IsDeferredScrollingEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether scroll bars auto-hide when not being interacted with.
    /// Matches WinUI-style behavior and is enabled by default.
    /// </summary>
    public bool IsScrollBarAutoHideEnabled
    {
        get => (bool)GetValue(IsScrollBarAutoHideEnabledProperty)!;
        set => SetValue(IsScrollBarAutoHideEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the content of the ScrollViewer.
    /// </summary>
    public UIElement? Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                // Disconnect old IScrollInfo
                if (_scrollInfo != null)
                {
                    _scrollInfo.ScrollOwner = null;
                    _scrollInfo = null;
                }

                // Remove old content from visual tree
                if (_content != null)
                {
                    RemoveContentChild(_content);
                }

                _content = value;

                // Add new content to visual tree
                if (_content != null)
                {
                    AddContentChild(_content);
                }

                // Connect new IScrollInfo if content implements it
                _scrollInfo = _content as IScrollInfo;
                if (_scrollInfo != null)
                {
                    _scrollInfo.ScrollOwner = this;
                    _scrollInfo.CanHorizontallyScroll = HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
                    _scrollInfo.CanVerticallyScroll = VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
                }

                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset => _horizontalOffset;

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset => _verticalOffset;

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

    // Smooth scroll animation fields
    private DispatcherTimer? _smoothScrollTimer;
    private DispatcherTimer? _scrollBarAutoHideTimer;
    private long _scrollBarAutoHideDeadlineTick;
    private double _smoothTargetX;
    private double _smoothTargetY;
    private bool _isSmoothScrolling;
    private bool _isApplyingSmoothScrollStep;
    private bool _areAutoHideScrollBarsRevealed;
    private long _lastSmoothTickTime;
    private const double DefaultScrollInertiaDurationMs = 300.0;
    private const double DefaultScrollBarAutoHideDelayMs = 3000.0;
    private const double SmoothScrollDurationTailRatio = 0.05;
    private const double SmoothScrollSnapThreshold = 0.5;
    private const double SmoothScrollMinSpeedPixelsPerSecond = 60.0;
    private const double SmoothScrollMaxDeltaTimeSeconds = 0.1;
    private static int SmoothScrollIntervalMs => CompositionTarget.FrameIntervalMs;
    private const double DefaultPanningDeceleration = 0.001;
    private const double DefaultPanningRatio = 1.0;
    private const double PointerPanningLockThreshold = 8.0;

    // Deferred scrolling fields
    private bool _isDeferredScrolling;
    private double _deferredVerticalOffset;
    private double _deferredHorizontalOffset;

    // Direct viewer-level thumb drag fallback (used by synthetic input paths in tests)
    private bool _isDraggingVerticalThumb;
    private double _dragStartMouseY;
    private double _dragStartVerticalOffset;
    private const double InputThumbHitWidth = 16.0;
    private const double InputScrollButtonSize = 16.0;

    // Touch/stylus panning state
    private bool _isPointerPanningActive;
    private bool _hasPointerPanningMoved;
    private uint _activePanningPointerId;
    private Point _pointerPanningStartPoint;
    private Point _pointerPanningLastPoint;
    private long _pointerPanningLastTimestamp;
    private double _pointerPanningVelocityX;
    private double _pointerPanningVelocityY;
    private bool _pointerPanningAxisResolved;
    private bool _pointerPanningAllowHorizontal;
    private bool _pointerPanningAllowVertical;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollViewer"/> class.
    /// </summary>
    public ScrollViewer()
    {
        _verticalScrollBar = CreateScrollBar(Orientation.Vertical);
        _horizontalScrollBar = CreateScrollBar(Orientation.Horizontal);
        AddVisualChild(_verticalScrollBar);
        AddVisualChild(_horizontalScrollBar);

        // ScrollViewer clips content by default
        ClipToBounds = true;

        // Register for input events
        AddHandler(MouseWheelEvent, new RoutedEventHandler(HandleMouseWheel));
        AddHandler(MouseDownEvent, new RoutedEventHandler(HandleMouseDown));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(HandleMouseMove));
        AddHandler(MouseUpEvent, new RoutedEventHandler(HandleMouseUp));
        AddHandler(PointerDownEvent, new RoutedEventHandler(HandlePointerDown));
        AddHandler(PointerMoveEvent, new RoutedEventHandler(HandlePointerMove));
        AddHandler(PointerUpEvent, new RoutedEventHandler(HandlePointerUp));
        AddHandler(PointerCancelEvent, new RoutedEventHandler(HandlePointerCancel));

        // Register keyboard handler
        AddHandler(KeyDownEvent, new RoutedEventHandler(HandleKeyDown));

        // Register for BringIntoView requests
        AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(HandleRequestBringIntoView));
    }

    private ScrollBar CreateScrollBar(Orientation orientation)
    {
        var scrollBar = new ScrollBar
        {
            Orientation = orientation,
            Focusable = false,
            Cursor = Jalium.UI.Cursors.Arrow,
            Visibility = Visibility.Collapsed,
            SmallChange = LineScrollAmount
        };
        scrollBar.Scroll += OnScrollBarScroll;
        scrollBar.AddHandler(MouseEnterEvent, new RoutedEventHandler(OnScrollBarMouseEnter));
        scrollBar.AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnScrollBarMouseLeave));
        return scrollBar;
    }

    private void HandleKeyDown(object sender, RoutedEventArgs e)
    {
        if (e is KeyEventArgs keyArgs)
        {
            OnKeyDown(keyArgs);
        }
    }

    private void HandleRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is FrameworkElement targetElement)
        {
            MakeVisible(targetElement, e.TargetRect);
            e.Handled = true;
        }
    }

    private void HandleMouseWheel(object sender, RoutedEventArgs e)
    {
        if (e is MouseWheelEventArgs wheelArgs)
        {
            OnMouseWheel(wheelArgs);
        }
    }

    private void HandleMouseDown(object sender, RoutedEventArgs e)
    {
        // Only apply this fallback for direct viewer-originated input.
        // Real pointer input normally targets ScrollBar/Thumb directly.
        if (!ReferenceEquals(e.OriginalSource, this))
            return;

        if (e is not MouseButtonEventArgs mouseArgs ||
            mouseArgs.ChangedButton != MouseButton.Left ||
            !CanScrollVertically ||
            ScrollableHeight <= 0)
        {
            return;
        }

        var point = mouseArgs.GetPosition(this);
        if (point.X < RenderSize.Width - InputThumbHitWidth)
            return;

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

    private void HandleMouseMove(object sender, RoutedEventArgs e)
    {
        if (!_isDraggingVerticalThumb)
            return;

        if (e is not MouseEventArgs mouseArgs || mouseArgs.LeftButton != MouseButtonState.Pressed)
            return;

        var metrics = GetInputVerticalThumbMetrics();
        if (metrics.ScrollRange <= 0)
            return;

        var point = mouseArgs.GetPosition(this);
        var deltaY = point.Y - _dragStartMouseY;
        var newOffset = _dragStartVerticalOffset + (deltaY / metrics.ScrollRange) * ScrollableHeight;

        CancelSmoothScroll();
        ScrollToVerticalOffset(newOffset);
        e.Handled = true;
    }

    private void HandleMouseUp(object sender, RoutedEventArgs e)
    {
        if (!_isDraggingVerticalThumb)
            return;

        if (e is not MouseButtonEventArgs mouseArgs || mouseArgs.ChangedButton != MouseButton.Left)
            return;

        _isDraggingVerticalThumb = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void HandlePointerDown(object sender, RoutedEventArgs e)
    {
        if (e is PointerDownEventArgs pointerArgs)
        {
            OnPointerDown(pointerArgs);
        }
    }

    private void HandlePointerMove(object sender, RoutedEventArgs e)
    {
        if (e is PointerMoveEventArgs pointerArgs)
        {
            OnPointerMove(pointerArgs);
        }
    }

    private void HandlePointerUp(object sender, RoutedEventArgs e)
    {
        if (e is PointerUpEventArgs pointerArgs)
        {
            OnPointerUp(pointerArgs);
        }
    }

    private void HandlePointerCancel(object sender, RoutedEventArgs e)
    {
        if (e is PointerCancelEventArgs pointerArgs)
        {
            OnPointerCancel(pointerArgs);
        }
    }

    private void OnScrollBarMouseEnter(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _verticalScrollBar) && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        RevealAutoHideScrollBarsTemporarily();
    }

    private void OnScrollBarMouseLeave(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _verticalScrollBar) && !ReferenceEquals(sender, _horizontalScrollBar))
            return;

        // Keep WinUI-like timing: leaving the bar starts the idle countdown,
        // then slim mode is applied when the auto-hide timer elapses.
        RestartScrollBarAutoHideTimer();
    }

    private void OnPointerDown(PointerDownEventArgs e)
    {
        if (!CanStartPointerPanning(e.Pointer))
            return;

        CancelSmoothScroll();

        _isPointerPanningActive = true;
        _hasPointerPanningMoved = false;
        _activePanningPointerId = e.Pointer.PointerId;
        _pointerPanningStartPoint = e.Pointer.Position;
        _pointerPanningLastPoint = e.Pointer.Position;
        _pointerPanningLastTimestamp = e.Timestamp;
        _pointerPanningVelocityX = 0;
        _pointerPanningVelocityY = 0;

        InitializePointerPanningAxes();
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

        bool moved = ApplyPointerPanningDelta(horizontalDelta, verticalDelta);
        if (moved)
        {
            _hasPointerPanningMoved = true;
            double blend = 0.35;
            double instantVelocityX = deltaX / dt;
            double instantVelocityY = deltaY / dt;
            _pointerPanningVelocityX = (_pointerPanningVelocityX * (1 - blend)) + (instantVelocityX * blend);
            _pointerPanningVelocityY = (_pointerPanningVelocityY * (1 - blend)) + (instantVelocityY * blend);
            e.Handled = true;
        }

        _pointerPanningLastPoint = currentPoint;
        _pointerPanningLastTimestamp = currentTimestamp;
    }

    private void OnPointerUp(PointerUpEventArgs e)
    {
        if (!_isPointerPanningActive || e.Pointer.PointerId != _activePanningPointerId)
            return;

        if (_hasPointerPanningMoved)
        {
            StartPointerPanningInertia();
            e.Handled = true;
        }

        ResetPointerPanningState();
    }

    private void OnPointerCancel(PointerCancelEventArgs e)
    {
        bool isActivePointer = _isPointerPanningActive && e.Pointer.PointerId == _activePanningPointerId;
        if (!isActivePointer && !_isSmoothScrolling)
            return;

        CancelSmoothScroll();
        ResetPointerPanningState();
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
                _pointerPanningAllowHorizontal = true;
                _pointerPanningAllowVertical = false;
                break;
            case PanningMode.VerticalOnly:
                _pointerPanningAllowHorizontal = false;
                _pointerPanningAllowVertical = true;
                break;
            case PanningMode.Both:
                _pointerPanningAllowHorizontal = true;
                _pointerPanningAllowVertical = true;
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
            _pointerPanningAllowHorizontal = chooseHorizontal;
            _pointerPanningAllowVertical = !chooseHorizontal;
        }
        else
        {
            bool chooseVertical = absY >= absX || absX < PointerPanningLockThreshold;
            _pointerPanningAllowHorizontal = !chooseVertical;
            _pointerPanningAllowVertical = chooseVertical;
        }

        _pointerPanningAxisResolved = true;
    }

    private bool ApplyPointerPanningDelta(double horizontalDelta, double verticalDelta)
    {
        bool moved = false;

        if (_pointerPanningAllowHorizontal && CanScrollHorizontally && Math.Abs(horizontalDelta) > double.Epsilon)
        {
            double newOffset = Math.Clamp(_horizontalOffset + horizontalDelta, 0, ScrollableWidth);
            if (!AreClose(newOffset, _horizontalOffset))
            {
                ScrollToHorizontalOffset(newOffset);
                moved = true;
            }
        }

        if (_pointerPanningAllowVertical && CanScrollVertically && Math.Abs(verticalDelta) > double.Epsilon)
        {
            double newOffset = Math.Clamp(_verticalOffset + verticalDelta, 0, ScrollableHeight);
            if (!AreClose(newOffset, _verticalOffset))
            {
                ScrollToVerticalOffset(newOffset);
                moved = true;
            }
        }

        return moved;
    }

    private void StartPointerPanningInertia()
    {
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
        var trackHeight = Math.Max(0, RenderSize.Height - (InputScrollButtonSize * 2));
        if (trackHeight <= 0 || ExtentHeight <= 0 || ScrollableHeight <= 0)
            return (0, 0, 0);

        var thumbHeight = Math.Max(20, (ViewportHeight / ExtentHeight) * trackHeight);
        var scrollRange = Math.Max(0, trackHeight - thumbHeight);
        var thumbTop = InputScrollButtonSize + (VerticalOffset / ScrollableHeight) * scrollRange;
        return (thumbTop, thumbHeight, scrollRange);
    }

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        return base.HitTestCore(point);
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

        _areAutoHideScrollBarsRevealed = true;
        ApplyScrollBarAutoHideVisualState();
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

        return scrollBar.IsMouseOver;
    }

    private void RestartScrollBarAutoHideTimer()
    {
        if (!IsScrollBarAutoHideEnabled || !HasAutoHideScrollBarCandidates())
            return;

        _scrollBarAutoHideDeadlineTick = Environment.TickCount64 + (long)DefaultScrollBarAutoHideDelayMs;

        _scrollBarAutoHideTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _scrollBarAutoHideTimer.Tick -= OnScrollBarAutoHideTimerTick;
        _scrollBarAutoHideTimer.Tick += OnScrollBarAutoHideTimerTick;

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
            _scrollBarAutoHideDeadlineTick = Environment.TickCount64 + (long)DefaultScrollBarAutoHideDelayMs;
            return;
        }

        _areAutoHideScrollBarsRevealed = false;
        StopScrollBarAutoHideTimer();
        ApplyScrollBarAutoHideVisualState();
    }

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

            return;
        }

        if (scrollBar.Visibility != Visibility.Visible)
        {
            scrollBar.Visibility = Visibility.Visible;
        }

        bool keepExpanded = _areAutoHideScrollBarsRevealed || ShouldKeepAutoHideScrollBarVisible(scrollBar);
        bool shouldUseSlimThumb = !keepExpanded;
        if (scrollBar.IsThumbSlim != shouldUseSlimThumb)
        {
            scrollBar.IsThumbSlim = shouldUseSlimThumb;
        }
    }

    private static bool SupportsAutoHide(ScrollBarVisibility visibilityMode)
    {
        return visibilityMode == ScrollBarVisibility.Auto;
    }

    /// <summary>
    /// Returns a clip geometry for the viewport area.
    /// Respects the ClipToBounds property - when false, no clipping is applied.
    /// </summary>
    protected internal override object? GetLayoutClip()
    {
        if (!ClipToBounds)
        {
            return null;
        }

        // Keep the full control unclipped so visual-child scrollbars remain visible.
        // Content clipping is handled by layout offsets and child layering.
        var clipRect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        return new Media.RectangleGeometry(clipRect);
    }

    #endregion

    #region Scroll Methods

    /// <summary>
    /// Scrolls to the specified horizontal offset.
    /// </summary>
    /// <param name="offset">The horizontal offset.</param>
    public void ScrollToHorizontalOffset(double offset)
    {
        if (!_isApplyingSmoothScrollStep)
        {
            CancelSmoothScroll();
        }

        if (_scrollInfo != null)
        {
            var oldOffset = _scrollInfo.HorizontalOffset;
            _scrollInfo.SetHorizontalOffset(offset);
            _horizontalOffset = _scrollInfo.HorizontalOffset;

            if (oldOffset != _horizontalOffset)
            {
                InvalidateArrange();
                RaiseScrollChanged(oldOffset, _verticalOffset);
                UpdateScrollBarMetrics();
            }
            return;
        }

        var oldOff = _horizontalOffset;
        _horizontalOffset = Math.Clamp(offset, 0, ScrollableWidth);

        if (oldOff != _horizontalOffset)
        {
            InvalidateArrange();
            RaiseScrollChanged(oldOff, _verticalOffset);
            UpdateScrollBarMetrics();
        }
    }

    /// <summary>
    /// Scrolls to the specified vertical offset.
    /// </summary>
    /// <param name="offset">The vertical offset.</param>
    public void ScrollToVerticalOffset(double offset)
    {
        if (!_isApplyingSmoothScrollStep)
        {
            CancelSmoothScroll();
        }

        if (_scrollInfo != null)
        {
            var oldOffset = _scrollInfo.VerticalOffset;
            _scrollInfo.SetVerticalOffset(offset);
            _verticalOffset = _scrollInfo.VerticalOffset;

            if (oldOffset != _verticalOffset)
            {
                InvalidateArrange();
                RaiseScrollChanged(_horizontalOffset, oldOffset);
                UpdateScrollBarMetrics();
            }
            return;
        }

        var oldOff = _verticalOffset;
        _verticalOffset = Math.Clamp(offset, 0, ScrollableHeight);

        if (oldOff != _verticalOffset)
        {
            InvalidateArrange();
            RaiseScrollChanged(_horizontalOffset, oldOff);
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

        _horizontalOffset = _scrollInfo.HorizontalOffset;
        _verticalOffset = _scrollInfo.VerticalOffset;
        _extentWidth = _scrollInfo.ExtentWidth;
        _extentHeight = _scrollInfo.ExtentHeight;
        // Note: Do NOT sync _viewportWidth/_viewportHeight here; those are authoritatively
        // computed in ArrangeOverride based on finalSize and scrollbar visibility.
        // The IScrollInfo.ViewportWidth/Height reflects the Measure constraint, which can
        // differ from the final Arrange size and cause scrollbar visibility to flicker.

        if (oldHorizontalOffset != _horizontalOffset || oldVerticalOffset != _verticalOffset)
        {
            InvalidateArrange();
            RaiseScrollChanged(oldHorizontalOffset, oldVerticalOffset);
            UpdateScrollBarMetrics();
        }
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
        if (element == null || _content == null)
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
        if (element == null || _content == null)
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
        if (_content == null)
            return null;

        // Walk up the visual tree from the element to find the content root
        double x = 0;
        double y = 0;

        Visual? current = element;
        while (current != null)
        {
            if (current == _content)
            {
                // We've reached the content - return the accumulated offset
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
        if (_content == null)
        {
            _extentWidth = 0;
            _extentHeight = 0;
            _viewportWidth = 0;
            _viewportHeight = 0;
            _verticalScrollBar.Measure(Size.Empty);
            _horizontalScrollBar.Measure(Size.Empty);
            return Size.Empty;
        }

        // Reserve space for scrollbars if they might be needed
        var reserveVertical = VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                              VerticalScrollBarVisibility == ScrollBarVisibility.Auto;
        var reserveHorizontal = HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                HorizontalScrollBarVisibility == ScrollBarVisibility.Auto;

        // Calculate available space for content (accounting for potential scrollbars)
        var contentAvailableWidth = availableSize.Width - (reserveVertical ? ScrollBarSize : 0);
        var contentAvailableHeight = availableSize.Height - (reserveHorizontal ? ScrollBarSize : 0);

        // If scrolling is enabled in a direction, give infinite space in that direction
        // When using IScrollInfo, pass the viewport size so the content can manage its own scrolling
        var measureWidth = HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && _scrollInfo == null
            ? double.PositiveInfinity
            : contentAvailableWidth;

        var measureHeight = VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && _scrollInfo == null
            ? double.PositiveInfinity
            : contentAvailableHeight;

        var contentAvailable = new Size(measureWidth, measureHeight);

        // Measure content
        _content.Measure(contentAvailable);
        var contentDesired = _content.DesiredSize;

        // Update extent from IScrollInfo or from content desired size
        if (_scrollInfo != null)
        {
            _extentWidth = _scrollInfo.ExtentWidth;
            _extentHeight = _scrollInfo.ExtentHeight;
        }
        else
        {
            _extentWidth = contentDesired.Width;
            _extentHeight = contentDesired.Height;
        }

        // Return the smaller of content size and available size
        var resultWidth = Math.Min(contentDesired.Width + (reserveVertical ? ScrollBarSize : 0), availableSize.Width);
        var resultHeight = Math.Min(contentDesired.Height + (reserveHorizontal ? ScrollBarSize : 0), availableSize.Height);

        _verticalScrollBar.Measure(new Size(ScrollBarSize, Math.Max(0, availableSize.Height)));
        _horizontalScrollBar.Measure(new Size(Math.Max(0, availableSize.Width), ScrollBarSize));

        return new Size(resultWidth, resultHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Sync extent/offset from IScrollInfo FIRST so scrollbar visibility uses fresh values
        if (_scrollInfo != null)
        {
            _horizontalOffset = _scrollInfo.HorizontalOffset;
            _verticalOffset = _scrollInfo.VerticalOffset;
            _extentWidth = _scrollInfo.ExtentWidth;
            _extentHeight = _scrollInfo.ExtentHeight;
        }

        // Calculate if scrollbars are needed (now using up-to-date extent values)
        var needsVerticalScrollBar = VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                      (VerticalScrollBarVisibility == ScrollBarVisibility.Auto && _extentHeight > finalSize.Height);
        var needsHorizontalScrollBar = HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                        (HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && _extentWidth > finalSize.Width);

        // Calculate viewport size (excluding scrollbar space)
        _viewportWidth = finalSize.Width - (needsVerticalScrollBar ? ScrollBarSize : 0);
        _viewportHeight = finalSize.Height - (needsHorizontalScrollBar ? ScrollBarSize : 0);

        if (_scrollInfo == null)
        {
            // Clamp scroll offsets
            _horizontalOffset = Math.Clamp(_horizontalOffset, 0, Math.Max(0, _extentWidth - _viewportWidth));
            _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, _extentHeight - _viewportHeight));
        }

        if (_content != null)
        {
            if (_scrollInfo != null)
            {
                // IScrollInfo manages its own scrolling; arrange at full viewport size
                var arrangeRect = new Rect(0, 0, _viewportWidth, _viewportHeight);
                _content.Arrange(arrangeRect);
            }
            else
            {
                // Arrange content with offset (content area excludes scrollbar space)
                var contentWidth = Math.Max(_extentWidth, _viewportWidth);
                var contentHeight = Math.Max(_extentHeight, _viewportHeight);

                var arrangeRect = new Rect(
                    -_horizontalOffset,
                    -_verticalOffset,
                    contentWidth,
                    contentHeight);

                _content.Arrange(arrangeRect);
            }
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        UpdateScrollBarMetrics();

        if (needsVerticalScrollBar)
        {
            _verticalScrollBar.Visibility = Visibility.Visible;
            _verticalScrollBar.Arrange(new Rect(
                Math.Max(0, finalSize.Width - ScrollBarSize),
                0,
                ScrollBarSize,
                Math.Max(0, _viewportHeight)));
        }
        else
        {
            _verticalScrollBar.Visibility = Visibility.Collapsed;
            _verticalScrollBar.Arrange(Rect.Empty);
        }

        if (needsHorizontalScrollBar)
        {
            _horizontalScrollBar.Visibility = Visibility.Visible;
            _horizontalScrollBar.Arrange(new Rect(
                0,
                Math.Max(0, finalSize.Height - ScrollBarSize),
                Math.Max(0, _viewportWidth),
                ScrollBarSize));
        }
        else
        {
            _horizontalScrollBar.Visibility = Visibility.Collapsed;
            _horizontalScrollBar.Arrange(Rect.Empty);
        }

        ApplyScrollBarAutoHideVisualState();

        return finalSize;
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount => _content != null ? 3 : 2;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (_content != null)
        {
            return index switch
            {
                0 => _content,
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

    private void AddContentChild(UIElement child)
    {
        if (child is Visual visual)
        {
            AddVisualChild(visual);
        }
    }

    private void RemoveContentChild(UIElement child)
    {
        if (child is Visual visual)
        {
            RemoveVisualChild(visual);
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.Left > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            dc.DrawRectangle(null, pen, bounds);
        }

        // Content and scrollbars render through the visual tree.
    }

    #endregion

    #region Input Handling

    /// <summary>
    /// Handles mouse wheel events for scrolling.
    /// </summary>
    protected void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        bool useSmoothWheelInertia = IsScrollInertiaEnabled && GetEffectiveScrollInertiaDurationMs() > 0;

        // Delegate to IScrollInfo if available
        if (_scrollInfo != null)
        {
            SyncFromScrollInfo();

            if (useSmoothWheelInertia)
            {
                // Smooth animated scroll through IScrollInfo
                var scrollLines = GetSystemWheelScrollLines();
                var notches = -e.Delta / 120.0;
                var delta = scrollLines == WHEEL_PAGESCROLL
                    ? notches * _viewportHeight
                    : notches * scrollLines * LineScrollAmount;

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

        var scrollLines2 = GetSystemWheelScrollLines();
        var notches2 = -e.Delta / 120.0;

        if (CanScrollVertically)
        {
            // Only consume the event if we can actually scroll in the requested direction.
            // This allows nested ScrollViewers to bubble the event to the parent when at bounds.
            bool atTop = _verticalOffset <= 0;
            bool atBottom = _verticalOffset >= ScrollableHeight;
            bool scrollingUp = notches2 < 0;
            bool scrollingDown = notches2 > 0;

            if ((scrollingUp && atTop) || (scrollingDown && atBottom))
            {
                // At boundary: don't handle, let parent ScrollViewer process it
            }
            else
            {
                var delta = scrollLines2 == WHEEL_PAGESCROLL
                    ? notches2 * _viewportHeight
                    : notches2 * scrollLines2 * LineScrollAmount;

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
            bool scrollingLeft = notches2 < 0;
            bool scrollingRight = notches2 > 0;

            if ((scrollingLeft && atLeft) || (scrollingRight && atRight))
            {
                // At boundary: don't handle, let parent ScrollViewer process it
            }
            else
            {
                var delta = scrollLines2 == WHEEL_PAGESCROLL
                    ? notches2 * _viewportWidth
                    : notches2 * scrollLines2 * LineScrollAmount;

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
        if (GetEffectiveScrollInertiaDurationMs() <= 0)
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
    protected void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
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

        var sourceScrollBar = sender as ScrollBar;
        var fromScrollBarWheel = sourceScrollBar?.IsWheelScrollingInput == true;
        var useSmoothWheelInertia = fromScrollBarWheel &&
                                    IsScrollInertiaEnabled &&
                                    GetEffectiveScrollInertiaDurationMs() > 0;

        if (useSmoothWheelInertia)
        {
            // Keep thumb/content visually aligned with current offset, then animate toward new target.
            _isUpdatingScrollBars = true;
            try
            {
                if (isVertical)
                {
                    _verticalScrollBar.Value = _verticalOffset;
                }
                else
                {
                    _horizontalScrollBar.Value = _horizontalOffset;
                }
            }
            finally
            {
                _isUpdatingScrollBars = false;
            }

            if (!_isSmoothScrolling)
            {
                _smoothTargetX = _horizontalOffset;
                _smoothTargetY = _verticalOffset;
            }

            if (isVertical)
            {
                _smoothTargetY = Math.Clamp(e.NewValue, 0, Math.Max(0, ScrollableHeight));
            }
            else
            {
                _smoothTargetX = Math.Clamp(e.NewValue, 0, Math.Max(0, ScrollableWidth));
            }

            StartSmoothScroll();
            return;
        }

        // ScrollBar thumb drag/page click/line click should remain immediate.
        // Cancel wheel inertia so direct interactions do not compete with animation.
        CancelSmoothScroll();

        if (isVertical)
        {
            HandleScrollBarValueChange(
                e,
                ScrollableHeight,
                value => _deferredVerticalOffset = value,
                () => _deferredVerticalOffset,
                ScrollToVerticalOffset);
        }
        else
        {
            HandleScrollBarValueChange(
                e,
                ScrollableWidth,
                value => _deferredHorizontalOffset = value,
                () => _deferredHorizontalOffset,
                ScrollToHorizontalOffset);
        }
    }

    private void HandleScrollBarValueChange(
        ScrollEventArgs e,
        double maxValue,
        Action<double> setDeferredValue,
        Func<double> getDeferredValue,
        Action<double> applyValue)
    {
        var clampedValue = Math.Clamp(e.NewValue, 0, Math.Max(0, maxValue));

        if (IsDeferredScrollingEnabled && e.ScrollEventType == ScrollEventType.ThumbTrack)
        {
            _isDeferredScrolling = true;
            setDeferredValue(clampedValue);
            return;
        }

        if (IsDeferredScrollingEnabled && e.ScrollEventType == ScrollEventType.EndScroll && _isDeferredScrolling)
        {
            applyValue(getDeferredValue());
            _isDeferredScrolling = false;
            return;
        }

        _isDeferredScrolling = false;
        applyValue(clampedValue);
    }

    private void UpdateScrollBarMetrics()
    {
        _isUpdatingScrollBars = true;
        try
        {
            ConfigureScrollBar(
                _verticalScrollBar,
                ScrollableHeight,
                _viewportHeight,
                _verticalOffset,
                VerticalScrollBarVisibility,
                canScroll: CanScrollVertically);

            ConfigureScrollBar(
                _horizontalScrollBar,
                ScrollableWidth,
                _viewportWidth,
                _horizontalOffset,
                HorizontalScrollBarVisibility,
                canScroll: CanScrollHorizontally);
        }
        finally
        {
            _isUpdatingScrollBars = false;
        }

        ApplyScrollBarAutoHideVisualState();
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

        if (scrollViewer.GetEffectiveScrollInertiaDurationMs() > 0 || !scrollViewer._isSmoothScrolling)
            return;

        scrollViewer._isApplyingSmoothScrollStep = true;
        try
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer._smoothTargetY);
            scrollViewer.ScrollToHorizontalOffset(scrollViewer._smoothTargetX);
        }
        finally
        {
            scrollViewer._isApplyingSmoothScrollStep = false;
        }

        scrollViewer.StopSmoothScroll();
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

    private void RaiseScrollChanged(double oldHorizontalOffset, double oldVerticalOffset)
    {
        var e = new ScrollChangedEventArgs(ScrollChangedEvent, this)
        {
            HorizontalChange = _horizontalOffset - oldHorizontalOffset,
            VerticalChange = _verticalOffset - oldVerticalOffset,
            HorizontalOffset = _horizontalOffset,
            VerticalOffset = _verticalOffset,
            ViewportWidth = _viewportWidth,
            ViewportHeight = _viewportHeight,
            ExtentWidth = _extentWidth,
            ExtentHeight = _extentHeight
        };

        RaiseEvent(e);
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
            scrollViewer._areAutoHideScrollBarsRevealed = true;
            scrollViewer.StopScrollBarAutoHideTimer();
        }
        else
        {
            scrollViewer._areAutoHideScrollBarsRevealed = false;
            scrollViewer.RestartScrollBarAutoHideTimer();
        }

        scrollViewer.ApplyScrollBarAutoHideVisualState();
    }

    private static uint GetSystemWheelScrollLines()
    {
        if (SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, out uint lines, 0))
            return lines;
        return 3;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);

    #endregion
}

/// <summary>
/// Provides data for the ScrollChanged event.
/// </summary>
public sealed class ScrollChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the horizontal offset change.
    /// </summary>
    public double HorizontalChange { get; init; }

    /// <summary>
    /// Gets the vertical offset change.
    /// </summary>
    public double VerticalChange { get; init; }

    /// <summary>
    /// Gets the current horizontal offset.
    /// </summary>
    public double HorizontalOffset { get; init; }

    /// <summary>
    /// Gets the current vertical offset.
    /// </summary>
    public double VerticalOffset { get; init; }

    /// <summary>
    /// Gets the viewport width.
    /// </summary>
    public double ViewportWidth { get; init; }

    /// <summary>
    /// Gets the viewport height.
    /// </summary>
    public double ViewportHeight { get; init; }

    /// <summary>
    /// Gets the extent width.
    /// </summary>
    public double ExtentWidth { get; init; }

    /// <summary>
    /// Gets the extent height.
    /// </summary>
    public double ExtentHeight { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollChangedEventArgs"/> class.
    /// </summary>
    public ScrollChangedEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
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
