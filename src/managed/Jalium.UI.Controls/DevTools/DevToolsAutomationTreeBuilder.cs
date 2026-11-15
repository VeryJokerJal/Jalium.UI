using System.Diagnostics;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Enumerates providers in bounded UI-thread batches without constructing visuals.</summary>
internal sealed class DevToolsAutomationTreeBuilder
{
    private sealed class Pending(DevToolsAutomationItem item, AutomationPeer peer, int depth)
    {
        internal readonly DevToolsAutomationItem Item = item;
        internal readonly AutomationPeer Peer = peer;
        internal readonly int Depth = depth;
        internal List<AutomationPeer>? Children;
        internal int NextChild;
    }
    private readonly Queue<Pending> _pending = new();
    private readonly HashSet<AutomationPeer> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly IReadOnlyDictionary<object, bool>? _expansion;
    private readonly object? _selectedIdentity;
    private readonly int _maximumNodes;
    private readonly int _maximumDepth;
    internal DevToolsAutomationItem Root { get; }
    internal DevToolsAutomationItem? SelectedItem { get; private set; }
    internal int NodeCount { get; private set; }
    internal int IssueCount { get; private set; }
    internal bool IsBuilding => _pending.Count > 0;
    internal bool IsLimited { get; private set; }

    internal DevToolsAutomationTreeBuilder(AutomationPeer peer, IReadOnlyDictionary<object, bool>? expansion = null, object? selectedIdentity = null,
        int maximumNodes = 20000, int maximumDepth = 128)
    {
        _expansion = expansion; _selectedIdentity = selectedIdentity;
        _maximumNodes = Math.Max(1, maximumNodes); _maximumDepth = Math.Max(0, maximumDepth);
        Root = Capture(peer); Root.IsExpanded = expansion?.TryGetValue(Root.Identity!, out var expanded) == true ? expanded : true;
        _visited.Add(peer); _pending.Enqueue(new Pending(Root, peer, 0));
        SelectedItem ??= Root;
    }

    internal void ProcessBatch(int maximumParents = 8, int maximumChildren = 48)
    {
        int parents = 0, children = 0;
        var start = Stopwatch.GetTimestamp();
        while (_pending.TryPeek(out var task) && parents < maximumParents && children < maximumChildren)
        {
            if (task.Depth >= _maximumDepth)
            {
                MarkIssue(task.Item, "Depth limit"); IsLimited = true; _pending.Dequeue(); parents++; continue;
            }
            if (task.Children == null)
            {
                try { task.Children = task.Peer.GetChildren() ?? []; }
                catch (Exception ex) { MarkIssue(task.Item, "Children: " + ex.GetType().Name); _pending.Dequeue(); parents++; continue; }
            }
            while (task.NextChild < task.Children.Count && children < maximumChildren)
            {
                if (NodeCount >= _maximumNodes)
                {
                    MarkIssue(task.Item, "Node limit"); IsLimited = true; Cancel(); return;
                }
                var peer = task.Children[task.NextChild++];
                children++;
                if (peer == null) continue;
                var node = Capture(peer);
                task.Item.Children.Add(node);
                if (_visited.Add(peer)) _pending.Enqueue(new Pending(node, peer, task.Depth + 1));
                else MarkIssue(node, "Repeated peer");
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 8) return;
            }
            if (task.NextChild < task.Children.Count) break;
            _pending.Dequeue(); parents++;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 8) break;
        }
        if (!IsBuilding) _visited.Clear();
    }

    internal void Cancel() { _pending.Clear(); _visited.Clear(); }
    private DevToolsAutomationItem Capture(AutomationPeer peer)
    {
        var item = new DevToolsAutomationItem(); item.AttachPeer(peer); NodeCount++;
        RefreshDetails(item);
        if (item.IsIssue) IssueCount++;
        if (_expansion?.TryGetValue(item.Identity!, out var expanded) == true) item.IsExpanded = expanded;
        if (ReferenceEquals(_selectedIdentity, item.Identity)) { item.IsSelected = true; SelectedItem = item; }
        return item;
    }
    private void MarkIssue(DevToolsAutomationItem item, string note)
    {
        if (!item.IsIssue) IssueCount++;
        item.IsIssue = true; item.Note = note;
        item.Properties.Add(new DevToolsAutomationProperty("Enumeration", note, DevToolsAutomationValueKind.Error));
    }

    internal static void RefreshDetails(DevToolsAutomationItem item)
    {
        if (!item.TryGetPeer(out var peer) || peer == null) return;
        var enumerationIssues = item.Properties.Where(property => property.Name == "Enumeration").ToArray();
        item.Properties.Clear(); item.Patterns.Clear(); item.IsIssue = false; item.Note = "";
        string Read(string name, Func<string> getter, DevToolsAutomationValueKind kind)
        {
            string value;
            try { value = getter(); }
            catch (Exception ex) { value = "<" + ex.GetType().Name + ": " + ex.Message + ">"; kind = DevToolsAutomationValueKind.Error; item.IsIssue = true; }
            item.Properties.Add(new DevToolsAutomationProperty(name, value.Length > 0 ? value : "(none)", kind));
            return kind == DevToolsAutomationValueKind.Error ? "" : value;
        }
        item.PeerName = Read("Name", peer.GetName, DevToolsAutomationValueKind.String);
        item.Role = Read("ControlType", () => peer.GetAutomationControlType().ToString(), DevToolsAutomationValueKind.Enum);
        item.ClassName = Read("ClassName", peer.GetClassName, DevToolsAutomationValueKind.Text);
        Read("AutomationId", peer.GetAutomationId, DevToolsAutomationValueKind.String);
        Read("HelpText", peer.GetHelpText, DevToolsAutomationValueKind.String);
        Read("IsEnabled", () => peer.IsEnabled().ToString(), DevToolsAutomationValueKind.Boolean);
        Read("IsKeyboardFocusable", () => peer.IsKeyboardFocusable().ToString(), DevToolsAutomationValueKind.Boolean);
        Read("HasKeyboardFocus", () => peer.HasKeyboardFocus().ToString(), DevToolsAutomationValueKind.Boolean);
        Read("BoundingRectangle", () => peer.GetBoundingRectangle().ToString(), DevToolsAutomationValueKind.Number);
        foreach (var pattern in Enum.GetValues<PatternInterface>())
        {
            try { if (peer.GetPattern(pattern) != null) item.Patterns.Add(pattern.ToString()); }
            catch (Exception ex)
            {
                item.IsIssue = true;
                item.Properties.Add(new DevToolsAutomationProperty(pattern + " pattern", "<" + ex.GetType().Name + ">", DevToolsAutomationValueKind.Error));
            }
        }
        foreach (var issue in enumerationIssues) { item.Properties.Add(issue); item.IsIssue = true; item.Note = issue.Value; }
    }
}
