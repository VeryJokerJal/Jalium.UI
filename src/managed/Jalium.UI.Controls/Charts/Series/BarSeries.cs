using System.Collections.ObjectModel;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a bar series in a chart.
/// </summary>
public class BarSeries : ChartSeries
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the DataPoints dependency property.
    /// </summary>
    public static readonly DependencyProperty DataPointsProperty =
        DependencyProperty.Register(nameof(DataPoints), typeof(ObservableCollection<ChartDataPoint>), typeof(BarSeries),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

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
