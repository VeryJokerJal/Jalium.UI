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
    Absolute,
}

/// <summary>A layout-time length: unset, resolved pixels, or a percentage fraction.</summary>
internal readonly struct CssLayoutLength : IEquatable<CssLayoutLength>
{
    public const byte KindUnset = 0;
    public const byte KindPx = 1;
    public const byte KindPercent = 2;

    public readonly double Value;
    public readonly byte Kind;

    private CssLayoutLength(double value, byte kind)
    {
        Value = value;
        Kind = kind;
    }

    public static CssLayoutLength Unset => default;

    public static CssLayoutLength Px(double value) => new(value, KindPx);

    /// <summary>fraction is 0-1 (50% ⇒ 0.5).</summary>
    public static CssLayoutLength Percent(double fraction) => new(fraction, KindPercent);

    public bool IsSet => Kind != KindUnset;

    public bool IsPercent => Kind == KindPercent;

    /// <summary>
    /// Resolves against the containing-block basis. A percentage against an infinite or
    /// invalid basis degrades to <paramref name="infinityFallback"/> (auto=NaN, 0, or +∞
    /// depending on the property).
    /// </summary>
    public double Resolve(double basis, double infinityFallback)
    {
        switch (Kind)
        {
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

    public bool Equals(CssLayoutLength other) => Kind == other.Kind && Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is CssLayoutLength other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, Kind);
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
    // Sizes: occupied only when the declaration contains a percentage; px/auto stay on
    // the regular DP channel (with a sentinel in the CSS layer, see the engine wiring).
    public CssLayoutLength Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight;

    // Margin/padding: the whole Thickness migrates here when any edge is a percentage
    // (absolute edges arrive pre-resolved as Px).
    public bool HasMargin;
    public CssLayoutLength MarginLeft, MarginTop, MarginRight, MarginBottom;
    public bool HasPadding;
    public CssLayoutLength PaddingLeft, PaddingTop, PaddingRight, PaddingBottom;

    /// <summary>width / height; NaN = unset.</summary>
    public double AspectRatio = double.NaN;

    public CssBoxSizing BoxSizing;

    public CssPositionMode Position;
    public CssLayoutLength InsetLeft, InsetTop, InsetRight, InsetBottom;

    /// <summary>Precomputed: any percentage anywhere (drives the arrange-basis correction).</summary>
    public bool UsesPercent;

    // ── Per-layout mutable caches (not part of equality) ──
    internal Thickness MeasureMarginCache;
    internal bool ArrangeCorrectionQueued;

    internal void Seal()
    {
        UsesPercent =
            Width.IsPercent || Height.IsPercent ||
            MinWidth.IsPercent || MinHeight.IsPercent ||
            MaxWidth.IsPercent || MaxHeight.IsPercent ||
            MarginLeft.IsPercent || MarginTop.IsPercent || MarginRight.IsPercent || MarginBottom.IsPercent ||
            PaddingLeft.IsPercent || PaddingTop.IsPercent || PaddingRight.IsPercent || PaddingBottom.IsPercent ||
            InsetLeft.IsPercent || InsetTop.IsPercent || InsetRight.IsPercent || InsetBottom.IsPercent;
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
               BoxSizing == other.BoxSizing &&
               Position == other.Position &&
               InsetLeft.Equals(other.InsetLeft) && InsetTop.Equals(other.InsetTop) &&
               InsetRight.Equals(other.InsetRight) && InsetBottom.Equals(other.InsetBottom);
    }

    public override bool Equals(object? obj) => obj is CssLayoutState other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Width, Height, AspectRatio, (int)BoxSizing, (int)Position, HasMargin, HasPadding);
}
