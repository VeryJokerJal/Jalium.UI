using Jalium.UI.Media;

namespace Jalium.UI.Input;

internal interface IKeyboardFocusHost
{
    // Querying navigation candidates must not select a native input surface.
    bool IsKeyboardFocusTargetEligible(UIElement target) => true;
    bool TryAcquireKeyboardFocus(UIElement target);
}

internal static class KeyboardFocusHost
{
    internal static bool IsTargetEligible(UIElement target) =>
        Find(target)?.IsKeyboardFocusTargetEligible(target) != false;

    internal static IKeyboardFocusHost? Find(UIElement target)
    {
        var visited = new HashSet<Visual>();
        for (Visual? node = target; node != null && visited.Add(node); node = node.VisualParent)
            if (node is IKeyboardFocusHost host) return host;
        return null;
    }
}
