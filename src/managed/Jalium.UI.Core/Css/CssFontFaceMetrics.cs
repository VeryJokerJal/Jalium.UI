using System.Globalization;

namespace Jalium.UI.Styling;

// Descriptors belong to a selected face, not to the element's inherited font
// properties. Ratios use -1 for normal; zero is an explicit, valid override.
internal sealed record CssFontFaceMetrics(double SizeAdjust = 1, double Ascent = -1, double Descent = -1,
    double LineGap = -1, double VerticalAscent = -1, double VerticalDescent = -1, double VerticalLineGap = -1)
{
    internal static readonly CssFontFaceMetrics Initial = new();
    internal bool HasHorizontalOverrides => Ascent >= 0 || Descent >= 0 || LineGap >= 0;
    internal bool IsInitial => this == Initial;
    internal double[] Values => [SizeAdjust, Ascent, Descent, LineGap, VerticalAscent, VerticalDescent, VerticalLineGap];
    internal static CssFontFaceMetrics? FromValues(IReadOnlyList<double> values)
        => values.Count == 7 && double.IsFinite(values[0]) && values[0] >= 0 &&
           values.Skip(1).All(v => double.IsFinite(v) && (v == -1 || v >= 0))
            ? new(values[0], values[1], values[2], values[3], values[4], values[5], values[6]) : null;

    internal string Encode() => string.Join(',', Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    internal static CssFontFaceMetrics? Decode(ReadOnlySpan<char> source)
    {
        var parts = source.ToString().Split(',');
        if (parts.Length != 7) return null;
        var values = new double[7];
        for (int i = 0; i < values.Length; ++i)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        return FromValues(values);
    }

    internal static bool TrySizeAdjust(string source, out double value)
    {
        var reader = new CssTokenReader(source);
        return TryPercentage(ref reader, out value) && reader.AtEnd;
    }
    internal static bool TryOverride(string source, out double horizontal, out double vertical)
    {
        horizontal = vertical = -1;
        var reader = new CssTokenReader(source);
        return ReadMetric(ref reader, out horizontal) && (reader.AtEnd || ReadMetric(ref reader, out vertical) && reader.AtEnd);
    }
    private static bool ReadMetric(ref CssTokenReader reader, out double value)
    {
        var probe = reader; value = -1;
        if (probe.TryReadIdent(out var word) && word.Equals("normal", StringComparison.OrdinalIgnoreCase))
        { reader = probe; return true; }
        return TryPercentage(ref reader, out value);
    }
    private static bool TryPercentage(ref CssTokenReader reader, out double value)
    {
        value = 0;
        if (!reader.TryReadNumber(out var number, out var unit) || unit != CssUnit.Percent || !double.IsFinite(number) ||
            number < 0 && !reader.NumberWasCalculated) return false;
        value = Math.Max(0, number) / 100;
        return true;
    }
}
