using Jalium.UI.Styling;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Arranges ToolBar items and manages overflow.
/// </summary>
public class ToolBarPanel : StackPanel
{
    /// <summary>Identifies the Padding dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(ToolBarPanel),
            new PropertyMetadata(new Thickness(0), OnPaddingChanged));

    #region CLR Properties

    /// <summary>Gets or sets the space inside the panel's border.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

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

    internal double GetItemSpacing(double containingWidth)
    {
        var value = Spacing;
        if (!HasLocalOrAnimatedValue(SpacingProperty))
        {
            var row = (CssLayoutLength)GetValue(CssGapProperties.RowProperty)!;
            var column = (CssLayoutLength)GetValue(CssGapProperties.ColumnProperty)!;
            if (row.IsSet || column.IsSet)
            {
                var axis = Orientation == Orientation.Horizontal ? column : row;
                value = axis.IsSet ? axis.Resolve(containingWidth, 0) : 0;
            }
        }

        return double.IsFinite(value) ? Math.Max(0, value) : 0;
    }

    private static void OnPaddingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolBarPanel)d).InvalidateMeasure();

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        var contentAvailable = CssBoxMetrics.InnerSize(availableSize, insets);
        var isHorizontal = Orientation == Orientation.Horizontal;
        var totalSize = 0.0;
        var maxCrossSize = 0.0;
        var spacing = GetItemSpacing(contentAvailable.Width);
        var sawVisibleChild = false;
        var childConstraint = isHorizontal
            ? new Size(double.PositiveInfinity, contentAvailable.Height)
            : new Size(contentAvailable.Width, double.PositiveInfinity);

        foreach (UIElement child in Children.EnumerateStruct())
        {
            child.Measure(childConstraint);
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var childMainSize = isHorizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
            var childCrossSize = isHorizontal ? child.DesiredSize.Height : child.DesiredSize.Width;
            if (sawVisibleChild)
                totalSize += spacing;
            totalSize += childMainSize;
            maxCrossSize = Math.Max(maxCrossSize, childCrossSize);
            sawVisibleChild = true;
        }

        return isHorizontal
            ? new Size(totalSize + insets.Left + insets.Right,
                maxCrossSize + insets.Top + insets.Bottom)
            : new Size(maxCrossSize + insets.Left + insets.Right,
                totalSize + insets.Top + insets.Bottom);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = ControlRenderGeometry.GetContentRect(new Rect(finalSize),
            CssBoxMetrics.ContentInsets(this,
                CssLayout?.ContainingWidthCache ?? finalSize.Width));
        var isHorizontal = Orientation == Orientation.Horizontal;
        var offset = 0.0;
        var spacing = GetItemSpacing(content.Width);
        var sawVisibleChild = false;

        foreach (UIElement child in Children.EnumerateStruct())
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(default);
                continue;
            }

            if (sawVisibleChild)
                offset += spacing;
            if (isHorizontal)
            {
                child.Arrange(new Rect(content.X + offset, content.Y,
                    child.DesiredSize.Width, content.Height));
                offset += child.DesiredSize.Width;
            }
            else
            {
                child.Arrange(new Rect(content.X, content.Y + offset,
                    content.Width, child.DesiredSize.Height));
                offset += child.DesiredSize.Height;
            }
            sawVisibleChild = true;
        }

        return finalSize;
    }

    #endregion
}
