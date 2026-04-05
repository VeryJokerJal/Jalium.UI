using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a switch that can be toggled between on and off states,
/// with spring-physics-driven animations for hover, press, drag, and toggle.
/// </summary>
public class ToggleSwitch : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ToggleSwitchAutomationPeer(this);
    }

    #region Animation Constants

    private const double TrackWidth = 44.0;
    private const double TrackHeight = 20.0;
    private const double TrackBorderThickness = 1.0;
    private const double TrackInnerWidth = TrackWidth - 2 * TrackBorderThickness; // 42
    private const double TrackInnerHeight = TrackHeight - 2 * TrackBorderThickness; // 18
    private const double ThumbPadding = 3.0;

    private const double ThumbDefaultSize = 14.0;
    private const double ThumbHoverSize = 15.0;
    private const double ThumbPressedWidth = 17.0;

    private const double PositionStiffness = 800.0;
    private const double PositionDamping = 0.85;
    private const double SizeStiffness = 1200.0;
    private const double SizeDamping = 0.75;

    private const double DragThreshold = 3.0;

    // Theme colors (matching Colors.jalxaml)
    private static readonly Color OffColor = Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D);
    private static readonly Color OnColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
    private static readonly Color OffBorderColor = Color.FromArgb(0xFF, 0x8C, 0x8C, 0x8C);
    private static readonly Color OnBorderColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
    private static readonly Color DisabledBgColor = Color.FromArgb(0xFF, 0x28, 0x28, 0x28);
    private static readonly Color DisabledBorderColor = Color.FromArgb(0xFF, 0x46, 0x46, 0x46);
    private static readonly SolidColorBrush s_disabledBgBrush = new(DisabledBgColor);
    private static readonly SolidColorBrush s_disabledBorderBrush = new(DisabledBorderColor);

    #endregion

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(ToggleSwitch),
            new PropertyMetadata(false, OnIsOnChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty OnContentProperty =
        DependencyProperty.Register(nameof(OnContent), typeof(object), typeof(ToggleSwitch),
            new PropertyMetadata("On", OnContentPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty OffContentProperty =
        DependencyProperty.Register(nameof(OffContent), typeof(object), typeof(ToggleSwitch),
            new PropertyMetadata("Off", OnContentPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(ToggleSwitch),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty OnBackgroundProperty =
        DependencyProperty.Register(nameof(OnBackground), typeof(Brush), typeof(ToggleSwitch),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty OffBackgroundProperty =
        DependencyProperty.Register(nameof(OffBackground), typeof(Brush), typeof(ToggleSwitch),
            new PropertyMetadata(null));

    #endregion

    #region Routed Events

    public static readonly RoutedEvent ToggledEvent =
        EventManager.RegisterRoutedEvent(nameof(Toggled), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToggleSwitch));

    public event RoutedEventHandler Toggled
    {
        add => AddHandler(ToggledEvent, value);
        remove => RemoveHandler(ToggledEvent, value);
    }

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty)!;
        set => SetValue(IsOnProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? OnContent
    {
        get => GetValue(OnContentProperty);
        set => SetValue(OnContentProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? OffContent
    {
        get => GetValue(OffContentProperty);
        set => SetValue(OffContentProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? OnBackground
    {
        get => (Brush?)GetValue(OnBackgroundProperty);
        set => SetValue(OnBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? OffBackground
    {
        get => (Brush?)GetValue(OffBackgroundProperty);
        set => SetValue(OffBackgroundProperty, value);
    }

    #endregion

    #region Template Parts

    private Border? _switchTrack;
    private Border? _switchThumb;
    private ContentPresenter? _contentPresenter;
    private ContentPresenter? _headerPresenter;

    #endregion

    #region Interaction State

    private enum InteractionState
    {
        Idle,
        Hovered,
        Pressed,
        Dragging,
    }

    private InteractionState _state = InteractionState.Idle;
    private Point _pressStartPoint;
    private double _pressStartProgress;
    private bool _hasDragged;

    #endregion

    #region Spring Animation State

    private SpringAxis _positionSpring;
    private SpringAxis _thumbWidthSpring;
    private SpringAxis _thumbHeightSpring;
    private long _lastTickTime;
    private bool _springSubscribed;


    #endregion

    #region Constructor

    public ToggleSwitch()
    {
        Focusable = true;

        _positionSpring = new SpringAxis { Position = 0.0, Target = 0.0 };
        _thumbWidthSpring = new SpringAxis { Position = ThumbDefaultSize, Target = ThumbDefaultSize };
        _thumbHeightSpring = new SpringAxis { Position = ThumbDefaultSize, Target = ThumbDefaultSize };

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(LostMouseCaptureEvent, new RoutedEventHandler(OnLostMouseCaptureHandler));
    }

    #endregion

    #region Template Initialization

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _switchTrack = GetTemplateChild("PART_SwitchTrack") as Border;
        _switchThumb = GetTemplateChild("PART_SwitchThumb") as Border;
        _contentPresenter = GetTemplateChild("PART_ContentPresenter") as ContentPresenter;
        _headerPresenter = GetTemplateChild("PART_Header") as ContentPresenter;


        if (_switchThumb != null)
        {
            _switchThumb.HorizontalAlignment = HorizontalAlignment.Left;
            _switchThumb.VerticalAlignment = VerticalAlignment.Top;
        }

        UpdateHeaderVisibility();
        UpdateContentPresenter();

        // Snap springs to current IsOn state (no animation on first load)
        SyncSpringsToIsOn(animate: false);
        ApplySpringValues();
    }

    #endregion

    #region Spring Helpers

    private void SyncSpringsToIsOn(bool animate)
    {
        double target = IsOn ? 1.0 : 0.0;
        _positionSpring.Target = target;

        if (!animate)
        {
            _positionSpring.Position = target;
            _positionSpring.Velocity = 0;
        }
    }

    private Color GetOffColor()
    {
        return (OffBackground as SolidColorBrush)?.Color ?? OffColor;
    }

    private Color GetOnColor()
    {
        return (OnBackground as SolidColorBrush)?.Color ?? OnColor;
    }

    private Color GetOffBorderColor()
    {
        return ResolveThemeColor("ToggleUncheckedBorder", OffBorderColor);
    }

    private Color GetOnBorderColor()
    {
        return ResolveThemeColor("ToggleCheckedBorder", OnBorderColor);
    }

    private Brush ResolveDisabledTrackBackground()
    {
        return TryFindResource("ToggleDisabledBackground") as Brush ?? s_disabledBgBrush;
    }

    private Brush ResolveDisabledTrackBorderBrush()
    {
        return TryFindResource("ToggleDisabledBorder") as Brush ?? s_disabledBorderBrush;
    }

    private Color ResolveThemeColor(string resourceKey, Color fallback)
    {
        return (TryFindResource(resourceKey) as SolidColorBrush)?.Color ?? fallback;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static double ComputeThumbMarginLeft(double progress, double thumbWidth)
    {
        double travel = TrackInnerWidth - thumbWidth - 2 * ThumbPadding;
        return ThumbPadding + progress * Math.Max(0, travel);
    }

    #endregion

    #region Frame Animation Loop

    private void StartSpringAnimation()
    {
        if (_springSubscribed) return;
        _lastTickTime = Environment.TickCount64;
        _springSubscribed = true;
        CompositionTarget.Rendering += OnSpringTick;
        CompositionTarget.Subscribe();
    }

    private void StopSpringAnimation()
    {
        if (!_springSubscribed) return;
        _springSubscribed = false;
        CompositionTarget.Rendering -= OnSpringTick;
        CompositionTarget.Unsubscribe();
    }

    private void OnSpringTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = (now - _lastTickTime) / 1000.0;
        _lastTickTime = now;

        if (dt <= 0) return;

        bool posSettled = _positionSpring.Step(dt, PositionStiffness, PositionDamping);
        bool wSettled = _thumbWidthSpring.Step(dt, SizeStiffness, SizeDamping);
        bool hSettled = _thumbHeightSpring.Step(dt, SizeStiffness, SizeDamping);

        ApplySpringValues();

        if (posSettled && wSettled && hSettled &&
            _state != InteractionState.Pressed && _state != InteractionState.Dragging)
        {
            StopSpringAnimation();
        }
    }

    private void ApplySpringValues()
    {
        if (_switchThumb == null || _switchTrack == null) return;

        double thumbW = _thumbWidthSpring.Position;
        double thumbH = _thumbHeightSpring.Position;
        double progress = Math.Clamp(_positionSpring.Position, 0.0, 1.0);

        // Thumb size and shape
        _switchThumb.Width = thumbW;
        _switchThumb.Height = thumbH;
        _switchThumb.CornerRadius = new CornerRadius(thumbH / 2.0);

        // Thumb position — use the TARGET width for margin calculation so that
        // underdamped size oscillations don't cause the thumb to jitter horizontally.
        // VerticalAlignment is Top, so we compute the vertical centering ourselves
        // with pixel rounding to avoid subpixel jitter.
        double marginLeft = ComputeThumbMarginLeft(progress, _thumbWidthSpring.Target);
        double marginTop = Math.Round((TrackInnerHeight - thumbH) / 2.0);
        _switchThumb.Margin = new Thickness(marginLeft, marginTop, 0, 0);

        // Track colors (interpolate based on position)
        if (!IsEnabled) return; // disabled colors handled separately

        var offBg = GetOffColor();
        var onBg = GetOnColor();
        _switchTrack.Background = new SolidColorBrush(LerpColor(offBg, onBg, progress));
        _switchTrack.BorderBrush = new SolidColorBrush(LerpColor(GetOffBorderColor(), GetOnBorderColor(), progress));
    }

    #endregion

    #region Mouse Interaction

    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        base.OnIsMouseOverChanged(oldValue, newValue);

        if (!IsEnabled) return;
        if (_state == InteractionState.Pressed || _state == InteractionState.Dragging)
            return;

        if (newValue)
        {
            _state = InteractionState.Hovered;
            _thumbWidthSpring.Target = ThumbHoverSize;
            _thumbHeightSpring.Target = ThumbHoverSize;
        }
        else
        {
            _state = InteractionState.Idle;
            _thumbWidthSpring.Target = ThumbDefaultSize;
            _thumbHeightSpring.Target = ThumbDefaultSize;
        }

        StartSpringAnimation();
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        if (e.ChangedButton != MouseButton.Left) return;

        Focus();

        _state = InteractionState.Pressed;
        _pressStartPoint = e.GetPosition((UIElement?)_switchTrack ?? this);
        _pressStartProgress = _positionSpring.Position;
        _hasDragged = false;

        // Stretch thumb horizontally
        _thumbWidthSpring.Target = ThumbPressedWidth;
        _thumbHeightSpring.Target = ThumbDefaultSize;

        CaptureMouse();
        StartSpringAnimation();
        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_state != InteractionState.Pressed && _state != InteractionState.Dragging)
            return;

        var trackPos = e.GetPosition((UIElement?)_switchTrack ?? this);

        if (_state == InteractionState.Pressed)
        {
            double dx = Math.Abs(trackPos.X - _pressStartPoint.X);
            if (dx >= DragThreshold)
            {
                _state = InteractionState.Dragging;
                _hasDragged = true;
            }
            else
            {
                return;
            }
        }

        // Dragging: relative offset from press start (not absolute jump to mouse)
        double thumbW = _thumbWidthSpring.Position;
        double travel = TrackInnerWidth - thumbW - 2 * ThumbPadding;
        if (travel > 0)
        {
            double deltaX = trackPos.X - _pressStartPoint.X;
            double deltaProgress = deltaX / travel;
            double progress = Math.Clamp(_pressStartProgress + deltaProgress, 0.0, 1.0);

            _positionSpring.Position = progress;
            _positionSpring.Target = progress;
            _positionSpring.Velocity = 0;
        }

        // Keep spring loop alive for size animation
        StartSpringAnimation();
        e.Handled = true;
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (_state != InteractionState.Pressed && _state != InteractionState.Dragging)
            return;
        if (e.ChangedButton != MouseButton.Left) return;

        _state = IsMouseOver ? InteractionState.Hovered : InteractionState.Idle;
        ReleaseMouseCapture();

        if (!_hasDragged)
        {
            // Quick click: toggle
            IsOn = !IsOn;
        }
        else
        {
            // Drag release: check position
            double currentProgress = _positionSpring.Position;
            bool shouldBeOn = currentProgress >= 0.5;

            if (shouldBeOn != IsOn)
            {
                IsOn = shouldBeOn;
            }
            else
            {
                // Same state, spring back to current IsOn
                SyncSpringsToIsOn(animate: true);
            }
        }

        // Restore thumb size
        double targetSize = IsMouseOver ? ThumbHoverSize : ThumbDefaultSize;
        _thumbWidthSpring.Target = targetSize;
        _thumbHeightSpring.Target = targetSize;

        StartSpringAnimation();
        e.Handled = true;
    }

    private void OnLostMouseCaptureHandler(object sender, RoutedEventArgs e)
    {
        if (_state != InteractionState.Pressed && _state != InteractionState.Dragging)
            return;

        _state = IsMouseOver ? InteractionState.Hovered : InteractionState.Idle;
        _hasDragged = false;

        SyncSpringsToIsOn(animate: true);

        double targetSize = IsMouseOver ? ThumbHoverSize : ThumbDefaultSize;
        _thumbWidthSpring.Target = targetSize;
        _thumbHeightSpring.Target = targetSize;

        StartSpringAnimation();
    }

    #endregion

    #region Keyboard

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            IsOn = !IsOn;
            e.Handled = true;
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleSwitch ts)
        {
            ts.OnToggled();
            ts.UpdateContentPresenter();
            ts.SyncSpringsToIsOn(animate: true);
            ts.StartSpringAnimation();
        }
    }

    private static void OnContentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleSwitch ts)
            ts.UpdateContentPresenter();
    }

    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleSwitch ts)
            ts.UpdateHeaderVisibility();
    }

    protected void OnToggled()
    {
        RaiseEvent(new RoutedEventArgs(ToggledEvent, this));
    }

    #endregion

    #region Visual State Helpers

    private void UpdateContentPresenter()
    {
        if (_contentPresenter != null)
            _contentPresenter.Content = IsOn ? OnContent : OffContent;
    }

    private void UpdateHeaderVisibility()
    {
        if (_headerPresenter != null)
            _headerPresenter.Visibility = Header != null ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);

        if (!newValue)
        {
            // Cancel any active interaction
            if (_state == InteractionState.Pressed || _state == InteractionState.Dragging)
            {
                ReleaseMouseCapture();
                _state = InteractionState.Idle;
                _hasDragged = false;
            }

            // Apply disabled colors
            if (_switchTrack != null)
            {
                _switchTrack.Background = ResolveDisabledTrackBackground();
                _switchTrack.BorderBrush = ResolveDisabledTrackBorderBrush();
            }

            _thumbWidthSpring.Target = ThumbDefaultSize;
            _thumbHeightSpring.Target = ThumbDefaultSize;
            StartSpringAnimation();
        }
        else
        {
            // Re-enable: refresh colors via spring values
            ApplySpringValues();
        }
    }

    #endregion
}
