namespace Jalium.UI.Styling;

internal static partial class CssColorParser
{
    private enum ComponentCategory : byte
    {
        None, Red, Green, Blue, Lightness, Colorfulness, Hue, OpponentA, OpponentB,
    }

    private static CssInterpolationSpace SourceSpace(string name) => name switch
    {
        "srgb" => CssInterpolationSpace.Srgb,
        "srgb-linear" => CssInterpolationSpace.SrgbLinear,
        "display-p3" => CssInterpolationSpace.DisplayP3,
        "display-p3-linear" => CssInterpolationSpace.DisplayP3Linear,
        "a98-rgb" => CssInterpolationSpace.A98Rgb,
        "prophoto-rgb" => CssInterpolationSpace.ProPhotoRgb,
        "rec2020" => CssInterpolationSpace.Rec2020,
        "xyz" or "xyz-d65" => CssInterpolationSpace.XyzD65,
        "xyz-d50" => CssInterpolationSpace.XyzD50,
        "lab" => CssInterpolationSpace.Lab,
        "lch" => CssInterpolationSpace.Lch,
        "oklab" => CssInterpolationSpace.Oklab,
        "oklch" => CssInterpolationSpace.Oklch,
        "hsl" => CssInterpolationSpace.Hsl,
        "hwb" => CssInterpolationSpace.Hwb,
        _ => CssInterpolationSpace.Srgb,
    };

    private static Components ToInterpolationSpace(CssColorData value, CssInterpolationSpace space)
    {
        if (SourceSpace(value.Space) == space) return new(value.X, value.Y, value.Z);
        var linear = ToLinearSrgb(value);
        var xyz = LinearSrgbToXyzD65(linear);
        return space switch
        {
            CssInterpolationSpace.Srgb => linear.Map(EncodeSrgb),
            CssInterpolationSpace.SrgbLinear => linear,
            CssInterpolationSpace.DisplayP3 => XyzD65ToLinearP3(xyz).Map(EncodeSrgb),
            CssInterpolationSpace.DisplayP3Linear => XyzD65ToLinearP3(xyz),
            CssInterpolationSpace.A98Rgb => XyzD65ToLinearA98(xyz).Map(static c => SignedPower(c, 256.0 / 563)),
            CssInterpolationSpace.ProPhotoRgb => XyzD50ToLinearProPhoto(D65ToD50(xyz)).Map(EncodeProPhoto),
            CssInterpolationSpace.Rec2020 => XyzD65ToLinearRec2020(xyz).Map(static c => SignedPower(c, 1.0 / 2.4)),
            CssInterpolationSpace.XyzD65 => xyz,
            CssInterpolationSpace.XyzD50 => D65ToD50(xyz),
            CssInterpolationSpace.Lab => XyzD50ToLab(D65ToD50(xyz)),
            CssInterpolationSpace.Lch => RectangularToPolar(XyzD50ToLab(D65ToD50(xyz))),
            CssInterpolationSpace.Oklab => XyzD65ToOklab(xyz),
            CssInterpolationSpace.Oklch => RectangularToPolar(XyzD65ToOklab(xyz)),
            CssInterpolationSpace.Hsl => SrgbToHsl(linear.Map(EncodeSrgb)),
            CssInterpolationSpace.Hwb => SrgbToHwb(linear.Map(EncodeSrgb)),
            _ => default,
        };
    }

    private static Components FromInterpolationSpace(Components value, CssInterpolationSpace space)
    {
        if (space == CssInterpolationSpace.Oklab)
            return XyzD65ToLinearSrgb(OklabToXyzD65(value));
        if (space == CssInterpolationSpace.Oklch)
            return XyzD65ToLinearSrgb(OklabToXyzD65(PolarToRectangular(value)));
        if (space == CssInterpolationSpace.Lab)
            return XyzD65ToLinearSrgb(D50ToD65(LabToXyzD50(value)));
        if (space == CssInterpolationSpace.Lch)
            return XyzD65ToLinearSrgb(D50ToD65(LabToXyzD50(PolarToRectangular(value))));
        if (space == CssInterpolationSpace.Hsl)
            return HslToSrgb(value.X, value.Y / 100, value.Z / 100).Map(DecodeSrgb);
        if (space == CssInterpolationSpace.Hwb)
            return HwbToSrgb(value.X, value.Y / 100, value.Z / 100).Map(DecodeSrgb);

        var name = space switch
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
            _ => "srgb",
        };
        return TryConvertPredefined(name, value, out var linear) ? linear : default;
    }

    private static byte MissingForSpace(CssColorData color, CssInterpolationSpace space, Components converted)
    {
        var source = SourceSpace(color.Space);
        var missing = (byte)(color.Missing & 7);
        byte result;
        if (source == space) result = missing;
        else
        {
            result = 0;
            byte sourceMatched = 0, targetMatched = 0;
            for (var i = 0; i < 3; i++)
            {
                var category = Category(source, i);
                if (category == ComponentCategory.None) continue;
                for (var j = 0; j < 3; j++)
                {
                    if (Category(space, j) != category) continue;
                    sourceMatched |= (byte)(1 << i);
                    targetMatched |= (byte)(1 << j);
                    if ((missing & (1 << i)) != 0) result |= (byte)(1 << j);
                    break;
                }
            }
            var sourceRemainder = (byte)(7 & ~sourceMatched);
            if (sourceRemainder != 0 && (missing & sourceRemainder) == sourceRemainder)
                result |= (byte)(7 & ~targetMatched);
        }

        // CSS Color 4 makes a near-neutral polar hue powerless (and therefore
        // missing) before carrying missing components across the two stops.
        if (space switch
            {
                CssInterpolationSpace.Hsl => converted.Y <= .001,
                CssInterpolationSpace.Hwb => converted.Y + converted.Z >= 99.999,
                CssInterpolationSpace.Lch => converted.Y <= .0015,
                CssInterpolationSpace.Oklch => converted.Y <= .000004,
                _ => false,
            })
            result |= (byte)(space is CssInterpolationSpace.Hsl or CssInterpolationSpace.Hwb ? 1 : 4);
        return result;
    }

    private static ComponentCategory Category(CssInterpolationSpace space, int channel) => space switch
    {
        CssInterpolationSpace.Srgb or CssInterpolationSpace.SrgbLinear or CssInterpolationSpace.DisplayP3 or
        CssInterpolationSpace.DisplayP3Linear or CssInterpolationSpace.A98Rgb or CssInterpolationSpace.ProPhotoRgb or
        CssInterpolationSpace.Rec2020 or CssInterpolationSpace.XyzD65 or CssInterpolationSpace.XyzD50 =>
            channel switch { 0 => ComponentCategory.Red, 1 => ComponentCategory.Green, _ => ComponentCategory.Blue },
        CssInterpolationSpace.Lab or CssInterpolationSpace.Oklab => channel switch
        {
            0 => ComponentCategory.Lightness, 1 => ComponentCategory.OpponentA, _ => ComponentCategory.OpponentB,
        },
        CssInterpolationSpace.Lch or CssInterpolationSpace.Oklch => channel switch
        {
            0 => ComponentCategory.Lightness, 1 => ComponentCategory.Colorfulness, _ => ComponentCategory.Hue,
        },
        CssInterpolationSpace.Hsl => channel switch
        {
            0 => ComponentCategory.Hue, 1 => ComponentCategory.Colorfulness, _ => ComponentCategory.Lightness,
        },
        CssInterpolationSpace.Hwb => channel == 0 ? ComponentCategory.Hue : ComponentCategory.None,
        _ => ComponentCategory.None,
    };

    private static (double First, double Second) AdjustHue(double first, double second, CssHueMethod method)
    {
        first = ((first % 360) + 360) % 360;
        second = ((second % 360) + 360) % 360;
        var delta = second - first;
        switch (method)
        {
            case CssHueMethod.Shorter:
                if (delta > 180) first += 360;
                else if (delta < -180) second += 360;
                break;
            case CssHueMethod.Longer:
                if (delta is > 0 and < 180) first += 360;
                else if (delta is > -180 and <= 0) second += 360;
                break;
            case CssHueMethod.Increasing:
                if (second < first) second += 360;
                break;
            case CssHueMethod.Decreasing:
                if (first < second) first += 360;
                break;
        }
        return (first, second);
    }

    private static Components RectangularToPolar(Components value)
    {
        var chroma = Math.Sqrt(value.Y * value.Y + value.Z * value.Z);
        var hue = (Math.Atan2(value.Z, value.Y) * 180 / Math.PI + 360) % 360;
        return new(value.X, chroma, hue);
    }

    private static Components D65ToD50(Components xyz) => Multiply(xyz,
        1.0479297925449969, 0.022946870601609652, -0.05019226628920524,
        0.02962780877005599, 0.9904344267538799, -0.017073799063418826,
        -0.009243040646204504, 0.015055191490298152, 0.7518742814281371);

    private static Components XyzD50ToLab(Components xyz)
    {
        const double epsilon = 216.0 / 24389, kappa = 24389.0 / 27;
        static double F(double v) => v > epsilon ? Math.Cbrt(v) : (kappa * v + 16) / 116;
        var fx = F(xyz.X / (0.3457 / 0.3585));
        var fy = F(xyz.Y);
        var fz = F(xyz.Z / ((1 - 0.3457 - 0.3585) / 0.3585));
        return new(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static Components XyzD65ToLinearP3(Components xyz) => Multiply(xyz,
        446124.0 / 178915, -333277.0 / 357830, -72051.0 / 178915,
        -14852.0 / 17905, 63121.0 / 35810, 423.0 / 17905,
        11844.0 / 330415, -50337.0 / 660830, 316169.0 / 330415);

    private static Components XyzD65ToLinearA98(Components xyz) => Multiply(xyz,
        1829569.0 / 896150, -506331.0 / 896150, -308931.0 / 896150,
        -851781.0 / 878810, 1648619.0 / 878810, 36519.0 / 878810,
        16779.0 / 1248040, -147721.0 / 1248040, 1266979.0 / 1248040);

    private static Components XyzD50ToLinearProPhoto(Components xyz) => Multiply(xyz,
        1.34578688164715830, -0.25557208737979464, -0.05110186497554526,
        -0.54463070512490190, 1.50824774284514680, 0.02052744743642139,
        0, 0, 1.21196754563894520);

    private static double EncodeProPhoto(double value) => Math.Abs(value) >= 1.0 / 512
        ? SignedPower(value, 1.0 / 1.8) : 16 * value;

    private static Components XyzD65ToLinearRec2020(Components xyz) => Multiply(xyz,
        30757411.0 / 17917100, -6372589.0 / 17917100, -4539589.0 / 17917100,
        -19765991.0 / 29648200, 47925759.0 / 29648200, 467509.0 / 29648200,
        792561.0 / 44930125, -1921689.0 / 44930125, 42328811.0 / 44930125);

    private static Components SrgbToHsl(Components rgb)
    {
        var max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        var min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        var light = (max + min) / 2;
        var delta = max - min;
        if (delta == 0) return new(0, 0, light * 100);
        var saturation = light is 0 or 1 ? 0 : (max - light) / Math.Min(light, 1 - light);
        var hue = SrgbHue(rgb, max, delta);
        if (saturation < 0) { hue += 180; saturation = -saturation; }
        return new((hue + 360) % 360, saturation * 100, light * 100);
    }

    private static Components SrgbToHwb(Components rgb)
    {
        var max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        var min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        var hue = max == min ? 0 : SrgbHue(rgb, max, max - min);
        return new(hue, min * 100, (1 - max) * 100);
    }

    private static double SrgbHue(Components rgb, double max, double delta)
    {
        var hue = max == rgb.X ? (rgb.Y - rgb.Z) / delta + (rgb.Y < rgb.Z ? 6 : 0) :
            max == rgb.Y ? (rgb.Z - rgb.X) / delta + 2 : (rgb.X - rgb.Y) / delta + 4;
        return (hue * 60 + 360) % 360;
    }
}
