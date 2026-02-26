using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the flip axis.
/// </summary>
public enum FlipAxis
{
    /// <summary>Flip around vertical axis (ScaleX 1→0→1).</summary>
    Horizontal,

    /// <summary>Flip around horizontal axis (ScaleY 1→0→1).</summary>
    Vertical,
}

/// <summary>
/// Transition that simulates a card flip effect using ScaleX/ScaleY compression.
/// First half: old content compresses to zero. Second half: new content expands from zero.
/// A slight SkewTransform is applied for pseudo-3D perspective.
/// </summary>
public sealed class FlipTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the flip axis.
    /// </summary>
    public FlipAxis Axis { get; set; } = FlipAxis.Horizontal;

    /// <summary>
    /// Gets or sets the perspective skew amount in degrees (default 5).
    /// </summary>
    public double PerspectiveSkew { get; set; } = 5.0;

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
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2;
        var isHorizontal = Axis == FlipAxis.Horizontal;
        var skew = PerspectiveSkew;

        // New content starts hidden
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            if (progress <= 0.5)
            {
                // First half: old content compresses
                var halfProgress = progress * 2.0; // 0→1 within first half
                var scale = 1.0 - halfProgress;
                var skewAngle = skew * halfProgress;

                if (isHorizontal)
                {
                    host.OverlayTransform = new TransformGroup
                    {
                        Children =
                        {
                            new ScaleTransform { ScaleX = scale, ScaleY = 1.0, CenterX = centerX, CenterY = centerY },
                            new SkewTransform { AngleY = skewAngle, CenterX = centerX, CenterY = centerY },
                        }
                    };
                }
                else
                {
                    host.OverlayTransform = new TransformGroup
                    {
                        Children =
                        {
                            new ScaleTransform { ScaleX = 1.0, ScaleY = scale, CenterX = centerX, CenterY = centerY },
                            new SkewTransform { AngleX = skewAngle, CenterX = centerX, CenterY = centerY },
                        }
                    };
                }

                host.OverlayOpacity = 1.0;
                host.NewContentOpacity = 0.0;
            }
            else
            {
                // Second half: new content expands
                var halfProgress = (progress - 0.5) * 2.0; // 0→1 within second half
                var scale = halfProgress;
                var skewAngle = skew * (1.0 - halfProgress);

                // Hide old, show new
                host.OverlayOpacity = 0.0;
                host.NewContentOpacity = 1.0;

                if (isHorizontal)
                {
                    host.NewContentTransform = new TransformGroup
                    {
                        Children =
                        {
                            new ScaleTransform { ScaleX = scale, ScaleY = 1.0, CenterX = centerX, CenterY = centerY },
                            new SkewTransform { AngleY = -skewAngle, CenterX = centerX, CenterY = centerY },
                        }
                    };
                }
                else
                {
                    host.NewContentTransform = new TransformGroup
                    {
                        Children =
                        {
                            new ScaleTransform { ScaleX = 1.0, ScaleY = scale, CenterX = centerX, CenterY = centerY },
                            new SkewTransform { AngleX = -skewAngle, CenterX = centerX, CenterY = centerY },
                        }
                    };
                }
            }
        }, onComplete);
    }
}
