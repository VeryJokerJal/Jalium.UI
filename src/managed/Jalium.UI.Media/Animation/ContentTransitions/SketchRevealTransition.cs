using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that reveals new content with an expanding hand-drawn style border.
/// The reveal boundary has noise-based wobble to simulate a sketch/pencil effect.
/// Old content fades to a desaturated state before being replaced.
/// </summary>
public sealed class SketchRevealTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the wobble amplitude in pixels (default 8).
    /// </summary>
    public double WobbleAmplitude { get; set; } = 8.0;

    /// <summary>
    /// Gets or sets the number of edge segments (default 32).
    /// </summary>
    public int SegmentCount { get; set; } = 32;

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
        var maxRadius = Math.Sqrt(centerX * centerX + centerY * centerY) * 1.2;
        var wobble = WobbleAmplitude;
        var segments = Math.Max(8, SegmentCount);

        // Pre-generate noise offsets for consistent wobble
        var rng = new Random(23);
        var noiseOffsets = new double[segments + 1];
        for (int i = 0; i <= segments; i++)
            noiseOffsets[i] = (rng.NextDouble() - 0.5) * 2.0;

        host.NewContentOpacity = 1.0;
        host.OverlayOpacity = 1.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            var radius = maxRadius * progress;
            var currentWobble = wobble * Math.Sin(progress * Math.PI); // Wobble peaks at midpoint

            // Build irregular circle clip for new content
            var figure = new PathFigure { IsClosed = true };

            for (int i = 0; i <= segments; i++)
            {
                var angle = (double)i / segments * Math.PI * 2;
                var noise = noiseOffsets[i % noiseOffsets.Length];
                var r = radius + noise * currentWobble;
                r = Math.Max(0, r);

                var px = centerX + Math.Cos(angle) * r;
                var py = centerY + Math.Sin(angle) * r;

                if (i == 0)
                    figure.StartPoint = new Point(px, py);
                else
                    figure.Segments.Add(new LineSegment { Point = new Point(px, py) });
            }

            var path = new PathGeometry { FillRule = FillRule.Nonzero };
            path.Figures.Add(figure);
            host.NewContentClip = path;

            // Old content fades out
            host.OverlayOpacity = Math.Max(0, 1.0 - progress * 1.3);
        }, onComplete);
    }
}
