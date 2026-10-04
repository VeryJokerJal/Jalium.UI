using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class Border
{
    private ReliefSideImageKey? _reliefSideImageKey;
    private ImageBrush? _reliefSideImageBrush;

    private readonly record struct ReliefSideImageKey(
        Rect Bounds, Rect Inner, CssBorderStyles Styles,
        Color Left, Color Top, Color Right, Color Bottom,
        double LeftAlpha, double TopAlpha, double RightAlpha, double BottomAlpha,
        CssUsedBorderRadii? CssRadius, CornerRadius NativeRadius,
        BorderShape Shape, double SuperEllipseExponent,
        int PixelWidth, int PixelHeight);

    private bool TryDrawTranslucentReliefSides(DrawingContext context,
        CssBorderPaint colors, CssBorderStyles styles, Geometry ring,
        Rect bounds, Rect inner, Thickness border, CssUsedBorderRadii? cssRadius)
    {
        static bool SingleBandOrRelief(CssBorderLineStyle style) =>
            style == CssBorderLineStyle.Solid || IsRelief(style);
        if (!(IsRelief(styles.Left) || IsRelief(styles.Top) ||
              IsRelief(styles.Right) || IsRelief(styles.Bottom)) ||
            !SingleBandOrRelief(styles.Left) || !SingleBandOrRelief(styles.Top) ||
            !SingleBandOrRelief(styles.Right) || !SingleBandOrRelief(styles.Bottom) ||
            colors.Left is not SolidColorBrush left ||
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

        var deviceWidth = bounds.Width * Math.Max(1, DpiScale.DpiScaleX);
        var deviceHeight = bounds.Height * Math.Max(1, DpiScale.DpiScaleY);
        var density = Math.Min(1, Math.Sqrt(1048576.0 / (deviceWidth * deviceHeight)));
        var pixelWidth = (int)Math.Clamp(Math.Ceiling(deviceWidth * density), 1, 2048);
        var pixelHeight = (int)Math.Clamp(Math.Ceiling(deviceHeight * density), 1, 2048);
        var key = new ReliefSideImageKey(bounds, inner, styles,
            left.Color, top.Color, right.Color, bottom.Color,
            leftAlpha, topAlpha, rightAlpha, bottomAlpha,
            cssRadius, CornerRadius, Shape, SuperEllipseN, pixelWidth, pixelHeight);
        if (_reliefSideImageBrush is null || _reliefSideImageKey != key)
        {
            var source = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96,
                PixelFormat.Bgra32, null, BuildReliefSidePixels(key, border), pixelWidth * 4);
            _reliefSideImageBrush = new ImageBrush(source)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds,
            };
            _reliefSideImageKey = key;
        }

        context.DrawGeometry(_reliefSideImageBrush, null, ring);
        return true;
    }

    private static byte[] BuildReliefSidePixels(ReliefSideImageKey key, Thickness border)
    {
        var colors = new[] { key.Top, key.Right, key.Bottom, key.Left };
        var styles = new[] { key.Styles.Top, key.Styles.Right, key.Styles.Bottom, key.Styles.Left };
        var alphas = new[] { key.TopAlpha, key.RightAlpha, key.BottomAlpha, key.LeftAlpha };
        BuildStyleColors(colors, styles, out var outerColors, out var innerColors,
            out var hasSplitBand);
        var middleContains = hasSplitBand
            ? BorderInsetContains(key.Bounds, border, .5, key.CssRadius,
                key.NativeRadius, key.Shape, key.SuperEllipseExponent)
            : null;
        return BuildSideColorPixels(key.Bounds, key.Inner, key.PixelWidth, key.PixelHeight,
            outerColors, innerColors, alphas, middleContains);
    }

    private static void BuildStyleColors(Color[] colors, CssBorderLineStyle[] styles,
        out Color[] outerColors, out Color[] innerColors, out bool hasSplitBand)
    {
        outerColors = new Color[4];
        innerColors = new Color[4];
        hasSplitBand = false;
        for (var side = 0; side < 4; side++)
        {
            var style = styles[side];
            if (!IsRelief(style))
            {
                outerColors[side] = innerColors[side] = colors[side];
                continue;
            }
            var outsideLight = (style is CssBorderLineStyle.Ridge or CssBorderLineStyle.Outset) ==
                (side is 0 or 3);
            outerColors[side] = CssLineStyleShading.ShadeColor(colors[side], outsideLight);
            innerColors[side] = style is CssBorderLineStyle.Groove or CssBorderLineStyle.Ridge
                ? CssLineStyleShading.ShadeColor(colors[side], !outsideLight)
                : outerColors[side];
            hasSplitBand |= style is CssBorderLineStyle.Groove or CssBorderLineStyle.Ridge;
        }
    }

    private static Func<Point, bool> BorderInsetContains(Rect bounds, Thickness border,
        double fraction, CssUsedBorderRadii? cssRadius, CornerRadius nativeRadius,
        BorderShape shape, double superEllipseExponent)
    {
        var inset = ScaleBorder(border, fraction);
        var rect = GetInnerRect(bounds, inset);
        if (cssRadius is { } radii)
        {
            var insetRadii = radii.Inset(inset).Normalize(rect.Size);
            return point => CssRoundedRectangleGeometry.Contains(rect, insetRadii, point);
        }
        if (shape == BorderShape.SuperEllipse)
        {
            var insetRadii = GetInnerCornerRadius(nativeRadius, inset).Normalize(rect.Width, rect.Height);
            var exponent = superEllipseExponent is >= 2 and <= 16
                ? superEllipseExponent : 4.0;
            return point => SuperEllipseContains(rect, insetRadii, exponent, point);
        }
        var contour = new RectangleGeometry(rect, GetInnerCornerRadius(nativeRadius, inset));
        return contour.FillContains;
    }

    private static bool SuperEllipseContains(Rect rect, CornerRadius radii,
        double exponent, Point point)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || point.X < rect.Left ||
            point.X > rect.Right || point.Y < rect.Top || point.Y > rect.Bottom)
            return false;

        static bool InsideCorner(double x, double y, double centerX, double centerY,
            double radius, double exponent)
        {
            if (radius <= 0) return true;
            var dx = Math.Abs((x - centerX) / radius);
            var dy = Math.Abs((y - centerY) / radius);
            return Math.Pow(dx, exponent) + Math.Pow(dy, exponent) <= 1;
        }

        if (point.X < rect.Left + radii.TopLeft && point.Y < rect.Top + radii.TopLeft)
            return InsideCorner(point.X, point.Y, rect.Left + radii.TopLeft,
                rect.Top + radii.TopLeft, radii.TopLeft, exponent);
        if (point.X > rect.Right - radii.TopRight && point.Y < rect.Top + radii.TopRight)
            return InsideCorner(point.X, point.Y, rect.Right - radii.TopRight,
                rect.Top + radii.TopRight, radii.TopRight, exponent);
        if (point.X > rect.Right - radii.BottomRight && point.Y > rect.Bottom - radii.BottomRight)
            return InsideCorner(point.X, point.Y, rect.Right - radii.BottomRight,
                rect.Bottom - radii.BottomRight, radii.BottomRight, exponent);
        if (point.X < rect.Left + radii.BottomLeft && point.Y > rect.Bottom - radii.BottomLeft)
            return InsideCorner(point.X, point.Y, rect.Left + radii.BottomLeft,
                rect.Bottom - radii.BottomLeft, radii.BottomLeft, exponent);
        return true;
    }
}
