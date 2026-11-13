using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class FlexPanel
{
    private static double MainEdges(Thickness value, FlexAxis axis)
        => axis.Horizontal ? value.Left + value.Right : value.Top + value.Bottom;

    private static bool AutomaticMainMinimum(FrameworkElement element, FlexAxis axis)
    {
        var property = axis.Horizontal ? MinWidthProperty : MinHeightProperty;
        if (element.HasLocalOrAnimatedValue(property)) return false;
        var minimum = (CssLayoutLength?)element.CssPresentedSize(axis.Horizontal ? CssLayoutSlotField.MinWidth : CssLayoutSlotField.MinHeight);
        if (minimum is { IsSet: true, IsAuto: false }) return false;
        // Absolute lengths, including an authored zero, live in the DP layer.
        if (minimum is not { IsAuto: true } && element.HasValueAboveInherited(property)) return false;
        return HasContentAutomaticMinimum(element, axis);
    }

    private static bool HasContentAutomaticMinimum(FrameworkElement element, FlexAxis axis)
    {
        var overflow = CssOverflowProperties.Get(element);
        return (axis.Horizontal ? overflow?.X : overflow?.Y) is null or CssOverflowMode.Visible or CssOverflowMode.Clip;
    }

    private static double IntrinsicMain(UIElement child, FlexAxis axis, Size containingBlock, bool minimum)
    {
        var replaced = child is FrameworkElement element && CssFlexItemSizing.IsReplaced(element);
        var main = minimum && axis.Horizontal && !replaced ? 0 : double.PositiveInfinity;
        var cross = minimum && replaced ? double.PositiveInfinity : axis.Cross(containingBlock);
        return axis.Main(CssFlexItemSizing.Measure(child, axis.Size(main, cross),
            containingBlock, new(axis.Horizontal, true, true, MinimumContribution: minimum), intrinsic: true));
    }

    private static double ContentMainMinimum(FrameworkElement element, FlexAxis axis,
        Size containingBlock, double content, double preferred, double maximum)
    {
        if (element.CssLayout is { AspectRatio: > 0 } state)
        {
            var ratio = axis.Horizontal ? state.AspectRatio : 1 / state.AspectRatio;
            var cross = axis.Horizontal ? element.CssPreferredHeight(containingBlock.Height)
                : element.CssPreferredWidth(containingBlock.Width);
            var bounds = axis.Horizontal ? element.CssHeightBounds(containingBlock.Height)
                : element.CssWidthBounds(containingBlock.Width);
            content = Math.Clamp(content, bounds.Min * ratio, bounds.Max * ratio);
            if (double.IsFinite(cross))
            {
                var transferred = Math.Clamp(cross, bounds.Min, bounds.Max) * ratio;
                content = CssFlexItemSizing.IsReplaced(element)
                    ? Math.Min(content, transferred) : Math.Max(content, transferred);
            }
        }
        if (double.IsFinite(preferred)) content = Math.Min(content, preferred);
        return Math.Min(content, maximum);
    }

    private FlexItem SizeItem(UIElement child, FlexAxis axis, Size containingBlock,
        double crossAvailable, FlexAlign alignItems)
    {
        var mainAvailable = axis.Main(containingBlock);
        var element = child as FrameworkElement;
        var basis = ResolveBasis(child, element, mainAvailable, axis.Horizontal, containingBlock);
        var intrinsicBasis = CssSizingPresentation.FlexBasis(child) is CssLayoutLength { IsSizing: true };
        var contentBasis = !child.HasLocalOrAnimatedValue(BasisProperty) &&
            ((CssSizingPresentation.IsFlexBasisAnimated(element)
                ? CssSizingPresentation.FlexBasis(child) is CssLayoutLength { Kind: CssLayoutLength.KindContent }
                : (bool)child.GetValue(CssContentBasisProperty)!) ||
             CssSizingPresentation.FlexBasis(child) is CssLength { UsesPercent: true } &&
             !double.IsFinite(mainAvailable) ||
             CssSizingPresentation.FlexBasis(child) is CssLayoutLength { Kind: CssLayoutLength.KindStretch } &&
             !double.IsFinite(mainAvailable) ||
             CssSizingPresentation.FlexBasis(child) is CssLayoutLength { Calculation: { } calculation } &&
             !double.IsFinite(mainAvailable) &&
             (calculation.SizingBasis.IsPercent || calculation.SizingBasis.Kind == CssLayoutLength.KindStretch));
        var natural = CssFlexItemSizing.Measure(child, axis.Size(double.PositiveInfinity, crossAvailable),
            containingBlock, new(axis.Horizontal, contentBasis, true), intrinsic: contentBasis);
        var margin = element is null ? 0 : MainEdges(CssBoxMetrics.Margin(element, containingBlock.Width), axis);
        var chrome = element is null ? 0 : MainEdges(CssBoxMetrics.ContentInsets(element, containingBlock.Width), axis);
        if (!axis.Horizontal && element is not null) chrome += CssTableLayout.CaptionHeight(element);
        var borderBase = double.IsNaN(basis) || contentBasis ? Math.Max(0, axis.Main(natural) - margin) : basis;
        if (!double.IsNaN(basis) && !contentBasis && !intrinsicBasis && !child.HasLocalOrAnimatedValue(BasisProperty) &&
            element?.CssLayout?.BoxSizing == CssBoxSizing.ContentBox) borderBase += chrome;
        var minimum = chrome;
        var maximum = double.PositiveInfinity;
        if (element is not null)
        {
            var bounds = axis.Horizontal ? element.CssWidthBounds(containingBlock.Width)
                : element.CssHeightBounds(containingBlock.Height);
            minimum = Math.Max(minimum, bounds.Min);
            maximum = Math.Max(minimum, bounds.Max);
            var table = element.CssDisplayMode == CssDisplayMode.Table;
            var minimumValue = (CssLayoutLength?)element.CssPresentedSize(axis.Horizontal ? CssLayoutSlotField.MinWidth : CssLayoutSlotField.MinHeight);
            var calculatedAuto = minimumValue?.Calculation?.SizingBasis.IsAuto == true &&
                !element.HasLocalOrAnimatedValue(axis.Horizontal ? MinWidthProperty : MinHeightProperty);
            if (table || AutomaticMainMinimum(element, axis) || calculatedAuto)
            {
                var intrinsic = Math.Max(0, IntrinsicMain(child, axis, containingBlock, minimum: true) - margin);
                if (table) minimum = Math.Max(minimum, intrinsic);
                var preferred = axis.Horizontal ? element.CssPreferredWidth(containingBlock.Width)
                    : element.CssPreferredHeight(containingBlock.Height);
                var automatic = HasContentAutomaticMinimum(element, axis)
                    ? ContentMainMinimum(element, axis, containingBlock, intrinsic, preferred, maximum) : 0;
                if (calculatedAuto)
                    minimum = Math.Max(table ? minimum : chrome, element.CssResolveSizing(minimumValue!.Value,
                        axis.Horizontal, axis.Main(containingBlock), 0, containingBlock, autoMinimum: automatic));
                else if (AutomaticMainMinimum(element, axis)) minimum = Math.Max(minimum, automatic);
                maximum = Math.Max(minimum, maximum);
            }
        }
        var align = GetAlignSelf(child);
        if (align == FlexAlign.Auto) align = alignItems;
        var (mainStart, mainEnd, crossStart, crossEnd) = AutoMargins(element, axis);
        var owner = _cssLayoutHost ?? this;
        var behavior = CssSizingPresentation.FlexBasis(child) is CssLayoutLength typedBasis ? typedBasis.SizingBasis : default;
        var intrinsicHeightBasis = !child.HasLocalOrAnimatedValue(BasisProperty) && intrinsicBasis &&
            (behavior.IsSizing && behavior.Kind != CssLayoutLength.KindStretch ||
             behavior.IsPercent && !double.IsFinite(containingBlock.Height) ||
             behavior.IsAuto && element?.CssHasDefiniteHeight(containingBlock.Height) != true);
        var ownDefiniteHeight = element?.CssHasDefiniteHeight(containingBlock.Height) == true;
        var definiteHeight = axis.Horizontal
            ? ownDefiniteHeight || align == FlexAlign.Stretch && !crossStart && !crossEnd
            : owner.CssHasDefiniteHeight(containingBlock.Height) ||
              !contentBasis && !intrinsicHeightBasis && double.IsFinite(basis) ||
              double.IsNaN(basis) && !contentBasis && ownDefiniteHeight;
        return new FlexItem
        {
            Element = child, Grow = GetGrow(child), Shrink = GetShrink(child),
            FlexBaseMain = borderBase + margin, InnerBaseMain = Math.Max(0, borderBase - chrome),
            BaseMain = Math.Clamp(borderBase, minimum, maximum) + margin,
            MinMain = minimum + margin, MaxMain = maximum + margin, MainMargin = margin,
            Align = align, AutoBasis = double.IsNaN(basis),
            DefiniteHeight = definiteHeight, OwnDefiniteHeight = ownDefiniteHeight,
            Collapsed = CssDisplayProperties.IsCollapsedFlexItem(child),
            AutoMainStart = mainStart, AutoMainEnd = mainEnd, AutoCrossStart = crossStart, AutoCrossEnd = crossEnd,
        };
    }

    private void MeasureItem(in FlexItem item, FlexAxis axis, double crossAvailable, Size containingBlock,
        bool intrinsic = false)
    {
        // A zero inline constraint requests the min-content contribution. Replaced
        // elements contribute their intrinsic size rather than scaling it to zero.
        if (intrinsic && !axis.Horizontal && crossAvailable == 0 &&
            item.Element is FrameworkElement element && CssFlexItemSizing.IsReplaced(element))
            crossAvailable = double.PositiveInfinity;
        var singleLineDefiniteCross = axis.Horizontal && Wrap == FlexWrap.NoWrap && double.IsFinite(containingBlock.Height);
        var definiteHeight = axis.Horizontal && !singleLineDefiniteCross ? item.OwnDefiniteHeight : item.DefiniteHeight;
        if (axis.Horizontal && !singleLineDefiniteCross && !item.OwnDefiniteHeight)
            crossAvailable = double.PositiveInfinity;
        var allocation = new CssFlexItemContext(axis.Horizontal, false, false,
            Math.Max(0, item.TargetMain - item.MainMargin), DefiniteHeight: definiteHeight);
        if (!axis.Horizontal && item.Align != FlexAlign.Stretch && item.Element is FrameworkElement crossElement &&
            !crossElement.HasLocalOrAnimatedValue(HorizontalAlignmentProperty) &&
            double.IsNaN(crossElement.CssPreferredWidth(containingBlock.Width)))
        {
            var minimum = CssFlexItemSizing.Measure(item.Element, axis.Size(Math.Max(0, item.TargetMain), 0),
                containingBlock, allocation, intrinsic: true, intrinsicHorizontal: true, intrinsicMinimum: true).Width;
            var maximum = Math.Max(minimum, CssFlexItemSizing.Measure(item.Element,
                axis.Size(Math.Max(0, item.TargetMain), double.PositiveInfinity), containingBlock,
                allocation, intrinsic: true, intrinsicHorizontal: true).Width);
            crossAvailable = Math.Clamp(crossAvailable, minimum, maximum);
        }
        CssFlexItemSizing.Measure(item.Element, axis.Size(Math.Max(0, item.TargetMain), crossAvailable),
            containingBlock, allocation, intrinsic);
    }
}
