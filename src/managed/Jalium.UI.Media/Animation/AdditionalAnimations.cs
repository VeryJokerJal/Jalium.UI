namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a CornerRadius property between two target values.
/// </summary>
public sealed class CornerRadiusAnimation : TypedAnimationTimeline<CornerRadius>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(CornerRadius?), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(CornerRadius?), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public CornerRadius? From
    {
        get => (CornerRadius?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public CornerRadius? To
    {
        get => (CornerRadius?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public CornerRadiusAnimation() { }

    public CornerRadiusAnimation(CornerRadius toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public CornerRadiusAnimation(CornerRadius fromValue, CornerRadius toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override CornerRadius GetCurrentValueCore(CornerRadius defaultOriginValue, CornerRadius defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return new CornerRadius(
            from.TopLeft + (to.TopLeft - from.TopLeft) * progress,
            from.TopRight + (to.TopRight - from.TopRight) * progress,
            from.BottomRight + (to.BottomRight - from.BottomRight) * progress,
            from.BottomLeft + (to.BottomLeft - from.BottomLeft) * progress);
    }
}
