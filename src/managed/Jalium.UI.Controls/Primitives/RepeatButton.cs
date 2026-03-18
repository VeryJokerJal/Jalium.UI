using System.Timers;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a button that raises the Click event repeatedly while it is pressed.
/// </summary>
public class RepeatButton : ButtonBase
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.RepeatButtonAutomationPeer(this);
    }

    private const string ScrollBarArrowBrushKey = "ScrollBarArrow";
    private const string ScrollBarArrowHoverBrushKey = "ScrollBarArrowHover";
    private const string ScrollBarArrowPressedBrushKey = "ScrollBarArrowPressed";

    #region Dependency Properties

    /// <summary>
    /// Identifies the Delay dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty DelayProperty =
        DependencyProperty.Register(nameof(Delay), typeof(int), typeof(RepeatButton),
            new PropertyMetadata(SystemParameters.KeyboardDelay, OnDelayChanged));

    /// <summary>
    /// Identifies the Interval dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty IntervalProperty =
        DependencyProperty.Register(nameof(Interval), typeof(int), typeof(RepeatButton),
            new PropertyMetadata(SystemParameters.KeyboardSpeed, OnIntervalChanged));

    /// <summary>
    /// Identifies the UseScrollBarArrowAnimation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty UseScrollBarArrowAnimationProperty =
        DependencyProperty.Register(nameof(UseScrollBarArrowAnimation), typeof(bool), typeof(RepeatButton),
            new PropertyMetadata(false, OnUseScrollBarArrowAnimationChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the amount of time, in milliseconds, that the RepeatButton waits while it is pressed
    /// before it starts repeating the Click event.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public int Delay
    {
        get => (int)GetValue(DelayProperty)!;
        set => SetValue(DelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the amount of time, in milliseconds, between repeats of the Click event
    /// after repeating starts.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int Interval
    {
        get => (int)GetValue(IntervalProperty)!;
        set => SetValue(IntervalProperty, value);
    }

    /// <summary>
    /// Gets or sets whether PART_Arrow visuals should animate using eased hover/pressed states.
    /// Intended for ScrollBar line buttons.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool UseScrollBarArrowAnimation
    {
        get => (bool)GetValue(UseScrollBarArrowAnimationProperty)!;
        set => SetValue(UseScrollBarArrowAnimationProperty, value);
    }

    #endregion

    #region Private Fields

    private System.Timers.Timer? _timer;
    private bool _isInDelay;
    private bool _isPointerOverForArrow;

    private const double ArrowNormalScale = 1.0;
    private const double ArrowHoverScale = 1.12;
    private const double ArrowPressedScale = 0.82;

    private const double ArrowHoverDurationMs = 140;
    private const double ArrowPressDurationMs = 95;
    private const double ArrowReleaseDurationMs = 175;
    private const double ArrowDisableDurationMs = 120;

    private static readonly CubicEase s_arrowHoverEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase s_arrowPressEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase s_arrowReleaseEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 };
    private static readonly SineEase s_arrowDisableEase = new() { EasingMode = EasingMode.EaseOut };

    private Jalium.UI.Controls.Shapes.Path? _arrowPath;
    private ScaleTransform? _arrowScaleTransform;
    private readonly SolidColorBrush _animatedArrowBrush = new(Color.FromRgb(210, 210, 210));
    private DispatcherTimer? _arrowStateTimer;

    private static readonly Color s_defaultArrowNormalColor = Color.FromRgb(210, 210, 210);
    private static readonly Color s_defaultArrowHoverColor = Color.FromRgb(230, 230, 230);
    private static readonly Color s_defaultArrowPressedColor = Color.FromRgb(240, 240, 240);

    private Color _arrowNormalColor = s_defaultArrowNormalColor;
    private Color _arrowHoverColor = s_defaultArrowHoverColor;
    private Color _arrowPressedColor = s_defaultArrowPressedColor;

    private Color _arrowFromColor = s_defaultArrowNormalColor;
    private Color _arrowToColor = s_defaultArrowNormalColor;
    private double _arrowFromScale = ArrowNormalScale;
    private double _arrowToScale = ArrowNormalScale;
    private Color _arrowCurrentColor = s_defaultArrowNormalColor;
    private double _arrowCurrentScale = ArrowNormalScale;
    private long _arrowAnimStartTick;
    private double _arrowAnimDurationMs;
    private IEasingFunction _arrowAnimEase = s_arrowHoverEase;

    #endregion

    #region Internal State

    internal double CurrentScrollBarArrowScale => _arrowCurrentScale;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RepeatButton"/> class.
    /// </summary>
    public RepeatButton()
    {
        ResourcesChanged += OnResourcesChangedHandler;
    }

    #endregion

    #region Timer Handling

    private void StartTimer()
    {
        if (_timer == null)
        {
            _timer = new System.Timers.Timer();
            _timer.Elapsed += OnTimerElapsed;
        }

        _isInDelay = true;
        _timer.Interval = Delay;
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _isInDelay = false;
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_timer == null) return;

        if (_isInDelay)
        {
            // Switch from delay to repeat interval
            _isInDelay = false;
            _timer.Interval = Interval;
        }

        // Raise Click event on the UI thread
        Dispatcher?.Invoke(() =>
        {
            if (IsPressed && IsEnabled)
            {
                OnClick();
            }
        });
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _arrowPath = GetTemplateChild("PART_Arrow") as Jalium.UI.Controls.Shapes.Path;
        if (_arrowPath != null)
        {
            _arrowScaleTransform = _arrowPath.RenderTransform as ScaleTransform;
            if (_arrowScaleTransform == null)
            {
                _arrowScaleTransform = new ScaleTransform
                {
                    ScaleX = ArrowNormalScale,
                    ScaleY = ArrowNormalScale,
                    // Shapes.Path applies RenderTransform around its own visual center.
                    // Keeping transform centers at 0 avoids double-centering offsets.
                    CenterX = 0,
                    CenterY = 0
                };
                _arrowPath.RenderTransform = _arrowScaleTransform;
            }
            else
            {
                _arrowScaleTransform.CenterX = 0;
                _arrowScaleTransform.CenterY = 0;
            }

            _arrowPath.Fill = _animatedArrowBrush;
        }
        else
        {
            _arrowScaleTransform = null;
        }

        RefreshArrowPalette();
        StartArrowVisualTransition(immediate: true);
    }

    /// <inheritdoc />
    protected override void OnIsPressedChanged(bool oldValue, bool newValue)
    {
        base.OnIsPressedChanged(oldValue, newValue);

        if (newValue)
        {
            // Start repeating
            StartTimer();
        }
        else
        {
            // Stop repeating
            StopTimer();
        }

        StartArrowVisualTransition(immediate: false);
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _isPointerOverForArrow = true;
        StartArrowVisualTransition(immediate: false);
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isPointerOverForArrow = false;
        StartArrowVisualTransition(immediate: false);
    }

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        StartArrowVisualTransition(immediate: false);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnDelayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RepeatButton button && button._timer != null && button._isInDelay)
        {
            button._timer.Interval = (int)(e.NewValue ?? SystemParameters.KeyboardDelay);
        }
    }

    private static void OnIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RepeatButton button && button._timer != null && !button._isInDelay)
        {
            button._timer.Interval = (int)(e.NewValue ?? SystemParameters.KeyboardSpeed);
        }
    }

    private static void OnUseScrollBarArrowAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RepeatButton button)
        {
            button.RefreshArrowPalette();
            button.StartArrowVisualTransition(immediate: true);
        }
    }

    private void OnResourcesChangedHandler(object? sender, EventArgs e)
    {
        RefreshArrowPalette();
        if (UseScrollBarArrowAnimation)
        {
            StartArrowVisualTransition(immediate: true);
        }
    }

    #endregion

    #region Arrow Animation

    private void RefreshArrowPalette()
    {
        var fallbackNormal = GetBrushColor(Foreground, s_defaultArrowNormalColor);
        _arrowNormalColor = ResolveColorFromResource(ScrollBarArrowBrushKey, fallbackNormal);
        _arrowHoverColor = ResolveColorFromResource(ScrollBarArrowHoverBrushKey, s_defaultArrowHoverColor);
        _arrowPressedColor = ResolveColorFromResource(ScrollBarArrowPressedBrushKey, s_defaultArrowPressedColor);
    }

    private Color ResolveColorFromResource(string key, Color fallback)
    {
        if (TryFindResource(key) is SolidColorBrush localBrush)
        {
            return localBrush.Color;
        }

        if (Application.Current?.Resources != null &&
            Application.Current.Resources.TryGetValue(key, out var resource))
        {
            if (resource is SolidColorBrush appBrush)
            {
                return appBrush.Color;
            }

            if (resource is Color appColor)
            {
                return appColor;
            }
        }

        return fallback;
    }

    private static Color GetBrushColor(Brush? brush, Color fallback)
    {
        return brush is SolidColorBrush solid ? solid.Color : fallback;
    }

    private void StartArrowVisualTransition(bool immediate)
    {
        if (!UseScrollBarArrowAnimation)
        {
            StopArrowTimer();
            return;
        }

        RefreshArrowPalette();
        ComputeArrowTargetVisual(out var targetColor, out var targetScale, out var durationMs, out var ease);

        if (immediate)
        {
            StopArrowTimer();
            _arrowCurrentColor = targetColor;
            _arrowCurrentScale = targetScale;
            ApplyCurrentArrowVisuals();
            return;
        }

        if (TryUpdateArrowAnimationToNow() &&
            NearlyEqual(_arrowCurrentColor, targetColor) &&
            NearlyEqual(_arrowCurrentScale, targetScale))
        {
            return;
        }

        _arrowFromColor = _arrowCurrentColor;
        _arrowToColor = targetColor;
        _arrowFromScale = _arrowCurrentScale;
        _arrowToScale = targetScale;
        _arrowAnimDurationMs = durationMs;
        _arrowAnimEase = ease;
        _arrowAnimStartTick = Environment.TickCount64;

        if (_arrowAnimDurationMs <= 0.5)
        {
            _arrowCurrentColor = _arrowToColor;
            _arrowCurrentScale = _arrowToScale;
            StopArrowTimer();
            ApplyCurrentArrowVisuals();
            return;
        }

        EnsureArrowTimer();
        _arrowStateTimer!.Start();
        ApplyCurrentArrowVisuals();
    }

    private void ComputeArrowTargetVisual(out Color targetColor, out double targetScale, out double durationMs, out IEasingFunction ease)
    {
        if (!IsEnabled)
        {
            targetColor = _arrowNormalColor;
            targetScale = ArrowNormalScale;
            durationMs = ArrowDisableDurationMs;
            ease = s_arrowDisableEase;
            return;
        }

        if (IsPressed)
        {
            targetColor = _arrowPressedColor;
            targetScale = ArrowPressedScale;
            durationMs = ArrowPressDurationMs;
            ease = s_arrowPressEase;
            return;
        }

        if (_isPointerOverForArrow)
        {
            targetColor = _arrowHoverColor;
            targetScale = ArrowHoverScale;
            durationMs = ArrowHoverDurationMs;
            ease = s_arrowHoverEase;
            return;
        }

        targetColor = _arrowNormalColor;
        targetScale = ArrowNormalScale;
        durationMs = ArrowReleaseDurationMs;
        ease = s_arrowReleaseEase;
    }

    private void EnsureArrowTimer()
    {
        if (_arrowStateTimer != null)
        {
            return;
        }

        _arrowStateTimer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        _arrowStateTimer.Tick += OnArrowTimerTick;
    }

    private void StopArrowTimer()
    {
        _arrowStateTimer?.Stop();
    }

    private void OnArrowTimerTick(object? sender, EventArgs e)
    {
        if (!TryUpdateArrowAnimationToNow())
        {
            _arrowCurrentColor = _arrowToColor;
            _arrowCurrentScale = _arrowToScale;
            ApplyCurrentArrowVisuals();
            StopArrowTimer();
            return;
        }

        ApplyCurrentArrowVisuals();
    }

    private bool TryUpdateArrowAnimationToNow()
    {
        if (_arrowAnimDurationMs <= 0.5)
        {
            return false;
        }

        var elapsedMs = Environment.TickCount64 - _arrowAnimStartTick;
        var rawProgress = Math.Clamp(elapsedMs / _arrowAnimDurationMs, 0.0, 1.0);
        var eased = Math.Clamp(_arrowAnimEase.Ease(rawProgress), 0.0, 1.0);

        _arrowCurrentColor = LerpColor(_arrowFromColor, _arrowToColor, eased);
        _arrowCurrentScale = Lerp(_arrowFromScale, _arrowToScale, eased);

        return rawProgress < 1.0;
    }

    private void ApplyCurrentArrowVisuals()
    {
        _animatedArrowBrush.Color = _arrowCurrentColor;
        Foreground = _animatedArrowBrush;

        if (_arrowPath != null)
        {
            _arrowPath.Fill = _animatedArrowBrush;
            _arrowPath.InvalidateVisual();
        }

        if (_arrowScaleTransform != null)
        {
            _arrowScaleTransform.ScaleX = _arrowCurrentScale;
            _arrowScaleTransform.ScaleY = _arrowCurrentScale;
        }

        InvalidateVisual();
        if (_arrowPath == null && VisualParent is UIElement visualParent)
        {
            // Fallback arrows are drawn by ScrollBar.OnRender, so parent must redraw.
            visualParent.InvalidateVisual();
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static Color LerpColor(Color from, Color to, double t)
    {
        byte LerpByte(byte a, byte b) => (byte)Math.Clamp(a + (b - a) * t, 0, 255);

        return Color.FromArgb(
            LerpByte(from.A, to.A),
            LerpByte(from.R, to.R),
            LerpByte(from.G, to.G),
            LerpByte(from.B, to.B));
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.0001;

    private static bool NearlyEqual(Color a, Color b)
    {
        return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases the timer resources.
    /// </summary>
    ~RepeatButton()
    {
        _timer?.Dispose();
        if (_arrowStateTimer != null)
        {
            _arrowStateTimer.Stop();
            _arrowStateTimer.Tick -= OnArrowTimerTick;
        }
    }

    #endregion
}

/// <summary>
/// Provides access to system parameters for repeat button timing.
/// </summary>
internal static class SystemParameters
{
    /// <summary>
    /// Gets the keyboard delay in milliseconds (default: 500ms).
    /// </summary>
    public static int KeyboardDelay => 500;

    /// <summary>
    /// Gets the keyboard speed (repeat interval) in milliseconds (default: 33ms for ~30 repeats/sec).
    /// </summary>
    public static int KeyboardSpeed => 33;
}
