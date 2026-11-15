namespace Jalium.UI.Styling;

/// <summary>Tracks parent brush changes for an explicitly inherited CSS color property.</summary>
internal sealed class CssInheritedColorObserver : IDisposable
{
    private readonly CssNode _parent;
    private readonly WeakReference<CssNode> _child;
    private readonly DependencyProperty _property;
    private readonly DependencyProperty? _fallbackProperty;
    private readonly DependencyProperty? _styleProperty;
    private readonly DependencyProperty? _autoColorProperty;
    private readonly DependencyProperty? _expressionProperty;
    private readonly Action<DependencyProperty, object?, object?> _handler;
    private bool _disposed;

    private CssInheritedColorObserver(CssNode child, CssNode parent, DependencyProperty property,
        DependencyProperty? fallbackProperty, string name)
    {
        _parent = parent;
        _child = new(child);
        _property = property;
        _fallbackProperty = fallbackProperty;
        if(name=="text-emphasis-color")_expressionProperty=CssTextEmphasis.ColorExpressionProperty;
        if (name == "outline-color")
        {
            _styleProperty = FrameworkElement.OutlineStyleProperty;
            _autoColorProperty = CssOutlineAutoColorProperties.AutoProperty;
        }
        _handler = Changed;
        parent.PropertyChangedInternal += _handler;
    }

    internal static void Observe(CssNode child, CssNode parent, string name)
    {
        var nativeName = name switch
        {
            "background-color" => "Background",
            "border-color" => "BorderBrush",
            "outline-color" => "OutlineBrush",
            _ => null,
        };
        if (child.CssRuntimeState is not { } state)
            return;
        var property = name switch
        {
            "column-rule-color" => CssMultiColumnProperties.RuleColorProperty,
            "text-decoration-color" => CssTextDecorationProperties.ColorProperty,
            "text-emphasis-color" => CssTextEmphasis.ColorProperty,
            _ => nativeName is null ? null : CssDependencyPropertyLookup.Find(parent.GetType(), nativeName),
        };
        if (property is null) return;
        var fallbackProperty = name is "text-decoration-color" or "text-emphasis-color"
            ? CssDependencyPropertyLookup.Find(parent.GetType(), "Foreground") : null;

        (state.ObservedInheritedColors ??= new(StringComparer.Ordinal)).Add(name);
        var observers = state.InheritedColorObservers ??= new(StringComparer.Ordinal);
        if (observers.TryGetValue(name, out var existing))
        {
            if (!existing._disposed && ReferenceEquals(existing._parent, parent) &&
                ReferenceEquals(existing._property, property) &&
                ReferenceEquals(existing._fallbackProperty, fallbackProperty))
                return;
            existing.Dispose();
        }
        observers[name] = new CssInheritedColorObserver(child, parent, property, fallbackProperty, name);
    }

    internal static void Finish(CssElementState state)
    {
        if (state.InheritedColorObservers is not { } observers) return;
        foreach (var name in observers.Keys.ToArray())
        {
            if (state.ObservedInheritedColors?.Contains(name) == true) continue;
            observers[name].Dispose();
            observers.Remove(name);
        }
        state.ObservedInheritedColors?.Clear();
    }

    private void Changed(DependencyProperty property, object? _, object? __)
    {
        if (!ReferenceEquals(property, _property) &&
            !ReferenceEquals(property, _fallbackProperty) &&
            !ReferenceEquals(property, _styleProperty) &&
            !ReferenceEquals(property, _expressionProperty) &&
            !ReferenceEquals(property, _autoColorProperty)) return;
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
    }
}
