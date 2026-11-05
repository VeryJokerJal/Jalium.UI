using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;
public class MenuCollectionSyncTests
{
    [Fact]
    public void MenuBar_MarkupAttachesBothTopLevelAndCommandItems()
    {
        var menu = Assert.IsType<MenuBar>(Jalium.UI.Markup.XamlReader.Parse("""
            <MenuBar xmlns="http://schemas.jalium.ui/2024">
                <MenuBarItem Title="File"><MenuFlyoutItem Text="Open" /></MenuBarItem>
            </MenuBar>
            """));
        var item = Assert.Single(menu.Items);
        Assert.Equal("File", item.Title);
        Assert.Equal("Open", Assert.IsType<MenuFlyoutItem>(Assert.Single(item.Items)).Text);
        Assert.Same(menu, item.ParentMenuBar);
    }

    [Fact]
    public void MenuBar_OwnershipTracksCollectionBeforeLayoutAndAfterReset()
    {
        var menu = new MenuBar();
        var item = new MenuBarItem
        {
            Title = "File"
        };
        menu.Items.Add(item);
        Assert.Same(menu, item.ParentMenuBar);
        menu.Items.Clear();
        Assert.Null(item.ParentMenuBar);
    }

    [Fact]
    public void MenuBar_ItemsChangedAfterInitialLayout_ShouldRefreshPanel()
    {
        var menuBar = LegacyMenuTemplateTestHelper.Apply(new MenuBar());
        var host = new StackPanel
        {
            Width = 400,
            Height = 60
        };
        host.Children.Add(menuBar);
        host.Measure(new Size(400, 60));
        host.Arrange(new Rect(0, 0, 400, 60));
        var panel = Assert.IsType<StackPanel>(menuBar.FindName("PART_ItemsPanel"));
        Assert.Empty(panel.Children);
        var fileItem = new MenuBarItem
        {
            Title = "File"
        };
        menuBar.Items.Add(fileItem);
        host.Measure(new Size(400, 60));
        host.Arrange(new Rect(0, 0, 400, 60));
        Assert.Single(panel.Children);
        Assert.Same(fileItem, panel.Children[0]);
        Assert.Same(menuBar, GetInternalProperty<MenuBar>(fileItem, "ParentMenuBar"));
        menuBar.Items.Remove(fileItem);
        host.Measure(new Size(400, 60));
        host.Arrange(new Rect(0, 0, 400, 60));
        Assert.Empty(panel.Children);
        Assert.Null(GetInternalProperty<MenuBar>(fileItem, "ParentMenuBar"));
    }

    [Fact]
    public void MenuBarItem_ItemsChangedAfterFlyoutCreated_ShouldSyncFlyoutItems()
    {
        var menuBarItem = new MenuBarItem
        {
            Title = "File"
        };
        menuBarItem.Items.Add(new MenuFlyoutItem { Text = "Open" });
        menuBarItem.OpenMenu();
        var flyout = GetPrivateField<MenuFlyout>(menuBarItem, "_flyout");
        Assert.Single(flyout.Items);
        menuBarItem.Items.Add(new MenuFlyoutItem { Text = "Save" });
        Assert.Equal(2, flyout.Items.Count);
        menuBarItem.Items.RemoveAt(0);
        Assert.Single(flyout.Items);
        Assert.Equal("Save", Assert.IsType<MenuFlyoutItem>(flyout.Items[0]).Text);
    }

    [Fact]
    public void MenuFlyout_ItemsChangedAfterPresenterCreated_ShouldRefreshPresenter()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Open" });
        var createPresenter = typeof(MenuFlyout).GetMethod("CreatePresenter", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(createPresenter);
        var presenter = Assert.IsAssignableFrom<Control>(createPresenter!.Invoke(flyout, null));
        LegacyMenuTemplateTestHelper.Register();
        presenter.Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[presenter.GetType()];
        presenter.ApplyTemplate();
        presenter.Measure(new Size(220, 120));
        presenter.Arrange(new Rect(0, 0, 220, 120));
        var scrollHost = GetPrivateField<MenuPopupScrollHost>(presenter, "_scrollHost");
        var itemsPanel = Assert.IsType<StackPanel>(scrollHost.ItemsHostPanel);
        Assert.Single(itemsPanel.Children);
        flyout.Items.Add(new MenuFlyoutItem { Text = "Save" });
        Assert.Equal(2, itemsPanel.Children.Count);
        flyout.Items.RemoveAt(0);
        Assert.Single(itemsPanel.Children);
        Assert.Equal("Save", Assert.IsType<MenuFlyoutItem>(itemsPanel.Children[0]).Text);
    }

    private static T GetPrivateField<T>(object owner, string fieldName)
    {
        var field = owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(owner);
        Assert.NotNull(value);
        return (T)value!;
    }

    private static T? GetInternalProperty<T>(object owner, string propertyName)
        where T : class
    {
        var property = owner.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(owner) as T;
    }
}
