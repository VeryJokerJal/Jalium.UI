using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSLegacyMenuTemplateTests
{
    [Fact]
    public void MenuTemplateHostsHorizontalItemsAndPreservesTopLevelRoles()
    {
        var menu = new Menu();
        var file = new MenuItem
        {
            Header = "File"
        };
        file.Items.Add(new MenuItem { Header = "Open" });
        var edit = new MenuItem
        {
            Header = "Edit"
        };
        menu.Items.Add(file);
        menu.Items.Add(edit);
        LegacyMenuTemplateTestHelper.Apply(menu);
        Layout(menu, 320, 40);
        Assert.IsType<Border>(menu.FindName("PART_Root"));
        Assert.IsType<ItemsPresenter>(menu.FindName("PART_ItemsPresenter"));
        Assert.Equal(MenuItemRole.TopLevelHeader, file.Role);
        Assert.Equal(MenuItemRole.TopLevelItem, edit.Role);
        Assert.True(edit.VisualBounds.X >= file.RenderSize.Width);
        Assert.Equal(0, file.IconColumnSize);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<MenuIconPresenter>(file.FindName("PART_Icon")).Visibility);
    }

    [Fact]
    public void HeaderPresenterSupportsVisualContentTemplatesAndFormatting()
    {
        var visual = new TextBlock
        {
            Text = "Visual header"
        };
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = visual });
        Layout(item);
        var presenter = Assert.IsType<ContentPresenter>(item.FindName("PART_Header"));
        Assert.Same(visual, presenter.Content);
        Assert.True(visual.RenderSize.Width > 0);
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock { Text = "Templated header" });
        item.Header = 7;
        item.HeaderTemplate = template;
        Layout(item);
        Assert.Same(template, presenter.ContentTemplate);
        Assert.Contains(Descendants(presenter), v => v is TextBlock t && t.Text == "Templated header");
        item.HeaderTemplate = null;
        item.HeaderStringFormat = "Item {0}";
        Layout(item);
        Assert.Equal("Item {0}", presenter.ContentStringFormat);
        Assert.Contains(Descendants(presenter), v => v is TextBlock t && t.Text == "Item 7");
    }

    [Fact]
    public void SubmenuChildrenStayHiddenUntilPopupOwnsThemAndSurviveRetemplate()
    {
        var parent = new MenuItem
        {
            Header = "Parent"
        };
        var child = new MenuItem
        {
            Header = "Child"
        };
        parent.Items.Add(child);
        LegacyMenuTemplateTestHelper.Apply(parent);
        Layout(parent);
        var oldPresenter = Assert.IsType<ItemsPresenter>(parent.FindName("PART_ItemsPresenter"));
        Assert.Equal(Visibility.Hidden, oldPresenter.Visibility);
        Assert.Equal(new Size(0, 0), oldPresenter.RenderSize);
        parent.IsSubmenuOpen = true;
        var host = GetField<MenuPopupScrollHost>(parent, "_submenuScrollHost");
        Assert.Same(child, Assert.Single(host.Items));
        var original = parent.Template;
        var blank = new ControlTemplate(typeof(MenuItem));
        blank.SetVisualTree(() => new Border());
        parent.Template = blank;
        parent.ApplyTemplate();
        parent.Template = original;
        parent.ApplyTemplate();
        Layout(parent);
        Assert.True(parent.ItemsHostInternal!.Children.Count == 0, $"before stale measure: children={parent.ItemsHostInternal.Children.Count}, parent={child.VisualParent?.GetType().Name}");
        oldPresenter.InvalidateMeasure();
        oldPresenter.Measure(new Size(100, 100));
        Assert.True(parent.IsSubmenuOpen);
        Assert.Empty(host.Items);
        var currentHost = GetField<MenuPopupScrollHost>(parent, "_submenuScrollHost");
        Assert.NotSame(host, currentHost);
        Assert.Same(child, Assert.Single(currentHost.Items));
        Assert.Empty(parent.ItemsHostInternal!.Children);
        Assert.NotSame(parent.ItemsHostInternal, child.VisualParent);
        parent.IsSubmenuOpen = false;
        Assert.Empty(currentHost.Items);
        Assert.Same(child, Assert.Single(parent.ItemsHostInternal!.Children));
        Assert.Same(parent.ItemsHostInternal, child.VisualParent);
    }

    [Fact]
    public void OpenSubmenuRefreshesThemeChromeAndCollectionWithoutDuplicatingContainers()
    {
        var parent = new MenuItem
        {
            Header = "Parent"
        };
        var first = new MenuItem
        {
            Header = "First"
        };
        parent.Resources["MenuFlyoutPresenterBackground"] = Brushes.Red;
        parent.Items.Add(first);
        LegacyMenuTemplateTestHelper.Apply(parent);
        parent.IsSubmenuOpen = true;
        var host = GetField<MenuPopupScrollHost>(parent, "_submenuScrollHost");
        Assert.Same(Brushes.Red, parent.SubmenuFrame!.Background);
        parent.Resources["MenuFlyoutPresenterBackground"] = Brushes.Blue;
        parent.InvalidateMeasure();
        Layout(parent);
        Assert.Same(Brushes.Blue, parent.SubmenuFrame.Background);
        var second = new MenuItem
        {
            Header = "Second"
        };
        parent.Items.Add(second);
        Assert.Equal(new UIElement[] { first, second }, host.Items);
        parent.Items.Remove(first);
        Assert.Same(second, Assert.Single(host.Items));
        parent.IsSubmenuOpen = false;
        Assert.Same(second, Assert.Single(parent.ItemsHostInternal!.Children));
        Assert.Same(parent.ItemsHostInternal, second.VisualParent);
    }

    [Fact]
    public void NarrowRowsKeepShortcutAtRightAndClipTheHeader()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "A long title", InputGestureText = "Ctrl+O", Width = 100, Height = 40 });
        Layout(item, 100, 40);
        var gesture = Assert.IsType<TextBlock>(item.FindName("PART_Gesture"));
        var header = Assert.IsType<ContentPresenter>(item.FindName("PART_Header"));
        Assert.Equal(92, gesture.TransformToAncestor(item).X + gesture.RenderSize.Width);
        Assert.True(header.ClipToBounds);
        Assert.True(header.RenderSize.Width <= 76);
    }

    [Fact]
    public void GeneratedContainersStayAssociatedWithEqualButDistinctDataDuringOpenChanges()
    {
        var parent = new MenuItem
        {
            Header = "Parent"
        };
        var first = new EqualItem();
        var second = new EqualItem();
        parent.Items.Add(first);
        LegacyMenuTemplateTestHelper.Apply(parent);
        parent.IsSubmenuOpen = true;
        var host = GetField<MenuPopupScrollHost>(parent, "_submenuScrollHost");
        var firstContainer = Assert.IsType<MenuItem>(Assert.Single(host.Items));
        Assert.Same(first, firstContainer.DataContext);
        parent.Items.Add(second);
        Assert.Same(firstContainer, host.Items[0]);
        var secondContainer = Assert.IsType<MenuItem>(host.Items[1]);
        Assert.Same(second, secondContainer.DataContext);
        parent.Items.RemoveAt(0);
        Assert.Same(secondContainer, Assert.Single(host.Items));
        Assert.Same(second, secondContainer.DataContext);
        parent.IsSubmenuOpen = false;
        Assert.Same(secondContainer, Assert.Single(parent.ItemsHostInternal!.Children));
    }

    private sealed class EqualItem
    {
        public override bool Equals(object? other) => other is EqualItem;
        public override int GetHashCode() => 1;
        public override string ToString() => "Equal data";
    }

    [Fact]
    public void MenuBarLogicalChildrenKeepViewModelBindingsAcrossPopupAndRemoval()
    {
        var command = new BindingCommand();
        var menu = new MenuBar
        {
            DataContext = new CommandSource(command)
        };
        var owner = new MenuBarItem
        {
            Title = "File"
        };
        var item = new MenuFlyoutItem
        {
            Text = "Open"
        };
        Jalium.UI.Data.BindingOperations.SetBinding(item, MenuFlyoutItem.CommandProperty, new Jalium.UI.Data.Binding(nameof(CommandSource.Command)));
        owner.Items.Add(item);
        menu.Items.Add(owner);
        Assert.Same(menu.DataContext, owner.DataContext);
        Assert.Same(menu.DataContext, item.DataContext);
        Layout(menu);
        Assert.Same(menu, owner.LogicalParentInternal);
        owner.OpenMenu();
        Jalium.UI.Threading.Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Same(owner, item.LogicalParentInternal);
        Assert.Same(command, item.Command);
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item)).Invoke();
        Assert.Equal(1, command.Executions);
        owner.CloseMenu();
        var replacement = new BindingCommand();
        menu.DataContext = new CommandSource(replacement);
        owner.OpenMenu();
        Jalium.UI.Threading.Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Same(menu.DataContext, owner.DataContext);
        Assert.Same(menu.DataContext, item.DataContext);
        Assert.Same(replacement, item.Command);
        owner.CloseMenu();
        owner.Items.Clear();
        Assert.Null(item.LogicalParentInternal);
        menu.Items.Clear();
        Assert.Null(owner.LogicalParentInternal);
    }

    private sealed record CommandSource(System.Windows.Input.ICommand Command);
    private sealed class BindingCommand : System.Windows.Input.ICommand
    {
        public int Executions { get; private set; }

        public bool CanExecute(object? value) => true;
        public void Execute(object? value) => Executions++;
        public event EventHandler? CanExecuteChanged
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    [Fact]
    public void TemplateCheckArrowGestureAndVisualStatesTrackControlState()
    {
        var item = new MenuItem
        {
            Header = "Open",
            InputGestureText = "Ctrl+O",
            IsCheckable = true,
            IsChecked = true
        };
        item.Items.Add(new MenuItem());
        LegacyMenuTemplateTestHelper.Apply(item);
        Layout(item);
        Assert.Equal("Ctrl+O", Assert.IsType<TextBlock>(item.FindName("PART_Gesture")).Text);
        Assert.Equal(Visibility.Visible, Assert.IsType<Jalium.UI.Shapes.Path>(item.FindName("PART_Check")).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<Jalium.UI.Shapes.Path>(item.FindName("PART_Arrow")).Visibility);
        Assert.Equal("Checked", VisualStateManager.GetCurrentStateName(item, "CheckStates"));
        item.RaiseEvent(new MouseEventArgs(UIElement.MouseEnterEvent) { Source = item });
        Assert.Equal("SubmenuOpen", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        item.IsSubmenuOpen = false;
        item.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(item, "CommonStates"));
        item.IsChecked = false;
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Jalium.UI.Shapes.Path>(item.FindName("PART_Check")).Visibility);
    }

    [Fact]
    public void InheritedTypographyAndCallerForegroundReachTheHeaderAndGesture()
    {
        var item = LegacyMenuTemplateTestHelper.Apply(new MenuItem { Header = "Open", InputGestureText = "Ctrl+O", Foreground = Brushes.Navy });
        var host = new Grid();
        host.Children.Add(item);
        host.SetValue(Control.FontSizeProperty, 28.0);
        host.SetValue(Control.FontWeightProperty, FontWeights.Bold);
        Layout(host, 320, 70);
        Assert.Equal(28, item.ItemFontSize);
        Assert.Equal(FontWeights.Bold, item.ItemFontWeight);
        var header = Assert.IsType<ContentPresenter>(item.FindName("PART_Header"));
        Assert.Contains(Descendants(header), v => v is TextBlock t && t.FontSize == 28 && t.Foreground == Brushes.Navy);
        Assert.Same(Brushes.Navy, Assert.IsType<TextBlock>(item.FindName("PART_Gesture")).Foreground);
        item.IsEnabled = false;
        Assert.Same(Brushes.Navy, item.ItemForeground);
    }

    [Fact]
    public void MissingPartsRemainSafeAndAutomationStillUsesVirtualClickPath()
    {
        var item = new MenuItem
        {
            Header = "Command",
            IsCheckable = true,
            StaysOpenOnClick = true
        };
        int clicks = 0;
        item.Click += (_, _) => clicks++;
        var empty = new ControlTemplate(typeof(MenuItem));
        empty.SetVisualTree(() => new Border());
        item.Template = empty;
        item.ApplyTemplate();
        ((IInvokeProvider)new MenuItemAutomationPeer(item)).Invoke();
        Assert.True(item.IsChecked);
        Assert.Equal(1, clicks);
        Layout(item);
    }

    private static void Layout(FrameworkElement element, double width = 240, double height = 40)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    private static T GetField<T>(object value, string name) => (T)typeof(MenuItem).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static IEnumerable<Visual> Descendants(Visual parent)
    {
        for (int i = 0; i < parent.VisualChildrenCount; i++)
        {
            var child = parent.GetVisualChild(i)!;
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
