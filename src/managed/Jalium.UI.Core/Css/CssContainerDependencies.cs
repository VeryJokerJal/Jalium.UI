using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

[Flags]
internal enum CssContainerDependency
{
    None = 0,
    Width = 1,
    Height = 2,
    Style = 4,
    ScrollX = 8,
    ScrollY = 16,
    ScrollAny = 32,
    Both = Width | Height,
    ScrollState = ScrollX | ScrollY | ScrollAny,
}

[Flags]
internal enum CssScrollableDirection : byte { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

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

    private sealed class StyleBrushSubscription : IDisposable
    {
        private readonly WeakReference<CssQueryContainer> _container;
        private readonly WeakReference<SolidColorBrush> _brush;
        private readonly EventHandler _handler;

        internal StyleBrushSubscription(CssQueryContainer container, SolidColorBrush brush)
        {
            _container = new(container);
            _brush = new(brush);
            _handler = Changed;
            brush.Changed += _handler;
        }

        internal bool Matches(SolidColorBrush brush)
            => _brush.TryGetTarget(out var observed) && ReferenceEquals(observed, brush);

        private void Changed(object? _, EventArgs __)
        {
            if (_container.TryGetTarget(out var container)) container.Notify(CssContainerDependency.Style);
            else Dispose();
        }

        public void Dispose()
        {
            if (_brush.TryGetTarget(out var brush)) brush.Changed -= _handler;
        }
    }

    private readonly WeakReference<CssNode> _node;
    private readonly List<Subscription> _subscriptions = [];
    private StyleBrushSubscription? _foregroundBrush;
    private StyleBrushSubscription? _backgroundBrush;
    internal Size ContentSize { get; private set; }
    internal bool HasBox { get; private set; }
    internal CssScrollableDirection ScrollableDirections { get; private set; }
    internal CssScrollableDirection ScrolledDirection { get; private set; }
    private CssContainerDependency _scrollAxes;

    internal CssQueryContainer(CssNode node)
    {
        _node = new(node);
        RefreshSize();
        RefreshScrollState();
        node.SizeChanged += (_, _) => RefreshSize();
        if (node.Target is ScrollViewer viewer)
        {
            viewer.ScrollChanged += (_, args) =>
            {
                if (ReferenceEquals(args.OriginalSource, viewer)) RefreshScrollState();
            };
            viewer.RelativeScrollChanged += (_, _) => RefreshScrollState();
        }
        node.PropertyChangedInternal += (property, _, _) =>
        {
            Notify(CssContainerDependency.Style);
            if (property == UIElement.VisibilityProperty) RefreshSize();
            if (property.Name is "FontSize" or "FontFamily" or "FontWeight" or "FontStyle" or "FlowDirection")
                Notify(CssContainerDependency.Both | CssContainerDependency.Style |
                    CssContainerDependency.ScrollState);
            if (property == ScrollViewer.HorizontalScrollBarVisibilityProperty ||
                property == ScrollViewer.VerticalScrollBarVisibilityProperty ||
                property == CssOverflowProperties.ValueProperty)
                RefreshScrollState();
        };
    }

    internal void RefreshScrollState()
    {
        var axes = CssContainerDependency.None;
        var directions = CssScrollableDirection.None;
        var scrolled = CssScrollableDirection.None;
        if (_node.TryGetTarget(out var node) && node.Target is ScrollViewer viewer)
        {
            axes = CssContainerQueries.ScrollAxes(viewer);
            scrolled = viewer.LastRelativeScrollDirection;
            const double tolerance = .001;
            if ((axes & CssContainerDependency.ScrollX) != 0 && viewer.ScrollableWidth > tolerance)
            {
                if (viewer.HorizontalOffset > tolerance) directions |= CssScrollableDirection.Left;
                if (viewer.ScrollableWidth - viewer.HorizontalOffset > tolerance)
                    directions |= CssScrollableDirection.Right;
            }
            if ((axes & CssContainerDependency.ScrollY) != 0 && viewer.ScrollableHeight > tolerance)
            {
                if (viewer.VerticalOffset > tolerance) directions |= CssScrollableDirection.Top;
                if (viewer.ScrollableHeight - viewer.VerticalOffset > tolerance)
                    directions |= CssScrollableDirection.Bottom;
            }
        }
        if (_scrollAxes == axes && ScrollableDirections == directions && ScrolledDirection == scrolled) return;
        _scrollAxes = axes;
        ScrollableDirections = directions;
        ScrolledDirection = scrolled;
        Notify(CssContainerDependency.ScrollState);
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

    internal void ObserveStyleBrush(string property, Brush? brush)
    {
        ref var subscription = ref (property == "color" ? ref _foregroundBrush : ref _backgroundBrush);
        if (brush is SolidColorBrush { IsFrozen: false } solid)
        {
            if (subscription?.Matches(solid) == true) return;
            subscription?.Dispose();
            subscription = new(this, solid);
        }
        else
        {
            subscription?.Dispose();
            subscription = null;
        }
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
    internal static CssContainerDependency ScrollAxes(ScrollViewer viewer)
    {
        var overflow = CssOverflowProperties.Get(viewer);
        var axes = CssContainerDependency.None;
        if (viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            overflow?.X is not (CssOverflowMode.Hidden or CssOverflowMode.Clip))
            axes |= CssContainerDependency.ScrollX;
        if (viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            overflow?.Y is not (CssOverflowMode.Hidden or CssOverflowMode.Clip))
            axes |= CssContainerDependency.ScrollY;
        return axes;
    }

    internal static CssContainerDependent Dependent(CssNode node)
        => (node.CssRuntimeState ??= new()).ContainerDependent ??= new(node);

    internal static CssQueryContainer Container(CssNode node)
    {
        var container = (node.CssRuntimeState ??= new()).QueryContainer ??= new(node);
        container.RefreshSize();
        return container;
    }

    internal static CssNode? Find(CssNode element, CssContainerDependency features, string? name = null,
        Func<CssNode, CssContainerDependency>? requirements = null)
    {
        var depth = 0;
        for (var current = CssMatcher.CssAncestor(element); current is not null && depth++ < 4096; current = CssMatcher.CssAncestor(current))
        {
            if (name is not null && !CssContainerProperties.HasName(current, name)) continue;
            var required = requirements?.Invoke(current) ?? features;
            var axes = required & CssContainerDependency.Both;
            if (axes != 0)
            {
                if (current.Target is not FrameworkElement) continue;
                var type = CssContainerProperties.Type(current.Target);
                if ((type & CssContainerType.InlineSize) == 0 ||
                    (axes & CssContainerDependency.Height) != 0 &&
                    (type & CssContainerType.BlockSize) == 0) continue;
            }
            var scroll = required & CssContainerDependency.ScrollState;
            if (scroll != 0)
            {
                if (current.Target is not ScrollViewer viewer ||
                    (CssContainerProperties.Type(viewer) & CssContainerType.ScrollState) == 0) continue;
                var available = ScrollAxes(viewer);
                if ((scroll & (CssContainerDependency.ScrollX | CssContainerDependency.ScrollY) & available) !=
                    (scroll & (CssContainerDependency.ScrollX | CssContainerDependency.ScrollY)) ||
                    (scroll & CssContainerDependency.ScrollAny) != 0 && available == CssContainerDependency.None)
                {
                    // A rejected inner scroller can become eligible without changing
                    // its container-type, so keep its dependent selection live.
                    Dependent(element).Observe(Container(current), features);
                    continue;
                }
            }
            return current;
        }
        return null;
    }

    internal static void CustomPropertiesChanged(CssNode node, IReadOnlyDictionary<string, string>? previous,
        IReadOnlyDictionary<string, string> current, IReadOnlySet<string>? previousTainted, IReadOnlySet<string>? currentTainted)
    {
        if ((previous?.Count ?? 0) == current.Count &&
            current.All(pair => previous is not null && previous.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
            (previousTainted?.Count ?? 0) == (currentTainted?.Count ?? 0) &&
            (currentTainted is null || currentTainted.All(name => previousTainted?.Contains(name) == true))) return;
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
