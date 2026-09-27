using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class Border
{
    private StyledSideImageKey? _styledSideImageKey;
    private ImageBrush? _styledSideImageBrush;

    private readonly record struct StyledSideImageKey(
        Rect Bounds, Rect Inner, CssBorderStyles Styles,
        Color Left, Color Top, Color Right, Color Bottom,
        double LeftAlpha, double TopAlpha, double RightAlpha, double BottomAlpha,
        CssUsedBorderRadii? CssRadius, CornerRadius NativeRadius,
        BorderShape Shape, double SuperEllipseExponent,
        int PixelWidth, int PixelHeight);

    private bool TryDrawTranslucentMixedStyles(DrawingContext context,
        CssBorderPaint colors, CssBorderStyles styles, Geometry ring,
        Rect bounds, Rect inner, Thickness border, CssUsedBorderRadii? cssRadius)
    {
        if (colors.Left is not SolidColorBrush left ||
            colors.Top is not SolidColorBrush top ||
            colors.Right is not SolidColorBrush right ||
            colors.Bottom is not SolidColorBrush bottom) return false;

        static double Alpha(SolidColorBrush brush) =>
            Math.Clamp(brush.Color.A / 255.0 * brush.Opacity, 0, 1);
        var leftAlpha = Alpha(left);
        var topAlpha = Alpha(top);
        var rightAlpha = Alpha(right);
        var bottomAlpha = Alpha(bottom);
        if (leftAlpha >= 1 && topAlpha >= 1 && rightAlpha >= 1 && bottomAlpha >= 1)
            return false;
        if (leftAlpha <= 0 && topAlpha <= 0 && rightAlpha <= 0 && bottomAlpha <= 0)
            return true;

        var deviceWidth = bounds.Width * Math.Max(1, DpiScale.DpiScaleX) * 2;
        var deviceHeight = bounds.Height * Math.Max(1, DpiScale.DpiScaleY) * 2;
        var density = Math.Min(1, Math.Sqrt(1048576.0 / (deviceWidth * deviceHeight)));
        var pixelWidth = (int)Math.Clamp(Math.Ceiling(deviceWidth * density), 1, 2048);
        var pixelHeight = (int)Math.Clamp(Math.Ceiling(deviceHeight * density), 1, 2048);
        var key = new StyledSideImageKey(bounds, inner, styles,
            left.Color, top.Color, right.Color, bottom.Color,
            leftAlpha, topAlpha, rightAlpha, bottomAlpha,
            cssRadius, CornerRadius, Shape, SuperEllipseN, pixelWidth, pixelHeight);
        if (_styledSideImageBrush is null || _styledSideImageKey != key)
        {
            var source = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96,
                PixelFormat.Bgra32, null, BuildStyledSidePixels(key, border), pixelWidth * 4);
            _styledSideImageBrush = new ImageBrush(source)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds,
            };
            _styledSideImageKey = key;
        }

        context.DrawGeometry(_styledSideImageBrush, null, ring);
        return true;
    }

    private byte[] BuildStyledSidePixels(StyledSideImageKey key, Thickness border)
    {
        var colors = new[] { key.Top, key.Right, key.Bottom, key.Left };
        var styles = new[] { key.Styles.Top, key.Styles.Right, key.Styles.Bottom, key.Styles.Left };
        var alphas = new[] { key.TopAlpha, key.RightAlpha, key.BottomAlpha, key.LeftAlpha };
        var widths = new[] { border.Top, border.Right, border.Bottom, border.Left };
        BuildStyleColors(colors, styles, out var outerColors, out var innerColors,
            out var hasSplitBand);
        var middleContains = hasSplitBand
            ? BorderInsetContains(key.Bounds, border, .5, key.CssRadius,
                key.NativeRadius, key.Shape, key.SuperEllipseExponent)
            : null;

        Func<Point, bool>? firstThirdContains = null;
        Func<Point, bool>? lastThirdContains = null;
        if (Enumerable.Range(0, 4).Any(side =>
                styles[side] == CssBorderLineStyle.Double && widths[side] >= 3))
        {
            firstThirdContains = BorderInsetContains(key.Bounds, border, 1.0 / 3,
                key.CssRadius, key.NativeRadius, key.Shape, key.SuperEllipseExponent);
            lastThirdContains = BorderInsetContains(key.Bounds, border, 2.0 / 3,
                key.CssRadius, key.NativeRadius, key.Shape, key.SuperEllipseExponent);
        }

        var patterns = new Dictionary<(CssBorderLineStyle Style, double Width), PatternHitTester>();
        if (styles.Any(IsPattern))
        {
            var contour = BorderContour(key.Bounds, border, key.CssRadius);
            for (var side = 0; side < 4; side++)
            {
                if (!IsPattern(styles[side]) || widths[side] <= 0) continue;
                var patternKey = (styles[side], widths[side]);
                if (!patterns.ContainsKey(patternKey))
                    patterns[patternKey] = new PatternHitTester(
                        BorderPattern(patternKey.Item1, patternKey.Item2, contour),
                        Math.Max(4, patternKey.Item2 * 2));
            }
        }

        bool Visible(Point point, int side)
        {
            var style = styles[side];
            if (widths[side] <= 0 || CssBorderStyles.IsHidden(style)) return false;
            if (style == CssBorderLineStyle.Double && widths[side] >= 3)
                return !firstThirdContains!(point) || lastThirdContains!(point);
            if (IsPattern(style)) return patterns[(style, widths[side])].Contains(point);
            return true;
        }

        return BuildSideColorPixels(key.Bounds, key.Inner, key.PixelWidth, key.PixelHeight,
            outerColors, innerColors, alphas, middleContains, Visible,
            ClippedInterior(key, border));
    }

    private static Rect ClippedInterior(StyledSideImageKey key, Thickness border)
    {
        double radiusX, radiusY;
        if (key.CssRadius is { } cssRadius)
        {
            var innerRadii = cssRadius.Inset(border).Normalize(key.Inner.Size);
            radiusX = Math.Max(Math.Max(innerRadii.TopLeft.Width, innerRadii.TopRight.Width),
                Math.Max(innerRadii.BottomRight.Width, innerRadii.BottomLeft.Width));
            radiusY = Math.Max(Math.Max(innerRadii.TopLeft.Height, innerRadii.TopRight.Height),
                Math.Max(innerRadii.BottomRight.Height, innerRadii.BottomLeft.Height));
        }
        else
        {
            var innerRadii = GetInnerCornerRadius(key.NativeRadius, border).Normalize(
                key.Inner.Width, key.Inner.Height);
            radiusX = radiusY = Math.Max(Math.Max(innerRadii.TopLeft, innerRadii.TopRight),
                Math.Max(innerRadii.BottomRight, innerRadii.BottomLeft));
        }
        var marginX = radiusX + 2 * key.Bounds.Width / key.PixelWidth;
        var marginY = radiusY + 2 * key.Bounds.Height / key.PixelHeight;
        return new Rect(key.Inner.Left + marginX, key.Inner.Top + marginY,
            Math.Max(0, key.Inner.Width - 2 * marginX),
            Math.Max(0, key.Inner.Height - 2 * marginY));
    }

    private sealed class PatternHitTester
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int X, int Y), List<PatternPolygon>> _cells = new();

        private readonly record struct PatternPolygon(Rect Bounds, Point[] Points);

        internal PatternHitTester(PathGeometry geometry, double cellSize)
        {
            _cellSize = cellSize;
            var flat = geometry.GetFlattenedPathGeometry();
            foreach (var figure in flat.Figures)
            {
                if (!figure.IsFilled) continue;
                var points = new List<Point> { figure.StartPoint };
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line) points.Add(line.Point);
                    else if (segment is PolyLineSegment lines) points.AddRange(lines.Points);
                }
                if (points.Count < 3) continue;
                var minX = points.Min(point => point.X);
                var minY = points.Min(point => point.Y);
                var maxX = points.Max(point => point.X);
                var maxY = points.Max(point => point.Y);
                var polygon = new PatternPolygon(
                    new Rect(minX, minY, maxX - minX, maxY - minY), points.ToArray());
                for (var cy = Cell(minY); cy <= Cell(maxY); cy++)
                    for (var cx = Cell(minX); cx <= Cell(maxX); cx++)
                    {
                        var cell = (cx, cy);
                        if (!_cells.TryGetValue(cell, out var polygons))
                            _cells[cell] = polygons = new List<PatternPolygon>();
                        polygons.Add(polygon);
                    }
            }
        }

        private int Cell(double coordinate) => (int)Math.Floor(coordinate / _cellSize);

        internal bool Contains(Point point)
        {
            if (!_cells.TryGetValue((Cell(point.X), Cell(point.Y)), out var polygons)) return false;
            foreach (var polygon in polygons)
            {
                var bounds = polygon.Bounds;
                if (point.X < bounds.Left || point.X > bounds.Right ||
                    point.Y < bounds.Top || point.Y > bounds.Bottom) continue;
                if (ContainsPolygon(polygon.Points, point)) return true;
            }
            return false;
        }

        private static bool ContainsPolygon(Point[] points, Point point)
        {
            var inside = false;
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                var cross = (b.X - a.X) * (point.Y - a.Y) -
                    (b.Y - a.Y) * (point.X - a.X);
                if (Math.Abs(cross) < 1e-9 && point.X >= Math.Min(a.X, b.X) &&
                    point.X <= Math.Max(a.X, b.X) && point.Y >= Math.Min(a.Y, b.Y) &&
                    point.Y <= Math.Max(a.Y, b.Y)) return true;
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < a.X + (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y))
                    inside = !inside;
            }
            return inside;
        }
    }
}
