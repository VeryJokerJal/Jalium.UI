using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides pre-built arrow icon geometries for use in controls.
/// All geometries are based on a 1024x1024 SVG viewBox and scaled at draw time.
/// </summary>
/// <remarks>
/// Public so external assemblies (e.g. Jalium.One.Modules.CodeEditor's minimap
/// scroll-bar arrows) can reuse the same vector arrow geometry as the framework
/// <c>ScrollBar</c> instead of duplicating the SVG path strings.
/// </remarks>
public static class ArrowIcons
{
    /// <summary>
    /// Arrow direction.
    /// </summary>
    public enum Direction
    {
        Up,
        Down,
        Left,
        Right
    }

    #region SVG Path Data

    private const string UpArrowPath =
        "M1000.011 690.586 "
        + "l-393.923-557.904 "
        + "c-42.21-58.785-147.737-58.785-189.948 0 "
        + "l-395.106 557.904 "
        + "c-42.21 58.785-11.943 155.948 72.478 155.948 "
        + "h835.205 "
        + "c84.421 0 113.505-97.162 71.293-155.948 Z";

    private const string DownArrowPath =
        "M1000.011 333.414 "
        + "l-393.923 557.904 "
        + "c-42.21 58.785-147.737 58.785-189.948 0 "
        + "l-395.106-557.904 "
        + "c-42.21-58.785-11.943-155.948 72.478-155.948 "
        + "h835.205 "
        + "c84.421 0 113.505 97.162 71.293 155.948 Z";

    private const string LeftArrowPath =
        "M690.586 1000.011 "
        + "l-557.904-393.923 "
        + "c-58.785-42.21-58.785-147.737 0-189.948 "
        + "l557.904-395.106 "
        + "c58.785-42.21 155.948-11.943 155.948 72.478 "
        + "v835.205 "
        + "c0 84.421-97.162 113.505-155.948 71.293 Z";

    private const string RightArrowPath =
        "M333.414 23.989 "
        + "l557.904 393.923 "
        + "c58.785 42.21 58.785 147.737 0 189.948 "
        + "l-557.904 395.106 "
        + "c-58.785 42.21-155.948 11.943-155.948-72.478 "
        + "v-835.205 "
        + "c0-84.421 97.162-113.505 155.948-71.293 Z";

    #endregion

    #region Cached Geometries

    private static readonly Lazy<PathGeometry> _upArrow = new(() => ParseFrozenSvgPath(UpArrowPath));
    private static readonly Lazy<PathGeometry> _downArrow = new(() => ParseFrozenSvgPath(DownArrowPath));
    private static readonly Lazy<PathGeometry> _leftArrow = new(() => ParseFrozenSvgPath(LeftArrowPath));
    private static readonly Lazy<PathGeometry> _rightArrow = new(() => ParseFrozenSvgPath(RightArrowPath));

    #endregion

    /// <summary>
    /// Gets the original (1024x1024) arrow geometry for the specified direction.
    /// </summary>
    public static PathGeometry GetGeometry(Direction direction) => direction switch
    {
        Direction.Up => _upArrow.Value,
        Direction.Down => _downArrow.Value,
        Direction.Left => _leftArrow.Value,
        Direction.Right => _rightArrow.Value,
        _ => _downArrow.Value,
    };

    /// <summary>
    /// Draws a filled arrow icon scaled to fit within the specified bounds.
    /// </summary>
    public static void DrawArrow(DrawingContext dc, Brush fill, Rect bounds, Direction direction)
    {
        var source = GetGeometry(direction);
        var sourceBounds = source.Bounds;
        if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0) return;

        var scaleX = bounds.Width / sourceBounds.Width;
        var scaleY = bounds.Height / sourceBounds.Height;
        var scale = Math.Min(scaleX, scaleY);

        var offsetX = bounds.X + (bounds.Width - sourceBounds.Width * scale) / 2 - sourceBounds.X * scale;
        var offsetY = bounds.Y + (bounds.Height - sourceBounds.Height * scale) / 2 - sourceBounds.Y * scale;

        var scaled = ScaleGeometry(source, scale, offsetX, offsetY);
        dc.DrawGeometry(fill, null, scaled);
    }

    #region Geometry Scaling

    /// <summary>
    /// Returns a copy of <paramref name="source"/> with a uniform scale and translation
    /// baked into every point. <c>internal</c> (rather than private) so tests can verify
    /// that every <see cref="PathSegment"/> kind the parser can emit — including
    /// <see cref="ArcSegment"/> — survives the transform.
    /// </summary>
    internal static PathGeometry ScaleGeometry(PathGeometry source, double scale, double offsetX, double offsetY)
    {
        var result = new PathGeometry();
        result.FillRule = source.FillRule;

        foreach (var figure in source.Figures)
        {
            var newFigure = new PathFigure
            {
                StartPoint = ScalePoint(figure.StartPoint, scale, offsetX, offsetY),
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        newFigure.Segments.Add(new LineSegment(
                            ScalePoint(line.Point, scale, offsetX, offsetY)));
                        break;

                    case BezierSegment bezier:
                        newFigure.Segments.Add(new BezierSegment(
                            ScalePoint(bezier.Point1, scale, offsetX, offsetY),
                            ScalePoint(bezier.Point2, scale, offsetX, offsetY),
                            ScalePoint(bezier.Point3, scale, offsetX, offsetY)));
                        break;

                    case QuadraticBezierSegment quad:
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            ScalePoint(quad.Point1, scale, offsetX, offsetY),
                            ScalePoint(quad.Point2, scale, offsetX, offsetY)));
                        break;

                    case ArcSegment arc:
                        // The arrow transform is a uniform scale (same factor on both axes)
                        // plus a translation, so the arc keeps its shape: scale the endpoint
                        // and the radii by the same factor and leave rotation / large-arc /
                        // sweep direction untouched. Without this case an 'A'/'a' command
                        // would parse correctly but be silently dropped here when drawn.
                        newFigure.Segments.Add(new ArcSegment(
                            ScalePoint(arc.Point, scale, offsetX, offsetY),
                            new Size(arc.Size.Width * scale, arc.Size.Height * scale),
                            arc.RotationAngle, arc.IsLargeArc, arc.SweepDirection, arc.IsStroked));
                        break;
                }
            }

            result.Figures.Add(newFigure);
        }

        return result;
    }

    private static Point ScalePoint(Point p, double scale, double offsetX, double offsetY)
        => new Point(p.X * scale + offsetX, p.Y * scale + offsetY);

    #endregion

    #region SVG Path Parser

    /// <summary>
    /// Parses SVG / XAML path mini-language data into a <see cref="PathGeometry"/>.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="PathMarkupParser"/>, the framework's canonical full-grammar
    /// parser, so the complete command set is supported: move (<c>M</c>/<c>m</c>); line
    /// (<c>L</c>/<c>l</c>, <c>H</c>/<c>h</c>, <c>V</c>/<c>v</c>); cubic Bézier (<c>C</c>/<c>c</c>)
    /// and its smooth form (<c>S</c>/<c>s</c>); quadratic Bézier (<c>Q</c>/<c>q</c>) and its
    /// smooth form (<c>T</c>/<c>t</c>); elliptical arc (<c>A</c>/<c>a</c>); close
    /// (<c>Z</c>/<c>z</c>); and the fill-rule extension (<c>F</c>) — including implicit command
    /// repetition, smooth-curve control-point reflection, packed numbers and exponent notation.
    /// This replaces an earlier hand-rolled parser that only understood
    /// <c>M/L/H/V/C/Z</c> and silently discarded the <c>S</c>, <c>Q</c>, <c>T</c> and <c>A</c>
    /// commands.
    /// </remarks>
    /// <exception cref="FormatException">The string is not valid path markup.</exception>
    internal static PathGeometry ParseSvgPath(string data) => PathMarkupParser.Parse(data);

    private static PathGeometry ParseFrozenSvgPath(string data)
    {
        var geometry = ParseSvgPath(data);
        geometry.Freeze();
        return geometry;
    }

    #endregion
}
