using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Transition that wipes old content away in a specified direction,
/// revealing new content underneath. Uses RectangleGeometry clips.
/// </summary>
public sealed class WipeTransition : ContentTransition
{
    /// <summary>
    /// Gets or sets the wipe direction.
    /// </summary>
    public SlideDirection Direction { get; set; } = SlideDirection.Left;

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

        // New content is fully visible underneath
        host.NewContentOpacity = 1.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Shrink the old content clip to reveal new content
            Rect clipRect = Direction switch
            {
                // Wipe left: old content visible area shrinks from right
                SlideDirection.Left => new Rect(0, 0, width * (1.0 - progress), height),
                // Wipe right: old content visible area shrinks from left
                SlideDirection.Right => new Rect(width * progress, 0, width * (1.0 - progress), height),
                // Wipe up: shrinks from bottom
                SlideDirection.Up => new Rect(0, 0, width, height * (1.0 - progress)),
                // Wipe down: shrinks from top
                SlideDirection.Down => new Rect(0, height * progress, width, height * (1.0 - progress)),
                _ => new Rect(0, 0, width, height),
            };

            host.OverlayClip = new RectangleGeometry(clipRect);
        }, onComplete);
    }
}

/// <summary>
/// Transition that wipes old content along a diagonal line from top-left to bottom-right.
/// The diagonal sweep line (x + y = threshold) moves across the area.
/// </summary>
public sealed class WipeDiagonalTransition : ContentTransition
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
        var w = bounds.Width;
        var h = bounds.Height;

        // New content is fully visible underneath
        host.NewContentOpacity = 1.0;

        return CreateFrameTimer(duration, easing, progress =>
        {
            // Sweep line: x + y = threshold
            // threshold goes from 0 to w+h
            var threshold = (w + h) * progress;

            // Build polygon for the remaining (un-wiped) area:
            // All points in [0,w]x[0,h] where x + y >= threshold
            var points = new List<Point>(6);

            // Intersect sweep line with bounding box edges
            // Left edge (x=0): y = threshold, valid if 0 <= threshold <= h
            // Top edge (y=0): x = threshold, valid if 0 <= threshold <= w
            // Right edge (x=w): y = threshold - w, valid if 0 <= threshold-w <= h
            // Bottom edge (y=h): x = threshold - h, valid if 0 <= threshold-h <= w

            // Walk clockwise from the sweep line intersection on the left/top edge
            if (threshold <= h)
                points.Add(new Point(0, threshold));         // Left edge intersection
            if (threshold > h && threshold - h <= w)
                points.Add(new Point(threshold - h, h));     // Bottom edge intersection

            // Corners in the remaining region (x+y >= threshold)
            if (0 + h >= threshold)  // bottom-left corner
                points.Add(new Point(0, h));
            if (w + h >= threshold)  // bottom-right corner (always true until end)
                points.Add(new Point(w, h));
            if (w + 0 >= threshold)  // top-right corner
                points.Add(new Point(w, 0));

            if (threshold <= w)
                points.Add(new Point(threshold, 0));         // Top edge intersection
            if (threshold > w && threshold - w <= h)
                points.Add(new Point(w, threshold - w));     // Right edge intersection

            if (points.Count < 3)
            {
                // Fully wiped
                host.OverlayOpacity = 0.0;
                return;
            }

            // Remove duplicate/degenerate points and ensure clockwise winding
            var cleanPoints = DeduplicatePoints(points);
            if (cleanPoints.Count < 3)
            {
                host.OverlayOpacity = 0.0;
                return;
            }

            host.OverlayOpacity = 1.0;
            var fig = new PathFigure { StartPoint = cleanPoints[0], IsClosed = true };
            for (int i = 1; i < cleanPoints.Count; i++)
                fig.Segments.Add(new LineSegment { Point = cleanPoints[i] });

            var path = new PathGeometry { FillRule = FillRule.Nonzero };
            path.Figures.Add(fig);
            host.OverlayClip = path;
        }, onComplete);
    }

    private static List<Point> DeduplicatePoints(List<Point> points)
    {
        var result = new List<Point>(points.Count);
        foreach (var pt in points)
        {
            if (result.Count == 0 ||
                Math.Abs(result[^1].X - pt.X) > 0.5 ||
                Math.Abs(result[^1].Y - pt.Y) > 0.5)
            {
                result.Add(pt);
            }
        }
        return result;
    }
}
