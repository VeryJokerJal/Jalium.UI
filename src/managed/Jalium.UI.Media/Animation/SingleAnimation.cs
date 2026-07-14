
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Single (float) property between two target values.
/// </summary>
public sealed partial class SingleAnimation : SingleAnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(float?), typeof(SingleAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(float?), typeof(SingleAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(float?), typeof(SingleAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(SingleAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public float? From
    {
        get => (float?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public float? To
    {
        get => (float?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public float? By
    {
        get => (float?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public SingleAnimation() { }

    public SingleAnimation(float toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public SingleAnimation(float fromValue, float toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override float GetCurrentValueCore(float defaultOriginValue, float defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = (float)animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = (float)EasingFunction.Ease(progress);
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
    public SingleAnimation(float toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public SingleAnimation(float fromValue, float toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new SingleAnimation Clone() => (SingleAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new SingleAnimation();

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