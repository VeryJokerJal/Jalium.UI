using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterBackgroundAndBorders()
    {
        RegisterLonghand("background-color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssColorParser.TryParse(ref reader, out var color, out var isCurrentColor) ||
                isCurrentColor || !reader.AtEnd)
            {
                return null;
            }

            var brush = FreezeIfPossible(new SolidColorBrush(color));
            return new CssSlotActionValue(slots => slots.SetBackgroundColor(brush));
        });

        RegisterLonghand("background-image", (ref CssTokenReader reader, CssCompileContext context) =>
            ParseBackgroundImage(ref reader, context));

        RegisterShorthand("background",
            (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
        {
            var sawAnything = false;
            while (!reader.AtEnd)
            {
                var probe = reader;
                if (CssColorParser.TryParse(ref probe, out var color, out var isCurrentColor) && !isCurrentColor)
                {
                    reader = probe;
                    var brush = FreezeIfPossible(new SolidColorBrush(color));
                    output.Add(new CssCompiledDeclaration(
                        "background-color", new CssSlotActionValue(slots => slots.SetBackgroundColor(brush)), false));
                    sawAnything = true;
                    continue;
                }

                probe = reader;
                if (probe.TryReadFunction(out var fn, out var args))
                {
                    if (CssGradientParser.TryParseGradientFunction(fn, ref args, out var gradient))
                    {
                        reader = probe;
                        var frozen = FreezeIfPossible(gradient!);
                        output.Add(new CssCompiledDeclaration(
                            "background-image", new CssSlotActionValue(slots => slots.SetBackgroundImage(frozen)), false));
                        sawAnything = true;
                        continue;
                    }

                    if (fn.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                        TryCreateImageBrush(ref args, context, out var imageBrush))
                    {
                        reader = probe;
                        output.Add(new CssCompiledDeclaration(
                            "background-image", new CssSlotActionValue(slots => slots.SetBackgroundImage(imageBrush)), false));
                        sawAnything = true;
                        continue;
                    }

                    return false;
                }

                probe = reader;
                if (probe.TryReadIdent(out var ident))
                {
                    // repeat/attachment/position keywords are recognized but not representable.
                    CssDiagnostics.Report(
                        "background", CssDiagnosticReason.LossyConversion, null,
                        $"background component '{ident.ToString()}' (repeat/position/attachment) is ignored");
                    reader = probe;
                    continue;
                }

                if (reader.TryReadSlash())
                {
                    continue; // position/size separator
                }

                return false;
            }

            return sawAnything;
        });

        RegisterLonghand("background-size", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (reader.TryReadIdent(out var ident) && reader.AtEnd)
            {
                if (ident.Equals("cover", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssSlotActionValue(static slots => slots.SetBackgroundStretch(Stretch.UniformToFill));
                }

                if (ident.Equals("contain", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssSlotActionValue(static slots => slots.SetBackgroundStretch(Stretch.Uniform));
                }

                if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    return CssNoOpValue.Instance;
                }

                return null;
            }

            // "100% 100%" stretches to fill; anything else is unsupported.
            var probe = reader;
            if (probe.TryReadNumber(out var w, out var wu) && wu == CssUnit.Percent && w == 100 &&
                probe.TryReadNumber(out var h, out var hu) && hu == CssUnit.Percent && h == 100 &&
                probe.AtEnd)
            {
                return new CssSlotActionValue(static slots => slots.SetBackgroundStretch(Stretch.Fill));
            }

            CssDiagnostics.Report(
                "background-size", CssDiagnosticReason.LossyConversion, null,
                "only cover/contain/auto/'100% 100%' are supported; declaration dropped");
            return null;
        });

        RegisterLonghand("color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssColorParser.TryParse(ref reader, out var color, out var isCurrentColor) ||
                isCurrentColor || !reader.AtEnd)
            {
                return null;
            }

            return new CssNamedValue("color", "Foreground", FreezeIfPossible(new SolidColorBrush(color)));
        });

        RegisterBorder();
    }

    private static CssCompiledValue? ParseBackgroundImage(ref CssTokenReader reader, CssCompileContext context)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
            {
                reader = probe;
                return new CssSlotActionValue(static slots => slots.SetBackgroundImage(null));
            }

            return null;
        }

        if (!reader.TryReadFunction(out var fn, out var args))
        {
            return null;
        }

        CssCompiledValue? result = null;
        if (CssGradientParser.TryParseGradientFunction(fn, ref args, out var gradient))
        {
            var frozen = FreezeIfPossible(gradient!);
            result = new CssSlotActionValue(slots => slots.SetBackgroundImage(frozen));
        }
        else if (fn.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                 TryCreateImageBrush(ref args, context, out var imageBrush))
        {
            result = new CssSlotActionValue(slots => slots.SetBackgroundImage(imageBrush));
        }

        if (result is null)
        {
            return null;
        }

        if (!reader.AtEnd)
        {
            if (!reader.TryReadComma())
            {
                return null;
            }

            // Multiple background layers collapse to the first (single Brush target).
            CssDiagnostics.Report(
                "background-image", CssDiagnosticReason.LossyConversion, null,
                "multiple background layers are not supported; only the first layer is used");
            while (reader.TryReadUntilTopLevelComma(out _) && reader.TryReadComma())
            {
            }
        }

        return result;
    }

    private static bool TryCreateImageBrush(ref CssTokenReader args, CssCompileContext context, out object? brush)
    {
        brush = null;
        string url;
        if (args.TryReadString(out var quoted))
        {
            url = quoted;
        }
        else
        {
            url = args.Remaining.ToString().Trim();
        }

        if (url.Length == 0)
        {
            return false;
        }

        try
        {
            var uri = context.BaseUri is not null
                ? new Uri(context.BaseUri, url)
                : new Uri(url, UriKind.RelativeOrAbsolute);
            var source = new BitmapImage(uri);
            brush = new ImageBrush(source) { Stretch = Stretch.Fill };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RegisterBorder()
    {
        RegisterShorthand("border-width",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            Span<CssLength> parts = stackalloc CssLength[4];
            var count = 0;
            while (!reader.AtEnd)
            {
                if (count == 4 || !TryReadBorderWidth(ref reader, out parts[count]))
                {
                    return false;
                }

                count++;
            }

            if (count == 0)
            {
                return false;
            }

            var (top, right, bottom, left) = count switch
            {
                1 => (parts[0], parts[0], parts[0], parts[0]),
                2 => (parts[0], parts[1], parts[0], parts[1]),
                3 => (parts[0], parts[1], parts[2], parts[1]),
                _ => (parts[0], parts[1], parts[2], parts[3]),
            };

            output.Add(new CssCompiledDeclaration("border-top-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 1, top), false));
            output.Add(new CssCompiledDeclaration("border-right-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 2, right), false));
            output.Add(new CssCompiledDeclaration("border-bottom-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 3, bottom), false));
            output.Add(new CssCompiledDeclaration("border-left-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 0, left), false));
            return true;
        });

        RegisterBorderWidthEdge("border-left-width", 0);
        RegisterBorderWidthEdge("border-top-width", 1);
        RegisterBorderWidthEdge("border-right-width", 2);
        RegisterBorderWidthEdge("border-bottom-width", 3);

        RegisterLonghand("border-color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssColorParser.TryParse(ref reader, out var color, out var isCurrentColor) || isCurrentColor)
            {
                return null;
            }

            if (!reader.AtEnd)
            {
                // Per-side colors collapse onto the single BorderBrush.
                CssDiagnostics.Report(
                    "border-color", CssDiagnosticReason.LossyConversion, null,
                    "per-side border colors are not supported; the first color is used for all sides");
                while (CssColorParser.TryParse(ref reader, out _, out _))
                {
                }

                if (!reader.AtEnd)
                {
                    return null;
                }
            }

            return new CssNamedValue("border-color", "BorderBrush", FreezeIfPossible(new SolidColorBrush(color)));
        });

        RegisterLonghand("border-style", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident))
            {
                return null;
            }

            if (!reader.AtEnd)
            {
                CssDiagnostics.Report(
                    "border-style", CssDiagnosticReason.LossyConversion, null,
                    "per-side border styles are not supported; the first style is used");
                while (reader.TryReadIdent(out _))
                {
                }

                if (!reader.AtEnd)
                {
                    return null;
                }
            }

            return MapBorderStyle(ident);
        });

        RegisterShorthand("border",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            CssLength? width = null;
            CssCompiledValue? colorValue = null;
            CssCompiledValue? styleValue = null;
            var styleIsHiddenOrNone = false;
            var sawAnything = false;

            while (!reader.AtEnd)
            {
                var probe = reader;
                if (TryReadBorderWidth(ref probe, out var w))
                {
                    reader = probe;
                    width = w;
                    sawAnything = true;
                    continue;
                }

                probe = reader;
                if (probe.TryReadIdent(out var ident) && TryClassifyBorderStyleKeyword(ident, out var isNone))
                {
                    reader = probe;
                    styleIsHiddenOrNone = isNone;
                    styleValue = MapBorderStyle(ident);
                    sawAnything = true;
                    continue;
                }

                probe = reader;
                if (CssColorParser.TryParse(ref probe, out var color, out var isCurrentColor) && !isCurrentColor)
                {
                    reader = probe;
                    colorValue = new CssNamedValue("border", "BorderBrush", FreezeIfPossible(new SolidColorBrush(color)));
                    sawAnything = true;
                    continue;
                }

                return false;
            }

            if (!sawAnything)
            {
                return false;
            }

            // `border: none` / `border: hidden` alone clears both the brush and the width.
            var effectiveWidth = width ?? new CssLength(
                styleIsHiddenOrNone && colorValue is null ? 0 : 3, CssUnit.Px);
            output.Add(new CssCompiledDeclaration("border-top-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 1, effectiveWidth), false));
            output.Add(new CssCompiledDeclaration("border-right-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 2, effectiveWidth), false));
            output.Add(new CssCompiledDeclaration("border-bottom-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 3, effectiveWidth), false));
            output.Add(new CssCompiledDeclaration("border-left-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 0, effectiveWidth), false));

            // Color first, then style: an omitted style resets to `none` (BorderBrush = null)
            // and must win over the color, matching the CSS initial-value semantics.
            if (colorValue is not null)
            {
                output.Add(new CssCompiledDeclaration("border-color", colorValue, false));
            }

            output.Add(new CssCompiledDeclaration(
                "border-style", styleValue ?? new CssNamedValue("border", "BorderBrush", null), false));
            return true;
        });

        RegisterBorderRadius();
    }

    private static bool TryClassifyBorderStyleKeyword(ReadOnlySpan<char> ident, out bool isNoneOrHidden)
    {
        isNoneOrHidden = ident.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("hidden", StringComparison.OrdinalIgnoreCase);
        return isNoneOrHidden ||
               ident.Equals("solid", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("dashed", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("dotted", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("double", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("groove", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("ridge", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("inset", StringComparison.OrdinalIgnoreCase) ||
               ident.Equals("outset", StringComparison.OrdinalIgnoreCase);
    }

    private static CssCompiledValue? MapBorderStyle(ReadOnlySpan<char> ident)
    {
        if (ident.Equals("solid", StringComparison.OrdinalIgnoreCase))
        {
            return CssNoOpValue.Instance;
        }

        if (ident.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            ident.Equals("hidden", StringComparison.OrdinalIgnoreCase))
        {
            return new CssNamedValue("border-style", "BorderBrush", null);
        }

        if (TryClassifyBorderStyleKeyword(ident, out _))
        {
            CssDiagnostics.Report(
                "border-style", CssDiagnosticReason.LossyConversion, null,
                $"'{ident.ToString()}' borders are not supported; rendered as solid");
            return CssNoOpValue.Instance;
        }

        return null;
    }

    private static void RegisterBorderWidthEdge(string name, int edge)
        => RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!TryReadBorderWidth(ref reader, out var length) || !reader.AtEnd)
            {
                return null;
            }

            return new CssSlotThicknessEdge(CssSlot.BorderWidth, edge, length);
        });

    private static bool TryReadBorderWidth(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            double px;
            if (ident.Equals("thin", StringComparison.OrdinalIgnoreCase)) { px = 1; }
            else if (ident.Equals("medium", StringComparison.OrdinalIgnoreCase)) { px = 3; }
            else if (ident.Equals("thick", StringComparison.OrdinalIgnoreCase)) { px = 5; }
            else
            {
                length = default;
                return false;
            }

            length = new CssLength(px, CssUnit.Px);
            reader = probe;
            return true;
        }

        return reader.TryReadLength(out length);
    }

    private static void RegisterBorderRadius()
    {
        RegisterShorthand("border-radius",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            Span<CssLength> parts = stackalloc CssLength[4];
            var count = 0;
            while (!reader.AtEnd)
            {
                if (reader.TryReadSlash())
                {
                    // Elliptical radii ("a / b"): keep the horizontal set, drop the vertical.
                    CssDiagnostics.Report(
                        "border-radius", CssDiagnosticReason.LossyConversion, null,
                        "elliptical corner radii are not supported; the horizontal radii are used");
                    while (reader.TryReadLength(out _))
                    {
                    }

                    break;
                }

                if (count == 4 || !reader.TryReadLength(out parts[count]))
                {
                    return false;
                }

                count++;
            }

            if (count == 0 || !reader.AtEnd)
            {
                return false;
            }

            // CSS corner order: 1 → all; 2 → (TL/BR, TR/BL); 3 → (TL, TR/BL, BR); 4 → TL TR BR BL.
            var (tl, tr, br, bl) = count switch
            {
                1 => (parts[0], parts[0], parts[0], parts[0]),
                2 => (parts[0], parts[1], parts[0], parts[1]),
                3 => (parts[0], parts[1], parts[2], parts[1]),
                _ => (parts[0], parts[1], parts[2], parts[3]),
            };

            output.Add(new CssCompiledDeclaration("border-top-left-radius", new CssSlotCorner(0, tl), false));
            output.Add(new CssCompiledDeclaration("border-top-right-radius", new CssSlotCorner(1, tr), false));
            output.Add(new CssCompiledDeclaration("border-bottom-right-radius", new CssSlotCorner(2, br), false));
            output.Add(new CssCompiledDeclaration("border-bottom-left-radius", new CssSlotCorner(3, bl), false));
            return true;
        });

        RegisterCornerLonghand("border-top-left-radius", 0);
        RegisterCornerLonghand("border-top-right-radius", 1);
        RegisterCornerLonghand("border-bottom-right-radius", 2);
        RegisterCornerLonghand("border-bottom-left-radius", 3);
    }

    private static void RegisterCornerLonghand(string name, int corner)
        => RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadLength(out var length) || !reader.AtEnd)
            {
                return null;
            }

            return new CssSlotCorner(corner, length);
        });

    private sealed class CssSlotCorner : CssCompiledValue
    {
        private readonly int _corner;
        private readonly CssLength _radius;

        public CssSlotCorner(int corner, CssLength radius)
        {
            _corner = corner;
            _radius = radius;
        }

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            context.Slots.SetCorner(_corner, _radius);
            return true;
        }
    }
}
