using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>
/// Parses path markup mini-language strings (e.g., "M 0,0 L 100,100 Z") into PathGeometry.
/// Supports: M/m, L/l, H/h, V/v, C/c, S/s, Q/q, T/t, A/a, Z/z, F0/F1
/// </summary>
public static class PathMarkupParser
{
    /// <summary>
    /// Parses a path markup string into a PathGeometry.
    /// </summary>
    public static PathGeometry Parse(string pathData)
    {
        ArgumentNullException.ThrowIfNull(pathData);

        var geometry = new PathGeometry();
        if (string.IsNullOrWhiteSpace(pathData))
            return geometry;

        var ctx = new ParseContext(pathData);
        PathFigure? currentFigure = null;
        Point currentPoint = default;
        Point lastControlPoint = default;
        char lastCommand = '\0';

        while (ctx.HasMore)
        {
            ctx.SkipWhitespace();
            if (!ctx.HasMore) break;

            char c = ctx.Peek();

            // Handle fill rule
            if (c == 'F')
            {
                ctx.Advance();
                ctx.SkipWhitespace();
                if (ctx.HasMore && ctx.Peek() == '0')
                {
                    geometry.FillRule = FillRule.EvenOdd;
                    ctx.Advance();
                }
                else if (ctx.HasMore && ctx.Peek() == '1')
                {
                    geometry.FillRule = FillRule.Nonzero;
                    ctx.Advance();
                }
                continue;
            }

            // Determine command
            char command;
            if (char.IsLetter(c) && c != 'e' && c != 'E')
            {
                command = c;
                ctx.Advance();
            }
            else
            {
                // Implicit repeat of last command
                command = lastCommand;
                // After M, implicit repeats are treated as L
                if (command == 'M') command = 'L';
                if (command == 'm') command = 'l';
            }

            bool isRelative = char.IsLower(command);
            char upperCmd = char.ToUpperInvariant(command);

            switch (upperCmd)
            {
                case 'M': // MoveTo
                {
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    currentFigure = new PathFigure { StartPoint = pt };
                    geometry.Figures.Add(currentFigure);
                    currentPoint = pt;
                    break;
                }
                case 'L': // LineTo
                {
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentFigure!.Segments.Add(new LineSegment { Point = pt });
                    currentPoint = pt;
                    break;
                }
                case 'H': // Horizontal LineTo
                {
                    double x = ctx.ReadDouble();
                    if (isRelative) x += currentPoint.X;
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentPoint = new Point(x, currentPoint.Y);
                    currentFigure!.Segments.Add(new LineSegment { Point = currentPoint });
                    break;
                }
                case 'V': // Vertical LineTo
                {
                    double y = ctx.ReadDouble();
                    if (isRelative) y += currentPoint.Y;
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentPoint = new Point(currentPoint.X, y);
                    currentFigure!.Segments.Add(new LineSegment { Point = currentPoint });
                    break;
                }
                case 'C': // Cubic Bezier
                {
                    var cp1 = ctx.ReadPoint();
                    var cp2 = ctx.ReadPoint();
                    var pt = ctx.ReadPoint();
                    if (isRelative)
                    {
                        cp1 = new Point(currentPoint.X + cp1.X, currentPoint.Y + cp1.Y);
                        cp2 = new Point(currentPoint.X + cp2.X, currentPoint.Y + cp2.Y);
                        pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    }
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentFigure!.Segments.Add(new BezierSegment { Point1 = cp1, Point2 = cp2, Point3 = pt });
                    lastControlPoint = cp2;
                    currentPoint = pt;
                    break;
                }
                case 'S': // Smooth Cubic Bezier
                {
                    var cp2 = ctx.ReadPoint();
                    var pt = ctx.ReadPoint();
                    if (isRelative)
                    {
                        cp2 = new Point(currentPoint.X + cp2.X, currentPoint.Y + cp2.Y);
                        pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    }
                    var cp1 = (lastCommand is 'C' or 'c' or 'S' or 's')
                        ? Reflect(lastControlPoint, currentPoint)
                        : currentPoint;
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentFigure!.Segments.Add(new BezierSegment { Point1 = cp1, Point2 = cp2, Point3 = pt });
                    lastControlPoint = cp2;
                    currentPoint = pt;
                    break;
                }
                case 'Q': // Quadratic Bezier
                {
                    var cp = ctx.ReadPoint();
                    var pt = ctx.ReadPoint();
                    if (isRelative)
                    {
                        cp = new Point(currentPoint.X + cp.X, currentPoint.Y + cp.Y);
                        pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    }
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentFigure!.Segments.Add(new QuadraticBezierSegment { Point1 = cp, Point2 = pt });
                    lastControlPoint = cp;
                    currentPoint = pt;
                    break;
                }
                case 'T': // Smooth Quadratic Bezier
                {
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    var cp = (lastCommand is 'Q' or 'q' or 'T' or 't')
                        ? Reflect(lastControlPoint, currentPoint)
                        : currentPoint;
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentFigure!.Segments.Add(new QuadraticBezierSegment { Point1 = cp, Point2 = pt });
                    lastControlPoint = cp;
                    currentPoint = pt;
                    break;
                }
                case 'A': // Arc
                {
                    double rx = ctx.ReadDouble();
                    double ry = ctx.ReadDouble();
                    double rotationAngle = ctx.ReadDouble();
                    bool isLargeArc = ctx.ReadBool();
                    bool sweepDirection = ctx.ReadBool();
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    EnsureFigure(ref currentFigure, geometry, currentPoint);
                    currentFigure!.Segments.Add(new ArcSegment
                    {
                        Size = new Size(rx, ry),
                        RotationAngle = rotationAngle,
                        IsLargeArc = isLargeArc,
                        SweepDirection = sweepDirection ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        Point = pt
                    });
                    currentPoint = pt;
                    break;
                }
                case 'Z': // CloseFigure
                {
                    if (currentFigure != null)
                    {
                        currentFigure.IsClosed = true;
                        currentPoint = currentFigure.StartPoint;
                    }
                    break;
                }
            }

            lastCommand = command;
        }

        return geometry;
    }

    private static void EnsureFigure(ref PathFigure? figure, PathGeometry geometry, Point currentPoint)
    {
        if (figure != null) return;
        figure = new PathFigure { StartPoint = currentPoint };
        geometry.Figures.Add(figure);
    }

    private static Point Reflect(Point controlPoint, Point currentPoint)
    {
        return new Point(
            2 * currentPoint.X - controlPoint.X,
            2 * currentPoint.Y - controlPoint.Y);
    }

    private ref struct ParseContext
    {
        private readonly ReadOnlySpan<char> _data;
        private int _pos;

        public ParseContext(string data)
        {
            _data = data.AsSpan();
            _pos = 0;
        }

        public bool HasMore => _pos < _data.Length;

        public char Peek() => _data[_pos];

        public void Advance() => _pos++;

        public void SkipWhitespace()
        {
            while (_pos < _data.Length && (_data[_pos] == ' ' || _data[_pos] == '\t' ||
                   _data[_pos] == '\r' || _data[_pos] == '\n' || _data[_pos] == ','))
            {
                _pos++;
            }
        }

        public double ReadDouble()
        {
            SkipWhitespace();
            int start = _pos;

            // Handle sign
            if (_pos < _data.Length && (_data[_pos] == '-' || _data[_pos] == '+'))
                _pos++;

            // Integer part
            while (_pos < _data.Length && char.IsDigit(_data[_pos]))
                _pos++;

            // Decimal part
            if (_pos < _data.Length && _data[_pos] == '.')
            {
                _pos++;
                while (_pos < _data.Length && char.IsDigit(_data[_pos]))
                    _pos++;
            }

            // Exponent part
            if (_pos < _data.Length && (_data[_pos] == 'e' || _data[_pos] == 'E'))
            {
                _pos++;
                if (_pos < _data.Length && (_data[_pos] == '-' || _data[_pos] == '+'))
                    _pos++;
                while (_pos < _data.Length && char.IsDigit(_data[_pos]))
                    _pos++;
            }

            if (_pos == start)
                throw new FormatException($"Expected number at position {_pos}");

            return double.Parse(_data[start.._pos], CultureInfo.InvariantCulture);
        }

        public Point ReadPoint()
        {
            double x = ReadDouble();
            SkipWhitespace();
            double y = ReadDouble();
            return new Point(x, y);
        }

        public bool ReadBool()
        {
            SkipWhitespace();
            if (_pos < _data.Length)
            {
                char c = _data[_pos];
                if (c == '0' || c == '1')
                {
                    _pos++;
                    return c == '1';
                }
            }
            return false;
        }
    }
}
