using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Input.Internal.Gestures;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that provides a scroll bar for scrolling content.
/// </summary>
public class ScrollBar : RangeBase
{
    public static readonly RoutedCommand LineUpCommand = new("LineUp", typeof(ScrollBar));
    public static readonly RoutedCommand LineDownCommand = new("LineDown", typeof(ScrollBar));
    public static readonly RoutedCommand LineLeftCommand = new("LineLeft", typeof(ScrollBar));
    public static readonly RoutedCommand LineRightCommand = new("LineRight", typeof(ScrollBar));
    public static readonly RoutedCommand PageUpCommand = new("PageUp", typeof(ScrollBar));
    public static readonly RoutedCommand PageDownCommand = new("PageDown", typeof(ScrollBar));
    public static readonly RoutedCommand PageLeftCommand = new("PageLeft", typeof(ScrollBar));
    public static readonly RoutedCommand PageRightCommand = new("PageRight", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToEndCommand = new("ScrollToEnd", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToHomeCommand = new("ScrollToHome", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToRightEndCommand = new("ScrollToRightEnd", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToLeftEndCommand = new("ScrollToLeftEnd", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToTopCommand = new("ScrollToTop", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToBottomCommand = new("ScrollToBottom", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToHorizontalOffsetCommand = new("ScrollToHorizontalOffset", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollToVerticalOffsetCommand = new("ScrollToVerticalOffset", typeof(ScrollBar));
    public static readonly RoutedCommand DeferScrollToHorizontalOffsetCommand = new("DeferScrollToToHorizontalOffset", typeof(ScrollBar));
    public static readonly RoutedCommand DeferScrollToVerticalOffsetCommand = new("DeferScrollToVerticalOffset", typeof(ScrollBar));
    public static readonly RoutedCommand ScrollHereCommand = new("ScrollHere", typeof(ScrollBar));

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.ScrollBarAutomationPeer(this);

    #region Static Brushes

    private static readonly SolidColorBrush s_defaultTrackBrush = new(ThemeColors.ScrollBarTrack);
    private static readonly SolidColorBrush s_defaultThumbBrush = new(Color.FromRgb(170, 170, 170));
    private static readonly SolidColorBrush s_defaultArrowBrush = new(Color.FromRgb(210, 210, 210));
    private static readonly SolidColorBrush s_transparentBrush = new(Color.Transparent);
    private static readonly BackdropBlurEffect s_defaultTrackBackdropEffect = new(0f, BackdropBlurType.Gaussian);
    private static readonly Style s_internalRepeatButtonStyle = new(typeof(RepeatButton));
    private static readonly Style s_internalThumbStyle = CreateInternalThumbStyle();

    #endregion

    // Cached pens
    private Pen? _borderPen;
    private Brush? _borderPenBrush;
    private double _borderPenThickness;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ScrollBar),
            new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ViewportSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ViewportSizeProperty =
        DependencyProperty.Register(nameof(ViewportSize), typeof(double), typeof(ScrollBar),
            new PropertyMetadata(0.0, OnViewportSizeChanged));

    /// <summary>
    /// Identifies the ThumbStyle dependency property.
    /// Allows ScrollBar themes to directly inject a keyed thumb style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ThumbStyleProperty =
        DependencyProperty.Register(nameof(ThumbStyle), typeof(Style), typeof(ScrollBar),
            new PropertyMetadata(null, OnPartStylePropertyChanged));

    /// <summary>
    /// Identifies the IsThumbSlim dependency property.
    /// When true, the Track renders the thumb as a thin line centered in the track.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsThumbSlimProperty =
        DependencyProperty.Register(nameof(IsThumbSlim), typeof(bool), typeof(ScrollBar),
            new PropertyMetadata(false, OnThumbPresentationPropertyChanged));

    /// <summary>
    /// Identifies the IsOverlayStyle dependency property.
    /// Overlay scroll bars use a compact edge indicator without track chrome or
    /// line buttons. The default is enabled on mobile operating systems.
    /// </summary>
    internal static readonly DependencyProperty IsOverlayStyleProperty =
        DependencyProperty.Register(nameof(IsOverlayStyle), typeof(bool), typeof(ScrollBar),
            new PropertyMetadata(
                OperatingSystem.IsAndroid() || OperatingSystem.IsIOS(),
                OnOverlayStyleChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Scroll routed event.
    /// </summary>
    public static readonly RoutedEvent ScrollEvent =
        EventManager.RegisterRoutedEvent(nameof(Scroll), RoutingStrategy.Bubble,
            typeof(ScrollEventHandler), typeof(ScrollBar));

    /// <summary>
    /// Occurs when the Scroll event is raised.
    /// </summary>
    public event ScrollEventHandler Scroll
    {
        add => AddHandler(ScrollEvent, value);
        remove => RemoveHandler(ScrollEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of the ScrollBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the viewport, which determines the thumb size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ViewportSize
    {
        get => (double)GetValue(ViewportSizeProperty)!;
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the style applied to the internal Track thumb.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Style? ThumbStyle
    {
        get => (Style?)GetValue(ThumbStyleProperty);
        set => SetValue(ThumbStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the thumb should render in slim mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsThumbSlim
    {
        get => (bool)GetValue(IsThumbSlimProperty)!;
        set => SetValue(IsThumbSlimProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this scroll bar uses the compact mobile overlay style.
    /// </summary>
    internal bool IsOverlayStyle
    {
        get => (bool)GetValue(IsOverlayStyleProperty)!;
        set => SetValue(IsOverlayStyleProperty, value);
    }

    /// <summary>Gets the track that owns the thumb and page buttons.</summary>
    public Track Track => _track!;

    internal bool IsThumbDragging => _isDragging;

    #endregion

    #region Private Fields

    private Track? _track;
    private RepeatButton? _lineUpButton;
    private RepeatButton? _lineDownButton;
    private const double DefaultThickness = 16;
    private const double MinThumbLength = 20;
    private bool _isDragging;
    private double _thumbDragStartValue;
    private double _thumbDragAccumulatedHorizontal;
    private double _thumbDragAccumulatedVertical;
    // Track-press paging ("click-and-hold in the trough"): while a page RepeatButton is held,
    // each auto-repeat page step is clamped so the thumb stops once it reaches the pointer
    // instead of running all the way to Minimum/Maximum. The target value is seeded on the
    // press and kept live on every mouse move so the thumb follows the cursor while held.
    private bool _hasTrackPagingTarget;
    private double _trackPagingTargetValue;
    private bool _hasCustomLineButtonStyle;
    private bool _hasOverlayThumbPaddingSnapshot;
    private bool _overlayThumbHadLocalPadding;
    private Thickness _overlayThumbLocalPadding;
    private bool _hasOverlayTrackPresentationSnapshot;
    private bool _overlayTrackHadLocalOpacity;
    private double _overlayTrackLocalOpacity;
    private bool _overlayTrackHadLocalHitTestVisibility;
    private bool _overlayTrackLocalHitTestVisibility;
    private GestureRecognizer? _overlayThumbGestureRecognizer;
    private int _overlayThumbTouchId = -1;
    private bool _isOverlayThumbTouchDragUnlocked;
    private bool _isOverlayLongPressExpanded;
    private DispatcherTimer? _autoHideVisualTimer;
    private long _autoHideVisualAnimStartTick;
    private double _autoHideVisualAnimFrom;
    private double _autoHideVisualAnimTo;
    private double _autoHideCollapseProgress;
    private double _chromeOpacity = 1.0;
    private const string ScrollBarStyleKey = "ScrollBarStyle";
    private const string LineButtonStyleKey = "ScrollBarLineButtonStyle";
    private const string PageButtonStyleKey = "ScrollBarPageButtonStyle";
    private const string ThumbStyleKey = "ScrollBarThumbStyle";
    private const string TrackBrushKey = "ScrollBarTrack";
    private const string ThumbBrushKey = "ScrollBarThumb";
    private const string ArrowBrushKey = "ScrollBarArrow";
    private const double SlimThumbThickness = 2.0;
    private const double ExpandedThumbInset = 4.0;
    private const double OverlayIndicatorThickness = 2.0;
    private const double OverlayExpandedIndicatorThickness = 8.0;
    private const double OverlayIndicatorEdgeInset = 2.0;
    private const double OverlayTrackEndInset = 3.0;
    private const double OverlayMinThumbLength = 40.0;
    // Smallest diameter the thumb is allowed to collapse to when content is huge and it bottoms
    // out as a round dot. Keeps the dot visible/grabbable on very thin scroll bars where the
    // expanded cross-axis thickness would otherwise drop below this.
    private const double MinThumbDotDiameter = 8.0;
    private const double AutoHideVisualTransitionDurationMs = 160.0;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollBar"/> class.
    /// </summary>
    public ScrollBar()
    {
        // Set default values for range base
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        Maximum = 100;
        SmallChange = 1;
        LargeChange = 10;
        BorderBrush = s_transparentBrush;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(2);

        // Create visual children
        CreateVisualChildren();

        // Register event handlers.
        // MouseWheel is intentionally NOT handled here: when a ScrollBar is hosted inside a
        // ScrollViewer, the wheel event should bubble up so that ScrollViewer.OnMouseWheel
        // runs the same accumulation-based smooth scroll logic as when the pointer is over
        // the content area. Handling the wheel directly on ScrollBar (and raising a Scroll
        // event with a precomputed NewValue) caused the scrollbar-track scrolling speed to
        // lag behind content-area scrolling when the user scrolled the wheel rapidly,
        // because the Scroll-event path assigned the smooth target instead of accumulating it.
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        ResourcesChanged += OnResourcesChangedHandler;
        RegisterScrollCommandBindings();

        _autoHideCollapseProgress = IsThumbSlim ? 1.0 : 0.0;
        ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);
    }

    private void RegisterScrollCommandBindings()
    {
        AddScrollCommand(LineUpCommand, static bar => bar.ChangeBy(-bar.SmallChange, ScrollEventType.SmallDecrement));
        AddScrollCommand(LineLeftCommand, static bar => bar.ChangeBy(-bar.SmallChange, ScrollEventType.SmallDecrement));
        AddScrollCommand(LineDownCommand, static bar => bar.ChangeBy(bar.SmallChange, ScrollEventType.SmallIncrement));
        AddScrollCommand(LineRightCommand, static bar => bar.ChangeBy(bar.SmallChange, ScrollEventType.SmallIncrement));
        AddScrollCommand(PageUpCommand, static bar => bar.ChangeBy(-bar.LargeChange, ScrollEventType.LargeDecrement));
        AddScrollCommand(PageLeftCommand, static bar => bar.ChangeBy(-bar.LargeChange, ScrollEventType.LargeDecrement));
        AddScrollCommand(PageDownCommand, static bar => bar.ChangeBy(bar.LargeChange, ScrollEventType.LargeIncrement));
        AddScrollCommand(PageRightCommand, static bar => bar.ChangeBy(bar.LargeChange, ScrollEventType.LargeIncrement));
        AddScrollCommand(ScrollToHomeCommand, static bar => bar.ChangeTo(bar.Minimum, ScrollEventType.First));
        AddScrollCommand(ScrollToLeftEndCommand, static bar => bar.ChangeTo(bar.Minimum, ScrollEventType.First));
        AddScrollCommand(ScrollToTopCommand, static bar => bar.ChangeTo(bar.Minimum, ScrollEventType.First));
        AddScrollCommand(ScrollToEndCommand, static bar => bar.ChangeTo(bar.Maximum, ScrollEventType.Last));
        AddScrollCommand(ScrollToRightEndCommand, static bar => bar.ChangeTo(bar.Maximum, ScrollEventType.Last));
        AddScrollCommand(ScrollToBottomCommand, static bar => bar.ChangeTo(bar.Maximum, ScrollEventType.Last));
        AddOffsetCommand(ScrollToHorizontalOffsetCommand, deferred: false);
        AddOffsetCommand(ScrollToVerticalOffsetCommand, deferred: false);
        AddOffsetCommand(DeferScrollToHorizontalOffsetCommand, deferred: true);
        AddOffsetCommand(DeferScrollToVerticalOffsetCommand, deferred: true);
        AddOffsetCommand(ScrollHereCommand, deferred: false);
    }

    private void AddScrollCommand(RoutedCommand command, Action<ScrollBar> action)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            (_, e) =>
            {
                action(this);
                e.Handled = true;
            },
            (_, e) =>
            {
                e.CanExecute = IsEnabled;
                e.Handled = true;
            }));
    }

    private void AddOffsetCommand(RoutedCommand command, bool deferred)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            (_, e) =>
            {
                if (TryConvertOffset(e.Parameter, out double offset))
                {
                    ChangeTo(offset, deferred ? ScrollEventType.ThumbTrack : ScrollEventType.ThumbPosition);
                }
                e.Handled = true;
            },
            (_, e) =>
            {
                e.CanExecute = IsEnabled && TryConvertOffset(e.Parameter, out double ignoredOffset);
                e.Handled = true;
            }));
    }

    private void ChangeBy(double delta, ScrollEventType eventType) => ChangeTo(Value + delta, eventType);

    private void ChangeTo(double requestedValue, ScrollEventType eventType)
    {
        double value = Math.Clamp(requestedValue, Minimum, Maximum);
        if (Math.Abs(value - Value) <= double.Epsilon)
        {
            return;
        }

        Value = value;
        RaiseScrollEvent(eventType);
    }

    private static bool TryConvertOffset(object? parameter, out double offset)
    {
        switch (parameter)
        {
            case double number when double.IsFinite(number):
                offset = number;
                return true;
            case IConvertible convertible:
                try
                {
                    offset = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                    return double.IsFinite(offset);
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    break;
                }
        }

        offset = 0;
        return false;
    }

    private void CreateVisualChildren()
    {
        // Create line up/left button
        _lineUpButton = new RepeatButton
        {
            Style = s_internalRepeatButtonStyle,
            Focusable = false,
            TransitionProperty = "None",
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _lineUpButton.Cursor = Jalium.UI.Input.Cursors.Arrow;
        _lineUpButton.Click += OnLineUpClick;
        AddVisualChild(_lineUpButton);

        // Create track
        _track = new Track
        {
            // ScrollBar owns thumb dragging so we do not pay for a second Track.Value update
            // and Arrange invalidation on every pointer move.
            HandlesThumbDragInternally = false
        };
        _track.SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        _track.Thumb = new Thumb
        {
            Style = s_internalThumbStyle,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            // Keep scrollbar thumb length controlled by Track.ArrangeOverride.
            // This prevents unrelated implicit Thumb styles (e.g. generic Thumb height)
            // from forcing a fixed square/rect thumb size.
            Width = double.NaN,
            Height = double.NaN
        };
        _track.Thumb.SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        _track.Thumb.Cursor = Jalium.UI.Input.Cursors.Arrow;
        _track.Thumb.DragStarted += OnThumbDragStarted;
        _track.Thumb.DragDelta += OnThumbDragDelta;
        _track.Thumb.DragCompleted += OnThumbDragCompleted;
        _track.Thumb.AddHandler(TouchDownEvent, new RoutedEventHandler(OnOverlayThumbTouchDown), handledEventsToo: true);
        _track.Thumb.AddHandler(TouchMoveEvent, new RoutedEventHandler(OnOverlayThumbTouchMove), handledEventsToo: true);
        _track.Thumb.AddHandler(TouchUpEvent, new RoutedEventHandler(OnOverlayThumbTouchUp), handledEventsToo: true);
        _track.Thumb.AddHandler(LostTouchCaptureEvent, new RoutedEventHandler(OnOverlayThumbLostTouchCapture), handledEventsToo: true);

        _track.DecreaseRepeatButton = new RepeatButton
        {
            Style = s_internalRepeatButtonStyle,
            // Page on press (not release) so the first trough step fires the instant the button
            // goes down, without waiting out the RepeatButton's initial Delay. Auto-repeat still
            // kicks in after Delay via the RepeatButton timer.
            ClickMode = ClickMode.Press,
            Focusable = false,
            TransitionProperty = "None",
            Opacity = 0,
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _track.DecreaseRepeatButton.Cursor = Jalium.UI.Input.Cursors.Arrow;
        _track.DecreaseRepeatButton.Click += OnPageUpClick;
        WirePageButtonTrackPaging(_track.DecreaseRepeatButton);

        _track.IncreaseRepeatButton = new RepeatButton
        {
            Style = s_internalRepeatButtonStyle,
            // Page on press (not release) so the first trough step fires the instant the button
            // goes down, without waiting out the RepeatButton's initial Delay. Auto-repeat still
            // kicks in after Delay via the RepeatButton timer.
            ClickMode = ClickMode.Press,
            Focusable = false,
            TransitionProperty = "None",
            Opacity = 0,
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _track.IncreaseRepeatButton.Cursor = Jalium.UI.Input.Cursors.Arrow;
        _track.IncreaseRepeatButton.Click += OnPageDownClick;
        WirePageButtonTrackPaging(_track.IncreaseRepeatButton);

        AddVisualChild(_track);

        // Create line down/right button
        _lineDownButton = new RepeatButton
        {
            Style = s_internalRepeatButtonStyle,
            Focusable = false,
            TransitionProperty = "None",
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _lineDownButton.Cursor = Jalium.UI.Input.Cursors.Arrow;
        _lineDownButton.Click += OnLineDownClick;
        AddVisualChild(_lineDownButton);

        ApplySelfStyle();
        ApplyPartStyles();
        UpdateLineButtonDirectionTags();
        UpdateTrackBindings();
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent == null)
        {
            StopAutoHideVisualTimer();
            CompleteOverlayThumbGesture();
        }
        ApplySelfStyle();
        ApplyPartStyles();
        ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);
    }

    private void OnResourcesChangedHandler(object? sender, EventArgs e)
    {
        ApplySelfStyle();
        ApplyPartStyles();
        UpdateTrackBindings();
        ApplyAutoHideVisualState(_autoHideCollapseProgress);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void UpdateTrackBindings()
    {
        if (_track != null)
        {
            _track.Minimum = Minimum;
            _track.Maximum = Maximum;
            _track.Value = Value;
            _track.ViewportSize = ViewportSize;
            _track.Orientation = Orientation;
            ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);

            if (_track.Thumb != null)
            {
                // Desktop keeps the extreme-ratio thumb as a round dot. Mobile keeps
                // at least 40 DIPs on the scroll axis as well, producing a genuine
                // finger-sized target while the padded template remains a slim pill.
                var minimumThumbLength = IsOverlayStyle
                    ? OverlayMinThumbLength
                    : ComputeThumbDotDiameter();
                if (Orientation == Orientation.Vertical)
                {
                    _track.Thumb.MinHeight = minimumThumbLength;
                    _track.Thumb.MinWidth = 0;
                }
                else
                {
                    _track.Thumb.MinWidth = minimumThumbLength;
                    _track.Thumb.MinHeight = 0;
                }
            }
        }

        UpdateLineButtonDirectionTags();
    }

    private void ApplyPartStyles()
    {
        var lineButtonStyle = ResolveStyleResource(LineButtonStyleKey);
        _hasCustomLineButtonStyle = lineButtonStyle != null;

        if (_lineUpButton != null)
        {
            if (lineButtonStyle != null && !ReferenceEquals(_lineUpButton.Style, lineButtonStyle))
            {
                ClearLineButtonDefaultsIfNeededForStyle(_lineUpButton);
                _lineUpButton.Style = lineButtonStyle;
            }
        }

        if (_lineDownButton != null)
        {
            if (lineButtonStyle != null && !ReferenceEquals(_lineDownButton.Style, lineButtonStyle))
            {
                ClearLineButtonDefaultsIfNeededForStyle(_lineDownButton);
                _lineDownButton.Style = lineButtonStyle;
            }
        }

        if (_lineUpButton != null && _lineDownButton != null)
        {
            if (_hasCustomLineButtonStyle)
            {
                // Ensure themed line-button visuals are visible when keyed style is available,
                // but only when the scrollbar is expanded.  During collapse the opacity is
                // managed by ApplyAutoHideVisualState and must not be cleared.
                if (_autoHideCollapseProgress <= 0.001)
                {
                    ClearLocalIfValueEquals(_lineUpButton, OpacityProperty, 0.0);
                    ClearLocalIfValueEquals(_lineDownButton, OpacityProperty, 0.0);
                }
            }
            else
            {
                // Prevent default RepeatButton pressed/hover fill (square overlay) from covering fallback arrows.
                _lineUpButton.Opacity = 0;
                _lineDownButton.Opacity = 0;
            }
        }

        var pageButtonStyle = ResolveStyleResource(PageButtonStyleKey);
        if (_track?.DecreaseRepeatButton != null)
        {
            if (pageButtonStyle != null && !ReferenceEquals(_track.DecreaseRepeatButton.Style, pageButtonStyle))
            {
                ClearPageButtonDefaultsIfNeededForStyle(_track.DecreaseRepeatButton);
                _track.DecreaseRepeatButton.Style = pageButtonStyle;
            }
        }

        if (_track?.IncreaseRepeatButton != null)
        {
            if (pageButtonStyle != null && !ReferenceEquals(_track.IncreaseRepeatButton.Style, pageButtonStyle))
            {
                ClearPageButtonDefaultsIfNeededForStyle(_track.IncreaseRepeatButton);
                _track.IncreaseRepeatButton.Style = pageButtonStyle;
            }
        }

        // Prefer the keyed ScrollBar thumb style so the control follows theme XAML directly.
        var thumbStyle = ResolveStyleResource(ThumbStyleKey) ?? ThumbStyle;
        if (_track?.Thumb != null)
        {
            if (thumbStyle != null && !ReferenceEquals(_track.Thumb.Style, thumbStyle))
            {
                ClearThumbDefaultsIfNeededForStyle(_track.Thumb);
                _track.Thumb.Style = thumbStyle;
            }

            EnsureThumbVisibilityFallback(_track.Thumb);
        }
    }

    private void ApplySelfStyle()
    {
        if (Style != null)
        {
            return;
        }

        if (ResolveStyleResource(ScrollBarStyleKey) is Style explicitStyle)
        {
            ClearScrollBarDefaultsIfNeededForStyle();
            Style = explicitStyle;
            return;
        }

        if (TryFindResource(typeof(ScrollBar)) is Style implicitStyle)
        {
            ClearScrollBarDefaultsIfNeededForStyle();
            Style = implicitStyle;
        }
    }

    private Style? ResolveStyleResource(object resourceKey)
    {
        if (TryFindResource(resourceKey) is Style localStyle)
        {
            return localStyle;
        }

        var app = Jalium.UI.Application.Current;
        if (app?.Resources != null &&
            app.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Style appStyle)
        {
            return appStyle;
        }

        return null;
    }

    private Brush? ResolveBrushResource(object resourceKey)
    {
        if (TryFindResource(resourceKey) is Brush localBrush)
        {
            return localBrush;
        }

        var app = Jalium.UI.Application.Current;
        if (app?.Resources != null &&
            app.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush appBrush)
        {
            return appBrush;
        }

        return null;
    }

    private Brush ResolveTrackBrush()
    {
        return ResolveBrushResource(TrackBrushKey) ?? s_defaultTrackBrush;
    }

    private Brush ResolveThumbBrush()
    {
        return ResolveBrushResource(ThumbBrushKey) ?? s_defaultThumbBrush;
    }

    private Brush ResolveArrowBrush()
    {
        return ResolveBrushResource(ArrowBrushKey) ?? s_defaultArrowBrush;
    }

    private void EnsureThumbVisibilityFallback(Thumb thumb)
    {
        if (thumb.Background == null)
        {
            thumb.Background = ResolveThumbBrush();
        }
    }

    private void ClearScrollBarDefaultsIfNeededForStyle()
    {
        ClearLocalIfReferenceEquals(this, BackgroundProperty, ResolveTrackBrush());
        ClearLocalIfReferenceEquals(this, BackgroundProperty, s_defaultTrackBrush);
        ClearLocalIfReferenceEquals(this, BorderBrushProperty, s_transparentBrush);
        ClearLocalIfValueEquals(this, BorderThicknessProperty, new Thickness(0));
        ClearLocalIfValueEquals(this, PaddingProperty, new Thickness(2));
    }

    private void ClearLineButtonDefaultsIfNeededForStyle(RepeatButton button)
    {
        ClearLocalIfReferenceEquals(button, BackgroundProperty, s_transparentBrush);
        ClearLocalIfReferenceEquals(button, BorderBrushProperty, s_transparentBrush);
        ClearLocalIfValueEquals(button, BorderThicknessProperty, new Thickness(0));
        ClearLocalIfReferenceEquals(button, ForegroundProperty, ResolveArrowBrush());
        ClearLocalIfReferenceEquals(button, ForegroundProperty, s_defaultArrowBrush);
        ClearLocalIfValueEquals(button, PaddingProperty, new Thickness(0));
        ClearLocalIfValueEquals(button, CornerRadiusProperty, new CornerRadius(0));
        ClearLocalIfValueEquals(button, MinWidthProperty, 0.0);
        ClearLocalIfValueEquals(button, MinHeightProperty, 0.0);
    }

    private static void ClearPageButtonDefaultsIfNeededForStyle(RepeatButton button)
    {
        ClearLocalIfValueEquals(button, OpacityProperty, 0.0);
        ClearLocalIfReferenceEquals(button, BackgroundProperty, s_transparentBrush);
        ClearLocalIfReferenceEquals(button, BorderBrushProperty, s_transparentBrush);
        ClearLocalIfValueEquals(button, BorderThicknessProperty, new Thickness(0));
        ClearLocalIfValueEquals(button, PaddingProperty, new Thickness(0));
        ClearLocalIfValueEquals(button, CornerRadiusProperty, new CornerRadius(0));
        ClearLocalIfValueEquals(button, MinWidthProperty, 0.0);
        ClearLocalIfValueEquals(button, MinHeightProperty, 0.0);
    }

    private void ClearThumbDefaultsIfNeededForStyle(Thumb thumb)
    {
        ClearLocalIfReferenceEquals(thumb, BackgroundProperty, ResolveThumbBrush());
        ClearLocalIfReferenceEquals(thumb, BackgroundProperty, s_defaultThumbBrush);
        ClearLocalIfReferenceEquals(thumb, BorderBrushProperty, s_transparentBrush);
        ClearLocalIfValueEquals(thumb, BorderThicknessProperty, new Thickness(0));
        ClearLocalIfValueEquals(thumb, CornerRadiusProperty, new CornerRadius(999));
        ClearLocalIfValueEquals(thumb, Thumb.ShowGripProperty, false);
    }

    private static Style CreateInternalThumbStyle()
    {
        var fallbackTemplate = new ControlTemplate(typeof(Thumb));
        fallbackTemplate.SetVisualTree(() =>
        {
            var border = new Border
            {
                Name = "ThumbBorder"
            };
            border.SetTemplateBinding(Border.BackgroundProperty, BackgroundProperty);
            border.SetTemplateBinding(Border.BorderBrushProperty, BorderBrushProperty);
            border.SetTemplateBinding(Border.BorderThicknessProperty, BorderThicknessProperty);
            border.SetTemplateBinding(Border.CornerRadiusProperty, CornerRadiusProperty);
            border.SetTemplateBinding(FrameworkElement.MarginProperty, PaddingProperty);
            return border;
        });

        var style = new Style(typeof(Thumb));
        style.Setters.Add(new Setter(BorderBrushProperty, s_transparentBrush));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(CornerRadiusProperty, new CornerRadius(999)));
        style.Setters.Add(new Setter(Thumb.ShowGripProperty, false));
        style.Setters.Add(new Setter(Control.TemplateProperty, fallbackTemplate));
        return style;
    }

    private static void ClearLocalIfReferenceEquals(DependencyObject target, DependencyProperty property, object expectedValue)
    {
        if (!target.HasLocalValue(property))
            return;

        var localValue = target.ReadLocalValue(property);
        if (ReferenceEquals(localValue, expectedValue))
        {
            target.ClearValue(property);
        }
    }

    private static void ClearLocalIfValueEquals<T>(DependencyObject target, DependencyProperty property, T expectedValue)
    {
        if (!target.HasLocalValue(property))
            return;

        var localValue = target.ReadLocalValue(property);
        if (localValue is T typedValue && EqualityComparer<T>.Default.Equals(typedValue, expectedValue))
        {
            target.ClearValue(property);
        }
    }

    private void UpdateLineButtonDirectionTags()
    {
        if (_lineUpButton == null || _lineDownButton == null)
            return;

        if (Orientation == Orientation.Vertical)
        {
            _lineUpButton.Tag = "Up";
            _lineDownButton.Tag = "Down";
        }
        else
        {
            _lineUpButton.Tag = "Left";
            _lineDownButton.Tag = "Right";
        }
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    protected override int VisualChildrenCount => 3; // LineUp, Track, LineDown

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        return index switch
        {
            0 => _lineUpButton,
            1 => _track,
            2 => _lineDownButton,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Orientation == Orientation.Vertical)
        {
            var width = double.IsNaN(Width) || Width <= 0 ? DefaultThickness : Width;
            var buttonHeight = IsOverlayStyle ? 0.0 : width; // Square desktop buttons

            _lineUpButton?.Measure(new Size(width, buttonHeight));
            _lineDownButton?.Measure(new Size(width, buttonHeight));

            var trackHeight = Math.Max(0, availableSize.Height - buttonHeight * 2);
            _track?.Measure(new Size(width, trackHeight));

            var height = double.IsPositiveInfinity(availableSize.Height)
                ? buttonHeight * 2 + MinThumbLength * 2
                : availableSize.Height;

            return new Size(width, height);
        }
        else
        {
            var height = double.IsNaN(Height) || Height <= 0 ? DefaultThickness : Height;
            var buttonWidth = IsOverlayStyle ? 0.0 : height; // Square desktop buttons

            _lineUpButton?.Measure(new Size(buttonWidth, height));
            _lineDownButton?.Measure(new Size(buttonWidth, height));

            var trackWidth = Math.Max(0, availableSize.Width - buttonWidth * 2);
            _track?.Measure(new Size(trackWidth, height));

            var width = double.IsPositiveInfinity(availableSize.Width)
                ? buttonWidth * 2 + MinThumbLength * 2
                : availableSize.Width;

            return new Size(width, height);
        }
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        ApplySelfStyle();
        ApplyPartStyles();
        UpdateTrackBindings();
        var crossAxisSize = Orientation == Orientation.Vertical ? finalSize.Width : finalSize.Height;
        ApplyAutoHideVisualState(_autoHideCollapseProgress, crossAxisSize, suppressArrangeInvalidation: true);

        if (IsOverlayStyle)
        {
            // Mobile scroll bars are edge indicators, not desktop chrome. Keep a
            // widened transparent host for a finger-sized Thumb target, while the
            // visual track spans almost the full axis near the outer edge.
            _lineUpButton?.Arrange(default);
            _lineDownButton?.Arrange(default);

            if (Orientation == Orientation.Vertical)
            {
                var endInset = Math.Min(OverlayTrackEndInset, Math.Max(0, finalSize.Height / 2));
                _track?.Arrange(new Rect(
                    0,
                    endInset,
                    Math.Max(0, finalSize.Width),
                    Math.Max(0, finalSize.Height - endInset * 2)));
            }
            else
            {
                var endInset = Math.Min(OverlayTrackEndInset, Math.Max(0, finalSize.Width / 2));
                _track?.Arrange(new Rect(
                    endInset,
                    0,
                    Math.Max(0, finalSize.Width - endInset * 2),
                    Math.Max(0, finalSize.Height)));
            }

            return finalSize;
        }

        if (Orientation == Orientation.Vertical)
        {
            var buttonSize = ControlRenderGeometry.GetAvailableLength(finalSize.Width, finalSize.Height / 2.0);
            var trackHeight = Math.Max(0, finalSize.Height - buttonSize * 2);

            _lineUpButton?.Arrange(new Rect(0, 0, finalSize.Width, buttonSize));
            _track?.Arrange(new Rect(0, buttonSize, finalSize.Width, trackHeight));
            _lineDownButton?.Arrange(new Rect(0, finalSize.Height - buttonSize, finalSize.Width, buttonSize));
        }
        else
        {
            var buttonSize = ControlRenderGeometry.GetAvailableLength(finalSize.Height, finalSize.Width / 2.0);
            var trackWidth = Math.Max(0, finalSize.Width - buttonSize * 2);

            _lineUpButton?.Arrange(new Rect(0, 0, buttonSize, finalSize.Height));
            _track?.Arrange(new Rect(buttonSize, 0, trackWidth, finalSize.Height));
            _lineDownButton?.Arrange(new Rect(finalSize.Width - buttonSize, 0, buttonSize, finalSize.Height));
        }

        return finalSize;
    }

    #endregion

    #region Event Handlers

    private void OnLineUpClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Max(Minimum, Value - SmallChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.SmallDecrement);
        }
    }

    private void OnLineDownClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Min(Maximum, Value + SmallChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.SmallIncrement);
        }
    }

    private void OnPageUpClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Max(Minimum, Value - LargeChange);

        // Track-press paging: when the upper trough is held down, page toward the pointer
        // and stop the moment the thumb reaches it instead of running on to Minimum.
        if (_hasTrackPagingTarget)
        {
            if (Value <= _trackPagingTargetValue + double.Epsilon)
            {
                return; // Thumb already reached the pointer — hold position.
            }

            newValue = Math.Max(newValue, _trackPagingTargetValue);
        }

        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.LargeDecrement);
        }
    }

    private void OnPageDownClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Min(Maximum, Value + LargeChange);

        // Track-press paging: when the lower trough is held down, page toward the pointer
        // and stop the moment the thumb reaches it instead of running on to Maximum.
        if (_hasTrackPagingTarget)
        {
            if (Value >= _trackPagingTargetValue - double.Epsilon)
            {
                return; // Thumb already reached the pointer — hold position.
            }

            newValue = Math.Min(newValue, _trackPagingTargetValue);
        }

        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.LargeIncrement);
        }
    }

    // ── Track-press paging (click-and-hold in the trough) ──────────────
    // The page RepeatButtons (ClickMode.Press) page immediately on mouse-down. The target is
    // seeded on the *tunnelling* PreviewMouseDown so it is armed before ButtonBase's bubbling
    // MouseDown fires that first page step — making even the instant first step clamp to the
    // pointer. MouseMove is not handled by ButtonBase and — because the button holds capture —
    // is routed to it on every physical move (WindowInputDispatcher.HandleMouseMove targets the
    // captured element), keeping the target live so the thumb follows the cursor while held.
    private void WirePageButtonTrackPaging(RepeatButton button)
    {
        button.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPageButtonPreviewMouseDown), handledEventsToo: true);
        button.AddHandler(MouseMoveEvent, new MouseEventHandler(OnPageButtonMouseMove));
        button.AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnPageButtonMouseUp), handledEventsToo: true);
        button.AddHandler(LostMouseCaptureEvent, new RoutedEventHandler(OnPageButtonLostMouseCapture));
    }

    private void OnPageButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Arm paging and seed the target from the press location so the first (immediate) page
        // step — and every auto-repeat after it — stops once the thumb's edge reaches the pointer.
        _hasTrackPagingTarget = TryComputeTrackPagingTarget(e, IsIncreasePageButton(sender), out _trackPagingTargetValue);
    }

    private void OnPageButtonMouseMove(object sender, MouseEventArgs e)
    {
        // Only retarget while an active page hold is in progress; ignore plain hover moves.
        if (!_hasTrackPagingTarget)
        {
            return;
        }

        if (TryComputeTrackPagingTarget(e, IsIncreasePageButton(sender), out var value))
        {
            _trackPagingTargetValue = value;
        }
    }

    // True when the event came from the increase (page-down / page-right) RepeatButton, i.e. the
    // pointer is on the far side of the thumb and paging moves Value toward Maximum.
    private bool IsIncreasePageButton(object sender)
        => _track != null && ReferenceEquals(sender, _track.IncreaseRepeatButton);

    private void OnPageButtonMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _hasTrackPagingTarget = false;
        }
    }

    private void OnPageButtonLostMouseCapture(object sender, RoutedEventArgs e)
    {
        _hasTrackPagingTarget = false;
    }

    // Maps a page-button mouse event to the Value at which the thumb's leading edge meets the
    // pointer (bottom/right edge for a page-down, top/left edge for a page-up). Returns false
    // (paging falls back to unclamped LargeChange steps) when the track has no usable geometry/range.
    private bool TryComputeTrackPagingTarget(MouseEventArgs e, bool pageDown, out double value)
    {
        value = Value;

        if (_track == null)
        {
            return false;
        }

        var isVertical = Orientation == Orientation.Vertical;
        var trackLength = isVertical ? _track.RenderSize.Height : _track.RenderSize.Width;
        if (!double.IsFinite(trackLength) || trackLength <= 0)
        {
            return false;
        }

        var range = Maximum - Minimum;
        if (!double.IsFinite(range) || range <= 0)
        {
            return false;
        }

        var thumbSize = 0.0;
        if (_track.Thumb != null)
        {
            thumbSize = isVertical ? _track.Thumb.RenderSize.Height : _track.Thumb.RenderSize.Width;
            if (!double.IsFinite(thumbSize) || thumbSize < 0)
            {
                thumbSize = 0;
            }
        }

        var availableLength = Math.Max(0.0, trackLength - thumbSize);
        if (availableLength <= 0)
        {
            return false;
        }

        var local = e.GetPosition(_track);
        var position = isVertical ? local.Y : local.X;

        // Microsoft trough design: the thumb stops with the edge facing the pointer landing on it,
        // not its center — the far edge (bottom / right) when paging toward the end, the near edge
        // (top / left) when paging toward the start. Pick that edge, then back out the thumb start
        // offset and convert it to a value along the track.
        var alignFarEdge = pageDown != _track.IsDirectionReversed;
        var desiredThumbStart = alignFarEdge ? (position - thumbSize) : position;

        var rawRatio = desiredThumbStart / availableLength;
        var ratio = Math.Clamp(_track.IsDirectionReversed ? (1.0 - rawRatio) : rawRatio, 0.0, 1.0);

        value = Minimum + (ratio * range);
        return true;
    }

    private void OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginThumbDrag();
    }

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        // Thumb starts its shared touch-drag behavior on TouchDown. Mobile overlay
        // scroll bars deliberately keep those deltas locked until the hold gesture
        // succeeds; mouse dragging and non-overlay scroll bars remain immediate.
        if (IsOverlayStyle &&
            _overlayThumbTouchId >= 0 &&
            !_isOverlayThumbTouchDragUnlocked)
        {
            return;
        }

        if (_track != null)
        {
            if (!_isDragging)
            {
                BeginThumbDrag();
            }

            _thumbDragAccumulatedHorizontal += e.HorizontalChange;
            _thumbDragAccumulatedVertical += e.VerticalChange;

            var newValue = _thumbDragStartValue + _track.ValueFromDistance(_thumbDragAccumulatedHorizontal, _thumbDragAccumulatedVertical);
            newValue = Math.Clamp(newValue, Minimum, Maximum);

            if (Math.Abs(newValue - Value) > double.Epsilon)
            {
                Value = newValue;
                RaiseScrollEvent(ScrollEventType.ThumbTrack);
            }
        }
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndThumbDrag();
        RaiseScrollEvent(ScrollEventType.EndScroll);
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
        }
    }

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        // Touch on the scroll bar background — give it focus. Inner Thumb and
        // page RepeatButtons consume their own TouchDown via ButtonBase /
        // Thumb's per-contact handlers, so this only fires for the surrounding
        // chrome.
        if (e is TouchEventArgs)
        {
            Focus();
        }
    }

    private void OnOverlayThumbTouchDown(object sender, RoutedEventArgs e)
    {
        if (!IsOverlayStyle ||
            e is not TouchEventArgs touchArgs ||
            sender is not Thumb thumb ||
            !thumb.IsEnabled ||
            !TouchHelper.GetIsTouchInteractive(thumb))
        {
            return;
        }

        if (_overlayThumbTouchId >= 0)
        {
            // Thumb ignores additional contacts while captured; keep the Hold
            // recognizer bound to that same owner instead of collapsing and
            // replacing the active 8-DIP gesture.
            return;
        }

        CompleteOverlayThumbGesture();
        _overlayThumbTouchId = touchArgs.TouchDevice.Id;
        _isOverlayThumbTouchDragUnlocked = false;
        EnsureOverlayThumbGestureRecognizer().ProcessDownEvent(
            CreateOverlayPointerPoint(touchArgs, isInContact: true));
    }

    private void OnOverlayThumbTouchMove(object sender, RoutedEventArgs e)
    {
        if (!IsOverlayStyle ||
            e is not TouchEventArgs touchArgs ||
            touchArgs.TouchDevice.Id != _overlayThumbTouchId ||
            _overlayThumbGestureRecognizer == null)
        {
            return;
        }

        _overlayThumbGestureRecognizer.ProcessMoveEvents(
            [CreateOverlayPointerPoint(touchArgs, isInContact: true)]);
    }

    private void OnOverlayThumbTouchUp(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs || touchArgs.TouchDevice.Id != _overlayThumbTouchId)
            return;

        _overlayThumbGestureRecognizer?.ProcessUpEvent(
            CreateOverlayPointerPoint(touchArgs, isInContact: false));
        CompleteOverlayThumbGesture();
    }

    private void OnOverlayThumbLostTouchCapture(object sender, RoutedEventArgs e)
    {
        if (e is TouchEventArgs touchArgs && touchArgs.TouchDevice.Id == _overlayThumbTouchId)
        {
            CompleteOverlayThumbGesture();
        }
    }

    private GestureRecognizer EnsureOverlayThumbGestureRecognizer()
    {
        if (_overlayThumbGestureRecognizer != null)
            return _overlayThumbGestureRecognizer;

        _overlayThumbGestureRecognizer = new GestureRecognizer(Dispatcher)
        {
            // Hold only: moving before the threshold cancels expansion, while
            // dragging after a completed hold keeps the wider indicator active.
            GestureSettings = GestureSettings.Hold
        };
        _overlayThumbGestureRecognizer.Holding += OnOverlayThumbHolding;
        return _overlayThumbGestureRecognizer;
    }

    private void OnOverlayThumbHolding(object? sender, HoldingEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started &&
            IsOverlayStyle &&
            _overlayThumbTouchId >= 0)
        {
            // Unlock from the finger's current position. Thumb has continued to
            // update its previous pointer position while ScrollBar ignored the
            // pre-hold deltas, so the first accepted move does not jump.
            _isOverlayThumbTouchDragUnlocked = true;
            _thumbDragStartValue = Value;
            _thumbDragAccumulatedHorizontal = 0;
            _thumbDragAccumulatedVertical = 0;
            StartAutoHideVisualTransition(0.0);
            SetOverlayLongPressExpanded(true);
        }
    }

    private static PointerPoint CreateOverlayPointerPoint(TouchEventArgs e, bool isInContact)
    {
        return new PointerPoint(
            unchecked((uint)e.TouchDevice.Id),
            e.GetTouchPoint(null).Position,
            PointerDeviceType.Touch,
            isInContact,
            new PointerPointProperties
            {
                IsPrimary = true,
                PointerUpdateKind = PointerUpdateKind.Other
            },
            unchecked((ulong)Math.Max(0, e.Timestamp)),
            0);
    }

    private void CompleteOverlayThumbGesture()
    {
        _overlayThumbGestureRecognizer?.CompleteGesture();
        _overlayThumbTouchId = -1;
        _isOverlayThumbTouchDragUnlocked = false;
        SetOverlayLongPressExpanded(false);
    }

    private void SetOverlayLongPressExpanded(bool expanded)
    {
        expanded &= IsOverlayStyle;
        if (_isOverlayLongPressExpanded == expanded)
            return;

        _isOverlayLongPressExpanded = expanded;
        ApplyAutoHideVisualState(_autoHideCollapseProgress);
    }

    internal void AdvanceOverlayLongPressClockForTesting(long milliseconds)
    {
        _overlayThumbGestureRecognizer?.AdvanceClockForTesting(milliseconds);
    }

    private void RaiseScrollEvent(ScrollEventType scrollType)
    {
        RaiseEvent(new ScrollEventArgs(ScrollEvent, scrollType, Value)
        {
            Source = this
        });
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollBar scrollBar)
        {
            scrollBar.UpdateTrackBindings();
            scrollBar.InvalidateMeasure();
        }
    }

    // ViewportSize changes every frame as content extent fluctuates (e.g. a
    // live-updating panel). Unlike Orientation, it does NOT affect the
    // ScrollBar's own desired size — MeasureOverride derives that purely from
    // Orientation + Width/Height. The value is forwarded to the Track via
    // UpdateTrackBindings, and Track.OnLayoutPropertyChanged invalidates the
    // Track's arrange to reposition the thumb — which is absorbed within the
    // SAME arrange pass when the ScrollBar arranges its template. Calling
    // InvalidateMeasure here (as the shared layout callback did) re-queued the
    // ScrollBar for measure during the arrange pass, forcing LayoutManager to
    // run a second iteration every frame ("tree re-invalidate", Iterations=2).
    // Routing only the binding update keeps layout settling in one pass.
    private static void OnViewportSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollBar scrollBar)
        {
            scrollBar.UpdateTrackBindings();
        }
    }

    private static void OnPartStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollBar scrollBar)
        {
            scrollBar.ApplyPartStyles();
            scrollBar.UpdateTrackBindings();
            scrollBar.InvalidateMeasure();
            scrollBar.InvalidateVisual();
        }
    }

    private static void OnThumbPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Visual transition is now driven by ScrollViewer.ApplyScrollBarAutoHideVisualState
        // which calls StartAutoHideVisualTransition directly after setting this property.
        // No action needed here — avoids duplicate animation starts.
    }

    private static void OnOverlayStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollBar scrollBar)
            return;

        if (!scrollBar.IsOverlayStyle)
        {
            scrollBar.CompleteOverlayThumbGesture();
        }

        scrollBar.UpdateTrackBindings();
        scrollBar.ApplyAutoHideVisualState(
            scrollBar._autoHideCollapseProgress,
            null,
            suppressArrangeInvalidation: true);
        scrollBar.InvalidateMeasure();
        scrollBar.InvalidateArrange();
        scrollBar.InvalidateVisual();
    }

    private void EnsureAutoHideVisualTimer()
    {
        if (_autoHideVisualTimer != null)
            return;

        _autoHideVisualTimer = new DispatcherTimer
        {
            Interval = CompositionTarget.FrameInterval
        };
        _autoHideVisualTimer.Tick += OnAutoHideVisualTimerTick;
    }

    internal void StartAutoHideVisualTransition(double targetProgress)
    {
        targetProgress = Math.Clamp(targetProgress, 0.0, 1.0);

        if (Math.Abs(_autoHideCollapseProgress - targetProgress) <= 0.001)
        {
            _autoHideCollapseProgress = targetProgress;
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
            StopAutoHideVisualTimer();
            return;
        }

        // Arrange can ask for the same state on every layout pass. Do not
        // restart an in-flight fade toward that target or a busy layout loop
        // could keep the indicator partially visible indefinitely.
        if (_autoHideVisualTimer is { IsEnabled: true } &&
            Math.Abs(_autoHideVisualAnimTo - targetProgress) <= 0.001)
        {
            return;
        }

        _autoHideVisualAnimFrom = _autoHideCollapseProgress;
        _autoHideVisualAnimTo = targetProgress;
        _autoHideVisualAnimStartTick = Environment.TickCount64;

        EnsureAutoHideVisualTimer();
        _autoHideVisualTimer!.Start();
        ApplyAutoHideVisualState(_autoHideCollapseProgress);
    }

    private void StopAutoHideVisualTimer()
    {
        _autoHideVisualTimer?.Stop();
    }

    private void OnAutoHideVisualTimerTick(object? sender, EventArgs e)
    {
        var elapsedMs = Environment.TickCount64 - _autoHideVisualAnimStartTick;
        var raw = Math.Clamp(elapsedMs / AutoHideVisualTransitionDurationMs, 0.0, 1.0);
        var eased = SmoothStep(raw);

        _autoHideCollapseProgress = Lerp(_autoHideVisualAnimFrom, _autoHideVisualAnimTo, eased);
        ApplyAutoHideVisualState(_autoHideCollapseProgress);

        if (raw >= 1.0)
        {
            _autoHideCollapseProgress = _autoHideVisualAnimTo;
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
            StopAutoHideVisualTimer();
        }
    }

    private void ApplyAutoHideVisualState(double collapseProgress, double? crossAxisSize = null, bool suppressArrangeInvalidation = false)
    {
        collapseProgress = Math.Clamp(collapseProgress, 0.0, 1.0);
        _autoHideCollapseProgress = collapseProgress;
        _chromeOpacity = IsOverlayStyle ? 0.0 : 1.0 - collapseProgress;

        if (_lineUpButton != null && _lineDownButton != null)
        {
            if (IsOverlayStyle)
            {
                _lineUpButton.Visibility = Visibility.Collapsed;
                _lineDownButton.Visibility = Visibility.Collapsed;
                _lineUpButton.Opacity = 0;
                _lineDownButton.Opacity = 0;
                _lineUpButton.IsHitTestVisible = false;
                _lineDownButton.IsHitTestVisible = false;
            }
            else
            {
                _lineUpButton.IsHitTestVisible = true;
                _lineDownButton.IsHitTestVisible = true;

                // Use Visibility.Collapsed when fully collapsed so the rendering pipeline
                // skips the buttons entirely (Opacity alone may not hide them reliably).
                var fullyCollapsed = collapseProgress >= 0.999;

                if (_hasCustomLineButtonStyle)
                {
                    var targetVisibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (_lineUpButton.Visibility != targetVisibility)
                        _lineUpButton.Visibility = targetVisibility;
                    if (_lineDownButton.Visibility != targetVisibility)
                        _lineDownButton.Visibility = targetVisibility;

                    if (!fullyCollapsed)
                    {
                        var arrowOpacity = 1.0 - collapseProgress;
                        _lineUpButton.Opacity = arrowOpacity;
                        _lineDownButton.Opacity = arrowOpacity;
                    }
                }
                else
                {
                    // Keep fallback mode line buttons transparent to avoid default square overlays.
                    _lineUpButton.Opacity = 0;
                    _lineDownButton.Opacity = 0;
                    if (fullyCollapsed)
                    {
                        if (_lineUpButton.Visibility != Visibility.Collapsed)
                            _lineUpButton.Visibility = Visibility.Collapsed;
                        if (_lineDownButton.Visibility != Visibility.Collapsed)
                            _lineDownButton.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        if (_lineUpButton.Visibility != Visibility.Visible)
                            _lineUpButton.Visibility = Visibility.Visible;
                        if (_lineDownButton.Visibility != Visibility.Visible)
                            _lineDownButton.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        if (_track?.DecreaseRepeatButton != null && _track.IncreaseRepeatButton != null)
        {
            // Touch panning should remain available across the transparent overlay
            // gutter. Only the thumb itself stays interactive in mobile mode.
            _track.DecreaseRepeatButton.IsHitTestVisible = !IsOverlayStyle;
            _track.IncreaseRepeatButton.IsHitTestVisible = !IsOverlayStyle;
        }

        if (_track != null)
        {
            if (IsOverlayStyle)
            {
                ApplyOverlayTrackPresentation(
                    _track,
                    opacity: 1.0 - collapseProgress,
                    isHitTestVisible: collapseProgress < 0.999);
            }
            else
            {
                RestoreTrackPresentationAfterOverlay(_track);
            }

            var expandedThickness = ComputeExpandedThumbCrossAxisThickness(crossAxisSize);
            var thumbThickness = IsOverlayStyle
                ? expandedThickness
                : Math.Max(
                    SlimThumbThickness,
                    Lerp(expandedThickness, SlimThumbThickness, collapseProgress));
            var currentThickness = _track.ThumbCrossAxisThickness;

            if (!double.IsFinite(currentThickness) || Math.Abs(currentThickness - thumbThickness) > 0.001)
            {
                _track.ThumbCrossAxisThickness = thumbThickness;
                if (!suppressArrangeInvalidation)
                {
                    _track.RefreshThumbVisualLayout();
                }
            }

            if (IsOverlayStyle && _track.Thumb != null)
            {
                var indicatorThickness = _isOverlayLongPressExpanded
                    ? OverlayExpandedIndicatorThickness
                    : OverlayIndicatorThickness;
                ApplyOverlayThumbInsets(
                    _track.Thumb,
                    thumbThickness,
                    indicatorThickness,
                    suppressArrangeInvalidation);
            }
            else if (_track.Thumb != null)
            {
                RestoreThumbPaddingAfterOverlay(_track.Thumb);
            }
        }

        InvalidateVisual();
    }

    private double ComputeExpandedThumbCrossAxisThickness(double? crossAxisSizeOverride)
    {
        double crossAxisSize;
        if (crossAxisSizeOverride.HasValue)
        {
            crossAxisSize = crossAxisSizeOverride.Value;
        }
        else
        {
            crossAxisSize = Orientation == Orientation.Vertical ? RenderSize.Width : RenderSize.Height;
            if (!double.IsFinite(crossAxisSize) || crossAxisSize <= 0)
            {
                crossAxisSize = Orientation == Orientation.Vertical
                    ? (double.IsNaN(Width) || Width <= 0 ? DefaultThickness : Width)
                    : (double.IsNaN(Height) || Height <= 0 ? DefaultThickness : Height);
            }
        }

        if (!double.IsFinite(crossAxisSize) || crossAxisSize <= 0)
        {
            crossAxisSize = DefaultThickness;
        }

        if (IsOverlayStyle)
        {
            // The Thumb itself fills the mobile hit strip. Its template uses
            // Padding to render only a 2-DIP idle / 8-DIP long-press indicator.
            return crossAxisSize;
        }

        return Math.Max(SlimThumbThickness, crossAxisSize - ExpandedThumbInset);
    }

    private void ApplyOverlayThumbInsets(
        Thumb thumb,
        double crossAxisSize,
        double indicatorThickness,
        bool suppressArrangeInvalidation)
    {
        if (!_hasOverlayThumbPaddingSnapshot)
        {
            _overlayThumbHadLocalPadding = thumb.HasLocalValue(PaddingProperty);
            if (_overlayThumbHadLocalPadding && thumb.ReadLocalValue(PaddingProperty) is Thickness localPadding)
            {
                _overlayThumbLocalPadding = localPadding;
            }

            _hasOverlayThumbPaddingSnapshot = true;
        }

        crossAxisSize = Math.Max(0, crossAxisSize);
        var edgeInset = Math.Min(OverlayIndicatorEdgeInset, crossAxisSize);
        indicatorThickness = Math.Min(
            Math.Max(0, indicatorThickness),
            Math.Max(0, crossAxisSize - edgeInset));
        var leadingInset = Math.Max(0, crossAxisSize - edgeInset - indicatorThickness);

        var padding = Orientation == Orientation.Vertical
            ? new Thickness(leadingInset, 0, edgeInset, 0)
            : new Thickness(0, leadingInset, 0, edgeInset);

        if (thumb.Padding != padding)
        {
            thumb.Padding = padding;
            if (!suppressArrangeInvalidation)
            {
                _track?.RefreshThumbVisualLayout();
            }
        }
    }

    private void RestoreThumbPaddingAfterOverlay(Thumb thumb)
    {
        if (!_hasOverlayThumbPaddingSnapshot)
            return;

        if (_overlayThumbHadLocalPadding)
        {
            thumb.Padding = _overlayThumbLocalPadding;
        }
        else
        {
            thumb.ClearValue(PaddingProperty);
        }

        _hasOverlayThumbPaddingSnapshot = false;
        _overlayThumbHadLocalPadding = false;
        _overlayThumbLocalPadding = default;
    }

    private void ApplyOverlayTrackPresentation(Track track, double opacity, bool isHitTestVisible)
    {
        if (!_hasOverlayTrackPresentationSnapshot)
        {
            _overlayTrackHadLocalOpacity = track.HasLocalValue(UIElement.OpacityProperty);
            if (_overlayTrackHadLocalOpacity && track.ReadLocalValue(UIElement.OpacityProperty) is double localOpacity)
            {
                _overlayTrackLocalOpacity = localOpacity;
            }

            _overlayTrackHadLocalHitTestVisibility = track.HasLocalValue(UIElement.IsHitTestVisibleProperty);
            if (_overlayTrackHadLocalHitTestVisibility &&
                track.ReadLocalValue(UIElement.IsHitTestVisibleProperty) is bool localHitTestVisibility)
            {
                _overlayTrackLocalHitTestVisibility = localHitTestVisibility;
            }

            _hasOverlayTrackPresentationSnapshot = true;
        }

        track.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        track.IsHitTestVisible = isHitTestVisible;
    }

    private void RestoreTrackPresentationAfterOverlay(Track track)
    {
        if (!_hasOverlayTrackPresentationSnapshot)
            return;

        if (_overlayTrackHadLocalOpacity)
        {
            track.Opacity = _overlayTrackLocalOpacity;
        }
        else
        {
            track.ClearValue(UIElement.OpacityProperty);
        }

        if (_overlayTrackHadLocalHitTestVisibility)
        {
            track.IsHitTestVisible = _overlayTrackLocalHitTestVisibility;
        }
        else
        {
            track.ClearValue(UIElement.IsHitTestVisibleProperty);
        }

        _hasOverlayTrackPresentationSnapshot = false;
        _overlayTrackHadLocalOpacity = false;
        _overlayTrackLocalOpacity = default;
        _overlayTrackHadLocalHitTestVisibility = false;
        _overlayTrackLocalHitTestVisibility = default;
    }

    // Diameter of the round dot the thumb collapses to at extreme content ratios. It matches the
    // expanded cross-axis thickness so the dot is a true circle (length == thickness, fully
    // rounded by the thumb corner radius), with a small floor so it stays visible on thin bars.
    private double ComputeThumbDotDiameter()
    {
        var diameter = ComputeExpandedThumbCrossAxisThickness(null);
        if (!double.IsFinite(diameter) || diameter <= 0)
        {
            diameter = DefaultThickness - ExpandedThumbInset;
        }

        return Math.Max(MinThumbDotDiameter, diameter);
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private void BeginThumbDrag()
    {
        _isDragging = true;
        _hasTrackPagingTarget = false;
        _thumbDragStartValue = Value;
        _thumbDragAccumulatedHorizontal = 0;
        _thumbDragAccumulatedVertical = 0;

        if (IsOverlayStyle)
        {
            StartAutoHideVisualTransition(0.0);
        }
    }

    private void EndThumbDrag()
    {
        CompleteOverlayThumbGesture();
        _isDragging = false;
        _thumbDragAccumulatedHorizontal = 0;
        _thumbDragAccumulatedVertical = 0;
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateTrackBindings();
    }

    /// <inheritdoc />
    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        UpdateTrackBindings();
    }

    /// <inheritdoc />
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        UpdateTrackBindings();
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        var result = base.HitTestCore(point);
        if (!IsOverlayStyle || result?.VisualHit is not Visual hitVisual)
        {
            return result;
        }

        // The widened mobile host must not steal taps from content underneath.
        // Only the proportional Thumb segment is interactive; empty track and
        // chrome pass through to the next sibling in the ScrollViewer.
        for (Visual? current = hitVisual; current != null && !ReferenceEquals(current, this); current = current.VisualParent)
        {
            if (ReferenceEquals(current, _track?.Thumb))
            {
                // The Thumb fills the wider overlay layout host so the 8-DIP
                // long-press visual has room to expand inward. That transparent
                // layout space is not a touch target: only the original 2-DIP
                // strip may start interaction. After Hold, touch capture keeps
                // the active contact dragging even when it leaves that strip.
                return IsPointWithinOverlayIndicator(point) ? result : null;
            }
        }

        return null;
    }

    private bool IsPointWithinOverlayIndicator(Point point)
    {
        var crossAxisSize = Orientation == Orientation.Vertical
            ? RenderSize.Width
            : RenderSize.Height;
        if (!double.IsFinite(crossAxisSize) || crossAxisSize <= 0)
            return false;

        var edgeInset = Math.Min(OverlayIndicatorEdgeInset, crossAxisSize);
        var indicatorThickness = Math.Min(
            OverlayIndicatorThickness,
            Math.Max(0, crossAxisSize - edgeInset));
        var indicatorStart = Math.Max(0, crossAxisSize - edgeInset - indicatorThickness);
        var indicatorEnd = indicatorStart + indicatorThickness;
        // FrameworkElement.HitTestCore receives points in the parent coordinate
        // space; normalize to this ScrollBar before comparing with RenderSize.
        var crossAxisPoint = Orientation == Orientation.Vertical
            ? point.X - VisualBounds.X
            : point.Y - VisualBounds.Y;

        return crossAxisPoint >= indicatorStart && crossAxisPoint <= indicatorEnd;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var innerRect = new Rect(
            Padding.Left,
            Padding.Top,
            Math.Max(0, RenderSize.Width - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - Padding.Top - Padding.Bottom));
        if (innerRect.Width <= 0 || innerRect.Height <= 0)
        {
            return;
        }

        var chromeOpacity = Math.Clamp(_chromeOpacity, 0.0, 1.0);
        if (chromeOpacity <= 0.001)
            return;

        dc.PushOpacity(chromeOpacity);

        var backdropEffect = BackdropEffect;
        if ((backdropEffect ?? s_defaultTrackBackdropEffect).HasEffect && backdropEffect != null)
        {
            dc.DrawBackdropEffect(innerRect, backdropEffect, CornerRadius);
        }

        var bgBrush = Background ?? ResolveTrackBrush();
        dc.DrawRoundedRectangle(bgBrush, null, innerRect, CornerRadius);

        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            if (_borderPen == null || _borderPenBrush != BorderBrush || _borderPenThickness != BorderThickness.Left)
            {
                _borderPen = new Pen(BorderBrush, BorderThickness.Left);
                _borderPenBrush = BorderBrush;
                _borderPenThickness = BorderThickness.Left;
            }
            dc.DrawRoundedRectangle(null, _borderPen, innerRect, CornerRadius);
        }

        // Fallback: if line-button styles are missing, draw simple arrows directly.
        if (!_hasCustomLineButtonStyle)
        {
            DrawFallbackArrows(dc);
        }

        dc.Pop();
    }

    private void DrawFallbackArrows(DrawingContext dc)
    {
        const double baseArrowSize = 8.0;
        var fallbackArrowBrush = ResolveArrowBrush();
        var upBrush = (_lineUpButton?.Foreground as Brush) ?? fallbackArrowBrush;
        var downBrush = (_lineDownButton?.Foreground as Brush) ?? fallbackArrowBrush;
        var upScale = Math.Clamp(_lineUpButton?.CurrentScrollBarArrowScale ?? 1.0, 0.7, 1.25);
        var downScale = Math.Clamp(_lineDownButton?.CurrentScrollBarArrowScale ?? 1.0, 0.7, 1.25);
        var upArrowSize = baseArrowSize * upScale;
        var downArrowSize = baseArrowSize * downScale;

        if (Orientation == Orientation.Vertical)
        {
            var buttonSize = RenderSize.Width;
            if (buttonSize <= 0 || RenderSize.Height < buttonSize * 2)
                return;

            var topCenter = new Point(RenderSize.Width / 2, buttonSize / 2);
            var bottomCenter = new Point(RenderSize.Width / 2, RenderSize.Height - buttonSize / 2);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                upBrush,
                new Rect(topCenter.X - upArrowSize / 2, topCenter.Y - upArrowSize / 2, upArrowSize, upArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Up);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                downBrush,
                new Rect(bottomCenter.X - downArrowSize / 2, bottomCenter.Y - downArrowSize / 2, downArrowSize, downArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Down);
        }
        else
        {
            var buttonSize = RenderSize.Height;
            if (buttonSize <= 0 || RenderSize.Width < buttonSize * 2)
                return;

            var leftCenter = new Point(buttonSize / 2, RenderSize.Height / 2);
            var rightCenter = new Point(RenderSize.Width - buttonSize / 2, RenderSize.Height / 2);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                upBrush,
                new Rect(leftCenter.X - upArrowSize / 2, leftCenter.Y - upArrowSize / 2, upArrowSize, upArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Left);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                downBrush,
                new Rect(rightCenter.X - downArrowSize / 2, rightCenter.Y - downArrowSize / 2, downArrowSize, downArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Right);
        }
    }

    #endregion
}
