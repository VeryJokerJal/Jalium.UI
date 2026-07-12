namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Arranges ToolBar items and manages overflow.
/// </summary>
public class ToolBarPanel : StackPanel
{
    #region CLR Properties

    /// <summary>
    /// Gets or sets the ToolBar that owns this panel.
    /// </summary>
    public Jalium.UI.Controls.ToolBar? ToolBarOwner { get; internal set; }

    /// <summary>
    /// Gets the list of items that overflow the panel.
    /// </summary>
    public List<UIElement> OverflowItems { get; } = new();

    /// <summary>
    /// Gets a value indicating whether there are overflow items.
    /// </summary>
    public bool HasOverflowItems => OverflowItems.Count > 0;

    internal void SetOverflowItems(IEnumerable<UIElement> items)
    {
        OverflowItems.Clear();
        OverflowItems.AddRange(items);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var isHorizontal = Orientation == Orientation.Horizontal;
        var totalSize = 0.0;
        var maxCrossSize = 0.0;
        var childConstraint = isHorizontal
            ? new Size(double.PositiveInfinity, availableSize.Height)
            : new Size(availableSize.Width, double.PositiveInfinity);

        foreach (UIElement child in Children)
        {
            child.Measure(childConstraint);

            var childMainSize = isHorizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
            var childCrossSize = isHorizontal ? child.DesiredSize.Height : child.DesiredSize.Width;
            totalSize += childMainSize;
            maxCrossSize = Math.Max(maxCrossSize, childCrossSize);
        }

        return isHorizontal
            ? new Size(totalSize, maxCrossSize)
            : new Size(maxCrossSize, totalSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var isHorizontal = Orientation == Orientation.Horizontal;
        var offset = 0.0;

        foreach (UIElement child in Children)
        {
            if (isHorizontal)
            {
                child.Arrange(new Rect(offset, 0, child.DesiredSize.Width, finalSize.Height));
                offset += child.DesiredSize.Width;
            }
            else
            {
                child.Arrange(new Rect(0, offset, finalSize.Width, child.DesiredSize.Height));
                offset += child.DesiredSize.Height;
            }
        }

        return finalSize;
    }

    #endregion
}
