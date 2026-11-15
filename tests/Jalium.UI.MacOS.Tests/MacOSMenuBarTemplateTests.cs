using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSMenuBarTemplateTests
{
    [Fact]
    public void ModelsHaveNoVisualTreeUntilTheTemplateProvidesItsPanel()
    {
        var bar = new MenuBar();
        var item = new MenuBarItem
        {
            Title = "File"
        };
        bar.Items.Add(item);
        Assert.Equal(0, bar.VisualChildrenCount);
        Assert.Same(bar, item.ParentMenuBar);
        LegacyMenuTemplateTestHelper.Apply(bar);
        Layout(bar);
        var panel = Assert.IsType<StackPanel>(bar.FindName("PART_ItemsPanel"));
        Assert.Same(item, Assert.Single(panel.Children));
        Assert.Same(bar, item.LogicalParentInternal);
        Assert.True(bar.HasItems);
        Assert.Same(bar.Items, bar.GetValue(MenuBar.ItemsProperty));
        Assert.Equal("File", Assert.IsType<TextBlock>(item.FindName("PART_Title")).Text);
    }

    [Fact]
    public void RetemplateRetiresThePreviousPanelAndPreservesOriginalItems()
    {
        var bar = new MenuBar();
        var file = new MenuBarItem
        {
            Title = "File"
        };
        bar.Items.Add(file);
        LegacyMenuTemplateTestHelper.Apply(bar);
        Layout(bar);
        var old = Assert.IsType<StackPanel>(bar.FindName("PART_ItemsPanel"));
        var original = bar.Template;
        var blank = new ControlTemplate(typeof(MenuBar));
        blank.SetVisualTree(() => new Border());
        bar.Template = blank;
        bar.ApplyTemplate();
        Assert.Empty(old.Children);
        bar.Template = original;
        bar.ApplyTemplate();
        Layout(bar);
        var current = Assert.IsType<StackPanel>(bar.FindName("PART_ItemsPanel"));
        old.Children.Add(new MenuBarItem { Title = "Retired" });
        old.Measure(new Size(300, 100));
        old.Arrange(new Rect(0, 0, 300, 100));
        Assert.Same(file, Assert.Single(current.Children));
        Assert.Same(current, file.VisualParent);
        bar.Items.Add(new MenuBarItem { Title = "Edit" });
        Assert.Equal(2, current.Children.Count);
        bar.Items.Clear();
        Assert.Empty(current.Children);
        Assert.False(bar.HasItems);
        Assert.Null(file.ParentMenuBar);
    }

    [Fact]
    public void TitleOpenAndFocusStatesComeFromRealTemplateElements()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuBarItem { Title = "File" });
        item.Items.Add(new MenuFlyoutItem { Text = "Open" });
        Layout(item);
        Assert.Equal("Normal", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        item.OpenMenu();
        Assert.True(item.IsMenuOpen);
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(item, "MenuStates"));
        Assert.Equal("MenuOpen", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        Assert.Equal(Visibility.Visible, Assert.IsType<Border>(item.FindName("PART_Highlight")).Visibility);
        item.CloseMenu();
        Assert.False(item.IsMenuOpen);
        Assert.Equal("Closed", VisualStateManager.GetCurrentStateName(item, "MenuStates"));
        item.Title = "Edit";
        Assert.Equal("Edit", Assert.IsType<TextBlock>(item.FindName("PART_Title")).Text);
        item.IsEnabled = false;
        item.OpenMenu();
        Assert.False(item.IsMenuOpen);
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
    }

    [Fact]
    public void InheritedTypographyAndExplicitStyleForegroundReachTheTitle()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuBarItem { Title = "File" });
        var bar = LegacyMenuTemplateTestHelper.Apply(new MenuBar());
        bar.FontSize = 28;
        bar.FontWeight = FontWeights.Bold;
        bar.Items.Add(item);
        Layout(bar, 320, 80);
        var title = Assert.IsType<TextBlock>(item.FindName("PART_Title"));
        Assert.Equal(28, title.FontSize);
        Assert.Equal(FontWeights.Bold, title.FontWeight);
        var style = new Style(typeof(MenuBarItem))
        {
            BasedOn = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(MenuBarItem)]
        };
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Navy));
        item.Style = style;
        Layout(bar, 320, 80);
        Assert.Same(Brushes.Navy, Assert.IsType<TextBlock>(item.FindName("PART_Title")).Foreground);
    }

    [Fact]
    public void AuthoredBackgroundRemainsEffectiveWhileMenuIsOpen()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuBarItem { Title = "File", Background = Brushes.Orange });
        item.Items.Add(new MenuFlyoutItem { Text = "Open" });
        item.OpenMenu();
        Layout(item);
        Assert.False(item.ShowHighlight);
        Assert.Same(Brushes.Orange, Assert.IsType<Border>(item.FindName("PART_Root")).Background);
        item.CloseMenu();
    }

    [Fact]
    public void EmptyTemplatesKeepTheCollectionAndMenuApisFunctional()
    {
        var item = new MenuBarItem
        {
            Title = "File"
        };
        item.Items.Add(new MenuFlyoutItem { Text = "Open" });
        var bar = new MenuBar();
        bar.Items.Add(item);
        var template = new ControlTemplate(typeof(MenuBar));
        template.SetVisualTree(() => new Border());
        bar.Template = template;
        bar.ApplyTemplate();
        bar.UpdateItems();
        Layout(bar);
        Assert.Same(item, Assert.Single(bar.Items));
        item.OpenMenu();
        Assert.True(item.IsMenuOpen);
        bar.IsEnabled = false;
        Assert.False(item.IsMenuOpen);
    }

    private static void Layout(FrameworkElement value, double width = 320, double height = 60)
    {
        value.Measure(new Size(width, height));
        value.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void ClosingBeforeQueuedCommandFocusKeepsFocusOnTheOwner()
    {
        // Drain requests from preceding popup fixtures before testing this menu.
        Jalium.UI.Threading.Dispatcher.GetForCurrentThread().ProcessQueue();
        Keyboard.Initialize();
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuBarItem { Title = "File" });
        item.Items.Add(new MenuFlyoutItem { Text = "Open" });
        Layout(item);
        Assert.True(item.Focus());
        item.OpenMenuAndFocusFirstItem();
        item.CloseMenu();
        Jalium.UI.Threading.Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.False(item.IsMenuOpen);
        Assert.Same(item, Keyboard.FocusedElement);
        Keyboard.ClearFocus();
    }
}
