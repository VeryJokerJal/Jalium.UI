using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public class VirtualizingPanelRemainingParityTests
{
    [Fact]
    public void VirtualizingPanel_RemainingSurface_MatchesWpfMetadata()
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.Static |
                                      BindingFlags.DeclaredOnly;

        PropertyInfo? generator =
            typeof(VirtualizingPanel).GetProperty(nameof(VirtualizingPanel.ItemContainerGenerator), declared);
        Assert.NotNull(generator);
        Assert.Equal(typeof(IItemContainerGenerator), generator!.PropertyType);
        Assert.NotNull(generator!.GetMethod);
        Assert.Null(generator!.SetMethod);

        MethodInfo? bring = typeof(VirtualizingPanel).GetMethod("BringIndexIntoView", declared);
        Assert.NotNull(bring);
        Assert.True(bring!.IsFamilyOrAssembly);
        Assert.True(bring!.IsVirtual);

        MethodInfo? bringPublic = typeof(VirtualizingPanel).GetMethod(
            nameof(VirtualizingPanel.BringIndexIntoViewPublic), declared);
        Assert.NotNull(bringPublic);
        Assert.True(bringPublic!.IsPublic);

        MethodInfo? clear = typeof(VirtualizingPanel).GetMethod("OnClearChildren", declared);
        Assert.NotNull(clear);
        Assert.True(clear!.IsFamily);
        Assert.True(clear!.IsVirtual);

        MethodInfo? should = typeof(VirtualizingPanel).GetMethod(
            nameof(VirtualizingPanel.ShouldItemsChangeAffectLayout), declared);
        Assert.NotNull(should);
        Assert.True(should!.IsPublic);
        Assert.False(should!.IsVirtual);

        MethodInfo? shouldCore = typeof(VirtualizingPanel).GetMethod(
            "ShouldItemsChangeAffectLayoutCore", declared);
        Assert.NotNull(shouldCore);
        Assert.True(shouldCore!.IsFamily);
        Assert.True(shouldCore!.IsVirtual);

        AssertProtectedVirtual(typeof(VirtualizingStackPanel), "OnCleanUpVirtualizedItem");
        AssertProtectedVirtual(typeof(VirtualizingStackPanel), "OnViewportOffsetChanged");
        AssertProtectedVirtual(typeof(VirtualizingStackPanel), "OnViewportSizeChanged");
    }

    [Fact]
    public void VirtualizingPanel_PublicWrappers_InvokeVirtualCallbacks()
    {
        var panel = new ContractProbePanel { ShouldAffectLayoutResult = false };
        var args = new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            new GeneratorPosition(-1, 1),
            itemCount: 1,
            itemUICount: 0,
            itemIndex: 12);

        panel.BringIndexIntoViewPublic(12);
        Assert.Equal(12, panel.BroughtIndex);

        Assert.False(panel.ShouldItemsChangeAffectLayout(false, args));
        Assert.False(panel.LastItemsChangeWasLocal);
        Assert.Same(args, panel.LastItemsChangedArgs);

        panel.Children.Add(new Border());
        panel.Children.Clear();
        Assert.Equal(1, panel.ClearChildrenCount);
    }

    [Fact]
    public void CleanUpVirtualizedItem_AttachedEvent_HasWpfShape()
    {
        RoutedEvent routedEvent = VirtualizingStackPanel.CleanUpVirtualizedItemEvent;
        Assert.Equal("CleanUpVirtualizedItemEvent", routedEvent.Name);
        Assert.Equal(RoutingStrategy.Direct, routedEvent.RoutingStrategy);
        Assert.Equal(typeof(CleanUpVirtualizedItemEventHandler), routedEvent.HandlerType);
        Assert.Equal(typeof(VirtualizingStackPanel), routedEvent.OwnerType);

        var element = new Border();
        var args = new CleanUpVirtualizedItemEventArgs("value", element);
        Assert.Same(routedEvent, args.RoutedEvent);
        Assert.Equal("value", args.Value);
        Assert.Same(element, args.UIElement);

        Assert.Throws<ArgumentNullException>(() =>
            VirtualizingStackPanel.AddCleanUpVirtualizedItemHandler(null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() =>
            VirtualizingStackPanel.AddCleanUpVirtualizedItemHandler(element, null!));
        Assert.Throws<ArgumentException>(() =>
            VirtualizingStackPanel.AddCleanUpVirtualizedItemHandler(
                new DependencyObject(),
                (_, _) => { }));
    }

    [Fact]
    public void CleanupEvent_Cancel_KeepsContainerRealized()
    {
        var listBox = CreateRealizedListBox(500, out VirtualizingStackPanel host);
        var firstContainer = Assert.IsAssignableFrom<UIElement>(
            listBox.ItemContainerGenerator.ContainerFromIndex(0));
        bool cleanupObserved = false;

        CleanUpVirtualizedItemEventHandler handler = (sender, args) =>
        {
            if (!ReferenceEquals(args.UIElement, firstContainer))
            {
                return;
            }

            cleanupObserved = true;
            Assert.Same(listBox, sender);
            Assert.Same(host, args.Source);
            Assert.Same(host, args.OriginalSource);
            Assert.Equal("Item 0", args.Value);
            args.Cancel = true;
        };

        VirtualizingStackPanel.AddCleanUpVirtualizedItemHandler(listBox, handler);
        try
        {
            host.SetVerticalOffset(5_000);
            host.Measure(new Size(320, 120));
            host.Arrange(new Rect(0, 0, 320, 120));

            Assert.True(cleanupObserved);
            Assert.Same(firstContainer, listBox.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Contains(firstContainer, host.Children.Cast<UIElement>());
        }
        finally
        {
            VirtualizingStackPanel.RemoveCleanUpVirtualizedItemHandler(listBox, handler);
        }
    }

    [Fact]
    public void VirtualizingStackPanel_ViewportHooks_FollowActualLayoutDataChanges()
    {
        var panel = new ViewportProbePanel();
        for (int i = 0; i < 20; i++)
        {
            panel.Children.Add(new Border { Height = 30 });
        }

        panel.Measure(new Size(100, 100));
        panel.Arrange(new Rect(0, 0, 100, 100));
        Assert.Contains(panel.SizeChanges, change => change.NewSize == new Size(100, 100));

        panel.SetVerticalOffset(50);
        Assert.Contains(panel.OffsetChanges, change =>
            change.OldOffset == new Vector(0, 0) &&
            change.NewOffset == new Vector(0, 50));

        int offsetChangeCount = panel.OffsetChanges.Count;
        panel.SetVerticalOffset(50);
        Assert.Equal(offsetChangeCount, panel.OffsetChanges.Count);

        panel.InvalidateMeasure();
        panel.Measure(new Size(100, 60));
        panel.Arrange(new Rect(0, 0, 100, 60));
        Assert.Contains(panel.SizeChanges, change => change.NewSize == new Size(100, 60));
    }

    [Fact]
    public void VirtualizingStackPanel_ItemsBeyondRealizedWindow_DoNotAffectFullViewportLayout()
    {
        var listBox = CreateRealizedListBox(500, out VirtualizingStackPanel host);

        var afterViewport = new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            new GeneratorPosition(-1, 1),
            itemCount: 1,
            itemUICount: 0,
            itemIndex: 499);
        var beforeViewport = new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            new GeneratorPosition(-1, 1),
            itemCount: 1,
            itemUICount: 0,
            itemIndex: 0);

        Assert.False(host.ShouldItemsChangeAffectLayout(true, afterViewport));
        Assert.True(host.ShouldItemsChangeAffectLayout(true, beforeViewport));
        Assert.True(host.ShouldItemsChangeAffectLayout(false, afterViewport));

        GC.KeepAlive(listBox);
    }

    private static TestListBox CreateRealizedListBox(
        int itemCount,
        out VirtualizingStackPanel host)
    {
        var listBox = new TestListBox { Width = 320, Height = 120 };
        for (int i = 0; i < itemCount; i++)
        {
            listBox.Items.Add($"Item {i}");
        }

        listBox.Measure(new Size(320, 120));
        listBox.Arrange(new Rect(0, 0, 320, 120));
        host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        return listBox;
    }

    private static void AssertProtectedVirtual(Type type, string methodName)
    {
        MethodInfo? method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method!.IsVirtual);
    }

    private sealed class TestListBox : ListBox
    {
        public Panel? Host => ItemsHost;
    }

    private sealed class ContractProbePanel : VirtualizingPanel
    {
        public int? BroughtIndex { get; private set; }

        public int ClearChildrenCount { get; private set; }

        public bool ShouldAffectLayoutResult { get; init; }

        public bool LastItemsChangeWasLocal { get; private set; }

        public ItemsChangedEventArgs? LastItemsChangedArgs { get; private set; }

        protected internal override void BringIndexIntoView(int index)
        {
            BroughtIndex = index;
        }

        protected override void OnClearChildren()
        {
            ClearChildrenCount++;
            base.OnClearChildren();
        }

        protected override bool ShouldItemsChangeAffectLayoutCore(
            bool areItemChangesLocal,
            ItemsChangedEventArgs args)
        {
            LastItemsChangeWasLocal = areItemChangesLocal;
            LastItemsChangedArgs = args;
            return ShouldAffectLayoutResult;
        }
    }

    private sealed class ViewportProbePanel : VirtualizingStackPanel
    {
        public List<(Size OldSize, Size NewSize)> SizeChanges { get; } = new();

        public List<(Vector OldOffset, Vector NewOffset)> OffsetChanges { get; } = new();

        protected override void OnViewportSizeChanged(Size oldViewportSize, Size newViewportSize)
        {
            SizeChanges.Add((oldViewportSize, newViewportSize));
            base.OnViewportSizeChanged(oldViewportSize, newViewportSize);
        }

        protected override void OnViewportOffsetChanged(Vector oldViewportOffset, Vector newViewportOffset)
        {
            OffsetChanges.Add((oldViewportOffset, newViewportOffset));
            base.OnViewportOffsetChanged(oldViewportOffset, newViewportOffset);
        }
    }
}
