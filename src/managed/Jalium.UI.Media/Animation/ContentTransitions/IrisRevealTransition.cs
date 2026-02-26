using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that reveals new content through an expanding circular iris from the center.
/// The old content is clipped to the area outside the expanding circle.
/// </summary>
public sealed class IrisRevealTransition : ContentTransition
{
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
        var centerX = width / 2;
        var centerY = height / 2;

        // Maximum radius: from center to corner
        var maxRadius = Math.Sqrt(centerX * centerX + centerY * centerY);

        // New content is fully visible underneath, revealed through the iris
        host.NewContentOpacity = 1.0;

        // Old content clips to the area NOT yet revealed
        // We use EllipseGeometry for new content clip (iris reveal)
        // and let old content stay fully visible but fade as iris grows

        return CreateFrameTimer(duration, easing, progress =>
        {
            var radius = maxRadius * progress;

            // New content: clip to expanding circle
            host.NewContentClip = new EllipseGeometry
            {
                Center = new Point(centerX, centerY),
                RadiusX = radius,
                RadiusY = radius,
            };

            // Old content: fade as iris expands (slight fade for visual polish)
            host.OverlayOpacity = progress > 0.8 ? (1.0 - progress) * 5.0 : 1.0;
        }, onComplete);
    }
}
