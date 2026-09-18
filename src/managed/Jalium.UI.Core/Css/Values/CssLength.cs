namespace Jalium.UI.Styling;

[System.Runtime.CompilerServices.InlineArray(4)]
internal struct CssLengthBuffer
{
    private CssLength _element;
}

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
    Expression,
    Vmin,
    Vmax,
    Auto,
    Fr,
    Normal,
    Cqw, Cqh, Cqi, Cqb, Cqmin, Cqmax,
    Dpi, Dpcm, Dppx,
    Hz, Khz,
    Ex, Rex, Cap, Rcap, Ch, Rch, Ic, Ric, Lh, Rlh,
    Vi, Vb,
    Svw, Svh, Svi, Svb, Svmin, Svmax,
    Lvw, Lvh, Lvi, Lvb, Lvmin, Lvmax,
    Dvw, Dvh, Dvi, Dvb, Dvmin, Dvmax,
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
    public readonly CssContainerUnitContext? Containers;
    public readonly CssFontContext? Fonts;
    public readonly CssViewportMetrics? Viewports;

    public CssLengthContext(
        double elementFontSize, double inheritedFontSize, double rootFontSize,
        double viewportWidth, double viewportHeight, CssContainerUnitContext? containers = null,
        CssFontContext? fonts = null, CssViewportMetrics? viewports = null)
    {
        ElementFontSize = elementFontSize > 0 && !double.IsNaN(elementFontSize) ? elementFontSize : DefaultFontSize;
        InheritedFontSize = inheritedFontSize > 0 && !double.IsNaN(inheritedFontSize) ? inheritedFontSize : DefaultFontSize;
        RootFontSize = rootFontSize > 0 && !double.IsNaN(rootFontSize) ? rootFontSize : DefaultFontSize;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        Containers = containers;
        Fonts = fonts;
        Viewports = viewports;
    }

    public CssLengthContext WithElementFontSize(double elementFontSize)
        => new(elementFontSize, InheritedFontSize, Fonts?.IsRoot == true ? elementFontSize : RootFontSize,
            ViewportWidth, ViewportHeight, Containers, Fonts, Viewports);

    public CssLengthContext WithRootFontSize(double rootFontSize)
        => new(ElementFontSize, InheritedFontSize, rootFontSize, ViewportWidth, ViewportHeight, Containers, Fonts, Viewports);

    internal CssLengthContext WithFonts(CssFontContext fonts)
        => new(ElementFontSize, InheritedFontSize, RootFontSize, ViewportWidth, ViewportHeight, Containers, fonts, Viewports);

    internal CssLengthContext ForFontProperty()
        => new(InheritedFontSize, InheritedFontSize, Fonts?.IsRoot == true ? DefaultFontSize : RootFontSize,
            ViewportWidth, ViewportHeight, Containers, (Fonts ?? CssFontContext.Initial).ForFontProperty(), Viewports);

    internal CssLengthContext ForLineHeight()
        => WithFonts((Fonts ?? CssFontContext.Initial).ForLineHeight());

    public static CssLengthContext Default => new(DefaultFontSize, DefaultFontSize, DefaultFontSize, 0, 0);
}

/// <summary>A parsed CSS length that may still require element context to resolve.</summary>
internal readonly struct CssLength
{
    public readonly double Value;
    public readonly CssUnit Unit;
    public readonly CssMathExpression? Expression;
    public bool UsesPercent => Unit == CssUnit.Percent || Expression?.UsesPercent == true;

    public CssLength(CssMathExpression expression)
    {
        Value = 0;
        Unit = CssUnit.Expression;
        Expression = expression;
    }

    public CssLength(double value, CssUnit unit)
    {
        Value = value;
        Unit = unit;
        Expression = null;
    }

    public bool IsAbsolute => Unit is CssUnit.None or CssUnit.Px or CssUnit.Pt or CssUnit.In
        or CssUnit.Cm or CssUnit.Mm or CssUnit.Q or CssUnit.Pc || Expression?.IsAbsolute == true;

    public bool IsLengthUnit => IsAbsolute || Unit is CssUnit.Em or CssUnit.Rem or CssUnit.Vw or CssUnit.Vh or CssUnit.Vmin or CssUnit.Vmax or CssUnit.Expression
        or CssUnit.Cqw or CssUnit.Cqh or CssUnit.Cqi or CssUnit.Cqb or CssUnit.Cqmin or CssUnit.Cqmax ||
        Unit is >= CssUnit.Ex and <= CssUnit.Dvmax;

    internal bool IsFontRelative => Unit is CssUnit.Em or CssUnit.Rem || Unit is >= CssUnit.Ex and <= CssUnit.Rlh;

    internal void ObserveContainerDependencies(in CssLengthContext context)
    {
        if (Unit is >= CssUnit.Cqw and <= CssUnit.Cqmax) context.Containers?.Resolve(Unit);
        if (Unit is >= CssUnit.Ex and <= CssUnit.Rlh) context.Fonts?.Dependency?.Observe();
        Expression?.ObserveContainerDependencies(context);
    }

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
        CssUnit.Expression => Expression!.TryEvaluate(CssLengthContext.Default, double.NaN, out var result) ? result : double.NaN,
        _ => double.NaN,
    };

    public bool TryResolve(in CssLengthContext context, CssPercentBasis percentBasis, out double result)
    {
        if (Unit is >= CssUnit.Ex and <= CssUnit.Rlh)
        {
            result = Value * (context.Fonts ?? CssFontContext.Initial).Resolve(Unit, context);
            return double.IsFinite(result);
        }
        if (Unit is CssUnit.Vw or CssUnit.Vh or CssUnit.Vmin or CssUnit.Vmax || Unit is >= CssUnit.Vi and <= CssUnit.Dvmax)
        {
            if (context.Viewports is null && (!double.IsFinite(context.ViewportWidth) || !double.IsFinite(context.ViewportHeight) ||
                context.ViewportWidth < 0 || context.ViewportHeight < 0)) { result = double.NaN; return false; }
            var viewports = context.Viewports ?? CssViewportMetrics.Uniform(new(context.ViewportWidth, context.ViewportHeight));
            var size = Unit is >= CssUnit.Svw and <= CssUnit.Svmax ? viewports.Small
                : Unit is >= CssUnit.Dvw and <= CssUnit.Dvmax ? viewports.Dynamic : viewports.Large;
            var dimension = Unit switch
            {
                CssUnit.Vw or CssUnit.Svw or CssUnit.Lvw or CssUnit.Dvw => size.Width,
                CssUnit.Vh or CssUnit.Svh or CssUnit.Lvh or CssUnit.Dvh => size.Height,
                CssUnit.Vi or CssUnit.Svi or CssUnit.Lvi or CssUnit.Dvi => viewports.IsVertical ? size.Height : size.Width,
                CssUnit.Vb or CssUnit.Svb or CssUnit.Lvb or CssUnit.Dvb => viewports.IsVertical ? size.Width : size.Height,
                CssUnit.Vmin or CssUnit.Svmin or CssUnit.Lvmin or CssUnit.Dvmin => Math.Min(size.Width, size.Height),
                _ => Math.Max(size.Width, size.Height),
            };
            result = Value * dimension / 100;
            return double.IsFinite(result);
        }
        switch (Unit)
        {
            case CssUnit.Cqw: case CssUnit.Cqh: case CssUnit.Cqi: case CssUnit.Cqb: case CssUnit.Cqmin: case CssUnit.Cqmax:
                var dimension = context.Containers?.Resolve(Unit) ?? (Unit switch
                {
                    CssUnit.Cqw or CssUnit.Cqi => context.ViewportWidth, CssUnit.Cqh or CssUnit.Cqb => context.ViewportHeight,
                    CssUnit.Cqmin => Math.Min(context.ViewportWidth, context.ViewportHeight), _ => Math.Max(context.ViewportWidth, context.ViewportHeight),
                });
                result = Value * dimension / 100;
                return double.IsFinite(result);
            case CssUnit.Expression:
                var basis = percentBasis == CssPercentBasis.ElementFontSize ? context.ElementFontSize
                    : percentBasis == CssPercentBasis.Fraction ? 1 : double.NaN;
                return Expression!.TryEvaluate(context, basis, out result);
            case CssUnit.Vmin:
            case CssUnit.Vmax:
                result = Value / 100 * (Unit == CssUnit.Vmin ? Math.Min(context.ViewportWidth, context.ViewportHeight)
                    : Math.Max(context.ViewportWidth, context.ViewportHeight));
                return context.ViewportWidth > 0 && context.ViewportHeight > 0;
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
        if (Eq(text, "ex")) { unit = CssUnit.Ex; return true; }
        if (Eq(text, "rex")) { unit = CssUnit.Rex; return true; }
        if (Eq(text, "cap")) { unit = CssUnit.Cap; return true; }
        if (Eq(text, "rcap")) { unit = CssUnit.Rcap; return true; }
        if (Eq(text, "ch")) { unit = CssUnit.Ch; return true; }
        if (Eq(text, "rch")) { unit = CssUnit.Rch; return true; }
        if (Eq(text, "ic")) { unit = CssUnit.Ic; return true; }
        if (Eq(text, "ric")) { unit = CssUnit.Ric; return true; }
        if (Eq(text, "lh")) { unit = CssUnit.Lh; return true; }
        if (Eq(text, "rlh")) { unit = CssUnit.Rlh; return true; }
        if (Eq(text, "vi")) { unit = CssUnit.Vi; return true; }
        if (Eq(text, "vb")) { unit = CssUnit.Vb; return true; }
        if (Eq(text, "svw")) { unit = CssUnit.Svw; return true; }
        if (Eq(text, "svh")) { unit = CssUnit.Svh; return true; }
        if (Eq(text, "svi")) { unit = CssUnit.Svi; return true; }
        if (Eq(text, "svb")) { unit = CssUnit.Svb; return true; }
        if (Eq(text, "svmin")) { unit = CssUnit.Svmin; return true; }
        if (Eq(text, "svmax")) { unit = CssUnit.Svmax; return true; }
        if (Eq(text, "lvw")) { unit = CssUnit.Lvw; return true; }
        if (Eq(text, "lvh")) { unit = CssUnit.Lvh; return true; }
        if (Eq(text, "lvi")) { unit = CssUnit.Lvi; return true; }
        if (Eq(text, "lvb")) { unit = CssUnit.Lvb; return true; }
        if (Eq(text, "lvmin")) { unit = CssUnit.Lvmin; return true; }
        if (Eq(text, "lvmax")) { unit = CssUnit.Lvmax; return true; }
        if (Eq(text, "dvw")) { unit = CssUnit.Dvw; return true; }
        if (Eq(text, "dvh")) { unit = CssUnit.Dvh; return true; }
        if (Eq(text, "dvi")) { unit = CssUnit.Dvi; return true; }
        if (Eq(text, "dvb")) { unit = CssUnit.Dvb; return true; }
        if (Eq(text, "dvmin")) { unit = CssUnit.Dvmin; return true; }
        if (Eq(text, "dvmax")) { unit = CssUnit.Dvmax; return true; }
        if (Eq(text, "vw")) { unit = CssUnit.Vw; return true; }
        if (Eq(text, "vh")) { unit = CssUnit.Vh; return true; }
        if (Eq(text, "vmin")) { unit = CssUnit.Vmin; return true; }
        if (Eq(text, "vmax")) { unit = CssUnit.Vmax; return true; }
        if (Eq(text, "cqw")) { unit = CssUnit.Cqw; return true; }
        if (Eq(text, "cqh")) { unit = CssUnit.Cqh; return true; }
        if (Eq(text, "cqi")) { unit = CssUnit.Cqi; return true; }
        if (Eq(text, "cqb")) { unit = CssUnit.Cqb; return true; }
        if (Eq(text, "cqmin")) { unit = CssUnit.Cqmin; return true; }
        if (Eq(text, "cqmax")) { unit = CssUnit.Cqmax; return true; }
        if (Eq(text, "deg")) { unit = CssUnit.Deg; return true; }
        if (Eq(text, "rad")) { unit = CssUnit.Rad; return true; }
        if (Eq(text, "grad")) { unit = CssUnit.Grad; return true; }
        if (Eq(text, "turn")) { unit = CssUnit.Turn; return true; }
        if (Eq(text, "s")) { unit = CssUnit.S; return true; }
        if (Eq(text, "ms")) { unit = CssUnit.Ms; return true; }
        if (Eq(text, "fr")) { unit = CssUnit.Fr; return true; }
        if (Eq(text, "dpi")) { unit = CssUnit.Dpi; return true; }
        if (Eq(text, "dpcm")) { unit = CssUnit.Dpcm; return true; }
        if (Eq(text, "dppx") || Eq(text, "x")) { unit = CssUnit.Dppx; return true; }
        if (Eq(text, "hz")) { unit = CssUnit.Hz; return true; }
        if (Eq(text, "khz")) { unit = CssUnit.Khz; return true; }

        unit = CssUnit.None;
        return false;
    }

    internal static bool TryToDppx(double value, CssUnit unit, out double result)
    {
        result = unit switch { CssUnit.Dpi => value / 96, CssUnit.Dpcm => value * 2.54 / 96, CssUnit.Dppx => value, _ => double.NaN };
        return double.IsFinite(result) && result >= 0;
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
