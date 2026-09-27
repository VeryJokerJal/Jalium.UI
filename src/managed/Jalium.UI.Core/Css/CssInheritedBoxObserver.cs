namespace Jalium.UI.Styling;

/// <summary>Re-evaluates selected explicit inherit values when their parent input changes.</summary>
internal sealed class CssInheritedBoxObserver : IDisposable
{
    private readonly CssNode _parent;
    private readonly WeakReference<CssNode> _child;
    private readonly DependencyProperty _property;
    private readonly bool _observeBorderPaint;
    private readonly bool _observeBorderStyle;
    private readonly bool _observeDisplay;
    private readonly Action<DependencyProperty, object?, object?> _handler;
    private readonly Action? _radiusHandler;
    private bool _disposed;

    private CssInheritedBoxObserver(CssNode child, CssNode parent, DependencyProperty property,
        bool observeBorderPaint, bool observeBorderStyle, bool observeDisplay)
    {
        _parent = parent;
        _child = new(child);
        _property = property;
        _observeBorderPaint = observeBorderPaint;
        _observeBorderStyle = observeBorderStyle;
        _observeDisplay = observeDisplay;
        _handler = Changed;
        parent.PropertyChangedInternal += _handler;
        if (property.PropertyType == typeof(CornerRadius))
        {
            _radiusHandler = RadiusChanged;
            parent.Target.CssCornerRadiusPresentationChanged += _radiusHandler;
        }
    }

    internal static void Observe(CssNode child, CssNode parent, string name)
    {
        var directProperty = name switch
        {
            "white-space-collapse" => CssFlowProperties.WhiteSpaceProperty,
            "text-wrap-mode" => CssFlowProperties.TextWrapModeProperty,
            "text-wrap-style" => CssFlowProperties.TextWrapStyleProperty,
            "line-padding" => CssFlowProperties.LinePaddingProperty,
            "tab-size" => CssFlowProperties.TabSizeProperty,
            "hyphens" => CssFlowProperties.HyphensProperty,
            "hyphenate-character" => CssFlowProperties.HyphenateCharacterProperty,
            "hyphenate-limit-lines" => CssFlowProperties.HyphenateLimitLinesProperty,
            "hyphenate-limit-last" => CssFlowProperties.HyphenateLimitLastProperty,
            "hyphenate-limit-zone" => CssFlowProperties.HyphenateLimitZoneProperty,
            "hyphenate-limit-chars" => CssFlowProperties.HyphenateLimitCharsProperty,
            "white-space-trim" => CssFlowProperties.WhiteSpaceTrimProperty,
            "text-transform" => CssFlowProperties.TextTransformProperty,
            "text-autospace" => CssFlowProperties.TextAutospaceProperty,
            "hanging-punctuation" => CssFlowProperties.HangingPunctuationProperty,
            "word-space-transform" => CssFlowProperties.WordSpaceTransformProperty,
            "image-rendering" => CssImageRenderingProperties.ValueProperty,
            "user-select" => CssUserSelectProperties.ValueProperty,
            "text-underline-offset" => CssTextDecorationProperties.UnderlineOffsetProperty,
            "text-underline-position" => CssTextDecorationProperties.UnderlinePositionProperty,
            "text-decoration-skip-inset" => CssTextDecorationProperties.SkipInsetProperty,
            "text-shadow" => CssTextShadowProperties.ValueProperty,
            "display" => CssDisplayProperties.SpecificationProperty,
            _ => null,
        };
        var nativeName = name switch
        {
            "width" or "inline-size" => "Width",
            "height" or "block-size" => "Height",
            "min-width" or "min-inline-size" => "MinWidth",
            "min-height" or "min-block-size" => "MinHeight",
            "max-width" or "max-inline-size" => "MaxWidth",
            "max-height" or "max-block-size" => "MaxHeight",
            _ => null,
        };
        nativeName ??= name.StartsWith("margin-", StringComparison.Ordinal) ? "Margin" :
            name.StartsWith("padding-", StringComparison.Ordinal) ? "Padding" :
            name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-width", StringComparison.Ordinal)
                ? "BorderThickness" : CssLogicalBoxEdges.IsCorner(name) ? "CornerRadius" :
                CssLogicalBoxEdges.IsBorderColor(name) ? "BorderBrush" :
                CssLogicalBoxEdges.IsBorderStyle(name) ? "BorderThickness" : null;
        var observeBorderPaint = CssLogicalBoxEdges.IsBorderColor(name);
        var observeBorderStyle = CssLogicalBoxEdges.IsBorderStyle(name);
        var observeDisplay = name == "display";
        if ((directProperty is null && nativeName is null) || child.CssRuntimeState is not { } state ||
            (directProperty ?? CssDependencyPropertyLookup.Find(parent.GetType(), nativeName!) ??
                (observeBorderPaint ? CssBorderPaintProperties.ValueProperty :
                    observeBorderStyle ? CssBorderStyleProperties.ValueProperty : null)) is not { } property)
            return;

        (state.ObservedInheritedBoxes ??= new(StringComparer.Ordinal)).Add(name);
        var observers = state.InheritedBoxObservers ??= new(StringComparer.Ordinal);
        if (observers.TryGetValue(name, out var existing))
        {
            if (!existing._disposed && ReferenceEquals(existing._parent, parent) &&
                ReferenceEquals(existing._property, property) &&
                existing._observeBorderPaint == observeBorderPaint &&
                existing._observeBorderStyle == observeBorderStyle &&
                existing._observeDisplay == observeDisplay) return;
            existing.Dispose();
        }
        observers[name] = new CssInheritedBoxObserver(child, parent, property,
            observeBorderPaint, observeBorderStyle, observeDisplay);
    }

    internal static void Finish(CssElementState state)
    {
        if (state.InheritedBoxObservers is not { } observers) return;
        foreach (var name in observers.Keys.ToArray())
        {
            if (state.ObservedInheritedBoxes?.Contains(name) == true) continue;
            observers[name].Dispose();
            observers.Remove(name);
        }
        state.ObservedInheritedBoxes?.Clear();
    }

    private void Changed(DependencyProperty property, object? _, object? __)
    {
        if (!ReferenceEquals(property, _property) &&
            !ReferenceEquals(property, FrameworkElement.FlowDirectionProperty) &&
            !(_observeBorderPaint && ReferenceEquals(property, CssBorderPaintProperties.ValueProperty)) &&
            !(_observeBorderStyle && ReferenceEquals(property, CssBorderStyleProperties.ValueProperty)) &&
            !(_observeDisplay && ReferenceEquals(property, CssFloatProperties.FloatProperty))) return;
        InvalidateChild();
    }

    private void RadiusChanged() => InvalidateChild();

    private void InvalidateChild()
    {
        if (!_child.TryGetTarget(out var child)) { Dispose(); return; }
        for (var current = CssMatcher.CssAncestor(child); current is not null;
             current = CssMatcher.CssAncestor(current))
        {
            if (!ReferenceEquals(current, _parent)) continue;
            CssEvaluationScheduler.InvalidateSubtree(child);
            return;
        }
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _parent.PropertyChangedInternal -= _handler;
        if (_radiusHandler is not null)
            _parent.Target.CssCornerRadiusPresentationChanged -= _radiusHandler;
    }
}
