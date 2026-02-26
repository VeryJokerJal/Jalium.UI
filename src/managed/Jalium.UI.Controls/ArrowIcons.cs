using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides pre-built arrow icon geometries for use in controls.
/// All geometries are based on a 1024x1024 SVG viewBox and scaled at draw time.
/// </summary>
internal static class ArrowIcons
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
        "M104.928 850.269 L919.072 850.269 "
        + "c45.467 0 82.191-35.892 82.191-80.688 "
        + "0-20.786-8.127-39.625-21.134-53.893 "
        + "L574.011 182.874 "
        + "c-27.178-35.363-78.468-42.801-114.638-15.957 "
        + "-6.334 4.581-11.867 9.956-16.209 15.957 "
        + "L32.407 722.327 "
        + "c-26.902 35.363-19.655 86.091 16.48 112.607 "
        + "14.836 11.171 31.857 16.279 48.953 16.279 Z";

    private const string DownArrowPath =
        "M919.072 173.731 l-821.243 0 "
        + "c-45.467 0-82.191 35.892-82.191 80.688 "
        + "0 20.786 8.127 39.625 21.134 53.893 "
        + "l406.118 533.814 "
        + "c27.178 35.363 78.468 42.801 114.638 15.957 "
        + "6.334-4.581 11.867-9.956 16.209-15.957 "
        + "l410.757-539.453 "
        + "c26.902-35.363 19.655-86.091-16.48-112.607 "
        + "-14.836-11.171-31.857-16.279-48.953-16.279 Z";

    private const string LeftArrowPath =
        "M850.269 104.928 L850.269 919.072 "
        + "c0 45.467-35.892 82.191-80.688 82.191 "
        + "-20.786 0-39.625-8.127-53.893-21.134 "
        + "L182.874 574.011 "
        + "c-35.363-27.178-42.801-78.468-15.957-114.638 "
        + "4.581-6.334 9.956-11.867 15.957-16.209 "
        + "L722.327 32.407 "
        + "c35.363-26.902 86.091-19.655 112.607 16.48 "
        + "11.171 14.836 16.279 31.857 16.279 48.953 Z";

    private const string RightArrowPath =
        "M173.731 104.928 L173.731 919.072 "
        + "c0 45.467 35.892 82.191 80.688 82.191 "
        + "20.786 0 39.625-8.127 53.893-21.134 "
        + "L841.126 574.011 "
        + "c35.363-27.178 42.801-78.468 15.957-114.638 "
        + "-4.581-6.334-9.956-11.867-15.957-16.209 "
        + "L301.673 32.407 "
        + "c-35.363-26.902-86.091-19.655-112.607 16.48 "
        + "-11.171 14.836-16.279 31.857-16.279 48.953 Z";

    #endregion

    #region Cached Geometries

    private static readonly Lazy<PathGeometry> _upArrow = new(() => ParseSvgPath(UpArrowPath));
    private static readonly Lazy<PathGeometry> _downArrow = new(() => ParseSvgPath(DownArrowPath));
    private static readonly Lazy<PathGeometry> _leftArrow = new(() => ParseSvgPath(LeftArrowPath));
    private static readonly Lazy<PathGeometry> _rightArrow = new(() => ParseSvgPath(RightArrowPath));

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

    private static PathGeometry ScaleGeometry(PathGeometry source, double scale, double offsetX, double offsetY)
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
    /// Parses SVG path data string into a PathGeometry.
    /// Supports: M/m, L/l, H/h, V/v, C/c, Z/z commands.
    /// </summary>
    internal static PathGeometry ParseSvgPath(string data)
    {
        var geometry = new PathGeometry();
        var tokens = Tokenize(data);
        var i = 0;
        var currentPoint = new Point(0, 0);
        var startPoint = new Point(0, 0);
        char lastCommand = 'M';
        PathFigure? currentFigure = null;

        while (i < tokens.Count)
        {
            var token = tokens[i];
            char cmd;
            bool isRelative;

            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                cmd = token[0];
                isRelative = char.IsLower(cmd);
                lastCommand = cmd;
                i++;
            }
            else
            {
                cmd = lastCommand;
                isRelative = char.IsLower(cmd);
            }

            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    if (ReadDouble(tokens, ref i, out var mx) && ReadDouble(tokens, ref i, out var my))
                    {
                        if (isRelative) { mx += currentPoint.X; my += currentPoint.Y; }
                        currentPoint = new Point(mx, my);
                        startPoint = currentPoint;

                        // Close previous figure if any
                        if (currentFigure != null)
                        {
                            geometry.Figures.Add(currentFigure);
                        }

                        currentFigure = new PathFigure
                        {
                            StartPoint = currentPoint,
                            IsFilled = true
                        };

                        // After M, implicit command is L/l
                        lastCommand = isRelative ? 'l' : 'L';
                    }
                    break;

                case 'L':
                    if (ReadDouble(tokens, ref i, out var lx) && ReadDouble(tokens, ref i, out var ly))
                    {
                        if (isRelative) { lx += currentPoint.X; ly += currentPoint.Y; }
                        var lp = new Point(lx, ly);
                        currentFigure?.Segments.Add(new LineSegment(lp));
                        currentPoint = lp;
                    }
                    break;

                case 'H':
                    if (ReadDouble(tokens, ref i, out var hx))
                    {
                        if (isRelative) hx += currentPoint.X;
                        var hp = new Point(hx, currentPoint.Y);
                        currentFigure?.Segments.Add(new LineSegment(hp));
                        currentPoint = hp;
                    }
                    break;

                case 'V':
                    if (ReadDouble(tokens, ref i, out var vy))
                    {
                        if (isRelative) vy += currentPoint.Y;
                        var vp = new Point(currentPoint.X, vy);
                        currentFigure?.Segments.Add(new LineSegment(vp));
                        currentPoint = vp;
                    }
                    break;

                case 'C':
                    if (ReadDouble(tokens, ref i, out var c1x) && ReadDouble(tokens, ref i, out var c1y) &&
                        ReadDouble(tokens, ref i, out var c2x) && ReadDouble(tokens, ref i, out var c2y) &&
                        ReadDouble(tokens, ref i, out var cex) && ReadDouble(tokens, ref i, out var cey))
                    {
                        if (isRelative)
                        {
                            c1x += currentPoint.X; c1y += currentPoint.Y;
                            c2x += currentPoint.X; c2y += currentPoint.Y;
                            cex += currentPoint.X; cey += currentPoint.Y;
                        }
                        var cp1 = new Point(c1x, c1y);
                        var cp2 = new Point(c2x, c2y);
                        var ep = new Point(cex, cey);
                        currentFigure?.Segments.Add(new BezierSegment(cp1, cp2, ep));
                        currentPoint = ep;
                    }
                    break;

                case 'Z':
                    if (currentFigure != null)
                    {
                        currentFigure.IsClosed = true;
                        geometry.Figures.Add(currentFigure);
                        currentFigure = null;
                    }
                    currentPoint = startPoint;
                    break;

                case 'F':
                    if (ReadDouble(tokens, ref i, out var fillRule))
                    {
                        geometry.FillRule = (int)fillRule == 1 ? FillRule.Nonzero : FillRule.EvenOdd;
                    }
                    break;

                default:
                    i++; // Skip unknown
                    break;
            }
        }

        // Add last figure if not closed
        if (currentFigure != null)
        {
            geometry.Figures.Add(currentFigure);
        }

        return geometry;
    }

    private static bool ReadDouble(List<string> tokens, ref int i, out double value)
    {
        if (i < tokens.Count && double.TryParse(tokens[i], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            i++;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>
    /// Tokenizes SVG path data into command letters and numeric values.
    /// Handles adjacent numbers separated by minus signs, and commands adjacent to numbers.
    /// </summary>
    private static List<string> Tokenize(string data)
    {
        var tokens = new List<string>(data.Length);
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < data.Length; i++)
        {
            char c = data[i];

            if (char.IsLetter(c))
            {
                // Flush current number
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                tokens.Add(c.ToString());
            }
            else if (c == '-')
            {
                // Minus sign: starts a new number if we already have digits
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                current.Append(c);
            }
            else if (c == '.')
            {
                // Check if we already have a decimal point in current token
                if (current.Length > 0 && current.ToString().Contains('.'))
                {
                    // New number starts at this decimal point
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                current.Append(c);
            }
            else if (char.IsDigit(c))
            {
                current.Append(c);
            }
            else if (c == ' ' || c == ',' || c == '\t' || c == '\n' || c == '\r')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    #endregion
}
