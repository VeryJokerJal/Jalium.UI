using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a data point for a pie chart slice.
/// </summary>
public class PieDataPoint : INotifyPropertyChanged
{
    private double _value;
    private string? _label;
    private bool _isExploded;
    private Brush? _brush;

    /// <summary>
    /// Gets or sets the numeric value of this slice.
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }
    }

    /// <summary>
    /// Gets or sets the display label for this slice.
    /// </summary>
    public string? Label
    {
        get => _label;
        set
        {
            if (_label != value)
            {
                _label = value;
                OnPropertyChanged(nameof(Label));
            }
        }
    }

    /// <summary>
    /// Gets or sets whether this slice is pulled out from the center.
    /// </summary>
    public bool IsExploded
    {
        get => _isExploded;
        set
        {
            if (_isExploded != value)
            {
                _isExploded = value;
                OnPropertyChanged(nameof(IsExploded));
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush used to fill this slice.
    /// </summary>
    public Brush? Brush
    {
        get => _brush;
        set
        {
            if (_brush != value)
            {
                _brush = value;
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a pie chart series.
/// </summary>
public class PieSeries : ChartSeries
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the DataPoints dependency property.
    /// </summary>
    public static readonly DependencyProperty DataPointsProperty =
        DependencyProperty.Register(nameof(DataPoints), typeof(ObservableCollection<PieDataPoint>), typeof(PieSeries),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the data points for this pie series.
    /// </summary>
    public ObservableCollection<PieDataPoint> DataPoints
    {
        get
        {
            var dp = (ObservableCollection<PieDataPoint>?)GetValue(DataPointsProperty);
            if (dp == null)
            {
                dp = new ObservableCollection<PieDataPoint>();
                SetValue(DataPointsProperty, dp);
            }
            return dp;
        }
        set => SetValue(DataPointsProperty, value);
    }

    #endregion
}
