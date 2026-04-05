using System.Collections.ObjectModel;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a scatter series in a chart.
/// </summary>
public class ScatterSeries : ChartSeries
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the PointShape dependency property.
    /// </summary>
    public static readonly DependencyProperty PointShapeProperty =
        DependencyProperty.Register(nameof(PointShape), typeof(PointShape), typeof(ScatterSeries),
            new PropertyMetadata(PointShape.Circle));

    /// <summary>
    /// Identifies the SizeBindingPath dependency property.
    /// </summary>
    public static readonly DependencyProperty SizeBindingPathProperty =
        DependencyProperty.Register(nameof(SizeBindingPath), typeof(string), typeof(ScatterSeries),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the DataPoints dependency property.
    /// </summary>
    public static readonly DependencyProperty DataPointsProperty =
        DependencyProperty.Register(nameof(DataPoints), typeof(ObservableCollection<ChartDataPoint>), typeof(ScatterSeries),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the shape used to render data points.
    /// </summary>
    public PointShape PointShape
    {
        get => (PointShape)GetValue(PointShapeProperty)!;
        set => SetValue(PointShapeProperty, value);
    }

    /// <summary>
    /// Gets or sets the property path for bubble/scatter point sizes.
    /// </summary>
    public string? SizeBindingPath
    {
        get => (string?)GetValue(SizeBindingPathProperty);
        set => SetValue(SizeBindingPathProperty, value);
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
