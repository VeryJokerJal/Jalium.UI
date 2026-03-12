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
    public void TreeViewItem_Expand_ShouldStartAnimationTimer()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var tree = new TreeView();
            var item = new TreeViewItem { Header = "Root" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            item.Items.Add(new TreeViewItem { Header = "Child" });
            tree.Items.Add(item);

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(tree);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            item.IsExpanded = true;

            var itemsHost = GetPrivateField<StackPanel>(item, "_itemsHost");
            var expanderArrow = GetPrivateField<Jalium.UI.Controls.Shapes.Path>(item, "_expanderArrow");
            var expandAnimTimer = GetPrivateField<Jalium.UI.Threading.DispatcherTimer>(item, "_expandAnimTimer");

            Assert.NotNull(expandAnimTimer);
            Assert.True(expandAnimTimer!.IsEnabled);
            Assert.Equal(Visibility.Visible, itemsHost!.Visibility);
            Assert.InRange(GetAngle(expanderArrow!), 0d, 90d);

            expandAnimTimer.Stop();
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
    public void NavigationViewItem_Expand_ShouldStartAnimationTimer()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var item = new NavigationViewItem { Content = "Root" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);
            item.MenuItems.Add(new NavigationViewItem { Content = "Child" });
            nav.MenuItems.Add(item);
            nav.UpdateMenuItems();

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(nav);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            item.IsExpanded = true;

            var childrenPanel = GetPrivateField<StackPanel>(item, "_childrenPanel");
            var chevron = GetPrivateField<Jalium.UI.Controls.Shapes.Path>(item, "_chevron");
            var expandAnimTimer = GetPrivateField<Jalium.UI.Threading.DispatcherTimer>(item, "_expandAnimTimer");

            Assert.NotNull(childrenPanel);
            Assert.NotNull(chevron);
            Assert.Equal(Visibility.Visible, childrenPanel!.Visibility);
            Assert.NotNull(expandAnimTimer);
            Assert.True(expandAnimTimer!.IsEnabled);
            Assert.InRange(GetAngle(chevron!), 0d, 90d);

            expandAnimTimer.Stop();
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
    public void NavigationViewItem_StopExpandAnimation_ShouldClearChildRenderOffsets()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var nav = new NavigationView();
            var item = new NavigationViewItem { Content = "Root" };
            var child = new NavigationViewItem { Content = "Child" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);
            child.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);
            item.MenuItems.Add(child);
            nav.MenuItems.Add(item);
            nav.UpdateMenuItems();

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(nav);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            item.IsExpanded = true;

            Assert.NotEqual(default, GetRenderOffset(child));

            InvokePrivateMethod(item, "StopExpandAnimation");

            Assert.Equal(default, GetRenderOffset(child));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeViewItem_StopExpandAnimation_ShouldClearChildRenderOffsets()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var tree = new TreeView();
            var item = new TreeViewItem { Header = "Root" };
            var child = new TreeViewItem { Header = "Child" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            child.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            item.Items.Add(child);
            tree.Items.Add(item);

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(tree);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            item.IsExpanded = true;

            Assert.NotEqual(default, GetRenderOffset(child));

            InvokePrivateMethod(item, "StopExpandAnimation");

            Assert.Equal(default, GetRenderOffset(child));
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
    public void Expander_Expand_ShouldApplyStateImmediately_WithoutAnimationTimer()
    {
        ResetApplicationState();

        try
        {
            var expander = new Expander();
            var contentBorder = new Border();
            var chevron = new Jalium.UI.Controls.Shapes.Path();

            SetPrivateField(expander, "_contentBorder", contentBorder);
            SetPrivateField(expander, "_chevron", chevron);

            expander.IsExpanded = true;

            var animationTimer = GetPrivateField<Jalium.UI.Threading.DispatcherTimer>(expander, "_animationTimer");

            Assert.Equal(Visibility.Visible, contentBorder.Visibility);
            Assert.Equal(90d, GetAngle(chevron), 3);
            Assert.Null(animationTimer);
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

    [Fact]
    public void TreeViewItem_HeaderHover_ShouldOnlyHighlightHoveredHeader()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var parent = new TreeViewItem { Header = "Parent", IsExpanded = true };
            var child = new TreeViewItem { Header = "Child" };
            parent.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            child.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            parent.Items.Add(child);

            parent.ApplyTemplate();
            child.ApplyTemplate();

            var parentHeader = GetPrivateField<Border>(parent, "_headerBorder");
            var childHeader = GetPrivateField<Border>(child, "_headerBorder");
            var hoverBrush = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackgroundHover"]);

            Assert.NotNull(parentHeader);
            Assert.NotNull(childHeader);

            childHeader!.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, childHeader));

            Assert.Same(hoverBrush, childHeader.Background);
            Assert.NotSame(hoverBrush, parentHeader!.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeViewItem_SelectedHover_ShouldUsePressedAccentBrush()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var item = new TreeViewItem { Header = "Node", IsSelected = true };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            item.ApplyTemplate();

            var header = GetPrivateField<Border>(item, "_headerBorder");
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var selectedHoverBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrushPressed"]);

            Assert.NotNull(header);
            Assert.Same(selectionBrush, header!.Background);

            header.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, header));
            Assert.Same(selectedHoverBrush, header.Background);

            header.RaiseEvent(new RoutedEventArgs(UIElement.MouseLeaveEvent, header));
            Assert.Same(selectionBrush, header.Background);
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

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static double GetAngle(Jalium.UI.Controls.Shapes.Path path)
    {
        return path.RenderTransform is RotateTransform rotate ? rotate.Angle : 0;
    }

    private static Point GetRenderOffset(UIElement element)
    {
        var property = typeof(UIElement).GetProperty("RenderOffset", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (Point)(property!.GetValue(element) ?? default(Point));
    }

    private static void InvokePrivateMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, null);
    }
}
