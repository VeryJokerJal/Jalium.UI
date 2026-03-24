namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Brush property between solid-color targets.
/// </summary>
public sealed class BrushAnimation : AnimationTimeline<Brush>
{
    private readonly record struct AnimatedBrushState(Color Color, double Opacity);

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Brush), typeof(BrushAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Brush), typeof(BrushAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(BrushAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the starting brush.
    /// </summary>
    public Brush? From
    {
        get => (Brush?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the ending brush.
    /// </summary>
    public Brush? To
    {
        get => (Brush?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the easing function.
    /// </summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    protected override Brush GetCurrentValueCore(Brush defaultOriginValue, Brush defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var originBrush = From ?? defaultOriginValue;
        var destinationBrush = To ?? defaultDestinationValue;

        if (!TryResolveAnimatedState(originBrush, destinationBrush, out var fromState, out var toState))
        {
            return destinationBrush ?? originBrush ?? new SolidColorBrush(Color.Transparent);
        }

        var color = LerpColor(fromState.Color, toState.Color, progress);
        var opacity = Lerp(fromState.Opacity, toState.Opacity, progress);
        return new SolidColorBrush(color)
        {
            Opacity = opacity
        };
    }

    internal static bool SupportsTransition(Brush? from, Brush? to)
    {
        return TryResolveAnimatedState(from, to, out _, out _);
    }

    private static bool TryResolveAnimatedState(Brush? from, Brush? to, out AnimatedBrushState fromState, out AnimatedBrushState toState)
    {
        if (from is SolidColorBrush fromSolid && to is SolidColorBrush toSolid)
        {
            fromState = new AnimatedBrushState(fromSolid.Color, fromSolid.Opacity);
            toState = new AnimatedBrushState(toSolid.Color, toSolid.Opacity);
            return true;
        }

        if (from is null && to is SolidColorBrush enterSolid)
        {
            fromState = new AnimatedBrushState(
                Color.FromArgb(0, enterSolid.Color.R, enterSolid.Color.G, enterSolid.Color.B),
                enterSolid.Opacity);
            toState = new AnimatedBrushState(enterSolid.Color, enterSolid.Opacity);
            return true;
        }

        if (from is SolidColorBrush exitSolid && to is null)
        {
            fromState = new AnimatedBrushState(exitSolid.Color, exitSolid.Opacity);
            toState = new AnimatedBrushState(
                Color.FromArgb(0, exitSolid.Color.R, exitSolid.Color.G, exitSolid.Color.B),
                exitSolid.Opacity);
            return true;
        }

        if (from is null && to is null)
        {
            fromState = new AnimatedBrushState(Color.Transparent, 1.0);
            toState = fromState;
            return true;
        }

        fromState = default;
        toState = default;
        return false;
    }

    private static Color LerpColor(Color from, Color to, double progress)
    {
        // Interpolate in linear light space for perceptually correct transitions.
        float a = (float)Lerp(from.ScA, to.ScA, progress);
        float r = (float)Lerp(from.ScR, to.ScR, progress);
        float g = (float)Lerp(from.ScG, to.ScG, progress);
        float b = (float)Lerp(from.ScB, to.ScB, progress);
        return Color.FromScRgb(a, r, g, b);
    }

    private static double Lerp(double from, double to, double progress)
    {
        return from + (to - from) * progress;
    }
}
