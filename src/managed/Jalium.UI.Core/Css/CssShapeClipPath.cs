using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssShapeCommandKind : byte { Move, Line, HLine, VLine, Curve, Smooth, Arc, Close }
internal enum CssShapeCurveKind : byte { None, Quadratic, Cubic }
internal enum CssShapeAnchor : byte { Reference, Start, End }
internal enum CssShapeEdge : byte { Near, Far, LogicalStart, LogicalEnd }

internal readonly record struct CssShapeAxisSyntax(CssLength Length, CssShapeEdge Edge = CssShapeEdge.Near);
internal readonly record struct CssShapePointSyntax(
    CssShapeAxisSyntax X, CssShapeAxisSyntax Y, CssShapeAnchor Anchor = CssShapeAnchor.Reference);

internal sealed record CssShapeCommandSyntax(
    CssShapeCommandKind Kind, CssShapePointSyntax? End = null,
    CssShapePointSyntax? Control1 = null, CssShapePointSyntax? Control2 = null,
    CssLength? RadiusX = null, CssLength? RadiusY = null,
    double Rotation = 0, bool Clockwise = false, bool LargeArc = false);

internal sealed record CssShapePathSyntax(FillRule FillRule, CssShapePointSyntax Start,
    CssShapeCommandSyntax[] Commands)
{
    internal bool TryCompute(in CssLengthContext context, out CssShapePathValue value)
    {
        var lengthsContext = context;
        bool ComputeLength(CssLength source, out CssLayoutLength result)
        {
            source.ObserveContainerDependencies(lengthsContext);
            if (source.Expression is { } expression)
            {
                result = CssLayoutLength.Math(expression, lengthsContext);
                return true;
            }
            if (source.Unit == CssUnit.Percent)
            {
                result = CssLayoutLength.Percent(source.Value / 100);
                return true;
            }
            if (source.TryResolve(lengthsContext, CssPercentBasis.NotSupported, out var pixels))
            {
                result = CssLayoutLength.Px(pixels);
                return true;
            }
            result = default;
            return false;
        }

        bool ComputePoint(CssShapePointSyntax source, out CssShapePointValue result)
        {
            if (ComputeLength(source.X.Length, out var x) &&
                ComputeLength(source.Y.Length, out var y))
            {
                result = new(new(x, source.X.Edge), new(y, source.Y.Edge), source.Anchor);
                return true;
            }
            result = default;
            return false;
        }

        if (!ComputePoint(Start, out var start))
        {
            value = null!;
            return false;
        }
        var commands = new CssShapeCommandValue[Commands.Length];
        for (var index = 0; index < Commands.Length; index++)
        {
            var source = Commands[index];
            CssShapePointValue? end = null, control1 = null, control2 = null;
            CssLayoutLength? radiusX = null, radiusY = null;
            if (source.End is { } endSyntax)
            {
                if (!ComputePoint(endSyntax, out var point)) { value = null!; return false; }
                end = point;
            }
            if (source.Control1 is { } controlSyntax)
            {
                if (!ComputePoint(controlSyntax, out var point)) { value = null!; return false; }
                control1 = point;
            }
            if (source.Control2 is { } secondSyntax)
            {
                if (!ComputePoint(secondSyntax, out var point)) { value = null!; return false; }
                control2 = point;
            }
            if (source.RadiusX is { } firstRadius)
            {
                if (!ComputeLength(firstRadius, out var computed)) { value = null!; return false; }
                radiusX = computed;
            }
            if (source.RadiusY is { } secondRadius)
            {
                if (!ComputeLength(secondRadius, out var computed)) { value = null!; return false; }
                radiusY = computed;
            }
            commands[index] = new(source.Kind, end, control1, control2, radiusX, radiusY,
                source.Rotation, source.Clockwise, source.LargeArc);
        }
        value = new CssShapePathValue(FillRule, start, commands);
        return true;
    }
}

internal readonly record struct CssShapeAxisValue(CssLayoutLength Length, CssShapeEdge Edge)
{
    internal double Resolve(double size, bool rtl, bool horizontal)
    {
        var offset = Length.Resolve(size, 0);
        var far = Edge switch
        {
            CssShapeEdge.Far => true,
            CssShapeEdge.LogicalStart => horizontal && rtl,
            CssShapeEdge.LogicalEnd => !horizontal || !rtl,
            _ => false,
        };
        return far ? size - offset : offset;
    }
}

internal readonly record struct CssShapePointValue(
    CssShapeAxisValue X, CssShapeAxisValue Y, CssShapeAnchor Anchor)
{
    internal Point Resolve(Rect reference, Point start, Point end, bool rtl)
    {
        var origin = Anchor switch
        {
            CssShapeAnchor.Start => start,
            CssShapeAnchor.End => end,
            _ => reference.TopLeft,
        };
        return new Point(origin.X + X.Resolve(reference.Width, rtl, horizontal: true),
            origin.Y + Y.Resolve(reference.Height, rtl, horizontal: false));
    }
}

internal sealed record CssShapeCommandValue(
    CssShapeCommandKind Kind, CssShapePointValue? End,
    CssShapePointValue? Control1, CssShapePointValue? Control2,
    CssLayoutLength? RadiusX, CssLayoutLength? RadiusY,
    double Rotation, bool Clockwise, bool LargeArc);

internal sealed record CssShapePathValue(FillRule FillRule, CssShapePointValue Start,
    CssShapeCommandValue[] Commands)
{
    internal PathGeometry Resolve(Rect reference, bool rtl)
    {
        var initial = Start.Resolve(reference, reference.TopLeft, reference.TopLeft, rtl);
        var geometry = new PathGeometry { FillRule = FillRule };
        PathFigure NewFigure(Point point)
        {
            var created = new PathFigure { StartPoint = point, IsClosed = true, IsFilled = true };
            geometry.Figures.Add(created);
            return created;
        }

        var figure = NewFigure(initial);
        var current = initial;
        var subpathStart = initial;
        var lastControl = initial;
        var lastCurve = CssShapeCurveKind.None;
        var afterClose = false;
        foreach (var command in Commands)
        {
            if (command.Kind == CssShapeCommandKind.Move)
            {
                current = command.End!.Value.Resolve(reference, current, current, rtl);
                figure = NewFigure(current);
                subpathStart = current;
                afterClose = false;
                lastCurve = CssShapeCurveKind.None;
                continue;
            }
            if (command.Kind == CssShapeCommandKind.Close)
            {
                current = subpathStart;
                afterClose = true;
                lastCurve = CssShapeCurveKind.None;
                continue;
            }
            if (afterClose)
            {
                figure = NewFigure(current);
                subpathStart = current;
                afterClose = false;
            }

            var end = command.End!.Value.Resolve(reference, current, current, rtl);
            if (command.Kind == CssShapeCommandKind.HLine) end.Y = current.Y;
            if (command.Kind == CssShapeCommandKind.VLine) end.X = current.X;
            switch (command.Kind)
            {
                case CssShapeCommandKind.Line:
                case CssShapeCommandKind.HLine:
                case CssShapeCommandKind.VLine:
                    figure.Segments.Add(new LineSegment(end, true));
                    lastCurve = CssShapeCurveKind.None;
                    break;
                case CssShapeCommandKind.Curve:
                {
                    var first = command.Control1!.Value.Resolve(reference, current, end, rtl);
                    if (command.Control2 is { } secondControl)
                    {
                        var second = secondControl.Resolve(reference, current, end, rtl);
                        figure.Segments.Add(new BezierSegment(first, second, end, true));
                        lastControl = second;
                        lastCurve = CssShapeCurveKind.Cubic;
                    }
                    else
                    {
                        figure.Segments.Add(new QuadraticBezierSegment(first, end, true));
                        lastControl = first;
                        lastCurve = CssShapeCurveKind.Quadratic;
                    }
                    break;
                }
                case CssShapeCommandKind.Smooth:
                {
                    var curveKind = command.Control1 is null
                        ? CssShapeCurveKind.Quadratic : CssShapeCurveKind.Cubic;
                    var reflected = lastCurve == curveKind
                        ? new Point(current.X * 2 - lastControl.X, current.Y * 2 - lastControl.Y)
                        : current;
                    if (command.Control1 is { } secondControl)
                    {
                        var second = secondControl.Resolve(reference, current, end, rtl);
                        figure.Segments.Add(new BezierSegment(reflected, second, end, true));
                        lastControl = second;
                    }
                    else
                    {
                        figure.Segments.Add(new QuadraticBezierSegment(reflected, end, true));
                        lastControl = reflected;
                    }
                    lastCurve = curveKind;
                    break;
                }
                case CssShapeCommandKind.Arc:
                {
                    var diagonal = Math.Sqrt(reference.Width * reference.Width +
                        reference.Height * reference.Height) / Math.Sqrt(2);
                    var rx = Math.Abs(command.RadiusX?.Resolve(
                        command.RadiusY is null ? diagonal : reference.Width, 0) ?? 0);
                    var ry = Math.Abs(command.RadiusY?.Resolve(reference.Height, 0) ?? rx);
                    if (rx == 0 || ry == 0)
                        figure.Segments.Add(new LineSegment(end, true));
                    else if (end != current)
                        figure.Segments.Add(new ArcSegment(end, new Size(rx, ry), command.Rotation,
                            command.LargeArc, command.Clockwise
                                ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
                    lastCurve = CssShapeCurveKind.None;
                    break;
                }
            }
            current = end;
        }
        geometry.Freeze();
        return geometry;
    }
}

internal sealed class CssShapeClipPathCompiledValue(CssClipBox box,
    CssShapePathSyntax syntax) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (!syntax.TryCompute(context.Lengths, out var path)) return false;
        sink.Set(CssClipPathProperties.ValueProperty,
            new CssClipPathValue(CssClipShape.Shape, box, [], ShapeData: path));
        return true;
    }
}
