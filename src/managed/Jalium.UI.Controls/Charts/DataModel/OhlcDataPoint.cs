using System.ComponentModel;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents an Open-High-Low-Close data point for financial charts.
/// </summary>
public class OhlcDataPoint : INotifyPropertyChanged
{
    private DateTime _date;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private double _volume;

    /// <summary>
    /// Gets or sets the date of this data point.
    /// </summary>
    public DateTime Date
    {
        get => _date;
        set
        {
            if (_date != value)
            {
                _date = value;
                OnPropertyChanged(nameof(Date));
            }
        }
    }

    /// <summary>
    /// Gets or sets the opening price.
    /// </summary>
    public double Open
    {
        get => _open;
        set
        {
            if (_open != value)
            {
                _open = value;
                OnPropertyChanged(nameof(Open));
            }
        }
    }

    /// <summary>
    /// Gets or sets the highest price.
    /// </summary>
    public double High
    {
        get => _high;
        set
        {
            if (_high != value)
            {
                _high = value;
                OnPropertyChanged(nameof(High));
            }
        }
    }

    /// <summary>
    /// Gets or sets the lowest price.
    /// </summary>
    public double Low
    {
        get => _low;
        set
        {
            if (_low != value)
            {
                _low = value;
                OnPropertyChanged(nameof(Low));
            }
        }
    }

    /// <summary>
    /// Gets or sets the closing price.
    /// </summary>
    public double Close
    {
        get => _close;
        set
        {
            if (_close != value)
            {
                _close = value;
                OnPropertyChanged(nameof(Close));
            }
        }
    }

    /// <summary>
    /// Gets or sets the trading volume.
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (_volume != value)
            {
                _volume = value;
                OnPropertyChanged(nameof(Volume));
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
