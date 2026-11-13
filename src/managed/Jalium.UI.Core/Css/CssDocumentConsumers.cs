using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Weak paint consumers for nonvisual content that can be displayed in several views.</summary>
internal static class CssDocumentConsumers
{
    private static readonly ConditionalWeakTable<DependencyObject, List<WeakReference<FrameworkElement>>> s_consumers = new();
    internal static void RootDpiChanged(Visual root)
    {
        // Root changes are rare. Resolve each weak consumer's current ancestry
        // here rather than retaining a root from its first, possibly detached,
        // measurement. A premeasured viewer may have since joined another tree.
        foreach (var pair in s_consumers)
        {
            pair.Value.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (pair.Value.Any(reference => reference.TryGetTarget(out var consumer) &&
                ReferenceEquals(VisualTreeHelper.GetRoot(consumer), root)))
                CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(pair.Key));
        }
    }

    internal static IDisposable Register(DependencyObject source, FrameworkElement consumer)
    {
        var oldScale = SourceScale(source);
        var list = s_consumers.GetOrCreateValue(source);
        var reference = new WeakReference<FrameworkElement>(consumer);
        list.Add(reference);
        if (oldScale != SourceScale(source)) CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(source));
        return new Registration(source, list, reference);
    }

    private static double SourceScale(DependencyObject source)
        => CssNode.Get(source).VisualOwner is { } visual ? Scale(visual) : 1;

    internal static Visual? Preferred(DependencyObject source, Visual? candidate)
    {
        if (!s_consumers.TryGetValue(source, out var list)) return candidate;
        // Computed style belongs to the shared document. Select the sharpest live
        // consumer's image-set candidate; all views retain the same CSS DIP sizes.
        var scale = candidate is null ? 0 : Scale(candidate);
        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (!list[index].TryGetTarget(out var consumer)) { list.RemoveAt(index); continue; }
            var current = Scale(consumer);
            if (current > scale) { candidate = consumer; scale = current; }
        }
        return candidate;
    }

    private static double Scale(Visual visual)
        => VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visual) as Visual ?? visual).DpiScaleY;

    internal static Visual Sharper(Visual? candidate, Visual other)
        => candidate is null || Scale(other) > Scale(candidate) ? other : candidate;

    internal static IEnumerable<FrameworkElement> EnumerateConsumers(DependencyObject source)
    {
        if (!s_consumers.TryGetValue(source, out var list)) yield break;
        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (list[index].TryGetTarget(out var consumer))
                yield return consumer;
        }
    }

    internal static void InvalidateConsumersInSubtree(DependencyObject subtreeRoot)
    {
        foreach (var pair in s_consumers)
        {
            pair.Value.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in pair.Value)
            {
                if (!reference.TryGetTarget(out var consumer)) continue;
                for (DependencyObject? current = consumer; current is not null;
                     current = current is Visual v ? VisualTreeHelper.GetParent(v) : null)
                {
                    if (ReferenceEquals(current, subtreeRoot))
                    {
                        CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(pair.Key));
                        if (pair.Key is Jalium.UI.Documents.FlowDocument doc)
                            doc.NotifyPaginationStyleChanged();
                        break;
                    }
                }
            }
        }
    }

    internal static void Invalidate(CssNode source)
    {
        var seen = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        for (var node = source; node is not null; node = node.FrameworkParent)
        {
            if (node.Target is UIElement owner && seen.Add(owner)) owner.InvalidateVisual();
            if (!s_consumers.TryGetValue(node.Target, out var list)) continue;
            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (!list[index].TryGetTarget(out var consumer)) { list.RemoveAt(index); continue; }
                if (seen.Add(consumer)) consumer.InvalidateVisual();
            }
        }
    }

    private sealed class Registration(DependencyObject source, List<WeakReference<FrameworkElement>> list,
        WeakReference<FrameworkElement> reference) : IDisposable
    {
        private DependencyObject? _source = source;
        public void Dispose()
        {
            if (_source is not { } previous) return;
            _source = null;
            var oldScale = SourceScale(previous);
            list.Remove(reference);
            if (oldScale != SourceScale(previous)) CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(previous));
        }
    }
}
