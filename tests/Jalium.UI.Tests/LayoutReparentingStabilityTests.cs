using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class LayoutReparentingStabilityTests
{
    [Fact]
    public void NullLayoutInvalidations_ShouldBeIgnoredWithoutBreakingPendingPass()
    {
        var host = new LayoutHostPanel();
        var leaf = new ProbeElement();
        host.Children.Add(leaf);

        var viewport = new Size(320, 240);
        host.UpdateLayoutPass(viewport);

        leaf.InvalidateMeasure();
        var layoutManager = ((ILayoutManagerHost)host).LayoutManager;
        layoutManager.InvalidateMeasure(null);
        layoutManager.InvalidateArrange(null);

        var exception = Record.Exception(() => host.UpdateLayoutPass(viewport));

        Assert.Null(exception);
        Assert.True(leaf.IsMeasureValid);
        Assert.True(leaf.IsArrangeValid);
    }

    [Fact]
    public void InvalidateMeasure_ShouldPropagatePastAlreadyInvalidAncestor()
    {
        var host = new LayoutHostPanel();
        var outer = new Border();
        var inner = new Border();
        var leaf = new ProbeElement();

        inner.Child = leaf;
        outer.Child = inner;
        host.Children.Add(outer);

        host.UpdateLayoutPass(new Size(400, 280));

        Assert.True(host.IsMeasureValid);
        Assert.True(outer.IsMeasureValid);
        Assert.True(inner.IsMeasureValid);

        // Simulate a stale invalid ancestor that may already be invalid before
        // a descendant triggers another invalidation.
        inner.MarkMeasureInvalid();

        Assert.False(inner.IsMeasureValid);
        Assert.True(outer.IsMeasureValid);
        Assert.True(host.IsMeasureValid);

        leaf.InvalidateMeasure();

        Assert.False(outer.IsMeasureValid);
        Assert.False(host.IsMeasureValid);
    }

    [Fact]
    public void DetachingSubtree_ShouldRemoveDescendantsFromLayoutQueue()
    {
        var host = new LayoutHostPanel();
        var branch = new Border();
        var leaf = new ProbeElement();

        branch.Child = leaf;
        host.Children.Add(branch);

        var viewport = new Size(360, 240);
        host.UpdateLayoutPass(viewport);
        var baselineMeasureCount = leaf.MeasureCount;

        leaf.InvalidateMeasure();
        host.Children.Clear(); // Detach the whole subtree before the pending pass.

        host.UpdateLayoutPass(viewport);

        Assert.Equal(baselineMeasureCount, leaf.MeasureCount);
    }

    [Fact]
    public void DockTabContentSwitch_ShouldKeepMeasuredWidthStableAcrossRepeatedSwitches()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel();
        var dockItem = new DockItem { Header = "Panel" };
        var explorerContent = new ProbeElement();
        var gitContent = new ProbeElement();

        dockItem.Content = explorerContent;
        panel.Items.Add(dockItem);
        host.Children.Add(panel);

        var viewport = new Size(360, 520);
        host.UpdateLayoutPass(viewport);

        for (int i = 0; i < 20; i++)
        {
            var next = i % 2 == 0 ? (UIElement)gitContent : explorerContent;
            dockItem.Content = next;
            host.UpdateLayoutPass(viewport);

            var active = ReferenceEquals(dockItem.Content, explorerContent) ? explorerContent : gitContent;
            Assert.True(active.LastMeasureWidth >= 150, $"pass={i}, lastMeasureWidth={active.LastMeasureWidth}");
            Assert.True(active.ActualWidth >= 150, $"pass={i}, actualWidth={active.ActualWidth}");
        }
    }

    [Fact]
    public void ReattachedSubtree_ShouldRearrangeDescendantsEvenWhenSizeIsUnchanged()
    {
        var host = new LayoutHostPanel();
        var container = new Border();
        var descendant = new ArrangeProbeElement();

        container.Child = descendant;
        host.Children.Add(container);

        var viewport = new Size(420, 280);
        host.UpdateLayoutPass(viewport);

        var arrangeBeforeDetach = descendant.ArrangeCount;

        host.Children.Clear();
        host.UpdateLayoutPass(viewport);

        host.Children.Add(container);
        host.UpdateLayoutPass(viewport);

        Assert.True(descendant.ArrangeCount > arrangeBeforeDetach,
            $"descendant arrange count did not increase after reattach. before={arrangeBeforeDetach}, after={descendant.ArrangeCount}");
        Assert.True(descendant.LastArrangeWidth > 0, $"unexpected arrange width: {descendant.LastArrangeWidth}");
    }

    [Fact]
    public void RearrangedAncestor_ShouldInvalidateDescendantScreenBoundsCache()
    {
        var host = new OffsetLayoutHostPanel();
        var container = new Border();
        var descendant = new ProbeElement();

        container.Child = descendant;
        host.Children.Add(container);

        var viewport = new Size(420, 280);
        host.ChildOffset = new Point(0, 0);
        host.UpdateLayoutPass(viewport);

        var initialBounds = descendant.GetScreenBounds();
        Assert.Equal(0, initialBounds.X);

        host.ChildOffset = new Point(120, 40);
        host.InvalidateArrange();
        host.UpdateLayoutPass(viewport);

        var movedBounds = descendant.GetScreenBounds();
        Assert.Equal(120, movedBounds.X);
        Assert.Equal(40, movedBounds.Y);
    }

    private sealed class LayoutHostPanel : Panel, ILayoutManagerHost
    {
        private readonly LayoutManager _layoutManager = new();

        LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

        public void UpdateLayoutPass(Size availableSize)
        {
            _layoutManager.UpdateLayout(this, availableSize);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    child.Measure(availableSize);
                }
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                }
            }

            return finalSize;
        }
    }

    private sealed class OffsetLayoutHostPanel : Panel, ILayoutManagerHost
    {
        private readonly LayoutManager _layoutManager = new();

        public Point ChildOffset { get; set; }

        LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

        public void UpdateLayoutPass(Size availableSize)
        {
            _layoutManager.UpdateLayout(this, availableSize);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    child.Measure(availableSize);
                }
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    child.Arrange(new Rect(ChildOffset.X, ChildOffset.Y, finalSize.Width - ChildOffset.X, finalSize.Height - ChildOffset.Y));
                }
            }

            return finalSize;
        }
    }

    private sealed class ProbeElement : FrameworkElement
    {
        public int MeasureCount { get; private set; }

        public double LastMeasureWidth { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            LastMeasureWidth = availableSize.Width;

            var width = double.IsInfinity(availableSize.Width) ? 1024 : Math.Max(0, availableSize.Width);
            var height = double.IsInfinity(availableSize.Height) ? 24 : Math.Max(0, Math.Min(availableSize.Height, 24));
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return finalSize;
        }
    }

    private sealed class ArrangeProbeElement : FrameworkElement
    {
        public int ArrangeCount { get; private set; }

        public double LastArrangeWidth { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? 120 : Math.Max(0, availableSize.Width);
            var height = double.IsInfinity(availableSize.Height) ? 24 : Math.Max(0, Math.Min(availableSize.Height, 24));
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeCount++;
            LastArrangeWidth = finalSize.Width;
            return finalSize;
        }
    }
}
