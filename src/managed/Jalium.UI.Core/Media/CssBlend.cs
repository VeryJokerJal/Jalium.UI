namespace Jalium.UI.Media;

/// <summary>Color blending operations used by CSS compositing groups.</summary>
public enum CssBlendMode
{
    Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn,
    HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity,
    PlusLighter,
}

/// <summary>Blends straight sRGB colors, then performs alpha compositing.</summary>
internal static class CssBlend
{
    private readonly record struct Rgb(double R, double G, double B)
    {
        internal double Min => Math.Min(R, Math.Min(G, B));
        internal double Max => Math.Max(R, Math.Max(G, B));
        internal double Lum => .3 * R + .59 * G + .11 * B;
        public static Rgb operator +(Rgb a, double b) => new(a.R + b, a.G + b, a.B + b);
        public static Rgb operator -(Rgb a, double b) => a + -b;
        public static Rgb operator *(Rgb a, double b) => new(a.R * b, a.G * b, a.B * b);
    }

    internal static Color Composite(Color source, Color backdrop, CssBlendMode mode, double opacity = 1)
    {
        var result = Composite((source.R / 255d, source.G / 255d, source.B / 255d, source.A / 255d),
            (backdrop.R / 255d, backdrop.G / 255d, backdrop.B / 255d, backdrop.A / 255d), mode, opacity);
        return Color.FromArgb(Byte(result.A), Byte(result.R), Byte(result.G), Byte(result.B));
    }

    internal static (double R, double G, double B, double A) Composite(
        (double R, double G, double B, double A) source,
        (double R, double G, double B, double A) backdrop, CssBlendMode mode, double opacity = 1)
    {
        var sa = source.A * Math.Clamp(opacity, 0, 1); var ba = backdrop.A;
        var s = new Rgb(source.R, source.G, source.B);
        var b = new Rgb(backdrop.R, backdrop.G, backdrop.B);
        double alpha; Rgb result;
        if (mode == CssBlendMode.PlusLighter)
        {
            alpha = Math.Min(1, sa + ba);
            result = new(Math.Min(1, s.R * sa + b.R * ba), Math.Min(1, s.G * sa + b.G * ba), Math.Min(1, s.B * sa + b.B * ba));
        }
        else
        {
            alpha = sa + ba * (1 - sa);
            var mixed = mode switch
            {
                CssBlendMode.Hue => SetLum(SetSat(s, b.Max - b.Min), b.Lum),
                CssBlendMode.Saturation => SetLum(SetSat(b, s.Max - s.Min), b.Lum),
                CssBlendMode.Color => SetLum(s, b.Lum),
                CssBlendMode.Luminosity => SetLum(b, s.Lum),
                _ => new Rgb(Channel(b.R, s.R, mode), Channel(b.G, s.G, mode), Channel(b.B, s.B, mode)),
            };
            double Part(double cs, double cb, double blend) => sa * (1 - ba) * cs + sa * ba * blend + (1 - sa) * ba * cb;
            result = new(Part(s.R, b.R, mixed.R), Part(s.G, b.G, mixed.G), Part(s.B, b.B, mixed.B));
        }
        if (!(alpha > 0)) return default;
        return (result.R / alpha, result.G / alpha, result.B / alpha, alpha);
    }

    internal static void Composite(ReadOnlySpan<byte> source, Span<byte> destination, CssBlendMode mode, double opacity)
    {
        var value = Composite(Color.FromArgb(source[3], source[2], source[1], source[0]),
            Color.FromArgb(destination[3], destination[2], destination[1], destination[0]), mode, opacity);
        destination[0] = value.B; destination[1] = value.G; destination[2] = value.R; destination[3] = value.A;
    }

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    private static double Channel(double b, double s, CssBlendMode mode) => mode switch
    {
        CssBlendMode.Multiply => b * s,
        CssBlendMode.Screen => b + s - b * s,
        CssBlendMode.Overlay => b <= .5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
        CssBlendMode.Darken => Math.Min(b, s),
        CssBlendMode.Lighten => Math.Max(b, s),
        CssBlendMode.ColorDodge => b == 0 ? 0 : s == 1 ? 1 : Math.Min(1, b / (1 - s)),
        CssBlendMode.ColorBurn => b == 1 ? 1 : s == 0 ? 0 : 1 - Math.Min(1, (1 - b) / s),
        CssBlendMode.HardLight => s <= .5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
        CssBlendMode.SoftLight => s <= .5 ? b - (1 - 2 * s) * b * (1 - b) : b + (2 * s - 1) * ((b <= .25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b)) - b),
        CssBlendMode.Difference => Math.Abs(b - s),
        CssBlendMode.Exclusion => b + s - 2 * b * s,
        _ => s,
    };

    private static Rgb SetLum(Rgb color, double luminance)
    {
        color += luminance - color.Lum;
        var l = color.Lum; var n = color.Min; var x = color.Max;
        if (n < 0) color = (color - l) * (l / (l - n)) + l;
        if (x > 1) color = (color - l) * ((1 - l) / (x - l)) + l;
        return color;
    }

    private static Rgb SetSat(Rgb color, double saturation)
    {
        var n = color.Min; var x = color.Max;
        if (x == n) return default;
        var scale = saturation / (x - n);
        return (color - n) * scale;
    }
}
