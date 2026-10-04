using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class Border
{
    private bool TryDrawTranslucentPatternSides(DrawingContext context,
        CssBorderPaint colors, CssBorderStyles styles, Geometry ring,
        Rect bounds, Rect inner, Thickness border, CssUsedBorderRadii? cssRadius)
    {
        var style = styles.Top;
        static bool Supported(CssBorderLineStyle value) =>
            value == CssBorderLineStyle.Solid || IsPattern(value);
        if (!(IsPattern(styles.Left) || IsPattern(styles.Top) ||
              IsPattern(styles.Right) || IsPattern(styles.Bottom)) ||
            !Supported(styles.Left) || !Supported(styles.Top) ||
            !Supported(styles.Right) || !Supported(styles.Bottom) ||
            !TryGetTranslucentSideColorBrush(colors, bounds, inner, out var brush)) return false;
        if (brush is null) return true;

        var contour = BorderContour(bounds, border, cssRadius);
        Geometry marks;
        if (IsPattern(style) && styles.Right == style && styles.Bottom == style &&
            styles.Left == style && border.Top > 0 && border.Left == border.Top &&
            border.Right == border.Top && border.Bottom == border.Top)
            marks = BorderPattern(style, border.Top, contour);
        else
        {
            var joined = new PathGeometry { FillRule = FillRule.Nonzero };
            var patterns = new Dictionary<(CssBorderLineStyle Style, double Thickness), PathGeometry>();
            void AddSide(CssBorderLineStyle sideStyle, double thickness,
                (Point First, Point Second, Point Third, Point Fourth) vertices)
            {
                if (thickness <= 0) return;
                var clip = SideClip(vertices);
                if (sideStyle == CssBorderLineStyle.Solid)
                {
                    joined.Figures.AddRange(clip.Figures);
                    return;
                }
                var key = (sideStyle, thickness);
                if (!patterns.TryGetValue(key, out var pattern))
                    patterns[key] = pattern = BorderPattern(sideStyle, thickness, contour);
                var sideMarks = Geometry.Combine(pattern, clip, GeometryCombineMode.Intersect, null);
                joined.Figures.AddRange(sideMarks.Figures);
            }
            AddSide(styles.Top, border.Top,
                new(bounds.TopLeft, bounds.TopRight, inner.TopRight, inner.TopLeft));
            AddSide(styles.Right, border.Right,
                new(bounds.TopRight, bounds.BottomRight, inner.BottomRight, inner.TopRight));
            AddSide(styles.Bottom, border.Bottom,
                new(bounds.BottomRight, bounds.BottomLeft, inner.BottomLeft, inner.BottomRight));
            AddSide(styles.Left, border.Left,
                new(bounds.BottomLeft, bounds.TopLeft, inner.TopLeft, inner.BottomLeft));
            joined.Freeze();
            marks = joined;
        }

        // One geometry receives the four-side colour field. The ring clip
        // retains curved edges and gaps without compositing adjacent marks twice.
        context.PushClip(ring);
        context.DrawGeometry(brush, null, marks);
        context.Pop();
        return true;
    }
}
