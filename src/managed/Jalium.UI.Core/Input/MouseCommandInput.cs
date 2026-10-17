using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>
/// Command ancestry for one native mouse packet. Raw mouse events still use
/// their physical route; commands visit omitted popup owners at that bridge.
/// </summary>
internal sealed class MouseCommandInput
{
    private readonly List<Visual> _route;
    private readonly List<Visual> _physicalRoute = [];
    private readonly Dictionary<UIElement, List<Visual>> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly UIElement _bindingScope;
    private readonly Func<bool> _isHostCurrent;

    internal UIElement Target { get; }
    internal UIElement? CommandScope { get; }

    internal MouseCommandInput(UIElement target, UIElement root, UIElement bindingScope,
        UIElement? commandScope, Func<bool> isHostCurrent)
    {
        Target = target;
        CommandScope = commandScope;
        _bindingScope = bindingScope;
        _isHostCurrent = isHostCurrent;
        _route = InputRoute.Capture(target, bindingScope);
        var physicalIndices = new Dictionary<Visual, int>(ReferenceEqualityComparer.Instance);
        for (Visual? node = target; node != null && _physicalRoute.Count < 4096; node = node.VisualParent)
        {
            if (!physicalIndices.TryAdd(node, _physicalRoute.Count)) break;
            _physicalRoute.Add(node);
            if (ReferenceEquals(node, root)) break;
        }
        if (_physicalRoute.Count == 0 || !ReferenceEquals(_physicalRoute[^1], root))
        { _physicalRoute.Clear(); return; }

        int previousPhysical = -1;
        foreach (var node in _route)
        {
            UIElement? receiver = null;
            if (physicalIndices.TryGetValue(node, out int physicalIndex))
            {
                previousPhysical = physicalIndex;
                receiver = node as UIElement;
            }
            else
            {
                // Wait until raw bubbling has left the shared child subtree.
                // Its control and instance handlers may consume ordinary clicks.
                for (int i = previousPhysical + 1; i < _physicalRoute.Count; i++)
                    if (_physicalRoute[i] is UIElement ancestor) { receiver = ancestor; break; }
            }
            if (node is not UIElement || receiver == null) continue;
            if (!_owners.TryGetValue(receiver, out var owners)) _owners[receiver] = owners = [];
            owners.Add(node);
        }
    }

    internal IReadOnlyList<Visual> OwnersAt(UIElement receiver) =>
        _owners.TryGetValue(receiver, out var owners) ? owners : [];

    internal bool IsHostCurrent()
    {
        if (!_isHostCurrent() || _physicalRoute.Count == 0) return false;
        for (int i = 0; i + 1 < _physicalRoute.Count; i++)
            if (!ReferenceEquals(_physicalRoute[i].VisualParent, _physicalRoute[i + 1])) return false;
        return true;
    }

    internal bool IsCurrent() => IsHostCurrent() && Target.IsEnabled && Target.IsVisible &&
        !Jalium.UI.Styling.CssDisplayProperties.IsExitInert(Target) &&
        InputRoute.IsCurrent(_route) && InputRoute.IsWithin(Target, _bindingScope);

    internal static void Raise(UIElement target, MouseEventArgs args, MouseCommandInput? input)
    {
        args.CommandInput = input;
        try { InputRoute.Raise(target, args, input == null ? null : input.IsCurrent); }
        finally { args.CommandInput = null; }
    }
}
