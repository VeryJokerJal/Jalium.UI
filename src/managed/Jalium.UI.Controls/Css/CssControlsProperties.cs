using System.Runtime.CompilerServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Controls/Media-level entries of the CSS property table: layout attached properties
/// (z-index, left/top, grid-row/-column, gap), shadow/filter effects, and text-decoration.
/// Registered as a deferred extension so the module initializer stays free of cross-type work.
/// </summary>
internal static class CssControlsProperties
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "The Controls-level CSS entries must be registered before any style sheet is parsed, which can happen before the first control type is touched.")]
    internal static void Hook()
        => CssPropertyRegistry.RegisterExtension(RegisterAll);

    private static void RegisterAll()
    {
        CssSlotAccumulator.EffectCombiner = CombineEffects;

        RegisterLayoutAttached();
        RegisterGap();
        RegisterEffects();
        RegisterTextDecoration();
        RegisterFlex();
        CssGridProperties.Register();
        CssGapProperties.Register();
        CssDisplayProperties.Register();
        CssFlowProperties.Register();
        CssFloatProperties.Register();
        CssContainerProperties.Register();
    }

    // ── Flexbox ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Container properties dispatch to FlexPanel's own DPs; any other element gets a
    /// chance through the interception protocol (StackPanel maps flex-direction to
    /// Orientation) before the diagnostic. Item properties are plain attached DPs —
    /// inert under a non-Flex parent, exactly like Grid.Row under a non-Grid.
    /// </summary>
    private static void RegisterFlex()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "flex-flow", Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
            {
                string? direction = null, wrap = null;
                while (!reader.AtEnd)
                {
                    if (!reader.TryReadIdent(out var word)) return false;
                    var value = word.ToString().ToLowerInvariant();
                    if (value is "row" or "row-reverse" or "column" or "column-reverse")
                    { if (direction is not null) return false; direction = value; }
                    else if (value is "nowrap" or "wrap" or "wrap-reverse")
                    { if (wrap is not null) return false; wrap = value; }
                    else return false;
                }
                if (direction is null && wrap is null) return false;
                output.AddRange(CssEngine.CompileDeclarations([
                    new CssDeclaration { PropertyName = "flex-direction", RawValue = direction ?? "row" },
                    new CssDeclaration { PropertyName = "flex-wrap", RawValue = wrap ?? "nowrap" },
                ], context));
                return true;
            },
        });
        RegisterFlexContainer("flex-direction", static ident =>
            ident switch
            {
                "row" => (FlexPanel.DirectionProperty, (object)FlexDirection.Row),
                "row-reverse" => (FlexPanel.DirectionProperty, (object)FlexDirection.RowReverse),
                "column" => (FlexPanel.DirectionProperty, (object)FlexDirection.Column),
                "column-reverse" => (FlexPanel.DirectionProperty, (object)FlexDirection.ColumnReverse),
                _ => null,
            });

        RegisterFlexContainer("flex-wrap", static ident =>
            ident switch
            {
                "nowrap" => (FlexPanel.WrapProperty, (object)FlexWrap.NoWrap),
                "wrap" => (FlexPanel.WrapProperty, (object)FlexWrap.Wrap),
                "wrap-reverse" => (FlexPanel.WrapProperty, (object)FlexWrap.WrapReverse),
                _ => null,
            });

        RegisterFlexContainer("justify-content", static ident =>
            ident switch
            {
                "flex-start" or "start" or "left" or "normal" => (FlexPanel.JustifyContentProperty, (object)FlexJustify.FlexStart),
                "flex-end" or "end" or "right" => (FlexPanel.JustifyContentProperty, (object)FlexJustify.FlexEnd),
                "center" => (FlexPanel.JustifyContentProperty, (object)FlexJustify.Center),
                "space-between" => (FlexPanel.JustifyContentProperty, (object)FlexJustify.SpaceBetween),
                "space-around" => (FlexPanel.JustifyContentProperty, (object)FlexJustify.SpaceAround),
                "space-evenly" => (FlexPanel.JustifyContentProperty, (object)FlexJustify.SpaceEvenly),
                _ => null,
            });

        RegisterFlexContainer("align-items", static ident =>
            ident switch
            {
                "stretch" or "normal" => (FlexPanel.AlignItemsProperty, (object)FlexAlign.Stretch),
                "flex-start" or "start" or "self-start" => (FlexPanel.AlignItemsProperty, (object)FlexAlign.FlexStart),
                "flex-end" or "end" or "self-end" => (FlexPanel.AlignItemsProperty, (object)FlexAlign.FlexEnd),
                "center" => (FlexPanel.AlignItemsProperty, (object)FlexAlign.Center),
                "baseline" => Lossy(FlexPanel.AlignItemsProperty, FlexAlign.FlexStart, "align-items", "baseline alignment is not supported; falling back to flex-start"),
                _ => null,
            });

        RegisterFlexContainer("align-content", static ident =>
            ident switch
            {
                "stretch" or "normal" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.Stretch),
                "flex-start" or "start" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.FlexStart),
                "flex-end" or "end" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.FlexEnd),
                "center" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.Center),
                "space-between" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.SpaceBetween),
                "space-around" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.SpaceAround),
                "space-evenly" => (FlexPanel.AlignContentProperty, (object)FlexContentAlign.SpaceEvenly),
                _ => null,
            });

        RegisterFlexItemNumber("flex-grow", FlexPanel.GrowProperty);
        RegisterFlexItemNumber("flex-shrink", FlexPanel.ShrinkProperty);

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "flex-basis",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseFlexBasis(ref reader),
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "align-self",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var identSpan) || !reader.AtEnd)
                {
                    return null;
                }

                var ident = identSpan.ToString().ToLowerInvariant();
                FlexAlign? align = ident switch
                {
                    "auto" => FlexAlign.Auto,
                    "stretch" or "normal" => FlexAlign.Stretch,
                    "flex-start" or "start" or "self-start" => FlexAlign.FlexStart,
                    "flex-end" or "end" or "self-end" => FlexAlign.FlexEnd,
                    "center" => FlexAlign.Center,
                    "baseline" => FlexAlign.FlexStart,
                    _ => null,
                };
                if (align is null)
                {
                    return null;
                }

                if (ident == "baseline")
                {
                    CssDiagnostics.Report(
                        "align-self", CssDiagnosticReason.LossyConversion, null,
                        "baseline alignment is not supported; falling back to flex-start");
                }

                return new CssImmediateValue(FlexPanel.AlignSelfProperty, align.Value);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "order",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadInteger(out var value) || !reader.AtEnd)
                {
                    return null;
                }

                return new CssImmediateValue(FlexPanel.OrderProperty, value);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "flex",
            Kind = CssPropertyKind.Shorthand,
            Expand = ExpandFlexShorthand,
        });
    }

    /// <summary>
    /// Controls-layer display override: adds flex awareness on top of the Core semantics
    /// (none → Collapsed, anything else → Visible). display:flex on a FlexPanel is a
    /// true no-op; on other elements it cannot change the layout model.
    /// </summary>
    private static void RegisterDisplayOverride()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "display",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var identSpan) || !reader.AtEnd)
                {
                    return null;
                }

                if (identSpan.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssImmediateValue(UIElement.VisibilityProperty, Visibility.Collapsed);
                }

                if (identSpan.Equals("flex", StringComparison.OrdinalIgnoreCase) ||
                    identSpan.Equals("inline-flex", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssDisplayFlexValue(CssDisplayMode.Flex);
                }

                if (identSpan.Equals("grid", StringComparison.OrdinalIgnoreCase) ||
                    identSpan.Equals("inline-grid", StringComparison.OrdinalIgnoreCase))
                    return new CssDisplayFlexValue(CssDisplayMode.Grid);

                CssDiagnostics.Report(
                    "display", CssDiagnosticReason.LossyConversion, null,
                    $"'{identSpan.ToString()}' does not change the layout model; treated as visible");
                return new CssImmediateValue(UIElement.VisibilityProperty, Visibility.Visible);
            },
        });

    private sealed class CssDisplayFlexValue(CssDisplayMode mode) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (context.Element.Target is not Panel)
            {
                CssDiagnostics.Report(
                    "display", CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                    $"display:{mode.ToString().ToLowerInvariant()} needs a panel that owns a layout child collection");
            }
            else sink.Set(CssDisplayLayout.ModeProperty, mode);

            sink.Set(UIElement.VisibilityProperty, Visibility.Visible);
            return true;
        }
    }

    private static (DependencyProperty, object)? Lossy(
        DependencyProperty property, object value, string cssName, string message)
    {
        CssDiagnostics.Report(cssName, CssDiagnosticReason.LossyConversion, null, message);
        return (property, value);
    }

    private static void RegisterFlexContainer(string cssName, Func<string, (DependencyProperty, object)?> map)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = cssName,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var identSpan) || !reader.AtEnd)
                {
                    return null;
                }

                var ident = identSpan.ToString().ToLowerInvariant();
                var mapped = map(ident);
                return mapped is null
                    ? null
                    : new CssFlexContainerValue(cssName, ident, mapped.Value.Item1, mapped.Value.Item2);
            },
        });

    private static void RegisterFlexItemNumber(string cssName, DependencyProperty property)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = cssName,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadNumber(out var value, out var unit) || unit != CssUnit.None ||
                    value < 0 && !reader.NumberWasCalculated || !reader.AtEnd)
                {
                    return null;
                }

                return new CssImmediateValue(property, Math.Max(0, value));
            },
        });

    private static CssCompiledValue? ParseFlexBasis(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && probe.AtEnd)
        {
            if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return new CssImmediateValue(FlexPanel.BasisProperty, double.NaN);
            }

            if (ident.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                CssDiagnostics.Report(
                    "flex-basis", CssDiagnosticReason.LossyConversion, null,
                    "'content' is treated as auto");
                return new CssImmediateValue(FlexPanel.BasisProperty, double.NaN);
            }

            return null;
        }

        if (!reader.TryReadLength(out var length) || !reader.AtEnd)
        {
            return null;
        }

        if (length.Expression is null && length.Value < 0) return null;
        if (length.UsesPercent)
        {
            return new CssFlexBasisValue(length);
        }

        if (length.IsAbsolute)
        {
            return new CssImmediateValue(FlexPanel.BasisProperty, Math.Max(0, length.ToPxAbsolute()));
        }

        var captured = length;
        return new CssDeferredValue("flex-basis", FlexPanel.BasisProperty,
            (in CssApplyContext ctx, out object? value) =>
            {
                var ok = captured.TryResolve(ctx.Lengths, CssPercentBasis.NotSupported, out var px) && px >= 0;
                value = px;
                return ok;
            });
    }

    /// <summary>flex: none | auto | initial | [grow shrink? basis?] in the CSS component order.</summary>
    private static bool ExpandFlexShorthand(
        ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output)
    {
        double grow = 1, shrink = 1, basis = double.NaN;
        CssLength? contextualBasis = null;

        var probe = reader;
        if (probe.TryReadIdent(out var ident) && probe.AtEnd)
        {
            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                (grow, shrink, basis) = (0, 0, double.NaN);
            }
            else if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                (grow, shrink, basis) = (1, 1, double.NaN);
            }
            else if (ident.Equals("initial", StringComparison.OrdinalIgnoreCase))
            {
                (grow, shrink, basis) = (0, 1, double.NaN);
            }
            else
            {
                return false;
            }

            reader = probe;
        }
        else
        {
            var numberSlot = 0; // 0 = grow next, 1 = shrink next, 2 = numbers exhausted
            var sawBasis = false;
            var sawAnything = false;
            while (!reader.AtEnd)
            {
                var lengthProbe = reader;
                if (lengthProbe.TryReadLength(out var basisLength) && basisLength.Unit != CssUnit.None)
                {
                    if (sawBasis || basisLength.IsAbsolute && basisLength.ToPxAbsolute() < 0 || basisLength.Unit == CssUnit.Percent && basisLength.Value < 0) return false;
                    if (basisLength.IsAbsolute) basis = basisLength.ToPxAbsolute();
                    else contextualBasis = basisLength;
                    sawBasis = true;
                    sawAnything = true;
                    reader = lengthProbe;
                    continue;
                }
                var numberProbe = reader;
                if (numberProbe.TryReadNumber(out var value, out var unit))
                {
                    if (unit == CssUnit.None)
                    {
                        if (numberProbe.NumberWasCalculated) value = Math.Max(0, value);
                        if (numberSlot == 2 && value == 0 && !sawBasis)
                        {
                            basis = 0;
                            sawBasis = true;
                            reader = numberProbe;
                            continue;
                        }
                        if (numberSlot >= 2 || value < 0 || !double.IsFinite(value))
                        {
                            return false;
                        }

                        if (numberSlot == 0)
                        {
                            grow = value;
                        }
                        else
                        {
                            shrink = value;
                        }

                        numberSlot++;
                        sawAnything = true;
                        reader = numberProbe;
                        continue;
                    }

                    // Valid dimensions were consumed by the length probe above.
                    return false;
                }

                var identProbe = reader;
                if (identProbe.TryReadIdent(out var word) &&
                    (word.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                     word.Equals("content", StringComparison.OrdinalIgnoreCase)))
                {
                    if (sawBasis)
                    {
                        return false;
                    }

                    basis = double.NaN;
                    sawBasis = true;
                    sawAnything = true;
                    reader = identProbe;
                    continue;
                }

                return false;
            }

            if (!sawAnything)
            {
                return false;
            }

            // `flex: <number>` implies basis 0 (the flex:1 pattern).
            if (numberSlot >= 1 && !sawBasis)
            {
                basis = 0;
            }

        }

        output.Add(new CssCompiledDeclaration("flex-grow",
            new CssImmediateValue(FlexPanel.GrowProperty, grow), false));
        output.Add(new CssCompiledDeclaration("flex-shrink",
            new CssImmediateValue(FlexPanel.ShrinkProperty, shrink), false));
        output.Add(new CssCompiledDeclaration("flex-basis",
            contextualBasis is { } deferred ? new CssFlexBasisValue(deferred) : new CssImmediateValue(FlexPanel.BasisProperty, basis), false));
        return true;
    }

    private sealed class CssFlexBasisValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            sink.Set(FlexPanel.BasisProperty, double.NaN);
            sink.Set(FlexPanel.CssBasisProperty, length);
            return true;
        }
    }

    /// <summary>
    /// Container-property dispatch: FlexPanel takes the value directly; other elements get
    /// the interception protocol (rawValue form); nobody consuming it reports a diagnostic.
    /// </summary>
    private sealed class CssFlexContainerValue : CssCompiledValue
    {
        private readonly string _cssName;
        private readonly string _rawValue;
        private readonly DependencyProperty _flexProperty;
        private readonly object _boxedValue;

        public CssFlexContainerValue(string cssName, string rawValue, DependencyProperty flexProperty, object boxedValue)
        {
            _cssName = cssName;
            _rawValue = rawValue;
            _flexProperty = flexProperty;
            _boxedValue = boxedValue;
        }

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            CssGridProperties.SetContainerAlignment(sink, _cssName, _rawValue);
            if (context.Element.Target is FlexPanel)
            {
                sink.Set(_flexProperty, _boxedValue);
                return true;
            }

            if (context.Element.Target is Panel) sink.Set(_flexProperty, _boxedValue);

            var setter = new CssDeclarationSetter(sink, context.Element, _cssName);
            if (context.Element.TryApplyCssPropertyCore(_cssName, _rawValue, in setter))
            {
                return true;
            }
            if (context.Element.Target is Panel) return true;

            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                "flex container properties need a FlexPanel (or a panel that intercepts them)");
            return false;
        }
    }

    private static void RegisterLayoutAttached()
    {
        RegisterDisplayOverride();

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "z-index",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident))
                {
                    return ident.Equals("auto", StringComparison.OrdinalIgnoreCase) && probe.AtEnd
                        ? new CssImmediateValue(Panel.ZIndexProperty, 0)
                        : null;
                }

                if (!reader.TryReadInteger(out var value) || !reader.AtEnd)
                {
                    return null;
                }

                return new CssImmediateValue(Panel.ZIndexProperty, value);
            },
        });

        // Inset edges: position:absolute routes them into the CSS layout state; static
        // keeps the historical Canvas mapping for absolute pixels.
        RegisterInsetEdge("left", 0, Canvas.LeftProperty);
        RegisterInsetEdge("top", 1, Canvas.TopProperty);
        RegisterInsetEdge("right", 2, Canvas.RightProperty);
        RegisterInsetEdge("bottom", 3, Canvas.BottomProperty);

        RegisterGridPlacement("grid-row", Grid.RowProperty, Grid.RowSpanProperty);
        RegisterGridPlacement("grid-column", Grid.ColumnProperty, Grid.ColumnSpanProperty);
    }

    private static void RegisterInsetEdge(string name, int edge, DependencyProperty staticCompatDp)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadLength(out var length) || !reader.AtEnd)
                {
                    return null;
                }

                return new CssInsetValue(name, edge, length, staticCompatDp);
            },
        });

    private static void RegisterGridPlacement(string name, DependencyProperty lineProperty, DependencyProperty spanProperty)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                // Supported grammar: <line> | <line> / <line> | span <n>. CSS lines are
                // 1-based; the Grid attached properties are 0-based.
                var probe = reader;
                if (probe.TryReadIdent(out var ident) && ident.Equals("span", StringComparison.OrdinalIgnoreCase))
                {
                    if (!probe.TryReadNumber(out var spanOnly, out var spanUnit) ||
                        spanUnit != CssUnit.None || spanOnly < 1 || !probe.AtEnd)
                    {
                        return null;
                    }

                    reader = probe;
                    return new CssImmediateValue(spanProperty, (int)Math.Round(spanOnly));
                }

                if (!reader.TryReadNumber(out var start, out var startUnit) || startUnit != CssUnit.None || start < 1)
                {
                    if (start < 1)
                    {
                        CssDiagnostics.Report(
                            name, CssDiagnosticReason.LossyConversion, null,
                            "negative or zero grid lines are not supported; declaration dropped");
                    }

                    return null;
                }

                var line = (int)Math.Round(start) - 1;
                if (reader.AtEnd)
                {
                    return new CssImmediateValue(lineProperty, line);
                }

                if (!reader.TryReadSlash())
                {
                    return null;
                }

                probe = reader;
                if (probe.TryReadIdent(out var spanIdent) && spanIdent.Equals("span", StringComparison.OrdinalIgnoreCase))
                {
                    if (!probe.TryReadNumber(out var spanValue, out var unit2) ||
                        unit2 != CssUnit.None || spanValue < 1 || !probe.AtEnd)
                    {
                        return null;
                    }

                    reader = probe;
                    return new CssImmediateValue(
                        new[] { lineProperty, spanProperty },
                        new object?[] { line, (int)Math.Round(spanValue) });
                }

                if (!reader.TryReadNumber(out var end, out var endUnit) || endUnit != CssUnit.None ||
                    end <= start || !reader.AtEnd)
                {
                    return null;
                }

                return new CssImmediateValue(
                    new[] { lineProperty, spanProperty },
                    new object?[] { line, (int)Math.Round(end - start) });
            },
        });

    private static void RegisterGap()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "gap",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!TryReadGapLength(ref reader, out var row))
                {
                    return null;
                }

                var column = row;
                if (!reader.AtEnd && (!TryReadGapLength(ref reader, out column) || !reader.AtEnd))
                {
                    return null;
                }

                return new CssGapValue(row, column, setRow: true, setColumn: true);
            },
        });

        RegisterSingleAxisGap("row-gap", isRow: true);
        RegisterSingleAxisGap("column-gap", isRow: false);
    }

    private static void RegisterSingleAxisGap(string name, bool isRow)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!TryReadGapLength(ref reader, out var value) || !reader.AtEnd)
                {
                    return null;
                }

                return new CssGapValue(value, value, setRow: isRow, setColumn: !isRow);
            },
        });

    private static bool TryReadGapLength(ref CssTokenReader reader, out double px)
    {
        px = 0;
        if (!reader.TryReadLength(out var length) || !length.IsAbsolute)
        {
            return false;
        }

        px = Math.Max(0, length.ToPxAbsolute());
        return true;
    }

    /// <summary>Dispatches gap onto the panel type's own spacing properties.</summary>
    private sealed class CssGapValue : CssCompiledValue
    {
        private readonly double _rowGap;
        private readonly double _columnGap;
        private readonly bool _setRow;
        private readonly bool _setColumn;

        public CssGapValue(double rowGap, double columnGap, bool setRow, bool setColumn)
        {
            _rowGap = rowGap;
            _columnGap = columnGap;
            _setRow = setRow;
            _setColumn = setColumn;
        }

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (context.Element.Target is Panel)
            {
                if (_setRow) sink.Set(FlexPanel.RowSpacingProperty, _rowGap);
                if (_setColumn) sink.Set(FlexPanel.ColumnSpacingProperty, _columnGap);
            }
            switch (context.Element.Target)
            {
                case FlexPanel flexPanel:
                    if (_setRow)
                    {
                        sink.Set(FlexPanel.RowSpacingProperty, _rowGap);
                    }

                    if (_setColumn)
                    {
                        sink.Set(FlexPanel.ColumnSpacingProperty, _columnGap);
                    }

                    return true;
                case Grid:
                    if (_setRow)
                    {
                        sink.Set(Grid.RowSpacingProperty, _rowGap);
                    }

                    if (_setColumn)
                    {
                        sink.Set(Grid.ColumnSpacingProperty, _columnGap);
                    }

                    return true;
                case UniformGrid:
                    if (_setRow)
                    {
                        sink.Set(UniformGrid.RowSpacingProperty, _rowGap);
                    }

                    if (_setColumn)
                    {
                        sink.Set(UniformGrid.ColumnSpacingProperty, _columnGap);
                    }

                    return true;
                case WrapPanel:
                    if (_setRow)
                    {
                        sink.Set(WrapPanel.VerticalSpacingProperty, _rowGap);
                    }

                    if (_setColumn)
                    {
                        sink.Set(WrapPanel.HorizontalSpacingProperty, _columnGap);
                    }

                    return true;
                case StackPanel:
                    ReportSingleAxis(context, _setRow && _setColumn && _rowGap != _columnGap);
                    sink.Set(StackPanel.SpacingProperty, _setRow ? _rowGap : _columnGap);
                    return true;
                case DockPanel:
                    ReportSingleAxis(context, _setRow && _setColumn && _rowGap != _columnGap);
                    sink.Set(DockPanel.SpacingProperty, _setRow ? _rowGap : _columnGap);
                    return true;
                default:
                    CssDiagnostics.Report(
                        "gap", CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                        "this panel type has no spacing properties; declaration skipped here");
                    return false;
            }
        }

        private static void ReportSingleAxis(in CssApplyContext context, bool lossy)
        {
            if (lossy)
            {
                CssDiagnostics.Report(
                    "gap", CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                    "this panel has a single spacing value; the row gap is used");
            }
        }
    }

    private static void RegisterEffects()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "box-shadow",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseBoxShadow(ref reader),
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "filter",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseFilter(ref reader),
        });
    }

    private static CssCompiledValue? ParseBoxShadow(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) &&
            ident.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        {
            reader = probe;
            return new CssSlotActionValue(static slots => slots.SetBoxShadow(null));
        }

        var effects = new List<Effect>();
        while (true)
        {
            if (!TryParseSingleShadow(ref reader, out var effect))
            {
                return null;
            }

            effects.Add(effect);
            if (!reader.TryReadComma())
            {
                break;
            }
        }

        if (!reader.AtEnd)
        {
            return null;
        }

        var combined = Combine(effects);
        return new CssSlotActionValue(slots => slots.SetBoxShadow(combined));
    }

    private static bool TryParseSingleShadow(ref CssTokenReader reader, out Effect effect)
    {
        effect = null!;
        var inset = false;
        Color color = Color.FromArgb(0xFF, 0, 0, 0);
        var hasColor = false;
        Span<double> lengths = stackalloc double[4];
        var lengthCount = 0;

        while (!reader.AtEnd)
        {
            var probe = reader;
            if (probe.TryPeekChar(out var c) && c == ',')
            {
                break;
            }

            if (probe.TryReadIdent(out var ident) && ident.Equals("inset", StringComparison.OrdinalIgnoreCase))
            {
                inset = true;
                reader = probe;
                continue;
            }

            probe = reader;
            if (!hasColor && CssColorParser.TryParse(ref probe, out var parsedColor, out var isCurrentColor) &&
                !isCurrentColor)
            {
                color = parsedColor;
                hasColor = true;
                reader = probe;
                continue;
            }

            probe = reader;
            if (probe.TryReadLength(out var length) && length.IsAbsolute && lengthCount < 4)
            {
                lengths[lengthCount++] = length.ToPxAbsolute();
                reader = probe;
                continue;
            }

            return false;
        }

        if (lengthCount < 2)
        {
            return false;
        }

        var offsetX = lengths[0];
        var offsetY = lengths[1];
        var blur = lengthCount > 2 ? Math.Max(0, lengths[2]) : 0;
        var spread = lengthCount > 3 ? lengths[3] : 0;

        // CSS cartesian offsets → the effect's polar Direction/ShadowDepth. Screen y grows
        // downward, so the y component is negated; Direction 0° points right, counterclockwise.
        var depth = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        var direction = depth == 0 ? 0 : Math.Atan2(-offsetY, offsetX) * 180.0 / Math.PI;
        if (direction < 0)
        {
            direction += 360.0;
        }

        var opaque = Color.FromArgb(0xFF, color.R, color.G, color.B);
        var opacity = color.A / 255.0;

        if (inset)
        {
            effect = new InnerShadowEffect
            {
                ShadowDepth = depth,
                Direction = direction,
                BlurRadius = blur,
                Color = opaque,
                Opacity = opacity,
                SpreadRadius = spread,
            };
        }
        else
        {
            if (spread != 0)
            {
                CssDiagnostics.Report(
                    "box-shadow", CssDiagnosticReason.LossyConversion, null,
                    "spread radii on outer shadows are not supported and are ignored");
            }

            effect = new DropShadowEffect
            {
                ShadowDepth = depth,
                Direction = direction,
                BlurRadius = blur,
                Color = opaque,
                Opacity = opacity,
            };
        }

        return true;
    }

    private static CssCompiledValue? ParseFilter(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
            {
                reader = probe;
                return new CssSlotActionValue(static slots => slots.SetFilter(null));
            }

            return null;
        }

        var effects = new List<Effect>();
        while (!reader.AtEnd)
        {
            if (!reader.TryReadFunction(out var fn, out var args))
            {
                return null;
            }

            if (fn.Equals("blur", StringComparison.OrdinalIgnoreCase))
            {
                if (!args.TryReadLength(out var radius) || !radius.IsAbsolute || !args.AtEnd)
                {
                    return null;
                }

                var pixels = radius.ToPxAbsolute();
                if (!double.IsFinite(pixels) || pixels < 0) return null;
                effects.Add(new BlurEffect { Radius = pixels });
            }
            else if (fn.Equals("drop-shadow", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSingleShadow(ref args, out var shadow) || !args.AtEnd ||
                    shadow is not DropShadowEffect)
                {
                    return null;
                }

                effects.Add(shadow);
            }
            else if (fn.Equals("hue-rotate", StringComparison.OrdinalIgnoreCase))
            {
                var degrees = 0.0;
                if (!args.AtEnd && (!args.TryReadNumber(out var angle, out var unit) ||
                    !CssUnitConversion.TryToDegrees(angle, unit, out degrees) || !args.AtEnd)) return null;
                effects.Add(ColorMatrixEffect.CreateHueRotation(degrees));
            }
            else if (fn.Equals("brightness", StringComparison.OrdinalIgnoreCase) || fn.Equals("contrast", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("grayscale", StringComparison.OrdinalIgnoreCase) || fn.Equals("invert", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("opacity", StringComparison.OrdinalIgnoreCase) || fn.Equals("saturate", StringComparison.OrdinalIgnoreCase) || fn.Equals("sepia", StringComparison.OrdinalIgnoreCase))
            {
                var amount = 1.0;
                if (!args.AtEnd)
                {
                    if (!args.TryReadNumber(out amount, out var unit) || unit is not (CssUnit.None or CssUnit.Percent) || !args.AtEnd) return null;
                    if (unit == CssUnit.Percent) amount /= 100;
                }
                if (!double.IsFinite(amount) || amount < 0) return null;
                var effect = fn.ToString().ToLowerInvariant() switch
                {
                    "brightness" => ColorMatrixEffect.CreateBrightness(amount), "contrast" => ColorMatrixEffect.CreateContrast(amount),
                    "grayscale" => ColorMatrixEffect.CreateGrayscale(amount), "invert" => ColorMatrixEffect.CreateInvert(amount),
                    "saturate" => ColorMatrixEffect.CreateSaturation(amount), "sepia" => ColorMatrixEffect.CreateSepia(amount),
                    _ => new ColorMatrixEffect(new ColorMatrix { M11 = 1, M22 = 1, M33 = 1, M44 = (float)Math.Clamp(amount, 0, 1) }),
                };
                effects.Add(effect);
            }
            else
            {
                // Per the CSS invalid-declaration rule the whole value is dropped.
                CssDiagnostics.Report(
                    "filter", CssDiagnosticReason.LossyConversion, null,
                    $"filter function '{fn.ToString()}' has no framework effect; declaration dropped");
                return null;
            }
        }

        if (effects.Count == 0)
        {
            return null;
        }

        var combined = Combine(effects);
        return new CssSlotActionValue(slots => slots.SetFilter(combined));
    }

    private static Effect Combine(List<Effect> effects)
    {
        if (effects.Count == 1)
        {
            return effects[0];
        }

        var group = new EffectGroup();
        foreach (var effect in effects)
        {
            group.Children.Add(effect);
        }

        return group;
    }

    private static object? CombineEffects(object? shadow, object? filter)
    {
        var group = new EffectGroup();
        Flatten(group, shadow);
        Flatten(group, filter);
        return group.Children.Count == 1 ? group.Children[0] : group;

        static void Flatten(EffectGroup group, object? value)
        {
            switch (value)
            {
                case EffectGroup nested:
                    foreach (var child in nested.Children)
                    {
                        group.Children.Add(child);
                    }

                    break;
                case Effect effect:
                    group.Children.Add(effect);
                    break;
            }
        }
    }

    private static void RegisterTextDecoration()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-decoration",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseTextDecoration(ref reader),
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-decoration-line",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) => ParseTextDecoration(ref reader),
        });
    }

    private static CssCompiledValue? ParseTextDecoration(ref CssTokenReader reader)
    {
        TextDecorationCollection? collection = null;
        var sawNone = false;
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident))
            {
                if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    sawNone = true;
                    reader = probe;
                    continue;
                }

                TextDecorationCollection? line = null;
                if (ident.Equals("underline", StringComparison.OrdinalIgnoreCase))
                {
                    line = TextDecorations.Underline;
                }
                else if (ident.Equals("overline", StringComparison.OrdinalIgnoreCase))
                {
                    line = TextDecorations.OverLine;
                }
                else if (ident.Equals("line-through", StringComparison.OrdinalIgnoreCase))
                {
                    line = TextDecorations.Strikethrough;
                }
                else if (ident.Equals("solid", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("double", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("dotted", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("dashed", StringComparison.OrdinalIgnoreCase) ||
                         ident.Equals("wavy", StringComparison.OrdinalIgnoreCase))
                {
                    CssDiagnostics.Report(
                        "text-decoration", CssDiagnosticReason.LossyConversion, null,
                        $"decoration style '{ident.ToString()}' is not supported and is ignored");
                    reader = probe;
                    continue;
                }
                else
                {
                    return null;
                }

                collection ??= new TextDecorationCollection();
                foreach (var decoration in line)
                {
                    collection.Add(decoration);
                }

                reader = probe;
                continue;
            }

            probe = reader;
            if (CssColorParser.TryParse(ref probe, out _, out _))
            {
                CssDiagnostics.Report(
                    "text-decoration", CssDiagnosticReason.LossyConversion, null,
                    "decoration colors are not supported and are ignored");
                reader = probe;
                continue;
            }

            return null;
        }

        if (sawNone && collection is null)
        {
            return new CssNamedValue("text-decoration", "TextDecorations", null);
        }

        if (collection is null)
        {
            return null;
        }

        return new CssNamedValue("text-decoration", "TextDecorations", collection);
    }
}
