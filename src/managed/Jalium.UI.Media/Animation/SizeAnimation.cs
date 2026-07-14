
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Size property between two target values.
/// </summary>
public sealed partial class SizeAnimation : SizeAnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Size?), typeof(SizeAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Size?), typeof(SizeAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Size?), typeof(SizeAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(SizeAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Size? From
    {
        get => (Size?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Size? To
    {
        get => (Size?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public Size? By
    {
        get => (Size?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public SizeAnimation() { }

    public SizeAnimation(Size toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public SizeAnimation(Size fromValue, Size toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Size GetCurrentValueCore(Size defaultOriginValue, Size defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue
            ? new Size(from.Width + By.Value.Width, from.Height + By.Value.Height)
            : defaultDestinationValue);

        return new Size(
            from.Width + (to.Width - from.Width) * progress,
            from.Height + (to.Height - from.Height) * progress);
    }

    // --- from AdditionalSimpleAnimations.WpfParity.cs ---
    public SizeAnimation(Size toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public SizeAnimation(Size fromValue, Size toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new SizeAnimation Clone() => (SizeAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new SizeAnimation();

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