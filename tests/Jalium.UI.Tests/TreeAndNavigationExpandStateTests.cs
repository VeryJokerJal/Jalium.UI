using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
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

    [Fact]
    public void TreeViewItem_Click_ShouldNotBlockAdjacentButtonHitTesting()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            Keyboard.Initialize();
            Keyboard.ClearFocus();
            UIElement.ForceReleaseMouseCapture();

            var tree = new TreeView
            {
                Width = 180,
                Height = 160,
                Style = Assert.IsType<Style>(app.Resources[typeof(TreeView)])
            };

            var rootItem = new TreeViewItem { Header = "Root" };
            rootItem.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            tree.Items.Add(rootItem);

            var buttonClicked = false;
            var button = new Button
            {
                Width = 100,
                Height = 32,
                Content = "Action",
                Style = Assert.IsType<Style>(app.Resources[typeof(Button)])
            };
            button.Click += (_, _) => buttonClicked = true;

            var layoutRoot = new Grid
            {
                Width = 320,
                Height = 160
            };
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(180) });
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(140) });
            layoutRoot.Children.Add(tree);
            Grid.SetColumn(button, 1);
            layoutRoot.Children.Add(button);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 160,
                Content = layoutRoot
            };

            window.Measure(new Size(320, 160));
            window.Arrange(new Rect(0, 0, 320, 160));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 20, y: 20);
            InvokeMouseButtonUp(window, MouseButton.Left, x: 20, y: 20);

            var buttonHit = InvokeHitTestElement(window, new Point(220, 20));
            Assert.Same(button, FindVisualAncestor<Button>(buttonHit));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 220, y: 20);
            InvokeMouseButtonUp(window, MouseButton.Left, x: 220, y: 20);

            Assert.True(buttonClicked);
        }
        finally
        {
            Keyboard.ClearFocus();
            UIElement.ForceReleaseMouseCapture();
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeViewItem_HeaderButton_ShouldReceiveClick()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            Keyboard.Initialize();
            Keyboard.ClearFocus();
            UIElement.ForceReleaseMouseCapture();

            var headerButtonClicked = false;
            var headerButton = new Button
            {
                Width = 64,
                Height = 24,
                Content = "Open",
                Style = Assert.IsType<Style>(app.Resources[typeof(Button)])
            };
            headerButton.Click += (_, _) => headerButtonClicked = true;

            var headerLayout = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            headerLayout.Children.Add(new TextBlock { Text = "Root" });
            headerLayout.Children.Add(headerButton);

            var rootItem = new TreeViewItem { Header = headerLayout };
            rootItem.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);

            var tree = new TreeView
            {
                Width = 260,
                Height = 120,
                Style = Assert.IsType<Style>(app.Resources[typeof(TreeView)])
            };
            tree.Items.Add(rootItem);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 260,
                Height = 120,
                Content = tree
            };

            window.Measure(new Size(260, 120));
            window.Arrange(new Rect(0, 0, 260, 120));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 70, y: 12);
            InvokeMouseButtonUp(window, MouseButton.Left, x: 70, y: 12);

            Assert.True(headerButtonClicked);
        }
        finally
        {
            Keyboard.ClearFocus();
            UIElement.ForceReleaseMouseCapture();
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeViewItem_HitTest_ShouldIgnoreTransparentAreaOutsideHeaderAndChildren()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var item = new TreeViewItem
            {
                Header = "Root",
                Width = 220,
                Height = 120,
                Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)])
            };

            item.Measure(new Size(220, 120));
            item.Arrange(new Rect(0, 0, 220, 120));

            var header = GetPrivateField<Border>(item, "_headerBorder");
            Assert.NotNull(header);
            Assert.True(header!.ActualHeight > 0);

            var headerHit = item.HitTest(new Point(12, 12));
            var transparentAreaHit = item.HitTest(new Point(12, 80));

            Assert.NotNull(headerHit);
            Assert.Null(transparentAreaHit);
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

    private static void InvokeMouseButtonDown(Window window, MouseButton button, int x, int y, int clickCount = 1)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, [button, (nint)0x0001, PackPointToLParam(x, y), clickCount]);
    }

    private static void InvokeMouseButtonUp(Window window, MouseButton button, int x, int y)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, [button, (nint)0x0000, PackPointToLParam(x, y)]);
    }

    private static UIElement? InvokeHitTestElement(Window window, Point point)
    {
        var method = typeof(Window).GetMethod("HitTestElement", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(window, [point, "treeview-regression"]) as UIElement;
    }

    private static nint PackPointToLParam(int x, int y)
    {
        int packed = (y << 16) | (x & 0xFFFF);
        return (nint)packed;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? start) where T : class
    {
        for (var current = start; current != null; current = (current as UIElement)?.VisualParent as DependencyObject)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
