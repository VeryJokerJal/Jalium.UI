using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a single item in a chart legend.
/// </summary>
public class ChartLegendItem
{
    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the color brush for the legend marker.
    /// </summary>
    public Brush Brush { get; set; } = new SolidColorBrush(Color.Gray);

    /// <summary>
    /// Gets or sets whether the associated series is visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;
}
