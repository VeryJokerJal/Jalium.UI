namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides a panel that displays overflow items from a ToolBar.
/// </summary>
public class ToolBarOverflowPanel : Panel
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the WrapWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WrapWidthProperty =
        DependencyProperty.Register(nameof(WrapWidth), typeof(double), typeof(ToolBarOverflowPanel),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the width at which to wrap items.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double WrapWidth
    {
        get => (double)GetValue(WrapWidthProperty)!;
        set => SetValue(WrapWidthProperty, value);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var wrapWidth = double.IsNaN(WrapWidth) ? availableSize.Width : WrapWidth;

        var currentRowWidth = 0.0;
        var currentRowHeight = 0.0;
        var totalHeight = 0.0;
        var maxWidth = 0.0;

        foreach (var child in Children)
        {
            child.Measure(availableSize);

            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (currentRowWidth + childWidth > wrapWidth && currentRowWidth > 0)
            {
                // Start new row
                maxWidth = Math.Max(maxWidth, currentRowWidth);
                totalHeight += currentRowHeight;
                currentRowWidth = childWidth;
                currentRowHeight = childHeight;
            }
            else
            {
                currentRowWidth += childWidth;
                currentRowHeight = Math.Max(currentRowHeight, childHeight);
            }
        }

        // Add last row
        maxWidth = Math.Max(maxWidth, currentRowWidth);
        totalHeight += currentRowHeight;

        return new Size(maxWidth, totalHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var wrapWidth = double.IsNaN(WrapWidth) ? finalSize.Width : WrapWidth;

        var currentX = 0.0;
        var currentY = 0.0;
        var currentRowHeight = 0.0;

        foreach (var child in Children)
        {
            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (currentX + childWidth > wrapWidth && currentX > 0)
            {
                // Start new row
                currentX = 0;
                currentY += currentRowHeight;
                currentRowHeight = 0;
            }

            child.Arrange(new Rect(currentX, currentY, childWidth, childHeight));

            currentX += childWidth;
            currentRowHeight = Math.Max(currentRowHeight, childHeight);
        }

        return finalSize;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolBarOverflowPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    #endregion
}
