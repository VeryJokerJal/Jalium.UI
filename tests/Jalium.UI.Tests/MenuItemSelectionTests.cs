using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MenuItemSelectionTests
{
    [Fact]
    public void MenuItem_IsSelected_ShouldTrackMouseKeyboardAndSubmenuState()
    {
        var item = new MenuItem { Header = "File" };

        Assert.False(item.IsSelected);

        item.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, item));
        Assert.True(item.IsSelected);

        item.RaiseEvent(new RoutedEventArgs(UIElement.MouseLeaveEvent, item));
        Assert.False(item.IsSelected);

        var updateFocusMethod = typeof(UIElement).GetMethod("UpdateIsKeyboardFocused", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(updateFocusMethod);

        updateFocusMethod!.Invoke(item, [true]);
        Assert.True(item.IsSelected);

        updateFocusMethod.Invoke(item, [false]);
        Assert.False(item.IsSelected);

        var parent = new MenuItem { Header = "Parent" };
        parent.Items.Add(new MenuItem { Header = "Child" });

        parent.IsSubmenuOpen = true;
        Assert.True(parent.IsSelected);

        parent.IsSubmenuOpen = false;
        Assert.False(parent.IsSelected);
    }

    [Fact]
    public void MenuItem_SelectedStyle_ShouldUseRoleSpecificThemeBrushes()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var rootHost = new StackPanel { Width = 360, Height = 120 };

            var menu = new Menu();
            var topLevelItem = new MenuItem { Header = "File" };
            topLevelItem.Items.Add(new MenuItem { Header = "Open" });
            menu.Items.Add(topLevelItem);
            rootHost.Children.Add(menu);

            var submenuItem = new MenuItem { Header = "Save" };
            rootHost.Children.Add(submenuItem);

            rootHost.Measure(new Size(360, 120));
            rootHost.Arrange(new Rect(0, 0, 360, 120));

            var menuBarHover = Assert.IsAssignableFrom<Brush>(app.Resources["MenuBarItemBackgroundHover"]);
            var flyoutHover = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutItemBackgroundHover"]);

            Assert.Equal(MenuItemRole.TopLevelHeader, topLevelItem.Role);
            topLevelItem.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, topLevelItem));
            Assert.True(topLevelItem.IsSelected);
            Assert.Same(menuBarHover, topLevelItem.Background);

            Assert.Equal(MenuItemRole.SubmenuItem, submenuItem.Role);
            submenuItem.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, submenuItem));
            Assert.True(submenuItem.IsSelected);
            Assert.Same(flyoutHover, submenuItem.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuItem_Role_ShouldUpdateWhenParentOrChildrenChange()
    {
        var menu = new Menu();
        var topLevelItem = new MenuItem { Header = "File" };
        menu.Items.Add(topLevelItem);

        var host = new StackPanel();
        host.Children.Add(menu);
        host.Measure(new Size(240, 80));
        host.Arrange(new Rect(0, 0, 240, 80));

        Assert.Equal(MenuItemRole.TopLevelItem, topLevelItem.Role);

        topLevelItem.Items.Add(new MenuItem { Header = "Open" });
        Assert.Equal(MenuItemRole.TopLevelHeader, topLevelItem.Role);

        var submenuItem = new MenuItem { Header = "Save" };
        host.Children.Add(submenuItem);
        host.Measure(new Size(240, 120));
        host.Arrange(new Rect(0, 0, 240, 120));

        Assert.Equal(MenuItemRole.SubmenuItem, submenuItem.Role);

        submenuItem.Items.Add(new MenuItem { Header = "Save As" });
        Assert.Equal(MenuItemRole.SubmenuHeader, submenuItem.Role);
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}
