using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class Border
{
    private SolidSideImageKey? _solidSideImageKey;
    private ImageBrush? _solidSideImageBrush;

    private readonly record struct SolidSideImageKey(
        Rect Bounds, Rect Inner, Color Left, Color Top, Color Right, Color Bottom, double Alpha);

    private void DrawCssSideColors(DrawingContext dc, CssBorderPaint colors, CssBorderStyles? styles)
    {
        var border = GetSnappedBorderThickness();
        if (border.Left <= 0 && border.Top <= 0 && border.Right <= 0 && border.Bottom <= 0) return;

        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var inner = GetInnerRect(bounds, border);
        var hasCssRadius = TryGetCssRadius(out var cssRadius);
        Geometry ring;
        if (hasCssRadius)
            ring = GetCssBorderGeometry(cssRadius);
        else
            ring = GetOrCreateAsymmetricStrokeGeometry(bounds, border, CornerRadius, inner,
                GetInnerCornerRadius(CornerRadius, border), Shape == BorderShape.SuperEllipse, SuperEllipseN);

        PaintCssSideColors(dc, colors, styles, bounds, border, inner, ring,
            hasCssRadius ? cssRadius : null);
    }

    internal void DrawCssSideColorsFor(DrawingContext dc, CssBorderPaint colors,
        CssBorderStyles? styles, Rect bounds, Thickness border, CssUsedBorderRadii? cssRadius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 ||
            border.Left <= 0 && border.Top <= 0 && border.Right <= 0 && border.Bottom <= 0)
            return;
        var inner = GetInnerRect(bounds, border);
        var radii = cssRadius ?? default;
        var ring = new PathGeometry { FillRule = FillRule.EvenOdd };
        ring.Figures.Add(CssRoundedRectangleGeometry.Figure(bounds, radii));
        if (inner.Width > 0 && inner.Height > 0)
            ring.Figures.Add(CssRoundedRectangleGeometry.Figure(inner,
                radii.Inset(border).Normalize(inner.Size)));
        ring.Freeze();
        PaintCssSideColors(dc, colors, styles, bounds, border, inner, ring, cssRadius);
    }

    private void PaintCssSideColors(DrawingContext dc, CssBorderPaint colors,
        CssBorderStyles? styles, Rect bounds, Thickness border, Rect inner,
        Geometry ring, CssUsedBorderRadii? cssRadius)
    {

        if ((styles is null || styles.AllSolid) &&
            TryDrawTranslucentSolidSides(dc, colors, ring, bounds, inner)) return;

        if (styles is not null &&
            TryDrawTranslucentReliefSides(dc, colors, styles, ring, bounds, inner,
                border, cssRadius)) return;

        if (styles is not null &&
            TryDrawTranslucentPatternSides(dc, colors, styles, ring, bounds, inner,
                border, cssRadius)) return;

        if (styles is not null &&
            TryDrawTranslucentMixedStyles(dc, colors, styles, ring, bounds, inner,
                border, cssRadius)) return;

        var needsPattern = styles is not null &&
            (IsPattern(styles.Left) || IsPattern(styles.Top) ||
             IsPattern(styles.Right) || IsPattern(styles.Bottom));
        var needsDouble = styles is not null &&
            (styles.Left == CssBorderLineStyle.Double || styles.Top == CssBorderLineStyle.Double ||
             styles.Right == CssBorderLineStyle.Double || styles.Bottom == CssBorderLineStyle.Double);
        var needsRelief = styles is not null &&
            (IsRelief(styles.Left) || IsRelief(styles.Top) ||
             IsRelief(styles.Right) || IsRelief(styles.Bottom));
        var contour = needsPattern ? BorderContour(bounds, border, cssRadius) : null;
        var patternCache = needsPattern ? new Dictionary<(CssBorderLineStyle Style, double Thickness), PathGeometry>() : null;
        var outerBand = needsDouble ? BorderBand(bounds, border, 0, 1.0 / 3,
            cssRadius) : null;
        var innerBand = needsDouble ? BorderBand(bounds, border, 2.0 / 3, 1,
            cssRadius) : null;
        var outerReliefBand = needsRelief ? BorderBand(bounds, border, 0, .5,
            cssRadius) : null;
        var innerReliefBand = needsRelief ? BorderBand(bounds, border, .5, 1,
            cssRadius) : null;

        // An opaque base coat closes antialias seams between adjoining solid colors.
        // Patterned borders need their gaps to expose the background, so skip it there.
        var baseCoat = colors.IsOpaque && !needsPattern && !needsDouble;
        if (baseCoat)
        {
            var baseBrush = styles is null || !CssBorderStyles.IsHidden(styles.Top) ? colors.Top :
                !CssBorderStyles.IsHidden(styles.Right) ? colors.Right :
                !CssBorderStyles.IsHidden(styles.Bottom) ? colors.Bottom : colors.Left;
            dc.DrawGeometry(baseBrush, null, ring);
        }
        PaintSide(dc, colors.Top, styles?.Top ?? CssBorderLineStyle.Solid, border.Top,
            ring, contour, patternCache, outerBand, innerBand, outerReliefBand, innerReliefBand, true,
            new(bounds.TopLeft, bounds.TopRight, inner.TopRight, inner.TopLeft));
        PaintSide(dc, colors.Right, styles?.Right ?? CssBorderLineStyle.Solid, border.Right,
            ring, contour, patternCache, outerBand, innerBand, outerReliefBand, innerReliefBand, false,
            new(bounds.TopRight, bounds.BottomRight, inner.BottomRight, inner.TopRight));
        PaintSide(dc, colors.Bottom, styles?.Bottom ?? CssBorderLineStyle.Solid, border.Bottom,
            ring, contour, patternCache, outerBand, innerBand, outerReliefBand, innerReliefBand, false,
            new(bounds.BottomRight, bounds.BottomLeft, inner.BottomLeft, inner.BottomRight));
        PaintSide(dc, colors.Left, styles?.Left ?? CssBorderLineStyle.Solid, border.Left,
            ring, contour, patternCache, outerBand, innerBand, outerReliefBand, innerReliefBand, true,
            new(bounds.BottomLeft, bounds.TopLeft, inner.TopLeft, inner.BottomLeft));
    }

    private bool TryDrawTranslucentSolidSides(DrawingContext context, CssBorderPaint colors,
        Geometry ring, Rect bounds, Rect inner)
    {
        if (!TryGetTranslucentSideColorBrush(colors, bounds, inner, out var brush)) return false;
        if (brush is not null) context.DrawGeometry(brush, null, ring);
        return true;
    }

    private bool TryGetTranslucentSideColorBrush(CssBorderPaint colors,
        Rect bounds, Rect inner, out ImageBrush? brush)
    {
        brush = null;
        if (colors.Left is not SolidColorBrush left ||
            colors.Top is not SolidColorBrush top ||
            colors.Right is not SolidColorBrush right ||
            colors.Bottom is not SolidColorBrush bottom) return false;

        static double Alpha(SolidColorBrush brush)
            => Math.Clamp(brush.Color.A / 255.0 * brush.Opacity, 0, 1);
        var alpha = Alpha(top);
        var leftAlpha = Alpha(left);
        var rightAlpha = Alpha(right);
        var bottomAlpha = Alpha(bottom);
        if (leftAlpha != alpha || rightAlpha != alpha || bottomAlpha != alpha)
        {
            brush = GetMixedAlphaSolidSideBrush(bounds, inner,
                left, top, right, bottom, leftAlpha, alpha, rightAlpha, bottomAlpha);
            return true;
        }
        if (alpha >= 1) return false;
        if (alpha <= 0) return true;

        var key = new SolidSideImageKey(bounds, inner,
            Color.FromRgb(left.Color.R, left.Color.G, left.Color.B),
            Color.FromRgb(top.Color.R, top.Color.G, top.Color.B),
            Color.FromRgb(right.Color.R, right.Color.G, right.Color.B),
            Color.FromRgb(bottom.Color.R, bottom.Color.G, bottom.Color.B), alpha);
        if (_solidSideImageBrush is null || _solidSideImageKey != key)
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(key.Top), null,
                new RectangleGeometry(bounds)));
            void AddSide(Color color, (Point First, Point Second, Point Third, Point Fourth) vertices)
                => drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null,
                    SideClip(vertices)));
            AddSide(key.Top, new(bounds.TopLeft, bounds.TopRight, inner.TopRight, inner.TopLeft));
            AddSide(key.Right, new(bounds.TopRight, bounds.BottomRight, inner.BottomRight, inner.TopRight));
            AddSide(key.Bottom, new(bounds.BottomRight, bounds.BottomLeft, inner.BottomLeft, inner.BottomRight));
            AddSide(key.Left, new(bounds.BottomLeft, bounds.TopLeft, inner.TopLeft, inner.BottomLeft));
            _solidSideImageBrush = new ImageBrush(new DrawingImage(drawing))
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds,
                Opacity = alpha,
            };
            _solidSideImageKey = key;
        }

        brush = _solidSideImageBrush;
        return true;
    }

    private static bool IsPattern(CssBorderLineStyle style)
        => style is CssBorderLineStyle.Dashed or CssBorderLineStyle.Dotted;

    private static bool IsRelief(CssBorderLineStyle style)
        => style is CssBorderLineStyle.Groove or CssBorderLineStyle.Ridge or
            CssBorderLineStyle.Inset or CssBorderLineStyle.Outset;

    private static void PaintSide(DrawingContext dc, Brush brush, CssBorderLineStyle style,
        double thickness, Geometry ring, IReadOnlyList<Point>? contour,
        Dictionary<(CssBorderLineStyle Style, double Thickness), PathGeometry>? patternCache,
        Geometry? outerBand,
        Geometry? innerBand,
        Geometry? outerReliefBand,
        Geometry? innerReliefBand,
        bool topOrLeft,
        (Point First, Point Second, Point Third, Point Fourth) vertices)
    {
        if (thickness <= 0 || CssBorderStyles.IsHidden(style)) return;
        if (IsPattern(style) && contour is not null)
        {
            var key = (style, thickness);
            if (!patternCache!.TryGetValue(key, out var pattern))
                patternCache[key] = pattern = BorderPattern(style, thickness, contour);
            dc.PushClip(ring);
            dc.PushClip(SideClip(vertices));
            dc.DrawGeometry(brush, null, pattern);
            dc.Pop();
            dc.Pop();
            return;
        }
        if (style == CssBorderLineStyle.Double && thickness >= 3 &&
            outerBand is not null && innerBand is not null)
        {
            DrawSide(dc, outerBand, brush, vertices);
            DrawSide(dc, innerBand, brush, vertices);
            return;
        }
        if (IsRelief(style))
        {
            var outsideLight = (style is CssBorderLineStyle.Ridge or CssBorderLineStyle.Outset) == topOrLeft;
            if ((style is CssBorderLineStyle.Groove or CssBorderLineStyle.Ridge) &&
                outerReliefBand is not null && innerReliefBand is not null)
            {
                DrawSide(dc, outerReliefBand, CssLineStyleShading.Shade(brush, outsideLight), vertices);
                DrawSide(dc, innerReliefBand, CssLineStyleShading.Shade(brush, !outsideLight), vertices);
            }
            else
                DrawSide(dc, ring, CssLineStyleShading.Shade(brush, outsideLight), vertices);
            return;
        }
        DrawSide(dc, ring, brush, vertices);
    }

    private IReadOnlyList<Point> BorderContour(Rect bounds, Thickness border, CssUsedBorderRadii? cssRadius)
    {
        var half = ScaleBorder(border, .5);
        var middle = GetInnerRect(bounds, half);
        var figure = cssRadius is { } radii
            ? CssRoundedRectangleGeometry.Figure(middle, radii.Inset(half).Normalize(middle.Size))
            : Shape == BorderShape.SuperEllipse
                ? BuildSuperEllipseFigure(middle, GetInnerCornerRadius(CornerRadius, half), SuperEllipseN)
                : BuildRoundedRectFigure(middle, GetInnerCornerRadius(CornerRadius, half));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var flat = geometry.GetFlattenedPathGeometry();
        var points = new List<Point>();
        foreach (var pathFigure in flat.Figures)
        {
            points.Add(pathFigure.StartPoint);
            foreach (var segment in pathFigure.Segments)
            {
                if (segment is LineSegment line) points.Add(line.Point);
                else if (segment is PolyLineSegment polyline) points.AddRange(polyline.Points);
            }
            if (pathFigure.IsClosed && points.Count > 1 && points[^1] != pathFigure.StartPoint)
                points.Add(pathFigure.StartPoint);
        }
        return points;
    }

    private PathGeometry BorderBand(Rect bounds, Thickness border, double outerFraction,
        double innerFraction, CssUsedBorderRadii? cssRadius)
    {
        var outerInset = ScaleBorder(border, outerFraction);
        var innerInset = ScaleBorder(border, innerFraction);
        var outer = GetInnerRect(bounds, outerInset);
        var inner = GetInnerRect(bounds, innerInset);
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        if (outer.Width > 0 && outer.Height > 0)
        {
            geometry.Figures.Add(BorderBandFigure(outer, outerInset, cssRadius));
            if (inner.Width > 0 && inner.Height > 0)
                geometry.Figures.Add(BorderBandFigure(inner, innerInset, cssRadius));
        }
        geometry.Freeze();
        return geometry;
    }

    private PathFigure BorderBandFigure(Rect rect, Thickness inset, CssUsedBorderRadii? cssRadius)
        => cssRadius is { } radii
            ? CssRoundedRectangleGeometry.Figure(rect, radii.Inset(inset).Normalize(rect.Size))
            : Shape == BorderShape.SuperEllipse
                ? BuildSuperEllipseFigure(rect, GetInnerCornerRadius(CornerRadius, inset), SuperEllipseN)
                : BuildRoundedRectFigure(rect, GetInnerCornerRadius(CornerRadius, inset));

    private static Thickness ScaleBorder(Thickness border, double factor)
        => new(border.Left * factor, border.Top * factor,
            border.Right * factor, border.Bottom * factor);

    private static PathGeometry BorderPattern(
        CssBorderLineStyle style, double thickness, IReadOnlyList<Point> points)
    {
        var geometry = new PathGeometry();
        if (points.Count < 2) return geometry;
        var period = thickness * (style == CssBorderLineStyle.Dashed ? 4 : 2.5);
        var onLength = thickness * 2;
        var pathPosition = 0.0;
        var nextDot = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6) continue;
            if (style == CssBorderLineStyle.Dotted)
            {
                while (nextDot < pathPosition + length - 1e-6)
                {
                    var fraction = (nextDot - pathPosition) / length;
                    geometry.Figures.Add(DotFigure(
                        new Point(start.X + dx * fraction, start.Y + dy * fraction), thickness / 2));
                    nextDot += period;
                }
            }
            else
            {
                var consumed = 0.0;
                while (consumed < length - 1e-6)
                {
                    var phase = (pathPosition + consumed) % period;
                    var visible = phase < onLength;
                    var step = Math.Min(length - consumed,
                        visible ? onLength - phase : period - phase);
                    if (step < 1e-6) step = Math.Min(length - consumed, 1e-6);
                    if (visible)
                    {
                        var from = consumed / length;
                        var to = (consumed + step) / length;
                        geometry.Figures.Add(DashFigure(
                            new Point(start.X + dx * from, start.Y + dy * from),
                            new Point(start.X + dx * to, start.Y + dy * to), thickness / 2));
                    }
                    consumed += step;
                }
            }
            pathPosition += length;
        }
        geometry.Freeze();
        return geometry;
    }

    private static PathFigure DashFigure(Point from, Point to, double halfWidth)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6) length = 1;
        var nx = -dy / length * halfWidth;
        var ny = dx / length * halfWidth;
        var figure = new PathFigure
        {
            StartPoint = new Point(from.X + nx, from.Y + ny),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment(new Point(to.X + nx, to.Y + ny), true));
        figure.Segments.Add(new LineSegment(new Point(to.X - nx, to.Y - ny), true));
        figure.Segments.Add(new LineSegment(new Point(from.X - nx, from.Y - ny), true));
        return figure;
    }

    private static PathFigure DotFigure(Point center, double radius)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(center.X + radius, center.Y),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new ArcSegment(new Point(center.X - radius, center.Y),
            new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new ArcSegment(new Point(center.X + radius, center.Y),
            new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        return figure;
    }

    private static void DrawSide(DrawingContext dc, Geometry ring, Brush brush,
        (Point First, Point Second, Point Third, Point Fourth) vertices)
    {
        dc.PushClip(SideClip(vertices));
        dc.DrawGeometry(brush, null, ring);
        dc.Pop();
    }

    private static PathGeometry SideClip(
        (Point First, Point Second, Point Third, Point Fourth) vertices)
    {
        var figure = new PathFigure { StartPoint = vertices.First, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(vertices.Second, true));
        figure.Segments.Add(new LineSegment(vertices.Third, true));
        figure.Segments.Add(new LineSegment(vertices.Fourth, true));
        var clip = new PathGeometry();
        clip.Figures.Add(figure);
        clip.Freeze();
        return clip;
    }
}
