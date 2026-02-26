using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that simulates double doors opening from the center.
/// The old content is split into left and right halves, each "opening" outward
/// with perspective skew. New content is revealed underneath.
/// </summary>
public sealed class DoorOpenTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the perspective skew amount in degrees (default 15).
    /// </summary>
    public double PerspectiveSkew { get; set; } = 15.0;

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
        var halfWidth = width / 2;
        var maxSkew = PerspectiveSkew;

        // New content fades in from behind
        host.NewContentOpacity = 0.0;

        // Left half clip
        host.OverlayClip = new RectangleGeometry(new Rect(0, 0, halfWidth, height));

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Left door: pivot at left edge, ScaleX shrinks, skew for perspective
            var doorScale = 1.0 - progress * 0.6;
            var skewAngle = maxSkew * progress;

            host.OverlayTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = doorScale, ScaleY = 1.0, CenterX = 0, CenterY = height / 2 },
                    new SkewTransform { AngleY = skewAngle, CenterX = 0, CenterY = height / 2 },
                }
            };
            host.OverlayClip = new RectangleGeometry(new Rect(0, 0, halfWidth, height));
            host.OverlayOpacity = 1.0 - progress * 0.5;

            // Right door: pivot at right edge, opposite direction
            // We use Overlay2 for the right half
            host.Overlay2Transform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = doorScale, ScaleY = 1.0, CenterX = width, CenterY = height / 2 },
                    new SkewTransform { AngleY = -skewAngle, CenterX = width, CenterY = height / 2 },
                }
            };
            host.Overlay2Clip = new RectangleGeometry(new Rect(halfWidth, 0, halfWidth, height));
            host.Overlay2Opacity = 1.0 - progress * 0.5;

            // New content fades in
            host.NewContentOpacity = progress;
        }, onComplete);
    }
}
