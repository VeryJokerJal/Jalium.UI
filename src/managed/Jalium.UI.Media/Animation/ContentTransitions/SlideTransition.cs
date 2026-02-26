using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the direction of a slide transition.
/// </summary>
public enum SlideDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// Transition that slides old content out and new content in.
/// The direction determines which way the content moves.
/// </summary>
public sealed class SlideTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the slide direction.
    /// </summary>
    public SlideDirection Direction { get; set; } = SlideDirection.Left;

    /// <inheritdoc />
    public override DispatcherTimer? Run(
        TransitionHost host,
        ImageSource? oldSnapshot,
        UIElement? newContent,
        Size bounds,
        Action onComplete)
    {
        var easing = EffectiveEasing;
        var duration = DurationMs;
        var width = bounds.Width;
        var height = bounds.Height;

        // Determine slide vectors
        double oldEndX = 0, oldEndY = 0;
        double newStartX = 0, newStartY = 0;

        switch (Direction)
        {
            case SlideDirection.Left:
                oldEndX = -width;
                newStartX = width;
                break;
            case SlideDirection.Right:
                oldEndX = width;
                newStartX = -width;
                break;
            case SlideDirection.Up:
                oldEndY = -height;
                newStartY = height;
                break;
            case SlideDirection.Down:
                oldEndY = height;
                newStartY = -height;
                break;
        }

        // Initial state
        host.OverlayTransform = null;
        host.NewContentTransform = new TranslateTransform { X = newStartX, Y = newStartY };

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Old content slides out
            host.OverlayTransform = new TranslateTransform
            {
                X = oldEndX * progress,
                Y = oldEndY * progress,
            };

            // New content slides in
            host.NewContentTransform = new TranslateTransform
            {
                X = newStartX * (1.0 - progress),
                Y = newStartY * (1.0 - progress),
            };
        }, onComplete);
    }
}
