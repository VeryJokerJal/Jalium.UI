using Jalium.UI.Media;
using System.Globalization;

namespace Jalium.UI.Controls;
/// <summary>Shared, allocation-free color conversion for the picker and its component controls.</summary>
internal static class ColorPickerColorMath
{
    internal static bool TryParseHex(string? text, byte defaultAlpha, out Color color)
    {
        color = default;
        if (text == null)
            return false;
        var span = text.AsSpan().Trim();
        if (span.StartsWith("#"))
            span = span[1..];
        if (span.Length is not (6 or 8))
            return false;
        byte alpha = defaultAlpha;
        if (span.Length == 8)
        {
            if (!byte.TryParse(span[..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out alpha))
                return false;
            span = span[2..];
        }

        if (!byte.TryParse(span[..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var red) || !byte.TryParse(span.Slice(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var green) || !byte.TryParse(span.Slice(4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var blue))
            return false;
        color = Color.FromArgb(alpha, red, green, blue);
        return true;
    }

    internal static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    internal static double Hue(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 360) : 0;
    internal static Color FromHsv(double hue, double saturation, double value, byte alpha = 255)
    {
        double h = Hue(hue) % 360, s = Unit(saturation), v = Unit(value);
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)};
        return Color.FromArgb(alpha, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    internal static (double Hue, double Saturation, double Value) ToHsv(Color color, double achromaticHue = 0)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? Hue(achromaticHue) : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0)
            hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }
}
