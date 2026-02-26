namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Arranges ToolBar items and manages overflow.
/// </summary>
public sealed class ToolBarPanel : StackPanel
{
    #region CLR Properties

    /// <summary>
    /// Gets or sets the ToolBar that owns this panel.
    /// </summary>
    public ToolBar? ToolBarOwner { get; internal set; }

    /// <summary>
    /// Gets the list of items that overflow the panel.
    /// </summary>
    public List<UIElement> OverflowItems { get; } = new();

    /// <summary>
    /// Gets a value indicating whether there are overflow items.
    /// </summary>
    public bool HasOverflowItems => OverflowItems.Count > 0;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        OverflowItems.Clear();

        var isHorizontal = Orientation == Orientation.Horizontal;
        var totalSize = 0.0;
        var maxCrossSize = 0.0;
        var limit = isHorizontal ? availableSize.Width : availableSize.Height;

        foreach (var child in Children)
        {
            child.Measure(availableSize);

            var childMainSize = isHorizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
            var childCrossSize = isHorizontal ? child.DesiredSize.Height : child.DesiredSize.Width;

            if (totalSize + childMainSize > limit && !double.IsPositiveInfinity(limit))
            {
                // This item and all subsequent items overflow
                OverflowItems.Add(child);
            }
            else
            {
                totalSize += childMainSize;
                maxCrossSize = Math.Max(maxCrossSize, childCrossSize);
            }
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

        foreach (var child in Children)
        {
            if (OverflowItems.Contains(child))
            {
                // Hide overflow items
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

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

/// <summary>
/// Placeholder for ToolBar reference.
/// </summary>
public class ToolBar : ItemsControl
{
    /// <summary>
    /// Gets or sets a value indicating whether there are overflow items.
    /// </summary>
    public bool HasOverflowItems { get; internal set; }
}
