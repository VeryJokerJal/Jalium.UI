using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that simulates a carousel/turntable rotation.
/// Old content rotates away (shrinks + slides + fades) while new content
/// rotates into view from the opposite side with depth perspective.
/// </summary>
public sealed class CarouselTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the depth scale factor at the far position (default 0.7).
    /// </summary>
    public double DepthScale { get; set; } = 0.7;

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
        var depthScale = DepthScale;

        // Initial state
        host.OverlayOpacity = 1.0;
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Arc motion: use sine for smooth depth curve
            var depthCurve = Math.Sin(progress * Math.PI); // 0→1→0

            // Old content: slides left, shrinks into depth, fades
            var oldTranslateX = -width * progress;
            var oldScale = 1.0 - (1.0 - depthScale) * depthCurve;
            var oldVerticalOffset = centerY * (1.0 - oldScale) / 2; // Keep centered while scaling

            host.OverlayTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = oldScale, ScaleY = oldScale, CenterX = centerX, CenterY = centerY },
                    new TranslateTransform { X = oldTranslateX, Y = 0 },
                }
            };
            host.OverlayOpacity = 1.0 - progress;

            // New content: slides in from right, grows from depth
            var newTranslateX = width * (1.0 - progress);
            var newScale = depthScale + (1.0 - depthScale) * (1.0 - depthCurve);

            // For new content, use similar depth curve but offset
            var newDepthCurve = Math.Sin((1.0 - progress) * Math.PI);
            newScale = 1.0 - (1.0 - depthScale) * newDepthCurve;

            host.NewContentTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = newScale, ScaleY = newScale, CenterX = centerX, CenterY = centerY },
                    new TranslateTransform { X = newTranslateX, Y = 0 },
                }
            };
            host.NewContentOpacity = progress;
        }, onComplete);
    }
}
