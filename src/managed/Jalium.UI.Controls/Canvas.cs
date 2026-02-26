namespace Jalium.UI.Controls;

/// <summary>
/// Defines an area within which you can explicitly position child elements
/// by using coordinates that are relative to the Canvas area.
/// </summary>
public class Canvas : Panel
{
    #region Attached Properties

    /// <summary>
    /// Identifies the Left attached property.
    /// </summary>
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.RegisterAttached("Left", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the Top attached property.
    /// </summary>
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.RegisterAttached("Top", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the Right attached property.
    /// </summary>
    public static readonly DependencyProperty RightProperty =
        DependencyProperty.RegisterAttached("Right", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the Bottom attached property.
    /// </summary>
    public static readonly DependencyProperty BottomProperty =
        DependencyProperty.RegisterAttached("Bottom", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Gets the value of the Left attached property.
    /// </summary>
    public static double GetLeft(UIElement element) =>
        (double)(element.GetValue(LeftProperty) ?? double.NaN);

    /// <summary>
    /// Sets the value of the Left attached property.
    /// </summary>
    public static void SetLeft(UIElement element, double value) =>
        element.SetValue(LeftProperty, value);

    /// <summary>
    /// Gets the value of the Top attached property.
    /// </summary>
    public static double GetTop(UIElement element) =>
        (double)(element.GetValue(TopProperty) ?? double.NaN);

    /// <summary>
    /// Sets the value of the Top attached property.
    /// </summary>
    public static void SetTop(UIElement element, double value) =>
        element.SetValue(TopProperty, value);

    /// <summary>
    /// Gets the value of the Right attached property.
    /// </summary>
    public static double GetRight(UIElement element) =>
        (double)(element.GetValue(RightProperty) ?? double.NaN);

    /// <summary>
    /// Sets the value of the Right attached property.
    /// </summary>
    public static void SetRight(UIElement element, double value) =>
        element.SetValue(RightProperty, value);

    /// <summary>
    /// Gets the value of the Bottom attached property.
    /// </summary>
    public static double GetBottom(UIElement element) =>
        (double)(element.GetValue(BottomProperty) ?? double.NaN);

    /// <summary>
    /// Sets the value of the Bottom attached property.
    /// </summary>
    public static void SetBottom(UIElement element, double value) =>
        element.SetValue(BottomProperty, value);

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && element.VisualParent is Canvas canvas)
        {
            canvas.InvalidateArrange();
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Canvas measures children with infinite space but returns Size(0,0)
        // like WPF — it relies on parent layout to determine its actual size.
        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        return new Size();
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            var left = GetLeft(child);
            var top = GetTop(child);
            var right = GetRight(child);
            var bottom = GetBottom(child);

            double x = 0;
            double y = 0;
            double width = fe.DesiredSize.Width;
            double height = fe.DesiredSize.Height;

            // Determine X position
            if (!double.IsNaN(left))
            {
                x = left;
            }
            else if (!double.IsNaN(right))
            {
                x = finalSize.Width - right - width;
            }

            // Determine Y position
            if (!double.IsNaN(top))
            {
                y = top;
            }
            else if (!double.IsNaN(bottom))
            {
                y = finalSize.Height - bottom - height;
            }

            var arrangeRect = new Rect(x, y, width, height);
            fe.Arrange(arrangeRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #endregion
}
