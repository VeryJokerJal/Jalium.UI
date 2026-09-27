using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Parses the CSS Shapes shape() command list without resolving layout-dependent lengths.</summary>
internal static class CssShapePathParser
{
    private enum PositionTokenKind : byte
    {
        Length, Center, XNear, XFar, XStart, XEnd, YNear, YFar, YStart, YEnd,
        InlineStart, InlineEnd, BlockStart, BlockEnd,
        Start, End,
    }

    private readonly record struct PositionToken(PositionTokenKind Kind, CssLength Length = default)
    {
        internal bool IsX => Kind is PositionTokenKind.XNear or PositionTokenKind.XFar
            or PositionTokenKind.XStart or PositionTokenKind.XEnd
            or PositionTokenKind.InlineStart or PositionTokenKind.InlineEnd;
        internal bool IsY => Kind is PositionTokenKind.YNear or PositionTokenKind.YFar
            or PositionTokenKind.YStart or PositionTokenKind.YEnd
            or PositionTokenKind.BlockStart or PositionTokenKind.BlockEnd;
        internal bool IsEdge => IsX || IsY || Kind is PositionTokenKind.Start or PositionTokenKind.End;
    }

    private static readonly CssShapeAxisSyntax Zero = new(new CssLength(0, CssUnit.None));
    private static readonly CssShapeAxisSyntax Center = new(new CssLength(50, CssUnit.Percent));

    internal static CssShapePathSyntax? Parse(ref CssTokenReader args)
    {
        var fillRule = FillRule.Nonzero;
        var probe = args;
        if (probe.TryReadIdent(out var rule) &&
            (rule.Equals("evenodd", StringComparison.OrdinalIgnoreCase) ||
             rule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)))
        {
            fillRule = rule.Equals("evenodd", StringComparison.OrdinalIgnoreCase)
                ? FillRule.EvenOdd : FillRule.Nonzero;
            args = probe;
        }
        if (!TryReadKeyword(ref args, "from") ||
            !TryReadPosition(ref args, out var start) ||
            !args.TryReadDelimiter(',')) return null;

        var commands = new List<CssShapeCommandSyntax>();
        while (args.TryReadUntilTopLevelComma(out var segment))
        {
            var commandReader = new CssTokenReader(segment, args.NumericContext);
            if (!TryReadCommand(ref commandReader, out var command) || !commandReader.AtEnd)
                return null;
            commands.Add(command);
            if (!args.TryReadDelimiter(',')) break;
            if (args.AtEnd) return null;
        }
        return commands.Count > 0 && args.AtEnd
            ? new CssShapePathSyntax(fillRule, start, [.. commands]) : null;
    }

    private static bool TryReadCommand(ref CssTokenReader reader, out CssShapeCommandSyntax command)
    {
        command = null!;
        if (!reader.TryReadIdent(out var name)) return false;
        if (name.Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            command = new(CssShapeCommandKind.Close);
            return true;
        }
        if (name.Equals("move", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadEndPoint(ref reader, out var end, out _)) return false;
            command = new(name.Equals("move", StringComparison.OrdinalIgnoreCase)
                ? CssShapeCommandKind.Move : CssShapeCommandKind.Line, end);
            return true;
        }
        if (name.Equals("hline", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("vline", StringComparison.OrdinalIgnoreCase))
        {
            var horizontal = name.Equals("hline", StringComparison.OrdinalIgnoreCase);
            if (!TryReadAxisEnd(ref reader, horizontal, out var end)) return false;
            command = new(horizontal ? CssShapeCommandKind.HLine : CssShapeCommandKind.VLine, end);
            return true;
        }
        if (name.Equals("curve", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("smooth", StringComparison.OrdinalIgnoreCase))
        {
            var smooth = name.Equals("smooth", StringComparison.OrdinalIgnoreCase);
            if (!TryReadEndPoint(ref reader, out var end, out var relative)) return false;
            CssShapePointSyntax? first = null, second = null;
            if (TryReadKeyword(ref reader, "with"))
            {
                if (!TryReadControl(ref reader, relative, out var control)) return false;
                first = control;
                if (!smooth && reader.TryReadDelimiter('/'))
                {
                    if (!TryReadControl(ref reader, relative, out var other)) return false;
                    second = other;
                }
            }
            else if (!smooth) return false;
            command = new(smooth ? CssShapeCommandKind.Smooth : CssShapeCommandKind.Curve,
                end, first, second);
            return true;
        }
        if (name.Equals("arc", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadEndPoint(ref reader, out var end, out _)) return false;
            CssLength? radiusX = null, radiusY = null;
            var hasSweep = false;
            var hasSize = false;
            var hasRotation = false;
            var clockwise = false;
            var large = false;
            var rotation = 0.0;
            while (!reader.AtEnd)
            {
                if (!reader.TryReadIdent(out var option)) return false;
                if (option.Equals("of", StringComparison.OrdinalIgnoreCase) && radiusX is null)
                {
                    if (!TryReadLength(ref reader, out var firstRadius)) return false;
                    radiusX = firstRadius;
                    if (TryReadLength(ref reader, out var secondRadius)) radiusY = secondRadius;
                }
                else if ((option.Equals("cw", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("ccw", StringComparison.OrdinalIgnoreCase)) && !hasSweep)
                {
                    clockwise = option.Equals("cw", StringComparison.OrdinalIgnoreCase);
                    hasSweep = true;
                }
                else if ((option.Equals("large", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("small", StringComparison.OrdinalIgnoreCase)) && !hasSize)
                {
                    large = option.Equals("large", StringComparison.OrdinalIgnoreCase);
                    hasSize = true;
                }
                else if (option.Equals("rotate", StringComparison.OrdinalIgnoreCase) && !hasRotation)
                {
                    if (!reader.TryReadNumber(out var value, out var unit) ||
                        unit is not (CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn) &&
                        (unit != CssUnit.None || value != 0) ||
                        !CssUnitConversion.TryToDegrees(value, unit, out rotation) ||
                        !double.IsFinite(rotation)) return false;
                    hasRotation = true;
                }
                else return false;
            }
            if (radiusX is null) return false;
            command = new(CssShapeCommandKind.Arc, end, RadiusX: radiusX, RadiusY: radiusY,
                Rotation: rotation, Clockwise: clockwise, LargeArc: large);
            return true;
        }
        return false;
    }

    private static bool TryReadEndPoint(ref CssTokenReader reader,
        out CssShapePointSyntax point, out bool relative)
    {
        point = default;
        relative = false;
        if (TryReadKeyword(ref reader, "to")) return TryReadPosition(ref reader, out point);
        if (!TryReadKeyword(ref reader, "by") || !TryReadPair(ref reader, out var x, out var y))
            return false;
        point = new(new(x), new(y), CssShapeAnchor.Start);
        relative = true;
        return true;
    }

    private static bool TryReadAxisEnd(ref CssTokenReader reader, bool horizontal,
        out CssShapePointSyntax point)
    {
        point = default;
        var relative = false;
        if (TryReadKeyword(ref reader, "by")) relative = true;
        else if (!TryReadKeyword(ref reader, "to")) return false;

        CssShapeAxisSyntax axis;
        if (relative)
        {
            if (!TryReadLength(ref reader, out var delta)) return false;
            axis = new(delta);
        }
        else
        {
            var probe = reader;
            if (probe.TryReadIdent(out var keyword))
            {
                var kind = MapPositionKeyword(keyword);
                if (horizontal && kind is not (PositionTokenKind.Center or PositionTokenKind.XNear or
                        PositionTokenKind.XFar or PositionTokenKind.XStart or PositionTokenKind.XEnd) ||
                    !horizontal && kind is not (PositionTokenKind.Center or PositionTokenKind.YNear or
                        PositionTokenKind.YFar or PositionTokenKind.YStart or PositionTokenKind.YEnd)) return false;
                axis = Axis(new PositionToken(kind));
                reader = probe;
            }
            else
            {
                if (!TryReadLength(ref reader, out var position)) return false;
                axis = new(position);
            }
        }
        point = horizontal ? new(axis, Zero, relative ? CssShapeAnchor.Start : CssShapeAnchor.Reference)
            : new(Zero, axis, relative ? CssShapeAnchor.Start : CssShapeAnchor.Reference);
        return true;
    }

    private static bool TryReadControl(ref CssTokenReader reader, bool relativeEnd,
        out CssShapePointSyntax point)
    {
        point = default;
        var probe = reader;
        if (TryReadPair(ref probe, out var x, out var y))
        {
            var anchor = CssShapeAnchor.Start;
            if (TryReadKeyword(ref probe, "from"))
            {
                if (!probe.TryReadIdent(out var anchorName)) return false;
                if (anchorName.Equals("start", StringComparison.OrdinalIgnoreCase))
                    anchor = CssShapeAnchor.Start;
                else if (anchorName.Equals("end", StringComparison.OrdinalIgnoreCase))
                    anchor = CssShapeAnchor.End;
                else if (anchorName.Equals("origin", StringComparison.OrdinalIgnoreCase))
                    anchor = CssShapeAnchor.Reference;
                else return false;
                point = new(new(x), new(y), anchor);
                reader = probe;
                return true;
            }
            if (relativeEnd)
            {
                point = new(new(x), new(y), CssShapeAnchor.Start);
                reader = probe;
                return true;
            }
        }
        return !relativeEnd && TryReadPosition(ref reader, out point);
    }

    private static bool TryReadPair(ref CssTokenReader reader, out CssLength x, out CssLength y)
    {
        x = y = default;
        var probe = reader;
        if (!TryReadLength(ref probe, out x) || !TryReadLength(ref probe, out y)) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadLength(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (!probe.TryReadLength(out length) ||
            length.Expression is null && length.Unit == CssUnit.None && length.Value != 0)
            return false;
        reader = probe;
        return true;
    }

    private static bool TryReadKeyword(ref CssTokenReader reader, string expected)
    {
        var probe = reader;
        if (!probe.TryReadIdent(out var value) ||
            !value.Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadPosition(ref CssTokenReader reader, out CssShapePointSyntax point)
    {
        point = default;
        var probe = reader;
        var tokens = new PositionToken[4];
        var count = 0;
        while (TryReadPositionToken(ref probe, out var token))
        {
            if (count == tokens.Length) return false;
            tokens[count++] = token;
        }
        if (count == 0 || count == 3 || !BuildPosition(tokens.AsSpan(0, count), out point)) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadPositionToken(ref CssTokenReader reader, out PositionToken token)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var keyword))
        {
            var kind = MapPositionKeyword(keyword);
            if (kind != PositionTokenKind.Length)
            {
                token = new(kind);
                reader = probe;
                return true;
            }
        }
        probe = reader;
        if (TryReadLength(ref probe, out var length))
        {
            token = new(PositionTokenKind.Length, length);
            reader = probe;
            return true;
        }
        token = default;
        return false;
    }

    private static PositionTokenKind MapPositionKeyword(ReadOnlySpan<char> value)
    {
        if (value.Equals("left", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.XNear;
        if (value.Equals("right", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.XFar;
        if (value.Equals("top", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.YNear;
        if (value.Equals("bottom", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.YFar;
        if (value.Equals("center", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.Center;
        if (value.Equals("x-start", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.XStart;
        if (value.Equals("x-end", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.XEnd;
        if (value.Equals("y-start", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.YStart;
        if (value.Equals("y-end", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.YEnd;
        if (value.Equals("inline-start", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.InlineStart;
        if (value.Equals("inline-end", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.InlineEnd;
        if (value.Equals("block-start", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.BlockStart;
        if (value.Equals("block-end", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.BlockEnd;
        if (value.Equals("start", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.Start;
        if (value.Equals("end", StringComparison.OrdinalIgnoreCase)) return PositionTokenKind.End;
        return PositionTokenKind.Length;
    }

    private static CssShapeAxisSyntax Axis(PositionToken token, CssLength? offset = null)
    {
        var amount = offset ?? new CssLength(0, CssUnit.Percent);
        return token.Kind switch
        {
            PositionTokenKind.Length => new(token.Length),
            PositionTokenKind.Center => Center,
            PositionTokenKind.XFar or PositionTokenKind.YFar => new(amount, CssShapeEdge.Far),
            PositionTokenKind.XStart or PositionTokenKind.YStart or PositionTokenKind.InlineStart
                or PositionTokenKind.BlockStart or PositionTokenKind.Start =>
                new(amount, CssShapeEdge.LogicalStart),
            PositionTokenKind.XEnd or PositionTokenKind.YEnd or PositionTokenKind.InlineEnd
                or PositionTokenKind.BlockEnd or PositionTokenKind.End =>
                new(amount, CssShapeEdge.LogicalEnd),
            _ => new(amount),
        };
    }

    private static bool BuildPosition(ReadOnlySpan<PositionToken> tokens,
        out CssShapePointSyntax point)
    {
        point = default;
        CssShapeAxisSyntax x = Center, y = Center;
        if (tokens.Length == 1)
        {
            var token = tokens[0];
            if (token.Kind is PositionTokenKind.Start or PositionTokenKind.End) return false;
            if (token.IsY) y = Axis(token);
            else x = Axis(token);
        }
        else if (tokens.Length == 2)
        {
            var first = tokens[0];
            var second = tokens[1];
            if ((first.Kind is PositionTokenKind.Start or PositionTokenKind.End) &&
                (second.Kind is PositionTokenKind.Start or PositionTokenKind.End))
            {
                y = Axis(first); x = Axis(second);
            }
            else if (first.IsX && second.IsY) { x = Axis(first); y = Axis(second); }
            else if (first.IsY && second.IsX) { x = Axis(second); y = Axis(first); }
            else if (first.Kind == PositionTokenKind.Center && second.IsX) x = Axis(second);
            else if (first.Kind == PositionTokenKind.Center && second.IsY) y = Axis(second);
            else if (first.IsX && second.Kind == PositionTokenKind.Center) x = Axis(first);
            else if (first.IsY && second.Kind == PositionTokenKind.Center) y = Axis(first);
            else if ((first.Kind is PositionTokenKind.Length or PositionTokenKind.Center) &&
                (second.Kind is PositionTokenKind.Length or PositionTokenKind.Center))
            { x = Axis(first); y = Axis(second); }
            else if ((first.Kind is PositionTokenKind.Length or PositionTokenKind.Center) && second.IsY)
            { x = Axis(first); y = Axis(second); }
            else if (first.IsX && second.Kind == PositionTokenKind.Length)
            { x = Axis(first); y = Axis(second); }
            else return false;
        }
        else if (tokens.Length == 4)
        {
            var first = tokens[0];
            var second = tokens[2];
            if (!first.IsEdge || !second.IsEdge ||
                tokens[1].Kind != PositionTokenKind.Length ||
                tokens[3].Kind != PositionTokenKind.Length) return false;
            if (first.IsX && second.IsY)
            { x = Axis(first, tokens[1].Length); y = Axis(second, tokens[3].Length); }
            else if (first.IsY && second.IsX)
            { x = Axis(second, tokens[3].Length); y = Axis(first, tokens[1].Length); }
            else if ((first.Kind is PositionTokenKind.Start or PositionTokenKind.End) &&
                (second.Kind is PositionTokenKind.Start or PositionTokenKind.End))
            { y = Axis(first, tokens[1].Length); x = Axis(second, tokens[3].Length); }
            else return false;
        }
        else return false;
        point = new(x, y);
        return true;
    }
}
