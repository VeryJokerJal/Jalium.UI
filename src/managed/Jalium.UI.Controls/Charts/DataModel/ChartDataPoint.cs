using System.ComponentModel;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a single data point in a chart series.
/// </summary>
public class ChartDataPoint : INotifyPropertyChanged
{
    private object? _xValue;
    private double _yValue;
    private string? _label;
    private object? _tag;

    /// <summary>
    /// Gets or sets the X-axis value.
    /// </summary>
    public object? XValue
    {
        get => _xValue;
        set
        {
            if (!Equals(_xValue, value))
            {
                _xValue = value;
                OnPropertyChanged(nameof(XValue));
            }
        }
    }

    /// <summary>
    /// Gets or sets the Y-axis value.
    /// </summary>
    public double YValue
    {
        get => _yValue;
        set
        {
            if (_yValue != value)
            {
                _yValue = value;
                OnPropertyChanged(nameof(YValue));
            }
        }
    }

    /// <summary>
    /// Gets or sets an optional label for this data point.
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
    /// Gets or sets optional application-specific data associated with this data point.
    /// </summary>
    public object? Tag
    {
        get => _tag;
        set
        {
            if (!Equals(_tag, value))
            {
                _tag = value;
                OnPropertyChanged(nameof(Tag));
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
