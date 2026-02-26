namespace Jalium.UI.Controls;

/// <summary>
/// Panel that lays out cells in a <see cref="DataGridRow"/>.
/// </summary>
public sealed class DataGridCellsPanel : VirtualizingPanel
{
    /// <summary>
    /// Measures the child elements of the panel and determines the desired size.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double totalWidth = 0;
        double maxHeight = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
            totalWidth += child.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }
        return new Size(totalWidth, maxHeight);
    }

    /// <summary>
    /// Positions child elements and determines the final size for the panel.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in Children)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }
        return finalSize;
    }
}
