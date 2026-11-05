using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSSubmenuPopupTemplateTests
{
    [Fact]
    public void MenuItemPopupPartsAndChromeAreProvidedByTheTemplate()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "File" });
        item.Items.Add(new MenuItem { Header = "Open" });
        var popup = Part<Popup>(item, "PART_Popup");
        var border = Part<Border>(item, "PART_PopupBorder");
        var host = Part<MenuPopupScrollHost>(item, "PART_ScrollHost");
        Assert.Same(border, popup.Child);
        Assert.Same(host, border.Child);
        item.IsSubmenuOpen = true;
        Assert.True(popup.IsOpen);
        Assert.Same(item.Items[0], Assert.Single(host.Items));
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(item, "SubmenuStates"));
        item.IsSubmenuOpen = false;
        Assert.Equal("Closed", VisualStateManager.GetCurrentStateName(item, "SubmenuStates"));
    }

    [Fact]
    public void MenuItemRetemplateRetainsEqualButDistinctDataContainersAndEvents()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "Data" });
        var a = new Entry("Equal");
        var b = new Entry("Equal");
        item.Items.Add(a);
        item.Items.Add(b);
        int opened = 0, closed = 0;
        item.SubmenuOpened += (_, _) => opened++;
        item.SubmenuClosed += (_, _) => closed++;
        item.IsSubmenuOpen = true;
        var oldPopup = Part<Popup>(item, "PART_Popup");
        var oldHost = Part<MenuPopupScrollHost>(item, "PART_ScrollHost");
        var original = oldHost.Items.ToArray();
        var template = item.Template;
        item.Template = Blank(typeof(MenuItem));
        item.ApplyTemplate();
        Assert.True(item.IsSubmenuOpen);
        Assert.False(oldPopup.IsOpen);
        Assert.Empty(oldHost.Items);
        item.Template = template;
        item.ApplyTemplate();
        var current = Part<MenuPopupScrollHost>(item, "PART_ScrollHost");
        Assert.Equal(original, current.Items.ToArray());
        Assert.Same(a, Assert.IsType<MenuItem>(current.Items[0]).Header);
        Assert.Same(b, Assert.IsType<MenuItem>(current.Items[1]).Header);
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);
        oldPopup.IsOpen = true;
        oldPopup.IsOpen = false;
        Assert.True(item.IsSubmenuOpen);
        item.IsSubmenuOpen = false;
        Assert.Equal(1, closed);
    }

    [Fact]
    public void MenuItemPopupInheritsTheContextMenuFrame()
    {
        var owner = LegacyMenuTemplateTestHelper.Apply(new ContextMenu { Background = Brushes.Navy, BorderBrush = Brushes.Orange, Padding = new Thickness(9), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(2) });
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "More" });
        item.Items.Add(new MenuItem { Header = "Child" });
        owner.Items.Add(item);
        owner.IsOpen = true;
        item.IsSubmenuOpen = true;
        var border = Part<Border>(item, "PART_PopupBorder");
        Assert.Same(Brushes.Navy, border.Background);
        Assert.Same(Brushes.Orange, border.BorderBrush);
        Assert.Equal(new Thickness(9), border.Padding);
        Assert.Equal(new CornerRadius(13), border.CornerRadius);
        Assert.Equal(new Thickness(2), border.BorderThickness);
        owner.Close();
        item.IsSubmenuOpen = false;
    }

    [Fact]
    public void FlyoutSubitemModelsHaveNoConstructorVisualsAndKeepLogicalBindings()
    {
        var sub = new MenuFlyoutSubItem
        {
            Text = "More",
            DataContext = "First"
        };
        var leaf = new MenuFlyoutItem();
        BindingOperations.SetBinding(leaf, MenuFlyoutItem.TextProperty, new Binding());
        sub.Items.Add(leaf);
        Assert.Equal(0, sub.VisualChildrenCount);
        Assert.Same(sub.Items, sub.GetValue(MenuFlyoutSubItem.ItemsProperty));
        Assert.True(sub.HasItems);
        Assert.Same(sub, leaf.LogicalParentInternal);
        Assert.Equal("First", leaf.Text);
        Apply(sub);
        sub.ShowSubMenu();
        Assert.True(sub.IsSubMenuOpen);
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(sub, "SubmenuStates"));
        sub.DataContext = "Second";
        Assert.Equal("Second", leaf.Text);
        sub.HideSubMenu();
    }

    [Fact]
    public void FlyoutSubitemOpenRetemplateReleasesOldPartsWithoutLosingIntent()
    {
        var sub = Apply(new MenuFlyoutSubItem { Text = "More" });
        var leaf = new MenuFlyoutItem
        {
            Text = "Open"
        };
        sub.Items.Add(leaf);
        sub.ShowSubMenu();
        var oldPopup = Part<Popup>(sub, "PART_Popup");
        var oldHost = Part<MenuPopupScrollHost>(sub, "PART_ScrollHost");
        var template = sub.Template;
        sub.Template = Blank(typeof(MenuFlyoutSubItem));
        sub.ApplyTemplate();
        Assert.True(sub.IsSubMenuOpen);
        Assert.False(oldPopup.IsOpen);
        Assert.Empty(oldHost.Items);
        sub.Template = template;
        sub.ApplyTemplate();
        var current = Part<MenuPopupScrollHost>(sub, "PART_ScrollHost");
        Assert.True(Part<Popup>(sub, "PART_Popup").IsOpen);
        Assert.Same(leaf, Assert.Single(current.Items));
        current.Measure(new Size(200, 100));
        current.Arrange(new Rect(0, 0, 200, 100));
        var label = Part<TextBlock>(leaf, "PART_Text");
        Assert.Equal("Open", label.Text);
        Assert.True(label.RenderSize.Width > 0, $"Leaf={leaf.RenderSize}, label={label.RenderSize}");
        oldPopup.IsOpen = true;
        oldPopup.IsOpen = false;
        oldHost.Items.Add(new MenuFlyoutItem { Text = "Retired" });
        oldHost.Measure(new Size(200, 200));
        oldHost.Arrange(new Rect(0, 0, 200, 200));
        Assert.True(sub.IsSubMenuOpen);
        Assert.Same(leaf, Assert.Single(current.Items));
        sub.HideSubMenu();
    }

    [Fact]
    public void FlyoutSubitemCollectionUpdatesKeepContainersAndClearLogicalParents()
    {
        var sub = Apply(new MenuFlyoutSubItem { Text = "More" });
        var first = new MenuFlyoutItem
        {
            Text = "First"
        };
        sub.Items.Add(first);
        sub.ShowSubMenu();
        var host = Part<MenuPopupScrollHost>(sub, "PART_ScrollHost");
        var second = new MenuFlyoutItem
        {
            Text = "Second"
        };
        sub.Items.Insert(0, second);
        Assert.Same(second, host.Items[0]);
        Assert.Same(first, host.Items[1]);
        sub.Items.Remove(first);
        Assert.Null(first.LogicalParentInternal);
        Assert.Same(second, Assert.Single(host.Items));
        sub.Items.Clear();
        Assert.Empty(host.Items);
        Assert.Null(second.LogicalParentInternal);
        Assert.False(sub.HasItems);
        Assert.False(sub.IsSubMenuOpen);
    }

    [Fact]
    public void DisabledItemFamiliesCloseAndRejectNewSubmenus()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "More" });
        item.Items.Add(new MenuItem { Header = "Child" });
        item.IsSubmenuOpen = true;
        item.IsEnabled = false;
        Assert.False(item.IsSubmenuOpen);
        Assert.False(Part<Popup>(item, "PART_Popup").IsOpen);
        item.IsSubmenuOpen = true;
        Assert.False(item.IsSubmenuOpen);
        var sub = Apply(new MenuFlyoutSubItem { Text = "More" });
        sub.Items.Add(new MenuFlyoutItem { Text = "Child" });
        sub.ShowSubMenu();
        sub.IsEnabled = false;
        Assert.False(sub.IsSubMenuOpen);
        Assert.False(Part<Popup>(sub, "PART_Popup").IsOpen);
        sub.ShowSubMenu();
        Assert.False(sub.IsSubMenuOpen);
    }

    [Fact]
    public void MissingPopupPartsKeepModelStateWithoutCreatingFallbackUi()
    {
        var item = new MenuItem
        {
            Template = Blank(typeof(MenuItem))
        };
        item.Items.Add(new MenuItem());
        item.ApplyTemplate();
        item.IsSubmenuOpen = true;
        Assert.True(item.IsSubmenuOpen);
        Assert.Null(item.FindName("PART_Popup"));
        item.IsSubmenuOpen = false;
        var sub = new MenuFlyoutSubItem
        {
            Template = Blank(typeof(MenuFlyoutSubItem))
        };
        sub.Items.Add(new MenuFlyoutItem());
        sub.ApplyTemplate();
        sub.ShowSubMenu();
        Assert.True(sub.IsSubMenuOpen);
        Assert.Null(sub.FindName("PART_Popup"));
        sub.HideSubMenu();
        Assert.False(sub.IsSubMenuOpen);
    }

    [Fact]
    public void PreparedContainersAvoidRepeatedSelectorCallsOnOpenAndRetemplate()
    {
        var selector = new CountingSelector();
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "Data", UsesItemContainerTemplate = true, ItemContainerTemplateSelector = selector });
        item.Items.Add(new Entry("Data"));
        item.IsSubmenuOpen = true;
        var original = Assert.Single(Part<MenuPopupScrollHost>(item, "PART_ScrollHost").Items);
        Assert.Equal(1, selector.Calls);
        var template = item.Template;
        item.Template = Blank(typeof(MenuItem));
        item.ApplyTemplate();
        item.Template = template;
        item.ApplyTemplate();
        Assert.Same(original, Assert.Single(Part<MenuPopupScrollHost>(item, "PART_ScrollHost").Items));
        Assert.Equal(1, selector.Calls);
        item.IsSubmenuOpen = false;
    }

    private sealed class CountingSelector : ItemContainerTemplateSelector
    {
        private readonly ItemContainerTemplate _template = new();
        internal int Calls { get; private set; }

        internal CountingSelector() => _template.SetVisualTree(() => new MenuItem { Header = "Generated" });
        public override DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl)
        {
            Calls++;
            return _template;
        }
    }

    private sealed record Entry(string Text);
    private static T Part<T>(Control item, string name)
        where T : class => Assert.IsType<T>(item.FindName(name));
    private static ControlTemplate Blank(Type type)
    {
        var result = new ControlTemplate(type);
        result.SetVisualTree(() => new Border());
        return result;
    }

    private static MenuFlyoutSubItem Apply(MenuFlyoutSubItem item)
    {
        LegacyMenuTemplateTestHelper.Register();
        MenuFlyoutTemplateTestHelper.Apply(item);
        return item;
    }
}
