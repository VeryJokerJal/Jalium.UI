using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterTextAndFonts()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "direction",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = FrameworkElement.FlowDirectionProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var direction = ident.ToString().ToLowerInvariant() switch
                {
                    "ltr" => FlowDirection.LeftToRight,
                    "rtl" => FlowDirection.RightToLeft,
                    _ => (FlowDirection?)null,
                };
                return direction is { } value
                    ? new CssImmediateValue(FrameworkElement.FlowDirectionProperty, value) : null;
            },
        });

        RegisterLonghand("font-family", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            return CssFontFamilyValue.TryRead(ref reader, out var family)
                ? new CssFontFamilyValue(family) : null;
        });

        RegisterLonghand("font-size", (ref CssTokenReader reader, CssCompileContext ctx_) => ParseFontSize(ref reader));

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "math-depth", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssMathProperties.DepthProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => CssMathProperties.ParseDepth(ref reader),
        });
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "math-style", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssMathProperties.StyleProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => CssMathProperties.ParseStyle(ref reader),
        });

        RegisterLonghand("font-weight", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var weight = ParseFontWeight(ref reader);
            return weight is null || !reader.AtEnd ? null : weight;
        });

        RegisterLonghand("font-style", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssFontStyleValue.TryRead(ref reader, out var style) || !reader.AtEnd) return null;
            ReportObliqueAngleFallback(style);
            return new CssFontStyleValue(style);
        });

        RegisterLonghand("font-stretch", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            return CssFontStretchValue.TryRead(ref reader, out var percentage)
                ? new CssFontStretchValue(percentage) : null;
        });

        RegisterShorthand("font", ExpandFontShorthand);

        RegisterLonghand("line-height", (ref CssTokenReader reader, CssCompileContext ctx_) => ParseLineHeight(ref reader));

        RegisterShorthand("text-align", static (ref CssTokenReader reader,
            CssCompileContext _, List<CssCompiledDeclaration> output) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return false;
            var keyword = ident.ToString().ToLowerInvariant();
            var all = keyword == "justify-all"
                ? CssFlowTextAlignment.Justify : ParseTextAlignment(keyword);
            if (all is null) return false;
            var last = keyword switch
            {
                "justify-all" => CssFlowLastTextAlignment.Justify,
                "match-parent" => CssFlowLastTextAlignment.MatchParent,
                _ => CssFlowLastTextAlignment.Auto,
            };
            output.Add(new CssCompiledDeclaration("text-align-all", new CssFlowTextAlignmentValue(all.Value), false));
            output.Add(new CssCompiledDeclaration("text-align-last", new CssFlowLastTextAlignmentValue(last), false));
            return true;
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-align-all", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextAlignProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var alignment = ParseTextAlignment(ident.ToString().ToLowerInvariant());
                return alignment is { } value ? new CssFlowTextAlignmentValue(value) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-align-last", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextAlignLastProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var alignment = ParseTextAlignLast(ident.ToString().ToLowerInvariant());
                return alignment is { } value ? new CssFlowLastTextAlignmentValue(value) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-justify", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextJustifyProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var method = ident.ToString().ToLowerInvariant() switch
                {
                    "auto" => CssTextJustification.Auto,
                    "none" => CssTextJustification.None,
                    "inter-word" => CssTextJustification.InterWord,
                    "inter-character" or "distribute" => CssTextJustification.InterCharacter,
                    _ => (CssTextJustification?)null,
                };
                return method is { } value
                    ? new CssImmediateValue(CssFlowProperties.TextJustifyProperty, value) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-group-align", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextGroupAlignProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var alignment = ident.ToString().ToLowerInvariant() switch
                {
                    "none" => CssTextGroupAlignment.None,
                    "start" => CssTextGroupAlignment.Start,
                    "end" => CssTextGroupAlignment.End,
                    "left" => CssTextGroupAlignment.Left,
                    "right" => CssTextGroupAlignment.Right,
                    "center" => CssTextGroupAlignment.Center,
                    _ => (CssTextGroupAlignment?)null,
                };
                return alignment is { } value
                    ? new CssImmediateValue(CssFlowProperties.TextGroupAlignProperty, value) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "line-padding", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.LinePaddingProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadLength(out var length) || !reader.AtEnd || length.UsesPercent ||
                    length.Expression is null && length.Unit == CssUnit.None && length.Value != 0)
                    return null;
                return new CssLinePaddingValue(length);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-indent", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextIndentProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                CssLength? length = null;
                var hanging = false;
                var eachLine = false;
                while (!reader.AtEnd)
                {
                    var probe = reader;
                    if (length is null && probe.TryReadLength(out var parsed))
                    {
                        if (parsed.Expression is null && parsed.Unit == CssUnit.None && parsed.Value != 0)
                            return null;
                        length = parsed;
                        reader = probe;
                        continue;
                    }

                    if (!reader.TryReadIdent(out var ident)) return null;
                    if (Eq(ident, "hanging") && !hanging) hanging = true;
                    else if (Eq(ident, "each-line") && !eachLine) eachLine = true;
                    else return null;
                }
                return length is { } value ? new CssTextIndentValue(value, hanging, eachLine) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overflow-wrap", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.OverflowWrapProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var value = ident.ToString().ToLowerInvariant() switch
                {
                    "normal" => CssOverflowWrap.Normal,
                    "anywhere" => CssOverflowWrap.Anywhere,
                    "break-word" => CssOverflowWrap.BreakWord,
                    _ => (CssOverflowWrap?)null,
                };
                return value is { } wrap
                    ? new CssImmediateValue(CssFlowProperties.OverflowWrapProperty, wrap) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "word-break", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.WordBreakProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var value = ident.ToString().ToLowerInvariant() switch
                {
                    "normal" => CssWordBreak.Normal,
                    "keep-all" => CssWordBreak.KeepAll,
                    "break-all" => CssWordBreak.BreakAll,
                    "break-word" => CssWordBreak.BreakWord,
                    _ => (CssWordBreak?)null,
                };
                return value is { } wordBreak
                    ? new CssImmediateValue(CssFlowProperties.WordBreakProperty, wordBreak) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "line-break", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.LineBreakProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var value = ident.ToString().ToLowerInvariant() switch
                {
                    "auto" => CssLineBreak.Auto,
                    "loose" => CssLineBreak.Loose,
                    "normal" => CssLineBreak.Normal,
                    "strict" => CssLineBreak.Strict,
                    "anywhere" => CssLineBreak.Anywhere,
                    _ => (CssLineBreak?)null,
                };
                return value is { } lineBreak
                    ? new CssImmediateValue(CssFlowProperties.LineBreakProperty, lineBreak) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "word-spacing", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.WordSpacingProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident) && probe.AtEnd)
                    return ident.Equals("normal", StringComparison.OrdinalIgnoreCase)
                        ? new CssImmediateValue(CssFlowProperties.WordSpacingProperty,
                            CssLayoutLength.Px(0)) : null;
                if (!reader.TryReadLength(out var length) || !reader.AtEnd ||
                    length.Expression is null && length.Unit == CssUnit.None && length.Value != 0)
                    return null;
                return new CssWordSpacingValue(length);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "letter-spacing", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.LetterSpacingProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident) && probe.AtEnd)
                    return ident.Equals("normal", StringComparison.OrdinalIgnoreCase)
                        ? new CssImmediateValue(CssFlowProperties.LetterSpacingProperty, 0.0) : null;
                if (!reader.TryReadLength(out var length) || !reader.AtEnd || length.UsesPercent ||
                    length.Expression is null && length.Unit == CssUnit.None && length.Value != 0)
                    return null;
                return new CssLetterSpacingValue(length);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-autospace", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextAutospaceProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var first)) return null;
                var keyword = first.ToString().ToLowerInvariant();
                if (reader.AtEnd)
                {
                    var single = keyword switch
                    {
                        "normal" or "auto" => CssTextAutospace.Normal,
                        "no-autospace" or "insert" => CssTextAutospace.None,
                        "replace" => CssTextAutospace.Replace,
                        "ideograph-alpha" => CssTextAutospace.IdeographAlpha,
                        "ideograph-numeric" => CssTextAutospace.IdeographNumeric,
                        "punctuation" => CssTextAutospace.Punctuation,
                        _ => (CssTextAutospace?)null,
                    };
                    return single is { } value
                        ? new CssImmediateValue(CssFlowProperties.TextAutospaceProperty, value)
                        : null;
                }
                var mode = CssTextAutospace.None;
                var insert = false;
                var replace = false;
                do
                {
                    switch (keyword)
                    {
                        case "ideograph-alpha" when (mode & CssTextAutospace.IdeographAlpha) == 0:
                            mode |= CssTextAutospace.IdeographAlpha;
                            break;
                        case "ideograph-numeric" when (mode & CssTextAutospace.IdeographNumeric) == 0:
                            mode |= CssTextAutospace.IdeographNumeric;
                            break;
                        case "punctuation" when (mode & CssTextAutospace.Punctuation) == 0:
                            mode |= CssTextAutospace.Punctuation;
                            break;
                        case "insert" when !insert && !replace:
                            insert = true;
                            break;
                        case "replace" when !replace && !insert:
                            replace = true;
                            mode |= CssTextAutospace.Replace;
                            break;
                        default:
                            return null;
                    }
                    if (!reader.TryReadIdent(out var next)) break;
                    keyword = next.ToString().ToLowerInvariant();
                } while (true);
                return reader.AtEnd
                    ? new CssImmediateValue(CssFlowProperties.TextAutospaceProperty, mode)
                    : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hanging-punctuation", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HangingPunctuationProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var first)) return null;
                var keyword = first.ToString().ToLowerInvariant();
                if (keyword == "none")
                    return reader.AtEnd
                        ? new CssImmediateValue(CssFlowProperties.HangingPunctuationProperty,
                            CssHangingPunctuation.None) : null;
                var mode = CssHangingPunctuation.None;
                do
                {
                    var flag = keyword switch
                    {
                        "first" => CssHangingPunctuation.First,
                        "force-end" => CssHangingPunctuation.ForceEnd,
                        "allow-end" => CssHangingPunctuation.AllowEnd,
                        "last" => CssHangingPunctuation.Last,
                        _ => CssHangingPunctuation.None,
                    };
                    if (flag == CssHangingPunctuation.None || (mode & flag) != 0 ||
                        flag is CssHangingPunctuation.ForceEnd or CssHangingPunctuation.AllowEnd &&
                        (mode & (CssHangingPunctuation.ForceEnd |
                            CssHangingPunctuation.AllowEnd)) != 0)
                        return null;
                    mode |= flag;
                    if (!reader.TryReadIdent(out var next)) break;
                    keyword = next.ToString().ToLowerInvariant();
                } while (true);
                return reader.AtEnd
                    ? new CssImmediateValue(CssFlowProperties.HangingPunctuationProperty, mode)
                    : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "tab-size", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TabSizeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadNumber(out var number, out var unit) && probe.AtEnd &&
                    unit == CssUnit.None && double.IsFinite(number) && number >= 0)
                    return new CssImmediateValue(CssFlowProperties.TabSizeProperty,
                        new CssTabSize(number, false));

                if (!reader.TryReadLength(out var length) || !reader.AtEnd || length.UsesPercent ||
                    length.Expression is null && (length.Unit == CssUnit.None || length.Value < 0))
                    return null;
                return new CssTabSizeValue(length);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hyphens", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HyphensProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var value = ident.ToString().ToLowerInvariant() switch
                {
                    "none" => CssHyphens.None,
                    "manual" => CssHyphens.Manual,
                    "auto" => CssHyphens.Auto,
                    _ => (CssHyphens?)null,
                };
                return value is { } hyphens
                    ? new CssImmediateValue(CssFlowProperties.HyphensProperty, hyphens)
                    : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hyphenate-character", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HyphenateCharacterProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident) && probe.AtEnd &&
                    ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(CssFlowProperties.HyphenateCharacterProperty,
                        default(CssHyphenateCharacter));
                if (!reader.TryReadString(out var value) || !reader.AtEnd) return null;
                return new CssImmediateValue(CssFlowProperties.HyphenateCharacterProperty,
                    new CssHyphenateCharacter(value));
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hyphenate-limit-zone", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HyphenateLimitZoneProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadLength(out var length) || !reader.AtEnd ||
                    length.Expression is null && length.Unit == CssUnit.None && length.Value != 0)
                    return null;
                return new CssHyphenateLimitZoneValue(length);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hyphenate-limit-last", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HyphenateLimitLastProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var value = ident.ToString().ToLowerInvariant() switch
                {
                    "none" => CssHyphenateLimitLast.None,
                    "always" => CssHyphenateLimitLast.Always,
                    "column" => CssHyphenateLimitLast.Column,
                    "page" => CssHyphenateLimitLast.Page,
                    "spread" => CssHyphenateLimitLast.Spread,
                    _ => (CssHyphenateLimitLast?)null,
                };
                return value is { } limit
                    ? new CssImmediateValue(CssFlowProperties.HyphenateLimitLastProperty, limit)
                    : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hyphenate-limit-lines", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HyphenateLimitLinesProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident) && probe.AtEnd &&
                    ident.Equals("no-limit", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(CssFlowProperties.HyphenateLimitLinesProperty,
                        default(CssHyphenateLimitLines));
                if (!reader.TryReadInteger(out var number, minimum: 0) || !reader.AtEnd)
                    return null;
                return new CssImmediateValue(CssFlowProperties.HyphenateLimitLinesProperty,
                    new CssHyphenateLimitLines(number));
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "hyphenate-limit-chars", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.HyphenateLimitCharsProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var values = new int?[3];
                var count = 0;
                while (!reader.AtEnd && count < values.Length)
                {
                    var probe = reader;
                    if (probe.TryReadIdent(out var ident))
                    {
                        if (!ident.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
                        reader = probe;
                    }
                    else if (reader.TryReadInteger(out var number, minimum: 0))
                        values[count] = number;
                    else return null;
                    count++;
                }
                if (count == 0 || !reader.AtEnd) return null;
                var before = count >= 2 ? values[1] : null;
                return new CssImmediateValue(CssFlowProperties.HyphenateLimitCharsProperty,
                    new CssHyphenateLimitChars(values[0], before,
                        count == 3 ? values[2] : before));
            },
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

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "word-space-transform", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.WordSpaceTransformProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident)) return null;
                if (Eq(ident, "none"))
                    return reader.AtEnd
                        ? new CssImmediateValue(CssFlowProperties.WordSpaceTransformProperty,
                            CssWordSpaceTransform.None) : null;

                var mode = CssWordSpaceTransform.None;
                do
                {
                    var part = ident.ToString().ToLowerInvariant() switch
                    {
                        "space" => CssWordSpaceTransform.Space,
                        "ideographic-space" => CssWordSpaceTransform.IdeographicSpace,
                        "auto-phrase" => CssWordSpaceTransform.AutoPhrase,
                        _ => CssWordSpaceTransform.None,
                    };
                    if (part == CssWordSpaceTransform.None ||
                        (mode & part) != 0 ||
                        (part is CssWordSpaceTransform.Space or CssWordSpaceTransform.IdeographicSpace) &&
                        (mode & (CssWordSpaceTransform.Space |
                                 CssWordSpaceTransform.IdeographicSpace)) != 0)
                        return null;
                    mode |= part;
                    if (reader.AtEnd)
                        return (mode & (CssWordSpaceTransform.Space |
                                        CssWordSpaceTransform.IdeographicSpace)) != 0
                            ? new CssImmediateValue(CssFlowProperties.WordSpaceTransformProperty,
                                mode) : null;
                } while (reader.TryReadIdent(out ident));
                return null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-transform", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextTransformProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident)) return null;
                if (Eq(ident, "none"))
                    return reader.AtEnd
                        ? new CssImmediateValue(CssFlowProperties.TextTransformProperty,
                            CssTextTransformMode.None) : null;

                var mode = CssTextTransformMode.None;
                do
                {
                    var keyword = ident.ToString().ToLowerInvariant();
                    var part = keyword switch
                    {
                        "capitalize" => CssTextTransformMode.Capitalize,
                        "uppercase" => CssTextTransformMode.Uppercase,
                        "lowercase" => CssTextTransformMode.Lowercase,
                        "full-width" => CssTextTransformMode.FullWidth,
                        "full-size-kana" => CssTextTransformMode.FullSizeKana,
                        _ => CssTextTransformMode.None,
                    };
                    if (part == CssTextTransformMode.None ||
                        ((part & CssTextTransformMode.CaseMask) != CssTextTransformMode.None &&
                         (mode & CssTextTransformMode.CaseMask) != CssTextTransformMode.None) ||
                        (mode & part) != CssTextTransformMode.None)
                        return null;
                    mode |= part;
                    if (reader.AtEnd)
                        return new CssImmediateValue(CssFlowProperties.TextTransformProperty, mode);
                } while (reader.TryReadIdent(out ident));
                return null;
            },
        });

        RegisterShorthand("white-space", ExpandWhiteSpaceShorthand);
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "white-space-collapse", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.WhiteSpaceProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                return ParseWhiteSpaceCollapse(ident.ToString().ToLowerInvariant()) is { } collapse
                    ? new CssImmediateValue(CssFlowProperties.WhiteSpaceProperty, collapse) : null;
            },
        });
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-wrap-mode", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextWrapModeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                return ParseTextWrapMode(ident.ToString().ToLowerInvariant()) is { } mode
                    ? new CssTextWrapModeValue(mode) : null;
            },
        });
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-wrap-style", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.TextWrapStyleProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                return ParseTextWrapStyle(ident.ToString().ToLowerInvariant()) is { } style
                    ? new CssImmediateValue(CssFlowProperties.TextWrapStyleProperty, style) : null;
            },
        });
        RegisterShorthand("text-wrap", ExpandTextWrapShorthand);
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "white-space-trim", Kind = CssPropertyKind.Longhand,
            StorageProperty = CssFlowProperties.WhiteSpaceTrimProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseWhiteSpaceTrim(ref reader, out var trim)
                    ? new CssImmediateValue(CssFlowProperties.WhiteSpaceTrimProperty, trim) : null,
        });
    }

    private static bool ExpandWhiteSpaceShorthand(ref CssTokenReader reader,
        CssCompileContext _, List<CssCompiledDeclaration> output)
    {
        var tokens = new List<string>(5);
        while (reader.TryReadIdent(out var ident))
        {
            tokens.Add(ident.ToString().ToLowerInvariant());
            if (tokens.Count > 5) return false;
        }
        if (!reader.AtEnd || tokens.Count == 0) return false;

        CssWhiteSpaceCollapse? collapse = null;
        CssTextWrapMode? wrap = null;
        var trim = CssWhiteSpaceTrim.None;
        var trimSpecified = false;
        if (tokens.Count == 1 && ParseLegacyWhiteSpace(tokens[0]) is { } legacy)
        {
            (collapse, wrap) = legacy;
        }
        else
        {
            foreach (var token in tokens)
            {
                if (ParseWhiteSpaceCollapse(token) is { } parsedCollapse)
                {
                    if (collapse is not null) return false;
                    collapse = parsedCollapse;
                }
                else if (ParseTextWrapMode(token) is { } parsedWrap)
                {
                    if (wrap is not null) return false;
                    wrap = parsedWrap;
                }
                else if (token == "none")
                {
                    if (trimSpecified) return false;
                    trimSpecified = true;
                }
                else if (ParseWhiteSpaceTrimToken(token) is { } parsedTrim)
                {
                    if ((trimSpecified && trim == CssWhiteSpaceTrim.None) ||
                        (trim & parsedTrim) != 0) return false;
                    trim |= parsedTrim;
                    trimSpecified = true;
                }
                else return false;
            }
        }
        if (collapse is null && wrap is null && !trimSpecified) return false;
        output.Add(new CssCompiledDeclaration("white-space-collapse",
            new CssImmediateValue(CssFlowProperties.WhiteSpaceProperty,
                collapse ?? CssWhiteSpaceCollapse.Collapse), false));
        output.Add(new CssCompiledDeclaration("text-wrap-mode",
            new CssTextWrapModeValue(wrap ?? CssTextWrapMode.Wrap), false));
        output.Add(new CssCompiledDeclaration("white-space-trim",
            new CssImmediateValue(CssFlowProperties.WhiteSpaceTrimProperty, trim), false));
        return true;
    }

    private static (CssWhiteSpaceCollapse Collapse, CssTextWrapMode Wrap)? ParseLegacyWhiteSpace(string keyword)
        => keyword switch
        {
            "normal" => (CssWhiteSpaceCollapse.Collapse, CssTextWrapMode.Wrap),
            "pre" => (CssWhiteSpaceCollapse.Preserve, CssTextWrapMode.NoWrap),
            "pre-wrap" => (CssWhiteSpaceCollapse.Preserve, CssTextWrapMode.Wrap),
            "pre-line" => (CssWhiteSpaceCollapse.PreserveBreaks, CssTextWrapMode.Wrap),
            _ => null,
        };

    private static bool TryParseWhiteSpaceTrim(ref CssTokenReader reader, out CssWhiteSpaceTrim trim)
    {
        trim = CssWhiteSpaceTrim.None;
        if (!reader.TryReadIdent(out var first)) return false;
        var keyword = first.ToString().ToLowerInvariant();
        if (keyword == "none") return reader.AtEnd;
        do
        {
            if (ParseWhiteSpaceTrimToken(keyword) is not { } part || (trim & part) != 0)
                return false;
            trim |= part;
            if (reader.AtEnd) return true;
            if (!reader.TryReadIdent(out var next)) return false;
            keyword = next.ToString().ToLowerInvariant();
        } while (true);
    }

    private static CssWhiteSpaceTrim? ParseWhiteSpaceTrimToken(string keyword) => keyword switch
    {
        "discard-before" => CssWhiteSpaceTrim.DiscardBefore,
        "discard-after" => CssWhiteSpaceTrim.DiscardAfter,
        "discard-inner" => CssWhiteSpaceTrim.DiscardInner,
        _ => null,
    };

    private static CssWhiteSpaceCollapse? ParseWhiteSpaceCollapse(string keyword) => keyword switch
    {
        "collapse" => CssWhiteSpaceCollapse.Collapse,
        "discard" => CssWhiteSpaceCollapse.Discard,
        "preserve" => CssWhiteSpaceCollapse.Preserve,
        "preserve-breaks" => CssWhiteSpaceCollapse.PreserveBreaks,
        "preserve-spaces" => CssWhiteSpaceCollapse.PreserveSpaces,
        "break-spaces" => CssWhiteSpaceCollapse.BreakSpaces,
        _ => null,
    };

    private static CssTextWrapMode? ParseTextWrapMode(string keyword) => keyword switch
    {
        "wrap" => CssTextWrapMode.Wrap,
        "nowrap" => CssTextWrapMode.NoWrap,
        _ => null,
    };

    private static CssTextWrapStyle? ParseTextWrapStyle(string keyword) => keyword switch
    {
        "auto" => CssTextWrapStyle.Auto,
        "balance" => CssTextWrapStyle.Balance,
        "stable" => CssTextWrapStyle.Stable,
        "pretty" => CssTextWrapStyle.Pretty,
        "avoid-short-last-line" => CssTextWrapStyle.AvoidShortLastLine,
        _ => null,
    };

    private static bool ExpandTextWrapShorthand(ref CssTokenReader reader,
        CssCompileContext _, List<CssCompiledDeclaration> output)
    {
        CssTextWrapMode? mode = null;
        CssTextWrapStyle? style = null;
        var count = 0;
        while (reader.TryReadIdent(out var ident))
        {
            if (++count > 2) return false;
            var keyword = ident.ToString().ToLowerInvariant();
            if (ParseTextWrapMode(keyword) is { } parsedMode)
            {
                if (mode is not null) return false;
                mode = parsedMode;
            }
            else if (ParseTextWrapStyle(keyword) is { } parsedStyle)
            {
                if (style is not null) return false;
                style = parsedStyle;
            }
            else return false;
        }
        if (!reader.AtEnd || count == 0) return false;
        output.Add(new CssCompiledDeclaration("text-wrap-mode",
            new CssTextWrapModeValue(mode ?? CssTextWrapMode.Wrap), false));
        output.Add(new CssCompiledDeclaration("text-wrap-style",
            new CssImmediateValue(CssFlowProperties.TextWrapStyleProperty,
                style ?? CssTextWrapStyle.Auto), false));
        return true;
    }

    private static bool Eq(ReadOnlySpan<char> text, string candidate)
        => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);

    private static CssFlowTextAlignment? ParseTextAlignment(string keyword) => keyword switch
    {
        "start" => CssFlowTextAlignment.Start,
        "end" => CssFlowTextAlignment.End,
        "left" => CssFlowTextAlignment.Left,
        "right" => CssFlowTextAlignment.Right,
        "center" => CssFlowTextAlignment.Center,
        "justify" => CssFlowTextAlignment.Justify,
        "match-parent" => CssFlowTextAlignment.MatchParent,
        _ => null,
    };

    private static CssFlowLastTextAlignment? ParseTextAlignLast(string keyword) => keyword switch
    {
        "auto" => CssFlowLastTextAlignment.Auto,
        "start" => CssFlowLastTextAlignment.Start,
        "end" => CssFlowLastTextAlignment.End,
        "left" => CssFlowLastTextAlignment.Left,
        "right" => CssFlowLastTextAlignment.Right,
        "center" => CssFlowLastTextAlignment.Center,
        "justify" => CssFlowLastTextAlignment.Justify,
        "match-parent" => CssFlowLastTextAlignment.MatchParent,
        _ => null,
    };

    private sealed class CssTextIndentValue(CssLength length, bool hanging, bool eachLine) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            CssLayoutLength used;
            if (length.Expression is { } expression)
                used = CssLayoutLength.Math(expression, context.Lengths);
            else if (length.Unit == CssUnit.Percent)
                used = CssLayoutLength.Percent(length.Value / 100);
            else if (length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels))
                used = CssLayoutLength.Px(pixels);
            else
                return false;
            sink.Set(CssFlowProperties.TextIndentProperty,
                new CssTextIndent(used, hanging, eachLine));
            return true;
        }
    }

    private sealed class CssHyphenateLimitZoneValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            CssLayoutLength computed;
            if (length.Expression is { } expression)
                computed = CssLayoutLength.Math(expression, context.Lengths);
            else if (length.Unit == CssUnit.Percent)
                computed = CssLayoutLength.Percent(length.Value / 100);
            else if (length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels))
                computed = CssLayoutLength.Px(pixels);
            else
                return false;
            sink.Set(CssFlowProperties.HyphenateLimitZoneProperty, computed);
            return true;
        }
    }

    private sealed class CssWordSpacingValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            CssLayoutLength computed;
            if (length.Expression is { } expression)
                computed = CssLayoutLength.Math(expression, context.Lengths);
            else if (length.Unit == CssUnit.Percent)
                computed = CssLayoutLength.Percent(length.Value / 100);
            else if (length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) &&
                     double.IsFinite(pixels))
                computed = CssLayoutLength.Px(pixels);
            else
                return false;
            sink.Set(CssFlowProperties.WordSpacingProperty, computed);
            return true;
        }
    }

    private sealed class CssLetterSpacingValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ||
                !double.IsFinite(pixels)) return false;
            sink.Set(CssFlowProperties.LetterSpacingProperty, pixels);
            return true;
        }
    }

    private sealed class CssLinePaddingValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ||
                !double.IsFinite(pixels)) return false;
            sink.Set(CssFlowProperties.LinePaddingProperty, pixels);
            return true;
        }
    }

    private sealed class CssTabSizeValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ||
                !double.IsFinite(pixels)) return false;
            sink.Set(CssFlowProperties.TabSizeProperty, new CssTabSize(Math.Max(0, pixels), true));
            return true;
        }
    }

    private sealed class CssTextWrapModeValue(CssTextWrapMode mode) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
            => ApplyTextWrapMode(mode, context, sink);
    }

    internal static bool ApplyTextWrapMode(CssTextWrapMode mode,
        in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssFlowProperties.TextWrapModeProperty, mode);
        var native = CssDependencyPropertyLookup.Find(context.Element.GetType(), "TextWrapping");
        if (native?.PropertyType == typeof(TextWrapping))
            sink.Set(native, mode == CssTextWrapMode.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap);
        return true;
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
                "xxx-large" => CssLengthContext.DefaultFontSize * 3,
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

            if (ident.Equals("math", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                return new CssDeferredValue("font-size", "FontSize",
                    (in CssApplyContext ctx, out object? value) =>
                    {
                        var depth = ctx.MathDepth ?? (ctx.Element.GetValue(CssMathProperties.DepthProperty) is int current ? current : 0);
                        var size = CssMathProperties.FontSize(ctx.Element, depth, ctx.Lengths);
                        value = size;
                        return double.IsFinite(size) && size >= 0;
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
        // A negative literal is invalid, whereas math results are range-clamped
        // at computed-value time. Zero itself is a valid font size.
        if (length.Expression is null && length.Value < 0) return null;
        if (length.IsAbsolute)
        {
            var px = length.ToPxAbsolute();
            return double.IsFinite(px) && (length.Expression is not null || px >= 0)
                ? new CssNamedValue(cssName, "FontSize", Math.Max(0, px)) : null;
        }

        var captured = length;
        return new CssDeferredValue(cssName, "FontSize",
            (in CssApplyContext ctx, out object? value) =>
            {
                var basis = ctx.Lengths.ForFontProperty();
                var ok = captured.TryResolve(basis, CssPercentBasis.ElementFontSize, out var px) &&
                    double.IsFinite(px) && (captured.Expression is not null || px >= 0);
                value = captured.Expression is not null ? Math.Max(0, px) : px;
                return ok;
            });
    }

    private static CssCompiledValue? ParseFontWeight(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (Eq(ident, "normal")) { reader = probe; return AbsoluteFontWeight("font-weight", 400); }
            if (Eq(ident, "bold")) { reader = probe; return AbsoluteFontWeight("font-weight", 700); }
            if (Eq(ident, "bolder") || Eq(ident, "lighter"))
            {
                reader = probe;
                return RelativeFontWeight("font-weight", Eq(ident, "bolder"));
            }

            return null;
        }

        if (probe.TryReadNumber(out var value, out var unit) && unit == CssUnit.None &&
            value >= 1 && value <= 1000)
        {
            reader = probe;
            return AbsoluteFontWeight("font-weight", (int)Math.Round(value));
        }

        return null;
    }

    private static CssCompiledValue AbsoluteFontWeight(string cssName, int value)
        // CSS accepts 1000; the public WPF-compatible factory accepts 1..999.
        => new CssNamedValue(cssName, "FontWeight", new FontWeight(value));

    private static CssCompiledValue RelativeFontWeight(string cssName, bool bolder)
        => new CssDeferredValue(cssName, "FontWeight", (in CssApplyContext context, out object? value) =>
        {
            var inherited = context.Lengths.Fonts?.Parent.Weight ?? 400;
            var computed = inherited switch
            {
                < 100 => bolder ? 400 : inherited,
                < 350 => bolder ? 400 : 100,
                < 550 => bolder ? 700 : 100,
                < 750 => bolder ? 900 : 400,
                < 900 => bolder ? 900 : 700,
                _ => bolder ? inherited : 700,
            };
            value = new FontWeight(computed);
            return true;
        });

    private static void ReportObliqueAngleFallback(CssComputedFontStyle style)
    {
        if (style.AngleDegrees is not null)
            CssDiagnostics.Report("font-style", CssDiagnosticReason.LossyConversion, null,
                "oblique angle is retained for computed style; native face selection uses its default slant");
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
        var style = CssComputedFontStyle.Normal;
        CssCompiledValue weight = AbsoluteFontWeight("font", 400);
        var stretchPercentage = 100.0;
        var hasStyle = false;
        var hasWeight = false;
        var hasStretch = false;

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
                    if (hasStyle) return false;
                    probe = reader;
                    if (!CssFontStyleValue.TryRead(ref probe, out style)) return false;
                    hasStyle = true;
                    reader = probe;
                    continue;
                }

                if (Eq(ident, "bold"))
                {
                    if (hasWeight) return false;
                    hasWeight = true;
                    weight = AbsoluteFontWeight("font", 700);
                    reader = probe;
                    continue;
                }

                if (Eq(ident, "bolder") || Eq(ident, "lighter"))
                {
                    if (hasWeight) return false;
                    hasWeight = true;
                    weight = RelativeFontWeight("font", Eq(ident, "bolder"));
                    reader = probe;
                    continue;
                }

                var mappedStretch = MapFontStretch(ident);
                if (mappedStretch is not null && !Eq(ident, "normal"))
                {
                    if (hasStretch) return false;
                    hasStretch = true;
                    stretchPercentage = CssFontStretchValue.Percentage((FontStretch)mappedStretch);
                    reader = probe;
                    continue;
                }

                break; // A size keyword or the family list begins here.
            }

            probe = reader;
            if (probe.TryReadNumber(out var number, out var numberUnit) && numberUnit == CssUnit.None &&
                number >= 1 && number <= 1000)
            {
                if (hasWeight) return false;
                hasWeight = true;
                weight = AbsoluteFontWeight("font", (int)Math.Round(number));
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

        if (!CssFontFamilyValue.TryRead(ref reader, out var family)) return false;

        ReportObliqueAngleFallback(style);
        output.Add(new CssCompiledDeclaration("font-style", new CssFontStyleValue(style), false));
        output.Add(new CssCompiledDeclaration("font-weight", weight, false));
        output.Add(new CssCompiledDeclaration("font-stretch",
            new CssFontStretchValue(stretchPercentage), false));
        output.Add(new CssCompiledDeclaration("font-size", sizeValue, false));
        output.Add(new CssCompiledDeclaration("line-height", lineHeightValue, false));
        output.Add(new CssCompiledDeclaration("font-family", new CssFontFamilyValue(family), false));
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
