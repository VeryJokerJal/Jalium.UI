using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssColorParser
{
    /// <summary>
    /// Reads one color token. A mix or relative color containing currentColor
    /// is validated with a placeholder and kept for per-element evaluation.
    /// </summary>
    internal static bool TryParseContextual(ref CssTokenReader reader, out Color color,
        out bool directCurrentColor, out string? deferredExpression)
    {
        deferredExpression = null;
        var original = reader;
        if (TryParse(ref reader, out color, out directCurrentColor)) return true;

        reader = original;
        color = default;
        directCurrentColor = false;
        var remaining = reader.Remaining;
        var functionReader = new CssTokenReader(remaining, reader.NumericContext);
        if (!functionReader.TryReadFunction(out var name, out var functionArgs) ||
            !name.Equals("color-mix", StringComparison.OrdinalIgnoreCase) &&
            !(IsRelativeFunction(name) && functionArgs.TryReadIdent(out var first) &&
              first.Equals("from", StringComparison.OrdinalIgnoreCase))) return false;
        var expression = remaining[..functionReader.Position];
        if (!TrySubstituteCurrentColor(expression, Colors.Black, out var substituted, out var depends) ||
            !depends) return false;
        var check = new CssTokenReader(substituted, reader.NumericContext);
        if (!TryParse(ref check, out color, out var stillCurrent) || stillCurrent || !check.AtEnd)
            return false;
        deferredExpression = expression.ToString();
        reader = new CssTokenReader(remaining[functionReader.Position..], reader.NumericContext);
        return true;
    }

    internal static bool TryResolveCurrentColor(string expression, Color currentColor, out Color color)
    {
        color = default;
        if (!TrySubstituteCurrentColor(expression, currentColor, out var substituted, out var depends) ||
            !depends) return false;
        var reader = new CssTokenReader(substituted);
        return TryParse(ref reader, out color, out var stillCurrent) && !stillCurrent && reader.AtEnd;
    }

    internal static bool TrySubstituteCurrentColor(ReadOnlySpan<char> source, Color currentColor,
        out string substituted, out bool depends)
    {
        var result = new StringBuilder(source.Length + 24);
        depends = false;
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
            var endOfIdent = i;
            if (CssSyntax.ReadIdentifier(source, ref endOfIdent, out var name))
            {
                if (name.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                {
                    result.Append('#')
                        .Append(currentColor.R.ToString("X2"))
                        .Append(currentColor.G.ToString("X2"))
                        .Append(currentColor.B.ToString("X2"))
                        .Append(currentColor.A.ToString("X2"));
                    depends = true;
                }
                else result.Append(source[i..endOfIdent]);
                i = endOfIdent;
                continue;
            }
            result.Append(source[i++]);
        }
        substituted = result.ToString();
        return true;
    }
}
