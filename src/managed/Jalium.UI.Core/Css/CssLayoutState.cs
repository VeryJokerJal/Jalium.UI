namespace Jalium.UI.Styling;

internal enum CssLayoutSlotField : byte
{
    Width,
    Height,
    MinWidth,
    MinHeight,
    MaxWidth,
    MaxHeight,
}

internal enum CssBoxSizing : byte
{
    /// <summary>The framework's native sizing model (Width includes padding + border).</summary>
    BorderBox,
    ContentBox,
}

internal enum CssPositionMode : byte
{
    Static,
    Relative,
    Absolute,
    Fixed,
    Sticky,
}

internal enum CssPositionKeyword : byte
{
    Static,
    Relative,
    Absolute,
    Fixed,
    Sticky,
}

/// <summary>A layout-time length: unset, resolved pixels, or a percentage fraction.</summary>
internal readonly struct CssLayoutLength : IEquatable<CssLayoutLength>
{
    public const byte KindUnset = 0;
    public const byte KindPx = 1;
    public const byte KindPercent = 2;
    public const byte KindExpression = 3;
    public const byte KindAuto = 4;
    public const byte KindNormal = 5;
    public const byte KindMinContent = 6;
    public const byte KindMaxContent = 7;
    public const byte KindFitContent = 8;
    public const byte KindStretch = 9;
    public const byte KindFitFunction = 10;
    public const byte KindCalcSize = 11;
    public const byte KindContent = 12;

    public readonly double Value;
    public readonly byte Kind;
    private readonly byte _argumentKind;
    public readonly CssMathExpression? Expression;
    public readonly CssLengthContext Context;
    internal readonly CssCalcSizeComputed? Calculation;

    private CssLayoutLength(CssMathExpression expression, CssLengthContext context)
    {
        Kind = KindExpression;
        Value = 0;
        Expression = expression;
        Context = context;
    }

    public static CssLayoutLength Math(CssMathExpression expression, CssLengthContext context)
    {
        expression.ObserveContainerDependencies(context);
        return new(expression, context);
    }

    private CssLayoutLength(double value, byte kind)
    {
        Value = value;
        Kind = kind;
    }

    public static CssLayoutLength Unset => default;
    public static CssLayoutLength Auto => new(0, KindAuto);
    public static CssLayoutLength Normal => new(0, KindNormal);
    public static CssLayoutLength Sizing(byte kind) => new(0, kind);
    public bool IsSizing => Kind >= KindMinContent && Kind <= KindContent;
    internal CssLayoutLength SizingBasis => Calculation?.SizingBasis ?? this;
    internal static CssLayoutLength Calculate(CssCalcSizeComputed calculation) => new(calculation);
    private CssLayoutLength(CssCalcSizeComputed calculation)
    {
        Kind = KindCalcSize;
        Calculation = calculation;
    }
    public CssLayoutLength FitArgument => Kind == KindFitFunction
        ? _argumentKind == KindExpression ? new CssLayoutLength(Expression!, Context) : new(Value, _argumentKind)
        : default;
    public static CssLayoutLength Fit(CssLayoutLength argument) => new(argument);
    private CssLayoutLength(CssLayoutLength argument)
    {
        Kind = KindFitFunction; Value = argument.Value; Expression = argument.Expression;
        Context = argument.Context; _argumentKind = argument.Kind;
    }
    public bool IsNormal => Kind == KindNormal;
    public bool IsAuto => Kind == KindAuto;

    public static CssLayoutLength Px(double value) => new(value, KindPx);

    /// <summary>fraction is 0-1 (50% ⇒ 0.5).</summary>
    public static CssLayoutLength Percent(double fraction) => new(fraction, KindPercent);

    public bool IsSet => Kind != KindUnset;

    public bool IsPercent => Kind == KindPercent || Kind == KindFitFunction && _argumentKind == KindPercent ||
        Expression?.UsesPercent == true || Calculation?.UsesPercent == true;

    /// <summary>
    /// Resolves against the containing-block basis. A percentage against an infinite or
    /// invalid basis degrades to <paramref name="infinityFallback"/> (auto=NaN, 0, or +∞
    /// depending on the property).
    /// </summary>
    public double Resolve(double basis, double infinityFallback)
    {
        switch (Kind)
        {
            case KindExpression:
                return Expression!.TryEvaluate(Context, basis, out var value) ? value : infinityFallback;
            case KindPx:
                return Value;
            case KindPercent:
                if (double.IsInfinity(basis) || double.IsNaN(basis) || basis < 0)
                {
                    return infinityFallback;
                }

                return Value * basis;
            default:
                return infinityFallback;
        }
    }

    public bool Equals(CssLayoutLength other) => Kind == other.Kind && _argumentKind == other._argumentKind && Value.Equals(other.Value) &&
        Equals(Expression, other.Expression) && Context.Equals(other.Context) && Equals(Calculation, other.Calculation);

    public override bool Equals(object? obj) => obj is CssLayoutLength other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, Kind, Expression, Context, _argumentKind, Calculation);
}

/// <summary>
/// Layout-time CSS state: values that can only resolve during Measure/Arrange
/// (percentages, aspect-ratio, box-sizing, position/inset). Immutable snapshot — one
/// instance per CSS evaluation, whole-object Equals drives the apply diff. Mutable
/// per-layout caches are excluded from equality (a replaced snapshot always comes with
/// an InvalidateMeasure, so stale caches die with the old instance).
/// </summary>
internal sealed class CssLayoutState : IEquatable<CssLayoutState>
{
    // Sizes: percentage expressions and minimum-size auto keep their computed values
    // here; ordinary px/auto values use the regular DP channel and its CSS-layer sentinel.
    public CssLayoutLength Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight;
    public bool HasSizingKeywords => Width.IsSizing || Height.IsSizing || MinWidth.IsSizing ||
        MinHeight.IsSizing || MaxWidth.IsSizing || MaxHeight.IsSizing;

    // When a layout snapshot is needed, computed margin/padding edges are retained for
    // style queries. HasMargin/HasPadding say whether layout reads them instead of native DPs.
    public bool HasMargin;
    public CssLayoutLength MarginLeft, MarginTop, MarginRight, MarginBottom;
    public bool HasPadding;
    public CssLayoutLength PaddingLeft, PaddingTop, PaddingRight, PaddingBottom;

    /// <summary>Used width / height; NaN = unset or a degenerate specified pair.</summary>
    public double AspectRatio = double.NaN;
    // The computed value retains its number pair, including an optional auto keyword.
    public bool HasAspectRatio, AspectRatioAuto;
    public double AspectRatioNumerator, AspectRatioDenominator;

    public CssBoxSizing BoxSizing;
    public bool HasBoxSizing;

    public CssPositionMode Position;
    public bool IsOutOfFlow => Position is CssPositionMode.Absolute or CssPositionMode.Fixed;
    // Retained separately for computed style and CSS-wide inheritance.
    public CssPositionKeyword ComputedPosition;
    public CssLayoutLength InsetLeft, InsetTop, InsetRight, InsetBottom;

    /// <summary>Percentages that affect measurement (drive the arrange-basis correction).</summary>
    public bool UsesPercent;

    // ── Per-layout mutable caches (not part of equality) ──
    internal Thickness MeasureMarginCache;
    internal double ContainingWidthCache = double.NaN;
    internal Size? NativeMeasureContainingBlock;
    internal bool ArrangeCorrectionQueued;

    internal CssLayoutState CloneForLayout()
    {
        var copy = (CssLayoutState)MemberwiseClone();
        copy.MeasureMarginCache = default;
        copy.ContainingWidthCache = double.NaN;
        copy.NativeMeasureContainingBlock = null;
        copy.ArrangeCorrectionQueued = false;
        return copy;
    }

    internal void Seal()
    {
        UsesPercent =
            Width.IsPercent || Height.IsPercent ||
            MinWidth.IsPercent || MinHeight.IsPercent ||
            MaxWidth.IsPercent || MaxHeight.IsPercent ||
            MarginLeft.IsPercent || MarginTop.IsPercent || MarginRight.IsPercent || MarginBottom.IsPercent ||
            PaddingLeft.IsPercent || PaddingTop.IsPercent || PaddingRight.IsPercent || PaddingBottom.IsPercent;
    }

    public bool Equals(CssLayoutState? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Width.Equals(other.Width) && Height.Equals(other.Height) &&
               MinWidth.Equals(other.MinWidth) && MinHeight.Equals(other.MinHeight) &&
               MaxWidth.Equals(other.MaxWidth) && MaxHeight.Equals(other.MaxHeight) &&
               HasMargin == other.HasMargin &&
               MarginLeft.Equals(other.MarginLeft) && MarginTop.Equals(other.MarginTop) &&
               MarginRight.Equals(other.MarginRight) && MarginBottom.Equals(other.MarginBottom) &&
               HasPadding == other.HasPadding &&
               PaddingLeft.Equals(other.PaddingLeft) && PaddingTop.Equals(other.PaddingTop) &&
               PaddingRight.Equals(other.PaddingRight) && PaddingBottom.Equals(other.PaddingBottom) &&
               AspectRatio.Equals(other.AspectRatio) &&
               HasAspectRatio == other.HasAspectRatio &&
               AspectRatioAuto == other.AspectRatioAuto &&
               AspectRatioNumerator.Equals(other.AspectRatioNumerator) &&
               AspectRatioDenominator.Equals(other.AspectRatioDenominator) &&
               BoxSizing == other.BoxSizing &&
               HasBoxSizing == other.HasBoxSizing &&
               Position == other.Position &&
               ComputedPosition == other.ComputedPosition &&
               InsetLeft.Equals(other.InsetLeft) && InsetTop.Equals(other.InsetTop) &&
               InsetRight.Equals(other.InsetRight) && InsetBottom.Equals(other.InsetBottom);
    }

    public override bool Equals(object? obj) => obj is CssLayoutState other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Width, Height, AspectRatio, (int)BoxSizing, (int)Position, HasMargin, HasPadding, HasBoxSizing);
}
