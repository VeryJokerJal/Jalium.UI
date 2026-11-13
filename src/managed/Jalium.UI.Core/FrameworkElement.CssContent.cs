using Jalium.UI.Styling;

namespace Jalium.UI;

public partial class FrameworkElement
{
    /// <summary>
    /// Allows CSS child formatting for a container whose native visual children
    /// represent its content. Native text and self-drawn content need a content adapter.
    /// </summary>
    protected virtual bool SupportsCssContentLayout => false;

    /// <summary>Creates deferred content, such as a control template, before CSS layout.</summary>
    protected virtual void PrepareCssContentLayout() { }

    /// <summary>Notifies a host after CSS pseudo-element and content evaluation updates.</summary>
    protected internal virtual void OnCssContentHostUpdated() { }

    internal bool IsCssContentHost => this is Controls.Panel || SupportsCssContentLayout;
    internal bool CssIntrinsicMeasure;
    internal void PrepareCssContent() => PrepareCssContentLayout();
    internal void AttachCssGeneratedChild(UIElement child)
    {
        if (this is Controls.Panel panel) panel.AddVisualChildInternal(child);
        else AddVisualChild(child);
    }
    internal void DetachCssGeneratedChild(UIElement child) => RemoveVisualChild(child);

    private Rect CssAbsoluteContainingBox(Size outerSize)
    {
        var (border, _) = CssBoxMetrics.BackgroundInsets(this, CssLayout?.ContainingWidthCache ?? outerSize.Width);
        return new(border.Left, border.Top,
            Math.Max(0, outerSize.Width - border.Left - border.Right),
            Math.Max(0, outerSize.Height - border.Top - border.Bottom));
    }

    internal void MeasureCssLayoutAbsoluteChildren(Size availableSize)
    {
        var containing = CssAbsoluteContainingBox(availableSize).Size;
        foreach (var child in new CssPanelChildren(this))
        {
            if (child is not FrameworkElement element || element.CssLayout is not { IsOutOfFlow: true } layout ||
                child.Visibility == Visibility.Collapsed) continue;
            if (layout.Position == CssPositionMode.Fixed) CssPositionedLayout.MeasureFixed(element, availableSize);
            else if (CssPositionedLayout.UsesAncestorContainingBlock(element)) CssPositionedLayout.MeasureAbsolute(element, availableSize);
            else CssLayoutBox.MeasureWithContainingBlock(child,
                CssAbsoluteLayout.ComputeMeasureConstraint(layout, containing, element), containing);
        }
    }

    internal void ArrangeCssLayoutAbsoluteChildren(Size finalSize)
    {
        var containing = CssAbsoluteContainingBox(finalSize);
        foreach (var child in new CssPanelChildren(this))
        {
            if (child is not FrameworkElement element || element.CssLayout is not { IsOutOfFlow: true } layout ||
                child.Visibility == Visibility.Collapsed) continue;
            if (layout.Position == CssPositionMode.Fixed) { CssPositionedLayout.ArrangeFixed(element, finalSize); continue; }
            if (CssPositionedLayout.UsesAncestorContainingBlock(element)) { CssPositionedLayout.ArrangeAbsolute(element, finalSize); continue; }
            CssLayoutBox.MeasureWithContainingBlock(child,
                CssAbsoluteLayout.ComputeMeasureConstraint(layout, containing.Size, element), containing.Size);
            var width = element.CssPreferredWidth(containing.Width);
            var height = element.CssPreferredHeight(containing.Height);
            var slot = CssAbsoluteLayout.ComputeSlot(layout, containing.Size, child.DesiredSize, width, height, element,
                FlowDirection == FlowDirection.RightToLeft);
            CssLayoutBox.ArrangeWithContainingBlock(child,
                new Rect(slot.X + containing.X, slot.Y + containing.Y, slot.Width, slot.Height), containing.Size);
        }
    }
}
