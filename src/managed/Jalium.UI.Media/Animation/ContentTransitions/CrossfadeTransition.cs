using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that cross-fades between old and new content.
/// Old content fades out while new content fades in simultaneously.
/// </summary>
public sealed class CrossfadeTransition : ContentTransition
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

        // Initial state: old content fully visible on top, new content hidden
        host.OverlayOpacity = 1.0;
        host.NewContentOpacity = 0.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            host.OverlayOpacity = 1.0 - progress;
            host.NewContentOpacity = progress;
        }, onComplete);
    }
}
