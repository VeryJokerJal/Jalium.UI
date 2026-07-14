
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of an Int64 property between two target values.
/// </summary>
public sealed partial class Int64Animation : Int64AnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(long?), typeof(Int64Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(long?), typeof(Int64Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(long?), typeof(Int64Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Int64Animation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public long? From
    {
        get => (long?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public long? To
    {
        get => (long?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public long? By
    {
        get => (long?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public Int64Animation() { }

    public Int64Animation(long toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public Int64Animation(long fromValue, long toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override long GetCurrentValueCore(long defaultOriginValue, long defaultDestinationValue, AnimationClock animationClock)
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
    public Int64Animation(long toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Int64Animation(long fromValue, long toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Int64Animation Clone() => (Int64Animation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Int64Animation();

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