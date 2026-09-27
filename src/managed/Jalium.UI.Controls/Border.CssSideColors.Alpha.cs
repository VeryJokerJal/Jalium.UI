using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Controls;

public partial class Border
{
    private MixedAlphaSideImageKey? _mixedAlphaSideImageKey;
    private ImageBrush? _mixedAlphaSideImageBrush;

    private readonly record struct MixedAlphaSideImageKey(
        Rect Bounds, Rect Inner,
        Color Left, Color Top, Color Right, Color Bottom,
        double LeftAlpha, double TopAlpha, double RightAlpha, double BottomAlpha,
        int PixelWidth, int PixelHeight);

    private ImageBrush GetMixedAlphaSolidSideBrush(Rect bounds, Rect inner,
        SolidColorBrush left, SolidColorBrush top,
        SolidColorBrush right, SolidColorBrush bottom,
        double leftAlpha, double topAlpha, double rightAlpha, double bottomAlpha)
    {
        var deviceWidth = bounds.Width * Math.Max(1, DpiScale.DpiScaleX);
        var deviceHeight = bounds.Height * Math.Max(1, DpiScale.DpiScaleY);
        // Keep the cached colour field bounded for very large controls.
        var density = Math.Min(1, Math.Sqrt(1048576.0 / (deviceWidth * deviceHeight)));
        var pixelWidth = (int)Math.Clamp(Math.Ceiling(deviceWidth * density), 1, 2048);
        var pixelHeight = (int)Math.Clamp(Math.Ceiling(deviceHeight * density), 1, 2048);
        var key = new MixedAlphaSideImageKey(bounds, inner,
            left.Color, top.Color, right.Color, bottom.Color,
            leftAlpha, topAlpha, rightAlpha, bottomAlpha, pixelWidth, pixelHeight);
        if (_mixedAlphaSideImageBrush is null || _mixedAlphaSideImageKey != key)
        {
            var pixels = BuildMixedAlphaSidePixels(key);
            var source = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96,
                PixelFormat.Bgra32, null, pixels, pixelWidth * 4);
            _mixedAlphaSideImageBrush = new ImageBrush(source)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds,
            };
            _mixedAlphaSideImageKey = key;
        }

        return _mixedAlphaSideImageBrush;
    }

    private static byte[] BuildMixedAlphaSidePixels(MixedAlphaSideImageKey key)
    {
        var colors = new[] { key.Top, key.Right, key.Bottom, key.Left };
        var alphas = new[] { key.TopAlpha, key.RightAlpha, key.BottomAlpha, key.LeftAlpha };
        return BuildSideColorPixels(key.Bounds, key.Inner, key.PixelWidth, key.PixelHeight,
            colors, colors, alphas, null);
    }

    private static byte[] BuildSideColorPixels(Rect bounds, Rect inner,
        int pixelWidth, int pixelHeight, Color[] outerColors, Color[] innerColors,
        double[] alphas, Func<Point, bool>? middleContains,
        Func<Point, int, bool>? sideVisible = null, Rect? clippedInterior = null)
    {
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        const int samples = 4;
        for (var py = 0; py < pixelHeight; py++)
        {
            for (var px = 0; px < pixelWidth; px++)
            {
                if (clippedInterior is { } hole &&
                    bounds.Left + px * bounds.Width / pixelWidth >= hole.Left &&
                    bounds.Left + (px + 1) * bounds.Width / pixelWidth <= hole.Right &&
                    bounds.Top + py * bounds.Height / pixelHeight >= hole.Top &&
                    bounds.Top + (py + 1) * bounds.Height / pixelHeight <= hole.Bottom)
                    continue;
                double sumA = 0, sumR = 0, sumG = 0, sumB = 0;
                for (var sy = 0; sy < samples; sy++)
                {
                    var y = bounds.Top +
                        (py + (sy + .5) / samples) * bounds.Height / pixelHeight;
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var x = bounds.Left +
                            (px + (sx + .5) / samples) * bounds.Width / pixelWidth;
                        var side = MixedAlphaSideAt(bounds, inner, x, y);
                        var point = new Point(x, y);
                        var colors = middleContains?.Invoke(point) == true
                            ? innerColors : outerColors;
                        var first = side < 4 ? side : side - 4;
                        var firstAlpha = sideVisible?.Invoke(point, first) == false
                            ? 0 : alphas[first] * (side < 4 ? 1 : .5);
                        var firstColor = colors[first];
                        sumA += firstAlpha;
                        sumR += firstAlpha * firstColor.R;
                        sumG += firstAlpha * firstColor.G;
                        sumB += firstAlpha * firstColor.B;
                        if (side >= 4)
                        {
                            var second = (first + 1) & 3;
                            var secondAlpha = sideVisible?.Invoke(point, second) == false
                                ? 0 : alphas[second] * .5;
                            var secondColor = colors[second];
                            sumA += secondAlpha;
                            sumR += secondAlpha * secondColor.R;
                            sumG += secondAlpha * secondColor.G;
                            sumB += secondAlpha * secondColor.B;
                        }
                    }
                }

                var offset = (py * pixelWidth + px) * 4;
                if (sumA <= 0) continue;
                pixels[offset] = (byte)Math.Round(sumB / sumA);
                pixels[offset + 1] = (byte)Math.Round(sumG / sumA);
                pixels[offset + 2] = (byte)Math.Round(sumR / sumA);
                pixels[offset + 3] = (byte)Math.Round(sumA * 255 / (samples * samples));
            }
        }
        return pixels;
    }

    // 0..3 select top/right/bottom/left; 4..7 split an exact diagonal
    // sample between that side and the next side clockwise.
    private static int MixedAlphaSideAt(Rect bounds, Rect inner, double x, double y)
    {
        // The corner diagonals are the same joins used by SideClip. Classifying
        // subpixels lets adjacent colours share one alpha value at each join.
        if (y < inner.Top)
        {
            if (x < inner.Left)
            {
                var cross = (inner.Left - bounds.Left) * (y - bounds.Top) -
                    (inner.Top - bounds.Top) * (x - bounds.Left);
                return Math.Abs(cross) < 1e-9 ? 7 : cross < 0 ? 0 : 3;
            }
            if (x >= inner.Right)
            {
                var cross = (inner.Right - bounds.Right) * (y - bounds.Top) -
                    (inner.Top - bounds.Top) * (x - bounds.Right);
                return Math.Abs(cross) < 1e-9 ? 4 : cross > 0 ? 0 : 1;
            }
            return 0;
        }
        if (y >= inner.Bottom)
        {
            if (x < inner.Left)
            {
                var cross = (inner.Left - bounds.Left) * (y - bounds.Bottom) +
                    (bounds.Bottom - inner.Bottom) * (x - bounds.Left);
                return Math.Abs(cross) < 1e-9 ? 6 : cross > 0 ? 2 : 3;
            }
            if (x >= inner.Right)
            {
                var cross = (bounds.Right - inner.Right) * (bounds.Bottom - y) -
                    (bounds.Bottom - inner.Bottom) * (bounds.Right - x);
                return Math.Abs(cross) < 1e-9 ? 5 : cross > 0 ? 1 : 2;
            }
            return 2;
        }
        if (x < inner.Left) return 3;
        if (x >= inner.Right) return 1;

        // Pixels just inside the inner edge still participate in image filtering.
        var topDistance = y - inner.Top;
        var rightDistance = inner.Right - x;
        var bottomDistance = inner.Bottom - y;
        var leftDistance = x - inner.Left;
        var nearest = Math.Min(Math.Min(topDistance, rightDistance),
            Math.Min(bottomDistance, leftDistance));
        if (nearest == rightDistance) return 1;
        if (nearest == bottomDistance) return 2;
        if (nearest == leftDistance) return 3;
        return 0;
    }
}
