
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Color property between two target values.
/// </summary>
public sealed partial class ColorAnimation : ColorAnimationBase
{
    public bool IsAdditive
    {
        get => (bool)GetValue(IsAdditiveProperty)!;
        set => SetValue(IsAdditiveProperty, value);
    }

    public bool IsCumulative
    {
        get => (bool)GetValue(IsCumulativeProperty)!;
        set => SetValue(IsCumulativeProperty, value);
    }

    // --- from DoubleAnimation.cs ---
    #region Dependency Properties

    /// <summary>
    /// Identifies the From dependency property.
    /// </summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Color?), typeof(ColorAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the To dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Color?), typeof(ColorAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the By dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Color?), typeof(ColorAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the EasingFunction dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(ColorAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Color? From
    {
        get => (Color?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Color? To
    {
        get => (Color?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the amount by which the animation changes its starting value.
    /// </summary>
    public Color? By
    {
        get => (Color?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    /// <summary>
    /// Gets or sets the easing function applied to this animation.
    /// </summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    /// <summary>
    /// Creates a new ColorAnimation.
    /// </summary>
    public ColorAnimation()
    {
    }

    /// <summary>
    /// Creates a new ColorAnimation with the specified To value.
    /// </summary>
    public ColorAnimation(Color toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new ColorAnimation with the specified From and To values.
    /// </summary>
    public ColorAnimation(Color fromValue, Color toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Color GetCurrentValueCore(Color defaultOriginValue, Color defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        // Apply easing function
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue ? AddColors(from, By.Value) : defaultDestinationValue);

        // Linear interpolation for each color component
        var a = (byte)(from.A + (to.A - from.A) * progress);
        var r = (byte)(from.R + (to.R - from.R) * progress);
        var g = (byte)(from.G + (to.G - from.G) * progress);
        var b = (byte)(from.B + (to.B - from.B) * progress);

        return Color.FromArgb(a, r, g, b);
    }

    // Color's full WPF scRGB composition is intentionally outside this batch;
    // ARGB channel saturation still gives By deterministic, bounded behavior.
    private static Color AddColors(Color origin, Color offset) => Color.FromArgb(
        AddChannel(origin.A, offset.A),
        AddChannel(origin.R, offset.R),
        AddChannel(origin.G, offset.G),
        AddChannel(origin.B, offset.B));

    private static byte AddChannel(byte origin, byte offset) =>
        (byte)Math.Min(byte.MaxValue, origin + offset);

    // --- from SimpleAnimations.WpfParity.cs ---
    public ColorAnimation(Color toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public ColorAnimation(Color fromValue, Color toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new ColorAnimation Clone() => (ColorAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new ColorAnimation();
}