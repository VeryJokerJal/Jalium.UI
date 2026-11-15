using Jalium.UI.Styling;
using Jalium.UI.Media;

namespace Jalium.UI;

public partial class FrameworkElement
{
    private CssIntrinsicSizingState? _cssIntrinsicSizing;

    private sealed class CssIntrinsicSizingState
    {
        internal byte Probing;
        internal Size? Available;
        internal readonly List<CssIntrinsicProbe> Probes = [];
    }
    private sealed record CssIntrinsicProbe(long Version, CssLayoutState? Layout, Size Containing,
        Size Constraint, byte Mask, CssFlexItemContext? Flex, CssIntrinsicPercentageContext? Contribution, Size Result);

    private bool CssProbingSize(bool horizontal) => ((_cssIntrinsicSizing?.Probing ?? 0) & (horizontal ? 1 : 2)) != 0;

    internal bool CssHasIntrinsicPreferredSize(bool horizontal)
    {
        if (CssFlexItemSizing.MainSize(this, horizontal) is not null) return false;
        var value = CssPresentedSize(horizontal ? CssLayoutSlotField.Width : CssLayoutSlotField.Height).SizingBasis;
        return value is { IsSizing: true, Kind: not CssLayoutLength.KindStretch } &&
            !HasLocalOrAnimatedValue(horizontal ? WidthProperty : HeightProperty);
    }

    private void CssIntrinsicSourceChanged(DependencyProperty property)
    {
        if (CssLayout?.HasSizingKeywords != true ||
            property != WidthProperty && property != HeightProperty && property != MinWidthProperty &&
            property != MinHeightProperty && property != MaxWidthProperty && property != MaxHeightProperty) return;
        _cssIntrinsicSizing?.Probes.Clear(); InvalidateMeasure();
    }

    // Used sizes are border-box dimensions. Non-quantitative keywords do not
    // acquire an extra content-box chrome adjustment after intrinsic measurement.
    internal double CssResolveSizing(CssLayoutLength value, bool horizontal, double basis,
        double fallback, Size? containingOverride = null, bool flexBasis = false, double? autoMinimum = null)
    {
        if (double.IsNaN(basis) || basis < 0) basis = double.PositiveInfinity;
        var state = _cssIntrinsicSizing ??= new();
        var containing = containingOverride ?? CssParentBox?.ContainingBlock ?? CssNativeBox?.ContainingBlock ??
            state.Available ?? new Size(CssLayout?.ContainingWidthCache ?? (horizontal ? basis : double.PositiveInfinity),
                horizontal ? double.PositiveInfinity : basis);
        containing = horizontal ? new(basis, containing.Height) : new(containing.Width, basis);
        containing = new(double.IsNaN(containing.Width) ? double.PositiveInfinity : containing.Width,
            double.IsNaN(containing.Height) ? double.PositiveInfinity : containing.Height);
        var available = state.Available ?? containing;
        var margin = CssBoxMetrics.Margin(this, containing.Width);
        var ownMarginX = CssParentBox is null ? margin.Left + margin.Right : 0;
        var ownMarginY = CssParentBox is null ? margin.Top + margin.Bottom : 0;
        available = new(Math.Max(0, available.Width - ownMarginX), Math.Max(0, available.Height - ownMarginY));
        var marginMain = horizontal ? margin.Left + margin.Right : margin.Top + margin.Bottom;
        var slot = horizontal ? available.Width : available.Height;
        var stretchBasis = CssLayout is { IsOutOfFlow: true } positioned
            ? CssAbsoluteLayout.ComputeStretchSize(positioned, containing, this) : containing;
        var fill = Math.Min(slot, Math.Max(0, (horizontal ? stretchBasis.Width : stretchBasis.Height) - marginMain));
        if (value.Kind == CssLayoutLength.KindStretch) return double.IsFinite(fill)
            ? Math.Max(horizontal ? GetContentBoxChromeX() : GetContentBoxChromeY(), fill) : fallback;

        Size Probe(bool minimum)
        {
            var replaced = CssFlexItemSizing.IsReplaced(this);
            var main = minimum && horizontal && !replaced ? 0 : double.PositiveInfinity;
            var cross = replaced ? double.PositiveInfinity : horizontal ? available.Height : available.Width;
            var constraint = horizontal ? new Size(main, cross) : new Size(cross, main);
            return CssProbeIntrinsic(constraint, containing, horizontal, minimum);
        }
        double Axis(Size size) => horizontal ? size.Width : size.Height;
        if (value.Calculation is { } calculation)
        {
            var chrome = horizontal ? GetContentBoxChromeX() : GetContentBoxChromeY();
            var contentBox = CssLayout?.BoxSizing == CssBoxSizing.ContentBox;
            double Quantity(double border) => contentBox ? Math.Max(0, border - chrome) : border;
            double AutoBasis()
            {
                if (fallback == 0) return autoMinimum is { } autoBound ? Quantity(autoBound) : 0;
                if (flexBasis)
                {
                    var preferred = horizontal ? CssPreferredWidth(containing.Width) : CssPreferredHeight(containing.Height);
                    return Quantity(double.IsFinite(preferred) ? preferred : Axis(Probe(false)));
                }
                if (CssLayout is { IsOutOfFlow: true } absolute)
                {
                    var start = horizontal ? absolute.InsetLeft : absolute.InsetTop;
                    var end = horizontal ? absolute.InsetRight : absolute.InsetBottom;
                    if (start.IsSet && !start.IsAuto && end.IsSet && !end.IsAuto && double.IsFinite(fill))
                        return Quantity(fill);
                }
                var otherValue = (horizontal ? CssLayout?.Height : CssLayout?.Width)?.SizingBasis;
                if (CssLayout is { AspectRatio: > 0 } ratio &&
                    otherValue is not { Kind: CssLayoutLength.KindAuto } &&
                    otherValue?.IsSizing != true)
                {
                    var other = horizontal ? CssPreferredHeight(containing.Height) : CssPreferredWidth(containing.Width);
                    if (double.IsFinite(other))
                    {
                        var otherChrome = horizontal ? GetContentBoxChromeY() : GetContentBoxChromeX();
                        if (contentBox) other = Math.Max(0, other - otherChrome);
                        return horizontal ? other * ratio.AspectRatio : other / ratio.AspectRatio;
                    }
                }
                if (!horizontal)
                {
                    var stretched = CssFlexSizing?.Current is { Horizontal: true, DefiniteHeight: true } ||
                        CssContentsLayout.BoxParent(this) is FrameworkElement { CssDisplayMode: CssDisplayMode.Grid } &&
                        CssParentAlignment?.Vertical is CssBoxAlignment.Normal or CssBoxAlignment.Stretch;
                    return Quantity(stretched && double.IsFinite(fill) ? fill : Axis(Probe(false)));
                }
                if (CssIntrinsicMeasure && available.Width == 0) return Quantity(Axis(Probe(true)));
                var shrink = CssInlineOuter || CssLayout?.IsOutOfFlow == true ||
                    CssFloatProperties.ActiveSide(this) != CssFloatSide.None ||
                    CssParentAlignment?.Horizontal is CssBoxAlignment.Start or CssBoxAlignment.End or CssBoxAlignment.Center;
                if (!shrink && double.IsFinite(fill)) return Quantity(fill);
                var minimum = Axis(Probe(true)); var maximum = Math.Max(minimum, Axis(Probe(false)));
                return Quantity(Math.Clamp(double.IsNaN(fill) ? maximum : fill, minimum, maximum));
            }
            double ResolveBasis(CssLayoutLength calcBasis)
            {
                double size;
                if (calcBasis.IsAuto) size = AutoBasis();
                else if (calcBasis.Kind == CssLayoutLength.KindContent) size = Quantity(Axis(Probe(false)));
                else if (calcBasis.IsSizing)
                {
                    size = CssResolveSizing(calcBasis, horizontal, basis, fallback, containingOverride);
                    size = double.IsNaN(size) ? flexBasis ? Quantity(Axis(Probe(false))) : AutoBasis() : Quantity(size);
                }
                else
                {
                    var cyclic = CssIntrinsicPercentage.CyclicSize(this, calcBasis, horizontal);
                    size = cyclic
                        ? CssIntrinsicPercentage.Resolve(this, calcBasis, horizontal, double.NaN, minimumBound: fallback == 0)
                        : calcBasis.Resolve(fallback == 0 ? CssIntrinsicPercentage.MinimumBasis(basis) : basis, double.NaN);
                    if (double.IsNaN(size)) size = double.IsPositiveInfinity(fallback) ? fallback
                        : flexBasis ? Quantity(Axis(Probe(false))) : AutoBasis();
                }
                return size;
            }
            var calcPercentBasis = CssIntrinsicPercentage.Cyclic(this, horizontal)
                ? (fallback == 0 ? 0 : double.NaN)
                : (fallback == 0 ? CssIntrinsicPercentage.MinimumBasis(basis) : basis);
            var result = calculation.Evaluate(ResolveBasis, calcPercentBasis, fallback);
            return double.IsNaN(result) ? fallback : Math.Max(chrome, Math.Max(0, result) + (contentBox ? chrome : 0));
        }
        if (value.Kind == CssLayoutLength.KindMinContent) return Axis(Probe(true));
        if (value.Kind == CssLayoutLength.KindMaxContent) return Axis(Probe(false));
        var min = Axis(Probe(true)); var max = Math.Max(min, Axis(Probe(false)));
        if (value.Kind == CssLayoutLength.KindFitFunction)
        {
            var limit = CssIntrinsicPercentage.CyclicSize(this, value.FitArgument, horizontal)
                ? CssIntrinsicPercentage.Resolve(this, value.FitArgument, horizontal, double.PositiveInfinity, minimumBound: fallback == 0)
                : value.FitArgument.Resolve(fallback == 0 ? CssIntrinsicPercentage.MinimumBasis(basis) : basis, double.PositiveInfinity);
            if (double.IsFinite(limit))
            {
                limit = Math.Max(0, limit);
                if (CssLayout?.BoxSizing == CssBoxSizing.ContentBox)
                    limit += horizontal ? GetContentBoxChromeX() : GetContentBoxChromeY();
            }
            fill = limit;
        }
        else if (CssIntrinsicMeasure && horizontal)
            fill = available.Width == 0 ? min : double.IsPositiveInfinity(available.Width) ? max : fill;
        return Math.Clamp(double.IsNaN(fill) ? max : fill, min, max);
    }

    private Size CssProbeIntrinsic(Size constraint, Size containing, bool horizontal, bool minimum)
    {
        var state = _cssIntrinsicSizing ??= new();
        var mask = (byte)(state.Probing | (horizontal ? 1 : 2));
        var version = CssIntrinsicInputVersion(this);
        var flexContext = CssFlexSizing?.Current;
        state.Probes.RemoveAll(p => p.Version != version);
        foreach (var cached in state.Probes)
            if (cached.Layout == CssLayout && cached.Containing == containing && cached.Constraint == constraint && cached.Mask == mask && cached.Flex == flexContext && cached.Contribution == CssContribution)
                return cached.Result;
        var parent = CssParentBox; var native = CssNativeBox; var intrinsic = CssIntrinsicMeasure;
        var oldMask = state.Probing; var oldAvailable = state.Available; var last = LastCssMeasureBox;
        var layout = CssLayout; var oldWidth = layout?.ContainingWidthCache;
        var oldNative = layout?.NativeMeasureContainingBlock; var oldMargin = layout?.MeasureMarginCache;
        var oldFlex = CssFlexSizing?.Measured;
        var contribution = CssContribution;
        Size result;
        state.Probing = mask; CssIntrinsicMeasure = true; CssParentBox = new(containing); CssNativeBox = null;
        try
        {
            using var scope = new CssIntrinsicPercentage.Scope(this, horizontal, minimum, naturalRoot: true);
            result = MeasureCore(constraint);
            if (!horizontal) result = new(result.Width, Math.Max(0, result.Height - CssTableLayout.CaptionHeight(this)));
        }
        finally
        {
            state.Probing = oldMask; state.Available = oldAvailable;
            CssParentBox = parent; CssNativeBox = native; CssIntrinsicMeasure = intrinsic; LastCssMeasureBox = last;
            if (layout is not null)
            {
                layout.ContainingWidthCache = oldWidth!.Value;
                layout.NativeMeasureContainingBlock = oldNative; layout.MeasureMarginCache = oldMargin!.Value;
            }
            if (CssFlexSizing is { } flex) flex.Measured = oldFlex;
            InvalidateCssAllocation();
        }
        if (version == CssIntrinsicInputVersion(this))
        {
            if (state.Probes.Count == 8) state.Probes.RemoveAt(0);
            state.Probes.Add(new(version, layout, containing, constraint, mask, flexContext, contribution, result));
        }
        return result;
    }

    private static long CssIntrinsicInputVersion(Visual visual)
    {
        var version = visual is UIElement element ? element.MeasureInputVersion : 0;
        for (var i = 0; i < visual.InternalVisualChildrenCount; i++)
            if (visual.InternalGetVisualChild(i) is { } child) version = Math.Max(version, CssIntrinsicInputVersion(child));
        return version;
    }
}
