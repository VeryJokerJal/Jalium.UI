using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

/// <summary>A formatting box refers to its native element; it never owns or reparents that element.</summary>
internal sealed class CssLayoutBox(UIElement element, int sourceIndex)
{
    public UIElement Element { get; } = element;
    public int SourceIndex { get; } = sourceIndex;
    public Size MinContent;
    public Size MaxContent;

    public static CssLayoutBox[] ChildrenOf(Panel owner, bool orderModified = true) => owner.Children.Cast<UIElement>()
        .Select((child, index) => new CssLayoutBox(child, index))
        .Where(box => box.Element.Visibility != Visibility.Collapsed &&
            box.Element is not FrameworkElement { CssLayout.Position: CssPositionMode.Absolute })
        .OrderBy(box => orderModified ? FlexPanel.GetOrder(box.Element) : 0).ThenBy(box => box.SourceIndex).ToArray();

    internal void MeasureBorderBox(Size available, Size containingBlock, CssFloatEnvironment? floats = null)
    {
        if (Element is not FrameworkElement element) { Element.Measure(available); return; }
        var previous = element.CssParentBox;
        element.CssParentBox = new(containingBlock, floats);
        if (element.LastCssMeasureBox != element.CssParentBox) element.InvalidateCssAllocation();
        try { element.Measure(available); }
        finally { element.CssParentBox = previous; }
    }

    internal void ArrangeBorderBox(Rect slot, Size containingBlock, CssBoxAlignment horizontal = CssBoxAlignment.Normal, CssFloatEnvironment? floats = null)
    {
        if (Element is not FrameworkElement element) { Element.Arrange(slot); return; }
        var previous = element.CssParentBox;
        element.CssParentBox = new(containingBlock, floats);
        if (element.LastCssArrangeBox != element.CssParentBox) element.InvalidateArrange();
        try { Arrange(slot, horizontal, CssBoxAlignment.Start); }
        finally { element.CssParentBox = previous; }
    }

    public void MeasureIntrinsicWidth()
    {
        Element.Measure(new Size(0, double.PositiveInfinity));
        MinContent = Element.DesiredSize;
        Element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        MaxContent = Element.DesiredSize;
    }

    public void Arrange(Rect slot, CssBoxAlignment horizontal, CssBoxAlignment vertical)
    {
        if (Element is not FrameworkElement element) { Element.Arrange(slot); return; }
        var previous = element.CssParentAlignment;
        element.CssParentAlignment = (horizontal, vertical);
        if (element.LastCssParentAlignment != element.CssParentAlignment) element.InvalidateArrange();
        try { element.Arrange(slot); }
        finally { element.CssParentAlignment = previous; }
    }
}

/// <summary>The formatter owns margins and supplies the containing block independently of an allocated border box.</summary>
internal readonly record struct CssParentBoxContext(Size ContainingBlock, CssFloatEnvironment? Floats = null);
