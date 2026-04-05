using System.Collections.ObjectModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a line series in a chart.
/// </summary>
public class LineSeries : ChartSeries
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the DashArray dependency property.
    /// </summary>
    public static readonly DependencyProperty DashArrayProperty =
        DependencyProperty.Register(nameof(DashArray), typeof(DoubleCollection), typeof(LineSeries),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the PointShape dependency property.
    /// </summary>
    public static readonly DependencyProperty PointShapeProperty =
        DependencyProperty.Register(nameof(PointShape), typeof(PointShape), typeof(LineSeries),
            new PropertyMetadata(PointShape.Circle));

    /// <summary>
    /// Identifies the DataPoints dependency property.
    /// </summary>
    public static readonly DependencyProperty DataPointsProperty =
        DependencyProperty.Register(nameof(DataPoints), typeof(ObservableCollection<ChartDataPoint>), typeof(LineSeries),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the dash pattern for the line.
    /// </summary>
    public DoubleCollection? DashArray
    {
        get => (DoubleCollection?)GetValue(DashArrayProperty);
        set => SetValue(DashArrayProperty, value);
    }

    /// <summary>
    /// Gets or sets the shape used to render data points.
    /// </summary>
    public PointShape PointShape
    {
        get => (PointShape)GetValue(PointShapeProperty)!;
        set => SetValue(PointShapeProperty, value);
    }

    /// <summary>
    /// Gets or sets the data points for this series.
    /// </summary>
    public ObservableCollection<ChartDataPoint> DataPoints
    {
        get
        {
            var dp = (ObservableCollection<ChartDataPoint>?)GetValue(DataPointsProperty);
            if (dp == null)
            {
                dp = new ObservableCollection<ChartDataPoint>();
                SetValue(DataPointsProperty, dp);
            }
            return dp;
        }
        set => SetValue(DataPointsProperty, value);
    }

    #endregion
}
