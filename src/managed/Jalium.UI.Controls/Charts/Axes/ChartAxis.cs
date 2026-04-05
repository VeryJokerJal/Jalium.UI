using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Abstract base class for chart axes.
/// </summary>
public abstract class ChartAxis : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property. Null means auto-scale.
    /// </summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double?), typeof(ChartAxis),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Maximum dependency property. Null means auto-scale.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double?), typeof(ChartAxis),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ChartAxis),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the LabelFormat dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelFormatProperty =
        DependencyProperty.Register(nameof(LabelFormat), typeof(string), typeof(ChartAxis),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TickCount dependency property.
    /// </summary>
    public static readonly DependencyProperty TickCountProperty =
        DependencyProperty.Register(nameof(TickCount), typeof(int), typeof(ChartAxis),
            new PropertyMetadata(5));

    /// <summary>
    /// Identifies the IsVisible dependency property.
    /// </summary>
    public static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.Register(nameof(IsVisible), typeof(bool), typeof(ChartAxis),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the LabelFontSize dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelFontSizeProperty =
        DependencyProperty.Register(nameof(LabelFontSize), typeof(double), typeof(ChartAxis),
            new PropertyMetadata(12.0));

    /// <summary>
    /// Identifies the LabelForeground dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelForegroundProperty =
        DependencyProperty.Register(nameof(LabelForeground), typeof(Brush), typeof(ChartAxis),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the MajorTickLength dependency property.
    /// </summary>
    public static readonly DependencyProperty MajorTickLengthProperty =
        DependencyProperty.Register(nameof(MajorTickLength), typeof(double), typeof(ChartAxis),
            new PropertyMetadata(6.0));

    /// <summary>
    /// Identifies the MinorTickLength dependency property.
    /// </summary>
    public static readonly DependencyProperty MinorTickLengthProperty =
        DependencyProperty.Register(nameof(MinorTickLength), typeof(double), typeof(ChartAxis),
            new PropertyMetadata(3.0));

    /// <summary>
    /// Identifies the ShowMinorTicks dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowMinorTicksProperty =
        DependencyProperty.Register(nameof(ShowMinorTicks), typeof(bool), typeof(ChartAxis),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the minimum value. Null means auto-scale.
    /// </summary>
    public double? Minimum
    {
        get => (double?)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value. Null means auto-scale.
    /// </summary>
    public double? Maximum
    {
        get => (double?)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the axis title.
    /// </summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the format string for tick labels.
    /// </summary>
    public string? LabelFormat
    {
        get => (string?)GetValue(LabelFormatProperty);
        set => SetValue(LabelFormatProperty, value);
    }

    /// <summary>
    /// Gets or sets the desired number of ticks.
    /// </summary>
    public int TickCount
    {
        get => (int)GetValue(TickCountProperty)!;
        set => SetValue(TickCountProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this axis is visible.
    /// </summary>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty)!;
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size for axis labels.
    /// </summary>
    public double LabelFontSize
    {
        get => (double)GetValue(LabelFontSizeProperty)!;
        set => SetValue(LabelFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush for axis labels.
    /// </summary>
    public Brush? LabelForeground
    {
        get => (Brush?)GetValue(LabelForegroundProperty);
        set => SetValue(LabelForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the length of major tick marks.
    /// </summary>
    public double MajorTickLength
    {
        get => (double)GetValue(MajorTickLengthProperty)!;
        set => SetValue(MajorTickLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the length of minor tick marks.
    /// </summary>
    public double MinorTickLength
    {
        get => (double)GetValue(MinorTickLengthProperty)!;
        set => SetValue(MinorTickLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets whether minor ticks are shown.
    /// </summary>
    public bool ShowMinorTicks
    {
        get => (bool)GetValue(ShowMinorTicksProperty)!;
        set => SetValue(ShowMinorTicksProperty, value);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Generates tick values for the given data range and available pixels.
    /// </summary>
    /// <param name="min">The minimum data value.</param>
    /// <param name="max">The maximum data value.</param>
    /// <param name="availablePixels">The available pixel space for this axis.</param>
    /// <returns>An array of tick values.</returns>
    public abstract double[] GenerateTicks(double min, double max, double availablePixels);

    /// <summary>
    /// Formats a tick value as a label string.
    /// </summary>
    /// <param name="value">The tick value.</param>
    /// <returns>The formatted label string.</returns>
    public virtual string FormatLabel(double value)
    {
        if (LabelFormat != null)
            return value.ToString(LabelFormat);

        // Auto-format: use appropriate precision
        var range = Math.Abs(value);
        if (range == 0)
            return "0";
        if (Math.Abs(value) >= 1e6)
            return value.ToString("0.##E+0");
        if (Math.Abs(value) >= 1000)
            return value.ToString("N0");
        if (Math.Abs(value) >= 1)
            return value.ToString("G6");
        return value.ToString("G4");
    }

    /// <summary>
    /// Converts a data value to a pixel position within the given pixel range.
    /// </summary>
    /// <param name="value">The data value.</param>
    /// <param name="min">The minimum data value.</param>
    /// <param name="max">The maximum data value.</param>
    /// <param name="pixelRange">The available pixel range.</param>
    /// <returns>The pixel position.</returns>
    public virtual double ValueToPixel(double value, double min, double max, double pixelRange)
    {
        var dataRange = max - min;
        if (Math.Abs(dataRange) < 1e-15)
            return pixelRange / 2.0;

        return (value - min) / dataRange * pixelRange;
    }

    /// <summary>
    /// Converts a pixel position to a data value within the given range.
    /// </summary>
    /// <param name="pixel">The pixel position.</param>
    /// <param name="min">The minimum data value.</param>
    /// <param name="max">The maximum data value.</param>
    /// <param name="pixelRange">The available pixel range.</param>
    /// <returns>The data value.</returns>
    public virtual double PixelToValue(double pixel, double min, double max, double pixelRange)
    {
        if (Math.Abs(pixelRange) < 1e-15)
            return (min + max) / 2.0;

        return min + pixel / pixelRange * (max - min);
    }

    #endregion
}
