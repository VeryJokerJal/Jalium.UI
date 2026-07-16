namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for "zombie measure/arrange": LayoutManager.UpdateLayout drains
/// _measureQueue/_arrangeQueue into sorted snapshots before processing. An element that an
/// EARLIER snapshot entry detaches mid-pass (template re-apply, items-host teardown calling
/// RemoveVisualChild from inside Measure/Arrange) is no longer reachable by the detach-time
/// queue removal (FrameworkElement.RemoveSubtreeFromLayoutManager only edits the HashSets),
/// so without a processing-time connectivity check it still receives a real Measure/Arrange
/// with layout side effects (the TabControl "logical child already has a parent" crash class,
/// 2026-07-15).
///
/// These tests drive the internal LayoutManager directly (InternalsVisibleTo) through a
/// minimal ILayoutManagerHost root, so the real dirty-queue path is exercised headlessly.
/// </summary>
public sealed class LayoutManagerDetachedElementTests
{
    private static readonly Size HostSize = new(800, 600);

    /// <summary>
    /// Minimal layout root standing in for Window/PopupWindow: owns the LayoutManager so
    /// UIElement.InvalidateMeasure/InvalidateArrange resolve it via FindLayoutManager and
    /// the production enqueue/propagate/detach paths all run for real.
    /// </summary>
    private sealed class TestLayoutHost : FrameworkElement, ILayoutManagerHost
    {
        public LayoutManager LayoutManager { get; } = new();

        public int MeasureCount;
        public int ArrangeCount;

        public void AddChild(UIElement child) => AddVisualChild(child);

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeCount++;
            return finalSize;
        }
    }

    /// <summary>
    /// Single-child container whose Measure/Arrange can run a hook (e.g. detaching its own
    /// child) — the same shape as a template re-apply tearing down a retired items host from
    /// inside the layout pass. Deliberately does NOT measure/arrange its child: the child is
    /// queued independently so its processing comes from the LayoutManager snapshot alone.
    /// </summary>
    private sealed class DetachingParent : FrameworkElement
    {
        public Action<DetachingParent>? MeasureHook;
        public Action<DetachingParent>? ArrangeHook;

        public int MeasureCount;
        public int ArrangeCount;

        public void AddChild(UIElement child) => AddVisualChild(child);

        public void RemoveChild(UIElement child) => RemoveVisualChild(child);

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            MeasureHook?.Invoke(this);
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeCount++;
            ArrangeHook?.Invoke(this);
            return finalSize;
        }
    }

    private sealed class CountingChild : FrameworkElement
    {
        public int MeasureCount;
        public int ArrangeCount;

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeCount++;
            return finalSize;
        }
    }

    private static (TestLayoutHost Host, DetachingParent Parent, CountingChild Child) CreateTree()
    {
        var host = new TestLayoutHost();
        var parent = new DetachingParent();
        var child = new CountingChild();
        host.AddChild(parent);
        parent.AddChild(child);
        return (host, parent, child);
    }

    [Fact]
    public void ElementDetachedMidPass_DoesNotReceiveZombieMeasure()
    {
        var (host, parent, child) = CreateTree();
        parent.MeasureHook = p => p.RemoveChild(child);

        // Child and its ancestors land in the same drained snapshot; the parent (shallower,
        // processed first) detaches the child from inside its own MeasureOverride.
        child.InvalidateMeasure();
        host.LayoutManager.UpdateLayout(host, HostSize);

        Assert.Equal(1, parent.MeasureCount);
        Assert.Null(child.VisualParent);
        Assert.Equal(0, child.MeasureCount);
    }

    [Fact]
    public void ElementDetachedMidPass_DoesNotReceiveZombieArrange()
    {
        var (host, parent, child) = CreateTree();

        // Prime one clean queue-driven pass so every element holds valid layout state.
        child.InvalidateMeasure();
        host.LayoutManager.UpdateLayout(host, HostSize);
        var measuresAfterPrime = child.MeasureCount;
        var arrangesAfterPrime = child.ArrangeCount;

        parent.ArrangeHook = p => p.RemoveChild(child);
        child.InvalidateArrange();
        host.LayoutManager.UpdateLayout(host, HostSize);

        Assert.Null(child.VisualParent);
        Assert.Equal(measuresAfterPrime, child.MeasureCount);
        Assert.Equal(arrangesAfterPrime, child.ArrangeCount);
    }

    [Fact]
    public void ElementReattachedAfterMidPassDetach_IsMeasuredOnNextPass()
    {
        var (host, parent, child) = CreateTree();
        parent.MeasureHook = p => p.RemoveChild(child);

        child.InvalidateMeasure();
        host.LayoutManager.UpdateLayout(host, HostSize);
        Assert.Equal(0, child.MeasureCount);

        // Skipping (rather than processing) a mid-pass-detached element must not starve it:
        // reattaching runs OnVisualParentChanged, which re-invalidates and re-enqueues.
        parent.MeasureHook = null;
        parent.AddChild(child);
        host.LayoutManager.UpdateLayout(host, HostSize);

        Assert.Equal(1, child.MeasureCount);
        Assert.True(child.IsMeasureValid);
    }

    [Fact]
    public void ElementDetachedBeforePass_IsNotMeasured()
    {
        var (host, parent, child) = CreateTree();

        // Detach BEFORE UpdateLayout: RemoveSubtreeFromLayoutManager pulls the subtree out of
        // the queues, and the processing-time guard covers it independently (double net).
        child.InvalidateMeasure();
        parent.RemoveChild(child);
        host.LayoutManager.UpdateLayout(host, HostSize);

        Assert.Equal(0, child.MeasureCount);
    }

    [Fact]
    public void AncestorsAboveNonTopRoot_AreStillProcessed()
    {
        // PopupWindow shape: UpdateLayout is called with the popup CONTENT as root, so the
        // host sits ABOVE root yet is enqueued by PropagateInvalidMeasureUp. Live elements
        // above a non-top root must keep being processed by the queue pass — by the measure
        // loop AND the arrange loop (the two loops carry independent guards; InvalidateMeasure
        // queues into both).
        var (host, parent, child) = CreateTree();

        child.InvalidateMeasure();
        host.LayoutManager.UpdateLayout(parent, new Size(400, 300));

        Assert.Equal(1, host.MeasureCount);
        Assert.Equal(1, parent.MeasureCount);
        Assert.Equal(1, child.MeasureCount);
        Assert.Equal(1, host.ArrangeCount);
        Assert.Equal(1, parent.ArrangeCount);
        Assert.Equal(1, child.ArrangeCount);
    }

    [Fact]
    public void SubtreeDetachedMidPass_SkipsDescendantsWithLiveLocalParents()
    {
        // Deep-chain variant: the detached element's own VisualParent stays NON-null (only an
        // ANCESTOR was cut), so only a walk to the tree top can recognize it as disconnected.
        var host = new TestLayoutHost();
        var parent = new DetachingParent();
        var mid = new DetachingParent();
        var grandchild = new CountingChild();
        host.AddChild(parent);
        parent.AddChild(mid);
        mid.AddChild(grandchild);

        parent.MeasureHook = p => p.RemoveChild(mid);

        grandchild.InvalidateMeasure();
        host.LayoutManager.UpdateLayout(host, HostSize);

        Assert.Null(mid.VisualParent);
        Assert.Same(mid, grandchild.VisualParent);
        Assert.Equal(0, mid.MeasureCount);
        Assert.Equal(0, grandchild.MeasureCount);
    }
}
