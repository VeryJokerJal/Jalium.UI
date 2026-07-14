
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Decimal property between two target values.
/// </summary>
public sealed partial class DecimalAnimation : DecimalAnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(decimal?), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(decimal?), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(decimal?), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public decimal? From
    {
        get => (decimal?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public decimal? To
    {
        get => (decimal?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public decimal? By
    {
        get => (decimal?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public DecimalAnimation() { }

    public DecimalAnimation(decimal toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public DecimalAnimation(decimal fromValue, decimal toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override decimal GetCurrentValueCore(decimal defaultOriginValue, decimal defaultDestinationValue, AnimationClock animationClock)
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

    // --- from AdditionalSimpleAnimations.WpfParity.cs ---
    public DecimalAnimation(decimal toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public DecimalAnimation(decimal fromValue, decimal toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new DecimalAnimation Clone() => (DecimalAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new DecimalAnimation();

    // --- from AnimationCompositionProperties.cs ---
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
}