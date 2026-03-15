using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an icon that uses a vector path as its content.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.PathIcon.
/// </summary>
public class PathIcon : IconElement
{
    /// <summary>
    /// Identifies the Data dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(PathIcon),
            new PropertyMetadata(null, OnDataChanged));

    /// <summary>
    /// Gets or sets the Geometry that specifies the shape to be drawn.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Data != null)
        {
            var bounds = Data.Bounds;
            return new Size(
                Math.Min(bounds.Width, availableSize.Width),
                Math.Min(bounds.Height, availableSize.Height));
        }
        return new Size(
            Math.Min(20, availableSize.Width),
            Math.Min(20, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        if (Data == null) return;

        var foreground = GetEffectiveForeground();
        dc.DrawGeometry(foreground, null, Data);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PathIcon icon)
        {
            icon.InvalidateMeasure();
            icon.InvalidateVisual();
        }
    }
}
