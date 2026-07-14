
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Byte property between two target values.
/// </summary>
public sealed partial class ByteAnimation : ByteAnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(byte?), typeof(ByteAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(byte?), typeof(ByteAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(byte?), typeof(ByteAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(ByteAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public byte? From
    {
        get => (byte?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public byte? To
    {
        get => (byte?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public byte? By
    {
        get => (byte?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public ByteAnimation() { }

    public ByteAnimation(byte toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public ByteAnimation(byte fromValue, byte toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override byte GetCurrentValueCore(byte defaultOriginValue, byte defaultDestinationValue, AnimationClock animationClock)
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
    public ByteAnimation(byte toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public ByteAnimation(byte fromValue, byte toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new ByteAnimation Clone() => (ByteAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new ByteAnimation();

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