namespace Jalium.UI.Controls;

/// <summary>Updates an items host without detaching containers that stay in place.</summary>
internal static class PanelChildReconciler
{
    internal static void Synchronize(UIElementCollection children, IReadOnlyList<UIElement> desired)
    {
        var retained = new HashSet<UIElement>(desired, ReferenceEqualityComparer.Instance);
        for (var index = children.Count - 1; index >= 0; --index)
            if (!retained.Contains(children[index])) children.RemoveAt(index);
        for (var index = 0; index < desired.Count; ++index)
        {
            var child = desired[index];
            if (index < children.Count && ReferenceEquals(children[index], child)) continue;
            var previous = children.IndexOf(child);
            if (previous >= 0) children.RemoveAt(previous);
            children.Insert(index, child);
        }
    }
}
