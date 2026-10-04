using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssColorParser
{
    internal enum CssInterpolationSpace
    {
        Srgb, SrgbLinear, DisplayP3, DisplayP3Linear, A98Rgb, ProPhotoRgb, Rec2020,
        XyzD65, XyzD50, Lab, Lch, Oklab, Oklch, Hsl, Hwb,
    }
    internal enum CssHueMethod { Shorter, Longer, Increasing, Decreasing }
    internal readonly record struct CssInterpolationMethod(CssInterpolationSpace Space,
        CssHueMethod Hue = CssHueMethod.Shorter);

    internal static Color Interpolate(CssColorData from, CssColorData to,
        CssInterpolationMethod method, double progress)
        => InterpolateData(from, to, method, progress).Rendered;

    /// <summary>Interpolates without discarding the source space before a later mix.</summary>
    internal static CssColorData InterpolateData(CssColorData from, CssColorData to,
        CssInterpolationMethod method, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);

        var first = ToInterpolationSpace(from, method.Space);
        var second = ToInterpolationSpace(to, method.Space);
        var firstMissing = MissingForSpace(from, method.Space, first);
        var secondMissing = MissingForSpace(to, method.Space, second);
        CarryMissing(ref first, ref second, firstMissing, secondMissing);

        var polar = method.Space is CssInterpolationSpace.Hsl or CssInterpolationSpace.Hwb or
            CssInterpolationSpace.Lch or CssInterpolationSpace.Oklch;
        if (polar)
        {
            if (method.Space is CssInterpolationSpace.Hsl or CssInterpolationSpace.Hwb)
            {
                var (h1, h2) = AdjustHue(first.X, second.X, method.Hue);
                first = new(h1, first.Y, first.Z);
                second = new(h2, second.Y, second.Z);
            }
            else
            {
                var (h1, h2) = AdjustHue(first.Z, second.Z, method.Hue);
                first = new(first.X, first.Y, h1);
                second = new(second.X, second.Y, h2);
            }
        }

        var firstAlpha = (from.Missing & 8) != 0 && (to.Missing & 8) == 0 ? to.Alpha : from.Alpha;
        var secondAlpha = (to.Missing & 8) != 0 && (from.Missing & 8) == 0 ? from.Alpha : to.Alpha;
        var oneMinus = 1 - progress;
        var alpha = firstAlpha * oneMinus + secondAlpha * progress;
        var mixed = alpha > 1e-8
            ? new Components(
                (first.X * firstAlpha * oneMinus + second.X * secondAlpha * progress) / alpha,
                (first.Y * firstAlpha * oneMinus + second.Y * secondAlpha * progress) / alpha,
                (first.Z * firstAlpha * oneMinus + second.Z * secondAlpha * progress) / alpha)
            : second;
        if (polar)
        {
            var hue = method.Space is CssInterpolationSpace.Hsl or CssInterpolationSpace.Hwb
                ? first.X * oneMinus + second.X * progress
                : first.Z * oneMinus + second.Z * progress;
            mixed = method.Space is CssInterpolationSpace.Hsl or CssInterpolationSpace.Hwb
                ? new(hue, mixed.Y, mixed.Z) : new(mixed.X, mixed.Y, hue);
        }
        var linear = FromInterpolationSpace(mixed, method.Space);
        var mapped = MapToSrgb(linear);
        var rendered = Color.FromScRgb((float)alpha, (float)mapped.X, (float)mapped.Y, (float)mapped.Z);
        var missing = (byte)((firstMissing & secondMissing) |
            ((from.Missing & to.Missing) & 8));
        return new CssColorData(rendered, InterpolationSpaceName(method.Space),
            mixed.X, mixed.Y, mixed.Z, alpha, missing, false);
    }

    private static string InterpolationSpaceName(CssInterpolationSpace space) => space switch
    {
        CssInterpolationSpace.Srgb => "srgb",
        CssInterpolationSpace.SrgbLinear => "srgb-linear",
        CssInterpolationSpace.DisplayP3 => "display-p3",
        CssInterpolationSpace.DisplayP3Linear => "display-p3-linear",
        CssInterpolationSpace.A98Rgb => "a98-rgb",
        CssInterpolationSpace.ProPhotoRgb => "prophoto-rgb",
        CssInterpolationSpace.Rec2020 => "rec2020",
        CssInterpolationSpace.XyzD65 => "xyz-d65",
        CssInterpolationSpace.XyzD50 => "xyz-d50",
        CssInterpolationSpace.Lab => "lab",
        CssInterpolationSpace.Lch => "lch",
        CssInterpolationSpace.Oklab => "oklab",
        CssInterpolationSpace.Oklch => "oklch",
        CssInterpolationSpace.Hsl => "hsl",
        CssInterpolationSpace.Hwb => "hwb",
        _ => "srgb",
    };

    private static Components ToLinearSrgb(CssColorData value)
    {
        var source = new Components(value.X, value.Y, value.Z);
        switch (value.Space)
        {
            case "hwb":
                return HwbToSrgb(value.X, value.Y / 100, value.Z / 100).Map(DecodeSrgb);
            case "hsl":
                return HslToSrgb(value.X, value.Y / 100, value.Z / 100).Map(DecodeSrgb);
            case "lab":
                return XyzD65ToLinearSrgb(D50ToD65(LabToXyzD50(source)));
            case "lch":
                return XyzD65ToLinearSrgb(D50ToD65(LabToXyzD50(PolarToRectangular(source))));
            case "oklab":
                return XyzD65ToLinearSrgb(OklabToXyzD65(source));
            case "oklch":
                return XyzD65ToLinearSrgb(OklabToXyzD65(PolarToRectangular(source)));
            default:
                return TryConvertPredefined(value.Space, source, out var linear) ? linear : default;
        }
    }

    private static Components PolarToRectangular(Components value)
    {
        var radians = value.Z * Math.PI / 180;
        return new(value.X, value.Y * Math.Cos(radians), value.Y * Math.Sin(radians));
    }

    private static Components HslToSrgb(double hue, double saturation, double lightness)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        var h = ((hue % 360) + 360) % 360 / 60;
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var m = lightness - chroma / 2;
        return h < 1 ? new(chroma + m, x + m, m) :
            h < 2 ? new(x + m, chroma + m, m) :
            h < 3 ? new(m, chroma + m, x + m) :
            h < 4 ? new(m, x + m, chroma + m) :
            h < 5 ? new(x + m, m, chroma + m) : new(chroma + m, m, x + m);
    }

    private static double EncodeSrgb(double value)
    {
        var magnitude = Math.Abs(value);
        return magnitude <= 0.0031308 ? value * 12.92 :
            Math.CopySign(1.055 * Math.Pow(magnitude, 1 / 2.4) - 0.055, value);
    }

    private static void CarryMissing(ref Components first, ref Components second, byte firstMask, byte secondMask)
    {
        var x1 = (firstMask & 1) != 0 ? second.X : first.X;
        var y1 = (firstMask & 2) != 0 ? second.Y : first.Y;
        var z1 = (firstMask & 4) != 0 ? second.Z : first.Z;
        var x2 = (secondMask & 1) != 0 ? first.X : second.X;
        var y2 = (secondMask & 2) != 0 ? first.Y : second.Y;
        var z2 = (secondMask & 4) != 0 ? first.Z : second.Z;
        first = new(x1, y1, z1);
        second = new(x2, y2, z2);
    }
}
