using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterOutline()
    {
        RegisterLonghand("outline-width", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            return TryReadBorderWidth(ref reader, out var length) && reader.AtEnd
                ? CompileOutlineWidth(length) : null;
        });

        RegisterLonghand("outline-style", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            var style = Eq(ident, "auto") ? OutlineStyle.Auto : MapOutlineStyle(ident);
            return style is null
                ? null
                : new CssImmediateValue(FrameworkElement.OutlineStyleProperty, style.Value);
        });

        RegisterLonghand("outline-color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident) && Eq(ident, "auto") && probe.AtEnd)
            {
                reader = probe;
                return new CssOutlineAutoColorValue();
            }

            if (!CssColorParser.TryParseContextual(ref reader, out var color, out var isCurrentColor, out var deferred))
                return null;

            if (!reader.AtEnd)
            {
                return null;
            }

            if (deferred is not null)
            {
                return new CssOutlineExplicitColorValue(new CssContextualColorValue("outline-color", deferred));
            }

            if (isCurrentColor)
            {
                return new CssOutlineExplicitColorValue(MakeCurrentColorOutlineBrush("outline-color"));
            }

            return new CssOutlineExplicitColorValue(new CssImmediateValue(
                FrameworkElement.OutlineBrushProperty, FreezeIfPossible(new SolidColorBrush(color))));
        });

        RegisterLonghand("outline-offset", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadLength(out var length) || !reader.AtEnd || length.Unit == CssUnit.Percent)
            {
                return null;
            }

            if (length.IsAbsolute)
            {
                return new CssImmediateValue(FrameworkElement.OutlineOffsetProperty, length.ToPxAbsolute());
            }

            var captured = length;
            return new CssDeferredValue("outline-offset", FrameworkElement.OutlineOffsetProperty,
                (in CssApplyContext ctx, out object? value) =>
                {
                    var ok = captured.TryResolve(ctx.Lengths, CssPercentBasis.NotSupported, out var px);
                    value = px;
                    return ok;
                });
        });

        RegisterShorthand("outline",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            CssCompiledValue? widthValue = null;
            CssCompiledValue? styleValue = null;
            CssCompiledValue? colorValue = null;
            var autoCount = 0;

            while (!reader.AtEnd)
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident))
                {
                    if (Eq(ident, "auto"))
                    {
                        if (++autoCount > 2) return false;
                        reader = probe;
                        continue;
                    }

                    if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        if (styleValue is not null) return false;
                        styleValue = new CssImmediateValue(FrameworkElement.OutlineStyleProperty, OutlineStyle.None);
                        reader = probe;
                        continue;
                    }

                    var mappedStyle = MapOutlineStyle(ident);
                    if (mappedStyle is not null)
                    {
                        if (styleValue is not null) return false;
                        styleValue = new CssImmediateValue(FrameworkElement.OutlineStyleProperty, mappedStyle.Value);
                        reader = probe;
                        continue;
                    }
                }

                probe = reader;
                if (TryReadBorderWidth(ref probe, out var width))
                {
                    if (widthValue is not null) return false;
                    widthValue = CompileOutlineWidth(width);
                    if (widthValue is null) return false;
                    reader = probe;
                    continue;
                }

                probe = reader;
                if (CssColorParser.TryParseContextual(ref probe, out var color, out var isCurrentColor, out var deferred))
                {
                    if (colorValue is not null) return false;
                    var parsedColor = deferred is not null ? new CssContextualColorValue("outline-color", deferred) :
                        isCurrentColor ? MakeCurrentColorOutlineBrush("outline") :
                        new CssImmediateValue(
                            FrameworkElement.OutlineBrushProperty, FreezeIfPossible(new SolidColorBrush(color)));
                    colorValue = new CssOutlineExplicitColorValue(parsedColor);
                    reader = probe;
                    continue;
                }

                return false;
            }

            if (autoCount > 0)
            {
                var occupied = (styleValue is not null ? 1 : 0) + (colorValue is not null ? 1 : 0);
                if (autoCount + occupied > 2) return false;
                if (styleValue is null) styleValue = new CssImmediateValue(
                    FrameworkElement.OutlineStyleProperty, OutlineStyle.Auto);
                if (colorValue is null) colorValue = new CssOutlineAutoColorValue();
            }

            if (widthValue is null && styleValue is null && colorValue is null)
            {
                return false;
            }

            // CSS shorthand reset semantics: omitted components take their initial values —
            // width=medium(3), style=none (so `outline: 2px red` shows nothing, per spec),
            // color=auto (which uses currentColor unless style is also auto).
            output.Add(new CssCompiledDeclaration("outline-width",
                widthValue ?? new CssImmediateValue(FrameworkElement.OutlineThicknessProperty, 3.0), false));
            output.Add(new CssCompiledDeclaration("outline-style",
                styleValue ?? new CssImmediateValue(FrameworkElement.OutlineStyleProperty, OutlineStyle.None), false));
            output.Add(new CssCompiledDeclaration("outline-color",
                colorValue ?? new CssOutlineAutoColorValue(), false));
            return true;
        });
    }

    private static CssCompiledValue? CompileOutlineWidth(CssLength length)
    {
        if (length.UsesPercent) return null;
        if (length.IsAbsolute)
        {
            var px = length.ToPxAbsolute();
            return px < 0 ? null : new CssImmediateValue(FrameworkElement.OutlineThicknessProperty, px);
        }

        return new CssDeferredValue("outline-width", FrameworkElement.OutlineThicknessProperty,
            (in CssApplyContext context, out object? value) =>
            {
                var valid = length.TryResolve(context.Lengths, CssPercentBasis.NotSupported,
                    out var pixels) && pixels >= 0;
                value = pixels;
                return valid;
            });
    }

    private static OutlineStyle? MapOutlineStyle(ReadOnlySpan<char> ident)
    {
        if (Eq(ident, "solid"))
        {
            return OutlineStyle.Solid;
        }

        if (Eq(ident, "dashed"))
        {
            return OutlineStyle.Dashed;
        }

        if (Eq(ident, "dotted"))
        {
            return OutlineStyle.Dotted;
        }

        if (Eq(ident, "double"))
        {
            return OutlineStyle.Double;
        }

        if (Eq(ident, "groove")) return OutlineStyle.Groove;
        if (Eq(ident, "ridge")) return OutlineStyle.Ridge;
        if (Eq(ident, "inset")) return OutlineStyle.Inset;
        if (Eq(ident, "outset")) return OutlineStyle.Outset;

        return null;
    }

    private sealed class CssOutlineAutoColorValue : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
            => CssOutlineAutoColorProperties.Apply(context, sink);
    }

    private sealed class CssOutlineExplicitColorValue(CssCompiledValue color) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (!color.TryApply(context, sink)) return false;
            sink.Set(CssOutlineAutoColorProperties.AutoProperty, false);
            return true;
        }
    }

    /// <summary>outline-color: currentcolor — resolves the element's Foreground brush at apply time.</summary>
    private static CssCompiledValue MakeCurrentColorOutlineBrush(string cssName)
    {
        var symbolic = new CssContextualColorValue("outline-color", "currentColor");
        return new CssDeferredValue(cssName, FrameworkElement.OutlineBrushProperty,
            (in CssApplyContext ctx, out object? value) =>
            {
                value = ctx.CurrentColor;
                CssContextualColorValue.Remember(ctx.Element, "outline-color", symbolic);
                return true;
            });
    }
}
