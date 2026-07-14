
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Vector property between two target values.
/// </summary>
public sealed partial class VectorAnimation : VectorAnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Vector?), typeof(VectorAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Vector?), typeof(VectorAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Vector?), typeof(VectorAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(VectorAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Vector? From
    {
        get => (Vector?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Vector? To
    {
        get => (Vector?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public Vector? By
    {
        get => (Vector?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public VectorAnimation() { }

    public VectorAnimation(Vector toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public VectorAnimation(Vector fromValue, Vector toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Vector GetCurrentValueCore(Vector defaultOriginValue, Vector defaultDestinationValue, AnimationClock animationClock)
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
    public VectorAnimation(Vector toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public VectorAnimation(Vector fromValue, Vector toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new VectorAnimation Clone() => (VectorAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new VectorAnimation();

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