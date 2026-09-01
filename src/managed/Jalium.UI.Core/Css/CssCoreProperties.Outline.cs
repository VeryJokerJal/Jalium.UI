using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterOutline()
    {
        RegisterLonghand("outline-width", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!TryReadBorderWidth(ref reader, out var length) || !reader.AtEnd)
            {
                return null;
            }

            if (length.Unit == CssUnit.Percent)
            {
                return null;
            }

            if (length.IsAbsolute)
            {
                var px = length.ToPxAbsolute();
                return px < 0 ? null : new CssImmediateValue(FrameworkElement.OutlineThicknessProperty, px);
            }

            var captured = length;
            return new CssDeferredValue("outline-width", FrameworkElement.OutlineThicknessProperty,
                (in CssApplyContext ctx, out object? value) =>
                {
                    var ok = captured.TryResolve(ctx.Lengths, CssPercentBasis.NotSupported, out var px) && px >= 0;
                    value = px;
                    return ok;
                });
        });

        RegisterLonghand("outline-style", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            var style = MapOutlineStyle(ident);
            return style is null
                ? null
                : new CssImmediateValue(FrameworkElement.OutlineStyleProperty, style.Value);
        });

        RegisterLonghand("outline-color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssColorParser.TryParse(ref reader, out var color, out var isCurrentColor))
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident) &&
                    ident.Equals("invert", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
                {
                    CssDiagnostics.Report(
                        "outline-color", CssDiagnosticReason.LossyConversion, null,
                        "'invert' cannot be implemented; the foreground color is used instead");
                    return MakeCurrentColorOutlineBrush("outline-color");
                }

                return null;
            }

            if (!reader.AtEnd)
            {
                return null;
            }

            if (isCurrentColor)
            {
                return MakeCurrentColorOutlineBrush("outline-color");
            }

            return new CssImmediateValue(
                FrameworkElement.OutlineBrushProperty, FreezeIfPossible(new SolidColorBrush(color)));
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
            var sawNone = false;
            var sawAnything = false;

            while (!reader.AtEnd)
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident))
                {
                    if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        sawNone = true;
                        sawAnything = true;
                        reader = probe;
                        continue;
                    }

                    var mappedStyle = MapOutlineStyle(ident);
                    if (mappedStyle is not null)
                    {
                        styleValue = new CssImmediateValue(FrameworkElement.OutlineStyleProperty, mappedStyle.Value);
                        sawAnything = true;
                        reader = probe;
                        continue;
                    }

                    if (ident.Equals("invert", StringComparison.OrdinalIgnoreCase))
                    {
                        CssDiagnostics.Report(
                            "outline", CssDiagnosticReason.LossyConversion, null,
                            "'invert' cannot be implemented; the foreground color is used instead");
                        colorValue = MakeCurrentColorOutlineBrush("outline");
                        sawAnything = true;
                        reader = probe;
                        continue;
                    }
                }

                probe = reader;
                if (TryReadBorderWidth(ref probe, out var width) && width.IsAbsolute)
                {
                    widthValue = new CssImmediateValue(
                        FrameworkElement.OutlineThicknessProperty, Math.Max(0, width.ToPxAbsolute()));
                    sawAnything = true;
                    reader = probe;
                    continue;
                }

                probe = reader;
                if (CssColorParser.TryParse(ref probe, out var color, out var isCurrentColor))
                {
                    colorValue = isCurrentColor
                        ? MakeCurrentColorOutlineBrush("outline")
                        : new CssImmediateValue(
                            FrameworkElement.OutlineBrushProperty, FreezeIfPossible(new SolidColorBrush(color)));
                    sawAnything = true;
                    reader = probe;
                    continue;
                }

                return false;
            }

            if (!sawAnything)
            {
                return false;
            }

            if (sawNone && widthValue is null && colorValue is null)
            {
                // `outline: none` — clear the ring entirely.
                output.Add(new CssCompiledDeclaration("outline-width",
                    new CssImmediateValue(FrameworkElement.OutlineThicknessProperty, 0.0), false));
                output.Add(new CssCompiledDeclaration("outline-color",
                    new CssImmediateValue(FrameworkElement.OutlineBrushProperty, null), false));
                output.Add(new CssCompiledDeclaration("outline-style",
                    new CssImmediateValue(FrameworkElement.OutlineStyleProperty, OutlineStyle.None), false));
                return true;
            }

            // CSS shorthand reset semantics: omitted components take their initial values —
            // width=medium(3), style=none (so `outline: 2px red` shows nothing, per spec),
            // color=currentcolor.
            output.Add(new CssCompiledDeclaration("outline-width",
                widthValue ?? new CssImmediateValue(FrameworkElement.OutlineThicknessProperty, 3.0), false));
            output.Add(new CssCompiledDeclaration("outline-style",
                styleValue ?? new CssImmediateValue(FrameworkElement.OutlineStyleProperty, OutlineStyle.None), false));
            output.Add(new CssCompiledDeclaration("outline-color",
                colorValue ?? MakeCurrentColorOutlineBrush("outline"), false));
            return true;
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

        if (Eq(ident, "auto") || Eq(ident, "double") || Eq(ident, "groove") ||
            Eq(ident, "ridge") || Eq(ident, "inset") || Eq(ident, "outset"))
        {
            CssDiagnostics.Report(
                "outline-style", CssDiagnosticReason.LossyConversion, null,
                $"'{ident.ToString()}' outlines are not supported; rendered as solid");
            return OutlineStyle.Solid;
        }

        return null;
    }

    /// <summary>outline-color: currentcolor — resolves the element's Foreground brush at apply time.</summary>
    private static CssCompiledValue MakeCurrentColorOutlineBrush(string cssName)
        => new CssDeferredValue(cssName, FrameworkElement.OutlineBrushProperty,
            (in CssApplyContext ctx, out object? value) =>
            {
                var dp = CssDependencyPropertyLookup.Find(ctx.Element.GetType(), "Foreground");
                if (dp is not null && ctx.Element.GetValue(dp) is SolidColorBrush solid)
                {
                    value = FreezeIfPossible(new SolidColorBrush(solid.Color));
                    return true;
                }

                value = FreezeIfPossible(new SolidColorBrush(Jalium.UI.Media.Color.FromArgb(0xFF, 0, 0, 0)));
                return true;
            });
}
