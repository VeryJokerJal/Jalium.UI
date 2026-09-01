namespace Jalium.UI.Styling;

/// <summary>
/// Pure functions for position:absolute slot computation (headless-testable). The slot is
/// the child's margin-box rectangle inside the panel; the child's own Arrange applies
/// margin/alignment within it.
/// </summary>
internal static class CssAbsoluteLayout
{
    /// <summary>
    /// Pre-narrows the measure constraint: when both edges of an axis are set and the
    /// panel size is finite, the child's slot extent is known up front.
    /// </summary>
    public static Size ComputeMeasureConstraint(CssLayoutState layout, Size panelAvailable)
    {
        var width = panelAvailable.Width;
        if (!double.IsInfinity(width) &&
            layout.InsetLeft.IsSet && layout.InsetRight.IsSet)
        {
            var left = layout.InsetLeft.Resolve(width, 0);
            var right = layout.InsetRight.Resolve(width, 0);
            width = Math.Max(0, width - left - right);
        }

        var height = panelAvailable.Height;
        if (!double.IsInfinity(height) &&
            layout.InsetTop.IsSet && layout.InsetBottom.IsSet)
        {
            var top = layout.InsetTop.Resolve(height, 0);
            var bottom = layout.InsetBottom.Resolve(height, 0);
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
        double effectiveWidth, double effectiveHeight)
    {
        var (x, width) = SolveAxis(
            layout.InsetLeft, layout.InsetRight, panelSize.Width,
            effectiveWidth, childDesired.Width);
        var (y, height) = SolveAxis(
            layout.InsetTop, layout.InsetBottom, panelSize.Height,
            effectiveHeight, childDesired.Height);
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    private static (double pos, double extent) SolveAxis(
        CssLayoutLength start, CssLayoutLength end, double panelExtent,
        double determinate, double desired)
    {
        var hasStart = start.IsSet;
        var hasEnd = end.IsSet;
        var startPx = hasStart ? start.Resolve(panelExtent, 0) : 0;
        var endPx = hasEnd ? end.Resolve(panelExtent, 0) : 0;

        if (!double.IsNaN(determinate))
        {
            if (hasStart)
            {
                return (startPx, determinate); // start wins over end (LTR rule)
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
