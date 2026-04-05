namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A logarithmic axis that maps data values using a logarithmic scale.
/// </summary>
public class LogarithmicAxis : ChartAxis
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the LogBase dependency property.
    /// </summary>
    public static readonly DependencyProperty LogBaseProperty =
        DependencyProperty.Register(nameof(LogBase), typeof(double), typeof(LogarithmicAxis),
            new PropertyMetadata(10.0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the base of the logarithm.
    /// </summary>
    public double LogBase
    {
        get => (double)GetValue(LogBaseProperty)!;
        set => SetValue(LogBaseProperty, value);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override double[] GenerateTicks(double min, double max, double availablePixels)
    {
        if (max <= min || min <= 0)
            return Array.Empty<double>();

        var logBase = LogBase;
        if (logBase <= 1)
            logBase = 10;

        var logMin = Math.Log(min, logBase);
        var logMax = Math.Log(max, logBase);

        var ticks = new List<double>();
        var startPow = Math.Floor(logMin);
        var endPow = Math.Ceiling(logMax);

        for (var p = startPow; p <= endPow; p++)
        {
            var tickValue = Math.Pow(logBase, p);
            if (tickValue >= min && tickValue <= max)
            {
                ticks.Add(tickValue);
            }

            // Safety
            if (ticks.Count > 500)
                break;
        }

        return ticks.ToArray();
    }

    /// <inheritdoc />
    public override string FormatLabel(double value)
    {
        if (LabelFormat != null)
            return value.ToString(LabelFormat);

        // For log axes, show concise notation
        if (value >= 1e6)
            return value.ToString("0.##E+0");
        if (value >= 1)
            return value.ToString("G6");
        return value.ToString("G4");
    }

    /// <summary>
    /// Converts a data value to a pixel position using logarithmic scaling.
    /// </summary>
    public override double ValueToPixel(double value, double min, double max, double pixelRange)
    {
        if (value <= 0 || min <= 0 || max <= min)
            return 0;

        var logBase = LogBase > 1 ? LogBase : 10;
        var logValue = Math.Log(value, logBase);
        var logMin = Math.Log(min, logBase);
        var logMax = Math.Log(max, logBase);
        var logRange = logMax - logMin;

        if (Math.Abs(logRange) < 1e-15)
            return pixelRange / 2.0;

        return (logValue - logMin) / logRange * pixelRange;
    }

    /// <summary>
    /// Converts a pixel position to a data value using logarithmic scaling.
    /// </summary>
    public override double PixelToValue(double pixel, double min, double max, double pixelRange)
    {
        if (min <= 0 || max <= min || Math.Abs(pixelRange) < 1e-15)
            return min;

        var logBase = LogBase > 1 ? LogBase : 10;
        var logMin = Math.Log(min, logBase);
        var logMax = Math.Log(max, logBase);
        var logValue = logMin + pixel / pixelRange * (logMax - logMin);

        return Math.Pow(logBase, logValue);
    }

    #endregion
}
