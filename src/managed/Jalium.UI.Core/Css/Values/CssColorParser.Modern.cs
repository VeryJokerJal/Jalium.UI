using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssColorParser
{
    // CSS Color 4 absolute colors. Native brushes render into sRGB today, so
    // standalone colors are mapped for painting while the source channels survive
    // separately for CSS gradient interpolation.
    // See https://www.w3.org/TR/css-color-4/#color-conversion-code.
    private readonly record struct Components(double X, double Y, double Z)
    {
        internal bool Finite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
        internal Components Clip() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, 0, 1), Math.Clamp(Z, 0, 1));
        internal bool InGamut => X is >= 0 and <= 1 && Y is >= 0 and <= 1 && Z is >= 0 and <= 1;
    }

    private static bool TryParseModern(ReadOnlySpan<char> name, ref CssTokenReader args,
        out Color color, out CssColorData data)
    {
        color = default;
        data = default;
        var kind = name.ToString().ToLowerInvariant();
        Components linear;
        Components source;
        double alpha;
        byte missing;
        switch (kind)
        {
            case "hwb":
                if (!TryReadHue(ref args, out var hue, out var hueMissing) ||
                    !TryReadSeparatedComponent(ref args, 100, out var white, out var whiteMissing) ||
                    !TryReadSeparatedComponent(ref args, 100, out var black, out var blackMissing) ||
                    !TryReadModernAlpha(ref args, out alpha, out var alphaMissing)) return false;
                source = new(hue, white, black);
                missing = MissingBits(hueMissing, whiteMissing, blackMissing, alphaMissing);
                var encoded = HwbToSrgb(hue, white / 100, black / 100);
                linear = encoded.Map(DecodeSrgb);
                break;
            case "lab": case "lch": case "oklab": case "oklch":
                if (!TryParseLabLike(kind, ref args, out linear, out source, out alpha, out missing)) return false;
                break;
            case "color":
                if (!TryParsePredefined(ref args, out kind, out linear, out source, out alpha, out missing)) return false;
                break;
            default:
                return false;
        }

        if (!linear.Finite) return false;
        var mapped = MapToSrgb(linear);
        color = Color.FromScRgb((float)alpha, (float)mapped.X, (float)mapped.Y, (float)mapped.Z);
        data = new(color, kind, source.X, source.Y, source.Z, alpha, missing, kind == "hwb");
        return true;
    }

    private static bool TryParseLabLike(string kind, ref CssTokenReader args,
        out Components linear, out Components source, out double alpha, out byte missing)
    {
        linear = default;
        source = default;
        alpha = 1;
        missing = 0;
        var isOk = kind is "oklab" or "oklch";
        var polar = kind is "lch" or "oklch";
        var lightnessScale = isOk ? 1.0 : 100.0;
        var secondScale = isOk ? 0.4 : polar ? 150.0 : 125.0;
        if (!TryReadComponent(ref args, lightnessScale, out var lightness, out var lightnessMissing) ||
            !TryReadSeparatedComponent(ref args, secondScale, out var second, out var secondMissing)) return false;
        double third;
        bool thirdMissing;
        if (polar)
        {
            if (!HasSeparator(ref args) || !TryReadHue(ref args, out third, out thirdMissing)) return false;
            source = new(Math.Clamp(lightness, 0, lightnessScale), Math.Max(0, second), third);
            second = Math.Max(0, second);
            var angle = third * Math.PI / 180;
            third = second * Math.Sin(angle);
            second *= Math.Cos(angle);
        }
        else if (!TryReadSeparatedComponent(ref args, isOk ? 0.4 : 125, out third, out thirdMissing)) return false;
        if (!TryReadModernAlpha(ref args, out alpha, out var alphaMissing)) return false;

        lightness = Math.Clamp(lightness, 0, lightnessScale);
        if (!polar) source = new(lightness, second, third);
        missing = MissingBits(lightnessMissing, secondMissing, thirdMissing, alphaMissing);
        var lab = new Components(lightness, second, third);
        var xyz = isOk ? OklabToXyzD65(lab) : D50ToD65(LabToXyzD50(lab));
        linear = XyzD65ToLinearSrgb(xyz);
        return true;
    }

    private static bool TryParsePredefined(ref CssTokenReader args, out string space,
        out Components linear, out Components source, out double alpha, out byte missing)
    {
        linear = default;
        source = default;
        alpha = 1;
        missing = 0;
        space = string.Empty;
        if (!args.TryReadIdent(out var ident)) return false;
        space = ident.ToString().ToLowerInvariant();
        if (!TryReadSeparatedComponent(ref args, 1, out var x, out var xMissing) ||
            !TryReadSeparatedComponent(ref args, 1, out var y, out var yMissing) ||
            !TryReadSeparatedComponent(ref args, 1, out var z, out var zMissing) ||
            !TryReadModernAlpha(ref args, out alpha, out var alphaMissing)) return false;
        var components = new Components(x, y, z);
        source = components;
        missing = MissingBits(xMissing, yMissing, zMissing, alphaMissing);
        return TryConvertPredefined(space, components, out linear);
    }

    private static bool TryConvertPredefined(string space, Components components, out Components linear)
    {
        linear = default;
        Components xyz;
        switch (space)
        {
            case "srgb":
                linear = components.Map(DecodeSrgb); return true;
            case "srgb-linear":
                linear = components; return true;
            case "display-p3":
                xyz = LinearP3ToXyzD65(components.Map(DecodeSrgb)); break;
            case "display-p3-linear":
                xyz = LinearP3ToXyzD65(components); break;
            case "a98-rgb":
                xyz = LinearA98ToXyzD65(components.Map(static c => SignedPower(c, 563.0 / 256))); break;
            case "prophoto-rgb":
                xyz = D50ToD65(LinearProPhotoToXyzD50(components.Map(static c =>
                    Math.Abs(c) <= 16.0 / 512 ? c / 16 : SignedPower(c, 1.8)))); break;
            case "rec2020":
                xyz = LinearRec2020ToXyzD65(components.Map(static c => SignedPower(c, 2.4))); break;
            case "xyz": case "xyz-d65":
                xyz = components; break;
            case "xyz-d50":
                xyz = D50ToD65(components); break;
            default:
                return false;
        }
        linear = XyzD65ToLinearSrgb(xyz);
        return true;
    }

    private static byte MissingBits(bool first, bool second, bool third, bool alpha) =>
        (byte)((first ? 1 : 0) | (second ? 2 : 0) | (third ? 4 : 0) | (alpha ? 8 : 0));

    private static bool TryReadComponent(ref CssTokenReader args, double percentScale, out double value)
        => TryReadComponent(ref args, percentScale, out value, out _);

    private static bool TryReadComponent(ref CssTokenReader args, double percentScale, out double value, out bool missing)
    {
        missing = false;
        var probe = args;
        if (probe.TryReadIdent(out var ident) && ident.Equals("none", StringComparison.OrdinalIgnoreCase))
        { args = probe; value = 0; missing = true; return true; }
        if (!args.TryReadNumber(out value, out var unit) || !double.IsFinite(value)) return false;
        if (unit == CssUnit.Percent) value *= percentScale / 100;
        else if (unit != CssUnit.None) return false;
        return double.IsFinite(value);
    }

    private static bool HasSeparator(ref CssTokenReader args)
    {
        var position = args.Position;
        args.SkipWhitespace();
        return args.Position > position;
    }

    private static bool TryReadSeparatedComponent(ref CssTokenReader args, double percentScale, out double value)
        => TryReadSeparatedComponent(ref args, percentScale, out value, out _);

    private static bool TryReadSeparatedComponent(ref CssTokenReader args, double percentScale,
        out double value, out bool missing)
    {
        if (HasSeparator(ref args)) return TryReadComponent(ref args, percentScale, out value, out missing);
        value = 0; missing = false;
        return false;
    }

    private static bool TryReadHue(ref CssTokenReader args, out double hue)
        => TryReadHue(ref args, out hue, out _);

    private static bool TryReadHue(ref CssTokenReader args, out double hue, out bool missing)
    {
        missing = false;
        var probe = args;
        if (probe.TryReadIdent(out var ident) && ident.Equals("none", StringComparison.OrdinalIgnoreCase))
        { args = probe; hue = 0; missing = true; return true; }
        hue = 0;
        return args.TryReadNumber(out var value, out var unit) &&
            CssUnitConversion.TryToDegrees(value, unit, out hue) && double.IsFinite(hue);
    }

    private static bool TryReadModernAlpha(ref CssTokenReader args, out double alpha)
        => TryReadModernAlpha(ref args, out alpha, out _);

    private static bool TryReadModernAlpha(ref CssTokenReader args, out double alpha, out bool missing)
    {
        alpha = 1; missing = false;
        if (args.TryReadSlash() && !TryReadComponent(ref args, 1, out alpha, out missing)) return false;
        alpha = Math.Clamp(alpha, 0, 1);
        return args.AtEnd;
    }

    private static Components HwbToSrgb(double hue, double white, double black)
    {
        if (white + black >= 1)
        {
            var gray = white / (white + black);
            return new(gray, gray, gray);
        }
        var h = ((hue % 360) + 360) % 360 / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        var baseColor = h < 1 ? new Components(1, x, 0) :
            h < 2 ? new Components(x, 1, 0) :
            h < 3 ? new Components(0, 1, x) :
            h < 4 ? new Components(0, x, 1) :
            h < 5 ? new Components(x, 0, 1) : new Components(1, 0, x);
        var chroma = 1 - white - black;
        return new(baseColor.X * chroma + white, baseColor.Y * chroma + white, baseColor.Z * chroma + white);
    }

    private static double DecodeSrgb(double c)
    {
        var magnitude = Math.Abs(c);
        return magnitude <= 0.04045 ? c / 12.92 : Math.CopySign(Math.Pow((magnitude + 0.055) / 1.055, 2.4), c);
    }

    private static double SignedPower(double c, double exponent) => Math.CopySign(Math.Pow(Math.Abs(c), exponent), c);
    private static Components Map(this Components value, Func<double, double> map) => new(map(value.X), map(value.Y), map(value.Z));

    private static Components LabToXyzD50(Components lab)
    {
        const double epsilon = 216.0 / 24389, kappa = 24389.0 / 27;
        var fy = (lab.X + 16) / 116;
        var fx = fy + lab.Y / 500;
        var fz = fy - lab.Z / 200;
        static double Inverse(double f) => f * f * f > epsilon ? f * f * f : (116 * f - 16) / kappa;
        var xWhite = 0.3457 / 0.3585;
        var zWhite = (1 - 0.3457 - 0.3585) / 0.3585;
        return new(Inverse(fx) * xWhite, lab.X > kappa * epsilon ? fy * fy * fy : lab.X / kappa,
            Inverse(fz) * zWhite);
    }

    private static Components OklabToXyzD65(Components lab)
    {
        var l = lab.X + 0.3963377773761749 * lab.Y + 0.2158037573099136 * lab.Z;
        var m = lab.X - 0.1055613458156586 * lab.Y - 0.0638541728258133 * lab.Z;
        var s = lab.X - 0.0894841775298119 * lab.Y - 1.2914855480194092 * lab.Z;
        return Multiply(new(l * l * l, m * m * m, s * s * s),
            1.2268798758459243, -0.5578149944602171, 0.2813910456659647,
            -0.0405757452148008, 1.1122868032803170, -0.0717110580655164,
            -0.0763729366746601, -0.4214933324022432, 1.5869240198367816);
    }

    private static Components XyzD65ToOklab(Components xyz)
    {
        var lms = Multiply(xyz,
            0.8190224379967030, 0.3619062600528904, -0.1288737815209879,
            0.0329836539323885, 0.9292868615863434, 0.0361446663506424,
            0.0481771893596242, 0.2642395317527308, 0.6335478284694309).Map(Math.Cbrt);
        return Multiply(lms,
            0.2104542683093140, 0.7936177747023054, -0.0040720430116193,
            1.9779985324311684, -2.4285922420485799, 0.4505937096174110,
            0.0259040424655478, 0.7827717124575296, -0.8086757549230774);
    }

    private static Components LinearSrgbToXyzD65(Components rgb) => Multiply(rgb,
        506752.0 / 1228815, 87881.0 / 245763, 12673.0 / 70218,
        87098.0 / 409605, 175762.0 / 245763, 12673.0 / 175545,
        7918.0 / 409605, 87881.0 / 737289, 1001167.0 / 1053270);

    private static Components XyzD65ToLinearSrgb(Components xyz) => Multiply(xyz,
        12831.0 / 3959, -329.0 / 214, -1974.0 / 3959,
        -851781.0 / 878810, 1648619.0 / 878810, 36519.0 / 878810,
        705.0 / 12673, -2585.0 / 12673, 705.0 / 667);

    private static Components D50ToD65(Components xyz) => Multiply(xyz,
        0.955473421488075, -0.02309845494876471, 0.06325924320057072,
        -0.0283697093338637, 1.0099953980813041, 0.021041441191917323,
        0.012314014864481998, -0.020507649298898964, 1.330365926242124);

    private static Components LinearP3ToXyzD65(Components rgb) => Multiply(rgb,
        608311.0 / 1250200, 189793.0 / 714400, 198249.0 / 1000160,
        35783.0 / 156275, 247089.0 / 357200, 198249.0 / 2500400,
        0, 32229.0 / 714400, 5220557.0 / 5000800);

    private static Components LinearA98ToXyzD65(Components rgb) => Multiply(rgb,
        573536.0 / 994567, 263643.0 / 1420810, 187206.0 / 994567,
        591459.0 / 1989134, 6239551.0 / 9945670, 374412.0 / 4972835,
        53769.0 / 1989134, 351524.0 / 4972835, 4929758.0 / 4972835);

    private static Components LinearProPhotoToXyzD50(Components rgb) => Multiply(rgb,
        0.79776664490064230, 0.13518129740053308, 0.03134773412839220,
        0.28807482881940130, 0.71183523424187300, 0.00008993693872564,
        0, 0, 0.82510460251046020);

    private static Components LinearRec2020ToXyzD65(Components rgb) => Multiply(rgb,
        63426534.0 / 99577255, 20160776.0 / 139408157, 47086771.0 / 278816314,
        26158966.0 / 99577255, 472592308.0 / 697040785, 8267143.0 / 139408157,
        0, 19567812.0 / 697040785, 295819943.0 / 278816314);

    private static Components Multiply(Components v,
        double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
        new(a * v.X + b * v.Y + c * v.Z,
            d * v.X + e * v.Y + f * v.Z,
            g * v.X + h * v.Y + i * v.Z);

    private static Components MapToSrgb(Components linear)
    {
        // CSS Color 4 binary-search gamut mapping with local MINDE, targeting the
        // host's sRGB surface. In-gamut colors are left untouched.
        if (linear.InGamut) return linear;
        var origin = XyzD65ToOklab(LinearSrgbToXyzD65(linear));
        if (origin.X <= 0) return new(0, 0, 0);
        if (origin.X >= 1) return new(1, 1, 1);
        var chroma = Math.Sqrt(origin.Y * origin.Y + origin.Z * origin.Z);
        if (chroma < 1e-12) return linear.Clip();
        var angle = Math.Atan2(origin.Z, origin.Y);
        var clipped = linear.Clip();
        if (DeltaOklab(origin, XyzD65ToOklab(LinearSrgbToXyzD65(clipped))) < 0.02) return clipped;
        var minimum = 0.0; var maximum = chroma; var minimumInGamut = true;
        while (maximum - minimum > 0.0001)
        {
            var middle = (minimum + maximum) / 2;
            var candidateLab = new Components(origin.X, middle * Math.Cos(angle), middle * Math.Sin(angle));
            var candidate = XyzD65ToLinearSrgb(OklabToXyzD65(candidateLab));
            if (minimumInGamut && candidate.InGamut) { minimum = middle; continue; }
            clipped = candidate.Clip();
            var distance = DeltaOklab(candidateLab, XyzD65ToOklab(LinearSrgbToXyzD65(clipped)));
            if (distance < 0.02)
            {
                if (0.02 - distance < 0.0001) return clipped;
                minimumInGamut = false;
                minimum = middle;
            }
            else maximum = middle;
        }
        return clipped;
    }

    private static double DeltaOklab(Components one, Components two)
    {
        var dl = one.X - two.X; var da = one.Y - two.Y; var db = one.Z - two.Z;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }
}
