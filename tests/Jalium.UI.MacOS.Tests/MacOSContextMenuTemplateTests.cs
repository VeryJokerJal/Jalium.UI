using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSContextMenuTemplateTests
{
    [Fact]
    public void ModelsAndOwnerReferencesExistBeforeAnyTemplateVisuals()
    {
        var menu = new ContextMenu();
        var item = new MenuItem
        {
            Header = "Open"
        };
        menu.Items.Add(item);
        Assert.Equal(0, menu.VisualChildrenCount);
        Assert.Same(menu, item.OwnerMenu);
        Apply(menu);
        var popup = Assert.IsType<Popup>(menu.FindName("PART_Popup"));
        var frame = Assert.IsType<Border>(menu.FindName("PART_PopupBorder"));
        var host = Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost"));
        Assert.Same(frame, popup.Child);
        Assert.Same(host, frame.Child);
        Assert.Same(item, Assert.Single(host.Items));
        Assert.Equal("Closed", VisualStateManager.GetCurrentStateName(menu, "PopupStates"));
    }

    [Fact]
    public void OpenRetemplatePreservesModelsPositionAndPublicEvents()
    {
        var menu = Apply(new ContextMenu { HorizontalOffset = 3, VerticalOffset = 4 });
        var item = new MenuItem
        {
            Header = "Open"
        };
        menu.Items.Add(item);
        int opened = 0, closed = 0;
        menu.Opened += (_, _) => opened++;
        menu.Closed += (_, _) => closed++;
        menu.Open(new Point(10, 20));
        var oldPopup = Assert.IsType<Popup>(menu.FindName("PART_Popup"));
        var oldHost = Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost"));
        var original = menu.Template;
        var blank = new ControlTemplate(typeof(ContextMenu));
        blank.SetVisualTree(() => new Border());
        menu.Template = blank;
        menu.ApplyTemplate();
        Assert.Empty(oldHost.Items);
        Assert.False(oldPopup.IsOpen);
        Assert.True(menu.IsOpen);
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);
        menu.Template = original;
        menu.ApplyTemplate();
        var current = Assert.IsType<Popup>(menu.FindName("PART_Popup"));
        Assert.True(current.IsOpen);
        Assert.Equal(PlacementMode.Absolute, current.Placement);
        Assert.Equal(13, current.HorizontalOffset);
        Assert.Equal(24, current.VerticalOffset);
        Assert.Same(item, Assert.Single(Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost")).Items));
        oldPopup.IsOpen = true;
        oldPopup.IsOpen = false;
        Assert.True(menu.IsOpen);
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);
        menu.Close();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void RetiredHostCannotTakeCurrentContainersAndResetClearsOwnership()
    {
        var menu = Apply(new ContextMenu());
        var first = new MenuItem
        {
            Header = "First"
        };
        menu.Items.Add(first);
        var old = Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost"));
        var template = menu.Template;
        var blank = new ControlTemplate(typeof(ContextMenu));
        blank.SetVisualTree(() => new Border());
        menu.Template = blank;
        menu.ApplyTemplate();
        menu.Template = template;
        menu.ApplyTemplate();
        var current = Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost"));
        old.Items.Add(new MenuItem { Header = "Retired" });
        old.Measure(new Size(300, 200));
        old.Arrange(new Rect(0, 0, 300, 200));
        Assert.Same(first, Assert.Single(current.Items));
        var second = new MenuItem
        {
            Header = "Second"
        };
        menu.Items.Add(second);
        Assert.Equal(new[] { first, second }, current.Items.Cast<MenuItem>());
        menu.Items.Remove(first);
        Assert.Null(first.OwnerMenu);
        Assert.Same(second, Assert.Single(current.Items));
        menu.Items.Clear();
        Assert.Null(second.OwnerMenu);
        Assert.Empty(current.Items);
    }

    [Fact]
    public void GeneratedContainersUseDataIdentityAndRemainStableAcrossMoves()
    {
        var menu = Apply(new ContextMenu());
        var first = new Entry("Equal");
        var second = new Entry("Equal");
        menu.Items.Add(first);
        menu.Items.Add(second);
        var host = Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost"));
        var a = Assert.IsType<MenuItem>(host.Items[0]);
        var b = Assert.IsType<MenuItem>(host.Items[1]);
        Assert.NotSame(a, b);
        Assert.Same(first, a.Header);
        Assert.Same(first, a.DataContext);
        Assert.Same(second, b.Header);
        menu.Items.Remove(first);
        Assert.Same(b, Assert.Single(host.Items));
        menu.Items.Insert(0, first);
        Assert.Same(b, host.Items[1]);
        Assert.Null(a.OwnerMenu);
    }

    [Fact]
    public void FrameTracksTemplateBindingsAndExplicitZeroChrome()
    {
        var menu = Apply(new ContextMenu());
        var frame = Assert.IsType<Border>(menu.FindName("PART_PopupBorder"));
        menu.Background = Brushes.Navy;
        menu.BorderBrush = Brushes.Orange;
        menu.BorderThickness = new Thickness(0);
        menu.Padding = new Thickness(0);
        menu.CornerRadius = new CornerRadius(0);
        Assert.Same(Brushes.Navy, frame.Background);
        Assert.Same(Brushes.Orange, frame.BorderBrush);
        Assert.Equal(new Thickness(0), frame.BorderThickness);
        Assert.Equal(new Thickness(0), frame.Padding);
        Assert.Equal(new CornerRadius(0), frame.CornerRadius);
        Assert.Same(Brushes.Navy, menu.PopupBackground);
        Assert.Equal(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(menu, Control.BackgroundProperty).BaseValueSource);
    }

    [Fact]
    public void MissingPartsKeepOpenIntentAndEventsWithoutFallbackVisuals()
    {
        var menu = new ContextMenu();
        var template = new ControlTemplate(typeof(ContextMenu));
        template.SetVisualTree(() => new Border());
        menu.Template = template;
        menu.ApplyTemplate();
        int opened = 0, closed = 0;
        menu.Opened += (_, _) => opened++;
        menu.Closed += (_, _) => closed++;
        menu.IsOpen = true;
        Assert.True(menu.IsOpen);
        Assert.Null(menu.FindName("PART_Popup"));
        Assert.Equal(1, opened);
        menu.IsOpen = false;
        Assert.Equal(1, closed);
    }

    [Fact]
    public void DisabledMenusCloseAndRejectOpenRequests()
    {
        var menu = Apply(new ContextMenu());
        menu.Items.Add(new MenuItem { Header = "Open" });
        menu.IsOpen = true;
        menu.IsEnabled = false;
        Assert.False(menu.IsOpen);
        Assert.False(Assert.IsType<Popup>(menu.FindName("PART_Popup")).IsOpen);
        menu.IsOpen = true;
        Assert.False(menu.IsOpen);
        menu.Open(new Point(10, 10));
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void GeneratedHeadersFollowItemsControlPresentationChanges()
    {
        var menu = Apply(new ContextMenu());
        menu.Items.Add("Open");
        var host = Assert.IsType<MenuPopupScrollHost>(menu.FindName("PART_ScrollHost"));
        var item = Assert.IsType<MenuItem>(Assert.Single(host.Items));
        var template = new DataTemplate();
        menu.ItemTemplate = template;
        menu.ItemStringFormat = "Action: {0}";
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Navy));
        menu.ItemContainerStyle = style;
        Assert.Same(item, Assert.Single(host.Items));
        Assert.Same(template, item.HeaderTemplate);
        Assert.Equal("Action: {0}", item.HeaderStringFormat);
        Assert.Same(style, item.Style);
        Assert.Same(menu, item.LogicalParentInternal);
    }

    [Fact]
    public void PlacementOwnerContextUpdatesCachedRowsWhileOpenAndAcrossOwners()
    {
        var first = new ContextModel(new NoOpCommand());
        var second = new ContextModel(new NoOpCommand());
        var owner = new Border
        {
            DataContext = first
        };
        var menu = Apply(new ContextMenu { PlacementTarget = owner });
        var item = new MenuItem
        {
            Header = "Run"
        };
        BindingOperations.SetBinding(item, MenuItem.CommandProperty, new Binding(nameof(ContextModel.Command)));
        menu.Items.Add(item);
        menu.IsOpen = true;
        Assert.Same(first.Command, item.Command);
        Assert.False(menu.HasLocalValue(FrameworkElement.DataContextProperty));
        owner.DataContext = second;
        Assert.Same(second.Command, item.Command);
        var third = new ContextModel(new NoOpCommand());
        menu.PlacementTarget = new Border
        {
            DataContext = third
        };
        Assert.Same(third.Command, item.Command);
        owner.DataContext = first;
        Assert.Same(third.Command, item.Command);
        menu.Close();
    }

    [Fact]
    public void CallerDataContextWinsOverPlacementOwnerInheritance()
    {
        var caller = new ContextModel(new NoOpCommand());
        var owner = new Border
        {
            DataContext = new ContextModel(new NoOpCommand())
        };
        var menu = Apply(new ContextMenu { DataContext = caller, PlacementTarget = owner });
        var item = new MenuItem
        {
            Header = "Run"
        };
        BindingOperations.SetBinding(item, MenuItem.CommandProperty, new Binding(nameof(ContextModel.Command)));
        menu.Items.Add(item);
        menu.IsOpen = true;
        owner.DataContext = new ContextModel(new NoOpCommand());
        Assert.Same(caller, menu.DataContext);
        Assert.Same(caller.Command, item.Command);
        menu.Close();
    }

    [Fact]
    public void AutomationExposesTheMenuSemanticType()
    {
        var menu = new ContextMenu();
        var peer = Jalium.UI.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(menu);
        Assert.IsType<Jalium.UI.Automation.Peers.ContextMenuAutomationPeer>(peer);
        Assert.Equal(Jalium.UI.Automation.Peers.AutomationControlType.Menu, peer!.GetAutomationControlType());
    }

    private sealed record ContextModel(System.Windows.Input.ICommand Command);
    private sealed class NoOpCommand : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add
            {
            }

            remove
            {
            }
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
        }
    }

    private sealed record Entry(string Text);
    private static T Apply<T>(T menu)
        where T : ContextMenu => LegacyMenuTemplateTestHelper.Apply(menu);
}
