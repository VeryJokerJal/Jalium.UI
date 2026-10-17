using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>Connects an input surface to an owner outside its visual subtree.</summary>
internal interface IInputRouteParentProvider
{
    Visual? InputRouteParent { get; }
}

internal static class InputRoute
{
    internal static void Raise(UIElement target, RoutedEventArgs args, Func<bool>? lifetime)
    {
        args.CommandInputLifetime = lifetime;
        try { target.RaiseEvent(args); }
        finally { args.CommandInputLifetime = null; }
    }

    internal static Visual? GetParent(Visual node) =>
        node is IInputRouteParentProvider provider ? provider.InputRouteParent : node.VisualParent;

    internal static List<Visual> Capture(Visual element, Visual boundary)
    {
        List<Visual> route = [];
        HashSet<Visual> visited = [];
        for (Visual? current = element; current != null && route.Count < 4096; current = GetParent(current))
        {
            if (!visited.Add(current)) return [];
            route.Add(current);
            if (ReferenceEquals(current, boundary)) return route;
        }
        return [];
    }

    internal static bool IsCurrent(IReadOnlyList<Visual> route)
    {
        if (route.Count == 0) return false;
        for (int i = 0; i + 1 < route.Count; i++)
            if (!ReferenceEquals(GetParent(route[i]), route[i + 1])) return false;
        return true;
    }

    internal static bool IsWithin(Visual element, Visual boundary)
    {
        int depth = 0;
        bool checkExit = true;
        for (Visual? current = element; current != null && depth++ < 4096;)
        {
            if (current is UIElement ui)
            {
                if (!ui.IsEnabled || ui.Visibility != Visibility.Visible ||
                    checkExit && Styling.CssDisplayProperties.IsExitInert(ui)) return false;
                checkExit = false;
            }
            if (ReferenceEquals(current, boundary)) return true;
            var parent = GetParent(current);
            if (!ReferenceEquals(parent, current.VisualParent)) checkExit = true;
            current = parent;
        }
        return false;
    }
}
