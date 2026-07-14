using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using TabDock = Jalium.UI.Controls.Dock;

namespace Jalium.UI.Tests;

public sealed class TabControlParityTests
{
    [Fact]
    public void TabControlAndTabItemExposeTheWpfDependencyPropertySurface()
    {
        foreach (var fieldName in new[]
                 {
                     nameof(TabControl.ContentStringFormatProperty),
                     nameof(TabControl.ContentTemplateProperty),
                     nameof(TabControl.ContentTemplateSelectorProperty),
                     nameof(TabControl.SelectedContentProperty),
                     nameof(TabControl.SelectedContentStringFormatProperty),
                     nameof(TabControl.SelectedContentTemplateProperty),
                     nameof(TabControl.SelectedContentTemplateSelectorProperty),
                 })
        {
            var field = typeof(TabControl).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            Assert.Same(typeof(DependencyProperty), field!.FieldType);
        }

        Assert.False(TabControl.ContentStringFormatProperty.ReadOnly);
        Assert.False(TabControl.ContentTemplateProperty.ReadOnly);
        Assert.False(TabControl.ContentTemplateSelectorProperty.ReadOnly);
        Assert.True(TabControl.SelectedContentProperty.ReadOnly);
        Assert.True(TabControl.SelectedContentStringFormatProperty.ReadOnly);
        Assert.True(TabControl.SelectedContentTemplateProperty.ReadOnly);
        Assert.True(TabControl.SelectedContentTemplateSelectorProperty.ReadOnly);

        Assert.Null(typeof(TabControl).GetProperty(nameof(TabControl.SelectedContent))!.SetMethod);
        Assert.Null(typeof(TabControl).GetProperty(nameof(TabControl.SelectedContentStringFormat))!.SetMethod);
        Assert.Null(typeof(TabControl).GetProperty(nameof(TabControl.SelectedContentTemplate))!.SetMethod);
        Assert.Null(typeof(TabControl).GetProperty(nameof(TabControl.SelectedContentTemplateSelector))!.SetMethod);

        var tabControl = new TabControl();
        Assert.Throws<InvalidOperationException>(() =>
            tabControl.SetValue(TabControl.SelectedContentProperty, new object()));

        Assert.Same(Selector.IsSelectedProperty, TabItem.IsSelectedProperty);
        Assert.True(TabItem.TabStripPlacementProperty.ReadOnly);
        Assert.Null(typeof(TabItem).GetProperty(nameof(TabItem.TabStripPlacement))!.SetMethod);
        AssertVirtual<TabItem>("OnSelected", typeof(RoutedEventArgs));
        AssertVirtual<TabItem>("OnUnselected", typeof(RoutedEventArgs));

        var tabItem = new TabItem();
        Assert.Equal(TabDock.Top, tabItem.TabStripPlacement);
        Assert.Throws<InvalidOperationException>(() =>
            tabItem.SetValue(TabItem.TabStripPlacementProperty, TabDock.Left));
    }

    [Fact]
    public void SelectionSynchronizesContentFormattingAndSelectedTabMutations()
    {
        var first = new TabItem { Header = "First", Content = 12.34 };
        var second = new TabItem { Header = "Second", Content = 99.0 };
        var tabControl = new TestTabControl { ContentStringFormat = "value={0:0.0}" };

        tabControl.Items.Add(first);
        tabControl.Items.Add(second);

        Assert.Equal(0, tabControl.SelectedIndex);
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.Equal(12.34, tabControl.SelectedContent);
        Assert.Equal("value={0:0.0}", tabControl.SelectedContentStringFormat);
        Assert.Equal("value=12.3", Assert.IsType<TextBlock>(tabControl.SelectedPresenter.GetVisualChild(0)).Text);

        first.Content = 45.67;
        Assert.Equal(45.67, tabControl.SelectedContent);
        Assert.Equal("value=45.7", Assert.IsType<TextBlock>(tabControl.SelectedPresenter.GetVisualChild(0)).Text);

        second.Content = 100.0;
        Assert.Equal(45.67, tabControl.SelectedContent);

        tabControl.SelectedIndex = 1;
        Assert.Equal(100.0, tabControl.SelectedContent);
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);

        tabControl.SelectedIndex = -1;
        Assert.Null(tabControl.SelectedContent);
        Assert.Null(tabControl.SelectedContentTemplate);
        Assert.Null(tabControl.SelectedContentTemplateSelector);
        Assert.Null(tabControl.SelectedContentStringFormat);
        Assert.Null(tabControl.TryGetSelectedPresenter());
    }

    [Fact]
    public void ItemTemplateOverridesFallbackAndTemplateSelectorIsActuallyInvoked()
    {
        var fallbackTemplate = CreateTemplate("fallback");
        var replacementFallbackTemplate = CreateTemplate("replacement-fallback");
        var itemTemplate = CreateTemplate("item");
        var selectedTemplate = CreateTemplate("selector");
        var selector = new RecordingTemplateSelector(selectedTemplate);

        var first = new TabItem { Header = "First", Content = "first-content" };
        var second = new TabItem
        {
            Header = "Second",
            Content = "second-content",
            ContentTemplate = itemTemplate,
        };
        var tabControl = new TestTabControl { ContentTemplate = fallbackTemplate };
        tabControl.Items.Add(first);
        tabControl.Items.Add(second);

        Assert.Same(fallbackTemplate, tabControl.SelectedContentTemplate);
        Assert.Equal("fallback", Assert.IsType<Border>(tabControl.SelectedPresenter.GetVisualChild(0)).Tag);

        tabControl.ContentTemplate = replacementFallbackTemplate;
        tabControl.ContentStringFormat = "fallback={0}";
        Assert.Same(replacementFallbackTemplate, tabControl.SelectedContentTemplate);
        Assert.Equal("fallback={0}", tabControl.SelectedContentStringFormat);
        Assert.Equal("replacement-fallback", Assert.IsType<Border>(tabControl.SelectedPresenter.GetVisualChild(0)).Tag);

        tabControl.SelectedIndex = 1;
        Assert.Same(itemTemplate, tabControl.SelectedContentTemplate);
        Assert.Null(tabControl.SelectedContentStringFormat);
        Assert.Equal("item", Assert.IsType<Border>(tabControl.SelectedPresenter.GetVisualChild(0)).Tag);

        second.ContentStringFormat = "item={0}";
        Assert.Same(itemTemplate, tabControl.SelectedContentTemplate);
        Assert.Equal("item={0}", tabControl.SelectedContentStringFormat);

        second.ClearValue(ContentControl.ContentStringFormatProperty);
        Assert.Same(itemTemplate, tabControl.SelectedContentTemplate);
        Assert.Null(tabControl.SelectedContentStringFormat);

        second.ClearValue(ContentControl.ContentTemplateProperty);
        Assert.Equal("fallback={0}", tabControl.SelectedContentStringFormat);
        Assert.Same(replacementFallbackTemplate, tabControl.SelectedContentTemplate);

        second.ContentTemplateSelector = selector;

        Assert.Null(tabControl.SelectedContentTemplate);
        Assert.Same(selector, tabControl.SelectedContentTemplateSelector);
        Assert.Equal("selector", Assert.IsType<Border>(tabControl.SelectedPresenter.GetVisualChild(0)).Tag);
        Assert.True(selector.CallCount > 0);
        Assert.Equal("second-content", selector.LastItem);
        Assert.Same(tabControl.SelectedPresenter, selector.LastContainer);
    }

    [Fact]
    public void SelectionEventsPlacementAndDirectIsSelectedChangesStaySynchronized()
    {
        var first = new TrackingTabItem { Header = "First", Content = "one" };
        var second = new TrackingTabItem { Header = "Second", Content = "two" };
        var tabControl = new TabControl();
        tabControl.Items.Add(first);
        tabControl.Items.Add(second);
        first.ResetSelectionHooks();
        second.ResetSelectionHooks();

        RoutedEventArgs? selectedArgs = null;
        RoutedEventArgs? unselectedArgs = null;
        Selector.AddSelectedHandler(second, (_, e) => selectedArgs = e);
        Selector.AddUnselectedHandler(first, (_, e) => unselectedArgs = e);

        tabControl.SelectedIndex = 1;

        Assert.Equal(1, first.UnselectedCalls);
        Assert.Equal(1, second.SelectedCalls);
        Assert.Same(first, unselectedArgs!.Source);
        Assert.Same(second, selectedArgs!.Source);
        Assert.Same(Selector.UnselectedEvent, unselectedArgs.RoutedEvent);
        Assert.Same(Selector.SelectedEvent, selectedArgs.RoutedEvent);

        tabControl.TabStripPlacement = TabDock.Left;
        Assert.Equal(TabDock.Left, first.TabStripPlacement);
        Assert.Equal(TabDock.Left, second.TabStripPlacement);

        first.IsSelected = true;
        Assert.Equal(0, tabControl.SelectedIndex);
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);

        first.IsSelected = false;
        Assert.Equal(-1, tabControl.SelectedIndex);
        Assert.Null(tabControl.SelectedContent);
    }

    [Fact]
    public void GeneratedContainersUseTheItemAsHeaderAndContentWithoutStealingSelectedContentTemplate()
    {
        var headerTemplate = CreateTemplate("header");
        var item = new Payload("generated");
        var tabControl = new TestTabControl
        {
            ItemTemplate = headerTemplate,
            TabStripPlacement = TabDock.Right,
        };

        var container = tabControl.CreateAndPrepareContainer(item);

        Assert.Same(item, container.Header);
        Assert.Same(item, container.Content);
        Assert.Same(headerTemplate, container.HeaderTemplate);
        Assert.Null(container.ContentTemplate);
        Assert.Equal(TabDock.Right, container.TabStripPlacement);

        tabControl.ClearPreparedContainer(container, item);
        Assert.Null(container.Header);
        Assert.Null(container.Content);
        Assert.False(container.IsSelected);
        Assert.Equal(TabDock.Top, container.TabStripPlacement);
    }

    private static DataTemplate CreateTemplate(string tag)
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new Border { Tag = tag });
        return template;
    }

    private static void AssertVirtual<T>(string name, params Type[] parameterTypes)
    {
        var method = typeof(T).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method!.IsVirtual);
        Assert.False(method.IsFinal);
    }

    private sealed class TestTabControl : TabControl
    {
        public ContentPresenter SelectedPresenter =>
            TryGetSelectedPresenter() ?? throw new InvalidOperationException("No selected content presenter exists.");

        public ContentPresenter? TryGetSelectedPresenter()
        {
            for (var i = 0; i < VisualChildrenCount; i++)
            {
                if (GetVisualChild(i) is ContentPresenter presenter)
                {
                    return presenter;
                }
            }

            return null;
        }

        public TabItem CreateAndPrepareContainer(object item)
        {
            var container = Assert.IsType<TabItem>(GetContainerForItemOverride());
            PrepareContainerForItemOverride(container, item);
            return container;
        }

        public void ClearPreparedContainer(TabItem container, object item) =>
            ClearContainerForItemOverride(container, item);
    }

    private sealed class TrackingTabItem : TabItem
    {
        public int SelectedCalls { get; private set; }
        public int UnselectedCalls { get; private set; }

        public void ResetSelectionHooks()
        {
            SelectedCalls = 0;
            UnselectedCalls = 0;
        }

        protected override void OnSelected(RoutedEventArgs e)
        {
            SelectedCalls++;
            base.OnSelected(e);
        }

        protected override void OnUnselected(RoutedEventArgs e)
        {
            UnselectedCalls++;
            base.OnUnselected(e);
        }
    }

    private sealed class RecordingTemplateSelector : Jalium.UI.Controls.DataTemplateSelector
    {
        private readonly DataTemplate _template;

        public RecordingTemplateSelector(DataTemplate template)
        {
            _template = template;
        }

        public int CallCount { get; private set; }
        public object? LastItem { get; private set; }
        public DependencyObject? LastContainer { get; private set; }

        public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        {
            CallCount++;
            LastItem = item;
            LastContainer = container;
            return _template;
        }
    }

    private sealed record Payload(string Name);
}
