namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A numeric axis with automatic tick spacing using the NiceNumber algorithm.
/// </summary>
public class NumericAxis : ChartAxis
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the TickInterval dependency property. Null means auto-compute.
    /// </summary>
    public static readonly DependencyProperty TickIntervalProperty =
        DependencyProperty.Register(nameof(TickInterval), typeof(double?), typeof(NumericAxis),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the explicit tick interval. Null means auto-compute.
    /// </summary>
    public double? TickInterval
    {
        get => (double?)GetValue(TickIntervalProperty);
        set => SetValue(TickIntervalProperty, value);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override double[] GenerateTicks(double min, double max, double availablePixels)
    {
        if (max <= min || double.IsNaN(min) || double.IsNaN(max))
            return Array.Empty<double>();

        var range = max - min;
        double interval;

        if (TickInterval.HasValue && TickInterval.Value > 0)
        {
            interval = TickInterval.Value;
        }
        else
        {
            // Use the NiceNumber algorithm to determine spacing
            var desiredTicks = Math.Max(2, TickCount);
            var rawInterval = range / desiredTicks;
            interval = ChartHelpers.NiceNumber(rawInterval, true);
        }

        if (interval <= 0 || double.IsInfinity(interval))
            return new[] { min, max };

        var ticks = new List<double>();
        var firstTick = Math.Ceiling(min / interval) * interval;

        // Snap the first tick if it's extremely close to min
        if (Math.Abs(firstTick - min) < interval * 1e-10)
            firstTick = min;

        for (var tick = firstTick; tick <= max + interval * 1e-10; tick += interval)
        {
            // Round to avoid floating point drift
            var rounded = Math.Round(tick, 10);
            ticks.Add(rounded);

            // Safety: prevent infinite loop
            if (ticks.Count > 1000)
                break;
        }

        return ticks.ToArray();
    }

    #endregion
}
