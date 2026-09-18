using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

/// <summary>Receives the (dp, value) pairs produced by applying CSS declarations to one element.</summary>
internal interface ICssSetterSink
{
    /// <summary>While true, subsequent sets belong to a state-rule winner (CssState layer).</summary>
    bool CurrentValueIsState { get; set; }

    void Set(DependencyProperty property, object? value);

    /// <summary>Receives the layout-state snapshot built by the slot accumulator's flush.</summary>
    void SetLayoutState(CssLayoutState state);
}

/// <summary>Compile-time context for value parsing (no element available yet).</summary>
internal sealed class CssCompileContext
{
    public Uri? BaseUri;
    internal CssLengthContext? NumericLengths;

    public static readonly CssCompileContext Default = new();
}

/// <summary>Per-element context threaded through <see cref="CssCompiledValue.TryApply"/>.</summary>
internal readonly struct CssApplyContext
{
    private static readonly Jalium.UI.Media.SolidColorBrush s_black = CreateBlack();
    private static Jalium.UI.Media.SolidColorBrush CreateBlack()
    {
        var brush = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(0, 0, 0));
        brush.Freeze();
        return brush;
    }
    public readonly CssNode Element;
    public readonly CssLengthContext Lengths;
    public readonly CssSlotAccumulator Slots;
    public Jalium.UI.Media.Brush CurrentColor => Slots.ForegroundBrush ??
        (CssDependencyPropertyLookup.Find(Element.GetType(), "Foreground") is { } dp && Element.GetValue(dp) is Jalium.UI.Media.SolidColorBrush brush
            ? brush : s_black);

    public CssApplyContext(CssNode element, CssLengthContext lengths, CssSlotAccumulator slots)
    {
        Element = element;
        Lengths = lengths;
        Slots = slots;
    }
}

/// <summary>A longhand declaration after compilation: cascade merging keys on <see cref="Name"/>.</summary>
internal readonly struct CssCompiledDeclaration
{
    public readonly string Name;
    public readonly CssCompiledValue Value;
    public readonly bool Important;

    public CssCompiledDeclaration(string name, CssCompiledValue value, bool important)
    {
        Name = name;
        Value = value;
        Important = important;
    }

    public CssCompiledDeclaration WithImportant(bool important)
        => important == Important ? this : new CssCompiledDeclaration(Name, Value, important);
}

/// <summary>
/// An immutable, element-independent compiled declaration value. Instances are shared across
/// every element a rule matches; TryApply materializes the value for one element.
/// </summary>
internal abstract class CssCompiledValue
{
    /// <summary>Applies to one element. False means it cannot take effect there (diagnostic already reported).</summary>
    public abstract bool TryApply(in CssApplyContext context, ICssSetterSink sink);
}

/// <summary>Fixed DP reference(s) plus values converted at compile time. Zero apply-time cost.</summary>
internal sealed class CssImmediateValue : CssCompiledValue
{
    private readonly DependencyProperty[] _properties;
    private readonly object?[] _values;

    public CssImmediateValue(DependencyProperty property, object? value)
    {
        _properties = new[] { property };
        _values = new[] { value };
    }

    public CssImmediateValue(DependencyProperty[] properties, object?[] values)
    {
        _properties = properties;
        _values = values;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        for (var i = 0; i < _properties.Length; i++)
        {
            sink.Set(_properties[i], _values[i]);
        }

        return true;
    }
}

/// <summary>
/// Resolves the target DP by name against the element's runtime type (properties that live on
/// different owners: "Padding", "Background", "FontSize"...). Results, including negatives,
/// are cached per (type, name).
/// </summary>
internal sealed class CssNamedValue : CssCompiledValue
{
    private readonly string _cssName;
    private readonly string _dpName;
    private readonly object? _value;

    public CssNamedValue(string cssName, string dpName, object? value)
    {
        _cssName = cssName;
        _dpName = dpName;
        _value = value;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var dp = CssDependencyPropertyLookup.Find(context.Element.GetType(), _dpName);
        if (dp is null)
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                $"no dependency property '{_dpName}' on this element type; declaration skipped here");
            return false;
        }

        if (_value is not null && !dp.PropertyType.IsInstanceOfType(_value))
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                $"'{_dpName}' expects {dp.PropertyType.Name}, got {_value.GetType().Name}");
            return false;
        }

        sink.Set(dp, _value);
        return true;
    }
}

internal delegate bool CssDeferredEvaluator(in CssApplyContext context, out object? value);

/// <summary>
/// A value that needs the element context to materialize (em/rem, currentcolor, unitless
/// line-height). Target is either a fixed DP or resolved by name.
/// </summary>
internal sealed class CssDeferredValue : CssCompiledValue
{
    private readonly string _cssName;
    private readonly DependencyProperty? _property;
    private readonly string? _dpName;
    private readonly CssDeferredEvaluator _evaluate;

    public CssDeferredValue(string cssName, DependencyProperty property, CssDeferredEvaluator evaluate)
    {
        _cssName = cssName;
        _property = property;
        _evaluate = evaluate;
    }

    public CssDeferredValue(string cssName, string dpName, CssDeferredEvaluator evaluate)
    {
        _cssName = cssName;
        _dpName = dpName;
        _evaluate = evaluate;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var dp = _property ?? CssDependencyPropertyLookup.Find(context.Element.GetType(), _dpName!);
        if (dp is null)
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                $"no dependency property '{_dpName}' on this element type; declaration skipped here");
            return false;
        }

        if (!_evaluate(in context, out var value))
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.InvalidValue, context.Element.GetType(),
                "value could not be resolved in this element's context; declaration skipped here");
            return false;
        }

        sink.Set(dp, value);
        return true;
    }
}

/// <summary>
/// A percentage size (width/height/min/max): contributes to the layout-state slot AND
/// writes a sentinel (auto/0/+∞) into the target DP's CSS layer so precedence against
/// local values and layer-clearing fallback come for free.
/// </summary>
internal sealed class CssLayoutValue : CssCompiledValue
{
    private readonly CssLayoutSlotField _field;
    private readonly double _fraction;
    private readonly DependencyProperty _sentinelProperty;
    private readonly object _sentinelValue;

    public CssLayoutValue(CssLayoutSlotField field, double fraction, DependencyProperty sentinelProperty, object sentinelValue)
    {
        _field = field;
        _fraction = fraction;
        _sentinelProperty = sentinelProperty;
        _sentinelValue = sentinelValue;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        context.Slots.SetLayoutPercent(_field, _fraction);
        sink.Set(_sentinelProperty, _sentinelValue);
        return true;
    }
}

/// <summary>
/// One inset edge (left/top/right/bottom). Percent stays a fraction; other units resolve
/// at apply time. When position is static the slot flush falls back to the Canvas
/// compatibility DP for absolute pixels.
/// </summary>
internal sealed class CssExpressionLayoutValue(CssLayoutSlotField field, CssLength length,
    DependencyProperty property, object sentinel) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        context.Slots.SetLayoutLength(field, CssLayoutLength.Math(length.Expression!, context.Lengths));
        sink.Set(property, sentinel);
        return true;
    }
}

internal sealed class CssInsetValue : CssCompiledValue
{
    private readonly string _cssName;
    private readonly int _edge;
    private readonly CssLength _length;
    private readonly DependencyProperty? _staticCompatDp;

    public CssInsetValue(string cssName, int edge, CssLength length, DependencyProperty? staticCompatDp)
    {
        _cssName = cssName;
        _edge = edge;
        _length = length;
        _staticCompatDp = staticCompatDp;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        CssLayoutLength resolved;
        if (_length.Expression is { UsesPercent: true } expression)
        {
            resolved = CssLayoutLength.Math(expression, context.Lengths);
        }
        else if (_length.Unit == CssUnit.Percent)
        {
            resolved = CssLayoutLength.Percent(_length.Value / 100.0);
        }
        else if (_length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px))
        {
            resolved = CssLayoutLength.Px(px);
        }
        else
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.InvalidValue, context.Element.GetType(),
                "offset could not be resolved in this element's context; declaration skipped here");
            return false;
        }

        context.Slots.SetInset(_edge, resolved, _staticCompatDp);
        return true;
    }
}

/// <summary>Runs a compile-time-captured action against the element's slot accumulator.</summary>
internal sealed class CssSlotActionValue : CssCompiledValue
{
    private readonly Action<CssSlotAccumulator> _apply;

    public CssSlotActionValue(Action<CssSlotAccumulator> apply) => _apply = apply;

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        _apply(context.Slots);
        return true;
    }
}

/// <summary>Contributes one edge to a Thickness-shaped slot; the accumulator flushes the merged value.</summary>
internal sealed class CssSlotThicknessEdge : CssCompiledValue
{
    private readonly CssSlot _slot;
    private readonly int _edge;
    private readonly CssLength _length;

    public CssSlotThicknessEdge(CssSlot slot, int edge, CssLength length)
    {
        _slot = slot;
        _edge = edge;
        _length = length;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        context.Slots.SetThicknessEdge(_slot, _edge, _length);
        return true;
    }
}

/// <summary>
/// The kebab-case fallback channel: properties CSS has no keyword for map to a dependency
/// property by Pascal-cased name, with the raw value text converted through the XAML
/// converter stack (Style.StringValueConverter, registered by Jalium.UI.Xaml).
/// </summary>
internal sealed class CssFallbackValue : CssCompiledValue
{
    private readonly string _cssName;
    private readonly string _pascalName;
    private readonly string _rawValue;
    private readonly ConcurrentDictionary<Type, object?> _convertedCache = new();

    private static readonly object s_conversionFailed = new();

    public CssFallbackValue(string cssName, string pascalName, string rawValue)
    {
        _cssName = cssName;
        _pascalName = pascalName;
        _rawValue = rawValue;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var elementType = context.Element.GetType();
        var dp = CssDependencyPropertyLookup.Find(elementType, _pascalName);
        if (dp is null)
        {
            // Last resort before the unknown-property diagnostic: the interception
            // protocol lets custom elements consume names nothing else recognized.
            var setter = new CssDeclarationSetter(sink, context.Element, _cssName);
            if (context.Element.TryApplyCssPropertyCore(_cssName, _rawValue, in setter))
            {
                return true;
            }

            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.UnknownProperty, elementType,
                $"no matching CSS property and no dependency property '{_pascalName}' on this element type");
            return false;
        }

        var converted = _convertedCache.GetOrAdd(dp.PropertyType, Convert);
        if (ReferenceEquals(converted, s_conversionFailed))
        {
            CssDiagnostics.Report(
                _cssName, CssDiagnosticReason.InvalidValue, elementType,
                $"value '{_rawValue}' could not be converted to {dp.PropertyType.Name}");
            return false;
        }

        sink.Set(dp, converted);
        return true;
    }

    private object? Convert(Type targetType)
    {
        if (targetType == typeof(string))
        {
            return _rawValue;
        }

        // Built-in conversions keep the fallback channel functional even before the
        // Jalium.UI.Xaml converter stack is loaded; it covers everything else.
        if (targetType.IsEnum)
        {
            return Enum.TryParse(targetType, _rawValue, ignoreCase: true, out var parsed)
                ? parsed
                : s_conversionFailed;
        }

        if (targetType == typeof(bool))
        {
            return bool.TryParse(_rawValue, out var b) ? b : s_conversionFailed;
        }

        if (targetType == typeof(double))
        {
            if (_rawValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                _rawValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
            {
                return double.NaN;
            }

            return double.TryParse(_rawValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d
                : s_conversionFailed;
        }

        if (targetType == typeof(int))
        {
            return int.TryParse(_rawValue, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var i)
                ? i
                : s_conversionFailed;
        }

        var converter = Style.StringValueConverter;
        if (converter is null)
        {
            return s_conversionFailed;
        }

        try
        {
            var converted = converter(_rawValue, targetType);
            return converted is not null && targetType.IsInstanceOfType(converted) ? converted : s_conversionFailed;
        }
        catch
        {
            return s_conversionFailed;
        }
    }
}

/// <summary>AOT-safe DP-by-name lookup with per-(type, name) caching, negatives included.</summary>
internal static class CssDependencyPropertyLookup
{
    private static readonly ConcurrentDictionary<(Type, string), DependencyProperty?> s_cache = new();

    public static DependencyProperty? Find(Type ownerType, string name)
        => s_cache.GetOrAdd((ownerType, name), static key => DependencyProperty.FromName(key.Item1, key.Item2) ?? (key.Item2 switch
        {
            "Foreground" => Jalium.UI.Documents.TextElement.ForegroundProperty,
            "FontSize" => Jalium.UI.Documents.TextElement.FontSizeProperty,
            "FontFamily" => Jalium.UI.Documents.TextElement.FontFamilyProperty,
            "FontStyle" => Jalium.UI.Documents.TextElement.FontStyleProperty,
            "FontWeight" => Jalium.UI.Documents.TextElement.FontWeightProperty,
            "FontStretch" => Jalium.UI.Documents.TextElement.FontStretchProperty,
            "LineHeight" => Jalium.UI.Controls.TextBlock.LineHeightProperty,
            "TextAlignment" => Jalium.UI.Controls.TextBlock.TextAlignmentProperty,
            "TextWrapping" => Jalium.UI.Controls.TextBlock.TextWrappingProperty,
            _ => null,
        }));
}

internal sealed class CssCurrentColorValue(string property) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (property == "background-color") { context.Slots.SetBackgroundColor(context.CurrentColor); return true; }
        return new CssNamedValue(property, property == "outline-color" ? "OutlineBrush" : "BorderBrush", context.CurrentColor).TryApply(in context, sink);
    }
}
