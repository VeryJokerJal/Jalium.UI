namespace Jalium.UI.Styling;

/// <summary>Flex sizes the item's border box after resolving its preferred size.</summary>
internal readonly record struct CssFlexItemContext(bool Horizontal, bool IgnoreMainSize,
    bool IgnoreMainBounds, double UsedMain = double.NaN, bool MinimumContribution = false,
    bool? DefiniteHeight = null);

// Allocate the contexts only for elements that actually participate in flex layout.
internal sealed class CssFlexItemState
{
    internal CssFlexItemContext? Current, Measured, Arranged;
}

internal static class CssFlexItemSizing
{
    internal static Size Measure(UIElement child, Size available, Size containingBlock,
        CssFlexItemContext allocation, bool intrinsic = false, bool? intrinsicHorizontal = null,
        bool? intrinsicMinimum = null)
    {
        if (child is not FrameworkElement element) { child.Measure(available); return child.DesiredSize; }
        var state = element.CssFlexSizing ??= new();
        var previous = state.Current;
        var previousIntrinsic = element.CssIntrinsicMeasure;
        state.Current = allocation;
        element.CssIntrinsicMeasure = intrinsic;
        if (state.Measured != allocation || previousIntrinsic != intrinsic)
            element.InvalidateCssAllocation();
        try
        {
            var intrinsicAxis = intrinsicHorizontal ?? allocation.Horizontal;
            using var contribution = intrinsic ? new CssIntrinsicPercentage.Scope(element, intrinsicAxis,
                intrinsicMinimum ?? (allocation.MinimumContribution || intrinsicAxis && available.Width == 0),
                naturalRoot: allocation.IgnoreMainSize) : null;
            CssLayoutBox.MeasureWithContainingBlock(child, available, containingBlock);
            return child.DesiredSize;
        }
        finally { state.Current = previous; element.CssIntrinsicMeasure = previousIntrinsic; }
    }

    internal static void Arrange(UIElement child, Rect slot, Size containingBlock,
        bool horizontal, double borderMain, CssBoxAlignment inline, CssBoxAlignment block,
        bool? definiteHeight = null)
    {
        if (child is not FrameworkElement element) { child.Arrange(slot); return; }
        var state = element.CssFlexSizing ??= new();
        var previous = state.Current;
        state.Current = new(horizontal, false, false, borderMain, DefiniteHeight: definiteHeight);
        if (state.Arranged != state.Current) element.InvalidateArrange();
        try { CssLayoutBox.ArrangeWithContainingBlock(child, slot, containingBlock, inline, block); }
        finally { state.Current = previous; }
    }

    internal static double? MainSize(FrameworkElement element, bool horizontal)
    {
        if (element.CssFlexSizing?.Current is not { } allocation) return null;
        if (allocation.Horizontal != horizontal)
            return allocation.MinimumContribution && IsReplaced(element) ? double.NaN : null;
        if (double.IsFinite(allocation.UsedMain))
            return Math.Max(0, allocation.UsedMain - (!horizontal ? CssTableLayout.CaptionHeight(element) : 0));
        return allocation.IgnoreMainSize ? double.NaN : null;
    }

    internal static bool IgnoreBounds(FrameworkElement element, bool horizontal)
        => element.CssFlexSizing?.Current is { IgnoreMainBounds: true } allocation &&
            (allocation.Horizontal == horizontal || allocation.MinimumContribution && IsReplaced(element));

    internal static bool IsReplaced(FrameworkElement element)
        => CssReplacedContent.HasReplacement(element) || element is Controls.Image or Controls.MediaElement;

    internal static void SourceChanged(FrameworkElement element, DependencyProperty property)
    {
        if (property != FrameworkElement.MinWidthProperty && property != FrameworkElement.MinHeightProperty &&
            (property.Name != "Basis" || property != Controls.FlexPanel.BasisProperty)) return;
        if (element.VisualParent is FrameworkElement parent &&
            (parent is Controls.FlexPanel || parent.CssDisplayMode == CssDisplayMode.Flex))
        { element.InvalidateMeasure(); parent.InvalidateMeasure(); }
    }
}
