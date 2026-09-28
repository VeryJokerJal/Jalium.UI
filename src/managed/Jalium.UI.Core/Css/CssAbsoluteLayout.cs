namespace Jalium.UI.Styling;

/// <summary>
/// Position:absolute slot computation (headless-testable). The slot is
/// the child's margin-box rectangle inside the panel; the child's own Arrange applies
/// margin/alignment within it.
/// </summary>
internal static class CssAbsoluteLayout
{
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

    /// <summary>
    /// Solves the inset equations for one absolutely positioned child.
    /// <paramref name="effectiveWidth"/>/<paramref name="effectiveHeight"/> are the
    /// child's determinate sizes (explicit DP or CSS %, resolved against the panel), NaN = auto.
    /// </summary>
    public static Rect ComputeSlot(
        CssLayoutState layout, Size panelSize, Size childDesired,
        double effectiveWidth, double effectiveHeight, FrameworkElement? child = null)
    {
        var (x, width) = SolveAxis(
            EffectiveInset(child, 0, layout.InsetLeft), EffectiveInset(child, 2, layout.InsetRight), panelSize.Width,
            effectiveWidth, childDesired.Width,
            child?.VisualParent is FrameworkElement { FlowDirection: FlowDirection.RightToLeft });
        var (y, height) = SolveAxis(
            EffectiveInset(child, 1, layout.InsetTop), EffectiveInset(child, 3, layout.InsetBottom), panelSize.Height,
            effectiveHeight, childDesired.Height, false);
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
        double determinate, double desired, bool preferEnd)
    {
        var hasStart = start.IsSet && !start.IsAuto;
        var hasEnd = end.IsSet && !end.IsAuto;
        var startPx = hasStart ? start.Resolve(panelExtent, 0) : 0;
        var endPx = hasEnd ? end.Resolve(panelExtent, 0) : 0;

        if (!double.IsNaN(determinate))
        {
            if (hasStart && hasEnd && preferEnd)
                return (panelExtent - endPx - determinate, determinate);
            if (hasStart)
            {
                return (startPx, determinate);
            }

            if (hasEnd)
            {
                return (panelExtent - endPx - determinate, determinate);
            }

            return (0, determinate);
        }

        if (hasStart && hasEnd)
        {
            return (startPx, panelExtent - startPx - endPx);
        }

        if (hasStart)
        {
            return (startPx, desired);
        }

        if (hasEnd)
        {
            return (panelExtent - endPx - desired, desired);
        }

        return (0, desired);
    }
}
