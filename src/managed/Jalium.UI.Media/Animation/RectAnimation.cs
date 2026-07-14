
namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Rect property between two target values.
/// </summary>
public sealed partial class RectAnimation : RectAnimationBase
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Rect?), typeof(RectAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Rect?), typeof(RectAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Rect?), typeof(RectAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(RectAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Rect? From
    {
        get => (Rect?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Rect? To
    {
        get => (Rect?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public Rect? By
    {
        get => (Rect?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public RectAnimation() { }

    public RectAnimation(Rect toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public RectAnimation(Rect fromValue, Rect toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Rect GetCurrentValueCore(Rect defaultOriginValue, Rect defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue
            ? new Rect(
                from.X + By.Value.X,
                from.Y + By.Value.Y,
                from.Width + By.Value.Width,
                from.Height + By.Value.Height)
            : defaultDestinationValue);

        return new Rect(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress,
            from.Width + (to.Width - from.Width) * progress,
            from.Height + (to.Height - from.Height) * progress);
    }

    // --- from AdditionalSimpleAnimations.WpfParity.cs ---
    public RectAnimation(Rect toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public RectAnimation(Rect fromValue, Rect toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new RectAnimation Clone() => (RectAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new RectAnimation();

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