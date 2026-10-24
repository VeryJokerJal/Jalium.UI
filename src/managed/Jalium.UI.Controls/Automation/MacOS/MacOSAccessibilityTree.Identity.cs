using Jalium.UI.Automation.Peers;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Automation.MacOS;

internal sealed unsafe partial class MacOSAccessibilityTree
{
    internal bool Handle(ref MacOSAXRequest request)
    {
        // The retained child protocol owns immutable snapshots. Its read/release
        // opcodes must remain usable even after the owning item is recycled.
        if ((int)request.Operation is 16 or 17) return HandleCurrent(ref request);
        if (_disposed || !_peers.TryGetValue(request.NodeId, out var weak)
            || !weak.TryGetTarget(out var peer)
            || !_ids.TryGetValue(peer, out var identity) || identity.Value != request.NodeId
            || !HasCurrentIdentity(peer, identity)) return false;
        bool accepted = HandleCurrent(ref request);
        // A completed action can legitimately close its window or recycle its
        // row. Reads must never publish data obtained across that transition.
        bool writes = request.Operation is MacOSAXOperation.Action or MacOSAXOperation.SetValue
            or MacOSAXOperation.SetTextSelection or MacOSAccessibilityNative.SetSelectedItems
            || request.Operation == MacOSAXOperation.TextNavigation
                && request.Index is (int)MacOSAXTextNavigation.SetInsertionLine
                    or (int)MacOSAXTextNavigation.ReplaceSelection;
        if (!accepted || writes) return accepted;
        if (HasCurrentIdentity(peer, identity)) return true;
        if ((int)request.Operation == 15)
        {
            var release = request;
            release.Operation = (MacOSAXOperation)17;
            HandleCurrent(ref release);
        }
        return false;
    }

    private bool HasCurrentIdentity(AutomationPeer peer, NodeId identity) =>
        !_disposed && _ids.TryGetValue(peer, out var current)
        && ReferenceEquals(current, identity) && identity.Identity.IsCurrent(peer);

    private sealed class ItemIdentity
    {
        private readonly record struct Realization(WeakReference<ItemContainerGenerator> Generator,
            WeakReference<DependencyObject> Container, ulong Id);
        private readonly Realization[] _realizations;
        private readonly bool _valid;

        private ItemIdentity(Realization[] realizations, bool valid)
        { _realizations = realizations; _valid = valid; }

        internal static ItemIdentity Capture(AutomationPeer peer)
        {
            var current = Current(peer);
            return new(current?.Select(entry => new Realization(new(entry.Generator),
                new(entry.Container), entry.Id)).ToArray() ?? [], current != null);
        }

        internal bool IsCurrent(AutomationPeer peer)
        {
            if (!_valid) return false;
            var current = Current(peer);
            if (current == null || current.Count != _realizations.Length) return false;
            for (int i = 0; i < current.Count; i++)
            {
                var saved = _realizations[i];
                if (!saved.Generator.TryGetTarget(out var generator)
                    || !saved.Container.TryGetTarget(out var container)
                    || !ReferenceEquals(generator, current[i].Generator)
                    || !ReferenceEquals(container, current[i].Container)
                    || saved.Id != current[i].Id) return false;
            }
            return true;
        }

        private static List<(ItemContainerGenerator Generator, DependencyObject Container, ulong Id)>?
            Current(AutomationPeer peer)
        {
            List<(ItemContainerGenerator, DependencyObject, ulong)> result = [];
            HashSet<Visual> visited = [];
            for (Visual? visual = GetActionTarget(peer); visual != null; visual = visual.VisualParent)
            {
                if (visited.Count >= 4096 || !visited.Add(visual)) return null;
                if (ItemsControl.ItemsControlFromItemContainer(visual) is not { } owner) continue;
                var generator = owner.ItemContainerGenerator;
                ulong id = generator.GetRealizationId(visual);
                if (id != 0) result.Add((generator, visual, id));
            }
            return result;
        }
    }
}
