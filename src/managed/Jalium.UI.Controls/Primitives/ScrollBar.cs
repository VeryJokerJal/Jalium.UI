using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Input.Internal.Gestures;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that provides a scroll bar for scrolling content.
/// </summary>
[TemplatePart(Name = "PART_Track", Type = typeof(Track))]
[TemplatePart(Name = "PART_LineDecreaseButton", Type = typeof(RepeatButton))]
[TemplatePart(Name = "PART_LineIncreaseButton", Type = typeof(RepeatButton))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Vertical", GroupName = "OrientationStates")]
[TemplateVisualState(Name = "Horizontal", GroupName = "OrientationStates")]
[TemplateVisualState(Name = "Persistent", GroupName = "PresentationStates")]
[TemplateVisualState(Name = "Overlay", GroupName = "PresentationStates")]
[TemplateVisualState(Name = "Expanded", GroupName = "ThumbStates")]
[TemplateVisualState(Name = "Slim", GroupName = "ThumbStates")]
public class ScrollBar : RangeBase
{
    private bool HasCssBackdropEffect => GetEffectiveValueLayer(BackdropEffectProperty) is
        DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState;

    internal override bool RendersBackdropEffectInOnRender => TemplateRootInternal is null && !HasCssBackdropEffect;

    /// <summary>
    /// Set by a host that creates this bar and lays it out at a rectangle it derives
    /// itself, without consulting this bar's DesiredSize (see
    /// <see cref="ScrollViewer"/>). Such a bar cannot affect its host's layout, so
    /// its invalidations — including the ones the auto-hide animation raises by
    /// toggling the line buttons — are not propagated past the host.
    /// </summary>
    internal bool IsHostOwnedLayout { get; set; }

    /// <inheritdoc />
    internal override bool IsLayoutIsolated => IsHostOwnedLayout;

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

    #endregion

    // Cached pens
    private Pen? _borderPen;
    private Brush? _borderPenBrush;
    private double _borderPenThickness;
    private Border? _cssBorderPainter;
    private CssScrollBarColors? _ownerCssColors;
    private Brush? _fallbackThumbLocalBrush;
    private double _elasticOverscroll;

    private static readonly DependencyPropertyKey PartsInsetsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PartsInsets), typeof(Thickness), typeof(ScrollBar), new PropertyMetadata(default(Thickness)));
    public static readonly DependencyProperty PartsInsetsProperty = PartsInsetsPropertyKey.DependencyProperty;
    public Thickness PartsInsets => (Thickness)GetValue(PartsInsetsProperty)!;

    private static readonly DependencyPropertyKey ChromeInsetsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ChromeInsets), typeof(Thickness), typeof(ScrollBar), new PropertyMetadata(new Thickness(2)));
    public static readonly DependencyProperty ChromeInsetsProperty = ChromeInsetsPropertyKey.DependencyProperty;
    public Thickness ChromeInsets => (Thickness)GetValue(ChromeInsetsProperty)!;

    private static readonly DependencyPropertyKey NaturalThicknessPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(NaturalThickness), typeof(double), typeof(ScrollBar), new PropertyMetadata(16d));
    public static readonly DependencyProperty NaturalThicknessProperty = NaturalThicknessPropertyKey.DependencyProperty;
    public double NaturalThickness => (double)GetValue(NaturalThicknessProperty)!;

    private static readonly DependencyPropertyKey HasLineButtonsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasLineButtons), typeof(bool), typeof(ScrollBar), new PropertyMetadata(true));
    public static readonly DependencyProperty HasLineButtonsProperty = HasLineButtonsPropertyKey.DependencyProperty;
    public bool HasLineButtons => (bool)GetValue(HasLineButtonsProperty)!;

    private static readonly DependencyPropertyKey ChromeOpacityPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ChromeOpacity), typeof(double), typeof(ScrollBar), new PropertyMetadata(1d));
    public static readonly DependencyProperty ChromeOpacityProperty = ChromeOpacityPropertyKey.DependencyProperty;
    public double ChromeOpacity => (double)GetValue(ChromeOpacityProperty)!;

    private static readonly DependencyPropertyKey LineButtonsVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LineButtonsVisibility), typeof(Visibility), typeof(ScrollBar), new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty LineButtonsVisibilityProperty = LineButtonsVisibilityPropertyKey.DependencyProperty;
    public Visibility LineButtonsVisibility => (Visibility)GetValue(LineButtonsVisibilityProperty)!;

    private static readonly DependencyPropertyKey DecreaseDirectionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DecreaseDirection), typeof(string), typeof(ScrollBar), new PropertyMetadata("Up"));
    public static readonly DependencyProperty DecreaseDirectionProperty = DecreaseDirectionPropertyKey.DependencyProperty;
    public string DecreaseDirection => (string)GetValue(DecreaseDirectionProperty)!;

    private static readonly DependencyPropertyKey IncreaseDirectionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IncreaseDirection), typeof(string), typeof(ScrollBar), new PropertyMetadata("Down"));
    public static readonly DependencyProperty IncreaseDirectionProperty = IncreaseDirectionPropertyKey.DependencyProperty;
    public string IncreaseDirection => (string)GetValue(IncreaseDirectionProperty)!;

    public static readonly DependencyProperty LineButtonStyleProperty = DependencyProperty.Register(
        nameof(LineButtonStyle), typeof(Style), typeof(ScrollBar), new PropertyMetadata(null, OnPartStylePropertyChanged));
    public Style? LineButtonStyle { get => (Style?)GetValue(LineButtonStyleProperty); set => SetValue(LineButtonStyleProperty, value); }

    public static readonly DependencyProperty PageButtonStyleProperty = DependencyProperty.Register(
        nameof(PageButtonStyle), typeof(Style), typeof(ScrollBar), new PropertyMetadata(null, OnPartStylePropertyChanged));
    public Style? PageButtonStyle { get => (Style?)GetValue(PageButtonStyleProperty); set => SetValue(PageButtonStyleProperty, value); }

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
    /// line buttons. The default follows macOS preferences and is enabled on mobile systems.
    /// </summary>
    internal static readonly DependencyProperty IsOverlayStyleProperty =
        DependencyProperty.Register(nameof(IsOverlayStyle), typeof(bool), typeof(ScrollBar),
            new PropertyMetadata(
                OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ||
                (OperatingSystem.IsMacOS() && Controls.Platform.MacOSScrollBarSettings.PrefersOverlayScrollBars),
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
    public Track? Track => _track;

    internal bool IsThumbDragging => _isDragging;

    internal void SetElasticOverscroll(double overscroll)
    {
        _elasticOverscroll = double.IsFinite(overscroll) ? overscroll : 0;
        _track?.Thumb?.SetVisualCompression(Orientation,
            UseMacOSScrollBarBehavior && !IsThumbDragging ? _elasticOverscroll : 0);
    }

    internal bool UseMacOSScrollBarBehavior { get; set; } = OperatingSystem.IsMacOS();
    private bool UsesLineButtons => !IsOverlayStyle && !UseMacOSScrollBarBehavior;

    private double CurrentOverlayIndicatorThickness => _isOverlayLongPressExpanded ||
        (UseMacOSScrollBarBehavior && (IsMouseOver || _isDragging || IsKeyboardFocusWithin))
            ? OverlayExpandedIndicatorThickness
            : UseMacOSScrollBarBehavior ? 6.0 : OverlayIndicatorThickness;

    internal void ApplyMacOSScrollBarPreferences(bool prefersOverlay)
    {
        Controls.Platform.MacOSScrollBarSettings.ApplyDefault(this, IsOverlayStyleProperty, prefersOverlay);
        if (_autoHideVisualTimer is { IsEnabled: true })
            StartAutoHideTransition(_autoHideVisualAnimTo, _autoHideVisibilityAnimTo);
    }

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
    private Thickness? _appliedOverlayThumbPadding;
    private bool _hasOverlayTrackPresentationSnapshot;
    private bool _overlayTrackHadLocalOpacity;
    private double _overlayTrackLocalOpacity;
    private bool _overlayTrackHadLocalHitTestVisibility;
    private bool _overlayTrackLocalHitTestVisibility;
    private bool _hasAutoHideOpacitySnapshot;
    private bool _autoHideHadLocalOpacity;
    private double _autoHideLocalOpacity;
    private double _autoHideBaseOpacity = 1.0;
    private GestureRecognizer? _overlayThumbGestureRecognizer;
    private int _overlayThumbTouchId = -1;
    private bool _isOverlayThumbTouchDragUnlocked;
    private bool _isOverlayLongPressExpanded;
    private DispatcherTimer? _autoHideVisualTimer;
    private long _autoHideVisualAnimStartTick;
    private double _autoHideVisualAnimFrom;
    private double _autoHideVisualAnimTo;
    private double _autoHideCollapseProgress;
    private double _autoHideVisibilityAnimFrom = 1.0;
    private double _autoHideVisibilityAnimTo = 1.0;
    private double _autoHideVisibilityProgress = 1.0;
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
    private const double AutoHideVisualTransitionDurationMs = 280.0;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollBar"/> class.
    /// </summary>
    static ScrollBar()
    {
        BorderBrushProperty.OverrideMetadata(typeof(ScrollBar), new PropertyMetadata(s_transparentBrush));
        BorderThicknessProperty.OverrideMetadata(typeof(ScrollBar), new PropertyMetadata(default(Thickness)));
        PaddingProperty.OverrideMetadata(typeof(ScrollBar), new PropertyMetadata(new Thickness(2)));
    }

    public ScrollBar()
    {
        // Set default values for range base
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
        Maximum = 100;
        SmallChange = 1;
        LargeChange = 10;

        // Default visuals are supplied by the theme template.

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
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (IsOverlayStyle && UseMacOSScrollBarBehavior)
                ApplyAutoHideVisualState(_autoHideCollapseProgress);
        };
        Unloaded += static (sender, _) => (sender as ScrollBar)?.CompleteInteractionForDetach();
        RegisterScrollCommandBindings();

        _autoHideCollapseProgress = IsThumbSlim ? 1.0 : 0.0;
        ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);
        if (OperatingSystem.IsMacOS())
        {
            Loaded += OnMacOSScrollBarLoaded;
            Controls.Platform.MacOSScrollBarSettings.ApplyDefault(this, IsOverlayStyleProperty,
                Controls.Platform.MacOSScrollBarSettings.PrefersOverlayScrollBars);
        }
        DefaultStyleKey = typeof(ScrollBar);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild("PART_Track") as Track;
        _lineUpButton = GetTemplateChild("PART_LineDecreaseButton") as RepeatButton;
        _lineDownButton = GetTemplateChild("PART_LineIncreaseButton") as RepeatButton;
        if (_lineUpButton is not null) _lineUpButton.Click += OnLineUpClick;
        if (_lineDownButton is not null) _lineDownButton.Click += OnLineDownClick;
        if (_track is not null)
        {
            _track.HandlesThumbDragInternally = false;
            if (_track.DecreaseRepeatButton is { } decrease) { decrease.Click += OnPageUpClick; WirePageButtonTrackPaging(decrease); }
            if (_track.IncreaseRepeatButton is { } increase) { increase.Click += OnPageDownClick; WirePageButtonTrackPaging(increase); }
            if (_track.Thumb is { } thumb)
            {
                thumb.DragStarted += OnThumbDragStarted;
                thumb.DragDelta += OnThumbDragDelta;
                thumb.DragCompleted += OnThumbDragCompleted;
                thumb.AddHandler(TouchDownEvent, new RoutedEventHandler(OnOverlayThumbTouchDown), handledEventsToo: true);
                thumb.AddHandler(TouchMoveEvent, new RoutedEventHandler(OnOverlayThumbTouchMove), handledEventsToo: true);
                thumb.AddHandler(TouchUpEvent, new RoutedEventHandler(OnOverlayThumbTouchUp), handledEventsToo: true);
                thumb.AddHandler(LostTouchCaptureEvent, new RoutedEventHandler(OnOverlayThumbLostTouchCapture), handledEventsToo: true);
            }
        }
        _hasCustomLineButtonStyle = _lineUpButton?.Style is not null || _lineDownButton?.Style is not null;
        UpdateTemplateLayout(RenderSize.Width);
        UpdateTrackBindings();
        if (_ownerCssColors is { } colors) { _ownerCssColors = null; SetOwnerCssColors(colors); }
        ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);
        ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);
    }

    internal override void OnTemplateContentClearing()
    {
        CompleteInteractionForDetach();
        _hasTrackPagingTarget = false;
        _isDragging = false;
        if (_track is not null) RestoreTrackPresentationAfterOverlay(_track);
        if (_track?.Thumb is { } retiredThumb)
        {
            RestoreThumbPaddingAfterOverlay(retiredThumb);
            if (_ownerCssColors is not null)
            {
                retiredThumb.ClearLayerValue(BackgroundProperty, DependencyObject.LayerValueSource.ParentTemplate);
                retiredThumb.ClearLayerValue(TemplateProperty, DependencyObject.LayerValueSource.ParentTemplate);
            }
        }
        if (_lineUpButton is not null) _lineUpButton.Click -= OnLineUpClick;
        if (_lineDownButton is not null) _lineDownButton.Click -= OnLineDownClick;
        if (_track?.DecreaseRepeatButton is { } decrease) { decrease.Click -= OnPageUpClick; UnwirePageButton(decrease); }
        if (_track?.IncreaseRepeatButton is { } increase) { increase.Click -= OnPageDownClick; UnwirePageButton(increase); }
        if (_track?.Thumb is { } thumb)
        {
            thumb.DragStarted -= OnThumbDragStarted;
            thumb.DragDelta -= OnThumbDragDelta;
            thumb.DragCompleted -= OnThumbDragCompleted;
            thumb.RemoveHandler(TouchDownEvent, new RoutedEventHandler(OnOverlayThumbTouchDown));
            thumb.RemoveHandler(TouchMoveEvent, new RoutedEventHandler(OnOverlayThumbTouchMove));
            thumb.RemoveHandler(TouchUpEvent, new RoutedEventHandler(OnOverlayThumbTouchUp));
            thumb.RemoveHandler(LostTouchCaptureEvent, new RoutedEventHandler(OnOverlayThumbLostTouchCapture));
        }
        _track = null;
        _lineUpButton = _lineDownButton = null;
        _fallbackThumbLocalBrush = null;
        base.OnTemplateContentClearing();
    }

    private void UnwirePageButton(RepeatButton button)
    {
        button.RemoveHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPageButtonPreviewMouseDown));
        button.RemoveHandler(MouseMoveEvent, new MouseEventHandler(OnPageButtonMouseMove));
        button.RemoveHandler(MouseUpEvent, new MouseButtonEventHandler(OnPageButtonMouseUp));
        button.RemoveHandler(LostMouseCaptureEvent, new RoutedEventHandler(OnPageButtonLostMouseCapture));
    }

    private void UpdateTemplateLayout(double availableWidth)
    {
        double width = double.IsFinite(availableWidth) ? availableWidth : RenderSize.Width;
        var insets = GetPartsInsets(width);
        SetValue(PartsInsetsPropertyKey, insets);
        double thickness = Orientation == Orientation.Vertical
            ? double.IsNaN(Width) ? DefaultThickness : Math.Max(0, Width - insets.Left - insets.Right)
            : double.IsNaN(Height) ? DefaultThickness : Math.Max(0, Height - insets.Top - insets.Bottom);
        SetValue(NaturalThicknessPropertyKey, thickness);
        SetValue(HasLineButtonsPropertyKey, UsesLineButtons);
        bool css = CssBackgroundPaintProperties.Get(this) is not null || CssBorderPaintProperties.Get(this) is not null ||
            GetEffectiveValueLayer(BackgroundProperty) is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState ||
            GetEffectiveValueLayer(PaddingProperty) is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState ||
            GetEffectiveValueLayer(BorderThicknessProperty) is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState;
        SetValue(ChromeInsetsPropertyKey, css ? default(Thickness) : Padding);
        UpdateLineButtonDirectionTags();
        if (TemplateRootInternal is not null)
        {
            VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
            VisualStateManager.GoToState(this, Orientation == Orientation.Vertical ? "Vertical" : "Horizontal", false);
            VisualStateManager.GoToState(this, IsOverlayStyle ? "Overlay" : "Persistent", false);
            VisualStateManager.GoToState(this, _autoHideCollapseProgress >= .999 ? "Slim" : "Expanded", false);
        }
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateTemplateLayout(RenderSize.Width);
    }

    private void OnMacOSScrollBarLoaded(object sender, RoutedEventArgs e)
        => ApplyMacOSScrollBarPreferences(Controls.Platform.MacOSScrollBarSettings.PrefersOverlayScrollBars);

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

        SetCurrentValue(ValueProperty, value);
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
        UpdateTemplateLayout(RenderSize.Width);
        ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);
        ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);
        if (VisualParent == null) CompleteInteractionForDetach();
    }

    internal void CompleteInteractionForDetach()
    {
        bool transitioning = _autoHideVisualTimer?.IsEnabled == true;
        StopAutoHideVisualTimer();
        CompleteOverlayThumbGesture();
        if (transitioning)
        {
            _autoHideCollapseProgress = _autoHideVisualAnimTo;
            _autoHideVisibilityProgress = _autoHideVisibilityAnimTo;
            ApplyAutoHideVisualState(_autoHideCollapseProgress, null, suppressArrangeInvalidation: true);
            ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);
        }
        _lineUpButton?.CompleteInteractionForDetach();
        _lineDownButton?.CompleteInteractionForDetach();
        _track?.DecreaseRepeatButton?.CompleteInteractionForDetach();
        _track?.IncreaseRepeatButton?.CompleteInteractionForDetach();
    }

    private void OnResourcesChangedHandler(object? sender, EventArgs e)
    {
        ApplySelfStyle();
        UpdateTemplateLayout(RenderSize.Width);
        UpdateTrackBindings();
        ApplyAutoHideVisualState(_autoHideCollapseProgress);
        ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void UpdateTrackBindings()
    {
        if (_track != null)
        {
            _track.SetCurrentValue(Track.MinimumProperty, Minimum);
            _track.SetCurrentValue(Track.MaximumProperty, Maximum);
            _track.SetCurrentValue(Track.ValueProperty, Value);
            _track.SetCurrentValue(Track.ViewportSizeProperty, ViewportSize);
            _track.SetCurrentValue(Track.OrientationProperty, Orientation);
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

                SetElasticOverscroll(_elasticOverscroll);
            }
        }

        UpdateLineButtonDirectionTags();
    }



    private void ApplySelfStyle()
    {
        if (Style != null)
        {
            return;
        }

        if (ResolveStyleResource(ScrollBarStyleKey) is Style explicitStyle)
        {
            Style = explicitStyle;
            return;
        }

        if (TryFindResource(typeof(ScrollBar)) is Style implicitStyle)
        {
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

    internal void SetOwnerCssColors(CssScrollBarColors? colors)
    {
        if (ReferenceEquals(_ownerCssColors, colors))
            return;

        var thumb = _track?.Thumb;
        var wasStyled = _ownerCssColors is not null;
        _ownerCssColors = colors;
        if (colors is not null)
        {
            if (!wasStyled && thumb is not null && _fallbackThumbLocalBrush is not null &&
                ReferenceEquals(thumb.ReadLocalValue(BackgroundProperty), _fallbackThumbLocalBrush))
                thumb.ClearValue(BackgroundProperty);
            SetLayerValue(BackgroundProperty, colors.TrackBrush,
                DependencyObject.LayerValueSource.ParentTemplate);
            thumb?.SetLayerValue(BackgroundProperty, colors.ThumbBrush,
                DependencyObject.LayerValueSource.ParentTemplate);
            if (!wasStyled)
            {
                // The theme template supplies unrelated fixed hover and drag colors.
                // Use a plain background-bound template while CSS colors apply.
                // An explicit native local template still has higher priority.
                if (thumb?.TryFindResource("ScrollBarPlainThumbTemplate") is ControlTemplate plain)
                    thumb.SetLayerValue(TemplateProperty, plain, DependencyObject.LayerValueSource.ParentTemplate);
            }
        }
        else if (wasStyled)
        {
            ClearLayerValue(BackgroundProperty, DependencyObject.LayerValueSource.ParentTemplate);
            thumb?.ClearLayerValue(BackgroundProperty, DependencyObject.LayerValueSource.ParentTemplate);
            thumb?.ClearLayerValue(TemplateProperty, DependencyObject.LayerValueSource.ParentTemplate);
            if (thumb is not null)
                EnsureThumbVisibilityFallback(thumb);
        }

        InvalidateVisual();
    }

    private void EnsureThumbVisibilityFallback(Thumb thumb)
    {
        if (thumb.Background == null)
        {
            _fallbackThumbLocalBrush = ResolveThumbBrush();
            thumb.Background = _fallbackThumbLocalBrush;
        }
    }

    private void UpdateLineButtonDirectionTags()
    {
        SetValue(DecreaseDirectionPropertyKey, Orientation == Orientation.Vertical ? "Up" : "Left");
        SetValue(IncreaseDirectionPropertyKey, Orientation == Orientation.Vertical ? "Down" : "Right");
    }

    #endregion

    #region Layout

    internal override void OnEffectiveValueSourceChanged(DependencyProperty property)
    {
        base.OnEffectiveValueSourceChanged(property);
        if (property == PaddingProperty || property == BorderThicknessProperty)
            InvalidateMeasure();
    }

    private Thickness GetPartsInsets(double fallbackWidth)
    {
        // The built-in 2-DIP padding has historically inset track paint only.
        // Keep that default chrome geometry until an authored box edge asks the
        // buttons and track to occupy the actual content box.
        if (Padding == new Thickness(2) && BorderThickness == new Thickness(0) &&
            !HasLocalOrAnimatedValue(PaddingProperty) &&
            !HasLocalOrAnimatedValue(BorderThicknessProperty) &&
            GetEffectiveValueLayer(PaddingProperty) is not
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState) &&
            GetEffectiveValueLayer(BorderThicknessProperty) is not
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
            return default;

        return CssBoxMetrics.ContentInsets(this, CssLayout?.ContainingWidthCache ?? fallbackWidth);
    }

    private Rect GetPartsRect(Size outerSize)
    {
        var insets = GetPartsInsets(outerSize.Width);
        var innerSize = CssBoxMetrics.InnerSize(outerSize, insets);
        return new Rect(insets.Left, insets.Top, innerSize.Width, innerSize.Height);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateDefaultStyle();
        ApplyTemplate();
        UpdateTemplateLayout(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateTemplateLayout(finalSize.Width);
        UpdateTrackBindings();
        var inner = GetPartsRect(finalSize);
        ApplyAutoHideVisualState(_autoHideCollapseProgress,
            Orientation == Orientation.Vertical ? inner.Width : inner.Height, suppressArrangeInvalidation: true);
        return base.ArrangeOverride(finalSize);
    }

    #endregion

    #region Event Handlers

    private void OnLineUpClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Max(Minimum, Value - SmallChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            SetCurrentValue(ValueProperty, newValue);
            RaiseScrollEvent(ScrollEventType.SmallDecrement);
        }
    }

    private void OnLineDownClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Min(Maximum, Value + SmallChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            SetCurrentValue(ValueProperty, newValue);
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
            SetCurrentValue(ValueProperty, newValue);
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
            SetCurrentValue(ValueProperty, newValue);
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
                SetCurrentValue(ValueProperty, newValue);
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
            scrollBar.UpdateTemplateLayout(scrollBar.RenderSize.Width);
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
            scrollBar.UpdateTemplateLayout(scrollBar.RenderSize.Width);
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

        scrollBar.UpdateTemplateLayout(scrollBar.RenderSize.Width);
        scrollBar.UpdateTrackBindings();
        scrollBar.ApplyAutoHideVisualState(
            scrollBar._autoHideCollapseProgress,
            null,
            suppressArrangeInvalidation: true);
        scrollBar.ApplyAutoHideVisibilityState(scrollBar._autoHideVisibilityProgress);
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
        var targetVisibility = _autoHideVisualTimer is { IsEnabled: true }
            ? _autoHideVisibilityAnimTo
            : _autoHideVisibilityProgress;
        StartAutoHideTransition(targetProgress, targetVisibility);
    }

    internal void StartAutoHideVisibilityTransition(double targetProgress)
    {
        var targetCollapse = _autoHideVisualTimer is { IsEnabled: true }
            ? _autoHideVisualAnimTo
            : _autoHideCollapseProgress;
        StartAutoHideTransition(targetCollapse, targetProgress);
    }

    private void StartAutoHideTransition(double targetCollapseProgress, double targetVisibilityProgress)
    {
        targetCollapseProgress = Math.Clamp(targetCollapseProgress, 0.0, 1.0);
        targetVisibilityProgress = Math.Clamp(targetVisibilityProgress, 0.0, 1.0);

        if (UseMacOSScrollBarBehavior && Controls.Platform.MacOSScrollBarSettings.PrefersReducedMotion)
        {
            StopAutoHideVisualTimer();
            ApplyAutoHideVisualState(targetCollapseProgress);
            ApplyAutoHideVisibilityState(targetVisibilityProgress);
            return;
        }

        if (Math.Abs(_autoHideCollapseProgress - targetCollapseProgress) <= 0.001 &&
            Math.Abs(_autoHideVisibilityProgress - targetVisibilityProgress) <= 0.001)
        {
            _autoHideCollapseProgress = targetCollapseProgress;
            _autoHideVisibilityProgress = targetVisibilityProgress;
            // The target values are already painted. Reapplying them is not a
            // harmless no-op because ApplyAutoHideVisualState invalidates the
            // ScrollBar unconditionally. Nested ScrollViewer MouseEnter routes
            // otherwise multiply one pointer sample into redundant dirty work.
            StopAutoHideVisualTimer();
            return;
        }

        // Arrange can ask for the same state on every layout pass. Do not
        // restart an in-flight transition toward those targets or a busy layout
        // loop could keep the indicator partially transitioned indefinitely.
        if (_autoHideVisualTimer is { IsEnabled: true } &&
            Math.Abs(_autoHideVisualAnimTo - targetCollapseProgress) <= 0.001 &&
            Math.Abs(_autoHideVisibilityAnimTo - targetVisibilityProgress) <= 0.001)
        {
            return;
        }

        _autoHideVisualAnimFrom = _autoHideCollapseProgress;
        _autoHideVisualAnimTo = targetCollapseProgress;
        _autoHideVisibilityAnimFrom = _autoHideVisibilityProgress;
        _autoHideVisibilityAnimTo = targetVisibilityProgress;
        _autoHideVisualAnimStartTick = Environment.TickCount64;

        EnsureAutoHideVisualTimer();
        _autoHideVisualTimer!.Start();
        ApplyAutoHideVisualState(_autoHideCollapseProgress);
        ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);
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
        _autoHideVisibilityProgress = Lerp(
            _autoHideVisibilityAnimFrom,
            _autoHideVisibilityAnimTo,
            eased);
        ApplyAutoHideVisualState(_autoHideCollapseProgress);
        ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);

        if (raw >= 1.0)
        {
            _autoHideCollapseProgress = _autoHideVisualAnimTo;
            _autoHideVisibilityProgress = _autoHideVisibilityAnimTo;
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
            ApplyAutoHideVisibilityState(_autoHideVisibilityProgress);
            StopAutoHideVisualTimer();
        }
    }

    private void ApplyAutoHideVisualState(double collapseProgress, double? crossAxisSize = null, bool suppressArrangeInvalidation = false)
    {
        collapseProgress = Math.Clamp(collapseProgress, 0.0, 1.0);
        _autoHideCollapseProgress = collapseProgress;
        _chromeOpacity = IsOverlayStyle ? 0.0 : 1.0 - collapseProgress;

        SetValue(ChromeOpacityPropertyKey, _chromeOpacity);
        SetValue(LineButtonsVisibilityPropertyKey, UsesLineButtons && collapseProgress < .999 ? Visibility.Visible : Visibility.Collapsed);
        SetValue(HasLineButtonsPropertyKey, UsesLineButtons);
        if (TemplateRootInternal is not null)
            VisualStateManager.GoToState(this, collapseProgress >= .999 ? "Slim" : "Expanded", false);

        if (_track?.DecreaseRepeatButton != null && _track.IncreaseRepeatButton != null)
        {
            // The transparent overlay gutter passes input through to content;
            // only the thumb stays interactive. Persistent bars retain track paging.
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
                ApplyOverlayThumbInsets(
                    _track.Thumb,
                    thumbThickness,
                    CurrentOverlayIndicatorThickness,
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
            var parts = GetPartsRect(RenderSize);
            crossAxisSize = Orientation == Orientation.Vertical ? parts.Width : parts.Height;
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
            if (_overlayThumbHadLocalPadding) _overlayThumbLocalPadding = thumb.Padding;

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

        _appliedOverlayThumbPadding = padding;
        if (thumb.Padding != padding)
        {
            if (_overlayThumbHadLocalPadding) thumb.SetCurrentValue(PaddingProperty, padding);
            else thumb.Padding = padding;
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
            thumb.SetCurrentValue(PaddingProperty, _overlayThumbLocalPadding);
        }
        else
        {
            thumb.ClearValue(PaddingProperty);
            // A containing control template can promote constructor-created
            // visual children's local values into its parent-template layer.
            // Remove only our calculated inset if it was promoted this way;
            // a different padding supplied by the template must remain intact.
            if (_appliedOverlayThumbPadding is { } applied && thumb.Padding == applied &&
                thumb.GetEffectiveValueLayer(PaddingProperty) == DependencyValueStore.Layer.ParentTemplate)
                thumb.ClearLayerValue(PaddingProperty, DependencyObject.LayerValueSource.ParentTemplate);
        }

        _hasOverlayThumbPaddingSnapshot = false;
        _overlayThumbHadLocalPadding = false;
        _overlayThumbLocalPadding = default;
        _appliedOverlayThumbPadding = null;
    }

    private void ApplyAutoHideVisibilityState(double visibilityProgress)
    {
        visibilityProgress = Math.Clamp(visibilityProgress, 0.0, 1.0);
        _autoHideVisibilityProgress = visibilityProgress;

        // Do not introduce a local Opacity value while the bar is fully shown.
        // Keeping the property untouched lets theme/style changes continue to flow.
        if (!_hasAutoHideOpacitySnapshot)
        {
            if (visibilityProgress >= 0.999)
                return;

            _autoHideHadLocalOpacity = HasLocalValue(UIElement.OpacityProperty);
            if (_autoHideHadLocalOpacity &&
                ReadLocalValue(UIElement.OpacityProperty) is double localOpacity)
            {
                _autoHideLocalOpacity = localOpacity;
            }

            _autoHideBaseOpacity = double.IsFinite(Opacity)
                ? Math.Clamp(Opacity, 0.0, 1.0)
                : 1.0;
            _hasAutoHideOpacitySnapshot = true;
        }

        if (visibilityProgress >= 0.999)
        {
            RestoreAutoHideOpacity();
            return;
        }

        // Opacity remains hit-testable at zero, so the invisible ScrollBar can
        // still receive MouseEnter while shape and visibility animate separately.
        Opacity = _autoHideBaseOpacity * visibilityProgress;
    }

    private void RestoreAutoHideOpacity()
    {
        if (!_hasAutoHideOpacitySnapshot)
            return;

        if (_autoHideHadLocalOpacity)
        {
            Opacity = _autoHideLocalOpacity;
        }
        else
        {
            ClearValue(UIElement.OpacityProperty);
        }

        _hasAutoHideOpacitySnapshot = false;
        _autoHideHadLocalOpacity = false;
        _autoHideLocalOpacity = default;
        _autoHideBaseOpacity = 1.0;
    }

    private void ApplyOverlayTrackPresentation(Track track, double opacity, bool isHitTestVisible)
    {
        if (!_hasOverlayTrackPresentationSnapshot)
        {
            _overlayTrackHadLocalOpacity = track.HasLocalValue(UIElement.OpacityProperty);
            if (_overlayTrackHadLocalOpacity) _overlayTrackLocalOpacity = track.Opacity;

            _overlayTrackHadLocalHitTestVisibility = track.HasLocalValue(UIElement.IsHitTestVisibleProperty);
            if (_overlayTrackHadLocalHitTestVisibility) _overlayTrackLocalHitTestVisibility = track.IsHitTestVisible;

            _hasOverlayTrackPresentationSnapshot = true;
        }

        if (_overlayTrackHadLocalOpacity) track.SetCurrentValue(UIElement.OpacityProperty, Math.Clamp(opacity, 0.0, 1.0));
        else track.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        if (_overlayTrackHadLocalHitTestVisibility) track.SetCurrentValue(UIElement.IsHitTestVisibleProperty, isHitTestVisible);
        else track.IsHitTestVisible = isHitTestVisible;
    }

    private void RestoreTrackPresentationAfterOverlay(Track track)
    {
        if (!_hasOverlayTrackPresentationSnapshot)
            return;

        if (_overlayTrackHadLocalOpacity)
        {
            track.SetCurrentValue(UIElement.OpacityProperty, _overlayTrackLocalOpacity);
        }
        else
        {
            track.ClearValue(UIElement.OpacityProperty);
        }

        if (_overlayTrackHadLocalHitTestVisibility)
        {
            track.SetCurrentValue(UIElement.IsHitTestVisibleProperty, _overlayTrackLocalHitTestVisibility);
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
        SetElasticOverscroll(0);
        _hasTrackPagingTarget = false;
        _thumbDragStartValue = Value;
        _thumbDragAccumulatedHorizontal = 0;
        _thumbDragAccumulatedVertical = 0;

        if (IsOverlayStyle)
        {
            StartAutoHideVisualTransition(0.0);
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
        }
    }

    private void EndThumbDrag()
    {
        CompleteOverlayThumbGesture();
        _isDragging = false;
        _thumbDragAccumulatedHorizontal = 0;
        _thumbDragAccumulatedVertical = 0;
        if (IsOverlayStyle && UseMacOSScrollBarBehavior)
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
    }

    #endregion

    #region Overrides

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled || !IsEnabled) return;
        switch (e.Key)
        {
            case Key.Up when Orientation == Orientation.Vertical:
            case Key.Left when Orientation == Orientation.Horizontal:
                ChangeBy(-SmallChange, ScrollEventType.SmallDecrement); break;
            case Key.Down when Orientation == Orientation.Vertical:
            case Key.Right when Orientation == Orientation.Horizontal:
                ChangeBy(SmallChange, ScrollEventType.SmallIncrement); break;
            case Key.PageUp:
                ChangeBy(-LargeChange, ScrollEventType.LargeDecrement); break;
            case Key.PageDown:
                ChangeBy(LargeChange, ScrollEventType.LargeIncrement); break;
            case Key.Home:
                ChangeTo(Minimum, ScrollEventType.First); break;
            case Key.End:
                ChangeTo(Maximum, ScrollEventType.Last); break;
            default:
                base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (IsOverlayStyle && UseMacOSScrollBarBehavior)
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (IsOverlayStyle && UseMacOSScrollBarBehavior)
            ApplyAutoHideVisualState(_autoHideCollapseProgress);
    }

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
                // The Thumb fills the wider layout host to allow inward expansion.
                // macOS has a 12-DIP mouse target; mobile keeps the original 2-DIP
                // strip until Hold. Capture continues dragging outside the strip.
                return IsPointWithinOverlayIndicator(point) ? result : null;
            }
        }

        return null;
    }

    private bool IsPointWithinOverlayIndicator(Point point)
    {
        var parts = GetPartsRect(RenderSize);
        var crossAxisSize = Orientation == Orientation.Vertical ? parts.Width : parts.Height;
        if (!double.IsFinite(crossAxisSize) || crossAxisSize <= 0)
            return false;

        var edgeInset = Math.Min(OverlayIndicatorEdgeInset, crossAxisSize);
        var indicatorThickness = Math.Min(
            UseMacOSScrollBarBehavior ? Math.Max(12.0, CurrentOverlayIndicatorThickness) : OverlayIndicatorThickness,
            Math.Max(0, crossAxisSize - edgeInset));
        var indicatorStart = Math.Max(0, crossAxisSize - edgeInset - indicatorThickness);
        var indicatorEnd = indicatorStart + indicatorThickness;
        // FrameworkElement.HitTestCore receives points in the parent coordinate
        // space; normalize to this ScrollBar before comparing with the parts rect.
        var crossAxisPoint = Orientation == Orientation.Vertical
            ? point.X - VisualBounds.X
            : point.Y - VisualBounds.Y;
        var crossAxisStart = Orientation == Orientation.Vertical ? parts.Left : parts.Top;

        return crossAxisPoint >= crossAxisStart + indicatorStart &&
               crossAxisPoint <= crossAxisStart + indicatorEnd;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (TemplateRootInternal is not null) return;
        var dc = drawingContext;

        var innerRect = new Rect(
            Padding.Left,
            Padding.Top,
            Math.Max(0, RenderSize.Width - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - Padding.Top - Padding.Bottom));
        var chromeOpacity = Math.Clamp(_chromeOpacity, 0.0, 1.0);
        if (chromeOpacity <= 0.001)
            return;

        dc.PushOpacity(chromeOpacity);

        var backdropEffect = BackdropEffect;
        if (!HasCssBackdropEffect && innerRect.Width > 0 && innerRect.Height > 0 &&
            (backdropEffect ?? s_defaultTrackBackdropEffect).HasEffect && backdropEffect != null)
        {
            dc.DrawBackdropEffect(innerRect, backdropEffect, CornerRadius);
        }

        var background = Background;
        var cssBackgroundDrawn = false;
        if (background != null)
        {
            var outer = new Rect(RenderSize);
            var radii = CssBorderRadiusProperties.Get(this)?.Resolve(RenderSize) ??
                CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize);
            var shape = new CssRoundedRectangleGeometry(outer, radii);
            cssBackgroundDrawn = CssBackgroundPainter.TryDraw(this, BackgroundProperty, background, dc,
                outer, radii, BorderThickness, Padding,
                brush => dc.DrawGeometry(brush, null, shape));
        }

        var cssBackgroundNone = background is null &&
            GetEffectiveValueLayer(BackgroundProperty) is
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
        if (!cssBackgroundDrawn && !cssBackgroundNone &&
            innerRect.Width > 0 && innerRect.Height > 0)
            dc.DrawRoundedRectangle(background ?? ResolveTrackBrush(), null, innerRect, CornerRadius);

        if (innerRect.Width > 0 && innerRect.Height > 0 &&
            CssBorderPaintProperties.Get(this) is null && BorderBrush != null && BorderThickness.TotalWidth > 0)
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

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        if (TemplateRootInternal is not null || CssBorderPaintProperties.Get(this) is null) return;
        var opacity = Math.Clamp(_chromeOpacity, 0.0, 1.0);
        if (opacity <= 0.001) return;
        drawingContext.PushOpacity(opacity);
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter);
        drawingContext.Pop();
    }

    private void DrawFallbackArrows(DrawingContext dc)
    {
        if (!UsesLineButtons)
            return;
        const double baseArrowSize = 8.0;
        var parts = GetPartsRect(RenderSize);
        var fallbackArrowBrush = ResolveArrowBrush();
        var upBrush = (_lineUpButton?.Foreground as Brush) ?? fallbackArrowBrush;
        var downBrush = (_lineDownButton?.Foreground as Brush) ?? fallbackArrowBrush;
        var upScale = Math.Clamp(_lineUpButton?.CurrentScrollBarArrowScale ?? 1.0, 0.7, 1.25);
        var downScale = Math.Clamp(_lineDownButton?.CurrentScrollBarArrowScale ?? 1.0, 0.7, 1.25);
        var upArrowSize = baseArrowSize * upScale;
        var downArrowSize = baseArrowSize * downScale;

        if (Orientation == Orientation.Vertical)
        {
            var buttonSize = parts.Width;
            if (buttonSize <= 0 || parts.Height < buttonSize * 2)
                return;

            var topCenter = new Point(parts.Left + parts.Width / 2, parts.Top + buttonSize / 2);
            var bottomCenter = new Point(parts.Left + parts.Width / 2, parts.Bottom - buttonSize / 2);

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
            var buttonSize = parts.Height;
            if (buttonSize <= 0 || parts.Width < buttonSize * 2)
                return;

            var leftCenter = new Point(parts.Left + buttonSize / 2, parts.Top + parts.Height / 2);
            var rightCenter = new Point(parts.Right - buttonSize / 2, parts.Top + parts.Height / 2);

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
