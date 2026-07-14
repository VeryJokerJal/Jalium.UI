using Jalium.UI.Controls;
using Jalium.UI.Media;
using StatusBar = Jalium.UI.Controls.Primitives.StatusBar;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for app navigation and window chrome controls: the
/// WinUI-style <see cref="NavigationView"/>, tab strips, menu/command bars,
/// toolbars and status bars. Follows the per-category section pattern from
/// <c>GalleryWindow.Buttons.cs</c>.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildNavigationSection() => Section(
        "Navigation & Chrome",
        "App navigation, tabs, menus, toolbars and status bars.",
        Card("NavigationView", NavigationViewDemo(), width: 360, minHeight: 240),
        Card("TabControl", TabControlDemo(), width: 360),
        Card("MenuBar", MenuBarDemo(), width: 360),
        Card("CommandBar", CommandBarDemo(), width: 400),
        Card("Ribbon", Placeholder("Ribbon", "Office-style ribbon")),
        Card("ToolBar", ToolBarDemo(), width: 360),
        Card("StatusBar", StatusBarDemo(), width: 360),
        Card("Frame", Placeholder("Frame", "page navigation host")));

    private static UIElement NavigationViewDemo()
    {
        var nav = new NavigationView
        {
            PaneTitle = "Jalium",
            IsSettingsVisible = true,
            OpenPaneLength = 180,
            Width = 320,
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        nav.MenuItems.Add(new NavigationViewItem { Content = "Home", Icon = "", IsSelected = true });
        nav.MenuItems.Add(new NavigationViewItem { Content = "Documents", Icon = "" });
        nav.MenuItems.Add(new NavigationViewItem { Content = "Downloads", Icon = "" });
        nav.MenuItems.Add(new NavigationViewItem { Content = "Settings", Icon = "" });

        return nav;
    }

    private static UIElement TabControlDemo()
    {
        var tabs = new TabControl
        {
            TabStripPlacement = Dock.Top,
            Width = 320,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        tabs.Items.Add(new TabItem
        {
            Header = "Overview",
            Content = new TextBlock
            {
                Text = "Overview tab content.",
                Foreground = TextPrimary,
                FontSize = 14,
                Margin = new Thickness(12),
            },
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Details",
            Content = new TextBlock
            {
                Text = "Details tab content.",
                Foreground = TextSecondary,
                FontSize = 14,
                Margin = new Thickness(12),
            },
        });

        return tabs;
    }

    private static UIElement MenuBarDemo()
    {
        var menuBar = new MenuBar
        {
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        menuBar.Items.Add(new MenuBarItem { Title = "File" });
        menuBar.Items.Add(new MenuBarItem { Title = "Edit" });
        menuBar.Items.Add(new MenuBarItem { Title = "View" });
        menuBar.Items.Add(new MenuBarItem { Title = "Help" });

        return menuBar;
    }

    private static UIElement CommandBarDemo()
    {
        var bar = new CommandBar
        {
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
            Width = 360,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var add = new AppBarButton { Label = "Add" };
        add.SetIconGlyph(""); // Segoe Fluent "Add"

        var edit = new AppBarButton { Label = "Edit" };
        edit.SetIconGlyph(""); // "Edit"

        var share = new AppBarButton { Label = "Share" };
        share.SetIconGlyph(""); // "Share"

        bar.PrimaryCommands.Add(add);
        bar.PrimaryCommands.Add(edit);
        bar.PrimaryCommands.Add(share);

        return bar;
    }

    private static UIElement ToolBarDemo()
    {
        var toolBar = new ToolBar
        {
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        toolBar.Items.Add(new Button { Content = "Cut", Padding = new Thickness(12, 6, 12, 6) });
        toolBar.Items.Add(new Button { Content = "Copy", Padding = new Thickness(12, 6, 12, 6) });
        toolBar.Items.Add(new Button { Content = "Paste", Padding = new Thickness(12, 6, 12, 6) });

        return toolBar;
    }

    private static UIElement StatusBarDemo()
    {
        var statusBar = new StatusBar
        {
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        statusBar.Items.Add("Ready");
        statusBar.Items.Add(new Separator { Orientation = Orientation.Vertical });
        statusBar.Items.Add("Ln 1, Col 1");
        statusBar.Items.Add(new Separator { Orientation = Orientation.Vertical });
        statusBar.Items.Add("UTF-8");

        return statusBar;
    }
}
