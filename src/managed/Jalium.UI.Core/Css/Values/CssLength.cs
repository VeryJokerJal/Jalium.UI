namespace Jalium.UI.Styling;

/// <summary>Units recognized by the CSS value parser.</summary>
internal enum CssUnit : byte
{
    None,
    Px,
    Pt,
    In,
    Cm,
    Mm,
    Q,
    Pc,
    Em,
    Rem,
    Percent,
    Vw,
    Vh,
    Deg,
    Rad,
    Grad,
    Turn,
    S,
    Ms,
}

/// <summary>What a percentage value resolves against for a given CSS property.</summary>
internal enum CssPercentBasis : byte
{
    /// <summary>Percentages are not representable for this property; the declaration is dropped.</summary>
    NotSupported,

    /// <summary>Percentage multiplies the element's effective font size (font-size, line-height).</summary>
    ElementFontSize,

    /// <summary>Percentage maps to value/100 directly (opacity, transform-origin, gradient stops).</summary>
    Fraction,
}

/// <summary>Per-element context needed to resolve relative CSS lengths at apply time.</summary>
internal readonly struct CssLengthContext
{
    public const double DefaultFontSize = 14.0;

    /// <summary>The element's computed font size — the em basis for every property except font-size.</summary>
    public readonly double ElementFontSize;

    /// <summary>The parent's computed font size — the em/% basis for font-size itself (CSS semantics).</summary>
    public readonly double InheritedFontSize;

    public readonly double RootFontSize;
    public readonly double ViewportWidth;
    public readonly double ViewportHeight;

    public CssLengthContext(
        double elementFontSize, double inheritedFontSize, double rootFontSize,
        double viewportWidth, double viewportHeight)
    {
        ElementFontSize = elementFontSize > 0 && !double.IsNaN(elementFontSize) ? elementFontSize : DefaultFontSize;
        InheritedFontSize = inheritedFontSize > 0 && !double.IsNaN(inheritedFontSize) ? inheritedFontSize : DefaultFontSize;
        RootFontSize = rootFontSize > 0 && !double.IsNaN(rootFontSize) ? rootFontSize : DefaultFontSize;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    public CssLengthContext WithElementFontSize(double elementFontSize)
        => new(elementFontSize, InheritedFontSize, RootFontSize, ViewportWidth, ViewportHeight);

    public static CssLengthContext Default => new(DefaultFontSize, DefaultFontSize, DefaultFontSize, 0, 0);
}

/// <summary>A parsed CSS length that may still require element context to resolve.</summary>
internal readonly struct CssLength
{
    public readonly double Value;
    public readonly CssUnit Unit;

    public CssLength(double value, CssUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public bool IsAbsolute => Unit is CssUnit.None or CssUnit.Px or CssUnit.Pt or CssUnit.In
        or CssUnit.Cm or CssUnit.Mm or CssUnit.Q or CssUnit.Pc;

    public bool IsLengthUnit => IsAbsolute || Unit is CssUnit.Em or CssUnit.Rem or CssUnit.Vw or CssUnit.Vh;

    /// <summary>Converts an absolute length to device-independent pixels (1px = 1/96in).</summary>
    public double ToPxAbsolute() => Unit switch
    {
        CssUnit.None or CssUnit.Px => Value,
        CssUnit.Pt => Value * (96.0 / 72.0),
        CssUnit.In => Value * 96.0,
        CssUnit.Cm => Value * (96.0 / 2.54),
        CssUnit.Mm => Value * (96.0 / 25.4),
        CssUnit.Q => Value * (96.0 / 101.6),
        CssUnit.Pc => Value * 16.0,
        _ => double.NaN,
    };

    public bool TryResolve(in CssLengthContext context, CssPercentBasis percentBasis, out double result)
    {
        switch (Unit)
        {
            case CssUnit.None:
            case CssUnit.Px:
            case CssUnit.Pt:
            case CssUnit.In:
            case CssUnit.Cm:
            case CssUnit.Mm:
            case CssUnit.Q:
            case CssUnit.Pc:
                result = ToPxAbsolute();
                return true;
            case CssUnit.Em:
                result = Value * context.ElementFontSize;
                return true;
            case CssUnit.Rem:
                result = Value * context.RootFontSize;
                return true;
            case CssUnit.Percent:
                switch (percentBasis)
                {
                    case CssPercentBasis.ElementFontSize:
                        result = Value / 100.0 * context.ElementFontSize;
                        return true;
                    case CssPercentBasis.Fraction:
                        result = Value / 100.0;
                        return true;
                    default:
                        result = double.NaN;
                        return false;
                }
            case CssUnit.Vw:
                if (context.ViewportWidth > 0)
                {
                    result = Value / 100.0 * context.ViewportWidth;
                    return true;
                }
                result = double.NaN;
                return false;
            case CssUnit.Vh:
                if (context.ViewportHeight > 0)
                {
                    result = Value / 100.0 * context.ViewportHeight;
                    return true;
                }
                result = double.NaN;
                return false;
            default:
                result = double.NaN;
                return false;
        }
    }
}

internal static class CssUnitConversion
{
    public static bool TryMapUnit(ReadOnlySpan<char> text, out CssUnit unit)
    {
        if (text.IsEmpty)
        {
            unit = CssUnit.None;
            return true;
        }

        if (Eq(text, "px")) { unit = CssUnit.Px; return true; }
        if (Eq(text, "pt")) { unit = CssUnit.Pt; return true; }
        if (Eq(text, "in")) { unit = CssUnit.In; return true; }
        if (Eq(text, "cm")) { unit = CssUnit.Cm; return true; }
        if (Eq(text, "mm")) { unit = CssUnit.Mm; return true; }
        if (Eq(text, "q")) { unit = CssUnit.Q; return true; }
        if (Eq(text, "pc")) { unit = CssUnit.Pc; return true; }
        if (Eq(text, "em")) { unit = CssUnit.Em; return true; }
        if (Eq(text, "rem")) { unit = CssUnit.Rem; return true; }
        if (Eq(text, "vw")) { unit = CssUnit.Vw; return true; }
        if (Eq(text, "vh")) { unit = CssUnit.Vh; return true; }
        if (Eq(text, "deg")) { unit = CssUnit.Deg; return true; }
        if (Eq(text, "rad")) { unit = CssUnit.Rad; return true; }
        if (Eq(text, "grad")) { unit = CssUnit.Grad; return true; }
        if (Eq(text, "turn")) { unit = CssUnit.Turn; return true; }
        if (Eq(text, "s")) { unit = CssUnit.S; return true; }
        if (Eq(text, "ms")) { unit = CssUnit.Ms; return true; }

        unit = CssUnit.None;
        return false;
    }

    /// <summary>Angle conversion. A unitless number is treated as degrees (hue, lenient transform args).</summary>
    public static bool TryToDegrees(double value, CssUnit unit, out double degrees)
    {
        switch (unit)
        {
            case CssUnit.None:
            case CssUnit.Deg:
                degrees = value;
                return true;
            case CssUnit.Rad:
                degrees = value * (180.0 / Math.PI);
                return true;
            case CssUnit.Grad:
                degrees = value * 0.9;
                return true;
            case CssUnit.Turn:
                degrees = value * 360.0;
                return true;
            default:
                degrees = double.NaN;
                return false;
        }
    }

    public static bool TryToMilliseconds(double value, CssUnit unit, out double milliseconds)
    {
        switch (unit)
        {
            case CssUnit.S:
                milliseconds = value * 1000.0;
                return true;
            case CssUnit.Ms:
                milliseconds = value;
                return true;
            default:
                milliseconds = double.NaN;
                return false;
        }
    }

    private static bool Eq(ReadOnlySpan<char> text, string candidate)
        => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);
}
