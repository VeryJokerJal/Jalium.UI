using System.Text.Json;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;

namespace Jalium.UI.Controls.Automation.MacOS;

internal sealed unsafe partial class MacOSAccessibilityTree
{
    private bool ReplaceSelectedItems(AutomationPeer peer, ref MacOSAXRequest request)
    {
        if (peer.Owner is not (ListBox or TreeView)
            || request.TextCount < 2 || request.TextCount > 2 * 1024 * 1024
            || request.TextCount > request.TextCapacity || request.Text == null)
            return false;

        var lifetime = CaptureActionLifetime(peer);
        if (!lifetime() || peer.GetPattern(PatternInterface.Selection) is not ISelectionProvider provider)
            return false;
        List<ulong> ids = [];
        try
        {
            using var document = JsonDocument.Parse(new string(request.Text, 0, request.TextCount));
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() > 65536) return false;
            foreach (var value in document.RootElement.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong id)) return false;
                ids.Add(id);
            }
        }
        catch (JsonException) { return false; }

        List<AutomationPeer> members = [];
        List<Func<bool>> memberLifetimes = [];
        HashSet<ulong> seen = [];
        foreach (ulong id in ids)
        {
            if (!seen.Add(id)) continue;
            if (!_peers.TryGetValue(id, out var weak) || !weak.TryGetTarget(out var member)
                || !_ids.TryGetValue(member, out var identity) || identity.Value != id
                || !HasCurrentIdentity(member, identity)) return false;
            var memberLifetime = CaptureActionLifetime(member);
            if (!memberLifetime()
                || member.GetPattern(PatternInterface.SelectionItem) is not ISelectionItemProvider item
                || item.SelectionContainer is not IAutomationPeerRawProvider container
                || !ReferenceEquals(container.Peer.Owner, peer.Owner))
                return false;
            members.Add(member);
            memberLifetimes.Add(memberLifetime);
        }
        if (members.Count > 1 && !provider.CanSelectMultiple) return false;

        // Resolve every member before committing. A foreign, hidden, disabled,
        // recycled or retired item must never leave a partial selection behind.
        if (peer.Owner is ListBox list)
        {
            List<ItemOccurrence> rows = [];
            foreach (var member in members)
            {
                if (member is not ListBoxItemAutomationPeer item || list.Items.GetOccurrenceAt(item.ListIndex) is not { } row) return false;
                rows.Add(row);
            }
            bool Current() => lifetime() && memberLifetimes.All(current => current());
            return list.SetAutomationSelectedOccurrences(rows, Current);
        }
        if (peer.Owner is TreeView tree && members.Count <= 1)
        {
            TreeViewItem? item = null;
            if (members.Count == 1 && (item = members[0].Owner as TreeViewItem) is null) return false;
            if (!lifetime() || memberLifetimes.Any(current => !current())) return false;
            tree.SelectItem(item);
            return true;
        }
        return false;
    }
}
