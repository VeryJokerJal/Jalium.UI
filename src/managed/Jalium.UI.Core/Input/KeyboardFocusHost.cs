using Jalium.UI.Media;

namespace Jalium.UI.Input;

internal interface IKeyboardFocusHost
{
    bool TryAcquireKeyboardFocus(UIElement target);
}

internal static class KeyboardFocusHost
{
    internal static IKeyboardFocusHost? Find(UIElement target)
    {
        var visited = new HashSet<Visual>();
        for (Visual? node = target; node != null && visited.Add(node); node = node.VisualParent)
            if (node is IKeyboardFocusHost host) return host;
        return null;
    }
}
