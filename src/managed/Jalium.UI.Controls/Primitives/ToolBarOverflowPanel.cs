using Jalium.UI.Styling;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides a panel that displays overflow items from a ToolBar.
/// </summary>
public class ToolBarOverflowPanel : Panel
{
    internal Jalium.UI.Controls.ToolBar? ToolBarOwner { get; set; }

    #region Dependency Properties

    /// <summary>
    /// Identifies the WrapWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WrapWidthProperty =
        DependencyProperty.Register(nameof(WrapWidth), typeof(double), typeof(ToolBarOverflowPanel),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>Identifies the Padding dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(ToolBarOverflowPanel),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

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

    /// <summary>Gets or sets the space inside the panel's border.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override UIElementCollection CreateUIElementCollection(FrameworkElement logicalParent) =>
        new(this, TemplatedParent == null ? logicalParent : null);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        var contentAvailable = CssBoxMetrics.InnerSize(availableSize, insets);
        var wrapWidth = double.IsNaN(WrapWidth) ? contentAvailable.Width : WrapWidth;
        var rowGap = ResolveGap(row: true, contentAvailable.Width);
        var columnGap = ResolveGap(row: false, contentAvailable.Width);

        var currentRowWidth = 0.0;
        var currentRowHeight = 0.0;
        var totalHeight = 0.0;
        var maxWidth = 0.0;
        var rowHasChild = false;

        foreach (UIElement child in Children.EnumerateStruct())
        {
            child.Measure(contentAvailable);
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (rowHasChild && currentRowWidth + columnGap + childWidth > wrapWidth)
            {
                // Start new row
                maxWidth = Math.Max(maxWidth, currentRowWidth);
                totalHeight += currentRowHeight + rowGap;
                currentRowWidth = childWidth;
                currentRowHeight = childHeight;
            }
            else
            {
                currentRowWidth += (rowHasChild ? columnGap : 0) + childWidth;
                currentRowHeight = Math.Max(currentRowHeight, childHeight);
            }
            rowHasChild = true;
        }

        // Add last row
        maxWidth = Math.Max(maxWidth, currentRowWidth);
        if (rowHasChild)
            totalHeight += currentRowHeight;

        return new Size(maxWidth + insets.Left + insets.Right,
            totalHeight + insets.Top + insets.Bottom);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = ControlRenderGeometry.GetContentRect(new Rect(finalSize),
            CssBoxMetrics.ContentInsets(this,
                CssLayout?.ContainingWidthCache ?? finalSize.Width));
        var wrapWidth = double.IsNaN(WrapWidth) ? content.Width : WrapWidth;
        var rowGap = ResolveGap(row: true, content.Width);
        var columnGap = ResolveGap(row: false, content.Width);

        var currentX = 0.0;
        var currentY = 0.0;
        var currentRowHeight = 0.0;
        var rowHasChild = false;

        foreach (UIElement child in Children.EnumerateStruct())
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(default);
                continue;
            }

            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (rowHasChild && currentX + columnGap + childWidth > wrapWidth)
            {
                // Start new row
                currentX = 0;
                currentY += currentRowHeight + rowGap;
                currentRowHeight = 0;
                rowHasChild = false;
            }

            if (rowHasChild)
                currentX += columnGap;
            child.Arrange(new Rect(content.X + currentX, content.Y + currentY,
                childWidth, childHeight));

            currentX += childWidth;
            currentRowHeight = Math.Max(currentRowHeight, childHeight);
            rowHasChild = true;
        }

        return finalSize;
    }

    private double ResolveGap(bool row, double basis)
    {
        var gap = CssGapProperties.Resolve(this, row, basis);
        return double.IsFinite(gap) ? Math.Max(0, gap) : 0;
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
