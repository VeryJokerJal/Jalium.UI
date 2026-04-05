namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A date-time axis that formats tick labels as dates.
/// </summary>
public class DateTimeAxis : ChartAxis
{
    /// <summary>
    /// The epoch used for converting DateTime to/from numeric axis values.
    /// </summary>
    public static readonly DateTime Epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    #region Dependency Properties

    /// <summary>
    /// Identifies the IntervalType dependency property.
    /// </summary>
    public static readonly DependencyProperty IntervalTypeProperty =
        DependencyProperty.Register(nameof(IntervalType), typeof(DateTimeIntervalType), typeof(DateTimeAxis),
            new PropertyMetadata(DateTimeIntervalType.Day));

    /// <summary>
    /// Identifies the DateFormat dependency property.
    /// </summary>
    public static readonly DependencyProperty DateFormatProperty =
        DependencyProperty.Register(nameof(DateFormat), typeof(string), typeof(DateTimeAxis),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the interval type for tick generation.
    /// </summary>
    public DateTimeIntervalType IntervalType
    {
        get => (DateTimeIntervalType)GetValue(IntervalTypeProperty)!;
        set => SetValue(IntervalTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the date format string for labels.
    /// </summary>
    public string? DateFormat
    {
        get => (string?)GetValue(DateFormatProperty);
        set => SetValue(DateFormatProperty, value);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Converts a DateTime to a numeric axis value (days since epoch).
    /// </summary>
    public static double DateTimeToDouble(DateTime dt)
    {
        return (dt - Epoch).TotalDays;
    }

    /// <summary>
    /// Converts a numeric axis value back to a DateTime.
    /// </summary>
    public static DateTime DoubleToDateTime(double value)
    {
        return Epoch.AddDays(value);
    }

    /// <inheritdoc />
    public override double[] GenerateTicks(double min, double max, double availablePixels)
    {
        if (max <= min)
            return Array.Empty<double>();

        var startDate = DoubleToDateTime(min);
        var endDate = DoubleToDateTime(max);
        var ticks = new List<double>();

        // Align the first tick to the interval boundary
        var current = AlignToInterval(startDate);
        if (current < startDate)
            current = AdvanceByInterval(current);

        int maxTicks = 500;
        while (current <= endDate && ticks.Count < maxTicks)
        {
            ticks.Add(DateTimeToDouble(current));
            current = AdvanceByInterval(current);
        }

        return ticks.ToArray();
    }

    /// <inheritdoc />
    public override string FormatLabel(double value)
    {
        var dt = DoubleToDateTime(value);
        if (DateFormat != null)
            return dt.ToString(DateFormat);

        return IntervalType switch
        {
            DateTimeIntervalType.Year => dt.ToString("yyyy"),
            DateTimeIntervalType.Month => dt.ToString("yyyy-MM"),
            DateTimeIntervalType.Week => dt.ToString("MMM dd"),
            DateTimeIntervalType.Day => dt.ToString("MMM dd"),
            _ => dt.ToString("yyyy-MM-dd")
        };
    }

    private DateTime AlignToInterval(DateTime dt)
    {
        return IntervalType switch
        {
            DateTimeIntervalType.Year => new DateTime(dt.Year, 1, 1, 0, 0, 0, dt.Kind),
            DateTimeIntervalType.Month => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, dt.Kind),
            DateTimeIntervalType.Week => dt.AddDays(-(int)dt.DayOfWeek).Date,
            DateTimeIntervalType.Day => dt.Date,
            _ => dt.Date
        };
    }

    private DateTime AdvanceByInterval(DateTime dt)
    {
        return IntervalType switch
        {
            DateTimeIntervalType.Year => dt.AddYears(1),
            DateTimeIntervalType.Month => dt.AddMonths(1),
            DateTimeIntervalType.Week => dt.AddDays(7),
            DateTimeIntervalType.Day => dt.AddDays(1),
            _ => dt.AddDays(1)
        };
    }

    #endregion
}
