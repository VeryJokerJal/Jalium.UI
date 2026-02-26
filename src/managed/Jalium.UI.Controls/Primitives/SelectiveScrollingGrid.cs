using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies which direction an element should scroll.
/// </summary>
public enum SelectiveScrollingOrientation
{
    /// <summary>
    /// Element does not scroll.
    /// </summary>
    None = 0,

    /// <summary>
    /// Element scrolls horizontally.
    /// </summary>
    Horizontal = 1,

    /// <summary>
    /// Element scrolls vertically.
    /// </summary>
    Vertical = 2,

    /// <summary>
    /// Element scrolls both horizontally and vertically.
    /// </summary>
    Both = 3
}

/// <summary>
/// A grid panel that allows child elements to selectively scroll in different directions.
/// Commonly used in DataGrid to freeze certain rows or columns.
/// </summary>
public sealed class SelectiveScrollingGrid : Grid
{
    #region Attached Properties

    /// <summary>
    /// Identifies the SelectiveScrollingOrientation attached property.
    /// </summary>
    public static readonly DependencyProperty SelectiveScrollingOrientationProperty =
        DependencyProperty.RegisterAttached(
            "SelectiveScrollingOrientation",
            typeof(SelectiveScrollingOrientation),
            typeof(SelectiveScrollingGrid),
            new PropertyMetadata(SelectiveScrollingOrientation.Both));

    /// <summary>
    /// Gets the selective scrolling orientation of an element.
    /// </summary>
    /// <param name="element">The element to get the orientation for.</param>
    /// <returns>The selective scrolling orientation.</returns>
    public static SelectiveScrollingOrientation GetSelectiveScrollingOrientation(DependencyObject element)
    {
        return (SelectiveScrollingOrientation)(element.GetValue(SelectiveScrollingOrientationProperty) ?? SelectiveScrollingOrientation.Both);
    }

    /// <summary>
    /// Sets the selective scrolling orientation of an element.
    /// </summary>
    /// <param name="element">The element to set the orientation for.</param>
    /// <param name="value">The orientation value.</param>
    public static void SetSelectiveScrollingOrientation(DependencyObject element, SelectiveScrollingOrientation value)
    {
        element.SetValue(SelectiveScrollingOrientationProperty, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset { get; set; }

    /// <summary>
    /// Gets or sets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset { get; set; }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var baseResult = base.ArrangeOverride(finalSize);

        // Apply scroll offsets to children based on their SelectiveScrollingOrientation
        foreach (UIElement child in Children)
        {
            var orientation = GetSelectiveScrollingOrientation(child);

            if (orientation != SelectiveScrollingOrientation.Both)
            {
                ApplyScrollOffset(child, orientation);
            }
        }

        return baseResult;
    }

    private void ApplyScrollOffset(UIElement child, SelectiveScrollingOrientation orientation)
    {
        // Create a transform to offset the child based on its scroll orientation
        var transform = child.RenderTransform as TranslateTransform ?? new TranslateTransform();

        switch (orientation)
        {
            case SelectiveScrollingOrientation.None:
                // Element doesn't scroll - counteract scroll offset
                transform.X = HorizontalOffset;
                transform.Y = VerticalOffset;
                break;

            case SelectiveScrollingOrientation.Horizontal:
                // Only horizontal scrolling - counteract vertical
                transform.X = 0;
                transform.Y = VerticalOffset;
                break;

            case SelectiveScrollingOrientation.Vertical:
                // Only vertical scrolling - counteract horizontal
                transform.X = HorizontalOffset;
                transform.Y = 0;
                break;

            case SelectiveScrollingOrientation.Both:
                // Normal scrolling
                transform.X = 0;
                transform.Y = 0;
                break;
        }

        if (child.RenderTransform != transform)
        {
            child.RenderTransform = transform;
        }
    }

    #endregion

    #region Scroll Updates

    /// <summary>
    /// Updates the scroll position.
    /// </summary>
    /// <param name="horizontalOffset">The new horizontal offset.</param>
    /// <param name="verticalOffset">The new vertical offset.</param>
    public void UpdateScrollPosition(double horizontalOffset, double verticalOffset)
    {
        if (Math.Abs(HorizontalOffset - horizontalOffset) > double.Epsilon ||
            Math.Abs(VerticalOffset - verticalOffset) > double.Epsilon)
        {
            HorizontalOffset = horizontalOffset;
            VerticalOffset = verticalOffset;
            InvalidateArrange();
        }
    }

    #endregion
}
