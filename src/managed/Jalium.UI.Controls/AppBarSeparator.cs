using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a vertical line that separates items in a CommandBar.
/// </summary>
public class AppBarSeparator : Control, ICommandBarElement
{
    private static readonly SolidColorBrush s_fallbackSeparatorBrush = new(ThemeColors.ControlBorder);

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(AppBarSeparator),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DynamicOverflowOrder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DynamicOverflowOrderProperty =
        DependencyProperty.Register(nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarSeparator),
            new PropertyMetadata(0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the element is shown in its compact representation.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty)!;
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// Gets or sets the priority of this element's dynamic overflow behavior.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int DynamicOverflowOrder
    {
        get => (int)GetValue(DynamicOverflowOrderProperty)!;
        set => SetValue(DynamicOverflowOrderProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the AppBarSeparator class.
    /// </summary>
    public AppBarSeparator()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(1, IsCompact ? 24 : 32);
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        var brush = ResolveSeparatorBrush();
        var pen = new Jalium.UI.Media.Pen(brush, 1);

        var height = RenderSize.Height;
        var x = RenderSize.Width / 2;
        var margin = 4.0;

        dc.DrawLine(pen, new Point(x, margin), new Point(x, height - margin));
    }

    private Brush ResolveSeparatorBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("AppBarSeparatorForeground") as Brush
            ?? Foreground
            ?? s_fallbackSeparatorBrush;
    }
}
