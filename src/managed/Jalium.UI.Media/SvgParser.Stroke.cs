using System.Xml.Linq;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private static double StrokePercentageBasis()
    {
        var largest = Math.Max(Math.Abs(t_viewportWidth), Math.Abs(t_viewportHeight));
        if (largest == 0 || !double.IsFinite(largest)) return 0;
        var width = t_viewportWidth / largest;
        var height = t_viewportHeight / largest;
        return largest * Math.Sqrt((width * width + height * height) / 2);
    }

    private static double ResolveStrokeLength(XElement element, string name, double fallback)
    {
        var raw = GetResolvedAttribute(element, name);
        if (raw is null) return fallback;
        var reader = new CssTokenReader(raw);
        if (!reader.TryReadLength(out var length) || !reader.AtEnd) return fallback;
        var context = StrokeLengths(element);
        return TryResolveStrokeLength(length, context, 1, out var result) ? result : fallback;
    }

    private static CssLengthContext StrokeLengths(XElement element)
        => t_styles?.Lengths(element) ?? new CssLengthContext(16, 16, 16, t_viewportWidth, t_viewportHeight);

    private static bool TryResolveStrokeLength(CssLength length, CssLengthContext context, double scale, out double result)
    {
        result = length.Unit == CssUnit.Percent ? length.Value * StrokePercentageBasis() / 100
            : length.Expression is { } expression
                ? ScaleStrokeExpression(expression, scale).TryEvaluate(context, StrokePercentageBasis(), out var evaluated) ? evaluated : double.NaN
                : length.TryResolve(context, CssPercentBasis.NotSupported, out var pixels) ? pixels * scale : double.NaN;
        return double.IsFinite(result);
    }

    private static CssMathExpression ScaleStrokeExpression(CssMathExpression expression, double scale)
    {
        if (scale == 1) return expression;
        if (expression.Operation == "value" && expression.Kind == CssNumericKind.Length)
            return expression with { Literal = new CssLength(expression.Literal.Value * scale, expression.Literal.Unit) };
        return expression with { Arguments = expression.Arguments.Select(argument => ScaleStrokeExpression(argument, scale)).ToArray() };
    }

    private static double? AuthoredPathLength(XElement element)
    {
        if (element.Name.LocalName is not ("path" or "rect" or "circle" or "ellipse" or "line" or "polygon" or "polyline"))
            return null;
        var raw = t_styles?.Get(element, "path-length") ?? element.Attribute("pathLength")?.Value;
        if (raw is null || raw.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var reader = new CssTokenReader(raw);
        return reader.TryReadLength(out var value) && !value.UsesPercent && reader.AtEnd &&
            value.TryResolve(StrokeLengths(element), CssPercentBasis.NotSupported, out var number) &&
            number >= 0 && double.IsFinite(number) ? number : null;
    }

    private static bool ApplyStrokeDashes(XElement element, Geometry geometry, Pen pen)
    {
        var raw = GetResolvedAttribute(element, "stroke-dasharray");
        if (raw is null || raw.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        if (!SvgCssStyles.TryReadStrokeLengths(raw, out var lengths)) return true;
        var context = StrokeLengths(element);
        var offsetReader = new CssTokenReader(GetResolvedAttribute(element, "stroke-dashoffset") ?? "0");
        if (!offsetReader.TryReadLength(out var offsetLength) || !offsetReader.AtEnd) offsetLength = new(0, CssUnit.Px);
        var authored = AuthoredPathLength(element);
        var measured = geometry.GetFlattenedPathGeometry(.0001, ToleranceType.Absolute).GetTotalLength();
        if (!double.IsFinite(measured)) return false;
        var scale = authored is > 0 ? measured / authored.Value : 1;
        // A host-space path can be much longer than this local geometry. An
        // infinite pathLength limit must remain beyond the complete host path.
        var patternExtent = GetResolvedAttribute(element, "vector-effect") == "non-scaling-stroke" ? 1e29 : measured;
        double[] dashes;
        double offset;
        if (authored == 0 || !double.IsFinite(scale))
        {
            if (!TryZeroPathPattern(lengths, offsetLength, context, patternExtent, pen.Thickness,
                out dashes, out offset, out var paint)) return false;
            if (dashes.Length == 0) return paint;
        }
        else
        {
            dashes = new double[lengths.Length];
            for (var i = 0; i < lengths.Length; i++)
            {
                if (!TryResolveStrokeLength(lengths[i], context, scale, out var distance)) return false;
                dashes[i] = Math.Max(0, distance);
            }
            if (!TryResolveStrokeLength(offsetLength, context, scale, out offset)) return false;
        }
        return SetFiniteStrokePattern(pen, dashes, offset, patternExtent);
    }

    // Bound pattern entries only after locating the original phase. A run longer
    // than the complete path cannot end on that path; keeping huge CSS lengths
    // out of the float native API preserves that fact without overflow.
    private static bool SetFiniteStrokePattern(Pen pen, double[] values, double offset, double measured)
    {
        if (values.Length % 2 != 0) values = [.. values, .. values];
        var maximum = values.Max();
        if (maximum == 0) return true;
        if (!double.IsFinite(maximum) || !double.IsFinite(offset)) return false;
        var total = values.Sum(value => value / maximum);
        var phase = (offset / maximum) % total;
        if (phase < 0) phase += total;
        var index = 0;
        while (index < values.Length - 1 && phase >= values[index] / maximum)
            phase -= values[index++] / maximum;
        var consumed = phase * maximum;
        var remaining = Math.Max(0, values[index] - consumed);
        var bound = Math.Min(1e30, Math.Max(1, measured + pen.Thickness * 4 + 1) * 4);
        if (consumed > bound && remaining > bound) return index % 2 == 0;
        var bounded = values.Select(value => Math.Min(value, bound)).ToArray();
        var nativeOffset = bounded.Take(index).Sum() + (consumed <= bound ? consumed : bounded[index] - remaining);
        pen.DashStyle = new DashStyle(bounded.Select(value => value / pen.Thickness), nativeOffset / pen.Thickness);
        return true;
    }

    private readonly record struct StrokeLimit(double Absolute, double Percentage)
    {
        public static StrokeLimit operator +(StrokeLimit a, StrokeLimit b) => new(a.Absolute + b.Absolute, a.Percentage + b.Percentage);
        public static StrokeLimit operator -(StrokeLimit a, StrokeLimit b) => new(a.Absolute - b.Absolute, a.Percentage - b.Percentage);
        internal int CompareTo(StrokeLimit other) => Absolute != other.Absolute ? Absolute.CompareTo(other.Absolute) : Percentage.CompareTo(other.Percentage);
    }

    private static bool TryStrokeLimit(CssLength length, CssLengthContext context, out StrokeLimit value)
    {
        value = default;
        if (length.Unit == CssUnit.Percent) { value = new(0, length.Value * StrokePercentageBasis() / 100); return true; }
        if (length.Expression is not { } expression)
        {
            if (!length.TryResolve(context, CssPercentBasis.NotSupported, out var pixels)) return false;
            value = new(pixels, 0); return true;
        }
        var normalized = CssPropertySyntax.Normalize(expression, new(context));
        return normalized is not null && TryStrokeLimit(normalized, context, out value);
    }

    private static bool TryStrokeLimit(CssMathExpression expression, CssLengthContext context, out StrokeLimit value)
    {
        value = default;
        if (CssPropertySyntax.Linear(expression, out var absolute, out var percentage))
        { value = new(absolute, percentage * StrokePercentageBasis() / 100); return true; }
        if (!expression.UsesPercent && expression.TryEvaluate(context, double.NaN, out var pixels))
        { value = new(pixels, 0); return true; }
        var args = new List<StrokeLimit>();
        foreach (var argument in expression.Arguments)
        {
            if (!TryStrokeLimit(argument, context, out var item)) return false;
            args.Add(item);
        }
        if (expression.Operation is "min" or "max")
        {
            value = args[0];
            foreach (var item in args.Skip(1))
                if (expression.Operation == "min" ? item.CompareTo(value) < 0 : item.CompareTo(value) > 0) value = item;
            return true;
        }
        if (expression.Operation == "clamp")
        { value = args[1].CompareTo(args[2]) > 0 ? args[2] : args[1]; if (args[0].CompareTo(value) > 0) value = args[0]; return true; }
        if (expression.Operation is "+" or "-") { value = expression.Operation == "+" ? args[0] + args[1] : args[0] - args[1]; return true; }
        if (expression.Operation == "calc") { value = args[0]; return true; }
        if (expression.TryEvaluate(context, StrokePercentageBasis(), out var finite) &&
            args.All(item => item.Absolute == 0)) { value = new(0, finite); return true; }
        return false;
    }

    private static bool TryZeroPathPattern(CssLength[] lengths, CssLength offsetLength, CssLengthContext context,
        double measured, double width, out double[] dashes, out double offset, out bool paint)
    {
        dashes = []; offset = 0; paint = true;
        var values = new List<StrokeLimit>();
        foreach (var length in lengths)
        {
            if (!TryStrokeLimit(length, context, out var value)) return false;
            if (value.CompareTo(default) < 0) value = default;
            values.Add(value);
        }
        if (values.Count % 2 != 0) values.AddRange(values.ToArray());
        var cycle = values.Aggregate(default(StrokeLimit), (sum, value) => sum + value);
        if (cycle == default) return true;
        if (!TryStrokeLimit(offsetLength, context, out var phase)) return false;
        if (cycle.Absolute == 0)
        {
            if (phase.Absolute != 0) return false;
            dashes = values.Select(value => value.Percentage).ToArray(); offset = phase.Percentage; return true;
        }
        var remainder = phase.Absolute % cycle.Absolute;
        if (remainder < 0) remainder += cycle.Absolute;
        var cycles = (phase.Absolute - remainder) / cycle.Absolute;
        phase = new(remainder, phase.Percentage - (cycle.Percentage == 0 ? 0 : cycles * cycle.Percentage));
        if (!double.IsFinite(phase.Percentage)) return false;
        if (phase.CompareTo(default) < 0) phase += cycle;
        if (phase.CompareTo(cycle) >= 0) phase -= cycle;
        var prefix = default(StrokeLimit); var index = 0;
        while (index < values.Count - 1 && phase.CompareTo(prefix + values[index]) > 0) prefix += values[index++];
        var consumed = phase - prefix;
        var remaining = values[index] - consumed;
        if (consumed.Absolute > 0 && remaining.Absolute > 0)
        { paint = index % 2 == 0; return true; }
        var bound = Math.Min(1e30, (measured + width * 4 + values.Sum(value => Math.Abs(value.Percentage)) + Math.Abs(phase.Percentage) + 1) * 4);
        dashes = values.Select(value => value.Absolute > 0 ? bound : Math.Max(0, value.Percentage)).ToArray();
        offset = dashes.Take(index).Sum() + (consumed.Absolute == 0 ? consumed.Percentage : dashes[index] - remaining.Percentage);
        return double.IsFinite(offset);
    }
}
