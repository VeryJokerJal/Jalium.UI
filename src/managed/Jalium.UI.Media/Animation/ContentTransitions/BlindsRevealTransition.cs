using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the orientation of blinds strips.
/// </summary>
public enum BlindsOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>
/// Transition that reveals new content through expanding blinds strips.
/// The old content is divided into strips, each strip shrinks to reveal the new content.
/// </summary>
public sealed class BlindsRevealTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the number of blinds strips (default 8).
    /// </summary>
    public int StripCount { get; set; } = 8;

    /// <summary>
    /// Gets or sets the blinds orientation.
    /// </summary>
    public BlindsOrientation Orientation { get; set; } = BlindsOrientation.Horizontal;

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
        var stripCount = Math.Max(2, StripCount);
        var isHorizontal = Orientation == BlindsOrientation.Horizontal;

        // New content is fully visible underneath
        host.NewContentOpacity = 1.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Build a GeometryGroup of strips for the old content clip
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            if (isHorizontal)
            {
                var stripHeight = height / stripCount;
                var visibleHeight = stripHeight * (1.0 - progress);

                for (int i = 0; i < stripCount; i++)
                {
                    var y = i * stripHeight;
                    if (visibleHeight > 0.5)
                    {
                        group.Children.Add(new RectangleGeometry(
                            new Rect(0, y, width, visibleHeight)));
                    }
                }
            }
            else
            {
                var stripWidth = width / stripCount;
                var visibleWidth = stripWidth * (1.0 - progress);

                for (int i = 0; i < stripCount; i++)
                {
                    var x = i * stripWidth;
                    if (visibleWidth > 0.5)
                    {
                        group.Children.Add(new RectangleGeometry(
                            new Rect(x, 0, visibleWidth, height)));
                    }
                }
            }

            if (group.Children.Count > 0)
            {
                host.OverlayClip = group;
                host.OverlayOpacity = 1.0;
            }
            else
            {
                host.OverlayOpacity = 0.0;
            }
        }, onComplete);
    }
}
