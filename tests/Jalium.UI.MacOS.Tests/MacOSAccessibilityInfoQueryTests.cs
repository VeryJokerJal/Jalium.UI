using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSAccessibilityInfoQueryTests
{
    [Theory]
    [InlineData(0, true, true, true)]
    [InlineData(-1, false, false, false)]
    [InlineData(-2, true, false, false)]
    [InlineData(-3, false, true, false)]
    [InlineData(-4, false, false, true)]
    [InlineData(42, true, true, true)]
    [InlineData(-5, true, true, true)]
    public void InfoCallsOnlyTheRequestedGeometryAndChildProviders(
        int query, bool children, bool bounds, bool visibility)
    {
        using var fixture = new Fixture();
        var peer = fixture.Probe.Peer;
        peer.Reset();

        var result = fixture.Read(query);

        Assert.Equal((int)AutomationControlType.Group, result.Role);
        Assert.Equal(1ul, result.ParentId);
        Assert.Equal(children, peer.ChildrenCalls > 0);
        Assert.Equal(bounds, peer.BoundsCalls > 0);
        Assert.Equal(visibility, peer.VisibilityCalls > 0);
        Assert.Equal(children ? 1 : 0, result.ChildCount);
        Assert.Equal(bounds ? 50 : 0, result.Width);
        Assert.Equal(bounds ? 30 : 0, result.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void VisibilityRequestsKeepTheActualOffscreenState(int query)
    {
        using var fixture = new Fixture();
        fixture.Probe.Peer.ReportOffscreen = true;
        Assert.True(fixture.Read(query).Flags.HasFlag(MacOSAXFlags.Offscreen));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataRequestsStillRejectHiddenOrDetachedOwners(bool detach)
    {
        using var fixture = new Fixture();
        if (detach) fixture.Window.Content = null;
        else fixture.Probe.Visibility = Visibility.Collapsed;
        var request = new MacOSAXRequest { NodeId = fixture.Id, Operation = MacOSAXOperation.Info, Index = -1 };
        Assert.False(fixture.Tree.Handle(ref request));
    }

    private sealed class ProbeControl : Grid
    {
        internal CountingPeer Peer { get; }
        internal ProbeControl()
        {
            Peer = new CountingPeer(this);
            Children.Add(new TextBlock { Text = "child" });
        }
        protected override AutomationPeer OnCreateAutomationPeer() => Peer;
    }

    private sealed class CountingPeer(ProbeControl owner) : FrameworkElementAutomationPeer(owner)
    {
        internal int ChildrenCalls, BoundsCalls, VisibilityCalls;
        internal bool ReportOffscreen;
        internal void Reset() => ChildrenCalls = BoundsCalls = VisibilityCalls = 0;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override bool IsControlElementCore() => true;
        protected override List<AutomationPeer> GetChildrenCore()
        {
            ChildrenCalls++;
            return [owner.Children[0].GetAutomationPeer()!];
        }
        protected override Rect GetBoundingRectangleCore()
        {
            BoundsCalls++;
            return new Rect(0, 0, 50, 30);
        }
        protected override bool IsOffscreenCore()
        {
            VisibilityCalls++;
            return ReportOffscreen;
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal DisplayedTestWindow Window { get; } = new() { TitleBarStyle = WindowTitleBarStyle.Native, Width = 260, Height = 180 };
        internal ProbeControl Probe { get; } = new();
        internal MacOSAccessibilityTree Tree { get; }
        internal ulong Id { get; }
        internal Fixture()
        {
            Window.Content = Probe;
            Window.Measure(new Size(260, 180));
            Window.Arrange(new Rect(0, 0, 260, 180));
            Tree = new(Window);
            var child = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child };
            Assert.True(Tree.Handle(ref child));
            Id = child.ResultId;
        }
        internal MacOSAXRequest Read(int query)
        {
            var request = new MacOSAXRequest { NodeId = Id, Operation = MacOSAXOperation.Info, Index = query };
            Assert.True(Tree.Handle(ref request));
            return request;
        }
        public void Dispose() { Tree.Dispose(); Window.Close(); }
    }
}
