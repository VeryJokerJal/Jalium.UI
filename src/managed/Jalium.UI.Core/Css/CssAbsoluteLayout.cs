using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

/// <summary>
/// Position:absolute slot computation (headless-testable). The slot is
/// the child's margin-box rectangle inside the panel; the child's own Arrange applies
/// margin/alignment within it.
/// </summary>
internal static class CssAbsoluteLayout
{
    // Content-based fit/stretch sizes use the space remaining after every
    // specified inset, including a single inset opposite an auto edge.
    internal static Size ComputeStretchSize(CssLayoutState layout, Size containing, FrameworkElement child)
    {
        double Inset(int edge, CssLayoutLength value, double basis)
        {
            var effective = EffectiveInset(child, edge, value);
            return effective.IsSet && !effective.IsAuto ? effective.Resolve(basis, 0) : 0;
        }
        return new(Math.Max(0, containing.Width - Inset(0, layout.InsetLeft, containing.Width) - Inset(2, layout.InsetRight, containing.Width)),
            Math.Max(0, containing.Height - Inset(1, layout.InsetTop, containing.Height) - Inset(3, layout.InsetBottom, containing.Height)));
    }

    /// <summary>
    /// Pre-narrows the measure constraint: when both edges of an axis are set and the
    /// panel size is finite, the child's slot extent is known up front.
    /// </summary>
    public static Size ComputeMeasureConstraint(CssLayoutState layout, Size panelAvailable,
        FrameworkElement? child = null)
    {
        var leftInset = EffectiveInset(child, 0, layout.InsetLeft);
        var topInset = EffectiveInset(child, 1, layout.InsetTop);
        var rightInset = EffectiveInset(child, 2, layout.InsetRight);
        var bottomInset = EffectiveInset(child, 3, layout.InsetBottom);
        var width = panelAvailable.Width;
        if (!double.IsInfinity(width) &&
            leftInset.IsSet && !leftInset.IsAuto &&
            rightInset.IsSet && !rightInset.IsAuto)
        {
            var left = leftInset.Resolve(width, 0);
            var right = rightInset.Resolve(width, 0);
            width = Math.Max(0, width - left - right);
        }

        var height = panelAvailable.Height;
        if (!double.IsInfinity(height) &&
            topInset.IsSet && !topInset.IsAuto &&
            bottomInset.IsSet && !bottomInset.IsAuto)
        {
            var top = topInset.Resolve(height, 0);
            var bottom = bottomInset.Resolve(height, 0);
            height = Math.Max(0, height - top - bottom);
        }

        return new Size(width, height);
    }

    internal static bool HasBothVerticalInsets(CssLayoutState layout, FrameworkElement? child = null)
    {
        var top = EffectiveInset(child, 1, layout.InsetTop);
        var bottom = EffectiveInset(child, 3, layout.InsetBottom);
        return top.IsSet && !top.IsAuto && bottom.IsSet && !bottom.IsAuto;
    }

    internal static bool HasBothHorizontalInsets(CssLayoutState layout, FrameworkElement? child = null)
    {
        var left = EffectiveInset(child, 0, layout.InsetLeft);
        var right = EffectiveInset(child, 2, layout.InsetRight);
        return left.IsSet && !left.IsAuto && right.IsSet && !right.IsAuto;
    }

    /// <summary>
    /// Solves the inset equations for one absolutely positioned child.
    /// <paramref name="effectiveWidth"/>/<paramref name="effectiveHeight"/> are the
    /// child's determinate sizes (explicit DP or CSS %, resolved against the panel), NaN = auto.
    /// </summary>
    public static Rect ComputeSlot(
        CssLayoutState layout, Size panelSize, Size childDesired,
        double effectiveWidth, double effectiveHeight, FrameworkElement? child = null, bool? preferInlineEnd = null,
        Point? staticParentOuterInContaining = null)
    {
        if (child is not null && CssAnchorPositioning.HasAnchorConfiguration(child))
        {
            var placement = CssAnchorPositioning.EvaluatePlacement(
                child,
                child.VisualParent as UIElement,
                layout,
                panelSize,
                childDesired,
                effectiveWidth,
                effectiveHeight);
            return placement.Slot;
        }

        return ComputeSlotCore(
            layout, panelSize, childDesired, effectiveWidth, effectiveHeight,
            child, preferInlineEnd, staticParentOuterInContaining);
    }

    internal static Rect ComputeSlotCore(
        CssLayoutState layout, Size panelSize, Size childDesired,
        double effectiveWidth, double effectiveHeight, FrameworkElement? child = null, bool? preferInlineEnd = null,
        Point? staticParentOuterInContaining = null)
    {
        var widthBounds = child?.CssWidthBounds(panelSize.Width) ?? (0d, double.PositiveInfinity);
        var heightBounds = child?.CssHeightBounds(panelSize.Height) ?? (0d, double.PositiveInfinity);
        var margin = child is null ? default : CssBoxMetrics.Margin(child, panelSize.Width);
        bool hasLocalMargin = child is not null && child.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty);
        bool autoLeftMargin = !hasLocalMargin && layout.HasMargin && layout.MarginLeft.IsAuto;
        bool autoRightMargin = !hasLocalMargin && layout.HasMargin && layout.MarginRight.IsAuto;
        bool autoTopMargin = !hasLocalMargin && layout.HasMargin && layout.MarginTop.IsAuto;
        bool autoBottomMargin = !hasLocalMargin && layout.HasMargin && layout.MarginBottom.IsAuto;

        var leftInset = EffectiveInset(child, 0, layout.InsetLeft);
        var rightInset = EffectiveInset(child, 2, layout.InsetRight);
        var topInset = EffectiveInset(child, 1, layout.InsetTop);
        var bottomInset = EffectiveInset(child, 3, layout.InsetBottom);

        bool rtl = preferInlineEnd ?? child?.VisualParent is FrameworkElement { FlowDirection: FlowDirection.RightToLeft };

        var (x, width) = SolveAxis(
            leftInset, rightInset, panelSize.Width,
            effectiveWidth, childDesired.Width, widthBounds, margin.Left + margin.Right,
            rtl, autoLeftMargin, autoRightMargin, isHorizontal: true,
            child, panelSize, staticParentOuterInContaining);
        var (y, height) = SolveAxis(
            topInset, bottomInset, panelSize.Height,
            effectiveHeight, childDesired.Height, heightBounds, margin.Top + margin.Bottom,
            false, autoTopMargin, autoBottomMargin, isHorizontal: false,
            child, panelSize, staticParentOuterInContaining);
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    private static CssLayoutLength EffectiveInset(FrameworkElement? child, int edge, CssLayoutLength css)
    {
        var property = CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(edge);
        if (child is null || property is null || !child.HasLocalOrAnimatedValue(property)) return css;
        return child.GetValue(property) is double pixels && double.IsFinite(pixels)
            ? CssLayoutLength.Px(pixels) : CssLayoutLength.Auto;
    }

    private static (double pos, double extent) SolveAxis(
        CssLayoutLength start, CssLayoutLength end, double panelExtent,
        double determinate, double desired, (double Min, double Max) bounds, double marginExtent,
        bool preferEnd, bool autoStartMargin, bool autoEndMargin, bool isHorizontal,
        FrameworkElement? child, Size panelSize, Point? staticParentOuterInContaining)
    {
        var hasStart = start.IsSet && !start.IsAuto;
        var hasEnd = end.IsSet && !end.IsAuto;
        var startPx = hasStart ? start.Resolve(panelExtent, 0) : 0;
        var endPx = hasEnd ? end.Resolve(panelExtent, 0) : 0;

        if (!double.IsNaN(determinate))
        {
            determinate = Math.Clamp(determinate, bounds.Min, bounds.Max) + marginExtent;
            if (hasStart && hasEnd)
            {
                if (autoStartMargin || autoEndMargin)
                {
                    double remaining = panelExtent - startPx - endPx - determinate;
                    if (autoStartMargin && autoEndMargin)
                    {
                        if (remaining >= 0 || !isHorizontal)
                            return (startPx + remaining * 0.5, determinate);
                        return preferEnd
                            ? (panelExtent - endPx - determinate, determinate)
                            : (startPx, determinate);
                    }
                    if (autoStartMargin)
                        return (panelExtent - endPx - determinate, determinate);
                    return (startPx, determinate);
                }

                if (preferEnd)
                    return (panelExtent - endPx - determinate, determinate);
                return (startPx, determinate);
            }

            if (hasStart)
            {
                return (startPx, determinate);
            }

            if (hasEnd)
            {
                return (panelExtent - endPx - determinate, determinate);
            }

            return (ResolveStaticAxisPosition(child, determinate, panelSize, preferEnd, isHorizontal, staticParentOuterInContaining), determinate);
        }

        if (hasStart && hasEnd)
        {
            var stretched = Math.Clamp(Math.Max(0, panelExtent - startPx - endPx),
                bounds.Min + marginExtent, bounds.Max + marginExtent);
            if (autoStartMargin || autoEndMargin)
            {
                double extra = Math.Max(0, panelExtent - startPx - endPx - stretched);
                if (autoStartMargin && autoEndMargin)
                    return (startPx + extra * 0.5, stretched);
                if (autoStartMargin)
                    return (panelExtent - endPx - stretched, stretched);
                return (startPx, stretched);
            }
            if (preferEnd)
                return (panelExtent - endPx - stretched, stretched);
            return (startPx, stretched);
        }

        if (hasStart)
        {
            return (startPx, desired);
        }

        if (hasEnd)
        {
            return (panelExtent - endPx - desired, desired);
        }

        return (ResolveStaticAxisPosition(child, desired, panelSize, preferEnd, isHorizontal, staticParentOuterInContaining), desired);
    }

    private static double ResolveStaticAxisPosition(
        FrameworkElement? child,
        double childExtent,
        Size panelSize,
        bool preferEnd,
        bool isHorizontal,
        Point? staticParentOuterInContaining)
    {
        if (child is null)
            return isHorizontal && preferEnd ? Math.Max(0, panelSize.Width - childExtent) : 0;

        var boxParent = (CssContentsLayout.BoxParent(child) ?? child.VisualParent) as FrameworkElement;
        if (boxParent is null)
            return isHorizontal && preferEnd ? Math.Max(0, panelSize.Width - childExtent) : 0;

        bool parentRtl = boxParent.FlowDirection == FlowDirection.RightToLeft;
        var parentOuterSize = boxParent.RenderSize;
        if (parentOuterSize.Width <= 0 && parentOuterSize.Height <= 0)
            parentOuterSize = boxParent.CssArrangeSizeForChildren;
        if (parentOuterSize.Width <= 0 && parentOuterSize.Height <= 0)
            parentOuterSize = panelSize;

        var (pBorder, pPadding) = CssBoxMetrics.BackgroundInsets(
            boxParent,
            boxParent.CssLayout?.ContainingWidthCache ?? parentOuterSize.Width);

        Point parentOuterOffset = staticParentOuterInContaining ?? new Point(-pBorder.Left, -pBorder.Top);
        double contentStartX = parentOuterOffset.X + pBorder.Left + pPadding.Left;
        double contentStartY = parentOuterOffset.Y + pBorder.Top + pPadding.Top;
        double contentWidth = Math.Max(0, parentOuterSize.Width - pBorder.Left - pBorder.Right - pPadding.Left - pPadding.Right);
        double contentHeight = Math.Max(0, parentOuterSize.Height - pBorder.Top - pBorder.Bottom - pPadding.Top - pPadding.Bottom);

        bool isFlex = boxParent is FlexPanel || boxParent.CssDisplayMode == CssDisplayMode.Flex;
        bool isGrid = boxParent is Grid || boxParent.CssDisplayMode == CssDisplayMode.Grid;

        if (isFlex)
        {
            var dir = (FlexDirection)(boxParent.GetValue(FlexPanel.DirectionProperty) ?? FlexDirection.Row);
            var justify = (FlexJustify)(boxParent.GetValue(FlexPanel.JustifyContentProperty) ?? FlexJustify.FlexStart);
            var align = FlexPanel.GetAlignSelf(child);
            if (align == FlexAlign.Auto)
                align = (FlexAlign)(boxParent.GetValue(FlexPanel.AlignItemsProperty) ?? FlexAlign.Stretch);

            bool mainIsHorizontal = dir is FlexDirection.Row or FlexDirection.RowReverse;
            if (isHorizontal)
            {
                if (mainIsHorizontal)
                {
                    bool reverse = (dir == FlexDirection.RowReverse) ^ parentRtl;
                    return contentStartX + ResolveFlexMainOffset(justify, contentWidth, childExtent, reverse);
                }
                else
                {
                    return contentStartX + ResolveFlexCrossOffset(align, contentWidth, childExtent, parentRtl);
                }
            }
            else
            {
                if (!mainIsHorizontal)
                {
                    bool reverse = dir == FlexDirection.ColumnReverse;
                    return contentStartY + ResolveFlexMainOffset(justify, contentHeight, childExtent, reverse);
                }
                else
                {
                    return contentStartY + ResolveFlexCrossOffset(align, contentHeight, childExtent, false);
                }
            }
        }

        if (isGrid)
        {
            if (isHorizontal)
            {
                var justifySelf = (CssBoxAlignment)(child.GetValue(CssGridProperties.JustifySelfProperty) ?? CssBoxAlignment.Auto);
                if (justifySelf is CssBoxAlignment.Auto or CssBoxAlignment.Normal)
                    justifySelf = (CssBoxAlignment)(boxParent.GetValue(CssGridProperties.JustifyItemsProperty) ?? CssBoxAlignment.Normal);
                if (justifySelf is CssBoxAlignment.Auto or CssBoxAlignment.Normal)
                    justifySelf = (CssBoxAlignment)(boxParent.GetValue(CssGridProperties.JustifyContentProperty) ?? CssBoxAlignment.Normal);
                if (justifySelf is CssBoxAlignment.Auto or CssBoxAlignment.Normal &&
                    boxParent.HasLocalOrAnimatedValue(FlexPanel.JustifyContentProperty))
                {
                    var flexJustify = (FlexJustify)(boxParent.GetValue(FlexPanel.JustifyContentProperty) ?? FlexJustify.FlexStart);
                    return contentStartX + ResolveFlexMainOffset(flexJustify, contentWidth, childExtent, parentRtl);
                }

                return contentStartX + ResolveGridAxisOffset(justifySelf, contentWidth, childExtent, parentRtl);
            }
            else
            {
                var alignSelf = FlexPanel.GetAlignSelf(child);
                if (alignSelf != FlexAlign.Auto)
                    return contentStartY + ResolveFlexCrossOffset(alignSelf, contentHeight, childExtent, false);

                var alignItems = (CssBoxAlignment)(boxParent.GetValue(CssGridProperties.AlignItemsProperty) ?? CssBoxAlignment.Normal);
                if (alignItems is CssBoxAlignment.Auto or CssBoxAlignment.Normal)
                    alignItems = (CssBoxAlignment)(boxParent.GetValue(CssGridProperties.AlignContentProperty) ?? CssBoxAlignment.Normal);
                if (alignItems is CssBoxAlignment.Auto or CssBoxAlignment.Normal &&
                    boxParent.HasLocalOrAnimatedValue(FlexPanel.AlignItemsProperty))
                {
                    var flexAlign = (FlexAlign)(boxParent.GetValue(FlexPanel.AlignItemsProperty) ?? FlexAlign.Stretch);
                    return contentStartY + ResolveFlexCrossOffset(flexAlign, contentHeight, childExtent, false);
                }

                return contentStartY + ResolveGridAxisOffset(alignItems, contentHeight, childExtent, false);
            }
        }

        if (isHorizontal)
        {
            if (boxParent is StackPanel { Orientation: Orientation.Horizontal })
            {
                var prev = FindPrecedingInFlowSibling(boxParent, child);
                if (prev is not null)
                    return parentOuterOffset.X + prev.VisualBounds.Right;
            }

            return (preferEnd || parentRtl)
                ? contentStartX + contentWidth - childExtent
                : contentStartX;
        }
        else
        {
            if (boxParent is StackPanel { Orientation: Orientation.Vertical } || CssDisplayLayout.IsFlow(boxParent))
            {
                var prev = FindPrecedingInFlowSibling(boxParent, child);
                if (prev is not null)
                {
                    var prevMargin = CssBoxMetrics.Margin(prev, contentWidth);
                    double prevBottom = prev.VisualBounds.Bottom;
                    if (CssDisplayLayout.IsFlow(boxParent))
                        prevBottom += prevMargin.Bottom;
                    return parentOuterOffset.Y + prevBottom;
                }
            }

            return contentStartY;
        }
    }

    private static FrameworkElement? FindPrecedingInFlowSibling(FrameworkElement boxParent, FrameworkElement target)
    {
        FrameworkElement? lastInFlow = null;
        foreach (var item in new CssPanelChildren(boxParent))
        {
            if (ReferenceEquals(item, target))
                break;
            if (item is FrameworkElement fe &&
                fe.Visibility != Visibility.Collapsed &&
                fe.CssLayout?.IsOutOfFlow != true)
            {
                lastInFlow = fe;
            }
        }
        return lastInFlow;
    }

    private static double ResolveFlexMainOffset(FlexJustify justify, double containerExtent, double itemExtent, bool reverse)
        => justify switch
        {
            FlexJustify.Center or FlexJustify.SpaceAround or FlexJustify.SpaceEvenly => (containerExtent - itemExtent) * 0.5,
            FlexJustify.FlexEnd => reverse ? 0 : containerExtent - itemExtent,
            _ => reverse ? containerExtent - itemExtent : 0,
        };

    private static double ResolveFlexCrossOffset(FlexAlign align, double containerExtent, double itemExtent, bool reverse)
        => align switch
        {
            FlexAlign.Center => (containerExtent - itemExtent) * 0.5,
            FlexAlign.FlexEnd => reverse ? 0 : containerExtent - itemExtent,
            _ => reverse ? containerExtent - itemExtent : 0,
        };

    private static double ResolveGridAxisOffset(CssBoxAlignment align, double containerExtent, double itemExtent, bool reverse)
        => align switch
        {
            CssBoxAlignment.Center or CssBoxAlignment.SpaceAround or CssBoxAlignment.SpaceEvenly => (containerExtent - itemExtent) * 0.5,
            CssBoxAlignment.End => reverse ? 0 : containerExtent - itemExtent,
            _ => reverse ? containerExtent - itemExtent : 0,
        };
}
