using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Parses CSS color syntax: hex (CSS semantics: #RGB/#RGBA/#RRGGBB/#RRGGBBAA — trailing
/// alpha, unlike XAML's leading alpha), named colors, transparent, currentcolor, and the
/// rgb()/rgba()/hsl()/hsla() functions in both legacy comma and modern space syntax.
/// </summary>
internal static class CssColorParser
{
    public static bool TryParse(ref CssTokenReader reader, out Color color, out bool isCurrentColor)
    {
        isCurrentColor = false;

        var probe = reader;
        if (probe.TryReadHash(out var hexDigits) && TryParseHex(hexDigits, out color))
        {
            reader = probe;
            return true;
        }

        probe = reader;
        if (probe.TryReadFunction(out var name, out var args))
        {
            if (name.Equals("rgb", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("rgba", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseRgbArguments(ref args, out color))
                {
                    reader = probe;
                    return true;
                }
            }
            else if (name.Equals("hsl", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("hsla", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseHslArguments(ref args, out color))
                {
                    reader = probe;
                    return true;
                }
            }

            color = default;
            return false;
        }

        probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.FromArgb(0, 0, 0, 0);
                reader = probe;
                return true;
            }

            if (ident.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            {
                color = default;
                isCurrentColor = true;
                reader = probe;
                return true;
            }

            if (CssNamedColors.TryGet(ident, out color))
            {
                reader = probe;
                return true;
            }
        }

        color = default;
        return false;
    }

    internal static bool TryParseHex(ReadOnlySpan<char> digits, out Color color)
    {
        color = default;
        byte r, g, b;
        byte a = 0xFF;
        switch (digits.Length)
        {
            case 3:
            case 4:
                if (!TryHexNibble(digits[0], out r) || !TryHexNibble(digits[1], out g) || !TryHexNibble(digits[2], out b))
                {
                    return false;
                }

                r = (byte)(r * 17);
                g = (byte)(g * 17);
                b = (byte)(b * 17);
                if (digits.Length == 4)
                {
                    if (!TryHexNibble(digits[3], out a))
                    {
                        return false;
                    }

                    a = (byte)(a * 17);
                }

                break;
            case 6:
            case 8:
                if (!TryHexByte(digits, 0, out r) || !TryHexByte(digits, 2, out g) || !TryHexByte(digits, 4, out b))
                {
                    return false;
                }

                if (digits.Length == 8 && !TryHexByte(digits, 6, out a))
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static bool TryParseRgbArguments(ref CssTokenReader args, out Color color)
    {
        color = default;
        if (!TryReadRgbComponent(ref args, out var r))
        {
            return false;
        }

        var legacy = args.TryReadComma();
        if (!TryReadRgbComponent(ref args, out var g))
        {
            return false;
        }

        if (legacy && !args.TryReadComma())
        {
            return false;
        }

        if (!TryReadRgbComponent(ref args, out var b))
        {
            return false;
        }

        var alpha = 1.0;
        var hasAlphaSeparator = legacy ? args.TryReadComma() : args.TryReadSlash();
        if (hasAlphaSeparator && !TryReadAlpha(ref args, out alpha))
        {
            return false;
        }

        if (!args.AtEnd)
        {
            return false;
        }

        color = Color.FromArgb(ToAlphaByte(alpha), r, g, b);
        return true;
    }

    private static bool TryParseHslArguments(ref CssTokenReader args, out Color color)
    {
        color = default;
        if (!args.TryReadNumber(out var hueValue, out var hueUnit) ||
            !CssUnitConversion.TryToDegrees(hueValue, hueUnit, out var hue))
        {
            return false;
        }

        var legacy = args.TryReadComma();
        if (!TryReadPercentage(ref args, out var saturation))
        {
            return false;
        }

        if (legacy && !args.TryReadComma())
        {
            return false;
        }

        if (!TryReadPercentage(ref args, out var lightness))
        {
            return false;
        }

        var alpha = 1.0;
        var hasAlphaSeparator = legacy ? args.TryReadComma() : args.TryReadSlash();
        if (hasAlphaSeparator && !TryReadAlpha(ref args, out alpha))
        {
            return false;
        }

        if (!args.AtEnd)
        {
            return false;
        }

        HslToRgb(hue, Math.Clamp(saturation, 0, 1), Math.Clamp(lightness, 0, 1), out var r, out var g, out var b);
        color = Color.FromArgb(ToAlphaByte(alpha), r, g, b);
        return true;
    }

    private static bool TryReadRgbComponent(ref CssTokenReader reader, out byte component)
    {
        if (!reader.TryReadNumber(out var value, out var unit))
        {
            component = 0;
            return false;
        }

        double scaled;
        switch (unit)
        {
            case CssUnit.None:
                scaled = value;
                break;
            case CssUnit.Percent:
                scaled = value * 255.0 / 100.0;
                break;
            default:
                component = 0;
                return false;
        }

        component = (byte)Math.Clamp(Math.Round(scaled), 0, 255);
        return true;
    }

    private static bool TryReadPercentage(ref CssTokenReader reader, out double fraction)
    {
        if (reader.TryReadNumber(out var value, out var unit) && unit == CssUnit.Percent)
        {
            fraction = value / 100.0;
            return true;
        }

        fraction = 0;
        return false;
    }

    private static bool TryReadAlpha(ref CssTokenReader reader, out double alpha)
    {
        if (!reader.TryReadNumber(out var value, out var unit))
        {
            alpha = 1.0;
            return false;
        }

        switch (unit)
        {
            case CssUnit.None:
                alpha = value;
                return true;
            case CssUnit.Percent:
                alpha = value / 100.0;
                return true;
            default:
                alpha = 1.0;
                return false;
        }
    }

    private static byte ToAlphaByte(double alpha)
        => (byte)Math.Clamp(Math.Round(alpha * 255.0), 0, 255);

    private static void HslToRgb(double hue, double saturation, double lightness, out byte r, out byte g, out byte b)
    {
        var h = ((hue % 360.0) + 360.0) % 360.0 / 60.0;
        var c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        var x = c * (1.0 - Math.Abs(h % 2.0 - 1.0));
        var m = lightness - c / 2.0;

        double r1, g1, b1;
        if (h < 1) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 2) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 3) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 4) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 5) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        r = (byte)Math.Clamp(Math.Round((r1 + m) * 255.0), 0, 255);
        g = (byte)Math.Clamp(Math.Round((g1 + m) * 255.0), 0, 255);
        b = (byte)Math.Clamp(Math.Round((b1 + m) * 255.0), 0, 255);
    }

    private static bool TryHexNibble(char c, out byte value)
    {
        if (char.IsAsciiDigit(c))
        {
            value = (byte)(c - '0');
            return true;
        }

        if (c is >= 'a' and <= 'f')
        {
            value = (byte)(c - 'a' + 10);
            return true;
        }

        if (c is >= 'A' and <= 'F')
        {
            value = (byte)(c - 'A' + 10);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryHexByte(ReadOnlySpan<char> digits, int offset, out byte value)
    {
        if (TryHexNibble(digits[offset], out var hi) && TryHexNibble(digits[offset + 1], out var lo))
        {
            value = (byte)((hi << 4) | lo);
            return true;
        }

        value = 0;
        return false;
    }
}
