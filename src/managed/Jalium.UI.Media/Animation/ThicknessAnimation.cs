
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Thickness property between two target values.
/// </summary>
public sealed partial class ThicknessAnimation : ThicknessAnimationBase
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
        DependencyProperty.Register(nameof(From), typeof(Thickness?), typeof(ThicknessAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the To dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Thickness?), typeof(ThicknessAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the By dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Thickness?), typeof(ThicknessAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the EasingFunction dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(ThicknessAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Thickness? From
    {
        get => (Thickness?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Thickness? To
    {
        get => (Thickness?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the amount by which the animation changes its starting value.
    /// </summary>
    public Thickness? By
    {
        get => (Thickness?)GetValue(ByProperty);
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

    protected override Thickness GetCurrentValueCore(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

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
    public ThicknessAnimation() { }

    public ThicknessAnimation(Thickness toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public ThicknessAnimation(Thickness toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new ThicknessAnimation Clone() => (ThicknessAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new ThicknessAnimation();
}