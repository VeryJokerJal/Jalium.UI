using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>
/// Parses path markup mini-language strings (e.g., "M 0,0 L 100,100 Z") into a
/// <see cref="PathGeometry"/>. Implements the full SVG / XAML path data grammar:
/// <list type="bullet">
///   <item>Move: <c>M</c>/<c>m</c></item>
///   <item>Line: <c>L</c>/<c>l</c>, <c>H</c>/<c>h</c>, <c>V</c>/<c>v</c></item>
///   <item>Cubic Bézier: <c>C</c>/<c>c</c>, smooth <c>S</c>/<c>s</c></item>
///   <item>Quadratic Bézier: <c>Q</c>/<c>q</c>, smooth <c>T</c>/<c>t</c></item>
///   <item>Elliptical arc: <c>A</c>/<c>a</c></item>
///   <item>Close: <c>Z</c>/<c>z</c></item>
///   <item>Fill rule (XAML extension): <c>F0</c> (EvenOdd) / <c>F1</c> (Nonzero)</item>
/// </list>
/// Uppercase commands are absolute, lowercase relative. A command may be followed by
/// multiple coordinate sets (implicit repeat); an implicit repeat after <c>M</c>/<c>m</c>
/// is treated as <c>L</c>/<c>l</c> per the spec. Numbers may be packed without separators
/// ("1-2", "1.5.3", "1e2"); commas and whitespace are interchangeable separators.
///
/// <para>
/// Robustness contract: every loop iteration either consumes input or throws — malformed
/// input (an unknown command, a coordinate before any command, or a missing/invalid arc
/// flag) raises <see cref="FormatException"/> rather than silently skipping or looping
/// forever. Callers (e.g. <c>Path.OnDataChanged</c>) catch this and render nothing.
/// </para>
/// </summary>
public static class PathMarkupParser
{
    /// <summary>
    /// Parses a path markup string into a <see cref="PathGeometry"/>.
    /// </summary>
    /// <exception cref="FormatException">The string is not valid path markup.</exception>
    public static PathGeometry Parse(string pathData)
    {
        ArgumentNullException.ThrowIfNull(pathData);

        var geometry = new PathGeometry();
        if (string.IsNullOrWhiteSpace(pathData))
            return geometry;

        var ctx = new ParseContext(pathData);
        PathFigure? currentFigure = null;
        Point currentPoint = default;   // current pen position
        Point subpathStart = default;   // start point of the active subpath (target of Z)
        Point lastControlPoint = default;
        char lastCommand = '\0';

        while (ctx.HasMore)
        {
            ctx.SkipSeparators();
            if (!ctx.HasMore) break;

            char c = ctx.Peek();

            // Fill rule (XAML extension): F0 = EvenOdd, F1 = Nonzero. Not a drawing command,
            // so it does not affect implicit-repeat tracking.
            if (c == 'F' || c == 'f')
            {
                ctx.Advance();
                ctx.SkipSeparators();
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
                else
                {
                    throw new FormatException($"Expected '0' or '1' after fill-rule command 'F' at position {ctx.Position}.");
                }
                continue;
            }

            // Determine the command for this iteration. 'e'/'E' are excluded so an exponent
            // inside a number is never mistaken for a command at this position.
            char command;
            if (char.IsLetter(c) && c != 'e' && c != 'E')
            {
                command = c;
                ctx.Advance();
            }
            else
            {
                // No explicit command letter — this must be an implicit repeat of the
                // previous command applied to a fresh coordinate set.
                if (lastCommand == '\0')
                    throw new FormatException($"Path data must begin with a command; found '{c}' at position {ctx.Position}.");

                command = lastCommand;
                // After a MoveTo, additional coordinate sets are implicit LineTo's.
                if (command == 'M') command = 'L';
                else if (command == 'm') command = 'l';
            }

            bool isRelative = char.IsLower(command);
            char upperCmd = char.ToUpperInvariant(command);

            switch (upperCmd)
            {
                case 'M': // MoveTo — opens a new subpath
                {
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    currentFigure = new PathFigure { StartPoint = pt };
                    geometry.Figures.Add(currentFigure);
                    currentPoint = pt;
                    subpathStart = pt;
                    break;
                }
                case 'L': // LineTo
                {
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentFigure!.Segments.Add(new LineSegment { Point = pt });
                    currentPoint = pt;
                    break;
                }
                case 'H': // Horizontal LineTo
                {
                    double x = ctx.ReadDouble();
                    if (isRelative) x += currentPoint.X;
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentPoint = new Point(x, currentPoint.Y);
                    currentFigure!.Segments.Add(new LineSegment { Point = currentPoint });
                    break;
                }
                case 'V': // Vertical LineTo
                {
                    double y = ctx.ReadDouble();
                    if (isRelative) y += currentPoint.Y;
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentPoint = new Point(currentPoint.X, y);
                    currentFigure!.Segments.Add(new LineSegment { Point = currentPoint });
                    break;
                }
                case 'C': // Cubic Bézier
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
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentFigure!.Segments.Add(new BezierSegment { Point1 = cp1, Point2 = cp2, Point3 = pt });
                    lastControlPoint = cp2;
                    currentPoint = pt;
                    break;
                }
                case 'S': // Smooth Cubic Bézier — first control is the reflection of the previous one
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
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentFigure!.Segments.Add(new BezierSegment { Point1 = cp1, Point2 = cp2, Point3 = pt });
                    lastControlPoint = cp2;
                    currentPoint = pt;
                    break;
                }
                case 'Q': // Quadratic Bézier
                {
                    var cp = ctx.ReadPoint();
                    var pt = ctx.ReadPoint();
                    if (isRelative)
                    {
                        cp = new Point(currentPoint.X + cp.X, currentPoint.Y + cp.Y);
                        pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    }
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentFigure!.Segments.Add(new QuadraticBezierSegment { Point1 = cp, Point2 = pt });
                    lastControlPoint = cp;
                    currentPoint = pt;
                    break;
                }
                case 'T': // Smooth Quadratic Bézier — control is the reflection of the previous one
                {
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    var cp = (lastCommand is 'Q' or 'q' or 'T' or 't')
                        ? Reflect(lastControlPoint, currentPoint)
                        : currentPoint;
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentFigure!.Segments.Add(new QuadraticBezierSegment { Point1 = cp, Point2 = pt });
                    lastControlPoint = cp;
                    currentPoint = pt;
                    break;
                }
                case 'A': // Elliptical Arc
                {
                    // Per the SVG spec's out-of-range elliptical-arc handling
                    // (SVG 1.1 §F.6.2 / SVG 2 §9.5.1): negative rx/ry are not an
                    // error — take the absolute value. ArcSegment.Size cannot hold
                    // a negative radius anyway (Size rejects negative dimensions).
                    double rx = Math.Abs(ctx.ReadDouble());
                    double ry = Math.Abs(ctx.ReadDouble());
                    double rotationAngle = ctx.ReadDouble();
                    bool isLargeArc = ctx.ReadFlag();
                    bool sweepClockwise = ctx.ReadFlag();
                    var pt = ctx.ReadPoint();
                    if (isRelative) pt = new Point(currentPoint.X + pt.X, currentPoint.Y + pt.Y);
                    EnsureOpenFigure(ref currentFigure, ref subpathStart, geometry, currentPoint);
                    currentFigure!.Segments.Add(new ArcSegment
                    {
                        Size = new Size(rx, ry),
                        RotationAngle = rotationAngle,
                        IsLargeArc = isLargeArc,
                        SweepDirection = sweepClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
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
                        // Per the spec the pen returns to the subpath's start point; a
                        // subsequent drawing command opens a NEW subpath there (handled by
                        // EnsureOpenFigure, which sees the closed figure).
                        currentPoint = subpathStart;
                    }
                    break;
                }
                default:
                    throw new FormatException($"Unknown path command '{command}' at position {ctx.Position}.");
            }

            // Track the actual command applied this iteration so the next smooth curve can
            // decide whether to reflect, and so implicit repeats know what to repeat.
            lastCommand = command;
        }

        return geometry;
    }

    /// <summary>
    /// Ensures there is an open figure to append a segment to. Opens a new subpath (rooted
    /// at <paramref name="currentPoint"/>) when there is no figure yet or the active one was
    /// just closed by a <c>Z</c>.
    /// </summary>
    private static void EnsureOpenFigure(ref PathFigure? figure, ref Point subpathStart, PathGeometry geometry, Point currentPoint)
    {
        if (figure != null && !figure.IsClosed) return;
        figure = new PathFigure { StartPoint = currentPoint };
        geometry.Figures.Add(figure);
        subpathStart = currentPoint;
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

        public int Position => _pos;

        public char Peek() => _data[_pos];

        public void Advance() => _pos++;

        /// <summary>Skips whitespace and commas (interchangeable separators in path data).</summary>
        public void SkipSeparators()
        {
            while (_pos < _data.Length)
            {
                char ch = _data[_pos];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n' || ch == ',')
                    _pos++;
                else
                    break;
            }
        }

        public double ReadDouble()
        {
            SkipSeparators();
            int start = _pos;

            // Sign
            if (_pos < _data.Length && (_data[_pos] == '-' || _data[_pos] == '+'))
                _pos++;

            // Integer part
            while (_pos < _data.Length && char.IsDigit(_data[_pos]))
                _pos++;

            // Fractional part
            if (_pos < _data.Length && _data[_pos] == '.')
            {
                _pos++;
                while (_pos < _data.Length && char.IsDigit(_data[_pos]))
                    _pos++;
            }

            // Exponent
            if (_pos < _data.Length && (_data[_pos] == 'e' || _data[_pos] == 'E'))
            {
                _pos++;
                if (_pos < _data.Length && (_data[_pos] == '-' || _data[_pos] == '+'))
                    _pos++;
                while (_pos < _data.Length && char.IsDigit(_data[_pos]))
                    _pos++;
            }

            if (_pos == start)
                throw new FormatException($"Expected a number at position {_pos}.");

            var slice = _data[start.._pos];
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"'{slice.ToString()}' is not a valid number at position {start}.");

            return value;
        }

        public Point ReadPoint()
        {
            double x = ReadDouble();
            double y = ReadDouble();
            return new Point(x, y);
        }

        /// <summary>
        /// Reads a single arc flag. Flags are exactly one character ('0' or '1') and may be
        /// packed against the following number (e.g. "011" → 0, 1, then 1). Anything else is
        /// a malformed arc.
        /// </summary>
        public bool ReadFlag()
        {
            SkipSeparators();
            if (_pos < _data.Length)
            {
                char ch = _data[_pos];
                if (ch == '0') { _pos++; return false; }
                if (ch == '1') { _pos++; return true; }
            }
            throw new FormatException($"Expected an arc flag (0 or 1) at position {_pos}.");
        }
    }
}
