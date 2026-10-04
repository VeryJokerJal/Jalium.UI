using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterTextDecoration()
    {
        RegisterDecorationLonghand("text-decoration-line", CssTextDecorationProperties.LineProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadDecorationLines(ref reader, out var lines) && reader.AtEnd
                    ? new CssImmediateValue(CssTextDecorationProperties.LineProperty, lines) : null);

        RegisterDecorationLonghand("text-decoration-style", CssTextDecorationProperties.StyleProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadDecorationStyle(ref reader, out var style) && reader.AtEnd
                    ? new CssImmediateValue(CssTextDecorationProperties.StyleProperty, style) : null);

        RegisterDecorationLonghand("text-decoration-color", CssTextDecorationProperties.ColorProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadDecorationColor(ref reader, out var color) && reader.AtEnd ? color : null);

        RegisterDecorationLonghand("text-decoration-thickness", CssTextDecorationProperties.ThicknessProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadDecorationThickness(ref reader, out var thickness) && reader.AtEnd ? thickness : null);

        RegisterDecorationLonghand("text-underline-offset", CssTextDecorationProperties.UnderlineOffsetProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadUnderlineOffset(ref reader, out var offset) && reader.AtEnd ? offset : null);

        RegisterDecorationLonghand("text-underline-position", CssTextDecorationProperties.UnderlinePositionProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadUnderlinePosition(ref reader, out var position) && reader.AtEnd
                    ? new CssImmediateValue(CssTextDecorationProperties.UnderlinePositionProperty, position) : null);

        RegisterDecorationLonghand("text-decoration-skip-inset", CssTextDecorationProperties.SkipInsetProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                reader.TryReadIdent(out var ident) && reader.AtEnd
                    ? ident.ToString().ToLowerInvariant() switch
                    {
                        "none" => new CssImmediateValue(CssTextDecorationProperties.SkipInsetProperty,
                            CssTextDecorationSkipInset.None),
                        "auto" => new CssImmediateValue(CssTextDecorationProperties.SkipInsetProperty,
                            CssTextDecorationSkipInset.Auto),
                        _ => null,
                    }
                    : null);

        RegisterDecorationLonghand("text-decoration-inset", CssTextDecorationProperties.InsetProperty,
            static (ref CssTokenReader reader, CssCompileContext _) =>
                TryReadDecorationInset(ref reader));

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "box-decoration-break", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssBoxDecorationBreakProperties.ValueProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var value = ident.ToString().ToLowerInvariant() switch
                {
                    "slice" => CssBoxDecorationBreak.Slice,
                    "clone" => CssBoxDecorationBreak.Clone,
                    _ => (CssBoxDecorationBreak?)null,
                };
                return value is { } mode
                    ? new CssImmediateValue(CssBoxDecorationBreakProperties.ValueProperty, mode)
                    : null;
            },
        });

        RegisterShorthand("text-decoration",
            static (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                var lines = CssTextDecorationLine.None;
                var lineSeen = false;
                var noneSeen = false;
                var blinkSeen = false;
                CssTextDecorationStyle? style = null;
                CssCompiledValue? color = null;
                CssCompiledValue? thickness = null;
                var sawToken = false;
                while (!reader.AtEnd)
                {
                    var probe = reader;
                    if (probe.TryReadIdent(out var ident))
                    {
                        var token = ident.ToString().ToLowerInvariant();
                        var line = token switch
                        {
                            "underline" => CssTextDecorationLine.Underline,
                            "overline" => CssTextDecorationLine.Overline,
                            "line-through" => CssTextDecorationLine.LineThrough,
                            "spelling-error" => CssTextDecorationLine.SpellingError,
                            "grammar-error" => CssTextDecorationLine.GrammarError,
                            _ => CssTextDecorationLine.None,
                        };
                        if (line != CssTextDecorationLine.None || token is "none" or "blink")
                        {
                            var errorLine = line is CssTextDecorationLine.SpellingError or
                                CssTextDecorationLine.GrammarError;
                            var hasErrorLine = (lines & (CssTextDecorationLine.SpellingError |
                                CssTextDecorationLine.GrammarError)) != 0;
                            if (noneSeen || token == "none" && lineSeen ||
                                errorLine && lineSeen || hasErrorLine ||
                                line != CssTextDecorationLine.None && lines.HasFlag(line) ||
                                token == "blink" && blinkSeen)
                                return false;
                            lineSeen = true;
                            noneSeen = token == "none";
                            blinkSeen |= token == "blink";
                            lines |= line;
                            reader = probe;
                            sawToken = true;
                            continue;
                        }
                    }

                    probe = reader;
                    if (TryReadDecorationStyle(ref probe, out var parsedStyle))
                    {
                        if (style is not null) return false;
                        style = parsedStyle;
                        reader = probe;
                        sawToken = true;
                        continue;
                    }

                    probe = reader;
                    if (TryReadDecorationThickness(ref probe, out var parsedThickness))
                    {
                        if (thickness is not null) return false;
                        thickness = parsedThickness;
                        reader = probe;
                        sawToken = true;
                        continue;
                    }

                    probe = reader;
                    if (TryReadDecorationColor(ref probe, out var parsedColor))
                    {
                        if (color is not null) return false;
                        color = parsedColor;
                        reader = probe;
                        sawToken = true;
                        continue;
                    }

                    return false;
                }

                if (!sawToken) return false;
                output.Add(new CssCompiledDeclaration("text-decoration-line",
                    new CssImmediateValue(CssTextDecorationProperties.LineProperty, lines), false));
                output.Add(new CssCompiledDeclaration("text-decoration-style",
                    new CssImmediateValue(CssTextDecorationProperties.StyleProperty,
                        style ?? CssTextDecorationStyle.Solid), false));
                output.Add(new CssCompiledDeclaration("text-decoration-color",
                    color ?? new CssContextualColorValue("text-decoration-color", "currentColor"), false));
                output.Add(new CssCompiledDeclaration("text-decoration-thickness",
                    thickness ?? new CssImmediateValue(CssTextDecorationProperties.ThicknessProperty,
                        CssTextDecorationThickness.Auto), false));
                return true;
            });
    }

    private static void RegisterDecorationLonghand(string name, DependencyProperty storage,
        CssParseValueDelegate parse)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = storage,
            Parse = parse,
        });

    private static bool TryReadDecorationLines(ref CssTokenReader reader, out CssTextDecorationLine lines)
    {
        lines = CssTextDecorationLine.None;
        var seen = false;
        var seenBlink = false;
        var probe = reader;
        while (probe.TryReadIdent(out var ident))
        {
            var token = ident.ToString().ToLowerInvariant();
            if (token == "none")
            {
                if (seen || !probe.AtEnd) return false;
                reader = probe;
                return true;
            }
            if (token == "blink")
            {
                if (seenBlink) return false;
                seenBlink = true;
            }
            else if (token is "spelling-error" or "grammar-error")
            {
                if (seen || !probe.AtEnd) return false;
                lines = token == "spelling-error"
                    ? CssTextDecorationLine.SpellingError : CssTextDecorationLine.GrammarError;
                reader = probe;
                return true;
            }
            else
            {
                var line = token switch
                {
                    "underline" => CssTextDecorationLine.Underline,
                    "overline" => CssTextDecorationLine.Overline,
                    "line-through" => CssTextDecorationLine.LineThrough,
                    _ => CssTextDecorationLine.None,
                };
                if (line == CssTextDecorationLine.None || lines.HasFlag(line)) return false;
                lines |= line;
            }
            seen = true;
            if (probe.AtEnd) break;
        }
        if (!seen) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadDecorationStyle(ref CssTokenReader reader, out CssTextDecorationStyle style)
    {
        style = CssTextDecorationStyle.Solid;
        var probe = reader;
        if (!probe.TryReadIdent(out var ident)) return false;
        var mapped = ident.ToString().ToLowerInvariant() switch
        {
            "solid" => CssTextDecorationStyle.Solid,
            "double" => CssTextDecorationStyle.Double,
            "dotted" => CssTextDecorationStyle.Dotted,
            "dashed" => CssTextDecorationStyle.Dashed,
            "wavy" => CssTextDecorationStyle.Wavy,
            _ => (CssTextDecorationStyle?)null,
        };
        if (mapped is null) return false;
        style = mapped.Value;
        reader = probe;
        return true;
    }

    private static bool TryReadDecorationColor(ref CssTokenReader reader, out CssCompiledValue? result)
    {
        result = null;
        var probe = reader;
        if (!CssColorParser.TryParseContextual(ref probe, out var color, out var current, out var deferred))
            return false;
        result = deferred is not null
            ? new CssContextualColorValue("text-decoration-color", deferred)
            : current
                ? new CssContextualColorValue("text-decoration-color", "currentColor")
                : new CssImmediateValue(CssTextDecorationProperties.ColorProperty,
                    FreezeIfPossible(new SolidColorBrush(color)));
        reader = probe;
        return true;
    }

    private static bool TryReadDecorationThickness(ref CssTokenReader reader, out CssCompiledValue? result)
    {
        result = null;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                ident.Equals("from-font", StringComparison.OrdinalIgnoreCase))
            {
                result = new CssImmediateValue(CssTextDecorationProperties.ThicknessProperty,
                    ident.Equals("from-font", StringComparison.OrdinalIgnoreCase)
                        ? CssTextDecorationThickness.Font : CssTextDecorationThickness.Auto);
                reader = probe;
                return true;
            }
            return false;
        }
        probe = reader;
        if (!probe.TryReadLength(out var length) ||
            length.Expression is null && length.Value < 0 ||
            length.Unit == CssUnit.None && length.Value != 0) return false;
        if (length.IsAbsolute)
        {
            result = new CssImmediateValue(CssTextDecorationProperties.ThicknessProperty,
                CssTextDecorationThickness.Length(length.ToPxAbsolute()));
        }
        else
        {
            result = new CssDeferredValue("text-decoration-thickness",
                CssTextDecorationProperties.ThicknessProperty,
                (in CssApplyContext context, out object? value) =>
                {
                    var ok = length.TryResolve(context.Lengths, CssPercentBasis.ElementFontSize,
                        out var pixels) && pixels >= 0;
                    value = CssTextDecorationThickness.Length(pixels);
                    return ok;
                });
        }
        reader = probe;
        return true;
    }

    private static CssCompiledValue? TryReadDecorationInset(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (!ident.Equals("auto", StringComparison.OrdinalIgnoreCase) || !probe.AtEnd)
                return null;
            reader = probe;
            return new CssImmediateValue(CssTextDecorationProperties.InsetProperty,
                new CssTextDecorationInset(CssLayoutLength.Px(0), CssLayoutLength.Px(0), true));
        }

        probe = reader;
        if (!TryReadDecorationInsetLength(ref probe, out var start)) return null;
        var end = start;
        if (!probe.AtEnd && !TryReadDecorationInsetLength(ref probe, out end)) return null;
        if (!probe.AtEnd) return null;
        reader = probe;
        return new CssDecorationInsetValue(start, end);
    }

    private static bool TryReadDecorationInsetLength(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (!probe.TryReadLength(out length) ||
            !(length.IsLengthUnit && length.Unit != CssUnit.None ||
              length.Unit == CssUnit.Percent ||
              length.Unit == CssUnit.None && length.Value == 0))
            return false;
        reader = probe;
        return true;
    }

    private sealed class CssDecorationInsetValue(CssLength start, CssLength end) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (!TryCompute(start, context.Lengths, out var startLength) ||
                !TryCompute(end, context.Lengths, out var endLength)) return false;
            sink.Set(CssTextDecorationProperties.InsetProperty,
                new CssTextDecorationInset(startLength, endLength, false));
            return true;
        }

        private static bool TryCompute(CssLength length, in CssLengthContext context,
            out CssLayoutLength result)
        {
            if (length.Expression is { } expression)
                result = CssLayoutLength.Math(expression, context);
            else if (length.Unit == CssUnit.Percent)
                result = CssLayoutLength.Percent(length.Value / 100);
            else if (length.TryResolve(context, CssPercentBasis.NotSupported, out var pixels) &&
                     double.IsFinite(pixels))
                result = CssLayoutLength.Px(pixels);
            else
            {
                result = default;
                return false;
            }
            return true;
        }
    }

    private static bool TryReadUnderlineOffset(ref CssTokenReader reader, out CssCompiledValue? result)
    {
        result = null;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (!ident.Equals("auto", StringComparison.OrdinalIgnoreCase)) return false;
            result = new CssImmediateValue(CssTextDecorationProperties.UnderlineOffsetProperty,
                CssLayoutLength.Auto);
            reader = probe;
            return true;
        }
        probe = reader;
        if (!probe.TryReadLength(out var length) ||
            !(length.IsLengthUnit && length.Unit != CssUnit.None || length.Unit == CssUnit.Percent ||
              length.Unit == CssUnit.None && length.Value == 0)) return false;
        result = new CssUnderlineOffsetValue(length);
        reader = probe;
        return true;
    }

    private static bool TryReadUnderlinePosition(ref CssTokenReader reader, out CssTextUnderlinePosition position)
    {
        position = CssTextUnderlinePosition.Auto;
        var modeSeen = false;
        var sideSeen = false;
        var seen = false;
        while (reader.TryReadIdent(out var ident))
        {
            var token = ident.ToString().ToLowerInvariant();
            if (token == "auto") return !seen && reader.AtEnd;
            switch (token)
            {
                case "from-font" when !modeSeen:
                    position |= CssTextUnderlinePosition.FromFont;
                    modeSeen = true;
                    break;
                case "under" when !modeSeen:
                    position |= CssTextUnderlinePosition.Under;
                    modeSeen = true;
                    break;
                case "left" when !sideSeen:
                    position |= CssTextUnderlinePosition.Left;
                    sideSeen = true;
                    break;
                case "right" when !sideSeen:
                    position |= CssTextUnderlinePosition.Right;
                    sideSeen = true;
                    break;
                default:
                    return false;
            }
            seen = true;
        }
        return seen;
    }

    private sealed class CssUnderlineOffsetValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            CssLayoutLength offset;
            if (length.Expression is { } expression)
                offset = CssLayoutLength.Math(expression, context.Lengths);
            else if (length.Unit == CssUnit.Percent)
                offset = CssLayoutLength.Percent(length.Value / 100);
            else if (length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) &&
                     double.IsFinite(pixels))
                offset = CssLayoutLength.Px(pixels);
            else
                return false;
            sink.Set(CssTextDecorationProperties.UnderlineOffsetProperty, offset);
            return true;
        }
    }
}
