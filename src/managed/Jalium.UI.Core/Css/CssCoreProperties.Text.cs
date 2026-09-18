using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterTextAndFonts()
    {
        RegisterLonghand("font-family", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var family = MapFontFamilyList(reader.Remaining.ToString());
            return family.Length == 0 ? null : new CssNamedValue("font-family", "FontFamily", new FontFamily(family));
        });

        RegisterLonghand("font-size", (ref CssTokenReader reader, CssCompileContext ctx_) => ParseFontSize(ref reader));

        RegisterLonghand("font-weight", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var weight = ParseFontWeight(ref reader);
            return weight is null || !reader.AtEnd ? null : new CssNamedValue("font-weight", "FontWeight", weight);
        });

        RegisterLonghand("font-style", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var style = ParseFontStyle(ref reader);
            return style is null || !reader.AtEnd ? null : new CssNamedValue("font-style", "FontStyle", style);
        });

        RegisterLonghand("font-stretch", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            var stretch = MapFontStretch(ident);
            return stretch is null ? null : new CssNamedValue("font-stretch", "FontStretch", stretch);
        });

        RegisterShorthand("font", ExpandFontShorthand);

        RegisterLonghand("line-height", (ref CssTokenReader reader, CssCompileContext ctx_) => ParseLineHeight(ref reader));

        RegisterLonghand("text-align", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            TextAlignment? alignment = null;
            if (Eq(ident, "left") || Eq(ident, "start")) { alignment = TextAlignment.Left; }
            else if (Eq(ident, "right") || Eq(ident, "end")) { alignment = TextAlignment.Right; }
            else if (Eq(ident, "center")) { alignment = TextAlignment.Center; }
            else if (Eq(ident, "justify")) { alignment = TextAlignment.Justify; }

            if (alignment is null) return null;
            var flow = ident.ToString().ToLowerInvariant() switch
            {
                "start" => CssFlowTextAlignment.Start, "end" => CssFlowTextAlignment.End,
                "right" => CssFlowTextAlignment.Right, "center" => CssFlowTextAlignment.Center,
                "justify" => CssFlowTextAlignment.Justify, _ => CssFlowTextAlignment.Left,
            };
            return new CssFlowTextAlignmentValue(flow);
        });

        RegisterLonghand("text-overflow", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            if (Eq(ident, "ellipsis"))
            {
                return new CssNamedValue("text-overflow", "TextTrimming", TextTrimming.CharacterEllipsis);
            }

            if (Eq(ident, "clip"))
            {
                return new CssNamedValue("text-overflow", "TextTrimming", TextTrimming.None);
            }

            return null;
        });

        RegisterLonghand("white-space", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            if (Eq(ident, "normal"))
            {
                return new CssNamedValue("white-space", "TextWrapping", TextWrapping.Wrap);
            }

            if (Eq(ident, "nowrap"))
            {
                return new CssNamedValue("white-space", "TextWrapping", TextWrapping.NoWrap);
            }

            if (Eq(ident, "pre"))
            {
                ReportWhitespaceApproximation(ident);
                return new CssNamedValue("white-space", "TextWrapping", TextWrapping.NoWrap);
            }

            if (Eq(ident, "pre-wrap") || Eq(ident, "pre-line") || Eq(ident, "break-spaces"))
            {
                ReportWhitespaceApproximation(ident);
                return new CssNamedValue("white-space", "TextWrapping", TextWrapping.Wrap);
            }

            return null;

            static void ReportWhitespaceApproximation(ReadOnlySpan<char> ident)
                => CssDiagnostics.Report(
                    "white-space", CssDiagnosticReason.LossyConversion, null,
                    $"'{ident.ToString()}' whitespace collapsing is not emulated; only wrapping is mapped");
        });
    }

    private static bool Eq(ReadOnlySpan<char> text, string candidate)
        => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);

    internal static string MapFontFamilyList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var parts = raw.Split(',');
        for (var i = 0; i < parts.Length; i++)
        {
            var name = parts[i].Trim().Trim('"', '\'');
            parts[i] = name.ToLowerInvariant() switch
            {
                "sans-serif" or "system-ui" or "ui-sans-serif" => "Segoe UI",
                "serif" or "ui-serif" => "Times New Roman",
                "monospace" or "ui-monospace" => "Consolas",
                _ => name,
            };
        }

        return string.Join(", ", parts.Where(static p => p.Length > 0));
    }

    private static CssCompiledValue? ParseFontSize(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && probe.AtEnd)
        {
            double? absolute = ident.ToString().ToLowerInvariant() switch
            {
                "xx-small" => CssLengthContext.DefaultFontSize * 3 / 5,
                "x-small" => CssLengthContext.DefaultFontSize * 3 / 4,
                "small" => CssLengthContext.DefaultFontSize * 8 / 9,
                "medium" => CssLengthContext.DefaultFontSize,
                "large" => CssLengthContext.DefaultFontSize * 6 / 5,
                "x-large" => CssLengthContext.DefaultFontSize * 3 / 2,
                "xx-large" => CssLengthContext.DefaultFontSize * 2,
                _ => null,
            };
            if (absolute is not null)
            {
                reader = probe;
                return new CssNamedValue("font-size", "FontSize", absolute.Value);
            }

            double? relativeFactor = ident.ToString().ToLowerInvariant() switch
            {
                "larger" => 1.2,
                "smaller" => 1.0 / 1.2,
                _ => null,
            };
            if (relativeFactor is not null)
            {
                reader = probe;
                var factor = relativeFactor.Value;
                return new CssDeferredValue("font-size", "FontSize",
                    (in CssApplyContext ctx, out object? value) =>
                    {
                        value = ctx.Lengths.InheritedFontSize * factor;
                        return true;
                    });
            }

            return null;
        }

        if (!reader.TryReadLength(out var length) || !reader.AtEnd)
        {
            return null;
        }

        return MakeFontSizeValue("font-size", length);
    }

    /// <summary>font-size's em/% resolve against the inherited (parent) size per the CSS rules.</summary>
    private static CssCompiledValue? MakeFontSizeValue(string cssName, CssLength length)
    {
        if (length.IsAbsolute)
        {
            var px = length.ToPxAbsolute();
            return px > 0 ? new CssNamedValue(cssName, "FontSize", px) : null;
        }

        var captured = length;
        return new CssDeferredValue(cssName, "FontSize",
            (in CssApplyContext ctx, out object? value) =>
            {
                var basis = ctx.Lengths.ForFontProperty();
                var ok = captured.TryResolve(basis, CssPercentBasis.ElementFontSize, out var px) && px > 0;
                value = px;
                return ok;
            });
    }

    private static object? ParseFontWeight(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (Eq(ident, "normal")) { reader = probe; return FontWeight.FromOpenTypeWeight(400); }
            if (Eq(ident, "bold")) { reader = probe; return FontWeight.FromOpenTypeWeight(700); }
            if (Eq(ident, "bolder") || Eq(ident, "lighter"))
            {
                CssDiagnostics.Report(
                    "font-weight", CssDiagnosticReason.LossyConversion, null,
                    $"'{ident.ToString()}' needs the parent weight and is not supported; declaration dropped");
            }

            return null;
        }

        if (probe.TryReadNumber(out var value, out var unit) && unit == CssUnit.None &&
            value >= 1 && value <= 1000)
        {
            reader = probe;
            return FontWeight.FromOpenTypeWeight((int)Math.Round(value));
        }

        return null;
    }

    private static object? ParseFontStyle(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var ident))
        {
            return null;
        }

        if (Eq(ident, "normal"))
        {
            return FontStyles.Normal;
        }

        if (Eq(ident, "italic"))
        {
            return FontStyles.Italic;
        }

        if (Eq(ident, "oblique"))
        {
            var probe = reader;
            if (probe.TryReadNumber(out _, out var unit) && unit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn)
            {
                CssDiagnostics.Report(
                    "font-style", CssDiagnosticReason.LossyConversion, null,
                    "oblique angles are not supported; the default oblique slant is used");
                reader = probe;
            }

            return FontStyles.Oblique;
        }

        return null;
    }

    private static object? MapFontStretch(ReadOnlySpan<char> ident)
    {
        if (Eq(ident, "normal")) { return FontStretches.Normal; }
        if (Eq(ident, "ultra-condensed")) { return FontStretches.UltraCondensed; }
        if (Eq(ident, "extra-condensed")) { return FontStretches.ExtraCondensed; }
        if (Eq(ident, "condensed")) { return FontStretches.Condensed; }
        if (Eq(ident, "semi-condensed")) { return FontStretches.SemiCondensed; }
        if (Eq(ident, "semi-expanded")) { return FontStretches.SemiExpanded; }
        if (Eq(ident, "expanded")) { return FontStretches.Expanded; }
        if (Eq(ident, "extra-expanded")) { return FontStretches.ExtraExpanded; }
        if (Eq(ident, "ultra-expanded")) { return FontStretches.UltraExpanded; }
        return null;
    }

    private static CssCompiledValue? ParseLineHeight(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (Eq(ident, "normal") && probe.AtEnd)
            {
                reader = probe;
                return new CssComputedLineHeightValue(default);
            }

            return null;
        }

        probe = reader;
        if (probe.TryReadLength(out var length) && probe.AtEnd && length.Unit != CssUnit.None)
        {
            if (length.Expression is null && length.Value < 0) return null;
            reader = probe;
            return new CssLengthLineHeightValue(length);
        }
        if (!reader.TryReadNumber(out var value, out var unit) || !reader.AtEnd)
        {
            return null;
        }

        return MakeLineHeightValue(value, unit);
    }

    private static CssCompiledValue? MakeLineHeightValue(double value, CssUnit unit)
    {
        if (value < 0)
        {
            return null;
        }

        if (unit == CssUnit.None) return new CssComputedLineHeightValue(new(CssLineHeightKind.Number, value));
        var length = new CssLength(value, unit);
        return length.IsLengthUnit || unit == CssUnit.Percent ? new CssLengthLineHeightValue(length) : null;
    }

    private static bool ExpandFontShorthand(
        ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output)
    {
        object style = FontStyles.Normal;
        object weight = FontWeight.FromOpenTypeWeight(400);
        object stretch = FontStretches.Normal;

        // Prefix components in any order: style / weight / stretch / normal.
        while (true)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident))
            {
                if (Eq(ident, "normal"))
                {
                    reader = probe;
                    continue;
                }

                if (Eq(ident, "italic") || Eq(ident, "oblique"))
                {
                    style = Eq(ident, "italic") ? FontStyles.Italic : FontStyles.Oblique;
                    reader = probe;
                    continue;
                }

                if (Eq(ident, "bold"))
                {
                    weight = FontWeight.FromOpenTypeWeight(700);
                    reader = probe;
                    continue;
                }

                var mappedStretch = MapFontStretch(ident);
                if (mappedStretch is not null && !Eq(ident, "normal"))
                {
                    stretch = mappedStretch;
                    reader = probe;
                    continue;
                }

                break; // A size keyword or the family list begins here.
            }

            probe = reader;
            if (probe.TryReadNumber(out var number, out var numberUnit) && numberUnit == CssUnit.None &&
                number >= 1 && number <= 1000)
            {
                weight = FontWeight.FromOpenTypeWeight((int)Math.Round(number));
                reader = probe;
                continue;
            }

            break;
        }

        var sizeValue = ParseFontSizeComponent(ref reader);
        if (sizeValue is null)
        {
            return false;
        }

        CssCompiledValue lineHeightValue = new CssComputedLineHeightValue(default);
        if (reader.TryReadSlash())
        {
            if (!reader.TryReadNumber(out var lhValue, out var lhUnit))
            {
                return false;
            }

            var parsed = MakeLineHeightValue(lhValue, lhUnit);
            if (parsed is null)
            {
                return false;
            }

            lineHeightValue = parsed;
        }

        var family = MapFontFamilyList(reader.Remaining.ToString());
        if (family.Length == 0)
        {
            return false;
        }

        output.Add(new CssCompiledDeclaration("font-style", new CssNamedValue("font", "FontStyle", style), false));
        output.Add(new CssCompiledDeclaration("font-weight", new CssNamedValue("font", "FontWeight", weight), false));
        output.Add(new CssCompiledDeclaration("font-stretch", new CssNamedValue("font", "FontStretch", stretch), false));
        output.Add(new CssCompiledDeclaration("font-size", sizeValue, false));
        output.Add(new CssCompiledDeclaration("line-height", lineHeightValue, false));
        output.Add(new CssCompiledDeclaration("font-family", new CssNamedValue("font", "FontFamily", new FontFamily(family)), false));
        return true;
    }

    /// <summary>Parses only the size component of the font shorthand (stops before the family list).</summary>
    private static CssCompiledValue? ParseFontSizeComponent(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out _))
        {
            // Size keywords (medium, large…) are idents; ParseFontSize validates them, but it
            // requires AtEnd — carve the single ident out into its own reader first.
            var single = reader;
            if (!single.TryReadIdent(out var ident))
            {
                return null;
            }

            var identReader = new CssTokenReader(ident);
            var parsed = ParseFontSize(ref identReader);
            if (parsed is not null)
            {
                reader = single;
            }

            return parsed;
        }

        if (!reader.TryReadLength(out var length))
        {
            return null;
        }

        return MakeFontSizeValue("font", length);
    }

}
