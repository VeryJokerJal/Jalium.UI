using Jalium.UI.Input;

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
        RegisterTextAndFonts();
        RegisterTransformAndTransition();
        RegisterDetailedTransitions();
        RegisterOutline();
        RegisterCssLayout();
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
        RegisterSize("min-width", FrameworkElement.MinWidthProperty, 0, "auto", CssLayoutSlotField.MinWidth);
        RegisterSize("min-height", FrameworkElement.MinHeightProperty, 0, "auto", CssLayoutSlotField.MinHeight);
        RegisterSize("max-width", FrameworkElement.MaxWidthProperty, double.PositiveInfinity, "none", CssLayoutSlotField.MaxWidth);
        RegisterSize("max-height", FrameworkElement.MaxHeightProperty, double.PositiveInfinity, "none", CssLayoutSlotField.MaxHeight);
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
                    return ident.Equals(keyword, StringComparison.OrdinalIgnoreCase) && reader.AtEnd
                        ? new CssImmediateValue(property, keywordValue)
                        : null;
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

        return reader.TryReadLength(out length);
    }

    private static void RegisterVisibility()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "visibility",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssDisplayProperties.VisibilityProperty,
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

        RegisterOverflow("overflow");
        RegisterOverflow("overflow-x");
        RegisterOverflow("overflow-y");

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "pointer-events",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                var hitTestVisible = !ident.Equals("none", StringComparison.OrdinalIgnoreCase);
                return new CssImmediateValue(UIElement.IsHitTestVisibleProperty, hitTestVisible);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "cursor",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseCursor(ref reader),
        });
    }

    private static void RegisterOverflow(string name)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                bool clip;
                if (ident.Equals("visible", StringComparison.OrdinalIgnoreCase))
                {
                    clip = false;
                }
                else if (ident.Equals("hidden", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("clip", StringComparison.OrdinalIgnoreCase))
                {
                    clip = true;
                }
                else if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("scroll", StringComparison.OrdinalIgnoreCase))
                {
                    CssDiagnostics.Report(
                        name, CssDiagnosticReason.LossyConversion, null,
                        "scrollable overflow does not create a ScrollViewer; content is clipped");
                    clip = true;
                }
                else
                {
                    return null;
                }

                return new CssImmediateValue(UIElement.ClipToBoundsProperty, clip);
            },
        });
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
            if (probe.TryReadIdent(out var ident))
            {
                // `auto` clears the ratio (slot untouched ⇒ state omits it).
                return ident.Equals("auto", StringComparison.OrdinalIgnoreCase) && probe.AtEnd
                    ? CssNoOpValue.Instance
                    : null;
            }

            if (!reader.TryReadNumber(out var width, out var widthUnit) || widthUnit != CssUnit.None || width <= 0)
            {
                return null;
            }

            var ratio = width;
            if (reader.TryReadSlash())
            {
                if (!reader.TryReadNumber(out var height, out var heightUnit) ||
                    heightUnit != CssUnit.None || height <= 0)
                {
                    return null;
                }

                ratio = width / height;
            }

            if (!reader.AtEnd || !double.IsFinite(ratio) || ratio <= 0)
            {
                return null;
            }

            var captured = ratio;
            return new CssSlotActionValue(slots => slots.SetAspectRatio(captured));
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
                return new CssSlotActionValue(static slots => slots.SetPosition(CssPositionMode.Static));
            }

            if (ident.Equals("absolute", StringComparison.OrdinalIgnoreCase))
            {
                return new CssSlotActionValue(static slots => slots.SetPosition(CssPositionMode.Absolute));
            }

            if (ident.Equals("relative", StringComparison.OrdinalIgnoreCase) ||
                ident.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                ident.Equals("sticky", StringComparison.OrdinalIgnoreCase))
            {
                CssDiagnostics.Report(
                    "position", CssDiagnosticReason.LossyConversion, null,
                    $"'{ident.ToString()}' positioning is not supported; treated as static");
                return new CssSlotActionValue(static slots => slots.SetPosition(CssPositionMode.Static));
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
                if (count == 4 || !reader.TryReadLength(out parts[count]))
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
    }

    private static void RegisterUnsupportedPlaceholders()
    {
        Unsupported("letter-spacing", "the framework has no character-spacing dependency property");
        Unsupported("word-spacing", "the framework has no word-spacing dependency property");
        Unsupported("text-transform", "changing text casing would rewrite the Text content");
        Unsupported("text-shadow", "no per-text shadow; box-shadow approximates a whole-element shadow");
        Unsupported("vertical-align", "inline baseline alignment has no general equivalent; use vertical-alignment for block alignment");
        Unsupported("clip-path", "not supported yet; UIElement.Clip exists for geometry clipping");
        Unsupported("backdrop-filter", "the element tree has no backdrop-filter dependency property yet");
        Unsupported("user-select", "the framework has no text-selection-suppression property");
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
        Unsupported("animation", "keyframe animations are not supported yet; transition is");
        Unsupported("animation-name", "see 'animation'");
        Unsupported("animation-duration", "see 'animation'");
        Unsupported("animation-timing-function", "see 'animation'");
        Unsupported("animation-delay", "see 'animation'");
        Unsupported("animation-iteration-count", "see 'animation'");
        Unsupported("animation-direction", "see 'animation'");
        Unsupported("animation-fill-mode", "see 'animation'");
        Unsupported("animation-play-state", "see 'animation'");
    }

    private static void Unsupported(string name, string reason)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Unsupported,
            UnsupportedReason = reason,
        });
}
