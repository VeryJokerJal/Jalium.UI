namespace Jalium.UI.Controls;

/// <summary>
/// Horizontal items host used by <see cref="Primitives.DataGridCellsPresenter"/>.
/// It preserves each column's measured width and supplies the virtualization hooks inherited
/// from <see cref="VirtualizingPanel"/>.
/// </summary>
public class DataGridCellsPanel : VirtualizingPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (UIElement child in Children)
        {
            child.Measure(childConstraint);
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (UIElement child in Children)
        {
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
