using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

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
    public void VirtualizingPanel_HierarchicalSeams_VspEnablesHierarchicalVirtualization()
    {
        var panel = new VirtualizingStackPanel();
        Assert.True(panel.CanHierarchicallyScrollAndVirtualize);
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
        var stubImplementation = new HvasiStub();
        IHierarchicalVirtualizationAndScrollInfo stub = stubImplementation;

        var header = new HierarchicalVirtualizationHeaderDesiredSizes(new Size(1, 2), new Size(3, 4));
        var items = new HierarchicalVirtualizationItemDesiredSizes(
            new Size(1, 1), new Size(2, 2), new Size(3, 3), new Size(4, 4),
            new Size(5, 5), new Size(6, 6), new Size(7, 7), new Size(8, 8));

        stubImplementation.HeaderDesiredSizes = header;
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
    public void LazyItemsSource_IsNotReenumeratedByVirtualizedResizeOrScroll()
    {
        var source = new CountingEnumerable(100);
        var listBox = new TestListBox
        {
            Width = 320,
            Height = 120,
            ItemsSource = source,
        };

        // ItemCollection creates one stable CollectionView snapshot for a non-list
        // enumerable. The generator must index that view instead of walking the raw
        // source again for every Count/GetItemAt call.
        var snapshotMoveNextCount = source.MoveNextCount;
        Assert.Equal(100, snapshotMoveNextCount);

        listBox.Measure(new Size(320, 120));
        listBox.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        host.Measure(new Size(280, 160));
        host.Arrange(new Rect(0, 0, 280, 160));
        host.SetVerticalOffset(800);
        host.Measure(new Size(280, 160));
        host.Arrange(new Rect(0, 0, 280, 160));

        Assert.Equal(snapshotMoveNextCount, source.MoveNextCount);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(99));
    }

    [Fact]
    public void VirtualizingStackPanel_WidthResize_RemeasuresOnlyRealizedRowsWithNewConstraint()
    {
        var listBox = new ListBox { Width = 320, Height = 120 };
        var rows = new List<ConstraintTrackingListBoxItem>();
        for (int i = 0; i < 100; i++)
        {
            var row = new ConstraintTrackingListBoxItem();
            rows.Add(row);
            listBox.Items.Add(row);
        }

        listBox.Measure(new Size(320, 120));
        listBox.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.ItemsHostInternal);
        var first = Assert.IsType<ConstraintTrackingListBoxItem>(
            listBox.ItemContainerGenerator.ContainerFromIndex(0));

        host.Measure(new Size(180, 120));
        host.Arrange(new Rect(0, 0, 180, 120));

        Assert.Equal(180, first.LastAvailableSize.Width);
        Assert.Equal(180, host.ExtentWidth);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(99));
        Assert.True(host.Children.Count < 100);
    }

    [Fact]
    public void ViewportRecycling_RebindsExistingTemplateSubtrees()
    {
        var templateBuildCount = 0;
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            templateBuildCount++;
            return new TextBlock();
        });

        var listBox = new TemplatePresenterVirtualizingItemsControl
        {
            Width = 320,
            Height = 120,
            ItemTemplate = template,
            ItemsSource = Enumerable.Range(0, 200).Select(i => $"Item {i}").ToList(),
        };
        VirtualizingPanel.SetCacheLength(listBox, new VirtualizationCacheLength(0));

        listBox.Measure(new Size(320, 120));
        listBox.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        Assert.True(listBox.PrepareCount > 0);

        // First jump creates a second viewport, then leaves the first viewport's
        // detached containers in the recycle pool.
        host.SetVerticalOffset(900);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        var buildsAfterFirstJump = templateBuildCount;
        Assert.True(buildsAfterFirstJump > 0);
        Assert.True(listBox.ClearCount > 0);

        // The next jump consumes that pool. Content changes directly from the old
        // item to the new item under the same template, so no visual root is rebuilt.
        host.SetVerticalOffset(1800);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));

        Assert.Equal(buildsAfterFirstJump, templateBuildCount);
    }

    [Fact]
    public void ViewportRecyclePool_ReleasesPreservedContentWhenItemsSourceResets()
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock());
        var control = new TemplatePresenterVirtualizingItemsControl
        {
            Width = 320,
            Height = 120,
            ItemTemplate = template,
            ItemsSource = Enumerable.Range(0, 200).Select(i => $"Item {i}").ToList(),
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        control.Measure(new Size(320, 120));
        control.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(control.Host);
        var firstViewport = host.Children.OfType<ContentPresenter>().ToArray();
        Assert.NotEmpty(firstViewport);

        host.SetVerticalOffset(900);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        Assert.All(firstViewport, presenter => Assert.NotNull(presenter.Content));

        control.ItemsSource = Array.Empty<string>();

        Assert.All(firstViewport, presenter =>
        {
            Assert.Null(presenter.Content);
            Assert.Null(presenter.ContentTemplate);
        });
    }

    [Fact]
    public void ViewportRecyclePool_DoesNotClearCustomContainerContentWhenOverrideSkipsBase()
    {
        var control = new PersistentContentVirtualizingItemsControl
        {
            Width = 320,
            Height = 120,
            ItemsSource = Enumerable.Range(0, 200).Select(i => $"Item {i}").ToList(),
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        control.Measure(new Size(320, 120));
        control.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(control.Host);
        var firstViewport = host.Children
            .OfType<PersistentContentContainer>()
            .Select(container => (Container: container, Content: container.Content))
            .ToArray();
        Assert.NotEmpty(firstViewport);

        host.SetVerticalOffset(900);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        control.ItemsSource = Array.Empty<string>();

        Assert.All(firstViewport, entry => Assert.Same(entry.Content, entry.Container.Content));
    }

    [Fact]
    public void ViewportRecycling_ReevaluatesContainerStyleForNewItem()
    {
        var styleA = new Style(typeof(ContentPresenter));
        var styleB = new Style(typeof(ContentPresenter));
        var control = new TemplatePresenterVirtualizingItemsControl
        {
            Width = 320,
            Height = 120,
            ItemContainerStyleSelector = new PrefixStyleSelector(styleA, styleB),
            ItemsSource = Enumerable.Range(0, 300)
                .Select(i => i < 100 ? $"A {i}" : $"B {i}")
                .ToList(),
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        control.Measure(new Size(320, 120));
        control.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(control.Host);
        Assert.All(host.Children.OfType<ContentPresenter>(), presenter => Assert.Same(styleA, presenter.Style));

        // The first jump creates B containers and pools the A viewport. The second jump consumes
        // those A containers; their old selected style must have been cleared before B is applied.
        host.SetVerticalOffset(2400);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        host.SetVerticalOffset(4800);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));

        Assert.All(host.Children.OfType<ContentPresenter>(), presenter => Assert.Same(styleB, presenter.Style));
    }

    [Fact]
    public void VirtualizingWrapPanel_AutoSize_DoesNotReprobeIndexZeroAfterScroll()
    {
        var items = Enumerable.Range(0, 100).Select(i => $"Item {i}").ToList();
        var control = new TestVirtualizingWrapItemsControl
        {
            Width = 320,
            Height = 120,
            ItemsSource = items,
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        control.Measure(new Size(320, 120));
        control.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);

        host.SetVerticalOffset(600);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        Assert.Null(control.ItemContainerGenerator.ContainerFromIndex(0));
        var indexZeroPrepareCount = control.IndexZeroPrepareCount;

        // Force new layout constraints, as successive WM_SIZE notifications do.
        // Index zero is outside the realization window and must stay virtualized.
        host.Measure(new Size(321, 120));
        host.Arrange(new Rect(0, 0, 321, 120));
        host.Measure(new Size(322, 120));
        host.Arrange(new Rect(0, 0, 322, 120));

        Assert.Equal(indexZeroPrepareCount, control.IndexZeroPrepareCount);
        Assert.Null(control.ItemContainerGenerator.ContainerFromIndex(0));
    }

    [Fact]
    public void VirtualizingWrapPanel_ResizeKeepsHundredItemRealizationBounded()
    {
        var items = Enumerable.Range(0, 100).Select(i => $"Item {i}").ToList();
        var control = new TestVirtualizingWrapItemsControl
        {
            Width = 176,
            Height = 86,
            ItemsSource = items,
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        control.Measure(new Size(176, 86));
        control.Arrange(new Rect(0, 0, 176, 86));
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);
        host.ItemWidth = 80;
        host.ItemHeight = 40;
        host.HorizontalSpacing = 8;
        host.VerticalSpacing = 6;
        host.Measure(new Size(176, 86));
        host.Arrange(new Rect(0, 0, 176, 86));

        var first = Assert.IsAssignableFrom<UIElement>(
            control.ItemContainerGenerator.ContainerFromIndex(0));
        var second = Assert.IsAssignableFrom<UIElement>(
            control.ItemContainerGenerator.ContainerFromIndex(1));
        var third = Assert.IsAssignableFrom<UIElement>(
            control.ItemContainerGenerator.ContainerFromIndex(2));

        Assert.Equal(new Rect(0, 0, 80, 40), first.VisualBounds);
        Assert.Equal(new Rect(88, 0, 80, 40), second.VisualBounds);
        Assert.Equal(new Rect(0, 46, 80, 40), third.VisualBounds);
        Assert.Equal(2294, host.ExtentHeight);
        Assert.True(host.Children.Count < 20);
        Assert.Null(control.ItemContainerGenerator.ContainerFromIndex(99));

        // Simulate a live window-width change. The number of columns changes,
        // but work stays proportional to the visible realization window. Expansion is
        // committed only after the panel itself has received the wider Arrange; the follow-up
        // measure distinguishes a real resize from a speculative wide parent measure.
        host.Measure(new Size(264, 86));
        host.Arrange(new Rect(0, 0, 264, 86));
        Assert.False(host.IsMeasureValid);
        host.Measure(new Size(264, 86));
        host.Arrange(new Rect(0, 0, 264, 86));

        Assert.True(host.Children.Count < 20);
        Assert.Null(control.ItemContainerGenerator.ContainerFromIndex(99));
        Assert.Equal(1558, host.ExtentHeight);
    }

    [Fact]
    public void VirtualizingWrapPanel_AutoSizeRefreshesWhenRealizedContentChanges()
    {
        var items = Enumerable.Range(0, 100)
            .Select(_ => new MutableAutoSizeElement(new Size(80, 40)))
            .ToList();
        var control = new TestVirtualizingWrapItemsControl
        {
            Width = 220,
            Height = 120,
            ItemsSource = items,
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        control.Measure(new Size(220, 120));
        control.Arrange(new Rect(0, 0, 220, 120));
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);
        Assert.Equal(80, items[1].VisualBounds.X);

        foreach (var item in items)
        {
            item.NaturalSize = new Size(100, 50);
        }

        // This detached unit-test tree has no LayoutManager to propagate the children's
        // invalidations upward. In a Window that propagation is automatic; explicitly invalidate
        // the host here so the test exercises the panel's auto-size refresh branch.
        host.InvalidateMeasure();
        host.Measure(new Size(220, 120));
        host.Arrange(new Rect(0, 0, 220, 120));

        Assert.Equal(100, items[1].VisualBounds.X);
        Assert.Equal(2500, host.ExtentHeight);
        Assert.Null(control.ItemContainerGenerator.ContainerFromIndex(99));
    }

    [Fact]
    public void VirtualizingWrapPanel_NonVirtualizedHorizontalPathPreservesCellSpacing()
    {
        var control = CreateNonVirtualizedWrapControl();
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);
        host.Orientation = Orientation.Horizontal;

        control.Measure(new Size(176, 200));
        control.Arrange(new Rect(0, 0, 176, 200));

        Assert.Equal(new Rect(0, 0, 80, 40), host.Children[0].VisualBounds);
        Assert.Equal(new Rect(88, 0, 80, 40), host.Children[1].VisualBounds);
        Assert.Equal(new Rect(0, 46, 80, 40), host.Children[2].VisualBounds);
        Assert.Equal(86, host.ExtentHeight);
    }

    [Fact]
    public void VirtualizingWrapPanel_NonVirtualizedVerticalPathPreservesCellSpacing()
    {
        var control = CreateNonVirtualizedWrapControl();
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);
        host.Orientation = Orientation.Vertical;

        control.Measure(new Size(200, 92));
        control.Arrange(new Rect(0, 0, 200, 92));

        Assert.Equal(new Rect(0, 0, 80, 40), host.Children[0].VisualBounds);
        Assert.Equal(new Rect(0, 46, 80, 40), host.Children[1].VisualBounds);
        Assert.Equal(new Rect(88, 0, 80, 40), host.Children[2].VisualBounds);
        Assert.Equal(168, host.ExtentWidth);
    }

    [Fact]
    public void VirtualizingWrapPanel_NonVirtualizedPathUsesExtentForScrolling()
    {
        var control = CreateNonVirtualizedWrapControl();
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);

        control.Measure(new Size(80, 40));
        control.Arrange(new Rect(0, 0, 80, 40));
        Assert.True(host.ExtentHeight > host.ViewportHeight);

        host.SetVerticalOffset(46);
        host.Measure(new Size(80, 40));
        host.Arrange(new Rect(0, 0, 80, 40));

        Assert.Equal(46, host.VerticalOffset);
        Assert.Equal(-46, host.Children[0].VisualBounds.Y);
    }

    [Fact]
    public void VirtualizingWrapPanel_NonVirtualizedResizeClampsStaleOffset()
    {
        var control = CreateNonVirtualizedWrapControl();
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);

        control.Measure(new Size(80, 40));
        control.Arrange(new Rect(0, 0, 80, 40));
        host.SetVerticalOffset(92);
        host.Measure(new Size(80, 40));
        host.Arrange(new Rect(0, 0, 80, 40));
        Assert.Equal(92, host.VerticalOffset);

        host.Measure(new Size(80, 200));
        host.Arrange(new Rect(0, 0, 80, 200));

        Assert.Equal(0, host.VerticalOffset);
        Assert.Equal(0, host.Children[0].VisualBounds.Y);
    }

    private static TestVirtualizingWrapItemsControl CreateNonVirtualizedWrapControl()
    {
        var control = new TestVirtualizingWrapItemsControl();
        VirtualizingPanel.SetIsVirtualizing(control, false);
        control.ItemsSource = new[] { "Item 0", "Item 1", "Item 2" };
        var host = Assert.IsType<VirtualizingWrapPanel>(control.Host);
        host.ItemWidth = 80;
        host.ItemHeight = 40;
        host.HorizontalSpacing = 8;
        host.VerticalSpacing = 6;
        return control;
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

        // "Item 2" moved to index 10 and kept its identity.
        var moved = gen.ContainerFromIndex(10);
        Assert.NotNull(moved);
        Assert.Equal("Item 2", gen.ItemFromContainer(moved!));

        // The item it vacated shifted up to take its place.
        var shifted = gen.ContainerFromIndex(2);
        Assert.NotNull(shifted);
        Assert.Equal("Item 3", gen.ItemFromContainer(shifted!));

        // Index 11 is the first index past the moved span [2, 11), so it must be untouched.
        var outside = gen.ContainerFromIndex(11);
        Assert.NotNull(outside);
        Assert.Equal("Item 11", gen.ItemFromContainer(outside!));

        // Routing the change as a Move must not realize the rest of the list: index 40 is
        // far outside the realization window and stays virtualized. This assertion used to
        // read Assert.NotNull(ContainerFromIndex(40)), which only held because unthemed
        // containers measured to ~0 and the panel degenerated into realizing all 50 items.
        Assert.Null(gen.ContainerFromIndex(40));
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
    public void ReentrantHeadInsertDuringMeasure_RetryRealizesInsertedItem_NotStaleContainer()
    {
        // A reentrant structural mutation DURING the realize loop (the binding-side-effect scenario the
        // OnItemsChanged _measureInProgress branch exists for): PrepareContainerForItem inserts a new
        // item at the head while measure is in flight. The deferred retry must re-realize from the
        // post-mutation generator. Before the reset-before-retry fix, the retry reused the pre-mutation
        // _realizedContainers map (RealizeContainer returns a cached container without consulting the
        // generator), so index 0 kept showing the stale "Item 0".
        var items = new ObservableCollection<string>();
        for (var i = 0; i < 50; i++)
            items.Add($"Item {i}");

        var listBox = new ReentrantHeadInsertListBox(items) { Width = 320, Height = 240, ItemsSource = items };
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        Assert.Equal(51, items.Count); // exactly one reentrant insert fired during measure
        var gen = listBox.ItemContainerGenerator;

        var slot0 = gen.ContainerFromIndex(0);
        Assert.NotNull(slot0);
        Assert.Equal("INSERTED", gen.ItemFromContainer(slot0!));

        // The item formerly at index 0 shifted to index 1 — not overwritten or dropped.
        var slot1 = gen.ContainerFromIndex(1);
        Assert.NotNull(slot1);
        Assert.Equal("Item 0", gen.ItemFromContainer(slot1!));
    }

    private sealed class ReentrantHeadInsertListBox : ListBox
    {
        private readonly ObservableCollection<string> _items;
        private bool _mutated;

        public ReentrantHeadInsertListBox(ObservableCollection<string> items) => _items = items;

        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        {
            base.PrepareContainerForItem(element, item);

            // Fire exactly once, from inside the in-flight realize loop, so OnItemsChanged sees
            // _measureInProgress and defers to MeasureOverride's bounded retry.
            if (!_mutated)
            {
                _mutated = true;
                _items.Insert(0, "INSERTED");
            }
        }
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

    // Containers are pinned to a fixed height so the realization window is identical
    // whether or not a theme dictionary happens to be loaded in this process.
    //
    // Without the pin these tests are a coin flip. Implicit styles resolve through
    // Application.Current.Resources, so with no Application a ListBoxItem gets no
    // ControlTemplate and measures to ~0 (ItemHeightIndex clamps it to 1px): 50 rows span
    // 50px, the 480px realization window swallows the list, and every item realizes —
    // silently turning the virtualization assertions below into no-ops. Once any earlier
    // test in the process constructs an Application, the themed style applies
    // (MinHeight=28 plus font-dependent content) and only the first ~16 rows realize.
    // Test collections run in a random order — xUnit's DefaultTestCollectionOrderer keys
    // off ITestCollection.UniqueID, a Guid freshly minted per process — so which state a
    // run lands in varies between identical runs even though the assembly is serial.
    //
    // Height and MinHeight are set as local values: they outrank the theme's implicit
    // style while leaving it applied. An explicit ItemContainerStyle would instead
    // displace the theme's default style, leaving the container with no template.
    private const double ItemHeight = 24;

    private static ListBoxItem CreateFixedHeightContainer()
        => new() { Height = ItemHeight, MinHeight = ItemHeight };

    private sealed class TestListBox : ListBox
    {
        public Panel? Host => ItemsHost;

        protected override FrameworkElement GetContainerForItem(object item)
            => CreateFixedHeightContainer();
    }

    private sealed class CountingEnumerable : IEnumerable
    {
        private readonly int _count;

        public CountingEnumerable(int count) => _count = count;

        public int MoveNextCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                MoveNextCount++;
                yield return $"Item {i}";
            }
        }
    }

    private sealed class ConstraintTrackingListBoxItem : ListBoxItem
    {
        public Size LastAvailableSize { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            LastAvailableSize = availableSize;
            return new Size(availableSize.Width, ItemHeight);
        }
    }

    private sealed class MutableAutoSizeElement : FrameworkElement
    {
        private Size _naturalSize;

        public MutableAutoSizeElement(Size naturalSize) => _naturalSize = naturalSize;

        public Size NaturalSize
        {
            get => _naturalSize;
            set
            {
                if (_naturalSize == value)
                {
                    return;
                }

                _naturalSize = value;
                InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize) => _naturalSize;
    }

    private sealed class TestVirtualizingWrapItemsControl : ItemsControl
    {
        public TestVirtualizingWrapItemsControl()
        {
            ItemsPanel = new ItemsPanelTemplate { PanelType = typeof(VirtualizingWrapPanel) };
        }

        public Panel? Host => ItemsHost;
        public int IndexZeroPrepareCount { get; private set; }

        protected override FrameworkElement GetContainerForItem(object item)
            => new Border { Width = 80, Height = 40 };

        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        {
            base.PrepareContainerForItem(element, item);
            if (Equals(item, "Item 0"))
            {
                IndexZeroPrepareCount++;
            }
        }
    }

    private sealed class TemplatePresenterVirtualizingItemsControl : ItemsControl
    {
        public TemplatePresenterVirtualizingItemsControl()
        {
            ItemsPanel = new ItemsPanelTemplate { PanelType = typeof(VirtualizingStackPanel) };
        }

        public Panel? Host => ItemsHost;
        public int PrepareCount { get; private set; }
        public int ClearCount { get; private set; }

        protected override FrameworkElement GetContainerForItem(object item)
            => new ContentPresenter { Height = 24 };

        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        {
            PrepareCount++;
            base.PrepareContainerForItem(element, item);
        }

        protected override void ClearContainerForItem(FrameworkElement element, object item)
        {
            ClearCount++;
            base.ClearContainerForItem(element, item);
        }
    }

    private sealed class PrefixStyleSelector(Style styleA, Style styleB) : StyleSelector
    {
        public override Style? SelectStyle(object item, DependencyObject container)
            => item is string text && text.StartsWith("A ", StringComparison.Ordinal)
                ? styleA
                : styleB;
    }

    private sealed class PersistentContentVirtualizingItemsControl : ItemsControl
    {
        public PersistentContentVirtualizingItemsControl()
        {
            ItemsPanel = new ItemsPanelTemplate { PanelType = typeof(VirtualizingStackPanel) };
        }

        public Panel? Host => ItemsHost;

        protected override FrameworkElement GetContainerForItem(object item)
            => new PersistentContentContainer();

        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        {
            if (element is PersistentContentContainer container)
            {
                container.BoundItem = item;
                return;
            }

            base.PrepareContainerForItem(element, item);
        }

        protected override void ClearContainerForItem(FrameworkElement element, object item)
        {
            if (element is PersistentContentContainer container)
            {
                container.BoundItem = null;
                return;
            }

            base.ClearContainerForItem(element, item);
        }
    }

    private sealed class PersistentContentContainer : ContentControl
    {
        public PersistentContentContainer()
        {
            Height = 24;
            Content = new Border();
        }

        public object? BoundItem { get; set; }
    }

    private sealed class ClearTrackingListBox : ListBox
    {
        public Panel? Host => ItemsHost;

        public List<object> ClearedItems { get; } = new();

        protected override FrameworkElement GetContainerForItem(object item)
            => CreateFixedHeightContainer();

        protected override void ClearContainerForItem(FrameworkElement element, object item)
        {
            ClearedItems.Add(item);
            base.ClearContainerForItem(element, item);
        }
    }
}
