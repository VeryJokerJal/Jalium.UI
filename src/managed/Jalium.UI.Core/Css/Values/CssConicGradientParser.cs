using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>Parses angular gradients and materializes a used-size image brush.</summary>
internal static class CssConicGradientParser
{
    internal static Brush? Parse(ref CssTokenReader args, in CssLengthContext context,
        double width, double height, bool repeating)
    {
        var fromAngle = 0.0;
        var center = new Point(.5, .5);
        var hasFrom = false;
        var hasCenter = false;
        var sawPrelude = false;
        CssColorParser.CssInterpolationMethod? interpolation = null;
        var probe = args;
        while (true)
        {
            var before = probe;
            if (CssColorParser.TryReadInterpolationMethod(ref probe, out var method))
            {
                if (interpolation is not null) return null;
                interpolation = method;
                sawPrelude = true;
                continue;
            }
            probe = before;
            if (!probe.TryReadIdent(out var keyword)) break;
            if (keyword.Equals("from", StringComparison.OrdinalIgnoreCase))
            {
                if (hasFrom || hasCenter || !probe.TryReadNumber(out var value, out var unit) ||
                    !double.IsFinite(value) ||
                    !(unit == CssUnit.None && value == 0 ||
                      unit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn &&
                      CssUnitConversion.TryToDegrees(value, unit, out value))) return null;
                fromAngle = value;
                hasFrom = true;
            }
            else if (keyword.Equals("at", StringComparison.OrdinalIgnoreCase))
            {
                if (hasCenter || !CssGradientParser.TryParsePosition(ref probe, context, width, height, out center))
                    return null;
                hasCenter = true;
            }
            else
            {
                probe = before;
                break;
            }
            sawPrelude = true;
        }
        if (sawPrelude)
        {
            if (!probe.TryReadComma()) return null;
            args = probe;
        }

        // CSS Images 4 introduces conic gradients with Oklab as their default
        // interpolation space, including when every stop is a legacy color.
        var stops = CssGradientParser.ParseStops(ref args,
            interpolation ?? new CssColorParser.CssInterpolationMethod(CssColorParser.CssInterpolationSpace.Oklab),
            context, 1, angular: true);
        if (stops is null) return null;
        if (repeating)
        {
            // The angular cycle's physical resolution is governed by its arc
            // length at the farthest painted pixel. This also bounds the native
            // stop expansion for very short repeating periods.
            var circumference = Math.PI * Math.Max(width, height);
            stops = CssGradientParser.RepeatStops(stops, circumference, 1);
        }
        return new ImageBrush(Rasterize(width, height, center, fromAngle, stops))
        {
            Stretch = Stretch.Fill,
        };
    }

    private static BitmapImage Rasterize(double width, double height, Point center,
        double fromAngle, GradientStopCollection stops)
    {
        // A CSS image has no natural size. The paint box determines its raster;
        // cap residency while preserving its aspect ratio for large surfaces.
        var scale = Math.Min(1, Math.Min(2048 / width, 2048 / height));
        var pixelWidth = (int)Math.Max(1, Math.Ceiling(width * scale));
        var pixelHeight = (int)Math.Max(1, Math.Ceiling(height * scale));
        var pixels = new byte[checked(pixelWidth * pixelHeight * 4)];
        // Every ray with the same angle has the same color. Resolve the CSS
        // interpolation once into a subpixel angular ramp, then raster rows
        // with only atan2 and a table lookup.
        var rampLength = pixelWidth * pixelHeight <= 4 ? 0 :
            Math.Clamp(4 * (pixelWidth + pixelHeight), 256, 16384);
        var ramp = new Color[rampLength];
        for (var i = 0; i < ramp.Length; i++)
            ramp[i] = Sample(stops, (double)i / rampLength);
        var at = 0;
        for (var y = 0; y < pixelHeight; y++)
        {
            var dy = ((y + .5) / pixelHeight - center.Y) * height;
            for (var x = 0; x < pixelWidth; x++)
            {
                var dx = ((x + .5) / pixelWidth - center.X) * width;
                var angle = dx == 0 && dy == 0 ? 0 : Math.Atan2(dx, -dy) * 180 / Math.PI;
                var turn = ((angle - fromAngle) % 360 + 360) % 360 / 360;
                var color = rampLength == 0 ? Sample(stops, turn) :
                    ramp[Math.Min((int)(turn * rampLength), rampLength - 1)];
                pixels[at++] = color.B;
                pixels[at++] = color.G;
                pixels[at++] = color.R;
                pixels[at++] = color.A;
            }
        }
        // Publish an immutable source. A WriteableBitmap would force whole-frame
        // recording to fall back to direct drawing even though this raster never
        // changes after construction.
        return BitmapImage.FromPixels(pixels, pixelWidth, pixelHeight, pixelWidth * 4);
    }

    private static Color Sample(GradientStopCollection stops, double position)
    {
        var low = 0;
        var high = stops.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (stops[middle].Offset <= position) low = middle + 1;
            else high = middle;
        }
        if (low == 0) return stops[0].Color;
        if (low == stops.Count) return stops[^1].Color;
        var before = stops[low - 1];
        var after = stops[low];
        if (after.Offset <= before.Offset) return after.Color;
        var progress = (float)((position - before.Offset) / (after.Offset - before.Offset));
        var remaining = 1 - progress;
        var firstAlpha = before.Color.ScA;
        var secondAlpha = after.Color.ScA;
        var alpha = firstAlpha * remaining + secondAlpha * progress;
        if (alpha <= 0) return Color.FromArgb(0, 0, 0, 0);
        return Color.FromScRgb(alpha,
            (before.Color.ScR * firstAlpha * remaining + after.Color.ScR * secondAlpha * progress) / alpha,
            (before.Color.ScG * firstAlpha * remaining + after.Color.ScG * secondAlpha * progress) / alpha,
            (before.Color.ScB * firstAlpha * remaining + after.Color.ScB * secondAlpha * progress) / alpha);
    }
}
