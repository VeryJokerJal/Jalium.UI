using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterCaretColor()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "caret-color",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssCaretColorProperties.ValueProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
                {
                    reader = probe;
                    return new CssImmediateValue(CssCaretColorProperties.ValueProperty, null);
                }

                if (!CssColorParser.TryParseContextual(ref reader, out var color, out var current, out var deferred) ||
                    !reader.AtEnd) return null;
                if (deferred is not null) return new CssContextualColorValue("caret-color", deferred);
                if (current) return new CssContextualColorValue("caret-color", "currentColor");
                return new CssImmediateValue(CssCaretColorProperties.ValueProperty,
                    FreezeIfPossible(new SolidColorBrush(color)));
            },
        });
    }

    private static void RegisterCaretAnimation()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "caret-animation",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssCaretAnimationProperties.ManualProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var keyword) || !reader.AtEnd) return null;
                if (keyword.Equals("manual", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(CssCaretAnimationProperties.ManualProperty, true);
                if (keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(CssCaretAnimationProperties.ManualProperty, false);
                return null;
            },
        });
    }

    private static void RegisterCaretShape()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "caret-shape",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssCaretShapeProperties.ValueProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var keyword) || !reader.AtEnd) return null;
                if (!TryCaretShape(keyword, out var shape)) return null;
                return new CssImmediateValue(CssCaretShapeProperties.ValueProperty, shape);
            },
        });
    }

    private static void RegisterCaretShorthand()
    {
        RegisterShorthand("caret", static (ref CssTokenReader reader, CssCompileContext _,
            List<CssCompiledDeclaration> output) =>
        {
            CssCompiledValue? colorValue = null;
            var shape = CssCaretShape.Auto;
            var manual = false;
            var hasColor = false;
            var hasShape = false;
            var hasAnimation = false;
            var autoCount = 0;

            while (!reader.AtEnd)
            {
                var probe = reader;
                if (probe.TryReadIdent(out var keyword))
                {
                    if (keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        autoCount++;
                        reader = probe;
                        continue;
                    }
                    if (keyword.Equals("manual", StringComparison.OrdinalIgnoreCase))
                    {
                        if (hasAnimation) return false;
                        hasAnimation = true;
                        manual = true;
                        reader = probe;
                        continue;
                    }
                    if (TryCaretShape(keyword, out var parsedShape) && parsedShape != CssCaretShape.Auto)
                    {
                        if (hasShape) return false;
                        hasShape = true;
                        shape = parsedShape;
                        reader = probe;
                        continue;
                    }
                }

                probe = reader;
                if (hasColor || !CssColorParser.TryParseContextual(ref probe, out var color,
                        out var current, out var deferred)) return false;
                hasColor = true;
                colorValue = deferred is not null ? new CssContextualColorValue("caret-color", deferred)
                    : current ? new CssContextualColorValue("caret-color", "currentColor")
                    : new CssImmediateValue(CssCaretColorProperties.ValueProperty,
                        FreezeIfPossible(new SolidColorBrush(color)));
                reader = probe;
            }

            var explicitCount = (hasColor ? 1 : 0) + (hasShape ? 1 : 0) + (hasAnimation ? 1 : 0);
            if (explicitCount + autoCount is < 1 or > 3) return false;
            output.Add(new CssCompiledDeclaration("caret-color",
                colorValue ?? new CssImmediateValue(CssCaretColorProperties.ValueProperty, null), false));
            output.Add(new CssCompiledDeclaration("caret-animation",
                new CssImmediateValue(CssCaretAnimationProperties.ManualProperty, manual), false));
            output.Add(new CssCompiledDeclaration("caret-shape",
                new CssImmediateValue(CssCaretShapeProperties.ValueProperty, shape), false));
            return true;
        });
    }

    private static bool TryCaretShape(ReadOnlySpan<char> keyword, out CssCaretShape shape)
    {
        if (keyword.Equals("auto", StringComparison.OrdinalIgnoreCase)) shape = CssCaretShape.Auto;
        else if (keyword.Equals("bar", StringComparison.OrdinalIgnoreCase)) shape = CssCaretShape.Bar;
        else if (keyword.Equals("block", StringComparison.OrdinalIgnoreCase)) shape = CssCaretShape.Block;
        else if (keyword.Equals("underscore", StringComparison.OrdinalIgnoreCase)) shape = CssCaretShape.Underscore;
        else { shape = default; return false; }
        return true;
    }
}
