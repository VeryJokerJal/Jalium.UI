using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that simulates a 3D cube rotation. Old content is one face of the cube,
/// rotating away as the new content face rotates into view.
/// Uses ScaleX + TranslateX + SkewY to simulate perspective.
/// </summary>
public sealed class CubeRotateTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the perspective skew intensity (degrees, default 8).
    /// </summary>
    public double PerspectiveIntensity { get; set; } = 8.0;

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
        var centerY = height / 2;
        var maxSkew = PerspectiveIntensity;

        // Initial state
        host.NewContentOpacity = 1.0;
        host.OverlayOpacity = 1.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Old face: ScaleX shrinks, slides left, skews for perspective
            var oldScaleX = 1.0 - progress;
            var oldTranslateX = -width * progress * 0.5;
            // Skew increases then decreases: peaks at progress=0.5
            var skewFactor = Math.Sin(progress * Math.PI);
            var oldSkewY = maxSkew * skewFactor;

            host.OverlayTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = Math.Max(0.01, oldScaleX), ScaleY = 1.0, CenterX = 0, CenterY = centerY },
                    new SkewTransform { AngleY = oldSkewY, CenterX = 0, CenterY = centerY },
                    new TranslateTransform { X = oldTranslateX },
                }
            };
            host.OverlayOpacity = 1.0 - progress * 0.3; // Slight fade

            // New face: ScaleX grows, slides in from right, opposite skew
            var newScaleX = progress;
            var newTranslateX = width * (1.0 - progress) * 0.5;
            var newSkewY = -maxSkew * skewFactor;

            host.NewContentTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform { ScaleX = Math.Max(0.01, newScaleX), ScaleY = 1.0, CenterX = width, CenterY = centerY },
                    new SkewTransform { AngleY = newSkewY, CenterX = width, CenterY = centerY },
                    new TranslateTransform { X = newTranslateX },
                }
            };
            host.NewContentOpacity = 0.7 + progress * 0.3;
        }, onComplete);
    }
}
