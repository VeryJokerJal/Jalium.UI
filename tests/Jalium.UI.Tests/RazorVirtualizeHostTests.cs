using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers <see cref="RazorItemsHost"/>, the runtime host the <c>@virtualize</c> directive lowers
/// to: that it virtualizes in both of its scroll-host modes, that it picks the right mode, and
/// that the numeric form scales without materializing its sequence.
/// </summary>
/// <remarks>
/// A bare panel cannot prove any of this. Whether virtualization happens is decided by the
/// constraint the panel is handed, and that is decided further up — by whether a ScrollViewer
/// adopted something as its <c>IScrollInfo</c>. So every case here drives the whole chain.
/// </remarks>
[Collection("Application")]
public sealed class RazorVirtualizeHostTests
{
    private const double RowHeight = 25;
    private const double ViewportWidth = 300;
    private const double ViewportHeight = 300;   // twelve rows

    /// <summary>
    /// Containers are pinned on both axes. An unpinned container collapses, and then a
    /// realized-count assertion stops meaning anything.
    /// </summary>
    private static RazorItemsHost BuildHost(int itemCount, out int[] source)
    {
        source = Enumerable.Range(0, itemCount).ToArray();
        var host = new RazorItemsHost();
        var template = new DataTemplate();
        template.SetVisualTree(() => new Border
        {
            Width = ViewportWidth,
            MinWidth = ViewportWidth,
            Height = RowHeight,
            MinHeight = RowHeight,
        });

        host.ItemTemplate = template;
        host.ItemsSource = source;
        return host;
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        if (root is T match)
        {
            return match;
        }

        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is { } child && FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Size PreviousConstraintOf(UIElement element)
        => (Size)typeof(UIElement)
            .GetProperty("PreviousAvailableSize", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(element)!;

    /// <summary>Returns the first descendant still asking to be measured.</summary>
    private static UIElement? FindInvalidated(Visual root)
    {
        if (root is UIElement { IsMeasureValid: false } stale)
        {
            return stale;
        }

        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is { } child && FindInvalidated(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static VirtualizingPanel? Settle(FrameworkElement root, Size constraint, int maxPasses = 10)
    {
        var bounds = new Rect(0, 0, constraint.Width, constraint.Height);
        root.ApplyTemplate();

        for (var pass = 0; pass < maxPasses; pass++)
        {
            root.Measure(constraint);
            root.Arrange(bounds);

            // Propagate descendant invalidations before the viewer consumes refined extents.
            // Measuring only the panel can release an end anchor too early.
            root.UpdateLayout();

            // Measuring from the root is not enough on its own: a parent whose own constraint did
            // not change short-circuits before reaching the child, so an invalidation raised deep
            // in the tree goes nowhere. Re-measuring the stale element against the constraint it
            // itself last saw is what LayoutManager does.
            if (FindInvalidated(root) is { } stale && !ReferenceEquals(stale, root))
            {
                stale.Measure(PreviousConstraintOf(stale));
                root.InvalidateArrange();
                root.Arrange(bounds);
                continue;
            }

            if (root.IsMeasureValid && root.IsArrangeValid)
            {
                break;
            }

            root.InvalidateMeasure();
        }

        return FindDescendant<VirtualizingPanel>(root);
    }

    private static object? ScrollInfoOf(ScrollViewer viewer)
        => typeof(ScrollViewer)
            .GetProperty("ScrollInfo", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(viewer);

    [Fact]
    public void AsScrollViewerContent_TheViewerAdoptsTheHostItself()
    {
        var host = BuildHost(600, out _);
        var viewer = new ScrollViewer { CanContentScroll = true, Content = host };

        Settle(viewer, new Size(ViewportWidth, ViewportHeight));

        Assert.Same(host, ScrollInfoOf(viewer));
    }

    [Fact]
    public void InsideAScrollViewer_TheHostDoesNotBuildASecondOne()
    {
        var host = BuildHost(600, out _);
        var viewer = new ScrollViewer { CanContentScroll = true, Content = host };

        Settle(viewer, new Size(ViewportWidth, ViewportHeight));

        Assert.Null(FindDescendant<ScrollViewer>(host));
        Assert.IsType<ItemsPresenter>(host.TemplateRootInternal);
    }

    [Fact]
    public void InsideAScrollViewer_FiveThousandItems_RealizesOnlyTheViewport()
    {
        var host = BuildHost(5000, out _);
        var viewer = new ScrollViewer { CanContentScroll = true, Content = host };

        var panel = Settle(viewer, new Size(ViewportWidth, ViewportHeight));

        Assert.NotNull(panel);
        Assert.InRange(panel!.Children.Count, 12, 120);
    }

    [Fact]
    public void Standalone_WithABoundedHeight_BuildsItsOwnViewerAndStillVirtualizes()
    {
        var host = BuildHost(5000, out _);
        var frame = new Border { Height = ViewportHeight, Child = host };

        var panel = Settle(frame, new Size(ViewportWidth, ViewportHeight));

        Assert.NotNull(FindDescendant<ScrollViewer>(host));
        Assert.NotNull(panel);
        Assert.InRange(panel!.Children.Count, 12, 120);
    }

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void Standalone_ScrollCommandsReachTheLastItem(Orientation orientation)
    {
        var host = BuildHost(5000, out _);
        host.Orientation = orientation;
        var frame = new Border { Width = ViewportWidth, Height = ViewportHeight, Child = host };
        var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native,
            Width = ViewportWidth, Height = ViewportHeight, Content = frame };
        var constraint = new Size(ViewportWidth, ViewportHeight);
        var panel = Settle(window, constraint);
        var viewer = Assert.IsType<ScrollViewer>(FindDescendant<ScrollViewer>(host));

        Assert.NotNull(panel);
        Assert.InRange(panel!.Children.Count, 1, 120);
        Assert.Null(host.ItemContainerGenerator.ContainerFromIndex(4999));

        if (orientation == Orientation.Vertical)
            viewer.ScrollToBottom();
        else
            viewer.ScrollToEnd();
        Settle(window, constraint);

        double offset = orientation == Orientation.Vertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
        double extent = orientation == Orientation.Vertical ? viewer.ExtentHeight : viewer.ExtentWidth;
        double viewport = orientation == Orientation.Vertical ? viewer.ViewportHeight : viewer.ViewportWidth;
        Assert.True(offset > 0);
        Assert.Equal(extent - viewport, offset, 3);
        Assert.True(host.ItemContainerGenerator.ContainerFromIndex(4999) != null,
            $"last item missing: {orientation}, offset={offset}, extent={extent}, viewport={viewport}, " +
            $"realized={string.Join(',', panel.Children.Cast<UIElement>().Select(child => host.ItemContainerGenerator.IndexFromContainer(child)))}");
        Assert.Null(host.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.InRange(panel.Children.Count, 1, 120);

        if (orientation == Orientation.Vertical)
            viewer.ScrollToTop();
        else
            viewer.ScrollToHome();
        Settle(window, constraint);
        Assert.NotNull(host.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Null(host.ItemContainerGenerator.ContainerFromIndex(4999));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalOuterViewer_IsPreservedUntilContentScrollingIsEnabled(bool explicitFalse)
    {
        var host = BuildHost(200, out _);
        var viewer = new ScrollViewer { Content = host };
        if (explicitFalse)
            viewer.CanContentScroll = false;
        var constraint = new Size(ViewportWidth, ViewportHeight);
        var panel = Settle(viewer, constraint);

        Assert.False(viewer.CanContentScroll);
        Assert.Null(ScrollInfoOf(viewer));
        Assert.Null(host.ScrollOwner);
        Assert.NotNull(FindDescendant<ScrollViewer>(host));
        Assert.Equal(200, panel!.Children.Count);
        viewer.LineDown();
        Settle(viewer, constraint);
        Assert.True(viewer.VerticalOffset > 0);

        viewer.CanContentScroll = true;
        panel = Settle(viewer, constraint);
        Assert.Same(host, ScrollInfoOf(viewer));
        Assert.Same(viewer, host.ScrollOwner);
        Assert.Null(FindDescendant<ScrollViewer>(host));
        Assert.InRange(panel!.Children.Count, 12, 120);
        Assert.Equal(0, viewer.VerticalOffset);
        viewer.ScrollToBottom();
        Settle(viewer, constraint);
        Assert.NotNull(host.ItemContainerGenerator.ContainerFromIndex(199));

        viewer.CanContentScroll = false;
        panel = Settle(viewer, constraint);
        Assert.Null(host.ScrollOwner);
        Assert.Null(ScrollInfoOf(viewer));
        Assert.Equal(0, viewer.VerticalOffset);
        Assert.Equal(200, panel!.Children.Count);
    }

    [Fact]
    public void UnboundedScrollAxis_DegradesToEagerLayoutRatherThanRenderingNothing()
    {
        // With an infinite scroll axis the panel's viewport coerces to zero and its desired size
        // collapses with it, so without the guard the list renders blank. Falling back to eager
        // layout is not a regression; a blank list is.
        var host = BuildHost(200, out _);
        var frame = new Border { Child = host };

        Settle(frame, new Size(ViewportWidth, double.PositiveInfinity));

        Assert.False(VirtualizingPanel.GetIsVirtualizing(host));
        Assert.True(host.DesiredSize.Height > 0, "the host collapsed to zero height instead of degrading");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BoundedLayoutRestoresTheApplicationsVirtualizationValue(bool enabled)
    {
        var host = BuildHost(200, out _);
        VirtualizingPanel.SetIsVirtualizing(host, enabled);
        var viewer = new ScrollViewer { Content = host };
        var constraint = new Size(ViewportWidth, ViewportHeight);
        Settle(viewer, constraint);
        Assert.False(VirtualizingPanel.GetIsVirtualizing(host));
        Assert.Equal(enabled, host.ReadLocalValue(VirtualizingPanel.IsVirtualizingProperty));

        viewer.CanContentScroll = true;
        var panel = Settle(viewer, constraint);
        Assert.Equal(enabled, VirtualizingPanel.GetIsVirtualizing(host));
        Assert.Equal(enabled, host.ReadLocalValue(VirtualizingPanel.IsVirtualizingProperty));
        if (enabled)
            Assert.InRange(panel!.Children.Count, 12, 120);
        else
            Assert.Equal(200, panel!.Children.Count);
    }

    [Fact]
    public void UnboundedFallbackPreservesTheVirtualizationBindingAndItsLatestValue()
    {
        var source = new Border();
        ScrollViewer.SetCanContentScroll(source, true);
        var host = BuildHost(200, out _);
        host.SetBinding(VirtualizingPanel.IsVirtualizingProperty, new Binding
        {
            Source = source, Path = new PropertyPath(ScrollViewer.CanContentScrollProperty),
            Mode = BindingMode.OneWay,
        });
        var binding = host.GetBindingExpression(VirtualizingPanel.IsVirtualizingProperty);
        var viewer = new ScrollViewer { Content = host };
        var constraint = new Size(ViewportWidth, ViewportHeight);
        Settle(viewer, constraint);
        Assert.False(VirtualizingPanel.GetIsVirtualizing(host));
        Assert.True(ScrollViewer.GetCanContentScroll(source));
        Assert.Same(binding, host.GetBindingExpression(VirtualizingPanel.IsVirtualizingProperty));

        viewer.CanContentScroll = true;
        var panel = Settle(viewer, constraint);
        Assert.True(VirtualizingPanel.GetIsVirtualizing(host));
        Assert.InRange(panel!.Children.Count, 12, 120);
        Assert.Same(binding, host.GetBindingExpression(VirtualizingPanel.IsVirtualizingProperty));

        viewer.CanContentScroll = false;
        Settle(viewer, constraint);
        ScrollViewer.SetCanContentScroll(source, false);
        viewer.CanContentScroll = true;
        panel = Settle(viewer, constraint);
        Assert.False(VirtualizingPanel.GetIsVirtualizing(host));
        Assert.Equal(200, panel!.Children.Count);
        Assert.Same(binding, host.GetBindingExpression(VirtualizingPanel.IsVirtualizingProperty));
    }

    [Fact]
    public void UnboundedScrollAxis_AboveTheEagerBudget_RefusesRatherThanStalling()
    {
        var host = BuildHost(200, out _);
        host.MaxEagerItemCount = 10;
        var frame = new Border { Child = host };

        Settle(frame, new Size(ViewportWidth, double.PositiveInfinity));

        Assert.True(VirtualizingPanel.GetIsVirtualizing(host));
    }

    [Fact]
    public void NumericForm_PublishesARangeAndVirtualizesIt()
    {
        var host = new RazorItemsHost();
        var template = new DataTemplate();
        template.SetVisualTree(() => new Border { Height = RowHeight, MinHeight = RowHeight });
        host.ItemTemplate = template;

        host.RangeStart = 0;
        host.RangeStep = 1;
        host.RangeEnd = 5000;
        host.IsRangeSource = true;

        var viewer = new ScrollViewer { CanContentScroll = true, Content = host };
        var panel = Settle(viewer, new Size(ViewportWidth, ViewportHeight));

        Assert.IsType<RazorIntRange>(host.ItemsSource);
        Assert.Equal(5000, host.Items.Count);
        Assert.NotNull(panel);
        Assert.InRange(panel!.Children.Count, 12, 120);
    }

    [Fact]
    public void NumericForm_AMillionItems_IsNotMaterialized()
    {
        var host = new RazorItemsHost();

        var before = GC.GetAllocatedBytesForCurrentThread();
        host.RangeEnd = 1_000_000;
        host.IsRangeSource = true;
        var itemCount = host.Items.Count;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1_000_000, itemCount);

        // A snapshot would cost a boxed int per element plus the backing list — tens of megabytes.
        // Keeping the list by reference costs a few objects.
        Assert.True(
            allocated < 2 * 1024 * 1024,
            $"publishing a million-item range allocated {allocated:N0} bytes, so it was materialized");
    }

    [Fact]
    public void NumericForm_GrowingTheCountRepublishesTheRange()
    {
        var host = new RazorItemsHost { RangeEnd = 100, IsRangeSource = true };
        var range = host.ItemsSource;

        host.RangeEnd = 5000;

        Assert.Same(range, host.ItemsSource);
        Assert.Equal(5000, host.Items.Count);
    }

    [Fact]
    public void RangeStepOfZeroIsRejected()
    {
        // Every element would box to the same value, which collapses the generator's
        // item-to-container map.
        Assert.Throws<ArgumentException>(() => new RazorIntRange(0, 10, 0));
    }

    [Fact]
    public void RangeComputesValuesAndIndicesWithoutScanning()
    {
        var range = new RazorIntRange(10, 5, 3);

        Assert.Equal(5, range.Count);
        Assert.Equal(10, range[0]);
        Assert.Equal(22, range[4]);
        Assert.Equal(4, range.IndexOf(22));
        Assert.Equal(-1, range.IndexOf(23));
        Assert.Equal(-1, range.IndexOf(25));
        Assert.Throws<ArgumentOutOfRangeException>(() => range[5]);
    }

    [Fact]
    public void RangeIsAnIListSoTheViewKeepsItByReference()
    {
        // CollectionViewSource only routes an IList to ListCollectionView, and only that path
        // keeps the source by reference. Anything else is enumerated into a snapshot.
        Assert.IsAssignableFrom<IList>(new RazorIntRange(0, 4));
    }

    [Fact]
    public void SwitchingScrollHostModeLeavesNoSecondItemsPanelBehind()
    {
        var host = BuildHost(600, out _);
        host.ScrollHost = RazorVirtualizeScrollHost.Self;
        var frame = new Border { Height = ViewportHeight, Child = host };
        var first = Settle(frame, new Size(ViewportWidth, ViewportHeight));
        Assert.NotNull(first);

        host.ScrollHost = RazorVirtualizeScrollHost.None;
        var second = Settle(frame, new Size(ViewportWidth, ViewportHeight));

        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.False(first!.IsItemsHost, "the retired panel is still flagged as the items host");
        Assert.True(second!.IsItemsHost);
    }

    [Fact]
    public void LayoutSettlesInsteadOfLooping()
    {
        var host = BuildHost(5000, out _);
        var viewer = new ScrollViewer { CanContentScroll = true, Content = host };
        var constraint = new Size(ViewportWidth, ViewportHeight);
        var bounds = new Rect(0, 0, constraint.Width, constraint.Height);

        viewer.ApplyTemplate();

        var passes = 0;
        for (; passes < 10; passes++)
        {
            viewer.Measure(constraint);
            viewer.Arrange(bounds);

            if (FindDescendant<VirtualizingPanel>(viewer) is { IsMeasureValid: false } panel)
            {
                panel.Measure(PreviousConstraintOf(panel));
                viewer.InvalidateArrange();
                viewer.Arrange(bounds);
                continue;
            }

            if (viewer.IsMeasureValid && viewer.IsArrangeValid)
            {
                break;
            }

            viewer.InvalidateMeasure();
        }

        Assert.True(passes < 9, $"layout needed {passes} passes to settle");
    }

    [Theory]
    [InlineData(0, 5, 1, false, 5)]
    [InlineData(0, 5, 1, true, 6)]
    [InlineData(0, 10, 3, false, 4)]
    [InlineData(10, 0, -1, false, 10)]
    [InlineData(10, 0, -1, true, 11)]
    [InlineData(5, 5, 1, false, 0)]
    [InlineData(0, -5, 1, false, 0)]
    public void RangeCountsIterationsTheWayTheLoopWould(int start, int end, int step, bool inclusive, int expected)
    {
        var host = new RazorItemsHost
        {
            RangeStart = start,
            RangeEnd = end,
            RangeStep = step,
            RangeEndInclusive = inclusive,
            IsRangeSource = true,
        };

        Assert.Equal(expected, host.RangeCount);
    }
}
