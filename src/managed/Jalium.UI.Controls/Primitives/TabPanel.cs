namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Handles the layout of TabItem elements on a TabControl.
/// </summary>
public sealed class TabPanel : Panel
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the TabStripPlacement dependency property.
    /// </summary>
    public static readonly DependencyProperty TabStripPlacementProperty =
        DependencyProperty.Register(nameof(TabStripPlacement), typeof(Dock), typeof(TabPanel),
            new PropertyMetadata(Dock.Top, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the placement of the tab strip.
    /// </summary>
    public Dock TabStripPlacement
    {
        get => (Dock)GetValue(TabStripPlacementProperty)!;
        set => SetValue(TabStripPlacementProperty, value);
    }

    #endregion

    #region Private Fields

    private int _numRows = 1;
    private int _numHeadersPerRow = 0;
    private double _rowHeight = 0;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var isHorizontal = TabStripPlacement == Dock.Top || TabStripPlacement == Dock.Bottom;

        var totalWidth = 0.0;
        var totalHeight = 0.0;
        var maxWidth = 0.0;
        var maxHeight = 0.0;

        foreach (var child in Children)
        {
            child.Measure(availableSize);

            if (isHorizontal)
            {
                totalWidth += child.DesiredSize.Width;
                maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
            }
            else
            {
                totalHeight += child.DesiredSize.Height;
                maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
            }
        }

        if (isHorizontal)
        {
            // Check if wrapping is needed
            if (totalWidth > availableSize.Width && !double.IsPositiveInfinity(availableSize.Width))
            {
                CalculateWrapping(availableSize.Width);
                return new Size(availableSize.Width, _rowHeight * _numRows);
            }

            _numRows = 1;
            _rowHeight = maxHeight;
            return new Size(totalWidth, maxHeight);
        }
        else
        {
            _numRows = 1;
            _rowHeight = maxWidth;
            return new Size(maxWidth, totalHeight);
        }
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var isHorizontal = TabStripPlacement == Dock.Top || TabStripPlacement == Dock.Bottom;

        if (isHorizontal)
        {
            if (_numRows > 1)
            {
                ArrangeWrapped(finalSize);
            }
            else
            {
                ArrangeHorizontal(finalSize);
            }
        }
        else
        {
            ArrangeVertical(finalSize);
        }

        return finalSize;
    }

    private void CalculateWrapping(double availableWidth)
    {
        var currentRowWidth = 0.0;
        var maxRowHeight = 0.0;
        var headersPerRow = 0;
        _numRows = 1;

        foreach (var child in Children)
        {
            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (currentRowWidth + childWidth > availableWidth && headersPerRow > 0)
            {
                _numRows++;
                currentRowWidth = childWidth;
                headersPerRow = 1;
            }
            else
            {
                currentRowWidth += childWidth;
                headersPerRow++;
            }

            maxRowHeight = Math.Max(maxRowHeight, childHeight);
        }

        _rowHeight = maxRowHeight;
        _numHeadersPerRow = (Children.Count + _numRows - 1) / _numRows;
    }

    private void ArrangeHorizontal(Size finalSize)
    {
        var x = 0.0;

        foreach (var child in Children)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }
    }

    private void ArrangeVertical(Size finalSize)
    {
        var y = 0.0;

        foreach (var child in Children)
        {
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
    }

    private void ArrangeWrapped(Size finalSize)
    {
        var x = 0.0;
        var y = 0.0;
        var currentRowWidth = 0.0;
        var maxRowWidth = finalSize.Width;

        foreach (var child in Children)
        {
            var childWidth = child.DesiredSize.Width;

            if (currentRowWidth + childWidth > maxRowWidth && currentRowWidth > 0)
            {
                x = 0;
                y += _rowHeight;
                currentRowWidth = 0;
            }

            child.Arrange(new Rect(x, y, childWidth, _rowHeight));
            x += childWidth;
            currentRowWidth += childWidth;
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Specifies the dock position.
/// </summary>
public enum Dock
{
    /// <summary>
    /// Dock at the left.
    /// </summary>
    Left,

    /// <summary>
    /// Dock at the top.
    /// </summary>
    Top,

    /// <summary>
    /// Dock at the right.
    /// </summary>
    Right,

    /// <summary>
    /// Dock at the bottom.
    /// </summary>
    Bottom
}
