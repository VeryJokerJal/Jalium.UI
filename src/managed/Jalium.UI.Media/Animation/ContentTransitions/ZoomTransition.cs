using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the zoom direction.
/// </summary>
public enum ZoomMode
{
    /// <summary>New content zooms in from a smaller scale. Old content shrinks and fades out.</summary>
    ZoomIn,

    /// <summary>Old content zooms out (enlarges) and fades. New content appears from a larger scale.</summary>
    ZoomOut,
}

/// <summary>
/// Transition that zooms content in or out with a fade effect.
/// </summary>
public sealed class ZoomTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the zoom mode.
    /// </summary>
    public ZoomMode Mode { get; set; } = ZoomMode.ZoomIn;

    /// <summary>
    /// Gets or sets the scale factor for the smaller state (default 0.8).
    /// </summary>
    public double MinScale { get; set; } = 0.8;

    /// <summary>
    /// Gets or sets the scale factor for the larger state (default 1.2).
    /// </summary>
    public double MaxScale { get; set; } = 1.2;

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

        // Initial state
        host.OverlayOpacity = 1.0;
        host.NewContentOpacity = 0.0;

        if (Mode == ZoomMode.ZoomIn)
        {
            // Old content shrinks, new content grows from small to normal
            host.NewContentTransform = new ScaleTransform
            {
                ScaleX = MinScale,
                ScaleY = MinScale,
                CenterX = centerX,
                CenterY = centerY,
            };

            return CreateFrameTimer(duration, easing, progress =>
            {
                // Old: scale 1 → MinScale, fade out
                var oldScale = 1.0 - (1.0 - MinScale) * progress;
                host.OverlayTransform = new ScaleTransform
                {
                    ScaleX = oldScale,
                    ScaleY = oldScale,
                    CenterX = centerX,
                    CenterY = centerY,
                };
                host.OverlayOpacity = 1.0 - progress;

                // New: scale MinScale → 1, fade in
                var newScale = MinScale + (1.0 - MinScale) * progress;
                host.NewContentTransform = new ScaleTransform
                {
                    ScaleX = newScale,
                    ScaleY = newScale,
                    CenterX = centerX,
                    CenterY = centerY,
                };
                host.NewContentOpacity = progress;
            }, onComplete);
        }
        else
        {
            // ZoomOut: Old content enlarges and fades, new content shrinks to normal
            host.NewContentTransform = new ScaleTransform
            {
                ScaleX = MaxScale,
                ScaleY = MaxScale,
                CenterX = centerX,
                CenterY = centerY,
            };

            return CreateFrameTimer(duration, easing, progress =>
            {
                // Old: scale 1 → MaxScale, fade out
                var oldScale = 1.0 + (MaxScale - 1.0) * progress;
                host.OverlayTransform = new ScaleTransform
                {
                    ScaleX = oldScale,
                    ScaleY = oldScale,
                    CenterX = centerX,
                    CenterY = centerY,
                };
                host.OverlayOpacity = 1.0 - progress;

                // New: scale MaxScale → 1, fade in
                var newScale = MaxScale - (MaxScale - 1.0) * progress;
                host.NewContentTransform = new ScaleTransform
                {
                    ScaleX = newScale,
                    ScaleY = newScale,
                    CenterX = centerX,
                    CenterY = centerY,
                };
                host.NewContentOpacity = progress;
            }, onComplete);
        }
    }
}
