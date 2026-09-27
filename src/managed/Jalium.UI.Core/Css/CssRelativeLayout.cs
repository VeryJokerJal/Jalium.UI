namespace Jalium.UI.Styling;

/// <summary>Offsets the painted box after normal layout without moving its flow slot.</summary>
internal static class CssRelativeLayout
{
    internal static Size ContainingBlock(FrameworkElement child, Size fallback)
    {
        if (child.CssParentBox is { } cssParent) return cssParent.ContainingBlock;
        if (child.VisualParent is not FrameworkElement parent) return fallback;
        var parentSize = parent.CssArrangeSizeForChildren;
        var insets = CssBoxMetrics.ContentInsets(parent, parentSize.Width);
        return CssBoxMetrics.InnerSize(parentSize, insets);
    }

    internal static bool PreferRight(FrameworkElement child) =>
        child.VisualParent is FrameworkElement { FlowDirection: FlowDirection.RightToLeft };

    internal static byte LocalInsetMask(FrameworkElement child)
    {
        byte mask = 0;
        for (var edge = 0; edge < 4; edge++)
        {
            var property = CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(edge);
            if (property is not null && child.HasLocalOrAnimatedValue(property))
                mask |= (byte)(1 << edge);
        }
        return mask;
    }

    internal static Point Offset(FrameworkElement child, CssLayoutState layout, Size containingBlock)
    {
        var left = CssInset(child, 0, layout.InsetLeft);
        var top = CssInset(child, 1, layout.InsetTop);
        var right = CssInset(child, 2, layout.InsetRight);
        var bottom = CssInset(child, 3, layout.InsetBottom);

        var hasLeft = left.IsSet && !left.IsAuto;
        var hasTop = top.IsSet && !top.IsAuto;
        var hasRight = right.IsSet && !right.IsAuto;
        var hasBottom = bottom.IsSet && !bottom.IsAuto;

        // Opposing inline edges are overconstrained. The containing block's direction
        // chooses which edge determines the used offset; top wins on the block axis.
        var preferRight = PreferRight(child);
        var x = hasLeft && (!hasRight || !preferRight)
            ? left.Resolve(containingBlock.Width, 0)
            : hasRight ? -right.Resolve(containingBlock.Width, 0) : 0;
        var y = hasTop ? top.Resolve(containingBlock.Height, 0)
            : hasBottom ? -bottom.Resolve(containingBlock.Height, 0) : 0;
        return new Point(x, y);
    }

    private static CssLayoutLength CssInset(FrameworkElement child, int edge, CssLayoutLength css)
    {
        var property = CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(edge);
        // Canvas attached values already contribute to normal Canvas placement. Do not
        // count a local value again as a relative translation, and keep its precedence.
        return property is not null && child.HasLocalOrAnimatedValue(property)
            ? CssLayoutLength.Auto : css;
    }
}
