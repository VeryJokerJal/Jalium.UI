using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsAutomationBuilderTests
{
    [Fact]
    public void BatchesResumeEveryChildAfterTheFortyEightChildBoundary()
    {
        var peer = new ProbePeer("Root");
        for (int i = 0; i < 137; i++) peer.ChildPeers.Add(new ProbePeer("Child " + i));
        var builder = new DevToolsAutomationTreeBuilder(peer);
        builder.ProcessBatch(); Assert.InRange(builder.Root.Children.Count, 1, 48); Assert.True(builder.IsBuilding);
        Finish(builder);
        Assert.Equal(138, builder.NodeCount); Assert.Equal(137, builder.Root.Children.Count);
        Assert.Equal(Enumerable.Range(0, 137).Select(i => "Child " + i), builder.Root.Children.Select(child => child.PeerName));
        Assert.Equal(1, peer.ChildrenCalls); Assert.All(peer.ChildPeers.Cast<ProbePeer>(), child => Assert.Equal(1, child.ChildrenCalls));
        Assert.Equal(0, builder.IssueCount); Assert.False(builder.IsLimited);
    }

    [Fact]
    public void CyclesGetterErrorsAndPatternsHaveExplicitInspectableRows()
    {
        var peer = new ProbePeer("Root"); var broken = new ProbePeer("Broken") { ThrowName = true, ThrowPattern = true };
        var child = new ProbePeer("Child") { ThrowChildren = true };
        peer.ChildPeers.Add(broken); peer.ChildPeers.Add(child); peer.ChildPeers.Add(peer);
        var builder = new DevToolsAutomationTreeBuilder(peer); Finish(builder);
        Assert.Equal(4, builder.NodeCount); Assert.Equal(3, builder.IssueCount);
        var error = builder.Root.Children[0]; Assert.True(error.IsIssue);
        Assert.Contains(error.Properties, property => property.Name == "Name" && property.Kind == DevToolsAutomationValueKind.Error);
        Assert.Contains(error.Properties, property => property.Name == "Value pattern" && property.Kind == DevToolsAutomationValueKind.Error);
        Assert.Contains("Invoke", error.Patterns);
        Assert.Contains(builder.Root.Children[1].Properties, property => property.Name == "Enumeration" && property.Value.Contains("Children"));
        var repeated = builder.Root.Children[2]; Assert.Empty(repeated.Children); Assert.Equal("Repeated peer", repeated.Note);
        DevToolsAutomationTreeBuilder.RefreshDetails(repeated); Assert.True(repeated.IsIssue); Assert.Equal("Repeated peer", repeated.Note);
    }

    [Fact]
    public void NodeAndDepthLimitsTerminateWithoutSilentlyClaimingACompleteTree()
    {
        var root = new ProbePeer("Root"); for (int i = 0; i < 100; i++) root.ChildPeers.Add(new ProbePeer("Child " + i));
        var builder = new DevToolsAutomationTreeBuilder(root, maximumNodes: 25); Finish(builder);
        Assert.Equal(25, builder.NodeCount); Assert.True(builder.IsLimited); Assert.Equal("Node limit", builder.Root.Note);
        builder = new DevToolsAutomationTreeBuilder(root, maximumDepth: 0); Finish(builder);
        Assert.True(builder.IsLimited); Assert.Equal("Depth limit", builder.Root.Note); Assert.Empty(builder.Root.Children);
    }

    [Fact]
    public void SnapshotIdentityRestoresExpansionAndSelectionWithoutHoldingThePeer()
    {
        var root = new ProbePeer("Root"); var child = new ProbePeer("Child"); root.ChildPeers.Add(child);
        var first = new DevToolsAutomationTreeBuilder(root); Finish(first);
        var selected = first.Root.Children[0];
        var expansion = new Dictionary<object, bool> { [first.Root.Identity!] = false, [selected.Identity!] = true };
        var second = new DevToolsAutomationTreeBuilder(root, expansion, selected.Identity); Finish(second);
        Assert.False(second.Root.IsExpanded); Assert.True(second.Root.Children[0].IsExpanded);
        Assert.Same(second.Root.Children[0], second.SelectedItem); Assert.True(second.SelectedItem!.IsSelected);
        Assert.Same(selected.Identity, second.SelectedItem.Identity);
        var retained = CaptureForCollection();
        for (int i = 0; i < 3 && retained.Peer.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(retained.Peer.IsAlive); GC.KeepAlive(retained.Item);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Peer, DevToolsAutomationItem Item) CaptureForCollection()
    {
        var peer = new ProbePeer("Transient"); var builder = new DevToolsAutomationTreeBuilder(peer); Finish(builder);
        return (new WeakReference(peer), builder.Root);
    }
    private static void Finish(DevToolsAutomationTreeBuilder builder)
    {
        for (int pass = 0; builder.IsBuilding && pass < 1000; pass++) builder.ProcessBatch();
        Assert.False(builder.IsBuilding);
    }
    private sealed class ProbePeer(string name) : FrameworkElementAutomationPeer(new Border())
    {
        internal List<AutomationPeer> ChildPeers { get; } = [];
        internal int ChildrenCalls;
        internal bool ThrowName, ThrowChildren, ThrowPattern;
        protected override string GetNameCore() => ThrowName ? throw new InvalidOperationException("Name failed") : name;
        protected override string GetClassNameCore() => "Probe";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override List<AutomationPeer> GetChildrenCore() { ChildrenCalls++; return ThrowChildren ? throw new InvalidOperationException("Children failed") : ChildPeers; }
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Value && ThrowPattern ? throw new InvalidOperationException("Pattern failed") : pattern == PatternInterface.Invoke ? this : null;
    }
}
