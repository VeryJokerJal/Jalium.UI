using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a scrollable area that can contain other visible elements.
/// </summary>
[ContentProperty("Content")]
public class ScrollViewer : Control
{
    #region Fields

    private UIElement? _content;
    private double _horizontalOffset;
    private double _verticalOffset;
    private double _extentWidth;
    private double _extentHeight;
    private double _viewportWidth;
    private double _viewportHeight;

    /// <summary>
    /// Default line scroll amount in pixels.
    /// </summary>
    public const double LineScrollAmount = 16.0;

    /// <summary>
    /// Mouse wheel scroll amount in pixels.
    /// </summary>
    public const double WheelScrollAmount = 48.0;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Disabled, OnScrollBarVisibilityChanged));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Visible, OnScrollBarVisibilityChanged));

    /// <summary>
    /// Identifies the CanContentScroll dependency property.
    /// </summary>
    public static readonly DependencyProperty CanContentScrollProperty =
        DependencyProperty.Register(nameof(CanContentScroll), typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the PanningMode dependency property.
    /// </summary>
    public static readonly DependencyProperty PanningModeProperty =
        DependencyProperty.Register(nameof(PanningMode), typeof(PanningMode), typeof(ScrollViewer),
            new PropertyMetadata(PanningMode.Both));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the horizontal scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)(GetValue(HorizontalScrollBarVisibilityProperty) ?? ScrollBarVisibility.Disabled);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)(GetValue(VerticalScrollBarVisibilityProperty) ?? ScrollBarVisibility.Visible);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the content can scroll by items rather than pixels.
    /// </summary>
    public bool CanContentScroll
    {
        get => (bool)(GetValue(CanContentScrollProperty) ?? false);
        set => SetValue(CanContentScrollProperty, value);
    }

    /// <summary>
    /// Gets or sets the panning mode for touch interaction.
    /// </summary>
    public PanningMode PanningMode
    {
        get => (PanningMode)(GetValue(PanningModeProperty) ?? PanningMode.Both);
        set => SetValue(PanningModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the content of the ScrollViewer.
    /// </summary>
    public UIElement? Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                // Remove old content from visual tree
                if (_content != null)
                {
                    RemoveContentChild(_content);
                }

                _content = value;

                // Add new content to visual tree
                if (_content != null)
                {
                    AddContentChild(_content);
                }

                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset => _horizontalOffset;

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset => _verticalOffset;

    /// <summary>
    /// Gets the width of the scrollable content.
    /// </summary>
    public double ExtentWidth => _extentWidth;

    /// <summary>
    /// Gets the height of the scrollable content.
    /// </summary>
    public double ExtentHeight => _extentHeight;

    /// <summary>
    /// Gets the width of the viewport (visible area).
    /// </summary>
    public double ViewportWidth => _viewportWidth;

    /// <summary>
    /// Gets the height of the viewport (visible area).
    /// </summary>
    public double ViewportHeight => _viewportHeight;

    /// <summary>
    /// Gets a value indicating whether the horizontal scroll bar is at the maximum position.
    /// </summary>
    public bool IsAtHorizontalEnd => _horizontalOffset >= _extentWidth - _viewportWidth;

    /// <summary>
    /// Gets a value indicating whether the vertical scroll bar is at the maximum position.
    /// </summary>
    public bool IsAtVerticalEnd => _verticalOffset >= _extentHeight - _viewportHeight;

    /// <summary>
    /// Gets the maximum horizontal scroll offset.
    /// </summary>
    public double ScrollableWidth => Math.Max(0, _extentWidth - _viewportWidth);

    /// <summary>
    /// Gets the maximum vertical scroll offset.
    /// </summary>
    public double ScrollableHeight => Math.Max(0, _extentHeight - _viewportHeight);

    /// <summary>
    /// Gets a value indicating whether the content can be scrolled horizontally.
    /// </summary>
    public bool CanScrollHorizontally => HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && _extentWidth > _viewportWidth;

    /// <summary>
    /// Gets a value indicating whether the content can be scrolled vertically.
    /// </summary>
    public bool CanScrollVertically => VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && _extentHeight > _viewportHeight;

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ScrollChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ScrollChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ScrollChanged), RoutingStrategy.Bubble,
            typeof(ScrollChangedEventHandler), typeof(ScrollViewer));

    /// <summary>
    /// Occurs when the scroll position changes.
    /// </summary>
    public event ScrollChangedEventHandler ScrollChanged
    {
        add => AddHandler(ScrollChangedEvent, value);
        remove => RemoveHandler(ScrollChangedEvent, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// The default scroll bar width/height.
    /// </summary>
    private const double ScrollBarSize = 16.0;

    /// <summary>
    /// The scroll button height (up/down arrows).
    /// </summary>
    private const double ScrollButtonSize = 16.0;

    /// <summary>
    /// Tracks if we're dragging the vertical scrollbar thumb.
    /// </summary>
    private bool _isDraggingVerticalThumb;

    /// <summary>
    /// Tracks if we're dragging the horizontal scrollbar thumb.
    /// </summary>
    private bool _isDraggingHorizontalThumb;

    /// <summary>
    /// The starting mouse Y position when dragging vertical thumb.
    /// </summary>
    private double _dragStartY;

    /// <summary>
    /// The starting mouse X position when dragging horizontal thumb.
    /// </summary>
    private double _dragStartX;

    /// <summary>
    /// The starting scroll offset when dragging.
    /// </summary>
    private double _dragStartOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollViewer"/> class.
    /// </summary>
    public ScrollViewer()
    {
        // ScrollViewer clips content by default
        ClipToBounds = true;

        // Register for input events
        AddHandler(MouseWheelEvent, new RoutedEventHandler(HandleMouseWheel));
        AddHandler(MouseDownEvent, new RoutedEventHandler(HandleMouseDown));
        AddHandler(MouseUpEvent, new RoutedEventHandler(HandleMouseUp));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(HandleMouseMove));

        // Register for BringIntoView requests
        AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(HandleRequestBringIntoView));
    }

    private void HandleRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is FrameworkElement targetElement)
        {
            MakeVisible(targetElement, e.TargetRect);
            e.Handled = true;
        }
    }

    private void HandleMouseWheel(object sender, RoutedEventArgs e)
    {
        if (e is MouseWheelEventArgs wheelArgs)
        {
            OnMouseWheel(wheelArgs);
        }
    }

    private void HandleMouseDown(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            OnScrollBarMouseDown(mouseArgs);
        }
    }

    private void HandleMouseUp(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            OnScrollBarMouseUp(mouseArgs);
        }
    }

    private void HandleMouseMove(object sender, RoutedEventArgs e)
    {
        if (e is MouseEventArgs mouseArgs)
        {
            OnScrollBarMouseMove(mouseArgs);
        }
    }

    /// <summary>
    /// Returns a clip geometry for the viewport area.
    /// Respects the ClipToBounds property - when false, no clipping is applied.
    /// </summary>
    protected internal override object? GetLayoutClip()
    {
        if (!ClipToBounds)
        {
            return null;
        }

        // Clip to the viewport area (excluding scrollbar space)
        var clipRect = new Rect(0, 0, _viewportWidth, _viewportHeight);
        return new Media.RectangleGeometry(clipRect);
    }

    #endregion

    #region Scroll Methods

    /// <summary>
    /// Scrolls to the specified horizontal offset.
    /// </summary>
    /// <param name="offset">The horizontal offset.</param>
    public void ScrollToHorizontalOffset(double offset)
    {
        var oldOffset = _horizontalOffset;
        _horizontalOffset = Math.Clamp(offset, 0, ScrollableWidth);

        if (oldOffset != _horizontalOffset)
        {
            InvalidateArrange();
            RaiseScrollChanged(oldOffset, _verticalOffset);
        }
    }

    /// <summary>
    /// Scrolls to the specified vertical offset.
    /// </summary>
    /// <param name="offset">The vertical offset.</param>
    public void ScrollToVerticalOffset(double offset)
    {
        var oldOffset = _verticalOffset;
        _verticalOffset = Math.Clamp(offset, 0, ScrollableHeight);

        if (oldOffset != _verticalOffset)
        {
            InvalidateArrange();
            RaiseScrollChanged(_horizontalOffset, oldOffset);
        }
    }

    /// <summary>
    /// Scrolls up by one line.
    /// </summary>
    public void LineUp()
    {
        ScrollToVerticalOffset(_verticalOffset - LineScrollAmount);
    }

    /// <summary>
    /// Scrolls down by one line.
    /// </summary>
    public void LineDown()
    {
        ScrollToVerticalOffset(_verticalOffset + LineScrollAmount);
    }

    /// <summary>
    /// Scrolls left by one line.
    /// </summary>
    public void LineLeft()
    {
        ScrollToHorizontalOffset(_horizontalOffset - LineScrollAmount);
    }

    /// <summary>
    /// Scrolls right by one line.
    /// </summary>
    public void LineRight()
    {
        ScrollToHorizontalOffset(_horizontalOffset + LineScrollAmount);
    }

    /// <summary>
    /// Scrolls up by one page.
    /// </summary>
    public void PageUp()
    {
        ScrollToVerticalOffset(_verticalOffset - _viewportHeight);
    }

    /// <summary>
    /// Scrolls down by one page.
    /// </summary>
    public void PageDown()
    {
        ScrollToVerticalOffset(_verticalOffset + _viewportHeight);
    }

    /// <summary>
    /// Scrolls left by one page.
    /// </summary>
    public void PageLeft()
    {
        ScrollToHorizontalOffset(_horizontalOffset - _viewportWidth);
    }

    /// <summary>
    /// Scrolls right by one page.
    /// </summary>
    public void PageRight()
    {
        ScrollToHorizontalOffset(_horizontalOffset + _viewportWidth);
    }

    /// <summary>
    /// Scrolls to the beginning (left edge).
    /// </summary>
    public void ScrollToHome()
    {
        ScrollToHorizontalOffset(0);
    }

    /// <summary>
    /// Scrolls to the end (right edge).
    /// </summary>
    public void ScrollToEnd()
    {
        ScrollToHorizontalOffset(ScrollableWidth);
    }

    /// <summary>
    /// Scrolls to the top.
    /// </summary>
    public void ScrollToTop()
    {
        ScrollToVerticalOffset(0);
    }

    /// <summary>
    /// Scrolls to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        ScrollToVerticalOffset(ScrollableHeight);
    }

    /// <summary>
    /// Scrolls to make the specified element visible.
    /// </summary>
    /// <param name="element">The element to scroll into view.</param>
    public void ScrollToElement(UIElement element)
    {
        if (element == null || _content == null)
            return;

        if (element is FrameworkElement fe)
        {
            MakeVisible(fe, new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
        }
    }

    /// <summary>
    /// Scrolls the viewport to make the specified rectangle of the target element visible.
    /// </summary>
    /// <param name="element">The element to make visible.</param>
    /// <param name="targetRect">The rectangle within the element to make visible.</param>
    public void MakeVisible(FrameworkElement element, Rect targetRect)
    {
        if (element == null || _content == null)
            return;

        // Calculate the element's position relative to the content
        var elementPosition = CalculatePositionRelativeToContent(element);
        if (!elementPosition.HasValue)
            return;

        // Calculate the rectangle to bring into view in content coordinates
        var rectInContent = new Rect(
            elementPosition.Value.X + targetRect.X,
            elementPosition.Value.Y + targetRect.Y,
            targetRect.Width,
            targetRect.Height);

        // Calculate the new scroll offsets needed to bring the rectangle into view
        var newHorizontalOffset = _horizontalOffset;
        var newVerticalOffset = _verticalOffset;

        // Horizontal scrolling
        if (CanScrollHorizontally)
        {
            var viewportLeft = _horizontalOffset;
            var viewportRight = _horizontalOffset + _viewportWidth;

            if (rectInContent.Left < viewportLeft)
            {
                // Element is to the left of the viewport - scroll left
                newHorizontalOffset = rectInContent.Left;
            }
            else if (rectInContent.Right > viewportRight)
            {
                // Element is to the right of the viewport - scroll right
                // Try to show the entire element, but if it's larger than viewport, show the left edge
                if (rectInContent.Width <= _viewportWidth)
                {
                    newHorizontalOffset = rectInContent.Right - _viewportWidth;
                }
                else
                {
                    newHorizontalOffset = rectInContent.Left;
                }
            }
        }

        // Vertical scrolling
        if (CanScrollVertically)
        {
            var viewportTop = _verticalOffset;
            var viewportBottom = _verticalOffset + _viewportHeight;

            if (rectInContent.Top < viewportTop)
            {
                // Element is above the viewport - scroll up
                newVerticalOffset = rectInContent.Top;
            }
            else if (rectInContent.Bottom > viewportBottom)
            {
                // Element is below the viewport - scroll down
                // Try to show the entire element, but if it's larger than viewport, show the top edge
                if (rectInContent.Height <= _viewportHeight)
                {
                    newVerticalOffset = rectInContent.Bottom - _viewportHeight;
                }
                else
                {
                    newVerticalOffset = rectInContent.Top;
                }
            }
        }

        // Apply the new scroll offsets
        if (newHorizontalOffset != _horizontalOffset)
        {
            ScrollToHorizontalOffset(newHorizontalOffset);
        }
        if (newVerticalOffset != _verticalOffset)
        {
            ScrollToVerticalOffset(newVerticalOffset);
        }
    }

    /// <summary>
    /// Calculates the position of an element relative to the content of this ScrollViewer.
    /// </summary>
    private Point? CalculatePositionRelativeToContent(FrameworkElement element)
    {
        if (_content == null)
            return null;

        // Walk up the visual tree from the element to find the content root
        double x = 0;
        double y = 0;

        Visual? current = element;
        while (current != null)
        {
            if (current == _content)
            {
                // We've reached the content - return the accumulated offset
                return new Point(x, y);
            }

            if (current is FrameworkElement fe)
            {
                x += fe.VisualBounds.X;
                y += fe.VisualBounds.Y;
            }

            current = current.VisualParent;
        }

        // Element is not a descendant of our content
        return null;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_content == null)
        {
            _extentWidth = 0;
            _extentHeight = 0;
            return Size.Empty;
        }

        // Reserve space for scrollbars if they might be needed
        var reserveVertical = VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                              VerticalScrollBarVisibility == ScrollBarVisibility.Auto;
        var reserveHorizontal = HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                HorizontalScrollBarVisibility == ScrollBarVisibility.Auto;

        // Calculate available space for content (accounting for potential scrollbars)
        var contentAvailableWidth = availableSize.Width - (reserveVertical ? ScrollBarSize : 0);
        var contentAvailableHeight = availableSize.Height - (reserveHorizontal ? ScrollBarSize : 0);

        // Determine available size for content
        var contentAvailable = new Size(contentAvailableWidth, contentAvailableHeight);

        // If scrolling is enabled in a direction, give infinite space in that direction
        if (HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
        {
            contentAvailable = new Size(double.PositiveInfinity, contentAvailable.Height);
        }

        if (VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
        {
            contentAvailable = new Size(contentAvailable.Width, double.PositiveInfinity);
        }

        // Measure content
        _content.Measure(contentAvailable);
        var contentDesired = _content.DesiredSize;

        // Update extent
        _extentWidth = contentDesired.Width;
        _extentHeight = contentDesired.Height;

        // Return the smaller of content size and available size
        var resultWidth = Math.Min(contentDesired.Width + (reserveVertical ? ScrollBarSize : 0), availableSize.Width);
        var resultHeight = Math.Min(contentDesired.Height + (reserveHorizontal ? ScrollBarSize : 0), availableSize.Height);

        return new Size(resultWidth, resultHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Calculate if scrollbars are needed
        var needsVerticalScrollBar = VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                      (VerticalScrollBarVisibility == ScrollBarVisibility.Auto && _extentHeight > finalSize.Height);
        var needsHorizontalScrollBar = HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                        (HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && _extentWidth > finalSize.Width);

        // Calculate viewport size (excluding scrollbar space)
        _viewportWidth = finalSize.Width - (needsVerticalScrollBar ? ScrollBarSize : 0);
        _viewportHeight = finalSize.Height - (needsHorizontalScrollBar ? ScrollBarSize : 0);

        // Clamp scroll offsets
        _horizontalOffset = Math.Clamp(_horizontalOffset, 0, Math.Max(0, _extentWidth - _viewportWidth));
        _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, _extentHeight - _viewportHeight));

        if (_content != null)
        {
            // Arrange content with offset (content area excludes scrollbar space)
            var contentWidth = Math.Max(_extentWidth, _viewportWidth);
            var contentHeight = Math.Max(_extentHeight, _viewportHeight);

            var arrangeRect = new Rect(
                -_horizontalOffset,
                -_verticalOffset,
                contentWidth,
                contentHeight);

            _content.Arrange(arrangeRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount => _content != null ? 1 : 0;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index == 0 && _content != null)
            return _content;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private void AddContentChild(UIElement child)
    {
        if (child is Visual visual)
        {
            AddVisualChild(visual);
        }
    }

    private void RemoveContentChild(UIElement child)
    {
        if (child is Visual visual)
        {
            RemoveVisualChild(visual);
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.Left > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            dc.DrawRectangle(null, pen, bounds);
        }

        // Content renders itself through the visual tree
        // Scrollbars are drawn after children in OnPostRender
    }

    /// <summary>
    /// Override OnPostRender to draw scrollbars AFTER children.
    /// </summary>
    protected override void OnPostRender(object drawingContext)
    {
        base.OnPostRender(drawingContext);

        if (drawingContext is DrawingContext dc)
        {
            var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

            // Draw scrollbars on top of children
            DrawScrollBars(dc, bounds);
        }
    }

    private void DrawScrollBars(DrawingContext dc, Rect bounds)
    {
        var scrollBarBgBrush = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        var buttonBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        var thumbBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        var arrowBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));

        var needsVerticalScrollBar = NeedsVerticalScrollBar;
        var needsHorizontalScrollBar = NeedsHorizontalScrollBar;

        // Draw vertical scroll bar
        if (needsVerticalScrollBar)
        {
            var scrollBarHeight = bounds.Height - (needsHorizontalScrollBar ? ScrollBarSize : 0);
            var scrollBarRect = new Rect(
                bounds.Width - ScrollBarSize,
                0,
                ScrollBarSize,
                scrollBarHeight);

            // Background
            dc.DrawRectangle(scrollBarBgBrush, null, scrollBarRect);

            // Up button
            var upButtonRect = new Rect(scrollBarRect.X, 0, ScrollBarSize, ScrollButtonSize);
            dc.DrawRectangle(buttonBrush, null, upButtonRect);
            DrawUpArrow(dc, upButtonRect, arrowBrush);

            // Down button
            var downButtonRect = new Rect(scrollBarRect.X, scrollBarHeight - ScrollButtonSize, ScrollBarSize, ScrollButtonSize);
            dc.DrawRectangle(buttonBrush, null, downButtonRect);
            DrawDownArrow(dc, downButtonRect, arrowBrush);

            // Thumb track area (between buttons)
            var trackHeight = scrollBarHeight - 2 * ScrollButtonSize;
            if (trackHeight > 0 && ScrollableHeight > 0)
            {
                var thumbHeight = Math.Max(20, (_viewportHeight / _extentHeight) * trackHeight);
                var thumbTop = ScrollButtonSize + (_verticalOffset / ScrollableHeight) * (trackHeight - thumbHeight);

                var thumbRect = new Rect(
                    scrollBarRect.X + 2,
                    thumbTop + 2,
                    ScrollBarSize - 4,
                    thumbHeight - 4);

                dc.DrawRoundedRectangle(thumbBrush, null, thumbRect, 3, 3);
            }
        }

        // Draw horizontal scroll bar
        if (needsHorizontalScrollBar)
        {
            var scrollBarWidth = bounds.Width - (needsVerticalScrollBar ? ScrollBarSize : 0);
            var scrollBarRect = new Rect(
                0,
                bounds.Height - ScrollBarSize,
                scrollBarWidth,
                ScrollBarSize);

            // Background
            dc.DrawRectangle(scrollBarBgBrush, null, scrollBarRect);

            // Left button
            var leftButtonRect = new Rect(0, scrollBarRect.Y, ScrollButtonSize, ScrollBarSize);
            dc.DrawRectangle(buttonBrush, null, leftButtonRect);
            DrawLeftArrow(dc, leftButtonRect, arrowBrush);

            // Right button
            var rightButtonRect = new Rect(scrollBarWidth - ScrollButtonSize, scrollBarRect.Y, ScrollButtonSize, ScrollBarSize);
            dc.DrawRectangle(buttonBrush, null, rightButtonRect);
            DrawRightArrow(dc, rightButtonRect, arrowBrush);

            // Thumb track area (between buttons)
            var trackWidth = scrollBarWidth - 2 * ScrollButtonSize;
            if (trackWidth > 0 && ScrollableWidth > 0)
            {
                var thumbWidth = Math.Max(20, (_viewportWidth / _extentWidth) * trackWidth);
                var thumbLeft = ScrollButtonSize + (_horizontalOffset / ScrollableWidth) * (trackWidth - thumbWidth);

                var thumbRect = new Rect(
                    thumbLeft + 2,
                    scrollBarRect.Y + 2,
                    thumbWidth - 4,
                    ScrollBarSize - 4);

                dc.DrawRoundedRectangle(thumbBrush, null, thumbRect, 3, 3);
            }
        }
    }

    private void DrawUpArrow(DrawingContext dc, Rect bounds, Brush brush)
    {
        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        var size = 3.0;
        var pen = new Pen(brush, 2);

        dc.DrawLine(pen, new Point(cx - size, cy + size / 2), new Point(cx, cy - size / 2));
        dc.DrawLine(pen, new Point(cx, cy - size / 2), new Point(cx + size, cy + size / 2));
    }

    private void DrawDownArrow(DrawingContext dc, Rect bounds, Brush brush)
    {
        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        var size = 3.0;
        var pen = new Pen(brush, 2);

        dc.DrawLine(pen, new Point(cx - size, cy - size / 2), new Point(cx, cy + size / 2));
        dc.DrawLine(pen, new Point(cx, cy + size / 2), new Point(cx + size, cy - size / 2));
    }

    private void DrawLeftArrow(DrawingContext dc, Rect bounds, Brush brush)
    {
        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        var size = 3.0;
        var pen = new Pen(brush, 2);

        dc.DrawLine(pen, new Point(cx + size / 2, cy - size), new Point(cx - size / 2, cy));
        dc.DrawLine(pen, new Point(cx - size / 2, cy), new Point(cx + size / 2, cy + size));
    }

    private void DrawRightArrow(DrawingContext dc, Rect bounds, Brush brush)
    {
        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        var size = 3.0;
        var pen = new Pen(brush, 2);

        dc.DrawLine(pen, new Point(cx - size / 2, cy - size), new Point(cx + size / 2, cy));
        dc.DrawLine(pen, new Point(cx + size / 2, cy), new Point(cx - size / 2, cy + size));
    }

    /// <summary>
    /// Gets whether the vertical scrollbar is needed.
    /// </summary>
    private bool NeedsVerticalScrollBar =>
        VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
        (VerticalScrollBarVisibility == ScrollBarVisibility.Auto && _extentHeight > _viewportHeight);

    /// <summary>
    /// Gets whether the horizontal scrollbar is needed.
    /// </summary>
    private bool NeedsHorizontalScrollBar =>
        HorizontalScrollBarVisibility == ScrollBarVisibility.Visible ||
        (HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && _extentWidth > _viewportWidth);

    #endregion

    #region Input Handling

    /// <summary>
    /// Handles mouse wheel events for scrolling.
    /// </summary>
    protected virtual void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        if (CanScrollVertically)
        {
            var delta = -e.Delta / 120.0 * WheelScrollAmount;
            ScrollToVerticalOffset(_verticalOffset + delta);
            e.Handled = true;
        }
        else if (CanScrollHorizontally)
        {
            var delta = -e.Delta / 120.0 * WheelScrollAmount;
            ScrollToHorizontalOffset(_horizontalOffset + delta);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles mouse down on scrollbar elements.
    /// </summary>
    private void OnScrollBarMouseDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Check vertical scrollbar
        if (NeedsVerticalScrollBar)
        {
            var scrollBarX = bounds.Width - ScrollBarSize;
            if (pos.X >= scrollBarX)
            {
                var scrollBarHeight = bounds.Height - (NeedsHorizontalScrollBar ? ScrollBarSize : 0);

                // Up button
                if (pos.Y < ScrollButtonSize)
                {
                    LineUp();
                    e.Handled = true;
                    return;
                }

                // Down button
                if (pos.Y >= scrollBarHeight - ScrollButtonSize && pos.Y < scrollBarHeight)
                {
                    LineDown();
                    e.Handled = true;
                    return;
                }

                // Thumb track area
                var trackHeight = scrollBarHeight - 2 * ScrollButtonSize;
                if (trackHeight > 0 && ScrollableHeight > 0)
                {
                    var thumbHeight = Math.Max(20, (_viewportHeight / _extentHeight) * trackHeight);
                    var thumbTop = ScrollButtonSize + (_verticalOffset / ScrollableHeight) * (trackHeight - thumbHeight);

                    // Check if clicking on thumb
                    if (pos.Y >= thumbTop && pos.Y < thumbTop + thumbHeight)
                    {
                        CaptureMouse();
                        _isDraggingVerticalThumb = true;
                        _dragStartY = pos.Y;
                        _dragStartOffset = _verticalOffset;
                        e.Handled = true;
                        return;
                    }

                    // Click above thumb - page up
                    if (pos.Y < thumbTop && pos.Y >= ScrollButtonSize)
                    {
                        PageUp();
                        e.Handled = true;
                        return;
                    }

                    // Click below thumb - page down
                    if (pos.Y >= thumbTop + thumbHeight && pos.Y < scrollBarHeight - ScrollButtonSize)
                    {
                        PageDown();
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // Check horizontal scrollbar
        if (NeedsHorizontalScrollBar)
        {
            var scrollBarY = bounds.Height - ScrollBarSize;
            var scrollBarWidth = bounds.Width - (NeedsVerticalScrollBar ? ScrollBarSize : 0);

            if (pos.Y >= scrollBarY && pos.X < scrollBarWidth)
            {
                // Left button
                if (pos.X < ScrollButtonSize)
                {
                    LineLeft();
                    e.Handled = true;
                    return;
                }

                // Right button
                if (pos.X >= scrollBarWidth - ScrollButtonSize)
                {
                    LineRight();
                    e.Handled = true;
                    return;
                }

                // Thumb track area
                var trackWidth = scrollBarWidth - 2 * ScrollButtonSize;
                if (trackWidth > 0 && ScrollableWidth > 0)
                {
                    var thumbWidth = Math.Max(20, (_viewportWidth / _extentWidth) * trackWidth);
                    var thumbLeft = ScrollButtonSize + (_horizontalOffset / ScrollableWidth) * (trackWidth - thumbWidth);

                    // Check if clicking on thumb
                    if (pos.X >= thumbLeft && pos.X < thumbLeft + thumbWidth)
                    {
                        CaptureMouse();
                        _isDraggingHorizontalThumb = true;
                        _dragStartX = pos.X;
                        _dragStartOffset = _horizontalOffset;
                        e.Handled = true;
                        return;
                    }

                    // Click left of thumb - page left
                    if (pos.X < thumbLeft && pos.X >= ScrollButtonSize)
                    {
                        PageLeft();
                        e.Handled = true;
                        return;
                    }

                    // Click right of thumb - page right
                    if (pos.X >= thumbLeft + thumbWidth && pos.X < scrollBarWidth - ScrollButtonSize)
                    {
                        PageRight();
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Handles mouse up to stop dragging.
    /// </summary>
    private void OnScrollBarMouseUp(MouseButtonEventArgs e)
    {
        if (_isDraggingVerticalThumb || _isDraggingHorizontalThumb)
        {
            _isDraggingVerticalThumb = false;
            _isDraggingHorizontalThumb = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isDraggingVerticalThumb || _isDraggingHorizontalThumb)
        {
            _isDraggingVerticalThumb = false;
            _isDraggingHorizontalThumb = false;
        }
    }

    /// <summary>
    /// Handles mouse move for thumb dragging.
    /// </summary>
    private void OnScrollBarMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_isDraggingVerticalThumb)
        {
            var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
            var scrollBarHeight = bounds.Height - (NeedsHorizontalScrollBar ? ScrollBarSize : 0);
            var trackHeight = scrollBarHeight - 2 * ScrollButtonSize;

            if (trackHeight > 0 && ScrollableHeight > 0)
            {
                var thumbHeight = Math.Max(20, (_viewportHeight / _extentHeight) * trackHeight);
                var deltaY = pos.Y - _dragStartY;
                var scrollRange = trackHeight - thumbHeight;

                if (scrollRange > 0)
                {
                    var newOffset = _dragStartOffset + (deltaY / scrollRange) * ScrollableHeight;
                    ScrollToVerticalOffset(newOffset);
                }
            }
            e.Handled = true;
        }

        if (_isDraggingHorizontalThumb)
        {
            var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
            var scrollBarWidth = bounds.Width - (NeedsVerticalScrollBar ? ScrollBarSize : 0);
            var trackWidth = scrollBarWidth - 2 * ScrollButtonSize;

            if (trackWidth > 0 && ScrollableWidth > 0)
            {
                var thumbWidth = Math.Max(20, (_viewportWidth / _extentWidth) * trackWidth);
                var deltaX = pos.X - _dragStartX;
                var scrollRange = trackWidth - thumbWidth;

                if (scrollRange > 0)
                {
                    var newOffset = _dragStartOffset + (deltaX / scrollRange) * ScrollableWidth;
                    ScrollToHorizontalOffset(newOffset);
                }
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles key down events for keyboard scrolling.
    /// </summary>
    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
            return;

        switch (e.Key)
        {
            case Key.Up:
                LineUp();
                e.Handled = true;
                break;
            case Key.Down:
                LineDown();
                e.Handled = true;
                break;
            case Key.Left:
                LineLeft();
                e.Handled = true;
                break;
            case Key.Right:
                LineRight();
                e.Handled = true;
                break;
            case Key.PageUp:
                PageUp();
                e.Handled = true;
                break;
            case Key.PageDown:
                PageDown();
                e.Handled = true;
                break;
            case Key.Home:
                if (e.IsControlDown)
                    ScrollToTop();
                else
                    ScrollToHome();
                e.Handled = true;
                break;
            case Key.End:
                if (e.IsControlDown)
                    ScrollToBottom();
                else
                    ScrollToEnd();
                e.Handled = true;
                break;
        }
    }

    #endregion

    #region Private Methods

    private void RaiseScrollChanged(double oldHorizontalOffset, double oldVerticalOffset)
    {
        var e = new ScrollChangedEventArgs(ScrollChangedEvent, this)
        {
            HorizontalChange = _horizontalOffset - oldHorizontalOffset,
            VerticalChange = _verticalOffset - oldVerticalOffset,
            HorizontalOffset = _horizontalOffset,
            VerticalOffset = _verticalOffset,
            ViewportWidth = _viewportWidth,
            ViewportHeight = _viewportHeight,
            ExtentWidth = _extentWidth,
            ExtentHeight = _extentHeight
        };

        RaiseEvent(e);
    }

    private static void OnScrollBarVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Provides data for the ScrollChanged event.
/// </summary>
public class ScrollChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the horizontal offset change.
    /// </summary>
    public double HorizontalChange { get; init; }

    /// <summary>
    /// Gets the vertical offset change.
    /// </summary>
    public double VerticalChange { get; init; }

    /// <summary>
    /// Gets the current horizontal offset.
    /// </summary>
    public double HorizontalOffset { get; init; }

    /// <summary>
    /// Gets the current vertical offset.
    /// </summary>
    public double VerticalOffset { get; init; }

    /// <summary>
    /// Gets the viewport width.
    /// </summary>
    public double ViewportWidth { get; init; }

    /// <summary>
    /// Gets the viewport height.
    /// </summary>
    public double ViewportHeight { get; init; }

    /// <summary>
    /// Gets the extent width.
    /// </summary>
    public double ExtentWidth { get; init; }

    /// <summary>
    /// Gets the extent height.
    /// </summary>
    public double ExtentHeight { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollChangedEventArgs"/> class.
    /// </summary>
    public ScrollChangedEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
    }
}

/// <summary>
/// Delegate for handling ScrollChanged events.
/// </summary>
public delegate void ScrollChangedEventHandler(object sender, ScrollChangedEventArgs e);

/// <summary>
/// Specifies how touch panning works in a ScrollViewer.
/// </summary>
public enum PanningMode
{
    /// <summary>
    /// Panning is disabled.
    /// </summary>
    None,

    /// <summary>
    /// Horizontal panning only.
    /// </summary>
    HorizontalOnly,

    /// <summary>
    /// Vertical panning only.
    /// </summary>
    VerticalOnly,

    /// <summary>
    /// Both horizontal and vertical panning.
    /// </summary>
    Both,

    /// <summary>
    /// First determines the panning direction from the initial touch.
    /// </summary>
    HorizontalFirst,

    /// <summary>
    /// First determines the panning direction from the initial touch.
    /// </summary>
    VerticalFirst
}

/// <summary>
/// Specifies the visibility of a scroll bar.
/// </summary>
public enum ScrollBarVisibility
{
    /// <summary>
    /// The scroll bar is disabled and not visible.
    /// </summary>
    Disabled,

    /// <summary>
    /// The scroll bar appears only when needed.
    /// </summary>
    Auto,

    /// <summary>
    /// The scroll bar is never visible.
    /// </summary>
    Hidden,

    /// <summary>
    /// The scroll bar is always visible.
    /// </summary>
    Visible
}
