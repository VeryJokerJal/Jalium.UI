namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Manages virtualized scrolling state and provides scroll-related calculations.
/// </summary>
internal sealed class ScrollManager
{
    private double _verticalOffset;
    private double _horizontalOffset;
    private double _viewportWidth;
    private double _viewportHeight;

    /// <summary>
    /// Gets or sets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset
    {
        get => _verticalOffset;
        set => _verticalOffset = Math.Max(0, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set => _horizontalOffset = Math.Max(0, value);
    }

    /// <summary>
    /// Gets or sets the viewport width.
    /// </summary>
    public double ViewportWidth
    {
        get => _viewportWidth;
        set => _viewportWidth = value;
    }

    /// <summary>
    /// Gets or sets the viewport height.
    /// </summary>
    public double ViewportHeight
    {
        get => _viewportHeight;
        set => _viewportHeight = value;
    }

    /// <summary>
    /// Updates the viewport dimensions.
    /// </summary>
    public void UpdateViewport(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>
    /// Scrolls vertically by the specified number of lines.
    /// </summary>
    public void ScrollByLines(double lineCount, double lineHeight, double totalContentHeight)
    {
        var delta = lineCount * lineHeight;
        VerticalOffset = Math.Clamp(
            _verticalOffset + delta,
            0,
            Math.Max(0, totalContentHeight - _viewportHeight));
    }

    /// <summary>
    /// Scrolls to make the specified line visible.
    /// </summary>
    public void ScrollToLine(int lineNumber, double lineHeight)
    {
        VerticalOffset = (lineNumber - 1) * lineHeight;
    }

    /// <summary>
    /// Ensures a vertical position is visible within the viewport.
    /// </summary>
    public void EnsureVisible(double y, double itemHeight, double totalContentHeight)
    {
        if (y < _verticalOffset)
        {
            VerticalOffset = y;
        }
        else if (y + itemHeight > _verticalOffset + _viewportHeight)
        {
            VerticalOffset = y + itemHeight - _viewportHeight;
        }

        VerticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, totalContentHeight - _viewportHeight));
    }

    /// <summary>
    /// Gets the first visible line number (1-based).
    /// </summary>
    public int GetFirstVisibleLine(double lineHeight)
    {
        if (lineHeight <= 0) return 1;
        return Math.Max(1, (int)(_verticalOffset / lineHeight) + 1);
    }

    /// <summary>
    /// Gets the last visible line number (1-based, capped by lineCount).
    /// </summary>
    public int GetLastVisibleLine(double lineHeight, int lineCount)
    {
        if (lineHeight <= 0) return 1;
        int last = (int)((_verticalOffset + _viewportHeight) / lineHeight) + 1;
        return Math.Min(last, lineCount);
    }
}
