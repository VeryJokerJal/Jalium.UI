using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSAccessibilityChildBatchTests
{
    [Fact]
    public void ChildVisibilityWorkIsLinearAndDoesNotRequestGeometry()
    {
        using var f = new Fixture(64);
        var ids = new ulong[64];
        Assert.True(f.Read(ids, out var request));
        Assert.Equal(64, request.ChildCount);
        Assert.Equal(64, request.TextCount);
        Assert.Equal(64, ids.Distinct().Count());
        Assert.All(ids, id => Assert.NotEqual(0ul, id));
        // Count the input-route provider visits instead of asserting wall time.
        Assert.InRange(f.Probe.Rows.Sum(row => row.RouteReads), 64, 128);
        Assert.Equal(0, f.Probe.Peer.BoundsCalls);
        Assert.Equal(0, f.Probe.Peer.OffscreenCalls);
    }

    [Fact]
    public void AdvertisedBatchHasTheSameOrderAndIdentityAsIndexedChildren()
    {
        using var f = new Fixture(7);
        var info = new MacOSAXRequest { NodeId = f.Id, Operation = MacOSAXOperation.Info, Index = -2 };
        Assert.True(f.Tree.Handle(ref info));
        Assert.True(info.Flags.HasFlag(MacOSAXFlags.ChildBatch));
        var first = new ulong[7];
        Assert.True(f.Read(first, out _));
        for (int i = 0; i < first.Length; i++)
        {
            var indexed = new MacOSAXRequest { NodeId = f.Id, Operation = MacOSAXOperation.Child, Index = i };
            Assert.True(f.Tree.Handle(ref indexed));
            Assert.Equal(indexed.ResultId, first[i]);
        }
        var again = new ulong[7];
        Assert.True(f.Read(again, out _));
        Assert.Equal(first, again);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public void InsufficientCapacityReportsCurrentSizeWithoutPartialWrites(int capacity)
    {
        using var f = new Fixture(7);
        var guarded = Enumerable.Repeat(ulong.MaxValue, capacity + 2).ToArray();
        fixed (ulong* p = guarded)
        {
            var request = new MacOSAXRequest { NodeId = f.Id, Operation = MacOSAccessibilityNative.ReadChildren,
                Text = capacity == 0 ? null : (char*)(p + 1), TextCapacity = capacity };
            Assert.True(f.Tree.Handle(ref request));
            Assert.Equal(7, request.ChildCount);
            Assert.Equal(-1, request.TextCount);
        }
        Assert.All(guarded, value => Assert.Equal(ulong.MaxValue, value));
    }

    [Fact]
    public void EmptyChildrenAcceptsNullSizingBuffer()
    {
        using var f = new Fixture(0);
        var request = new MacOSAXRequest { NodeId = f.Id, Operation = MacOSAccessibilityNative.ReadChildren };
        Assert.True(f.Tree.Handle(ref request));
        Assert.Equal(0, request.ChildCount);
        Assert.Equal(0, request.TextCount);
    }

    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(65537, false, false)]
    [InlineData(1, true, false)]
    [InlineData(1, false, true)]
    public void InvalidBuffersAreRejectedWithoutWrites(int capacity, bool missing, bool misaligned)
    {
        using var f = new Fixture(1);
        ulong* guard = stackalloc ulong[3];
        guard[0] = guard[1] = guard[2] = ulong.MaxValue;
        var request = new MacOSAXRequest { NodeId = f.Id, Operation = MacOSAccessibilityNative.ReadChildren,
            Text = missing ? null : (char*)((byte*)(guard + 1) + (misaligned ? 2 : 0)), TextCapacity = capacity };
        Assert.False(f.Tree.Handle(ref request));
        for (int i = 0; i < 3; i++) Assert.Equal(ulong.MaxValue, guard[i]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenOrDetachedParentStillRejectsTheRequest(bool detach)
    {
        using var f = new Fixture(2);
        if (detach) f.Window.Content = null;
        else f.Probe.Visibility = Visibility.Collapsed;
        var buffer = new ulong[2];
        Assert.False(f.Read(buffer, out _));
        Assert.All(buffer, value => Assert.Equal(0ul, value));
    }

    [Fact]
    public void ANewRequestReflectsMutationAndRejectsTheRemovedIdentity()
    {
        using var f = new Fixture(3);
        var before = new ulong[3];
        Assert.True(f.Read(before, out _));
        f.Probe.Children.Remove(f.Probe.Rows[0]);
        f.Probe.Children.Add(new RouteText { Text = "replacement" });
        f.Probe.Peer.InvalidatePeer();
        var after = new ulong[3];
        Assert.True(f.Read(after, out _));
        Assert.Equal(before.Skip(1), after.Take(2));
        Assert.DoesNotContain(after[2], before);
        var removed = new MacOSAXRequest { NodeId = before[0], Operation = MacOSAXOperation.Info, Index = -1 };
        Assert.False(f.Tree.Handle(ref removed));
    }

    [Fact]
    public void CollapsedAndForeignChildrenAreNotCopied()
    {
        using var f = new Fixture(3);
        var foreign = new DisplayedTestWindow { Width = 200, Height = 100 };
        try
        {
            var other = new TextBlock { Text = "foreign" };
            foreign.Content = other;
            f.Probe.Rows[1].Visibility = Visibility.Collapsed;
            f.Probe.Peer.Extra.Add(other.GetAutomationPeer()!);
            f.Probe.Peer.InvalidatePeer();
            var result = new ulong[3];
            Assert.True(f.Read(result, out var request));
            Assert.Equal(2, request.TextCount);
            Assert.Equal(0ul, result[2]);
        }
        finally { foreign.Close(); }
    }

    [Fact]
    public void DetachingDuringProviderEnumerationRejectsTheWholeCopy()
    {
        using var f = new Fixture(2);
        f.Probe.Peer.OnRead = () => f.Window.Content = null;
        f.Probe.Peer.InvalidatePeer();
        var result = new ulong[2];
        Assert.False(f.Read(result, out _));
        Assert.All(result, value => Assert.Equal(0ul, value));
    }

    private sealed class RouteText : TextBlock, IInputRouteParentProvider
    {
        internal int RouteReads;
        Visual? IInputRouteParentProvider.InputRouteParent { get { RouteReads++; return VisualParent as Visual; } }
    }
    private sealed class Probe : Grid
    {
        internal readonly List<RouteText> Rows = [];
        internal readonly ProbePeer Peer;
        internal Probe(int count)
        {
            Peer = new ProbePeer(this);
            for (int i = 0; i < count; i++) { var row = new RouteText { Text = "row " + i }; Rows.Add(row); Children.Add(row); }
        }
        protected override AutomationPeer OnCreateAutomationPeer() => Peer;
    }
    private sealed class ProbePeer(Probe owner) : FrameworkElementAutomationPeer(owner)
    {
        internal readonly List<AutomationPeer> Extra = [];
        internal Action? OnRead;
        internal int BoundsCalls, OffscreenCalls;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override bool IsControlElementCore() => true;
        protected override List<AutomationPeer> GetChildrenCore()
        {
            OnRead?.Invoke();
            return owner.Children.Cast<UIElement>().Select(child => child.GetAutomationPeer()!).Concat(Extra).ToList();
        }
        protected override Rect GetBoundingRectangleCore() { BoundsCalls++; return new Rect(0, 0, 200, 100); }
        protected override bool IsOffscreenCore() { OffscreenCalls++; return false; }
    }
    private sealed class Fixture : IDisposable
    {
        internal DisplayedTestWindow Window { get; } = new() { TitleBarStyle = WindowTitleBarStyle.Native, Width = 300, Height = 200 };
        internal Probe Probe { get; }
        internal MacOSAccessibilityTree Tree { get; }
        internal ulong Id { get; }
        internal Fixture(int count)
        {
            Probe = new Probe(count); Window.Content = Probe;
            Window.Measure(new Size(300, 200)); Window.Arrange(new Rect(0, 0, 300, 200));
            Tree = new MacOSAccessibilityTree(Window);
            var child = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child };
            Assert.True(Tree.Handle(ref child)); Id = child.ResultId;
            foreach (var row in Probe.Rows) row.RouteReads = 0;
            Probe.Peer.BoundsCalls = Probe.Peer.OffscreenCalls = 0;
        }
        internal bool Read(ulong[] values, out MacOSAXRequest request)
        {
            fixed (ulong* p = values)
            {
                request = new MacOSAXRequest { NodeId = Id, Operation = MacOSAccessibilityNative.ReadChildren,
                    Text = (char*)p, TextCapacity = values.Length };
                return Tree.Handle(ref request);
            }
        }
        public void Dispose() { Tree.Dispose(); Window.Close(); }
    }
}
