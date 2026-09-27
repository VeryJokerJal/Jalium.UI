using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssClipShape : byte { None, Box, Inset, Xywh, Rect, Circle, Ellipse, Polygon, Path, Shape, Url }
internal enum CssClipBox : byte { Border, Padding, Content, Margin }
internal enum CssClipRadius : byte { Length, ClosestSide, FarthestSide }

/// <summary>A CSS basic shape whose percentages are resolved against its used reference box.</summary>
internal sealed record CssClipPathValue(CssClipShape Shape, CssClipBox Box,
    CssLayoutLength[] Lengths, CssClipRadius Radius = CssClipRadius.Length,
    CssLayoutLength[]? RoundRadii = null,
    bool CenterXFromFarEdge = false, bool CenterYFromFarEdge = false,
    FillRule PolygonFillRule = FillRule.Nonzero, CssLayoutLength PolygonRoundRadius = default,
    PathGeometry? SvgPath = null, CssShapePathValue? ShapeData = null,
    CssClipPathUrlResource? UrlResource = null)
{
    internal static readonly CssClipPathValue None = new(CssClipShape.None, CssClipBox.Border, []);

    internal Geometry? Resolve(UIElement element)
    {
        if (Shape == CssClipShape.None) return null;
        var size = element.RenderSize;
        if (Shape == CssClipShape.Url) return UrlResource!.Resolve(size);
        var rect = new Rect(size);
        var insets = default(Thickness);
        if (element is FrameworkElement framework)
        {
            var containingWidth = framework.CssLayout?.ContainingWidthCache ?? size.Width;
            var (border, padding) = CssBoxMetrics.BackgroundInsets(framework, containingWidth);
            insets = Box switch
            {
                CssClipBox.Padding => border,
                CssClipBox.Content => new Thickness(border.Left + padding.Left, border.Top + padding.Top,
                    border.Right + padding.Right, border.Bottom + padding.Bottom),
                CssClipBox.Margin => Negate(CssBoxMetrics.Margin(framework, containingWidth)),
                _ => default,
            };
            rect = Inset(rect, insets);
        }

        if (Shape == CssClipShape.Box)
        {
            var radii = CssBorderRadiusProperties.Get(element)?.Resolve(size) ?? NativeRadii(element, size);
            radii = Box == CssClipBox.Margin
                ? radii.Offset(Negate(insets), rect.Size)
                : radii.Inset(insets).Normalize(rect.Size);
            return new CssRoundedRectangleGeometry(rect, radii);
        }

        if (Shape == CssClipShape.Polygon) return ResolvePolygon(rect);
        if (Shape == CssClipShape.Path) return ResolvePath(rect);
        if (Shape == CssClipShape.Shape)
            return ShapeData!.Resolve(rect,
                element is FrameworkElement { FlowDirection: FlowDirection.RightToLeft });

        if (Shape is CssClipShape.Inset or CssClipShape.Xywh or CssClipShape.Rect)
        {
            Rect shapeRect;
            if (Shape == CssClipShape.Xywh)
            {
                shapeRect = new Rect(rect.X + Lengths[0].Resolve(rect.Width, 0),
                    rect.Y + Lengths[1].Resolve(rect.Height, 0),
                    Math.Max(0, Lengths[2].Resolve(rect.Width, 0)),
                    Math.Max(0, Lengths[3].Resolve(rect.Height, 0)));
            }
            else if (Shape == CssClipShape.Rect)
            {
                var top = Lengths[0].Resolve(rect.Height, 0);
                var right = Lengths[1].Resolve(rect.Width, 0);
                var bottom = Lengths[2].Resolve(rect.Height, 0);
                var left = Lengths[3].Resolve(rect.Width, 0);
                // rect() measures all four edges from the top/left origin.
                shapeRect = new Rect(rect.X + left, rect.Y + top,
                    Math.Max(0, right - left), Math.Max(0, bottom - top));
            }
            else
            {
                var top = Lengths[0].Resolve(rect.Height, 0);
                var right = Lengths[1].Resolve(rect.Width, 0);
                var bottom = Lengths[2].Resolve(rect.Height, 0);
                var left = Lengths[3].Resolve(rect.Width, 0);
                // CSS Shapes scales opposing insets together when they cross.
                if (left + right > rect.Width && left + right > 0)
                {
                    var scale = rect.Width / (left + right);
                    left *= scale; right *= scale;
                }
                if (top + bottom > rect.Height && top + bottom > 0)
                {
                    var scale = rect.Height / (top + bottom);
                    top *= scale; bottom *= scale;
                }
                shapeRect = Inset(rect, new Thickness(left, top, right, bottom));
            }
            if (RoundRadii is not { Length: 8 } round)
                return new CssRoundedRectangleGeometry(shapeRect, default);
            Size Corner(int index) => new(Math.Max(0, round[index].Resolve(shapeRect.Width, 0)),
                Math.Max(0, round[index + 4].Resolve(shapeRect.Height, 0)));
            var radii = new CssUsedBorderRadii(Corner(0), Corner(1), Corner(2), Corner(3))
                .Normalize(shapeRect.Size);
            return new CssRoundedRectangleGeometry(shapeRect, radii);
        }

        var x = Lengths[0].Resolve(rect.Width, 0);
        var y = Lengths[1].Resolve(rect.Height, 0);
        var center = new Point(rect.X + (CenterXFromFarEdge ? rect.Width - x : x),
            rect.Y + (CenterYFromFarEdge ? rect.Height - y : y));
        double rx, ry;
        // Radial-size keywords measure distance to the reference-box edge as
        // an infinite line, including when the center lies outside that box.
        var leftDistance = Math.Abs(center.X - rect.Left);
        var rightDistance = Math.Abs(rect.Right - center.X);
        var topDistance = Math.Abs(center.Y - rect.Top);
        var bottomDistance = Math.Abs(rect.Bottom - center.Y);
        if (Shape == CssClipShape.Circle)
        {
            var nearest = Math.Min(Math.Min(leftDistance, rightDistance),
                Math.Min(topDistance, bottomDistance));
            var farthest = Math.Max(Math.Max(leftDistance, rightDistance),
                Math.Max(topDistance, bottomDistance));
            var radius = Radius switch
            {
                CssClipRadius.ClosestSide => nearest,
                CssClipRadius.FarthestSide => farthest,
                _ => Lengths[2].Resolve(Math.Sqrt((rect.Width * rect.Width + rect.Height * rect.Height) / 2), 0),
            };
            rx = ry = Math.Max(0, radius);
        }
        else
        {
            rx = Radius switch
            {
                CssClipRadius.ClosestSide => Math.Min(leftDistance, rightDistance),
                CssClipRadius.FarthestSide => Math.Max(leftDistance, rightDistance),
                _ => Lengths[2].Resolve(rect.Width, 0),
            };
            ry = Radius switch
            {
                CssClipRadius.ClosestSide => Math.Min(topDistance, bottomDistance),
                CssClipRadius.FarthestSide => Math.Max(topDistance, bottomDistance),
                _ => Lengths[3].Resolve(rect.Height, 0),
            };
            rx = Math.Max(0, rx); ry = Math.Max(0, ry);
        }
        var circleRect = new Rect(center.X - rx, center.Y - ry, rx * 2, ry * 2);
        var radiusSize = new Size(rx, ry);
        return new CssRoundedRectangleGeometry(circleRect,
            new CssUsedBorderRadii(radiusSize, radiusSize, radiusSize, radiusSize));
    }

    private Geometry ResolvePolygon(Rect reference)
    {
        var vertices = new Point[Lengths.Length / 2];
        for (var i = 0; i < vertices.Length; i++)
            vertices[i] = new Point(reference.X + Lengths[i * 2].Resolve(reference.Width, 0),
                reference.Y + Lengths[i * 2 + 1].Resolve(reference.Height, 0));

        var figure = new PathFigure { StartPoint = vertices[0], IsClosed = true, IsFilled = true };
        var radius = PolygonRoundRadius.Resolve(0, 0);
        if (radius > 0 && vertices.Length >= 3) AddRoundedPolygon(figure, vertices, radius);
        else
            for (var i = 1; i < vertices.Length; i++)
                figure.Segments.Add(new LineSegment(vertices[i], true));

        var geometry = new PathGeometry { FillRule = PolygonFillRule };
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private Geometry ResolvePath(Rect reference)
    {
        var source = SvgPath!;
        if (reference.X == 0 && reference.Y == 0) return source;

        // path() coordinates are fixed CSS pixels relative to the reference-box
        // origin. Translate the points themselves so painting and hit testing
        // consume the same geometry on every renderer.
        var path = source.Clone();
        Point Shift(Point point) => new(point.X + reference.X, point.Y + reference.Y);
        void ShiftPoints(PointCollection points)
        {
            for (var index = 0; index < points.Count; index++) points[index] = Shift(points[index]);
        }

        foreach (var figure in path.Figures)
        {
            figure.StartPoint = Shift(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        line.Point = Shift(line.Point);
                        break;
                    case PolyLineSegment polyLine:
                        ShiftPoints(polyLine.Points);
                        break;
                    case BezierSegment bezier:
                        bezier.Point1 = Shift(bezier.Point1);
                        bezier.Point2 = Shift(bezier.Point2);
                        bezier.Point3 = Shift(bezier.Point3);
                        break;
                    case PolyBezierSegment polyBezier:
                        ShiftPoints(polyBezier.Points);
                        break;
                    case QuadraticBezierSegment quadratic:
                        quadratic.Point1 = Shift(quadratic.Point1);
                        quadratic.Point2 = Shift(quadratic.Point2);
                        break;
                    case PolyQuadraticBezierSegment polyQuadratic:
                        ShiftPoints(polyQuadratic.Points);
                        break;
                    case ArcSegment arc:
                        arc.Point = Shift(arc.Point);
                        break;
                }
            }
        }
        path.Freeze();
        return path;
    }

    private static void AddRoundedPolygon(PathFigure figure, Point[] vertices, double requestedRadius)
    {
        var entries = new Point[vertices.Length];
        var exits = new Point[vertices.Length];
        var radii = new double[vertices.Length];
        var sweeps = new SweepDirection[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var previous = vertices[(i + vertices.Length - 1) % vertices.Length];
            var current = vertices[i];
            var next = vertices[(i + 1) % vertices.Length];
            entries[i] = exits[i] = current;
            var ux = previous.X - current.X;
            var uy = previous.Y - current.Y;
            var vx = next.X - current.X;
            var vy = next.Y - current.Y;
            var incomingLength = Math.Sqrt(ux * ux + uy * uy);
            var outgoingLength = Math.Sqrt(vx * vx + vy * vy);
            if (incomingLength <= 1e-9 || outgoingLength <= 1e-9) continue;
            ux /= incomingLength; uy /= incomingLength;
            vx /= outgoingLength; vy /= outgoingLength;
            var angle = Math.Acos(Math.Clamp(ux * vx + uy * vy, -1, 1));
            if (angle <= 1e-7 || angle >= Math.PI - 1e-7) continue;
            var tangent = Math.Tan(angle / 2);
            var distance = Math.Min(requestedRadius / tangent,
                Math.Min(incomingLength, outgoingLength) / 2);
            if (!double.IsFinite(distance) || distance <= 1e-9) continue;
            var usedRadius = distance * tangent;
            if (!double.IsFinite(usedRadius) || usedRadius <= 1e-9) continue;
            entries[i] = new Point(current.X + ux * distance, current.Y + uy * distance);
            exits[i] = new Point(current.X + vx * distance, current.Y + vy * distance);
            radii[i] = usedRadius;
            sweeps[i] = ux * vy - uy * vx < 0
                ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        }

        figure.StartPoint = entries[0];
        for (var i = 0; i < vertices.Length; i++)
        {
            if (radii[i] > 0)
                figure.Segments.Add(new ArcSegment(exits[i], new Size(radii[i], radii[i]), 0,
                    false, sweeps[i], true));
            if (i + 1 < vertices.Length)
                figure.Segments.Add(new LineSegment(entries[i + 1], true));
        }
    }

    private static Thickness Negate(Thickness value) => new(-value.Left, -value.Top, -value.Right, -value.Bottom);

    private static Rect Inset(Rect rect, Thickness amount) => new(rect.Left + amount.Left, rect.Top + amount.Top,
        Math.Max(0, rect.Width - amount.Left - amount.Right),
        Math.Max(0, rect.Height - amount.Top - amount.Bottom));

    private static CssUsedBorderRadii NativeRadii(UIElement element, Size size)
    {
        if (CssDependencyPropertyLookup.Find(element.GetType(), "CornerRadius") is not { } property ||
            element.GetValue(property) is not CornerRadius radius) return default;
        return new CssUsedBorderRadii(new Size(radius.TopLeft, radius.TopLeft),
            new Size(radius.TopRight, radius.TopRight), new Size(radius.BottomRight, radius.BottomRight),
            new Size(radius.BottomLeft, radius.BottomLeft)).Normalize(size);
    }
}

internal static class CssClipPathProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssClipPath", typeof(CssClipPathValue), typeof(CssClipPathProperties),
        new PropertyMetadata(null, static (target, args) =>
        {
            if (target is UIElement element)
            {
                if (args.NewValue is CssClipPathValue { UrlResource: { } resource })
                    resource.Subscribe(element);
                UIElement.InvalidateHitTestCache();
                element.InvalidateVisual();
            }
        }));

    internal static Geometry? GetGeometry(UIElement element)
        => element.GetEffectiveValueLayer(ValueProperty) is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)
            ? (element.GetValue(ValueProperty) as CssClipPathValue)?.Resolve(element) : null;
}

internal sealed class CssClipPathCompiledValue(CssClipShape shape, CssClipBox box,
    CssLength[] lengths, CssClipRadius radius = CssClipRadius.Length,
    CssLength[]? roundRadii = null,
    bool centerXFromFarEdge = false, bool centerYFromFarEdge = false,
    FillRule polygonFillRule = FillRule.Nonzero, CssLength? polygonRoundRadius = null,
    PathGeometry? svgPath = null) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var lengthsContext = context.Lengths;
        CssLayoutLength[]? Compute(CssLength[] source)
        {
            var computed = new CssLayoutLength[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var length = source[i];
                length.ObserveContainerDependencies(lengthsContext);
                if (length.Expression is { } expression)
                    computed[i] = CssLayoutLength.Math(expression, lengthsContext);
                else if (length.Unit == CssUnit.Percent)
                    computed[i] = CssLayoutLength.Percent(length.Value / 100);
                else
                {
                    if (!length.TryResolve(lengthsContext, CssPercentBasis.NotSupported, out var px)) return null;
                    computed[i] = CssLayoutLength.Px(px);
                }
            }
            return computed;
        }

        var used = Compute(lengths);
        var usedRound = roundRadii is null ? null : Compute(roundRadii);
        var usedPolygonRound = polygonRoundRadius is { } round ? Compute([round]) : null;
        var usedPolygonRadius = usedPolygonRound is { Length: 1 }
            ? usedPolygonRound[0].Resolve(0, double.NaN) : 0;
        if (used is null || roundRadii is not null && usedRound is null ||
            polygonRoundRadius is not null && (usedPolygonRound is null ||
                !double.IsFinite(usedPolygonRadius) || usedPolygonRadius < 0)) return false;
        sink.Set(CssClipPathProperties.ValueProperty, new CssClipPathValue(shape, box, used, radius,
            usedRound, centerXFromFarEdge, centerYFromFarEdge, polygonFillRule,
            usedPolygonRound is null ? default : usedPolygonRound[0], svgPath));
        return true;
    }
}
