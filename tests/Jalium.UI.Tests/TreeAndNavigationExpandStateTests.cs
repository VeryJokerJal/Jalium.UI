using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class TreeAndNavigationExpandStateTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void TreeViewItem_DefaultExpanded_ShouldSyncArrowAndChildrenPanel()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var item = new TreeViewItem { Header = "Root", IsExpanded = true };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            item.Items.Add(new TreeViewItem { Header = "Child" });

            item.ApplyTemplate();

            var itemsHost = GetPrivateField<StackPanel>(item, "_itemsHost");
            var expanderBorder = GetPrivateField<Border>(item, "_expanderBorder");
            var expanderArrow = GetPrivateField<Jalium.UI.Controls.Shapes.Path>(item, "_expanderArrow");

            Assert.NotNull(itemsHost);
            Assert.NotNull(expanderBorder);
            Assert.NotNull(expanderArrow);
            Assert.Equal(Visibility.Visible, itemsHost!.Visibility);
            Assert.Equal(Visibility.Visible, expanderBorder!.Visibility);
            Assert.Equal(90d, GetAngle(expanderArrow!), 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeViewItem_AddChildrenAfterTemplate_WhenExpanded_ShouldKeepArrowExpanded()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var item = new TreeViewItem { Header = "Root", IsExpanded = true };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            item.ApplyTemplate();

            item.Items.Add(new TreeViewItem { Header = "Child" });

            var itemsHost = GetPrivateField<StackPanel>(item, "_itemsHost");
            var expanderBorder = GetPrivateField<Border>(item, "_expanderBorder");
            var expanderArrow = GetPrivateField<Jalium.UI.Controls.Shapes.Path>(item, "_expanderArrow");

            Assert.NotNull(itemsHost);
            Assert.NotNull(expanderBorder);
            Assert.NotNull(expanderArrow);
            Assert.Equal(Visibility.Visible, itemsHost!.Visibility);
            Assert.Equal(Visibility.Visible, expanderBorder!.Visibility);
            Assert.Equal(90d, GetAngle(expanderArrow!), 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationViewItem_DefaultExpanded_ShouldSyncChevronAndChildrenPanel()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var item = new NavigationViewItem { Content = "Root", IsExpanded = true };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);
            item.MenuItems.Add(new NavigationViewItem { Content = "Child" });
            nav.MenuItems.Add(item);
            nav.UpdateMenuItems();

            var childrenPanel = GetPrivateField<StackPanel>(item, "_childrenPanel");
            var chevron = GetPrivateField<Jalium.UI.Controls.Shapes.Path>(item, "_chevron");

            Assert.NotNull(childrenPanel);
            Assert.NotNull(chevron);
            Assert.Equal(Visibility.Visible, childrenPanel!.Visibility);
            Assert.Equal(Visibility.Visible, chevron!.Visibility);
            Assert.Equal(90d, GetAngle(chevron), 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationView_UpdateMenuItems_ShouldRefreshChevronForLateChildren()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var root = new NavigationViewItem { Content = "Root", IsExpanded = true };
            root.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);

            nav.MenuItems.Add(root);
            nav.UpdateMenuItems();

            root.MenuItems.Add(new NavigationViewItem { Content = "Child" });
            nav.UpdateMenuItems();

            var chevron = GetPrivateField<Jalium.UI.Controls.Shapes.Path>(root, "_chevron");

            Assert.NotNull(chevron);
            Assert.Equal(Visibility.Visible, chevron!.Visibility);
            Assert.Equal(90d, GetAngle(chevron), 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationView_MenuItemsAdd_ShouldRefreshWithoutManualUpdate()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var item = new NavigationViewItem { Content = "AutoRefresh" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);

            nav.MenuItems.Add(item);

            var menuPanel = GetPrivateField<StackPanel>(nav, "_menuItemsPanel");
            Assert.NotNull(menuPanel);
            Assert.Single(menuPanel!.Children);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationView_FooterItemsAdd_ShouldRefreshWithoutManualUpdate()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var item = new NavigationViewItem { Content = "FooterItem" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);

            nav.FooterMenuItems.Add(item);

            var footerPanel = GetPrivateField<StackPanel>(nav, "_footerItemsPanel");
            Assert.NotNull(footerPanel);
            Assert.Single(footerPanel!.Children);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationView_PreloadedItems_ShouldRemainVisibleAfterAttach()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var item = new NavigationViewItem { Content = "Preloaded" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);
            nav.MenuItems.Add(item);

            var host = new Grid();
            host.Children.Add(nav);

            var menuPanel = GetPrivateField<StackPanel>(nav, "_menuItemsPanel");
            Assert.NotNull(menuPanel);
            Assert.Single(menuPanel!.Children);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(instance) as T;
    }

    private static double GetAngle(Jalium.UI.Controls.Shapes.Path path)
    {
        return path.RenderTransform is RotateTransform rotate ? rotate.Angle : 0;
    }
}
