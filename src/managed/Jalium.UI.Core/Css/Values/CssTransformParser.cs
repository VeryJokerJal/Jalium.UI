using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Parses the CSS transform function chain into Transform objects (a TransformGroup when
/// chained) and transform-origin into the relative RenderTransformOrigin point.
/// </summary>
internal static class CssTransformParser
{
    internal static CssLength[] RelativeLengths(ReadOnlySpan<char> text)
    {
        var result = new List<CssLength>();
        Read(text, result, 0);
        return result.ToArray();

        static void Read(ReadOnlySpan<char> text, List<CssLength> result, int depth)
        {
            if (depth > 64) return;
            var reader = new CssTokenReader(text);
            while (!reader.AtEnd)
            {
                var probe = reader;
                if (probe.TryReadLength(out var length))
                {
                    if (!length.IsAbsolute) result.Add(length);
                    reader = probe; continue;
                }
                probe = reader;
                if (probe.TryReadFunction(out _, out var arguments))
                { Read(arguments.Remaining, result, depth + 1); reader = probe; continue; }
                if (reader.TryReadIdent(out _)) continue;
                reader = new CssTokenReader(reader.Remaining[1..]);
            }
        }
    }

    /// <summary>Returns the parsed transform, null for "none", or false on failure.</summary>
    public static bool TryParseTransformList(ref CssTokenReader reader, out Transform? transform,
        CssLengthContext? lengths = null, Size referenceSize = default)
    {
        transform = null;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
            {
                reader = probe;
                return true;
            }

            return false;
        }

        List<Transform>? chain = null;
        Transform? single = null;
        while (!reader.AtEnd)
        {
            if (!reader.TryReadFunction(out var name, out var args) ||
                !TryParseFunction(name, ref args, out var parsed, lengths, referenceSize))
            {
                return false;
            }

            if (single is null && chain is null)
            {
                single = parsed;
            }
            else
            {
                if (chain is null)
                {
                    chain = new List<Transform> { single! };
                    single = null;
                }

                chain.Add(parsed);
            }
        }

        if (chain is not null)
        {
            var group = new TransformGroup();
            foreach (var item in chain)
            {
                group.Children.Add(item);
            }

            transform = group;
            return true;
        }

        if (single is not null)
        {
            transform = single;
            return true;
        }

        return false;
    }

    private static bool TryParseFunction(ReadOnlySpan<char> name, ref CssTokenReader args, out Transform transform,
        CssLengthContext? lengths, Size referenceSize)
    {
        transform = null!;
        if (Eq(name, "translate"))
        {
            if (!TryReadTranslationLength(ref args, lengths, referenceSize.Width, out var x))
            {
                return false;
            }

            var y = 0.0;
            if (args.TryReadComma() && !TryReadTranslationLength(ref args, lengths, referenceSize.Height, out y))
            {
                return false;
            }

            transform = new TranslateTransform { X = x, Y = y };
        }
        else if (Eq(name, "translatex"))
        {
            if (!TryReadTranslationLength(ref args, lengths, referenceSize.Width, out var x))
            {
                return false;
            }

            transform = new TranslateTransform { X = x };
        }
        else if (Eq(name, "translatey"))
        {
            if (!TryReadTranslationLength(ref args, lengths, referenceSize.Height, out var y))
            {
                return false;
            }

            transform = new TranslateTransform { Y = y };
        }
        else if (Eq(name, "scale"))
        {
            if (!TryReadNumberOnly(ref args, out var sx))
            {
                return false;
            }

            var sy = sx;
            if (args.TryReadComma() && !TryReadNumberOnly(ref args, out sy))
            {
                return false;
            }

            transform = new ScaleTransform { ScaleX = sx, ScaleY = sy };
        }
        else if (Eq(name, "scalex"))
        {
            if (!TryReadNumberOnly(ref args, out var sx))
            {
                return false;
            }

            transform = new ScaleTransform { ScaleX = sx };
        }
        else if (Eq(name, "scaley"))
        {
            if (!TryReadNumberOnly(ref args, out var sy))
            {
                return false;
            }

            transform = new ScaleTransform { ScaleY = sy };
        }
        else if (Eq(name, "rotate"))
        {
            if (!TryReadAngle(ref args, out var angle))
            {
                return false;
            }

            transform = new RotateTransform { Angle = angle };
        }
        else if (Eq(name, "skew"))
        {
            if (!TryReadAngle(ref args, out var ax))
            {
                return false;
            }

            var ay = 0.0;
            if (args.TryReadComma() && !TryReadAngle(ref args, out ay))
            {
                return false;
            }

            transform = new SkewTransform { AngleX = ax, AngleY = ay };
        }
        else if (Eq(name, "skewx"))
        {
            if (!TryReadAngle(ref args, out var ax))
            {
                return false;
            }

            transform = new SkewTransform { AngleX = ax };
        }
        else if (Eq(name, "skewy"))
        {
            if (!TryReadAngle(ref args, out var ay))
            {
                return false;
            }

            transform = new SkewTransform { AngleY = ay };
        }
        else if (Eq(name, "matrix"))
        {
            Span<double> m = stackalloc double[6];
            for (var i = 0; i < 6; i++)
            {
                if (i > 0 && !args.TryReadComma())
                {
                    return false;
                }

                if (!TryReadNumberOnly(ref args, out m[i]))
                {
                    return false;
                }
            }

            transform = new MatrixTransform { Matrix = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]) };
        }
        else
        {
            CssDiagnostics.Report(
                "transform", CssDiagnosticReason.LossyConversion, null,
                $"transform function '{name.ToString()}' is not supported; declaration dropped");
            return false;
        }

        return args.AtEnd;
    }

    /// <summary>Percentages (self-size-relative) and font-relative units cannot be resolved; absolute only.</summary>
    private static bool TryReadTranslationLength(ref CssTokenReader reader, CssLengthContext? context, double basis, out double px)
    {
        px = 0;
        if (!reader.TryReadLength(out var length))
        {
            return false;
        }

        if (length.IsAbsolute) { px = length.ToPxAbsolute(); return double.IsFinite(px); }
        if (context is null) return true; // Syntax validation; the owning element resolves this value later.
        if (length.Expression is { } expression) return expression.TryEvaluate(context.Value, basis, out px);
        if (length.Unit == CssUnit.Percent) { px = length.Value / 100 * basis; return double.IsFinite(px); }
        return length.TryResolve(context.Value, CssPercentBasis.NotSupported, out px);
    }

    private static bool Eq(ReadOnlySpan<char> text, string candidate)
        => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadNumberOnly(ref CssTokenReader reader, out double value)
    {
        if (reader.TryReadNumber(out value, out var unit) && unit == CssUnit.None)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadAngle(ref CssTokenReader reader, out double degrees)
    {
        degrees = 0;
        return reader.TryReadNumber(out var value, out var unit) &&
               CssUnitConversion.TryToDegrees(value, unit, out degrees);
    }

    /// <summary>transform-origin: keywords/percentages map straight onto the relative origin point.</summary>
    public static bool TryParseTransformOrigin(ref CssTokenReader reader, out Point origin)
    {
        var x = double.NaN;
        var y = double.NaN;
        var count = 0;
        while (count < 2 && !reader.AtEnd)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident))
            {
                if (ident.Equals("left", StringComparison.OrdinalIgnoreCase) && double.IsNaN(x)) { x = 0; }
                else if (ident.Equals("right", StringComparison.OrdinalIgnoreCase) && double.IsNaN(x)) { x = 1; }
                else if (ident.Equals("top", StringComparison.OrdinalIgnoreCase) && double.IsNaN(y)) { y = 0; }
                else if (ident.Equals("bottom", StringComparison.OrdinalIgnoreCase) && double.IsNaN(y)) { y = 1; }
                else if (ident.Equals("center", StringComparison.OrdinalIgnoreCase))
                {
                    // Leaves both axes open; unset axes default to 0.5 below.
                }
                else
                {
                    origin = default;
                    return false;
                }

                reader = probe;
                count++;
                continue;
            }

            if (probe.TryReadNumber(out var value, out var unit) && unit == CssUnit.Percent)
            {
                if (count == 0)
                {
                    x = value / 100.0;
                }
                else
                {
                    y = value / 100.0;
                }

                reader = probe;
                count++;
                continue;
            }

            origin = default;
            return false;
        }

        if (count == 0)
        {
            origin = default;
            return false;
        }

        origin = new Point(double.IsNaN(x) ? 0.5 : x, double.IsNaN(y) ? 0.5 : y);
        return true;
    }
}
