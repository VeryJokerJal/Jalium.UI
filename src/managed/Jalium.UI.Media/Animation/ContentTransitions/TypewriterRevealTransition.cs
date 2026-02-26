using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that reveals new content line by line from top to bottom,
/// simulating a typewriter or printer effect. Old content fades out.
/// </summary>
public sealed class TypewriterRevealTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the height of each revealed "line" in pixels (default 20).
    /// </summary>
    public double LineHeight { get; set; } = 20.0;

    /// <inheritdoc />
    public override bool RequiresOldContentSnapshot => true;

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
        var lineH = Math.Max(4, LineHeight);

        // New content starts hidden
        host.NewContentOpacity = 1.0;
        host.OverlayOpacity = 1.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Reveal height expands line by line
            var revealHeight = height * progress;

            // Snap to line boundaries for typewriter effect
            var snappedHeight = Math.Floor(revealHeight / lineH) * lineH;
            snappedHeight = Math.Min(snappedHeight, height);

            // New content clip: revealed region from top
            if (snappedHeight < height)
            {
                host.NewContentClip = new RectangleGeometry(
                    new Rect(0, 0, width, snappedHeight));
            }
            else
            {
                host.NewContentClip = null;
            }

            // Old content: shrinks from top, fades
            if (snappedHeight < height)
            {
                host.OverlayClip = new RectangleGeometry(
                    new Rect(0, snappedHeight, width, height - snappedHeight));
                host.OverlayOpacity = 1.0 - progress * 0.5;
            }
            else
            {
                host.OverlayOpacity = 0.0;
            }
        }, onComplete);
    }
}
