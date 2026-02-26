namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Displays the content of a ScrollViewer control. Acts as a proxy for IScrollInfo
/// when the content implements it.
/// </summary>
public sealed class ScrollContentPresenter : ContentPresenter, IScrollInfo
{
    private IScrollInfo? _scrollInfo;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private Size _extent;
    private Size _viewport;
    private double _horizontalOffset;
    private double _verticalOffset;

    /// <summary>
    /// Gets or sets the IScrollInfo implementation that is exposed to the ScrollViewer.
    /// </summary>
    internal IScrollInfo? ScrollInfo
    {
        get => _scrollInfo;
        set
        {
            _scrollInfo = value;
            if (_scrollInfo != null)
            {
                _scrollInfo.ScrollOwner = ScrollOwner;
            }
        }
    }

    #region IScrollInfo Implementation

    /// <inheritdoc />
    public bool CanHorizontallyScroll
    {
        get => _scrollInfo?.CanHorizontallyScroll ?? _canHorizontallyScroll;
        set
        {
            if (_scrollInfo != null)
                _scrollInfo.CanHorizontallyScroll = value;
            else
                _canHorizontallyScroll = value;
        }
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll
    {
        get => _scrollInfo?.CanVerticallyScroll ?? _canVerticallyScroll;
        set
        {
            if (_scrollInfo != null)
                _scrollInfo.CanVerticallyScroll = value;
            else
                _canVerticallyScroll = value;
        }
    }

    /// <inheritdoc />
    public double ExtentWidth => _scrollInfo?.ExtentWidth ?? _extent.Width;

    /// <inheritdoc />
    public double ExtentHeight => _scrollInfo?.ExtentHeight ?? _extent.Height;

    /// <inheritdoc />
    public double ViewportWidth => _scrollInfo?.ViewportWidth ?? _viewport.Width;

    /// <inheritdoc />
    public double ViewportHeight => _scrollInfo?.ViewportHeight ?? _viewport.Height;

    /// <inheritdoc />
    public double HorizontalOffset => _scrollInfo?.HorizontalOffset ?? _horizontalOffset;

    /// <inheritdoc />
    public double VerticalOffset => _scrollInfo?.VerticalOffset ?? _verticalOffset;

    /// <inheritdoc />
    public ScrollViewer? ScrollOwner { get; set; }

    /// <inheritdoc />
    public void LineUp()
    {
        if (_scrollInfo != null) _scrollInfo.LineUp();
        else SetVerticalOffset(VerticalOffset - 16);
    }

    /// <inheritdoc />
    public void LineDown()
    {
        if (_scrollInfo != null) _scrollInfo.LineDown();
        else SetVerticalOffset(VerticalOffset + 16);
    }

    /// <inheritdoc />
    public void LineLeft()
    {
        if (_scrollInfo != null) _scrollInfo.LineLeft();
        else SetHorizontalOffset(HorizontalOffset - 16);
    }

    /// <inheritdoc />
    public void LineRight()
    {
        if (_scrollInfo != null) _scrollInfo.LineRight();
        else SetHorizontalOffset(HorizontalOffset + 16);
    }

    /// <inheritdoc />
    public void PageUp()
    {
        if (_scrollInfo != null) _scrollInfo.PageUp();
        else SetVerticalOffset(VerticalOffset - ViewportHeight);
    }

    /// <inheritdoc />
    public void PageDown()
    {
        if (_scrollInfo != null) _scrollInfo.PageDown();
        else SetVerticalOffset(VerticalOffset + ViewportHeight);
    }

    /// <inheritdoc />
    public void PageLeft()
    {
        if (_scrollInfo != null) _scrollInfo.PageLeft();
        else SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    }

    /// <inheritdoc />
    public void PageRight()
    {
        if (_scrollInfo != null) _scrollInfo.PageRight();
        else SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    }

    /// <inheritdoc />
    public void MouseWheelUp()
    {
        if (_scrollInfo != null) _scrollInfo.MouseWheelUp();
        else SetVerticalOffset(VerticalOffset - 48);
    }

    /// <inheritdoc />
    public void MouseWheelDown()
    {
        if (_scrollInfo != null) _scrollInfo.MouseWheelDown();
        else SetVerticalOffset(VerticalOffset + 48);
    }

    /// <inheritdoc />
    public void MouseWheelLeft()
    {
        if (_scrollInfo != null) _scrollInfo.MouseWheelLeft();
        else SetHorizontalOffset(HorizontalOffset - 48);
    }

    /// <inheritdoc />
    public void MouseWheelRight()
    {
        if (_scrollInfo != null) _scrollInfo.MouseWheelRight();
        else SetHorizontalOffset(HorizontalOffset + 48);
    }

    /// <inheritdoc />
    public void SetHorizontalOffset(double offset)
    {
        if (_scrollInfo != null)
        {
            _scrollInfo.SetHorizontalOffset(offset);
        }
        else
        {
            _horizontalOffset = Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
            ScrollOwner?.InvalidateArrange();
            InvalidateArrange();
        }
    }

    /// <inheritdoc />
    public void SetVerticalOffset(double offset)
    {
        if (_scrollInfo != null)
        {
            _scrollInfo.SetVerticalOffset(offset);
        }
        else
        {
            _verticalOffset = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
            ScrollOwner?.InvalidateArrange();
            InvalidateArrange();
        }
    }

    /// <inheritdoc />
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (_scrollInfo != null)
            return _scrollInfo.MakeVisible(visual, rectangle);

        return rectangle;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var content = Content as UIElement;
        if (content == null)
            return Size.Empty;

        // Check if content implements IScrollInfo
        if (content is IScrollInfo scrollInfo && scrollInfo != _scrollInfo)
        {
            ScrollInfo = scrollInfo;
        }

        // Give the content infinite size in scrollable directions
        var measureSize = new Size(
            CanHorizontallyScroll ? double.PositiveInfinity : availableSize.Width,
            CanVerticallyScroll ? double.PositiveInfinity : availableSize.Height
        );

        content.Measure(measureSize);
        var desiredSize = content.DesiredSize;

        // Update extent and viewport (only if we're handling scroll ourselves)
        if (_scrollInfo == null)
        {
            _extent = desiredSize;
            _viewport = availableSize;
            ScrollOwner?.InvalidateArrange();
        }

        // Return the smaller of desired and available
        return new Size(
            Math.Min(desiredSize.Width, availableSize.Width),
            Math.Min(desiredSize.Height, availableSize.Height)
        );
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = Content as UIElement;
        if (content == null)
            return finalSize;

        // Update viewport
        if (_scrollInfo == null)
        {
            _viewport = finalSize;
            ScrollOwner?.InvalidateArrange();
        }

        var contentSize = content.DesiredSize;
        var arrangeRect = new Rect(
            -HorizontalOffset,
            -VerticalOffset,
            Math.Max(contentSize.Width, finalSize.Width),
            Math.Max(contentSize.Height, finalSize.Height)
        );

        content.Arrange(arrangeRect);
        return finalSize;
    }

    #endregion
}
