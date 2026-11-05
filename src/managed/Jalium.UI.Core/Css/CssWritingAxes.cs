namespace Jalium.UI.Styling;

/// <summary>Logical formatting coordinates are inline-right/block-down; element slots remain physical.</summary>
internal readonly record struct CssWritingAxes(CssWritingMode Mode)
{
    internal static FlowDirection UsedDirection(DependencyObject element)
        => CssWritingModeProperties.GetWritingMode(element) is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
           CssWritingModeProperties.GetTextOrientation(element) == CssTextOrientation.Upright
            ? FlowDirection.LeftToRight : (FlowDirection)element.GetValue(FrameworkElement.FlowDirectionProperty)!;
    internal bool Vertical => CssWritingModeProperties.IsVertical(Mode);
    internal bool ReversedLineCross => Mode == CssWritingMode.VerticalLr;
    internal Thickness LineLogical(Thickness physical) => ReversedLineCross
        ? new(physical.Top, physical.Right, physical.Bottom, physical.Left) : Logical(physical);
    internal Rect LinePlacement(Rect lineRelative, double lineTop, double lineHeight) => ReversedLineCross
        ? new(lineRelative.X, 2 * lineTop + lineHeight - lineRelative.Bottom, lineRelative.Width, lineRelative.Height)
        : lineRelative;
    internal static double PercentageBasis(FrameworkElement element, double fallback)
        => element.CssParentBox is { } parent && CssWritingModeProperties.IsVertical(parent.WritingMode)
            ? parent.InlineSize : element.CssLayout?.ContainingWidthCache ?? fallback;
    internal Size Logical(Size physical) => Vertical ? new(physical.Height, physical.Width) : physical;
    internal Size Physical(Size logical) => Logical(logical);
    internal Thickness Logical(Thickness physical) => Mode switch
    {
        CssWritingMode.VerticalLr => new(physical.Top, physical.Left, physical.Bottom, physical.Right),
        CssWritingMode.SidewaysLr => new(physical.Bottom, physical.Left, physical.Top, physical.Right),
        CssWritingMode.VerticalRl or CssWritingMode.SidewaysRl => new(physical.Top, physical.Right, physical.Bottom, physical.Left),
        _ => physical,
    };
    internal Thickness Physical(Thickness logical) => Mode switch
    {
        CssWritingMode.VerticalLr => new(logical.Top, logical.Left, logical.Bottom, logical.Right),
        CssWritingMode.SidewaysLr => new(logical.Top, logical.Right, logical.Bottom, logical.Left),
        CssWritingMode.VerticalRl or CssWritingMode.SidewaysRl => new(logical.Bottom, logical.Left, logical.Top, logical.Right),
        _ => logical,
    };
    internal Rect Physical(Rect logical, Size physicalContaining) => Mode switch
    {
        CssWritingMode.VerticalLr => new(logical.Top, logical.Left, logical.Height, logical.Width),
        CssWritingMode.SidewaysLr => new(logical.Top, physicalContaining.Height - logical.Right, logical.Height, logical.Width),
        CssWritingMode.VerticalRl or CssWritingMode.SidewaysRl => new(physicalContaining.Width - logical.Bottom, logical.Left, logical.Height, logical.Width),
        _ => logical,
    };
    internal Rect Local(Rect logical, Rect logicalAllocation) => Physical(
        new(logical.X - logicalAllocation.X, logical.Y - logicalAllocation.Y, logical.Width, logical.Height),
        Physical(logicalAllocation.Size));
}
