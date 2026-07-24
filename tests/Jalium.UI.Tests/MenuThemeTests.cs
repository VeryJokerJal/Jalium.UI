using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MenuThemeTests
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
    public void Menu_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var menu = new Menu();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(menu);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(Menu), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(menu.HasLocalValue(Control.BackgroundProperty));
            Assert.False(menu.HasLocalValue(Control.ForegroundProperty));
            Assert.False(menu.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.NotNull(menu.Background);
            Assert.NotNull(menu.Foreground);
            Assert.Equal(32, menu.Height);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ContextMenu_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var contextMenu = new ContextMenu();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(contextMenu);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(ContextMenu), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(contextMenu.HasLocalValue(Control.BackgroundProperty));
            Assert.False(contextMenu.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(contextMenu.HasLocalValue(Control.PaddingProperty));
            Assert.False(contextMenu.HasLocalValue(Control.CornerRadiusProperty));
            Assert.NotNull(contextMenu.Background);
            Assert.NotNull(contextMenu.BorderBrush);
            Assert.Equal(5, contextMenu.Padding.Left);
            Assert.Equal(14, contextMenu.CornerRadius.TopLeft);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuItem_InternalBrushResolution_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextSecondary", out var textSecondaryObj));

            var menuItem = new MenuItem();
            var resolveMethod = typeof(MenuItem).GetMethod("ResolveMenuBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolveMethod);

            var brush = resolveMethod!.Invoke(menuItem, new object[]
            {
                "TextSecondary",
                new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.Black)
            });

            Assert.Same(textSecondaryObj, brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuForegroundResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var menuItem = new MenuItem();
            var menuBarItem = new MenuBarItem();
            var flyoutItem = new MenuFlyoutItem();

            Assert.Same(textPrimary, InvokePrivateBrushResolver(menuItem, "ResolvePrimaryTextBrush"));
            Assert.Same(textPrimary, InvokePrivateBrushResolver(menuBarItem, "ResolveForegroundBrush"));
            Assert.Same(textPrimary, InvokePrivateBrushResolver(flyoutItem, "ResolveForegroundBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuItem_SubmenuPopupChrome_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var parent = new MenuItem { Header = "Root" };
            parent.Items.Add(new MenuItem { Header = "Child" });
            parent.IsSubmenuOpen = true;

            Assert.True(app.Resources.TryGetValue("MenuFlyoutPresenterBackground", out var bgObj));
            Assert.True(app.Resources.TryGetValue("MenuFlyoutPresenterBorderBrush", out var borderObj));

            var borderField = typeof(MenuItem).GetField("_submenuBorder", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(borderField);
            var border = Assert.IsType<Border>(borderField!.GetValue(parent));

            Assert.Same(bgObj, border.Background);
            Assert.Same(borderObj, border.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuItem_ImplicitThemeStyle_ShouldApplyWithoutLocalLayoutOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var menuItem = new MenuItem { Header = "File" };
            var host = new StackPanel { Width = 240, Height = 60 };
            host.Children.Add(menuItem);

            host.Measure(new Size(240, 60));
            host.Arrange(new Rect(0, 0, 240, 60));

            Assert.True(app.Resources.TryGetValue(typeof(MenuItem), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(menuItem.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.False(menuItem.HasLocalValue(Control.PaddingProperty));
            Assert.Equal(32, menuItem.Height);
            Assert.Equal(10, menuItem.Padding.Left);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ContextMenu_PopupChrome_ShouldUpdateWhenThemePropertiesChange()
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = "Item" });
        contextMenu.Open(new Point(10, 10));

        var borderField = typeof(ContextMenu).GetField("_popupBorder", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(borderField);
        var border = Assert.IsType<Border>(borderField!.GetValue(contextMenu));

        var background = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(11, 22, 33));
        var borderBrush = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(44, 55, 66));
        contextMenu.Background = background;
        contextMenu.BorderBrush = borderBrush;
        contextMenu.Padding = new Jalium.UI.Thickness(9);
        contextMenu.CornerRadius = new Jalium.UI.CornerRadius(7);

        Assert.Same(background, border.Background);
        Assert.Same(borderBrush, border.BorderBrush);
        Assert.Equal(9, border.Padding.Left);
        Assert.Equal(7, border.CornerRadius.TopLeft);
    }

    [Fact]
    public void ContextMenu_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var contextMenuBackground = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutPresenterBackground"]);
            var contextMenuBorder = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutPresenterBorderBrush"]);
            var contextMenu = new ContextMenu();

            Assert.Same(contextMenuBackground, InvokePrivateBrushResolver(contextMenu, "ResolvePopupBackgroundBrush"));
            Assert.Same(contextMenuBorder, InvokePrivateBrushResolver(contextMenu, "ResolvePopupBorderBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(ContextMenu contextMenu, string methodName)
    {
        var method = typeof(ContextMenu).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(contextMenu, null));
    }

    private static Brush InvokePrivateBrushResolver(Control control, string methodName)
    {
        var method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, null));
    }
}
