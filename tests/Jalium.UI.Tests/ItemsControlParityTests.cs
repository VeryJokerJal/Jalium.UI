using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ItemsControlParityTests
{
    [Fact]
    public void ApiSurface_ExposesWpfContainerLifecycleAndStateProperties()
    {
        var type = typeof(ItemsControl);
        var protectedInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(type.GetMethod("GetContainerForItemOverride", protectedInstance));
        Assert.NotNull(type.GetMethod("IsItemItsOwnContainerOverride", protectedInstance));
        Assert.NotNull(type.GetMethod("PrepareContainerForItemOverride", protectedInstance));
        Assert.NotNull(type.GetMethod("ClearContainerForItemOverride", protectedInstance));
        Assert.NotNull(type.GetMethod("ShouldApplyItemContainerStyle", protectedInstance));
        Assert.NotNull(type.GetMethod("OnItemsChanged", protectedInstance));
        Assert.NotNull(type.GetMethod("OnItemsSourceChanged", protectedInstance));

        Assert.True(typeof(Jalium.UI.Markup.IAddChild).IsAssignableFrom(type));
        Assert.True(ItemsControl.HasItemsProperty.ReadOnly);
        Assert.True(ItemsControl.IsGroupingProperty.ReadOnly);
        Assert.True(ItemsControl.AlternationIndexProperty.ReadOnly);
    }

    [Fact]
    public void HasItems_TracksDirectAndObservableSourceChanges_AndIsReadOnly()
    {
        var control = new ItemsControl();
        Assert.False(control.HasItems);
        Assert.Throws<InvalidOperationException>(() => control.SetValue(ItemsControl.HasItemsProperty, true));

        control.Items.Add("first");
        Assert.True(control.HasItems);
        control.Items.Clear();
        Assert.False(control.HasItems);

        var source = new ObservableCollection<string>();
        control.ItemsSource = source;
        Assert.False(control.HasItems);
        source.Add("source-item");
        Assert.True(control.HasItems);
        source.Clear();
        Assert.False(control.HasItems);
        Assert.Throws<InvalidOperationException>(() => control.Items.Add("local"));
    }

    [Fact]
    public void AlternationIndex_IsAssignedToMaterializedContainers()
    {
        var control = new ProbeItemsControl { AlternationCount = 2 };
        control.Items.Add("zero");
        control.Items.Add("one");
        control.Items.Add("two");
        Measure(control);

        Assert.NotNull(control.Host);
        Assert.Equal(3, control.Host!.Children.Count);
        Assert.Equal(0, ItemsControl.GetAlternationIndex(control.Host.Children[0]));
        Assert.Equal(1, ItemsControl.GetAlternationIndex(control.Host.Children[1]));
        Assert.Equal(0, ItemsControl.GetAlternationIndex(control.Host.Children[2]));

        control.AlternationCount = 3;
        Assert.Equal(2, ItemsControl.GetAlternationIndex(control.Host.Children[2]));
    }

    [Fact]
    public void WpfOverrideLifecycle_DrivesContainerCreationPreparationAndClear()
    {
        var control = new LifecycleItemsControl();
        control.Items.Add("item");
        Measure(control);

        var container = Assert.IsType<LifecycleContainer>(Assert.Single(control.Host!.Children));
        Assert.Equal(1, control.CreateCalls);
        Assert.Equal(1, control.PrepareCalls);
        Assert.Equal("item", container.Content);

        control.ClearForTest(container, "item");
        Assert.Equal(1, control.ClearCalls);
        Assert.Null(container.Content);
    }

    [Fact]
    public void ItemContainerStyleSelectorAndBindingGroup_AreAppliedWithoutOverwritingLocalValues()
    {
        var selectedStyle = new Style(typeof(ContentPresenter));
        var bindingGroup = new BindingGroup { Name = "items" };
        var control = new ProbeItemsControl
        {
            ItemContainerStyleSelector = new ConstantStyleSelector(selectedStyle),
            ItemBindingGroup = bindingGroup,
        };
        control.Items.Add("item");
        Measure(control);

        var container = Assert.IsType<ContentPresenter>(Assert.Single(control.Host!.Children));
        Assert.Same(selectedStyle, container.Style);
        Assert.Same(bindingGroup, container.BindingGroup);
    }

    [Fact]
    public void DisplayMemberPathAndItemStringFormat_CreateARealBindingTemplate()
    {
        var control = new ProbeItemsControl
        {
            DisplayMemberPath = nameof(DisplayItem.Name),
            ItemStringFormat = "Name: {0}",
        };
        var item = new DisplayItem { Name = "Jalium" };
        control.Items.Add(item);
        Measure(control);

        var container = Assert.IsType<ContentPresenter>(Assert.Single(control.Host!.Children));
        container.Measure(new Size(300, 100));
        var text = Assert.IsType<TextBlock>(container.GetVisualChild(0));
        Assert.Equal("Name: Jalium", text.Text);
        Assert.Same(item, container.Content);
    }

    [Fact]
    public void ContainerFromElement_WalksFromDescendantToOwningContainer()
    {
        var control = new ProbeItemsControl
        {
            ItemTemplate = new DataTemplate(),
        };
        control.ItemTemplate.SetVisualTree(() => new Border { Child = new TextBlock { Text = "child" } });
        control.Items.Add("item");
        Measure(control);

        var container = Assert.IsType<ContentPresenter>(Assert.Single(control.Host!.Children));
        container.Measure(new Size(300, 100));
        var border = Assert.IsType<Border>(container.GetVisualChild(0));
        var child = Assert.IsType<TextBlock>(border.Child);

        Assert.Same(container, control.ContainerFromElement(child));
        Assert.Same(container, ItemsControl.ContainerFromElement(control, child));
    }

    [Fact]
    public void GroupingAndSerializationState_TrackTheirBackingCollections()
    {
        var view = new CollectionView(new[] { new DisplayItem { Name = "A" } });
        var control = new ItemsControl { ItemsSource = view };
        Assert.False(control.IsGrouping);
        Assert.False(control.ShouldSerializeGroupStyle());

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DisplayItem.Name)));
        Assert.True(control.IsGrouping);

        control.GroupStyle.Add(new GroupStyle());
        Assert.True(control.ShouldSerializeGroupStyle());
        Assert.False(control.ShouldSerializeItems());
    }

    private static void Measure(FrameworkElement element)
    {
        element.Measure(new Size(300, 300));
        element.Arrange(new Rect(0, 0, 300, 300));
    }

    private class ProbeItemsControl : ItemsControl
    {
        public Panel? Host => ItemsHost;
    }

    private sealed class LifecycleItemsControl : ProbeItemsControl
    {
        public int CreateCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public int ClearCalls { get; private set; }

        protected override DependencyObject GetContainerForItemOverride()
        {
            CreateCalls++;
            return new LifecycleContainer();
        }

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            PrepareCalls++;
            base.PrepareContainerForItemOverride(element, item);
        }

        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            ClearCalls++;
            base.ClearContainerForItemOverride(element, item);
        }

        public void ClearForTest(DependencyObject element, object item) =>
            ClearContainerForItemOverride(element, item);
    }

    private sealed class LifecycleContainer : ContentControl
    {
    }

    private sealed class ConstantStyleSelector : StyleSelector
    {
        private readonly Style _style;

        public ConstantStyleSelector(Style style)
        {
            _style = style;
        }

        public override Style SelectStyle(object item, DependencyObject container) => _style;
    }

    private sealed class DisplayItem
    {
        public string Name { get; init; } = string.Empty;
    }
}
