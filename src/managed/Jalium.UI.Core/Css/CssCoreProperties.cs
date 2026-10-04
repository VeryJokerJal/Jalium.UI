using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Core-level entries of the CSS property table: box model, visibility, cursor, and the
/// Unsupported placeholders that keep real CSS names from falling through to the kebab
/// fallback. Entries whose targets live in the Controls/Media layers are registered by
/// CssControlsProperties via <see cref="CssPropertyRegistry.RegisterExtension"/>.
/// </summary>
internal static partial class CssCoreProperties
{
    public static void RegisterAll()
    {
        RegisterShorthand("all", static (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) => false);
        RegisterSizes();
        RegisterBoxShorthands();
        RegisterVisibility();
        RegisterMisc();
        RegisterBackgroundAndBorders();
        RegisterImageObject();
        RegisterTextAndFonts();
        RegisterTextDecoration();
        RegisterCaretColor();
        RegisterCaretAnimation();
        RegisterCaretShape();
        RegisterCaretShorthand();
        RegisterTransformAndTransition();
        RegisterDetailedTransitions();
        RegisterAnimations();
        RegisterOutline();
        RegisterCssLayout();
        RegisterClipPath();
        CssUserSelectProperties.Register();
        RegisterUnsupportedPlaceholders();
    }

    /// <summary>A successfully parsed declaration that produces no setter (e.g. border-style: solid).</summary>
    private sealed class CssNoOpValue : CssCompiledValue
    {
        public static readonly CssNoOpValue Instance = new();

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink) => true;
    }

    private static void RegisterLonghand(string name, CssParseValueDelegate parse)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = parse,
        });

    private static void RegisterShorthand(string name, CssExpandDelegate expand)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Shorthand,
            Expand = expand,
        });

    private static object FreezeIfPossible(Jalium.UI.Media.Brush brush)
    {
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static void RegisterSizes()
    {
        RegisterSize("width", FrameworkElement.WidthProperty, double.NaN, "auto", CssLayoutSlotField.Width);
        RegisterSize("height", FrameworkElement.HeightProperty, double.NaN, "auto", CssLayoutSlotField.Height);
        RegisterSize("inline-size", FrameworkElement.WidthProperty, double.NaN, "auto", CssLayoutSlotField.Width);
        RegisterSize("block-size", FrameworkElement.HeightProperty, double.NaN, "auto", CssLayoutSlotField.Height);
        RegisterSize("min-width", FrameworkElement.MinWidthProperty, 0, "auto", CssLayoutSlotField.MinWidth);
        RegisterSize("min-height", FrameworkElement.MinHeightProperty, 0, "auto", CssLayoutSlotField.MinHeight);
        RegisterSize("min-inline-size", FrameworkElement.MinWidthProperty, 0, "auto", CssLayoutSlotField.MinWidth);
        RegisterSize("min-block-size", FrameworkElement.MinHeightProperty, 0, "auto", CssLayoutSlotField.MinHeight);
        RegisterSize("max-width", FrameworkElement.MaxWidthProperty, double.PositiveInfinity, "none", CssLayoutSlotField.MaxWidth);
        RegisterSize("max-height", FrameworkElement.MaxHeightProperty, double.PositiveInfinity, "none", CssLayoutSlotField.MaxHeight);
        RegisterSize("max-inline-size", FrameworkElement.MaxWidthProperty, double.PositiveInfinity, "none", CssLayoutSlotField.MaxWidth);
        RegisterSize("max-block-size", FrameworkElement.MaxHeightProperty, double.PositiveInfinity, "none", CssLayoutSlotField.MaxHeight);
    }

    private static void RegisterSize(
        string name, DependencyProperty property, double keywordValue, string keyword, CssLayoutSlotField field)
    {
        // The keyword value doubles as the percentage sentinel: it is exactly the
        // "neutral" DP value (auto=NaN / 0 / +∞) the layout pass treats as unset,
        // deferring to the CSS layout state while keeping DP-layer precedence intact.
        var sentinel = (object)keywordValue;
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (reader.TryReadIdent(out var ident))
                {
                    if (!ident.Equals(keyword, StringComparison.OrdinalIgnoreCase) || !reader.AtEnd)
                        return null;
                    return field is CssLayoutSlotField.MinWidth or CssLayoutSlotField.MinHeight
                        ? new CssLayoutKeywordValue(field, CssLayoutLength.Auto, property, sentinel)
                        : new CssImmediateValue(property, keywordValue);
                }

                return ParseLengthValue(ref reader, name, property, field, sentinel);
            },
        });
    }

    /// <summary>Absolute lengths convert at compile time; em/rem defer; % becomes layout state + a DP sentinel.</summary>
    private static CssCompiledValue? ParseLengthValue(
        ref CssTokenReader reader, string cssName, DependencyProperty property,
        CssLayoutSlotField field, object sentinel)
    {
        if (!reader.TryReadLength(out var length) || !reader.AtEnd)
        {
            return null;
        }

        if (length.Expression is null && length.Value < 0) return null;

        if (length.Expression is { UsesPercent: true })
            return new CssExpressionLayoutValue(field, length, property, sentinel);

        if (length.Unit == CssUnit.Percent)
        {
            var fraction = length.Value / 100.0;
            return fraction < 0 ? null : new CssLayoutValue(field, fraction, property, sentinel);
        }

        if (length.IsAbsolute)
        {
            var pixels = length.ToPxAbsolute();
            if (length.Expression is null && pixels < 0) return null;
            return new CssImmediateValue(property, Math.Max(0, pixels));
        }

        var captured = length;
        return new CssDeferredValue(cssName, property, (in CssApplyContext ctx, out object? value) =>
        {
            var ok = captured.TryResolve(ctx.Lengths, CssPercentBasis.NotSupported, out var px);
            value = Math.Max(0, px);
            return ok;
        });
    }

    private static void RegisterBoxShorthands()
    {
        RegisterBox("margin", CssSlot.Margin, allowAuto: true);
        RegisterBox("padding", CssSlot.Padding, allowAuto: false);
        RegisterLogicalBox("margin", CssSlot.Margin, allowAuto: true);
        RegisterLogicalBox("padding", CssSlot.Padding, allowAuto: false);
    }

    private static void RegisterLogicalBox(string prefix, CssSlot slot, bool allowAuto)
    {
        foreach (var axis in new[] { "inline", "block" })
        {
            var shorthand = $"{prefix}-{axis}";
            CssPropertyRegistry.Register(new CssPropertyDescriptor
            {
                Name = shorthand,
                Kind = CssPropertyKind.Shorthand,
                Expand = (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadEdge(ref reader, prefix, allowAuto, out var start)) return false;
                    var end = start;
                    if (!reader.AtEnd && !TryReadEdge(ref reader, prefix, allowAuto, out end)) return false;
                    if (!reader.AtEnd) return false;
                    var startName = $"{shorthand}-start";
                    var endName = $"{shorthand}-end";
                    output.Add(new CssCompiledDeclaration(startName, new CssLogicalThicknessEdge(slot, startName, start), false));
                    output.Add(new CssCompiledDeclaration(endName, new CssLogicalThicknessEdge(slot, endName, end), false));
                    return true;
                },
            });
            foreach (var side in new[] { "start", "end" })
            {
                var name = $"{shorthand}-{side}";
                CssPropertyRegistry.Register(new CssPropertyDescriptor
                {
                    Name = name,
                    Kind = CssPropertyKind.Longhand,
                    Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
                        TryReadEdge(ref reader, prefix, allowAuto, out var length) && reader.AtEnd
                            ? new CssLogicalThicknessEdge(slot, name, length) : null,
                });
            }
        }
    }

    private static void RegisterBox(string prefix, CssSlot slot, bool allowAuto)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = prefix,
            Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
            {
                CssLengthBuffer buffer = default;
                Span<CssLength> parts = buffer;
                var count = 0;
                while (!reader.AtEnd)
                {
                    if (count == 4 || !TryReadEdge(ref reader, prefix, allowAuto, out parts[count]))
                    {
                        return false;
                    }

                    count++;
                }

                if (count == 0)
                {
                    return false;
                }

                // CSS order: 1 → all; 2 → (vertical, horizontal); 3 → (top, horizontal, bottom); 4 → (T, R, B, L).
                var (top, right, bottom, left) = count switch
                {
                    1 => (parts[0], parts[0], parts[0], parts[0]),
                    2 => (parts[0], parts[1], parts[0], parts[1]),
                    3 => (parts[0], parts[1], parts[2], parts[1]),
                    _ => (parts[0], parts[1], parts[2], parts[3]),
                };

                output.Add(new CssCompiledDeclaration($"{prefix}-top", new CssSlotThicknessEdge(slot, 1, top), false));
                output.Add(new CssCompiledDeclaration($"{prefix}-right", new CssSlotThicknessEdge(slot, 2, right), false));
                output.Add(new CssCompiledDeclaration($"{prefix}-bottom", new CssSlotThicknessEdge(slot, 3, bottom), false));
                output.Add(new CssCompiledDeclaration($"{prefix}-left", new CssSlotThicknessEdge(slot, 0, left), false));
                return true;
            },
        });

        RegisterBoxEdge($"{prefix}-left", slot, 0, allowAuto, prefix);
        RegisterBoxEdge($"{prefix}-top", slot, 1, allowAuto, prefix);
        RegisterBoxEdge($"{prefix}-right", slot, 2, allowAuto, prefix);
        RegisterBoxEdge($"{prefix}-bottom", slot, 3, allowAuto, prefix);
    }

    private static void RegisterBoxEdge(string name, CssSlot slot, int edge, bool allowAuto, string shorthandName)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!TryReadEdge(ref reader, shorthandName, allowAuto, out var length) || !reader.AtEnd)
                {
                    return null;
                }

                return new CssSlotThicknessEdge(slot, edge, length);
            },
        });
    }

    private static bool TryReadEdge(ref CssTokenReader reader, string cssName, bool allowAuto, out CssLength length)
    {
        if (allowAuto)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                length = new CssLength(0, CssUnit.Auto);
                reader = probe;
                return true;
            }
        }

        if (!reader.TryReadLength(out length)) return false;
        if (length.Unit == CssUnit.None && length.Value != 0) return false;
        if (cssName == "padding" && length.Expression is null && length.Value < 0) return false;
        return length.Unit == CssUnit.Percent || length.IsLengthUnit;
    }

    private static void RegisterVisibility()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "visibility",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssDisplayProperties.VisibilityProperty,
            TransitionTargetDpName = CssDisplayProperties.VisibilityProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                if (ident.Equals("visible", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssVisibilityValue(Visibility.Visible);
                }

                if (ident.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssVisibilityValue(Visibility.Hidden);
                }

                if (ident.Equals("collapse", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssVisibilityValue(Visibility.Collapsed);
                }

                return null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "display",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssImmediateValue(UIElement.VisibilityProperty, Visibility.Collapsed);
                }

                // Any other display keyword maps to Visible so `display: block` can override an
                // earlier `display: none` (the common show/hide pattern); layout models don't change.
                CssDiagnostics.Report(
                    "display", CssDiagnosticReason.LossyConversion, null,
                    $"'{ident.ToString()}' does not change the layout model; treated as visible");
                return new CssImmediateValue(UIElement.VisibilityProperty, Visibility.Visible);
            },
        });
    }

    private static void RegisterMisc()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "opacity",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = UIElement.OpacityProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadNumber(out var value, out var unit) || !reader.AtEnd)
                {
                    return null;
                }

                var opacity = unit switch
                {
                    CssUnit.None => value,
                    CssUnit.Percent => value / 100.0,
                    _ => double.NaN,
                };
                if (double.IsNaN(opacity))
                {
                    return null;
                }

                return new CssImmediateValue(UIElement.OpacityProperty, Math.Clamp(opacity, 0.0, 1.0));
            },
        });

        RegisterOverflowLonghand("overflow-x", horizontal: true);
        RegisterOverflowLonghand("overflow-y", horizontal: false);
        RegisterOverflowLonghand("overflow-inline", horizontal: true);
        RegisterOverflowLonghand("overflow-block", horizontal: false);
        RegisterShorthand("overflow",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            if (!TryReadOverflowMode(ref reader, out var x)) return false;
            var y = x;
            if (!reader.AtEnd && !TryReadOverflowMode(ref reader, out y)) return false;
            if (!reader.AtEnd) return false;
            ReportScrollableOverflow("overflow", x, y);
            output.Add(new CssCompiledDeclaration("overflow-x",
                new CssSlotActionValue(slots => slots.SetOverflow(horizontal: true, mode: x)), false));
            output.Add(new CssCompiledDeclaration("overflow-y",
                new CssSlotActionValue(slots => slots.SetOverflow(horizontal: false, mode: y)), false));
            return true;
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overflow-clip-margin",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssOverflowProperties.ClipMarginProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
                ParseOverflowClipMargin(ref reader),
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "pointer-events",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssPointerEventsProperties.ValueProperty,
            TransitionTargetDpName = CssPointerEventsProperties.ValueProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }
                if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(CssPointerEventsProperties.ValueProperty, CssPointerEventsValue.Auto);
                if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                    return new CssImmediateValue(CssPointerEventsProperties.ValueProperty, CssPointerEventsValue.None);
                return null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "cursor",
            Kind = CssPropertyKind.Longhand,
            TransitionTargetDpName = nameof(FrameworkElement.Cursor),
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseCursor(ref reader),
        });
    }

    private static void RegisterOverflowLonghand(string name, bool horizontal)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!TryReadOverflowMode(ref reader, out var mode) || !reader.AtEnd) return null;
                ReportScrollableOverflow(name, mode);
                return new CssSlotActionValue(slots => slots.SetOverflow(horizontal, mode));
            },
        });
    }

    private static CssCompiledValue? ParseOverflowClipMargin(ref CssTokenReader reader)
    {
        var box = CssBackgroundBox.Padding;
        var length = new CssLength(0, CssUnit.Px);
        var hasBox = false;
        var hasLength = false;
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (!hasBox && probe.TryReadIdent(out var ident))
            {
                CssBackgroundBox? parsed = ident.ToString().ToLowerInvariant() switch
                {
                    "content-box" => CssBackgroundBox.Content,
                    "padding-box" => CssBackgroundBox.Padding,
                    "border-box" => CssBackgroundBox.Border,
                    _ => null,
                };
                if (parsed is { } value)
                {
                    box = value;
                    hasBox = true;
                    reader = probe;
                    continue;
                }
            }

            probe = reader;
            if (hasLength || !probe.TryReadLength(out var parsedLength) ||
                !parsedLength.IsLengthUnit || parsedLength.UsesPercent ||
                parsedLength.Unit == CssUnit.None && parsedLength.Value != 0 ||
                parsedLength.Expression is null && parsedLength.Value < 0)
                return null;
            length = parsedLength;
            hasLength = true;
            reader = probe;
        }
        if (!hasBox && !hasLength) return null;
        if (length.IsAbsolute)
        {
            var px = length.ToPxAbsolute();
            return double.IsFinite(px) && px >= 0
                ? new CssImmediateValue(CssOverflowProperties.ClipMarginProperty, new CssOverflowClipMargin(box, px))
                : null;
        }
        var capturedLength = length;
        return new CssDeferredValue("overflow-clip-margin", CssOverflowProperties.ClipMarginProperty,
            (in CssApplyContext ctx, out object? value) =>
            {
                var valid = capturedLength.TryResolve(ctx.Lengths, CssPercentBasis.NotSupported, out var px) &&
                    double.IsFinite(px) && px >= 0;
                value = valid ? new CssOverflowClipMargin(box, px) : null;
                return valid;
            });
    }

    private static bool TryReadOverflowMode(ref CssTokenReader reader, out CssOverflowMode mode)
    {
        mode = CssOverflowMode.Visible;
        if (!reader.TryReadIdent(out var ident)) return false;
        if (ident.Equals("visible", StringComparison.OrdinalIgnoreCase)) return true;
        if (ident.Equals("clip", StringComparison.OrdinalIgnoreCase)) { mode = CssOverflowMode.Clip; return true; }
        if (ident.Equals("hidden", StringComparison.OrdinalIgnoreCase)) { mode = CssOverflowMode.Hidden; return true; }
        if (ident.Equals("scroll", StringComparison.OrdinalIgnoreCase)) { mode = CssOverflowMode.Scroll; return true; }
        if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            ident.Equals("overlay", StringComparison.OrdinalIgnoreCase)) { mode = CssOverflowMode.Auto; return true; }
        return false;
    }

    private static void ReportScrollableOverflow(string name, params CssOverflowMode[] modes)
    {
        if (modes.Any(static mode => mode is CssOverflowMode.Auto or CssOverflowMode.Scroll))
            CssDiagnostics.Report(name, CssDiagnosticReason.LossyConversion, null,
                "scrollable overflow does not create a ScrollViewer; content is clipped");
    }

    private static CssCompiledValue? ParseCursor(ref CssTokenReader reader)
    {
        // Grammar: [url(...) ,]* keyword — url entries are skipped (no custom cursor loading).
        while (true)
        {
            var probe = reader;
            if (probe.TryReadFunction(out var fn, out _) && fn.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                probe.TryReadComma())
            {
                CssDiagnostics.Report(
                    "cursor", CssDiagnosticReason.LossyConversion, null,
                    "custom cursor urls are not supported; using the keyword fallback");
                reader = probe;
                continue;
            }

            break;
        }

        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
        {
            return null;
        }

        if (!TryMapCursor(ident, out var cursor))
        {
            return null;
        }

        return new CssImmediateValue(FrameworkElement.CursorProperty, cursor);
    }

    private static bool TryMapCursor(ReadOnlySpan<char> name, out Cursor? cursor)
    {
        cursor = null;
        if (Eq(name, "auto")) { return true; }
        if (Eq(name, "default")) { cursor = Cursors.Arrow; return true; }
        if (Eq(name, "pointer")) { cursor = Cursors.Hand; return true; }
        if (Eq(name, "text")) { cursor = Cursors.IBeam; return true; }
        if (Eq(name, "wait")) { cursor = Cursors.Wait; return true; }
        if (Eq(name, "progress")) { cursor = Cursors.AppStarting; return true; }
        if (Eq(name, "help")) { cursor = Cursors.Help; return true; }
        if (Eq(name, "move")) { cursor = Cursors.SizeAll; return true; }
        if (Eq(name, "none")) { cursor = Cursors.None; return true; }
        if (Eq(name, "crosshair") || Eq(name, "cell")) { cursor = Cursors.Cross; return true; }
        if (Eq(name, "not-allowed") || Eq(name, "no-drop")) { cursor = Cursors.No; return true; }
        if (Eq(name, "grab") || Eq(name, "grabbing")) { cursor = Cursors.Hand; return true; }
        if (Eq(name, "all-scroll")) { cursor = Cursors.ScrollAll; return true; }
        if (Eq(name, "col-resize") || Eq(name, "ew-resize") || Eq(name, "e-resize") || Eq(name, "w-resize"))
        {
            cursor = Cursors.SizeWE;
            return true;
        }

        if (Eq(name, "row-resize") || Eq(name, "ns-resize") || Eq(name, "n-resize") || Eq(name, "s-resize"))
        {
            cursor = Cursors.SizeNS;
            return true;
        }

        if (Eq(name, "nesw-resize") || Eq(name, "ne-resize") || Eq(name, "sw-resize"))
        {
            cursor = Cursors.SizeNESW;
            return true;
        }

        if (Eq(name, "nwse-resize") || Eq(name, "nw-resize") || Eq(name, "se-resize"))
        {
            cursor = Cursors.SizeNWSE;
            return true;
        }

        if (Eq(name, "context-menu") || Eq(name, "alias") || Eq(name, "copy") ||
            Eq(name, "vertical-text") || Eq(name, "zoom-in") || Eq(name, "zoom-out"))
        {
            CssDiagnostics.Report(
                "cursor", CssDiagnosticReason.LossyConversion, null,
                $"no framework cursor for '{name.ToString()}'; using the default arrow");
            cursor = Cursors.Arrow;
            return true;
        }

        return false;

        static bool Eq(ReadOnlySpan<char> text, string candidate)
            => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterCssLayout()
    {
        RegisterLonghand("aspect-ratio", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var probe = reader;
            var auto = false;
            if (probe.TryReadIdent(out var ident))
            {
                if (!ident.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
                reader = probe;
                auto = true;
                if (reader.AtEnd) return CssNoOpValue.Instance;
            }

            if (!reader.TryReadNumber(out var width, out var widthUnit) ||
                widthUnit != CssUnit.None || width < 0 || !double.IsFinite(width))
            {
                return null;
            }

            var height = 1d;
            if (reader.TryReadSlash())
            {
                if (!reader.TryReadNumber(out height, out var heightUnit) ||
                    heightUnit != CssUnit.None || height < 0 || !double.IsFinite(height))
                {
                    return null;
                }
            }

            if (!auto && !reader.AtEnd)
            {
                if (!reader.TryReadIdent(out ident) ||
                    !ident.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
                auto = true;
            }
            if (!reader.AtEnd) return null;
            var numerator = width;
            var denominator = height;
            var hasAuto = auto;
            return new CssSlotActionValue(slots => slots.SetAspectRatio(numerator, denominator, hasAuto));
        });

        RegisterLonghand("box-sizing", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            if (ident.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            {
                return new CssSlotActionValue(static slots => slots.SetBoxSizing(CssBoxSizing.BorderBox));
            }

            if (ident.Equals("content-box", StringComparison.OrdinalIgnoreCase))
            {
                return new CssSlotActionValue(static slots => slots.SetBoxSizing(CssBoxSizing.ContentBox));
            }

            return null;
        });

        RegisterLonghand("position", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            {
                return null;
            }

            if (ident.Equals("static", StringComparison.OrdinalIgnoreCase))
            {
                return new CssSlotActionValue(static slots => slots.SetPosition(CssPositionKeyword.Static));
            }

            if (ident.Equals("absolute", StringComparison.OrdinalIgnoreCase))
            {
                return new CssSlotActionValue(static slots => slots.SetPosition(CssPositionKeyword.Absolute));
            }

            if (ident.Equals("relative", StringComparison.OrdinalIgnoreCase))
            {
                return new CssSlotActionValue(static slots => slots.SetPosition(CssPositionKeyword.Relative));
            }

            if (ident.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                ident.Equals("sticky", StringComparison.OrdinalIgnoreCase))
            {
                CssDiagnostics.Report(
                    "position", CssDiagnosticReason.LossyConversion, null,
                    $"'{ident.ToString()}' positioning is not supported; treated as static");
                var computed = ident.Equals("fixed", StringComparison.OrdinalIgnoreCase)
                    ? CssPositionKeyword.Fixed : CssPositionKeyword.Sticky;
                return new CssSlotActionValue(slots => slots.SetPosition(computed));
            }

            return null;
        });

        RegisterShorthand("inset",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            CssLengthBuffer buffer = default;
            Span<CssLength> parts = buffer;
            var count = 0;
            while (!reader.AtEnd)
            {
                if (count == 4 || !TryReadInsetLength(ref reader, out parts[count]))
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

            output.Add(new CssCompiledDeclaration("top", new CssInsetValue("top", 1, top, null), false));
            output.Add(new CssCompiledDeclaration("right", new CssInsetValue("right", 2, right, null), false));
            output.Add(new CssCompiledDeclaration("bottom", new CssInsetValue("bottom", 3, bottom, null), false));
            output.Add(new CssCompiledDeclaration("left", new CssInsetValue("left", 0, left, null), false));
            return true;
        });
        RegisterLogicalInsets();
    }

    internal static bool TryReadInsetLength(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            length = new CssLength(0, CssUnit.Auto);
            return true;
        }
        return reader.TryReadLength(out length);
    }

    private static void RegisterLogicalInsets()
    {
        foreach (var axis in new[] { "inline", "block" })
        {
            var shorthand = $"inset-{axis}";
            RegisterShorthand(shorthand,
                (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadInsetLength(ref reader, out var start)) return false;
                    var end = start;
                    if (!reader.AtEnd && !TryReadInsetLength(ref reader, out end)) return false;
                    if (!reader.AtEnd) return false;
                    var startName = $"{shorthand}-start";
                    var endName = $"{shorthand}-end";
                    output.Add(new CssCompiledDeclaration(startName, new CssInsetValue(startName, -1, start, null), false));
                    output.Add(new CssCompiledDeclaration(endName, new CssInsetValue(endName, -1, end, null), false));
                    return true;
                });
            foreach (var side in new[] { "start", "end" })
            {
                var name = $"{shorthand}-{side}";
                RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
                    TryReadInsetLength(ref reader, out var length) && reader.AtEnd
                        ? new CssInsetValue(name, -1, length, null) : null);
            }
        }
    }

    private static void RegisterUnsupportedPlaceholders()
    {
        Unsupported("text-shadow", "text rendering extension is unavailable");
        Unsupported("vertical-align", "inline baseline alignment has no general equivalent; use vertical-alignment for block alignment");
        Unsupported("content", "pseudo-element content generation is not supported");
        Unsupported("float", "float layout has no equivalent panel");
        Unsupported("clear", "float layout has no equivalent panel");
        Unsupported("grid-area", "grid template definitions belong to the container; not supported");
        Unsupported("grid-template-columns", "define columns on the Grid panel itself");
        Unsupported("grid-template-rows", "define rows on the Grid panel itself");
        Unsupported("grid-template-areas", "named grid areas are not supported");
        Unsupported("align-self", "cross-axis semantics depend on the parent panel; use horizontal-alignment/vertical-alignment");
        Unsupported("justify-self", "cross-axis semantics depend on the parent panel; use horizontal-alignment/vertical-alignment");
        Unsupported("align-items", "container alignment belongs to the panel; set alignment on the children");
        Unsupported("justify-content", "container alignment belongs to the panel; set alignment on the children");
        Unsupported("flex", "flexbox has no equivalent panel; use StackPanel/DockPanel/Grid");
        Unsupported("flex-direction", "see 'flex'");
        Unsupported("flex-wrap", "see 'flex' (WrapPanel wraps automatically)");
        Unsupported("flex-grow", "see 'flex'");
        Unsupported("flex-shrink", "see 'flex'");
        Unsupported("flex-basis", "see 'flex'");
    }

    private static void Unsupported(string name, string reason)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Unsupported,
            UnsupportedReason = reason,
        });
}
