namespace Jalium.UI.Styling;

internal enum CssMathStyle : byte
{
    Normal,
    Compact,
}

/// <summary>Inherited MathML Core styling used by font-size: math.</summary>
internal static class CssMathProperties
{
    internal static readonly DependencyProperty DepthProperty = DependencyProperty.RegisterAttached(
        "CssMathDepth", typeof(int), typeof(CssMathProperties),
        new PropertyMetadata(0, Changed, null, inherits: true));

    internal static readonly DependencyProperty StyleProperty = DependencyProperty.RegisterAttached(
        "CssMathStyle", typeof(CssMathStyle), typeof(CssMathProperties),
        new PropertyMetadata(CssMathStyle.Normal, Changed, null, inherits: true));

    internal static CssCompiledValue? ParseDepth(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && probe.AtEnd && ident.Equals("auto-add", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            return new CssDeferredValue("math-depth", DepthProperty,
                (in CssApplyContext context, out object? value) =>
                {
                    var parent = context.Element.FrameworkParent;
                    var depth = parent?.GetValue(DepthProperty) is int inherited ? inherited : 0;
                    var compact = parent?.GetValue(StyleProperty) is CssMathStyle.Compact;
                    value = ClampDepth((long)depth + (compact ? 1 : 0));
                    return true;
                });
        }

        probe = reader;
        if (probe.TryReadFunction(out var name, out var arguments) &&
            name.Equals("add", StringComparison.OrdinalIgnoreCase) &&
            arguments.TryReadInteger(out var amount) && arguments.AtEnd && probe.AtEnd)
        {
            reader = probe;
            return new CssDeferredValue("math-depth", DepthProperty,
                (in CssApplyContext context, out object? value) =>
                {
                    var parent = context.Element.FrameworkParent;
                    var depth = parent?.GetValue(DepthProperty) is int inherited ? inherited : 0;
                    value = ClampDepth((long)depth + amount);
                    return true;
                });
        }

        probe = reader;
        if (!probe.TryReadInteger(out var absolute) || !probe.AtEnd) return null;
        reader = probe;
        return new CssImmediateValue(DepthProperty, absolute);
    }

    internal static CssCompiledValue? ParseStyle(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
        var style = ident.ToString().ToLowerInvariant() switch
        {
            "normal" => CssMathStyle.Normal,
            "compact" => CssMathStyle.Compact,
            _ => (CssMathStyle?)null,
        };
        return style is { } value ? new CssImmediateValue(StyleProperty, value) : null;
    }

    internal static int ComputedDepth(CssNode element, CssCompiledValue? declaration, CssLengthContext lengths)
    {
        if (element.HasLocalOrAnimatedValue(DepthProperty) && element.GetValue(DepthProperty) is int local)
            return local;

        var parent = element.FrameworkParent;
        var inherited = parent?.GetValue(DepthProperty) is int depth ? depth : 0;
        if (declaration is null) return inherited;

        var sink = new CssEngine.CssSetterCollector();
        var context = new CssApplyContext(element, lengths, new CssSlotAccumulator(), inherited);
        return declaration.TryApply(in context, sink) &&
            sink.Values.TryGetValue(DepthProperty, out var applied) && applied.Value is int computed
            ? computed : inherited;
    }

    internal static double FontSize(CssNode element, int depth, in CssLengthContext lengths)
    {
        var inheritedSize = lengths.InheritedFontSize;
        if (inheritedSize == 0) return 0;
        var parent = element.FrameworkParent;
        var inheritedDepth = parent?.GetValue(DepthProperty) is int value ? value : 0;
        if (inheritedDepth == depth) return inheritedSize;

        var fonts = lengths.Fonts ?? CssFontContext.Initial;
        fonts.Dependency?.Observe();
        var constants = fonts.Parent.MathConstants(fonts.Dependency);
        var lower = inheritedDepth;
        var upper = depth;
        var invert = upper < lower;
        if (invert) (lower, upper) = (upper, lower);
        var remaining = (long)upper - lower;
        var scale = 1.0;
        if (constants.HasMathTable)
        {
            var script = constants.ScriptPercentScaleDown > 0 ? constants.ScriptPercentScaleDown : .71;
            var scriptScript = constants.ScriptScriptPercentScaleDown > 0
                ? constants.ScriptScriptPercentScaleDown : .5041;
            if (lower <= 0 && upper >= 2)
            {
                scale *= scriptScript;
                remaining -= 2;
            }
            else if (lower == 1)
            {
                scale *= scriptScript / script;
                remaining--;
            }
            else if (upper == 1)
            {
                scale *= script;
                remaining--;
            }
        }
        scale *= Math.Pow(.71, remaining);
        return inheritedSize * (invert ? 1 / scale : scale);
    }

    private static int ClampDepth(long depth) => (int)Math.Clamp(depth, int.MinValue, int.MaxValue);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is FrameworkElement or FrameworkContentElement)
            CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(target));
    }
}
