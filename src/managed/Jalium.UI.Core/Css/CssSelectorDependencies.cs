using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Tracks selector reads beyond a stylesheet's host, including ancestor and sibling states.</summary>
internal static class CssSelectorDependencies
{
    private static readonly ConditionalWeakTable<CssNode, CssSelectorSource> s_sources = new();
    internal static CssSelectorDependent For(CssNode node) => (node.CssRuntimeState ??= new()).SelectorDependent ??= new(node);
    internal static CssSelectorSource Source(CssNode node) => s_sources.GetValue(node, static value => new(value));
    internal static void TreeChanged(CssNode node)
    {
        if (s_sources.TryGetValue(node, out var source)) source.Notify(null);
    }
}

internal sealed class CssSelectorDependent(CssNode node)
{
    private readonly WeakReference<CssNode> _node = new(node);
    private readonly HashSet<CssSelectorSource> _sources = [];
    internal void Reset() { foreach (var source in _sources) source.Remove(this); _sources.Clear(); }
    internal void Observe(CssNode source, string? property)
    {
        var observer = CssSelectorDependencies.Source(source); _sources.Add(observer); observer.Add(this, property);
    }
    internal void Invalidate() { if (_node.TryGetTarget(out var node)) CssEvaluationScheduler.InvalidateElement(node); }
}

internal sealed class CssSelectorSource
{
    private sealed class Subscription(CssSelectorDependent dependent)
    {
        internal readonly WeakReference<CssSelectorDependent> Dependent = new(dependent);
        internal readonly HashSet<string> Properties = new(StringComparer.Ordinal);
        internal bool Tree;
    }
    private readonly List<Subscription> _subscriptions = [];
    internal CssSelectorSource(CssNode source) => source.PropertyChangedInternal += (property, _, _) => Notify(property.Name);
    internal void Add(CssSelectorDependent dependent, string? property)
    {
        Subscription? found = null;
        foreach (var subscription in _subscriptions)
            if (subscription.Dependent.TryGetTarget(out var target) && ReferenceEquals(target,dependent)) {found=subscription; break;}
        if (found is null) {found=new(dependent); _subscriptions.Add(found);}
        if (property is null) found.Tree=true; else found.Properties.Add(property);
    }
    internal void Remove(CssSelectorDependent dependent) => _subscriptions.RemoveAll(s => !s.Dependent.TryGetTarget(out var target) || ReferenceEquals(target,dependent));
    internal void Notify(string? property)
    {
        for(var i=_subscriptions.Count-1;i>=0;i--)
        {
            var subscription=_subscriptions[i];
            if (!subscription.Dependent.TryGetTarget(out var target)) _subscriptions.RemoveAt(i);
            else if (property is null ? subscription.Tree : subscription.Properties.Contains(property)) target.Invalidate();
        }
    }
}
