
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a double property between two target values.
/// </summary>
public sealed partial class DoubleAnimation : DoubleAnimationBase
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
        DependencyProperty.Register(nameof(From), typeof(double?), typeof(DoubleAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the To dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(double?), typeof(DoubleAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the By dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(double?), typeof(DoubleAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the EasingFunction dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(DoubleAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public double? From
    {
        get => (double?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public double? To
    {
        get => (double?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the total amount by which the animation changes its starting value.
    /// </summary>
    public double? By
    {
        get => (double?)GetValue(ByProperty);
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
    /// Creates a new DoubleAnimation.
    /// </summary>
    public DoubleAnimation()
    {
    }

    /// <summary>
    /// Creates a new DoubleAnimation with the specified To value.
    /// </summary>
    public DoubleAnimation(double toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new DoubleAnimation with the specified From and To values.
    /// </summary>
    public DoubleAnimation(double fromValue, double toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        // Apply easing function
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        return AnimationValueOperations.EvaluateFromToBy(
            defaultOriginValue,
            defaultDestinationValue,
            From,
            To,
            By,
            progress,
            animationClock.CurrentIteration ?? 1,
            IsAdditive,
            IsCumulative);
    }

    // --- from SimpleAnimations.WpfParity.cs ---
    public DoubleAnimation(double toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public DoubleAnimation(double fromValue, double toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new DoubleAnimation Clone() => (DoubleAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new DoubleAnimation();
}