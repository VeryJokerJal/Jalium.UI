
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of an Int16 property between two target values.
/// </summary>
public sealed partial class Int16Animation : Int16AnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(short?), typeof(Int16Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(short?), typeof(Int16Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(short?), typeof(Int16Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Int16Animation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public short? From
    {
        get => (short?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public short? To
    {
        get => (short?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public short? By
    {
        get => (short?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public Int16Animation() { }

    public Int16Animation(short toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public Int16Animation(short fromValue, short toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override short GetCurrentValueCore(short defaultOriginValue, short defaultDestinationValue, AnimationClock animationClock)
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
    public Int16Animation(short toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Int16Animation(short fromValue, short toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Int16Animation Clone() => (Int16Animation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Int16Animation();

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