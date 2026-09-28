using System.Globalization;
using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssColorParser
{
    private static bool IsRelativeFunction(ReadOnlySpan<char> name) =>
        name.Equals("rgb", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("rgba", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("hsl", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("hsla", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("hwb", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("lab", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("lch", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("oklab", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("oklch", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("color", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("alpha", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseRelative(ReadOnlySpan<char> name, ref CssTokenReader args,
        int depth, out Color color, out CssColorData data)
    {
        color = default;
        data = default;
        if (!IsRelativeFunction(name) || !args.TryReadIdent(out var from) ||
            !from.Equals("from", StringComparison.OrdinalIgnoreCase) ||
            !TryParseWithData(ref args, out _, out var unresolvedCurrentColor,
                out var origin, depth + 1) || unresolvedCurrentColor)
            return false;

        var function = name.ToString().ToLowerInvariant() switch
        {
            "rgba" => "rgb",
            "hsla" => "hsl",
            var other => other,
        };
        var spaceName = function == "rgb" ? "srgb" : function;
        if (function == "color")
        {
            if (!args.TryReadIdent(out var spaceIdent)) return false;
            spaceName = spaceIdent.ToString().ToLowerInvariant();
            if (!TryConvertPredefined(spaceName, default, out _)) return false;
        }

        var space = SourceSpace(spaceName);
        var converted = ToInterpolationSpace(origin, space);
        if (!converted.Finite) return false;
        var originMissing = MissingForSpace(origin, space, converted);
        var channels = function == "rgb"
            ? new Components(converted.X * 255, converted.Y * 255, converted.Z * 255)
            : converted;
        var rewritten = SubstituteRelativeChannels(args.Remaining, function, space,
            channels, origin.Alpha, originMissing);
        var values = new CssTokenReader(rewritten, args.NumericContext);

        double first = 0, second = 0, third = 0;
        bool missingFirst = false, missingSecond = false, missingThird = false;
        if (function != "alpha")
        {
            switch (function)
            {
                case "rgb":
                    if (!TryReadComponent(ref values, 255, out first, out missingFirst) ||
                        !TryReadSeparatedComponent(ref values, 255, out second, out missingSecond) ||
                        !TryReadSeparatedComponent(ref values, 255, out third, out missingThird)) return false;
                    first /= 255; second /= 255; third /= 255;
                    break;
                case "hsl": case "hwb":
                    if (!TryReadHue(ref values, out first, out missingFirst) ||
                        !TryReadSeparatedComponent(ref values, 100, out second, out missingSecond) ||
                        !TryReadSeparatedComponent(ref values, 100, out third, out missingThird)) return false;
                    break;
                case "lab": case "lch": case "oklab": case "oklch":
                    var isOk = function is "oklab" or "oklch";
                    var polar = function is "lch" or "oklch";
                    if (!TryReadComponent(ref values, isOk ? 1 : 100,
                            out first, out missingFirst) ||
                        !TryReadSeparatedComponent(ref values,
                            isOk ? .4 : polar ? 150 : 125,
                            out second, out missingSecond)) return false;
                    if (polar)
                    {
                        if (!HasSeparator(ref values) ||
                            !TryReadHue(ref values, out third, out missingThird)) return false;
                    }
                    else if (!TryReadSeparatedComponent(ref values, isOk ? .4 : 125,
                        out third, out missingThird)) return false;
                    break;
                case "color":
                    if (!TryReadComponent(ref values, 1, out first, out missingFirst) ||
                        !TryReadSeparatedComponent(ref values, 1, out second, out missingSecond) ||
                        !TryReadSeparatedComponent(ref values, 1, out third, out missingThird)) return false;
                    break;
                default:
                    return false;
            }
        }

        var alphaProbe = values;
        var hasAlpha = alphaProbe.TryReadSlash();
        if (!TryReadModernAlpha(ref values, out var alpha, out var alphaMissing)) return false;
        if (!hasAlpha)
        {
            alpha = origin.Alpha;
            alphaMissing = (origin.Missing & 8) != 0;
        }
        alpha = Math.Clamp(alpha, 0, 1);

        var source = function == "alpha"
            ? new Components(origin.X, origin.Y, origin.Z)
            : new Components(first, second, third);
        if (!source.Finite || !double.IsFinite(alpha)) return false;
        var resultSpace = function == "alpha" ? SourceSpace(origin.Space) : space;
        var linear = FromInterpolationSpace(source, resultSpace);
        if (!linear.Finite) return false;
        var mapped = MapToSrgb(linear);
        color = Color.FromScRgb((float)alpha, (float)mapped.X,
            (float)mapped.Y, (float)mapped.Z);
        var missing = function == "alpha" ? (byte)((origin.Missing & 7) |
            (alphaMissing ? 8 : 0)) :
            MissingBits(missingFirst, missingSecond, missingThird, alphaMissing);
        data = new(color, function == "alpha" ? origin.Space : spaceName,
            source.X, source.Y, source.Z, alpha, missing, false);
        return true;
    }

    private static string SubstituteRelativeChannels(ReadOnlySpan<char> source,
        string function, CssInterpolationSpace space, Components channels,
        double alpha, byte missing)
    {
        var result = new StringBuilder(source.Length + 24);
        var functionDepth = 0;
        for (var i = 0; i < source.Length;)
        {
            if (source[i] is '\'' or '"')
            {
                var end = CssTokenReader.SkipString(source, i);
                result.Append(source[i..end]);
                i = end;
                continue;
            }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = CssTokenReader.SkipComment(source, i);
                result.Append(source[i..end]);
                i = end;
                continue;
            }
            if (source[i] == '(') { functionDepth++; result.Append(source[i++]); continue; }
            if (source[i] == ')') { functionDepth--; result.Append(source[i++]); continue; }
            var endOfIdent = i;
            if (CssSyntax.ReadIdentifier(source, ref endOfIdent, out var name))
            {
                if (TryRelativeChannel(name, function, space, channels, alpha,
                        missing, out var value, out var isMissing))
                {
                    if (isMissing && functionDepth == 0)
                        result.Append("none");
                    else
                        result.Append((isMissing ? 0 : value).ToString("R", CultureInfo.InvariantCulture));
                    // A hex escape may consume the whitespace that separated
                    // this channel from the next one. Keep a token boundary.
                    result.Append(' ');
                }
                else result.Append(source[i..endOfIdent]);
                i = endOfIdent;
                continue;
            }
            result.Append(source[i++]);
        }
        return result.ToString();
    }

    private static bool TryRelativeChannel(ReadOnlySpan<char> name,
        string function, CssInterpolationSpace space, Components channels,
        double alpha, byte missing, out double value, out bool isMissing)
    {
        value = 0;
        isMissing = false;
        if (name.Equals("alpha", StringComparison.OrdinalIgnoreCase))
        {
            value = alpha;
            isMissing = (missing & 8) != 0;
            return true;
        }
        var index = -1;
        if (function == "rgb" ||
            (function == "color" &&
             space is not (CssInterpolationSpace.XyzD50 or CssInterpolationSpace.XyzD65)))
        {
            if (name.Equals("r", StringComparison.OrdinalIgnoreCase)) index = 0;
            else if (name.Equals("g", StringComparison.OrdinalIgnoreCase)) index = 1;
            else if (name.Equals("b", StringComparison.OrdinalIgnoreCase)) index = 2;
        }
        else if (function == "color")
        {
            if (name.Equals("x", StringComparison.OrdinalIgnoreCase)) index = 0;
            else if (name.Equals("y", StringComparison.OrdinalIgnoreCase)) index = 1;
            else if (name.Equals("z", StringComparison.OrdinalIgnoreCase)) index = 2;
        }
        else if (function is "hsl" or "hwb")
        {
            if (name.Equals("h", StringComparison.OrdinalIgnoreCase)) index = 0;
            else if (name.Equals(function == "hsl" ? "s" : "w", StringComparison.OrdinalIgnoreCase)) index = 1;
            else if (name.Equals(function == "hsl" ? "l" : "b", StringComparison.OrdinalIgnoreCase)) index = 2;
        }
        else if (function is "lab" or "oklab")
        {
            if (name.Equals("l", StringComparison.OrdinalIgnoreCase)) index = 0;
            else if (name.Equals("a", StringComparison.OrdinalIgnoreCase)) index = 1;
            else if (name.Equals("b", StringComparison.OrdinalIgnoreCase)) index = 2;
        }
        else if (function is "lch" or "oklch")
        {
            if (name.Equals("l", StringComparison.OrdinalIgnoreCase)) index = 0;
            else if (name.Equals("c", StringComparison.OrdinalIgnoreCase)) index = 1;
            else if (name.Equals("h", StringComparison.OrdinalIgnoreCase)) index = 2;
        }
        if (index < 0) return false;
        value = index switch { 0 => channels.X, 1 => channels.Y, _ => channels.Z };
        isMissing = (missing & (1 << index)) != 0;
        return true;
    }
}
