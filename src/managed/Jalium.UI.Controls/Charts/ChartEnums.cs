namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Specifies the position of the chart legend.
/// </summary>
public enum LegendPosition
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>
/// Specifies how bars are arranged in a bar chart.
/// </summary>
public enum BarMode
{
    Grouped,
    Stacked,
    StackedPercentage
}

/// <summary>
/// Specifies the type of sparkline chart.
/// </summary>
public enum SparklineType
{
    Line,
    Bar,
    WinLoss,
    Area
}

/// <summary>
/// Specifies the type of trend line.
/// </summary>
public enum TrendLineType
{
    Linear,
    Polynomial,
    Exponential,
    MovingAverage
}

/// <summary>
/// Specifies the algorithm used for treemap layout.
/// </summary>
public enum TreeMapAlgorithm
{
    Squarified,
    SliceAndDice,
    Binary
}

/// <summary>
/// Specifies the layout algorithm for network graphs.
/// </summary>
public enum NetworkLayoutAlgorithm
{
    ForceDirected,
    Circular,
    Hierarchical
}

/// <summary>
/// Specifies the color scale for heatmaps.
/// </summary>
public enum HeatmapColorScale
{
    BlueToRed,
    Viridis,
    Grayscale,
    Custom
}

/// <summary>
/// Specifies the label position for pie chart slices.
/// </summary>
public enum PieLabelPosition
{
    Inside,
    Outside,
    Connector
}

/// <summary>
/// Specifies the shape used to render data points.
/// </summary>
public enum PointShape
{
    Circle,
    Square,
    Triangle,
    Diamond
}

/// <summary>
/// Specifies the label position for Sankey diagram nodes.
/// </summary>
public enum SankeyLabelPosition
{
    Left,
    Right,
    Inside,
    Both
}

/// <summary>
/// Specifies the interval type for a date-time axis.
/// </summary>
public enum DateTimeIntervalType
{
    Day,
    Week,
    Month,
    Year
}
