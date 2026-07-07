using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class VirtualizationPipelineTests
{
    [Fact]
    public void VirtualizingPanel_Defaults_ShouldMatchWpfLikeSettings()
    {
        var panel = new VirtualizingStackPanel();

        Assert.True(VirtualizingPanel.GetIsVirtualizing(panel));

        // DELIBERATE Jalium deviation from WPF, locked by product decision:
        //   VirtualizationMode default = Recycling (WPF: Standard)
        //   ScrollUnit         default = Pixel     (WPF: Item)
        // CacheLength (1,1) and CacheLengthUnit (Page) DO match WPF.
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(panel));
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(panel));
        Assert.Equal(VirtualizationCacheLengthUnit.Page, VirtualizingPanel.GetCacheLengthUnit(panel));

        var cacheLength = VirtualizingPanel.GetCacheLength(panel);
        Assert.Equal(1.0, cacheLength.CacheBeforeViewport);
        Assert.Equal(1.0, cacheLength.CacheAfterViewport);

        // New Phase-0 attached-property defaults.
        Assert.True(VirtualizingPanel.GetIsContainerVirtualizable(panel));
        Assert.False(VirtualizingPanel.GetIsVirtualizingWhenGrouping(panel));
    }

    [Fact]
    public void VirtualizingPanel_NullAccessorArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => VirtualizingPanel.GetIsVirtualizing(null!));
        Assert.Throws<ArgumentNullException>(() => VirtualizingPanel.GetScrollUnit(null!));
        Assert.Throws<ArgumentNullException>(() => VirtualizingPanel.SetCacheLength(null!, new VirtualizationCacheLength(1)));
    }

    [Fact]
    public void IsVirtualizingWhenGrouping_CoercedFalse_WhenNotVirtualizing()
    {
        var panel = new VirtualizingStackPanel();
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(panel, true);
        Assert.True(VirtualizingPanel.GetIsVirtualizingWhenGrouping(panel));

        // Turning virtualization off coerces grouping-virtualization to false on read.
        VirtualizingPanel.SetIsVirtualizing(panel, false);
        Assert.False(VirtualizingPanel.GetIsVirtualizingWhenGrouping(panel));
    }

    [Fact]
    public void VirtualizingPanel_HierarchicalSeams_DefaultToFlatBehavior()
    {
        var panel = new VirtualizingStackPanel();
        Assert.False(panel.CanHierarchicallyScrollAndVirtualize);
        Assert.Equal(0.0, panel.GetItemOffset(new VirtualizingStackPanel()));
        Assert.Throws<ArgumentNullException>(() => panel.GetItemOffset(null!));
    }

    [Fact]
    public void CacheLength_NaNConstructor_Throws()
    {
        Assert.Throws<ArgumentException>(() => new VirtualizationCacheLength(double.NaN));
        Assert.Throws<ArgumentException>(() => new VirtualizationCacheLength(1.0, double.NaN));
    }

    [Fact]
    public void CacheLength_NegativeEdge_RejectedAtDependencyPropertyBoundary()
    {
        var panel = new VirtualizingStackPanel();
        // The struct ctor permits negatives (only NaN throws); the DP validator rejects them.
        var negative = new VirtualizationCacheLength(-1.0, 0.0);
        Assert.Throws<ArgumentException>(() => VirtualizingPanel.SetCacheLength(panel, negative));
    }

    [Fact]
    public void VirtualizationCacheLengthConverter_RoundTripsStringAndNumeric()
    {
        var converter = new VirtualizationCacheLengthConverter();
        var ci = CultureInfo.InvariantCulture;

        Assert.True(converter.CanConvertFrom(null, typeof(string)));
        Assert.True(converter.CanConvertFrom(null, typeof(double)));
        Assert.True(converter.CanConvertTo(null, typeof(string)));
        Assert.True(converter.CanConvertTo(null, typeof(InstanceDescriptor)));

        var fromUniform = Assert.IsType<VirtualizationCacheLength>(converter.ConvertFrom(null, ci, "5"));
        Assert.Equal(new VirtualizationCacheLength(5), fromUniform);

        var fromPair = Assert.IsType<VirtualizationCacheLength>(converter.ConvertFrom(null, ci, "1,2"));
        Assert.Equal(new VirtualizationCacheLength(1, 2), fromPair);

        var fromNumeric = Assert.IsType<VirtualizationCacheLength>(converter.ConvertFrom(null, ci, 3.0));
        Assert.Equal(new VirtualizationCacheLength(3), fromNumeric);

        Assert.Equal("1,2", converter.ConvertTo(null, ci, new VirtualizationCacheLength(1, 2), typeof(string)));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(null, ci, new VirtualizationCacheLength(1, 2), typeof(InstanceDescriptor)));
        Assert.Equal(2, descriptor.Arguments.Count);

        Assert.Throws<FormatException>(() => converter.ConvertFrom(null, ci, "1,2,3"));
    }

    [Fact]
    public void HierarchicalVirtualizationConstraints_Equality_IgnoresScrollGeneration()
    {
        var cache = new VirtualizationCacheLength(1);
        var viewport = new Rect(0, 0, 100, 50);
        var a = new HierarchicalVirtualizationConstraints(cache, VirtualizationCacheLengthUnit.Pixel, viewport) { ScrollGeneration = 1 };
        var b = new HierarchicalVirtualizationConstraints(cache, VirtualizationCacheLengthUnit.Pixel, viewport) { ScrollGeneration = 2 };

        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void HierarchicalVirtualizationConstraints_Equality_DistinguishesViewportAndCache()
    {
        var cache = new VirtualizationCacheLength(1);
        var a = new HierarchicalVirtualizationConstraints(cache, VirtualizationCacheLengthUnit.Pixel, new Rect(0, 0, 100, 50));
        var differentViewport = new HierarchicalVirtualizationConstraints(cache, VirtualizationCacheLengthUnit.Pixel, new Rect(0, 0, 100, 80));
        var differentCache = new HierarchicalVirtualizationConstraints(new VirtualizationCacheLength(2), VirtualizationCacheLengthUnit.Pixel, new Rect(0, 0, 100, 50));
        var differentUnit = new HierarchicalVirtualizationConstraints(cache, VirtualizationCacheLengthUnit.Item, new Rect(0, 0, 100, 50));

        // Guards the WPF self-compare typo: the viewport must actually participate.
        Assert.NotEqual(a, differentViewport);
        Assert.NotEqual(a, differentCache);
        Assert.NotEqual(a, differentUnit);
    }

    [Fact]
    public void HierarchicalVirtualizationConstraints_HasNoPublicScrollOffsetMember()
    {
        // ScrollOffset was a non-WPF invention removed in Phase 0.
        Assert.Null(typeof(HierarchicalVirtualizationConstraints).GetProperty("ScrollOffset"));
    }

    [Fact]
    public void HierarchicalVirtualizationHeaderDesiredSizes_ValueEquality()
    {
        var a = new HierarchicalVirtualizationHeaderDesiredSizes(new Size(10, 20), new Size(11, 22));
        var equal = new HierarchicalVirtualizationHeaderDesiredSizes(new Size(10, 20), new Size(11, 22));
        var different = new HierarchicalVirtualizationHeaderDesiredSizes(new Size(10, 20), new Size(11, 99));

        Assert.True(a == equal);
        Assert.Equal(a.GetHashCode(), equal.GetHashCode());
        Assert.True(a != different);
        Assert.False(a.Equals("not a header size"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void HierarchicalVirtualizationItemDesiredSizes_EveryMemberParticipatesInEquality(int memberToFlip)
    {
        var sizes = new Size[8];
        for (var i = 0; i < 8; i++)
        {
            sizes[i] = new Size(i + 1, i + 1);
        }

        var baseline = Build(sizes);

        var flipped = (Size[])sizes.Clone();
        flipped[memberToFlip] = new Size(999, 999);
        var changed = Build(flipped);

        Assert.Equal(baseline, Build(sizes));
        Assert.NotEqual(baseline, changed);

        static HierarchicalVirtualizationItemDesiredSizes Build(Size[] s) =>
            new(s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7]);
    }

    [Fact]
    public void IHierarchicalVirtualizationAndScrollInfo_ContractShape_Compiles()
    {
        IHierarchicalVirtualizationAndScrollInfo stub = new HvasiStub();

        var header = new HierarchicalVirtualizationHeaderDesiredSizes(new Size(1, 2), new Size(3, 4));
        var items = new HierarchicalVirtualizationItemDesiredSizes(
            new Size(1, 1), new Size(2, 2), new Size(3, 3), new Size(4, 4),
            new Size(5, 5), new Size(6, 6), new Size(7, 7), new Size(8, 8));

        stub.HeaderDesiredSizes = header;
        stub.ItemDesiredSizes = items;
        stub.Constraints = new HierarchicalVirtualizationConstraints(new VirtualizationCacheLength(1), VirtualizationCacheLengthUnit.Pixel, new Rect(0, 0, 10, 10));

        Assert.Equal(header, stub.HeaderDesiredSizes);
        Assert.Equal(items, stub.ItemDesiredSizes);
        Assert.Null(stub.ItemsHost);
    }

    private sealed class HvasiStub : IHierarchicalVirtualizationAndScrollInfo
    {
        public HierarchicalVirtualizationConstraints Constraints { get; set; }
        public HierarchicalVirtualizationHeaderDesiredSizes HeaderDesiredSizes { get; set; }
        public HierarchicalVirtualizationItemDesiredSizes ItemDesiredSizes { get; set; }
        public Panel? ItemsHost => null;
        public bool MustDisableVirtualization { get; set; }
        public bool InBackgroundLayout { get; set; }
    }

    [Fact]
    public void ListBox_Virtualization_ShouldRealizeVisibleRangeOnly()
    {
        var listBox = new TestListBox
        {
            Width = 320,
            Height = 240
        };

        for (var i = 0; i < 10_000; i++)
        {
            listBox.Items.Add($"Item {i}");
        }

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        Assert.True(host.Children.Count < 1000);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(5000));
    }

    [Fact]
    public void ListBox_Virtualization_ScrollDownThenUp_ShouldPreserveItemOrder()
    {
        // Regression: after scrolling down then back to the top, recycled
        // containers must show their new item's content — not the content
        // they previously held. The bug was that ItemContainerGenerator
        // pulled containers out of the recycle pool without flagging them
        // for PrepareItemContainer, so .Content stayed stale.
        var listBox = new TestListBox
        {
            Width = 320,
            Height = 240,
        };

        for (var i = 1; i <= 1000; i++)
            listBox.Items.Add($"Person {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        // Verify initial order: top-realized container should be "Person 1".
        AssertFirstRealizedMatchesIndex(host, listBox, expectedIndex: 0);

        // Scroll far enough down to recycle the initial window.
        host.SetVerticalOffset(500);
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        // Scroll back to the top — the containers previously holding 50+/60+
        // items should have been re-prepared with Person 1..N.
        host.SetVerticalOffset(0);
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        AssertFirstRealizedMatchesIndex(host, listBox, expectedIndex: 0);

        // And walk the next few realized containers — they must be contiguous.
        for (int i = 0; i < 5; i++)
        {
            var container = listBox.ItemContainerGenerator.ContainerFromIndex(i);
            Assert.NotNull(container);
            var item = listBox.ItemContainerGenerator.ItemFromContainer(container!);
            Assert.Equal($"Person {i + 1}", item);
            if (container is ListBoxItem lbi)
                Assert.Equal($"Person {i + 1}", lbi.Content);
        }
    }

    private static void AssertFirstRealizedMatchesIndex(VirtualizingStackPanel host, TestListBox listBox, int expectedIndex)
    {
        var container = listBox.ItemContainerGenerator.ContainerFromIndex(expectedIndex);
        Assert.NotNull(container);
        var item = listBox.ItemContainerGenerator.ItemFromContainer(container!);
        Assert.Equal($"Person {expectedIndex + 1}", item);

        if (container is ListBoxItem lbi)
            Assert.Equal($"Person {expectedIndex + 1}", lbi.Content);
    }

    [Fact]
    public void ListBox_ItemsSourceSetAfterFirstMeasure_ShouldRealize()
    {
        // Regression: when ItemsSource is initially null during the first Measure
        // pass and is set afterwards (simulating the state a Binding produces
        // when DataContext is assigned after InitializeComponent), the panel
        // must realize items on the next Measure rather than wait for a scroll.
        var listBox = new TestListBox
        {
            Width = 320,
            Height = 240,
        };

        // First Measure/Arrange with ItemsSource = null.
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        Assert.Empty(host.Children);

        // Simulate the binding resolving by assigning ItemsSource after first layout.
        var items = Enumerable.Range(1, 500).Select(i => $"Item {i}").ToList();
        listBox.ItemsSource = items;

        // Re-layout — without the fix in ItemsControl.RefreshItems, the panel's
        // virtualization state (realization window / height index) remained at the
        // empty first-measure snapshot and realized nothing on this second pass.
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        Assert.True(host.Children.Count > 0, "Panel should realize items after ItemsSource becomes non-null");
        Assert.NotNull(listBox.ItemContainerGenerator.ContainerFromIndex(0));
    }

    [Fact]
    public void ClearContainerForItem_InvokedWhenRealizedItemRemoved()
    {
        // The generator must call ItemsControl.ClearContainerForItem before pooling a recycled
        // container. Without the T4 fix ClearContainerForItem is never invoked.
        var listBox = new ClearTrackingListBox { Width = 320, Height = 240 };
        for (var i = 0; i < 200; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        listBox.ClearedItems.Clear();

        // Removing a realized item recycles its container -> RemoveIndexInternal(recycle:true)
        // -> ClearContainerForItemInternal -> ClearContainerForItem.
        listBox.Items.RemoveAt(0);

        Assert.NotEmpty(listBox.ClearedItems);
        Assert.Contains("Item 0", listBox.ClearedItems);
    }

    [Fact]
    public void Recycle_RealizeRecycleRepop_NoThrow_AndContainersReparented()
    {
        var listBox = new TestListBox { Width = 320, Height = 240 };
        // Use a large item count so the realization window (viewport + 1-page cache each side) is a
        // small fraction of the list and recycling genuinely occurs even with tiny headless heights.
        for (var i = 0; i < 5000; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        Assert.True(host.Children.Count < 5000, "Panel should virtualize, not realize all rows.");

        // Scroll far down so the initial window is recycled out, then back to the top. In a headless
        // test SetVerticalOffset only invalidates the panel; the parent chain is already
        // measure-valid so listBox.Measure(sameSize) would short-circuit before reaching the panel.
        // Re-measure the panel directly to apply the new offset.
        host.SetVerticalOffset(3000);
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        host.SetVerticalOffset(0);
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        // The re-realized container must be reparented to the panel and show the correct item.
        var container = listBox.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.NotNull(container);
        Assert.Same(host, ((Visual)container!).VisualParent);
        Assert.Equal("Item 0", listBox.ItemContainerGenerator.ItemFromContainer(container!));
    }

    [Fact]
    public void OwnContainer_NotClearedWhenRecycledOut()
    {
        var listBox = new TestListBox { Width = 320, Height = 240 };
        var owns = new List<ListBoxItem>();
        for (var i = 0; i < 2000; i++)
        {
            var ownContainer = new ListBoxItem { Content = $"Own {i}" };
            owns.Add(ownContainer);
            listBox.Items.Add(ownContainer);
        }

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        // Scroll the first own-containers out of the realization window (re-measure the panel
        // directly; see note in Recycle_RealizeRecycleRepop_NoThrow_AndContainersReparented).
        host.SetVerticalOffset(1500);
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        // Own containers are exempt from clear+pool, so their content must survive recycling-out.
        Assert.Equal("Own 0", owns[0].Content);
    }

    [Fact]
    public void IncrementalAdd_InsideWindow_RealizesInsertedItemAndShiftsRest()
    {
        var items = new ObservableCollection<string>();
        for (var i = 0; i < 50; i++)
            items.Add($"Item {i}");

        var listBox = new TestListBox { Width = 320, Height = 240, ItemsSource = items };
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var gen = listBox.ItemContainerGenerator;

        items.Insert(2, "NEW");
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        var inserted = gen.ContainerFromIndex(2);
        Assert.NotNull(inserted);
        Assert.Equal("NEW", gen.ItemFromContainer(inserted!));

        // The item formerly at index 2 must have shifted to index 3 (not been overwritten/dropped).
        var shifted = gen.ContainerFromIndex(3);
        Assert.NotNull(shifted);
        Assert.Equal("Item 2", gen.ItemFromContainer(shifted!));
    }

    [Fact]
    public void IncrementalRemove_ShiftsTrailingItemsDown()
    {
        var items = new ObservableCollection<string>();
        for (var i = 0; i < 50; i++)
            items.Add($"Item {i}");

        var listBox = new TestListBox { Width = 320, Height = 240, ItemsSource = items };
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var gen = listBox.ItemContainerGenerator;

        items.RemoveAt(2); // removes "Item 2"
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        // "Item 3" should now occupy slot 2.
        var slot2 = gen.ContainerFromIndex(2);
        Assert.NotNull(slot2);
        Assert.Equal("Item 3", gen.ItemFromContainer(slot2!));
        Assert.Equal(49, gen.ItemCount);
    }

    [Fact]
    public void IncrementalMove_RoutesAsMoveNotReset_AndReorders()
    {
        var items = new ObservableCollection<string>();
        for (var i = 0; i < 50; i++)
            items.Add($"Item {i}");

        var listBox = new TestListBox { Width = 320, Height = 240, ItemsSource = items };
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var gen = listBox.ItemContainerGenerator;

        NotifyCollectionChangedAction? lastAction = null;
        gen.ItemsChanged += (_, e) => lastAction = e.Action;

        items.Move(2, 10);
        Assert.Equal(NotifyCollectionChangedAction.Move, lastAction);

        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        // "Item 2" moved to index 10; an item outside the moved span keeps its identity.
        var moved = gen.ContainerFromIndex(10);
        Assert.NotNull(moved);
        Assert.Equal("Item 2", gen.ItemFromContainer(moved!));
        var outside = gen.ContainerFromIndex(40);
        Assert.NotNull(outside);
        Assert.Equal("Item 40", gen.ItemFromContainer(outside!));
    }

    [Fact]
    public void IncrementalChanges_KeepChildrenAscendingByIndex()
    {
        var items = new ObservableCollection<string>();
        for (var i = 0; i < 50; i++)
            items.Add($"Item {i}");

        var listBox = new TestListBox { Width = 320, Height = 240, ItemsSource = items };
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var gen = listBox.ItemContainerGenerator;

        items.Insert(5, "A");
        items.Insert(1, "B");
        items.RemoveAt(10);
        items[3] = "R"; // Replace
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        // The panel's visual children must stay ascending by realized index (ShiftRealizedKeys
        // ordering invariant) so RealizeContainer's IndexOfKey-based insertion stays correct.
        var prev = -1;
        foreach (UIElement child in host.Children)
        {
            var idx = gen.IndexFromContainer(child);
            Assert.True(idx > prev, $"Children must be ascending by realized index (got {idx} after {prev}).");
            prev = idx;
        }

        Assert.Equal(51, gen.ItemCount); // 50 + 2 inserts - 1 remove
    }

    [Fact]
    public void TwoOffset_SetVerticalOffset_CommitsSynchronously()
    {
        var listBox = new TestListBox { Width = 320, Height = 240 };
        for (var i = 0; i < 5000; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        // The offset must be readable in the SAME call (synchronous commit) — no intervening Measure.
        host.SetVerticalOffset(1000);
        Assert.True(Math.Abs(host.VerticalOffset - 1000) <= 0.5,
            $"Expected synchronous read-back ~1000 but got {host.VerticalOffset}.");
    }

    [Fact]
    public void TwoOffset_PositiveInfinity_ScrollsToEndSynchronously()
    {
        var listBox = new TestListBox { Width = 320, Height = 240 };
        for (var i = 0; i < 5000; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        var expectedEnd = Math.Max(0, host.ExtentHeight - host.ViewportHeight);
        host.SetVerticalOffset(double.PositiveInfinity);

        Assert.True(host.VerticalOffset > 0, "Scroll-to-end should land past the top.");
        Assert.True(Math.Abs(host.VerticalOffset - expectedEnd) <= 0.5,
            $"+Infinity should resolve to the end ({expectedEnd}) but got {host.VerticalOffset}.");
    }

    [Fact]
    public void TwoOffset_NonVirtualizedPath_OffsetIsNotClampedToZero()
    {
        // Regression guard for the non-virtualized CoerceOffset fix: the height index is empty on the
        // non-virtualized path, so clamping must use the measured _extent, not GetSpacedExtent()==0.
        var panel = new VirtualizingStackPanel { Orientation = Orientation.Vertical };
        VirtualizingPanel.SetIsVirtualizing(panel, false);
        for (var i = 0; i < 20; i++)
            panel.Children.Add(new Border { Width = 100, Height = 30 });

        panel.Measure(new Size(100, 100));
        panel.Arrange(new Rect(0, 0, 100, 100));

        // extent = 20*30 = 600, viewport = 100 -> maxOffset = 500; 200 must NOT be clamped to 0.
        panel.SetVerticalOffset(200);
        Assert.True(Math.Abs(panel.VerticalOffset - 200) <= 0.5,
            $"Non-virtualized offset should round-trip to 200 but got {panel.VerticalOffset}.");
    }

    [Fact]
    public void AttachedProps_SetOnControl_GovernThePanel()
    {
        // Architectural-inversion fix (T3): props set on the CONTROL must govern the items host panel,
        // not be silently ignored. The panel reads them off its owning ItemsControl via GetOwner().
        var listBox = new TestListBox { Width = 320, Height = 240 };
        VirtualizingPanel.SetScrollUnit(listBox, ScrollUnit.Item);
        VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Standard);
        listBox.Items.Add("only");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        Assert.Equal(ScrollUnit.Item, host.ScrollUnit);
        Assert.Equal(VirtualizationMode.Standard, host.VirtualizationMode);
    }

    [Fact]
    public void IsVirtualizingFalse_OnControl_RealizesAllContainers()
    {
        // The pipeline gate (ItemsControl.ShouldUseVirtualizingPipeline) and the panel's
        // ShouldVirtualize() must agree on the owner's IsVirtualizing — otherwise IsVirtualizing=false
        // on the control yields an empty panel (split-brain). Here all items must be realized.
        var listBox = new TestListBox { Width = 320, Height = 240 };
        VirtualizingPanel.SetIsVirtualizing(listBox, false);
        for (var i = 0; i < 60; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        Assert.Equal(60, host.Children.Count);
    }

    [Fact]
    public void KeepAlive_IsContainerVirtualizableFalse_SurvivesScrollOut()
    {
        // PRE-0 (wave-2 prerequisite): a container pinned with IsContainerVirtualizable=false must
        // never be virtualized away, even when scrolled out of the realization window.
        var listBox = new TestListBox { Width = 320, Height = 240 };
        for (var i = 0; i < 5000; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var gen = listBox.ItemContainerGenerator;

        var pinned = gen.ContainerFromIndex(0);
        Assert.NotNull(pinned);
        VirtualizingPanel.SetIsContainerVirtualizable(pinned!, false);

        host.SetVerticalOffset(3000);
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        // The pinned container is kept alive...
        Assert.Same(pinned, gen.ContainerFromIndex(0));
        // ...while an ordinary out-of-window neighbour is recycled as usual.
        Assert.Null(gen.ContainerFromIndex(1));
    }

    [Fact]
    public void ItemsControlFromItemContainer_ResolvesOwningControl()
    {
        // T13 discovery seams: the items host is marked IsItemsHost, and GetItemsOwner /
        // ItemsControlFromItemContainer walk back to the owning control.
        var listBox = new TestListBox { Width = 320, Height = 240 };
        for (var i = 0; i < 50; i++)
            listBox.Items.Add($"Item {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        Assert.True(host.IsItemsHost);
        Assert.Same(listBox, ItemsControl.GetItemsOwner(host));

        var container = listBox.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.NotNull(container);
        Assert.Same(listBox, ItemsControl.ItemsControlFromItemContainer(container!));
    }

    [Fact]
    public void GetItemsOwner_FreestandingPanel_ReturnsNull()
    {
        var panel = new VirtualizingStackPanel();
        Assert.Null(ItemsControl.GetItemsOwner(panel));
    }

    private sealed class TestListBox : ListBox
    {
        public Panel? Host => ItemsHost;
    }

    private sealed class ClearTrackingListBox : ListBox
    {
        public Panel? Host => ItemsHost;

        public List<object> ClearedItems { get; } = new();

        protected override void ClearContainerForItem(FrameworkElement element, object item)
        {
            ClearedItems.Add(item);
            base.ClearContainerForItem(element, item);
        }
    }
}
