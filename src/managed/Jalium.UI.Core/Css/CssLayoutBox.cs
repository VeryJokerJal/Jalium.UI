using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

/// <summary>A formatting box refers to its native element; it never owns or reparents that element.</summary>
internal sealed class CssLayoutBox(UIElement element, int sourceIndex)
{
    public UIElement Element { get; } = element;
    public int SourceIndex { get; } = sourceIndex;
    public Size MinContent;
    public Size MaxContent;

    public static CssLayoutBox[] ChildrenOf(FrameworkElement owner, bool orderModified = true) => new CssPanelChildren(owner).Enumerate().Cast<UIElement>()
        .Select((child, index) => new CssLayoutBox(child, index))
        .Where(box => box.Element.Visibility != Visibility.Collapsed &&
            box.Element is not FrameworkElement { CssLayout.IsOutOfFlow: true })
        .OrderBy(box => orderModified ? FlexPanel.GetOrder(box.Element) : 0).ThenBy(box => box.SourceIndex).ToArray();

    internal void MeasureBorderBox(Size available, Size containingBlock, CssFloatEnvironment? floats = null,
        CssWritingMode containingWritingMode = CssWritingMode.HorizontalTb)
    {
        if (Element is not FrameworkElement element) { Element.Measure(available); return; }
        var previous = element.CssParentBox;
        element.CssParentBox = new(containingBlock, floats, containingWritingMode);
        if (element.LastCssMeasureBox != element.CssParentBox) element.InvalidateCssAllocation();
        try { element.Measure(available); }
        finally { element.CssParentBox = previous; }
    }

    internal void ArrangeBorderBox(Rect slot, Size containingBlock, CssBoxAlignment horizontal = CssBoxAlignment.Normal, CssFloatEnvironment? floats = null,
        CssBoxAlignment vertical = CssBoxAlignment.Start, CssWritingMode containingWritingMode = CssWritingMode.HorizontalTb)
    {
        if (Element is not FrameworkElement element) { Element.Arrange(slot); return; }
        var previous = element.CssParentBox;
        element.CssParentBox = new(containingBlock, floats, containingWritingMode);
        if (element.LastCssArrangeBox != element.CssParentBox) element.InvalidateArrange();
        try { Arrange(slot, horizontal, vertical); }
        finally { element.CssParentBox = previous; }
    }

    public void MeasureIntrinsicWidth(Size? containingBlock = null, CssFloatEnvironment? floats = null)
    {
        var framework = Element as FrameworkElement;
        var previous = framework?.CssIntrinsicMeasure ?? false;
        if (framework is not null) { framework.CssIntrinsicMeasure = true; framework.InvalidateCssAllocation(); }
        try
        {
            Measure(new Size(0, double.PositiveInfinity));
            MinContent = Element.DesiredSize;
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            MaxContent = Element.DesiredSize;
        }
        finally { if (framework is not null) framework.CssIntrinsicMeasure = previous; }

        void Measure(Size available)
        {
            using var contribution = framework is null ? null : new CssIntrinsicPercentage.Scope(framework,
                true, available.Width == 0, cyclicRoot: containingBlock is null);
            // A typographic pseudo can change the very first wrapping decision.
            // Settle its deferred private-tree allocation before recording this
            // intrinsic contribution; finite-width fragments are not a probe.
            for (var pass = 0; pass < 8; pass++)
            {
                if (containingBlock is { } containing) MeasureBorderBox(available, containing, floats);
                else Element.Measure(available);
                if (!CssFirstLine.HasPending(Element.Dispatcher)) break;
                CssFirstLine.FlushLayout(Element.Dispatcher);
                if (Element.IsMeasureValid) break;
            }
        }
    }

    public void Arrange(Rect slot, CssBoxAlignment horizontal, CssBoxAlignment vertical)
    {
        ArrangeWithAlignment(Element, slot, horizontal, vertical);
    }

    internal static void ArrangeWithAlignment(UIElement child, Rect slot,
        CssBoxAlignment horizontal, CssBoxAlignment vertical)
    {
        if (child is not FrameworkElement element) { child.Arrange(slot); return; }
        var previous = element.CssParentAlignment;
        if (element is CssTableAnonymousBox anonymous && anonymous.AllocationOrigin != slot.Location)
        {
            anonymous.AllocationOrigin = slot.Location;
            element.InvalidateArrange();
        }
        element.CssParentAlignment = (horizontal, vertical);
        if (element.LastCssParentAlignment != element.CssParentAlignment) element.InvalidateArrange();
        try { element.Arrange(slot); }
        finally { element.CssParentAlignment = previous; }
    }

    // Flex and positioned parents leave margins to FrameworkElement. Their
    // percentage basis still belongs to the container, not the solved slot.
    internal static void MeasureWithContainingBlock(UIElement child, Size available, Size containingBlock)
    {
        if (child is not FrameworkElement element) { child.Measure(available); return; }
        var previous = element.CssNativeBox;
        element.CssNativeBox = new(containingBlock);
        if (element.CssLayout is { UsesPercent: true } layout &&
            layout.NativeMeasureContainingBlock != containingBlock) element.InvalidateCssAllocation();
        try { element.Measure(available); }
        finally { element.CssNativeBox = previous; }
    }

    internal static void ArrangeWithContainingBlock(UIElement child, Rect slot, Size containingBlock,
        CssBoxAlignment? horizontal = null, CssBoxAlignment? vertical = null)
    {
        if (child is not FrameworkElement element) { child.Arrange(slot); return; }
        var previous = element.CssNativeBox;
        element.CssNativeBox = new(containingBlock);
        if (element.LastCssNativeArrangeBox != element.CssNativeBox) element.InvalidateArrange();
        try
        {
            if (horizontal is { } h && vertical is { } v) ArrangeWithAlignment(child, slot, h, v);
            else element.Arrange(slot);
        }
        finally { element.CssNativeBox = previous; }
    }
}

/// <summary>The formatter owns margins and supplies the containing block independently of an allocated border box.</summary>
internal readonly record struct CssParentBoxContext(Size ContainingBlock, CssFloatEnvironment? Floats = null,
    CssWritingMode WritingMode = CssWritingMode.HorizontalTb)
{
    internal double InlineSize => new CssWritingAxes(WritingMode).Logical(ContainingBlock).Width;
}
