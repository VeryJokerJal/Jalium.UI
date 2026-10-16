using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Controls.Automation.MacOS;

internal sealed unsafe partial class MacOSAccessibilityTree
{
    private static UIElement? GetActionTarget(AutomationPeer peer)
    {
        HashSet<AutomationPeer> seen = [];
        for (AutomationPeer? current = peer; current != null && seen.Count < 4096 && seen.Add(current);
            current = current.GetParent())
            if (current.Owner is UIElement element) return element;
        return null;
    }

    private bool CanMutate(AutomationPeer peer) => !_disposed && IsVisible(peer) && peer.IsEnabled() &&
        GetActionTarget(peer) is { } target && _window.IsMacOSAccessibilityTargetEnabled(target);

    private Func<bool> CaptureActionLifetime(AutomationPeer peer)
    {
        var target = GetActionTarget(peer);
        if (target == null) return () => false;
        var windowLifetime = _window.CaptureMacOSAccessibilityLifetime(target);
        // Nonvisual providers can be detached or reparented independently of
        // their nearest UIElement. Preserve that provider ancestry as well.
        List<AutomationPeer> route = [];
        HashSet<AutomationPeer> seen = [];
        for (AutomationPeer? current = peer; current != null && route.Count < 4096 && seen.Add(current);
            current = current.GetParent())
        {
            route.Add(current);
            if (ReferenceEquals(current.Owner, target)) break;
        }
        return () => CanMutate(peer) && IsProviderRouteCurrent() && !_disposed && windowLifetime();

        bool IsProviderRouteCurrent()
        {
            if (route.Count == 0 || !ReferenceEquals(route[^1].Owner, target)) return false;
            for (int index = 0; index + 1 < route.Count; index++)
                if (!ReferenceEquals(route[index].GetParent(), route[index + 1])) return false;
            return true;
        }
    }
}
