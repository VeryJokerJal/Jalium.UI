using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Styling;

/// <summary>
/// Parses the CSS transform function chain into Transform objects (a TransformGroup when
/// chained).
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
        List<CssTransformPrimitive>? primitives = null;
        bool has3D = false;

        while (!reader.AtEnd)
        {
            if (!reader.TryReadFunction(out var name, out var args) ||
                !TryParseFunction(name, ref args, out var parsed, out var primitive, out var is3D, lengths, referenceSize))
            {
                return false;
            }

            has3D |= is3D;
            (primitives ??= new List<CssTransformPrimitive>(4)).Add(primitive);

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
            if (has3D && primitives is not null)
            {
                var primArray = primitives.ToArray();
                CssTransform3D.Attach(group, CssTransform3D.ComposePrimitives(primArray), primArray);
            }
            return true;
        }

        if (single is not null)
        {
            transform = single;
            if (has3D && primitives is not null)
            {
                var primArray = primitives.ToArray();
                CssTransform3D.Attach(single, CssTransform3D.ComposePrimitives(primArray), primArray);
            }
            return true;
        }

        return false;
    }

    private static bool TryParseFunction(
        ReadOnlySpan<char> name,
        ref CssTokenReader args,
        out Transform transform,
        out CssTransformPrimitive primitive,
        out bool is3D,
        CssLengthContext? lengths,
        Size referenceSize)
    {
        transform = null!;
        primitive = default;
        is3D = false;

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
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Translate2D, x, y, 0, 0);
        }
        else if (Eq(name, "translatex"))
        {
            if (!TryReadTranslationLength(ref args, lengths, referenceSize.Width, out var x))
            {
                return false;
            }

            transform = new TranslateTransform { X = x };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.TranslateX, x, 0, 0, 0);
        }
        else if (Eq(name, "translatey"))
        {
            if (!TryReadTranslationLength(ref args, lengths, referenceSize.Height, out var y))
            {
                return false;
            }

            transform = new TranslateTransform { Y = y };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.TranslateY, 0, y, 0, 0);
        }
        else if (Eq(name, "translatez"))
        {
            if (!TryReadTranslationZLength(ref args, lengths, out var z))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.TranslateZ, 0, 0, z, 0);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "translate3d"))
        {
            if (!TryReadTranslationLength(ref args, lengths, referenceSize.Width, out var x) ||
                !args.TryReadComma() ||
                !TryReadTranslationLength(ref args, lengths, referenceSize.Height, out var y) ||
                !args.TryReadComma() ||
                !TryReadTranslationZLength(ref args, lengths, out var z))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Translate3D, x, y, z, 0);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "scale"))
        {
            if (!TryReadScaleFactor(ref args, out var sx))
            {
                return false;
            }

            var sy = sx;
            if (args.TryReadComma() && !TryReadScaleFactor(ref args, out sy))
            {
                return false;
            }

            transform = new ScaleTransform { ScaleX = sx, ScaleY = sy };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Scale2D, sx, sy, 1, 0);
        }
        else if (Eq(name, "scalex"))
        {
            if (!TryReadScaleFactor(ref args, out var sx))
            {
                return false;
            }

            transform = new ScaleTransform { ScaleX = sx };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.ScaleX, sx, 1, 1, 0);
        }
        else if (Eq(name, "scaley"))
        {
            if (!TryReadScaleFactor(ref args, out var sy))
            {
                return false;
            }

            transform = new ScaleTransform { ScaleY = sy };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.ScaleY, 1, sy, 1, 0);
        }
        else if (Eq(name, "scalez"))
        {
            if (!TryReadScaleFactor(ref args, out var sz))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.ScaleZ, 1, 1, sz, 0);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "scale3d"))
        {
            if (!TryReadScaleFactor(ref args, out var sx) ||
                !args.TryReadComma() ||
                !TryReadScaleFactor(ref args, out var sy) ||
                !args.TryReadComma() ||
                !TryReadScaleFactor(ref args, out var sz))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Scale3D, sx, sy, sz, 0);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "rotate"))
        {
            if (!TryReadAngle(ref args, out var angle))
            {
                return false;
            }

            transform = new RotateTransform { Angle = angle };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Rotate2D, 0, 0, 1, angle);
        }
        else if (Eq(name, "rotatez"))
        {
            if (!TryReadAngle(ref args, out var angle))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.RotateZ, 0, 0, 1, angle);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "rotatex"))
        {
            if (!TryReadAngle(ref args, out var angle))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.RotateX, 1, 0, 0, angle);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "rotatey"))
        {
            if (!TryReadAngle(ref args, out var angle))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.RotateY, 0, 1, 0, angle);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
        }
        else if (Eq(name, "rotate3d"))
        {
            if (!TryReadNumberOnly(ref args, out var x) ||
                !args.TryReadComma() ||
                !TryReadNumberOnly(ref args, out var y) ||
                !args.TryReadComma() ||
                !TryReadNumberOnly(ref args, out var z) ||
                !args.TryReadComma() ||
                !TryReadAngle(ref args, out var angle))
            {
                return false;
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Rotate3D, x, y, z, angle);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
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
            CssTransformInterpolation.MarkSkewFunction((SkewTransform)transform, CssSkewFunction.Skew);
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Skew, ax, ay, 0, 0);
        }
        else if (Eq(name, "skewx"))
        {
            if (!TryReadAngle(ref args, out var ax))
            {
                return false;
            }

            transform = new SkewTransform { AngleX = ax };
            CssTransformInterpolation.MarkSkewFunction((SkewTransform)transform, CssSkewFunction.SkewX);
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.SkewX, ax, 0, 0, 0);
        }
        else if (Eq(name, "skewy"))
        {
            if (!TryReadAngle(ref args, out var ay))
            {
                return false;
            }

            transform = new SkewTransform { AngleY = ay };
            CssTransformInterpolation.MarkSkewFunction((SkewTransform)transform, CssSkewFunction.SkewY);
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.SkewY, 0, ay, 0, 0);
        }
        else if (Eq(name, "perspective"))
        {
            double d;
            var probe = args;
            if (probe.TryReadIdent(out var ident))
            {
                if (!ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                    return false;
                args = probe;
                d = double.PositiveInfinity;
            }
            else
            {
                if (!TryReadTranslationZLength(ref args, lengths, out d) || d < 0)
                {
                    return false;
                }
            }

            is3D = true;
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Perspective, d, 0, 0, 0);
            var m3d = primitive.ToMatrix3D();
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
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

            var mat2D = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]);
            transform = new MatrixTransform { Matrix = mat2D };
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Matrix2D, 0, 0, 0, 0, CssTransform3D.ToMatrix3D(mat2D));
        }
        else if (Eq(name, "matrix3d"))
        {
            Span<double> m = stackalloc double[16];
            for (var i = 0; i < 16; i++)
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

            is3D = true;
            var m3d = new Matrix3D(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
            primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Matrix3D, 0, 0, 0, 0, m3d);
            transform = new MatrixTransform { Matrix = CssTransform3D.ToAffineMatrix2D(m3d) };
            CssTransform3D.Attach(transform, m3d, [primitive]);
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

    private static bool TryReadTranslationLength(ref CssTokenReader reader, CssLengthContext? context, double basis, out double px)
    {
        px = 0;
        if (!reader.TryReadLength(out var length))
        {
            return false;
        }

        // CSS <length-percentage> accepts a unitless zero, but not a nonzero number.
        if (length.Unit == CssUnit.None && length.Value != 0) return false;

        if (length.IsAbsolute) { px = length.ToPxAbsolute(); return double.IsFinite(px); }
        if (context is null) return true; // Syntax validation; the owning element resolves this value later.
        if (length.Expression is { } expression) return expression.TryEvaluate(context.Value, basis, out px);
        if (length.Unit == CssUnit.Percent) { px = length.Value / 100 * basis; return double.IsFinite(px); }
        return length.TryResolve(context.Value, CssPercentBasis.NotSupported, out px);
    }

    private static bool TryReadTranslationZLength(ref CssTokenReader reader, CssLengthContext? context, out double px)
    {
        px = 0;
        if (!reader.TryReadLength(out var length) || length.UsesPercent || !length.IsLengthUnit)
        {
            return false;
        }

        if (length.Unit == CssUnit.None && length.Value != 0) return false;
        if (length.IsAbsolute) { px = length.ToPxAbsolute(); return double.IsFinite(px); }
        if (context is null) return true;
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

    private static bool TryReadScaleFactor(ref CssTokenReader reader, out double factor)
    {
        factor = 0;
        if (!reader.TryReadNumber(out var value, out var unit)) return false;
        if (unit == CssUnit.None) { factor = value; return true; }
        if (unit == CssUnit.Percent) { factor = value / 100; return true; }
        return false;
    }

    private static bool TryReadAngle(ref CssTokenReader reader, out double degrees)
    {
        degrees = 0;
        return reader.TryReadNumber(out var value, out var unit) &&
               (unit != CssUnit.None || value == 0) &&
               CssUnitConversion.TryToDegrees(value, unit, out degrees);
    }
}
