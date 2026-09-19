namespace Jalium.UI.Styling;

[Flags]
internal enum CssContainerDependency { None = 0, Width = 1, Height = 2, Style = 4, Both = Width | Height }

/// <summary>Weak subscriptions prevent long-lived containers from retaining removed controls.</summary>
internal sealed class CssContainerDependent(CssNode node)
{
    private readonly WeakReference<CssNode> _node = new(node);
    private readonly HashSet<CssQueryContainer> _sources = [];

    internal void Reset()
    {
        foreach (var source in _sources) source.Remove(this);
        _sources.Clear();
    }

    internal void Observe(CssQueryContainer source, CssContainerDependency features)
    {
        _sources.Add(source);
        source.Add(this, features);
    }

    internal void Invalidate()
    {
        if (_node.TryGetTarget(out var target)) CssEvaluationScheduler.InvalidateElement(target);
    }
}

internal sealed class CssQueryContainer
{
    private sealed class Subscription(CssContainerDependent dependent, CssContainerDependency features)
    {
        internal readonly WeakReference<CssContainerDependent> Dependent = new(dependent);
        internal CssContainerDependency Features = features;
    }

    private readonly WeakReference<CssNode> _node;
    private readonly List<Subscription> _subscriptions = [];
    internal Size ContentSize { get; private set; }
    internal bool HasBox { get; private set; }

    internal CssQueryContainer(CssNode node)
    {
        _node = new(node);
        RefreshSize();
        node.SizeChanged += (_, _) => RefreshSize();
        node.PropertyChangedInternal += (property, _, _) =>
        {
            if (property == UIElement.VisibilityProperty) RefreshSize();
            if (property.Name is "FontSize" or "FontFamily" or "FontWeight" or "FontStyle" or "FlowDirection")
                Notify(CssContainerDependency.Both | CssContainerDependency.Style);
        };
    }

    internal void RefreshSize()
    {
        var size = default(Size);
        var hasBox = false;
        if (_node.TryGetTarget(out var node) && node.Target is FrameworkElement element && element.Visibility != Visibility.Collapsed)
        {
            hasBox = true;
            for (var parent = node.FrameworkParent; parent is not null; parent = parent.FrameworkParent)
                if (parent.Target is UIElement { Visibility: Visibility.Collapsed }) { hasBox = false; break; }
            var basis = element.CssLayout?.ContainingWidthCache ?? element.LastCssArrangeBox?.ContainingBlock.Width ?? element.RenderSize.Width;
            size = CssBoxMetrics.InnerSize(element.RenderSize, CssContainerProperties.Insets(element, basis));
        }
        var changed = HasBox != hasBox ? CssContainerDependency.Both : CssContainerDependency.None;
        if (size.Width != ContentSize.Width) changed |= CssContainerDependency.Width;
        if (size.Height != ContentSize.Height) changed |= CssContainerDependency.Height;
        ContentSize = size; HasBox = hasBox;
        if (changed != CssContainerDependency.None) Notify(changed);
    }

    internal void Add(CssContainerDependent dependent, CssContainerDependency features)
    {
        foreach (var subscription in _subscriptions)
            if (subscription.Dependent.TryGetTarget(out var target) && ReferenceEquals(target, dependent))
            { subscription.Features |= features; return; }
        _subscriptions.Add(new(dependent, features));
    }

    internal void Remove(CssContainerDependent dependent)
        => _subscriptions.RemoveAll(s => !s.Dependent.TryGetTarget(out var target) || ReferenceEquals(target, dependent));

    internal void Notify(CssContainerDependency features)
    {
        for (var i = _subscriptions.Count - 1; i >= 0; i--)
        {
            var subscription = _subscriptions[i];
            if (!subscription.Dependent.TryGetTarget(out var target)) _subscriptions.RemoveAt(i);
            else if ((subscription.Features & features) != 0) target.Invalidate();
        }
    }
}

internal static class CssContainerQueries
{
    internal static CssContainerDependent Dependent(CssNode node)
        => (node.CssRuntimeState ??= new()).ContainerDependent ??= new(node);

    internal static CssQueryContainer Container(CssNode node)
    {
        var container = (node.CssRuntimeState ??= new()).QueryContainer ??= new(node);
        container.RefreshSize();
        return container;
    }

    internal static CssNode? Find(CssNode element, CssContainerDependency features, string? name = null)
    {
        var depth = 0;
        for (var current = CssMatcher.CssAncestor(element); current is not null && depth++ < 4096; current = CssMatcher.CssAncestor(current))
        {
            if (name is not null && !CssContainerProperties.HasName(current, name)) continue;
            var axes = features & CssContainerDependency.Both;
            if (axes != 0)
            {
                if (current.Target is not FrameworkElement) continue;
                var type = CssContainerProperties.Type(current.Target);
                if (type == CssContainerType.Normal || (axes & CssContainerDependency.Height) != 0 && type != CssContainerType.Size) continue;
            }
            return current;
        }
        return null;
    }

    internal static void CustomPropertiesChanged(CssNode node, IReadOnlyDictionary<string, string>? previous, IReadOnlyDictionary<string, string> current)
    {
        if ((previous?.Count ?? 0) == current.Count && current.All(pair => previous is not null && previous.TryGetValue(pair.Key, out var value) && value == pair.Value)) return;
        node.CssRuntimeState?.QueryContainer?.Notify(CssContainerDependency.Style | CssContainerDependency.Both);
        // A query can change inherited tokens during a self-only evaluation. Propagate
        // that new computed map before descendants substitute var() or run style queries.
        foreach (var child in node.EnumerateChildren()) CssEvaluationScheduler.InvalidateSubtree(child);
    }
}

/// <summary>Immutable metric snapshots give retained layout expressions stable equality across style passes.</summary>
internal sealed record CssContainerUnitContext(
    CssQueryContainer? WidthContainer, CssQueryContainer? HeightContainer,
    CssContainerDependent Dependent, double Width, double Height)
{
    internal static CssContainerUnitContext Create(CssNode node, CssNode dependent, double viewportWidth, double viewportHeight)
    {
        var widthNode = CssContainerQueries.Find(node, CssContainerDependency.Width);
        var heightNode = CssContainerQueries.Find(node, CssContainerDependency.Height);
        var width = widthNode is null ? null : CssContainerQueries.Container(widthNode);
        var height = heightNode is null ? null : CssContainerQueries.Container(heightNode);
        return new(width, height, CssContainerQueries.Dependent(dependent),
            width is { HasBox: true } ? width.ContentSize.Width : viewportWidth,
            height is { HasBox: true } ? height.ContentSize.Height : viewportHeight);
    }

    internal double Resolve(CssUnit unit)
    {
        if (unit is not (CssUnit.Cqh or CssUnit.Cqb) && WidthContainer is not null) Dependent.Observe(WidthContainer, CssContainerDependency.Width);
        if (unit is not (CssUnit.Cqw or CssUnit.Cqi) && HeightContainer is not null) Dependent.Observe(HeightContainer, CssContainerDependency.Height);
        return unit switch
        {
            CssUnit.Cqw or CssUnit.Cqi => Width, CssUnit.Cqh or CssUnit.Cqb => Height,
            CssUnit.Cqmin => Math.Min(Width, Height), _ => Math.Max(Width, Height),
        };
    }
}
