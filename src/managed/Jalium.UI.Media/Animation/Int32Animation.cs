
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of an Int32 property between two target values.
/// </summary>
public sealed partial class Int32Animation : Int32AnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(int?), typeof(Int32Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(int?), typeof(Int32Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(int?), typeof(Int32Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Int32Animation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public int? From
    {
        get => (int?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public int? To
    {
        get => (int?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public int? By
    {
        get => (int?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public Int32Animation() { }

    public Int32Animation(int toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public Int32Animation(int fromValue, int toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override int GetCurrentValueCore(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock)
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
    public Int32Animation(int toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Int32Animation(int fromValue, int toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Int32Animation Clone() => (Int32Animation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Int32Animation();

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