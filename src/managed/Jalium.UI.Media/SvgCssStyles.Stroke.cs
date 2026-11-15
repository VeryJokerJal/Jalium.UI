using System.Globalization;
using System.Xml.Linq;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal sealed partial class SvgCssStyles
{
    private static CssDeclaration PresentationAttribute(XAttribute attribute)
    {
        if (attribute.Name.LocalName == "gradientTransform" && attribute.Parent?.Name.LocalName is "linearGradient" or "radialGradient")
            return new() { PropertyName = "transform", RawValue = attribute.Value };
        if (attribute.Name.LocalName != "pathLength")
            return new() { PropertyName = attribute.Name.LocalName, RawValue = attribute.Value };
        var raw = double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) + "px" : string.Empty;
        return new() { PropertyName = "path-length", RawValue = raw };
    }

    internal static bool TryReadStrokeLengths(string text, out CssLength[] lengths)
    {
        lengths = [];
        var reader = new CssTokenReader(text);
        var values = new List<CssLength>();
        while (!reader.AtEnd)
        {
            if (values.Count >= 1024 || !reader.TryReadLength(out var length) ||
                length.Expression is null && length.Value < 0) return false;
            values.Add(length);
            var end = reader.Position;
            if (reader.AtEnd) break;
            var separated = reader.Position > end;
            if (reader.TryReadComma())
            {
                if (reader.AtEnd) return false;
            }
            else if (!separated) return false;
        }
        lengths = values.ToArray();
        return lengths.Length > 0;
    }

    private static string ComputeStrokeLengths(string text, CssLengthContext lengths)
    {
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (!TryReadStrokeLengths(text, out var values)) return "none";
        var computed = new List<string>(values.Length);
        foreach (var value in values)
        {
            if (!CssPropertySyntax.ComputeLength(value, new(lengths), out var result, out _, out _)) return "none";
            result = ClampComputedStrokeLength(result, value, lengths);
            computed.Add(result);
        }
        return string.Join(" ", computed);
    }

    private static string ComputeStrokeLength(string text, CssLengthContext lengths, bool nonnegative = false, bool unitlessAbsolute = false)
    {
        if (TryLength(text, out var length) &&
            CssPropertySyntax.ComputeLength(length, new(lengths), out var computed, out _, out _))
        {
            computed = nonnegative ? ClampComputedStrokeLength(computed, length, lengths) : computed;
            if (unitlessAbsolute && computed.EndsWith("px", StringComparison.Ordinal) &&
                double.TryParse(computed.AsSpan(0, computed.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return computed[..^2];
            return computed;
        }
        return text;
    }

    private static string ClampComputedStrokeLength(string computed, CssLength length, CssLengthContext lengths)
    {
        if (!length.UsesPercent && length.TryResolve(lengths, CssPercentBasis.NotSupported, out var number) && number < 0) return "0px";
        if (length.Expression is { Kind: CssNumericKind.Percent } expression &&
            expression.TryEvaluate(lengths, 100, out var percentage) && percentage < 0) return "0%";
        return computed;
    }
}
