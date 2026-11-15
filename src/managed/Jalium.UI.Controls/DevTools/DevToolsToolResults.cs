using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A finite measurement in the target window's device-independent coordinates.</summary>
public sealed class DevToolsRulerMeasurement
{
    static DevToolsRulerMeasurement()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(Start), value => ((DevToolsRulerMeasurement)value).Start);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(End), value => ((DevToolsRulerMeasurement)value).End);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(DeltaX), value => ((DevToolsRulerMeasurement)value).DeltaX);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(DeltaY), value => ((DevToolsRulerMeasurement)value).DeltaY);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(Distance), value => ((DevToolsRulerMeasurement)value).Distance);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(AngleDegrees), value => ((DevToolsRulerMeasurement)value).AngleDegrees);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(DistanceText), value => ((DevToolsRulerMeasurement)value).DistanceText);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(DeltaXText), value => ((DevToolsRulerMeasurement)value).DeltaXText);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(DeltaYText), value => ((DevToolsRulerMeasurement)value).DeltaYText);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(AngleText), value => ((DevToolsRulerMeasurement)value).AngleText);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(CoordinatesText), value => ((DevToolsRulerMeasurement)value).CoordinatesText);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(IsSnapped), value => ((DevToolsRulerMeasurement)value).IsSnapped);
        PropertyAccessorRegistry.Register(typeof(DevToolsRulerMeasurement), nameof(AutomationName), value => ((DevToolsRulerMeasurement)value).AutomationName);
    }

    public Point Start { get; }
    public Point End { get; }
    public double DeltaX { get; }
    public double DeltaY { get; }
    public double Distance { get; }
    public double AngleDegrees { get; }
    public bool IsSnapped { get; }
    public string DistanceText { get; }
    public string DeltaXText { get; }
    public string DeltaYText { get; }
    public string AngleText { get; }
    public string CoordinatesText { get; }
    public string AutomationName => $"Distance {DistanceText} device-independent pixels; delta X {DeltaXText}; delta Y {DeltaYText}; angle {AngleText}; {CoordinatesText}";

    private DevToolsRulerMeasurement(Point start, Point end, double dx, double dy, double distance, bool snapped)
    {
        Start = start; End = end; DeltaX = dx; DeltaY = dy; Distance = distance; IsSnapped = snapped;
        AngleDegrees = distance == 0 ? 0 : Math.Atan2(dy, dx) * 180 / Math.PI;
        DistanceText = FormatLength(distance, "F1");
        DeltaXText = FormatLength(dx, "F0");
        DeltaYText = FormatLength(dy, "F0");
        AngleText = AngleDegrees.ToString("F1", CultureInfo.InvariantCulture) + "°";
        CoordinatesText = $"({FormatLength(start.X, "F0")}, {FormatLength(start.Y, "F0")}) → ({FormatLength(end.X, "F0")}, {FormatLength(end.Y, "F0")})";
    }

    public static bool TryCreate(Point start, Point end, bool snapped, [NotNullWhen(true)] out DevToolsRulerMeasurement? measurement)
    {
        measurement = null;
        if (!TryMeasure(start, end, out var dx, out var dy, out var distance)) return false;
        measurement = new(start, end, dx, dy, distance, snapped);
        return true;
    }

    public static bool TrySnap(Point start, Point raw, out Point snapped)
    {
        snapped = start;
        // Pointer previews only need geometry; do not allocate formatted result
        // objects on every move. Exact axis vectors also avoid negative-zero
        // deltas from sin/cos roundoff at 90/180/270 degrees.
        if (!TryMeasure(start, raw, out var dx, out var dy, out var distance)) return false;
        if (distance < .5) return true;
        int direction = ((int)Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) + 8) & 7;
        double diagonal = distance / Math.Sqrt(2);
        (double x, double y) = direction switch
        {
            0 => (distance, 0d), 1 => (diagonal, diagonal),
            2 => (0d, distance), 3 => (-diagonal, diagonal),
            4 => (-distance, 0d), 5 => (-diagonal, -diagonal),
            6 => (0d, -distance), _ => (diagonal, -diagonal),
        };
        snapped = new Point(start.X + x, start.Y + y);
        return IsFinite(snapped);
    }

    private static bool TryMeasure(Point start, Point end, out double dx, out double dy, out double distance)
    {
        dx = dy = distance = 0;
        if (!IsFinite(start) || !IsFinite(end)) return false;
        dx = end.X - start.X; dy = end.Y - start.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return false;
        double maximum = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (maximum == 0) return true;
        double x = dx / maximum, y = dy / maximum;
        distance = maximum * Math.Sqrt(x * x + y * y);
        return double.IsFinite(distance);
    }

    internal static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    internal static string FormatLength(double value, string format)
    {
        if (Math.Abs(value) >= 1_000_000) return value.ToString("G6", CultureInfo.InvariantCulture);
        if (Math.Round(value, format == "F0" ? 0 : 1) == 0) value = 0;
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}

/// <summary>A sampled color and its location, independent of the result control's template.</summary>
public sealed class DevToolsColorSample
{
    static DevToolsColorSample()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(Color), value => ((DevToolsColorSample)value).Color);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(Position), value => ((DevToolsColorSample)value).Position);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(Hue), value => ((DevToolsColorSample)value).Hue);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(Saturation), value => ((DevToolsColorSample)value).Saturation);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(Lightness), value => ((DevToolsColorSample)value).Lightness);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(ArgbHex), value => ((DevToolsColorSample)value).ArgbHex);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(RgbHex), value => ((DevToolsColorSample)value).RgbHex);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(RgbText), value => ((DevToolsColorSample)value).RgbText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(HslText), value => ((DevToolsColorSample)value).HslText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(CoordinatesText), value => ((DevToolsColorSample)value).CoordinatesText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(RedText), value => ((DevToolsColorSample)value).RedText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(GreenText), value => ((DevToolsColorSample)value).GreenText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(BlueText), value => ((DevToolsColorSample)value).BlueText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(AlphaText), value => ((DevToolsColorSample)value).AlphaText);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(RedFraction), value => ((DevToolsColorSample)value).RedFraction);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(GreenFraction), value => ((DevToolsColorSample)value).GreenFraction);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(BlueFraction), value => ((DevToolsColorSample)value).BlueFraction);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(AlphaFraction), value => ((DevToolsColorSample)value).AlphaFraction);
        PropertyAccessorRegistry.Register(typeof(DevToolsColorSample), nameof(AutomationName), value => ((DevToolsColorSample)value).AutomationName);
    }

    public Color Color { get; }
    public Point Position { get; }
    public double Hue { get; }
    public double Saturation { get; }
    public double Lightness { get; }
    public string ArgbHex { get; }
    public string RgbHex { get; }
    public string RgbText { get; }
    public string HslText { get; }
    public string CoordinatesText { get; }
    public string RedText => Color.R.ToString(CultureInfo.InvariantCulture);
    public string GreenText => Color.G.ToString(CultureInfo.InvariantCulture);
    public string BlueText => Color.B.ToString(CultureInfo.InvariantCulture);
    public string AlphaText => Color.A.ToString(CultureInfo.InvariantCulture);
    public double RedFraction => Color.R / 255d;
    public double GreenFraction => Color.G / 255d;
    public double BlueFraction => Color.B / 255d;
    public double AlphaFraction => Color.A / 255d;
    public string AutomationName => $"ARGB {ArgbHex}; red {RedText}, green {GreenText}, blue {BlueText}, alpha {AlphaText}; {HslText}; {CoordinatesText}";

    private DevToolsColorSample(Color color, Point position)
    {
        Color = color; Position = position;
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        Lightness = (max + min) / 2;
        if (max != min)
        {
            double delta = max - min;
            Saturation = Lightness > .5 ? delta / (2 - max - min) : delta / (max + min);
            Hue = (max == r ? (g - b) / delta + (g < b ? 6 : 0) : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60;
        }
        ArgbHex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        RgbHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        RgbText = $"rgb({RedText}, {GreenText}, {BlueText})";
        HslText = FormattableString.Invariant($"HSL {Hue:F0}° · {Saturation * 100:F0}% · {Lightness * 100:F0}%");
        CoordinatesText = $"({DevToolsRulerMeasurement.FormatLength(position.X, "F0")}, {DevToolsRulerMeasurement.FormatLength(position.Y, "F0")})";
    }

    public static bool TryCreate(Color color, Point position, [NotNullWhen(true)] out DevToolsColorSample? sample)
    {
        sample = DevToolsRulerMeasurement.IsFinite(position) ? new(color, position) : null;
        return sample != null;
    }
}
