namespace Jalium.UI.Media;

/// <summary>Immutable stop samples shared by the two CPU drawing paths.</summary>
internal sealed class GradientColorRamp
{
    private readonly double[] _offsets;
    private readonly (double R, double G, double B, double A)[] _colors;
    private readonly GradientSpreadMethod _spread;
    private readonly bool _linearLight;
    private readonly (double R, double G, double B, double A) _average;

    private GradientColorRamp(GradientBrush brush)
    {
        _spread = brush.SpreadMethod;
        _linearLight = brush.ColorInterpolationMode == ColorInterpolationMode.ScRgbLinearInterpolation;
        var stops = brush.GradientStops.OrderBy(static stop => stop.Offset).ToArray();
        _offsets = new double[stops.Length];
        _colors = new (double, double, double, double)[stops.Length];
        for (var i = 0; i < stops.Length; i++)
        {
            _offsets[i] = stops[i].Offset;
            var color = stops[i].Color;
            _colors[i] = _linearLight ? (color.ScR, color.ScG, color.ScB, color.ScA)
                : (color.R / 255d, color.G / 255d, color.B / 255d, color.ScA);
        }
        // Integrate each piece of the unencoded ramp over [0,1]. Midpoint
        // quadrature is exact for these linear segments, including hard stops.
        double start = 0, red = 0, green = 0, blue = 0, alpha = 0;
        foreach (var end in _offsets.Where(static t => t > 0 && t < 1).Distinct().Append(1d))
        {
            var sample = Interpolate((start + end) / 2); var width = end - start;
            red += sample.R * width; green += sample.G * width;
            blue += sample.B * width; alpha += sample.A * width; start = end;
        }
        _average = (red, green, blue, alpha);
    }

    internal static GradientColorRamp? Build(GradientBrush brush) =>
        brush.GradientStops.Count == 0 ? null : new(brush);

    internal (double R, double G, double B, double A) Evaluate(double t, double opacity)
    {
        t = _spread switch
        {
            GradientSpreadMethod.Repeat => t - Math.Floor(t),
            GradientSpreadMethod.Reflect => Reflect(t),
            _ => Math.Clamp(t, 0, 1)
        };
        if (double.IsNaN(t)) return default;
        return Encode(Interpolate(t), opacity);
    }

    internal (double R, double G, double B, double A) EvaluateRadial(double t, double opacity) =>
        double.IsPositiveInfinity(t) && _spread != GradientSpreadMethod.Pad
            ? Encode(_average, opacity) : Evaluate(t, opacity);

    private (double R, double G, double B, double A) Interpolate(double t)
    {
        // Upper bound selects the last stop at a coincident position, including
        // hard transitions at the first offset. Equal offsets retain source order.
        var lo = 0; var hi = _offsets.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_offsets[mid] <= t) lo = mid + 1; else hi = mid;
        }
        var left = Math.Max(0, lo - 1); var right = Math.Min(lo, _offsets.Length - 1);
        var span = _offsets[right] - _offsets[left];
        var fraction = span > 0 ? Math.Clamp((t - _offsets[left]) / span, 0, 1) : 0;
        var a = _colors[left]; var b = _colors[right];
        return (a.R + (b.R - a.R) * fraction, a.G + (b.G - a.G) * fraction,
            a.B + (b.B - a.B) * fraction, a.A + (b.A - a.A) * fraction);
    }

    private (double R, double G, double B, double A) Encode((double R, double G, double B, double A) color, double opacity)
    {
        double Channel(double value) => Math.Clamp(_linearLight ? EncodeLinear(value) : value, 0, 1);
        return (Channel(color.R), Channel(color.G), Channel(color.B), Math.Clamp(color.A * opacity, 0, 1));
    }

    internal static double EncodeLinear(double value) => value <= .0031308
        ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055;

    internal static byte ToByte(double value) => (byte)Math.Clamp(value * 255 + .5, 0, 255);

    private static double Reflect(double value)
    {
        var wrapped = Math.Abs(value) % 2;
        return wrapped > 1 ? 2 - wrapped : wrapped;
    }
}
