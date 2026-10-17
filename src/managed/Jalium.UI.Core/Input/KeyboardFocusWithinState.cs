using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>Tracks focus ancestry even when an overlay's input owner or visual parent changes.</summary>
internal static class KeyboardFocusWithinState
{
    // A removed popup must not retain its content until the next focus change.
    private static List<WeakReference<UIElement>> s_tracked = [];
    private static long s_generation;

    internal static void Update(UIElement? oldFocus, UIElement? newFocus, IEnumerable<UIElement>? oldAncestors = null)
    {
        var old = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        foreach (var reference in s_tracked)
            if (reference.TryGetTarget(out var element)) old.Add(element);
        old.UnionWith(Collect(oldFocus));
        if (oldAncestors != null) old.UnionWith(oldAncestors);

        var live = Collect(newFocus);
        var liveSet = new HashSet<UIElement>(live, ReferenceEqualityComparer.Instance);
        var generation = unchecked(++s_generation);
        // Keep both sets until callbacks finish, so a reentrant update or an
        // exception can still clear every flag that may have been set.
        s_tracked = old.Concat(live).Distinct<UIElement>(ReferenceEqualityComparer.Instance)
            .Select(element => new WeakReference<UIElement>(element)).ToList();
        foreach (var element in old)
        {
            if (generation != s_generation) return;
            if (!liveSet.Contains(element)) element.UpdateIsKeyboardFocusWithin(false);
        }
        foreach (var element in live)
        {
            if (generation != s_generation) return;
            element.UpdateIsKeyboardFocusWithin(true);
        }
        if (generation == s_generation)
            s_tracked = live.Select(element => new WeakReference<UIElement>(element)).ToList();
    }

    private static List<UIElement> Collect(UIElement? focus)
    {
        var result = new List<UIElement>();
        var elements = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        Walk(input: false);
        Walk(input: true);
        return result;

        void Walk(bool input)
        {
            var visited = new HashSet<Visual>(ReferenceEqualityComparer.Instance);
            for (Visual? current = focus; current != null && visited.Count < 4096 && visited.Add(current);
                current = input ? InputRoute.GetParent(current) : current.VisualParent)
                if (current is UIElement element && elements.Add(element)) result.Add(element);
        }
    }
}
