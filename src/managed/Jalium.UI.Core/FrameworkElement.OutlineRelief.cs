using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI;

public partial class FrameworkElement
{
    private OutlineReliefGeometry? _outlineReliefGeometry;
    private OutlineReliefGeometry? _outlineReliefImageGeometry;
    private ImageBrush? _outlineReliefImageBrush;
    private OutlineStyle _outlineReliefImageStyle;
    private Color _outlineReliefImageColor;
    private double _outlineReliefImageOpacity;

    private sealed class OutlineReliefGeometry
    {
        internal readonly Size Size;
        internal readonly CssUsedBorderRadii Radii;
        internal readonly double OuterExpand;
        internal readonly double Thickness;
        internal readonly PathGeometry[] Full;
        internal readonly PathGeometry[] OuterHalf;
        internal readonly PathGeometry[] InnerHalf;
        internal readonly PathGeometry Ring;

        internal OutlineReliefGeometry(Size size, CssUsedBorderRadii radii, double outerExpand, double thickness)
        {
            Size = size;
            Radii = radii;
            OuterExpand = outerExpand;
            Thickness = thickness;
            Full = OutlineSideBand(size, radii, outerExpand, outerExpand - thickness);
            OuterHalf = OutlineSideBand(size, radii, outerExpand, outerExpand - thickness / 2);
            InnerHalf = OutlineSideBand(size, radii, outerExpand - thickness / 2, outerExpand - thickness);
            Ring = OutlineRing(size, radii, outerExpand, outerExpand - thickness);
        }
    }

    private void DrawReliefOutline(DrawingContext context, Size size, Brush brush,
        double thickness, OutlineStyle style, double outerExpand)
    {
        var radii = CssBorderRadiusProperties.Get(this)?.Resolve(size) ?? NativeOutlineRadii(size);
        var geometry = _outlineReliefGeometry;
        if (geometry is null || geometry.Size != size || geometry.Radii != radii ||
            geometry.OuterExpand != outerExpand || geometry.Thickness != thickness)
            _outlineReliefGeometry = geometry = new OutlineReliefGeometry(size, radii, outerExpand, thickness);

        if (brush is SolidColorBrush translucent &&
            (translucent.Color.A < byte.MaxValue || translucent.Opacity < 1))
        {
            DrawTranslucentReliefOutline(context, geometry, translucent, style);
            return;
        }

        // A single opaque undercoat covers the antialias gap where two side
        // polygons meet on a diagonal. Transparent outlines cannot use it,
        // because their alpha would be composited twice.
        if (brush is SolidColorBrush { Color.A: byte.MaxValue, Opacity: >= 1 })
            context.DrawGeometry(brush, null, geometry.Ring);

        var split = style is OutlineStyle.Groove or OutlineStyle.Ridge;
        for (var side = 0; side < 4; side++)
        {
            var topOrLeft = side is 0 or 3;
            var outerLight = (style is OutlineStyle.Ridge or OutlineStyle.Outset) == topOrLeft;
            var outerBrush = CssLineStyleShading.Shade(brush, outerLight);
            context.DrawGeometry(outerBrush, null, split ? geometry.OuterHalf[side] : geometry.Full[side]);
            if (split)
                context.DrawGeometry(CssLineStyleShading.Shade(brush, !outerLight), null,
                    geometry.InnerHalf[side]);
        }
    }

    private void DrawTranslucentReliefOutline(DrawingContext context,
        OutlineReliefGeometry geometry, SolidColorBrush source, OutlineStyle style)
    {
        var alpha = Math.Clamp(source.Opacity * source.Color.A / 255.0, 0, 1);
        if (alpha <= 0) return;
        if (!ReferenceEquals(_outlineReliefImageGeometry, geometry) ||
            _outlineReliefImageStyle != style ||
            _outlineReliefImageColor != source.Color ||
            _outlineReliefImageOpacity != source.Opacity ||
            _outlineReliefImageBrush is null)
        {
            var baseColor = source.Color;
            var opaque = new SolidColorBrush(Color.FromRgb(baseColor.R, baseColor.G, baseColor.B));
            opaque.Freeze();
            var drawing = new DrawingGroup();
            // The image holds opaque side colors. Its single final opacity is
            // applied after all seams and half-bands have been composited.
            drawing.Children.Add(new GeometryDrawing(opaque, null,
                new RectangleGeometry(geometry.Ring.Bounds)));
            var split = style is OutlineStyle.Groove or OutlineStyle.Ridge;
            for (var side = 0; side < 4; side++)
            {
                var outerLight = (style is OutlineStyle.Ridge or OutlineStyle.Outset) ==
                    (side is 0 or 3);
                drawing.Children.Add(new GeometryDrawing(
                    CssLineStyleShading.Shade(opaque, outerLight), null,
                    split ? geometry.OuterHalf[side] : geometry.Full[side]));
                if (split)
                    drawing.Children.Add(new GeometryDrawing(
                        CssLineStyleShading.Shade(opaque, !outerLight), null,
                        geometry.InnerHalf[side]));
            }
            var image = new DrawingImage(drawing);
            _outlineReliefImageBrush = new ImageBrush(image)
            {
                Stretch = Stretch.Fill,
                Opacity = alpha,
            };
            _outlineReliefImageGeometry = geometry;
            _outlineReliefImageStyle = style;
            _outlineReliefImageColor = source.Color;
            _outlineReliefImageOpacity = source.Opacity;
        }

        context.DrawGeometry(_outlineReliefImageBrush, null, geometry.Ring);
    }

    private CssUsedBorderRadii NativeOutlineRadii(Size size)
    {
        var radius = Visual.GetCornerRadius(this);
        return new CssUsedBorderRadii(
            new Size(radius.TopLeft, radius.TopLeft),
            new Size(radius.TopRight, radius.TopRight),
            new Size(radius.BottomRight, radius.BottomRight),
            new Size(radius.BottomLeft, radius.BottomLeft)).Normalize(size);
    }

    private static PathGeometry[] OutlineSideBand(Size size, CssUsedBorderRadii radii,
        double outerExpand, double innerExpand)
    {
        var outerRect = new Rect(-outerExpand, -outerExpand,
            size.Width + 2 * outerExpand, size.Height + 2 * outerExpand);
        var innerRect = new Rect(-innerExpand, -innerExpand,
            Math.Max(0, size.Width + 2 * innerExpand),
            Math.Max(0, size.Height + 2 * innerExpand));
        var sides = new PathGeometry[4];
        if (outerRect.Width <= 0 || outerRect.Height <= 0)
        {
            for (var i = 0; i < 4; i++) sides[i] = new PathGeometry();
            return sides;
        }

        var outer = new OutlineContour(outerRect,
            ExpandCssOutlineRadii(radii, outerExpand, outerRect.Size));
        var inner = new OutlineContour(innerRect,
            ExpandCssOutlineRadii(radii, innerExpand, innerRect.Size));
        for (var side = 0; side < 4; side++)
        {
            var next = (side + 1) & 3;
            var figure = new PathFigure
            {
                StartPoint = outer.Diagonal[side],
                IsClosed = true,
                IsFilled = true,
            };
            AddOutlineArc(figure, outer.Cardinal[side * 2], outer.Corner[side],
                SweepDirection.Clockwise);
            figure.Segments.Add(new LineSegment(outer.Cardinal[side * 2 + 1], true));
            AddOutlineArc(figure, outer.Diagonal[next], outer.Corner[next],
                SweepDirection.Clockwise);
            figure.Segments.Add(new LineSegment(inner.Diagonal[next], true));
            AddOutlineArc(figure, inner.Cardinal[side * 2 + 1], inner.Corner[next],
                SweepDirection.Counterclockwise);
            figure.Segments.Add(new LineSegment(inner.Cardinal[side * 2], true));
            AddOutlineArc(figure, inner.Diagonal[side], inner.Corner[side],
                SweepDirection.Counterclockwise);
            var path = new PathGeometry();
            path.Figures.Add(figure);
            path.Freeze();
            sides[side] = path;
        }
        return sides;
    }

    private static PathGeometry OutlineRing(Size size, CssUsedBorderRadii radii,
        double outerExpand, double innerExpand)
    {
        var outerRect = new Rect(-outerExpand, -outerExpand,
            size.Width + 2 * outerExpand, size.Height + 2 * outerExpand);
        var innerRect = new Rect(-innerExpand, -innerExpand,
            size.Width + 2 * innerExpand, size.Height + 2 * innerExpand);
        var ring = new PathGeometry { FillRule = FillRule.EvenOdd };
        if (outerRect.Width > 0 && outerRect.Height > 0)
        {
            ring.Figures.Add(CssRoundedRectangleGeometry.Figure(outerRect,
                ExpandCssOutlineRadii(radii, outerExpand, outerRect.Size)));
            if (innerRect.Width > 0 && innerRect.Height > 0)
                ring.Figures.Add(CssRoundedRectangleGeometry.Figure(innerRect,
                    ExpandCssOutlineRadii(radii, innerExpand, innerRect.Size)));
        }
        ring.Freeze();
        return ring;
    }

    private static void AddOutlineArc(PathFigure figure, Point end, Size radius,
        SweepDirection direction)
    {
        if (radius.Width > 0 && radius.Height > 0)
            figure.Segments.Add(new ArcSegment(end, radius, 0, false, direction, true));
        else
            figure.Segments.Add(new LineSegment(end, true));
    }

    private sealed class OutlineContour
    {
        internal readonly Size[] Corner;
        internal readonly Point[] Cardinal;
        internal readonly Point[] Diagonal;

        internal OutlineContour(Rect rect, CssUsedBorderRadii radii)
        {
            static Size KeepEllipse(Size value) => value.Width > 0 && value.Height > 0 ? value : default;
            var tl = KeepEllipse(radii.TopLeft);
            var tr = KeepEllipse(radii.TopRight);
            var br = KeepEllipse(radii.BottomRight);
            var bl = KeepEllipse(radii.BottomLeft);
            Corner = [tl, tr, br, bl];
            Cardinal =
            [
                new(rect.Left + tl.Width, rect.Top),
                new(rect.Right - tr.Width, rect.Top),
                new(rect.Right, rect.Top + tr.Height),
                new(rect.Right, rect.Bottom - br.Height),
                new(rect.Right - br.Width, rect.Bottom),
                new(rect.Left + bl.Width, rect.Bottom),
                new(rect.Left, rect.Bottom - bl.Height),
                new(rect.Left, rect.Top + tl.Height),
            ];
            const double diagonalInset = 0.2928932188134525; // 1 - cos(45°)
            Diagonal =
            [
                new(rect.Left + tl.Width * diagonalInset, rect.Top + tl.Height * diagonalInset),
                new(rect.Right - tr.Width * diagonalInset, rect.Top + tr.Height * diagonalInset),
                new(rect.Right - br.Width * diagonalInset, rect.Bottom - br.Height * diagonalInset),
                new(rect.Left + bl.Width * diagonalInset, rect.Bottom - bl.Height * diagonalInset),
            ];
        }
    }
}
