using System.Collections.ObjectModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a single data point in a geographic heatmap with latitude, longitude, and weight.
/// </summary>
public readonly struct HeatPoint : IEquatable<HeatPoint>
{
    /// <summary>
    /// Gets the latitude in degrees.
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Gets the longitude in degrees.
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Gets the weight (intensity) of this heat point.
    /// </summary>
    public double Weight { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeatPoint"/> struct.
    /// </summary>
    public HeatPoint(double latitude, double longitude, double weight = 1.0)
    {
        Latitude = latitude;
        Longitude = longitude;
        Weight = weight;
    }

    /// <inheritdoc />
    public bool Equals(HeatPoint other)
        => Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude) && Weight.Equals(other.Weight);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HeatPoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Latitude, Longitude, Weight);

    /// <inheritdoc />
    public override string ToString() => $"({Latitude:F6}, {Longitude:F6}, W={Weight:F2})";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(HeatPoint left, HeatPoint right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(HeatPoint left, HeatPoint right) => !left.Equals(right);
}

/// <summary>
/// A typed collection of <see cref="HeatPoint"/> objects.
/// </summary>
public class HeatPointCollection : ObservableCollection<HeatPoint>
{
}

/// <summary>
/// Defines the color gradient used to colorize the heatmap.
/// </summary>
public class HeatmapGradient
{
    /// <summary>
    /// Gets or sets the gradient stops that define the color ramp.
    /// </summary>
    public GradientStopCollection Stops { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeatmapGradient"/> class.
    /// </summary>
    public HeatmapGradient()
    {
        Stops = new GradientStopCollection();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeatmapGradient"/> class with the specified stops.
    /// </summary>
    public HeatmapGradient(GradientStopCollection stops)
    {
        Stops = stops;
    }

    /// <summary>
    /// Gets the default heatmap gradient (blue -> cyan -> green -> yellow -> red).
    /// </summary>
    public static HeatmapGradient Default { get; } = CreateDefaultGradient();

    /// <summary>
    /// Samples a color from the gradient at the specified normalized position (0.0 - 1.0).
    /// </summary>
    /// <param name="t">The normalized position (0.0 - 1.0).</param>
    /// <returns>The interpolated color.</returns>
    public Color SampleColor(double t)
    {
        t = Math.Clamp(t, 0, 1);

        if (Stops.Count == 0) return Color.Transparent;
        if (Stops.Count == 1) return Stops[0].Color;

        // Find the two stops that bracket the position
        GradientStop? lower = null;
        GradientStop? upper = null;

        for (int i = 0; i < Stops.Count; i++)
        {
            var stop = Stops[i];
            if (stop.Offset <= t)
            {
                if (lower == null || stop.Offset > lower.Offset)
                    lower = stop;
            }
            if (stop.Offset >= t)
            {
                if (upper == null || stop.Offset < upper.Offset)
                    upper = stop;
            }
        }

        if (lower == null && upper != null) return upper.Color;
        if (upper == null && lower != null) return lower.Color;
        if (lower == null || upper == null) return Color.Transparent;

        if (Math.Abs(lower.Offset - upper.Offset) < 0.0001)
            return lower.Color;

        // Linearly interpolate between the two stops
        double localT = (t - lower.Offset) / (upper.Offset - lower.Offset);
        return LerpColor(lower.Color, upper.Color, localT);
    }

    /// <summary>
    /// Builds a 256-entry lookup table for fast gradient evaluation.
    /// </summary>
    /// <returns>An array of 256 colors sampled from the gradient.</returns>
    public Color[] BuildLookupTable()
    {
        var lut = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            lut[i] = SampleColor(i / 255.0);
        }
        return lut;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        return Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static HeatmapGradient CreateDefaultGradient()
    {
        var stops = new GradientStopCollection();
        stops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 255), Offset = 0.0 });
        stops.Add(new GradientStop { Color = Color.FromRgb(0, 0, 255), Offset = 0.15 });
        stops.Add(new GradientStop { Color = Color.FromRgb(0, 200, 255), Offset = 0.3 });
        stops.Add(new GradientStop { Color = Color.FromRgb(0, 220, 0), Offset = 0.5 });
        stops.Add(new GradientStop { Color = Color.FromRgb(255, 255, 0), Offset = 0.7 });
        stops.Add(new GradientStop { Color = Color.FromRgb(255, 128, 0), Offset = 0.85 });
        stops.Add(new GradientStop { Color = Color.FromRgb(255, 0, 0), Offset = 1.0 });
        return new HeatmapGradient(stops);
    }
}

/// <summary>
/// Specifies the position of the heatmap legend.
/// </summary>
public enum HeatmapLegendPosition
{
    /// <summary>Top-left corner.</summary>
    TopLeft,
    /// <summary>Top-right corner.</summary>
    TopRight,
    /// <summary>Bottom-left corner.</summary>
    BottomLeft,
    /// <summary>Bottom-right corner.</summary>
    BottomRight
}
