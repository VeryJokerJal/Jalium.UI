using System.ComponentModel;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class NavigationViewTemplateTests
{
    private static readonly Lazy<Style> Default = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var defaults = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        return (Style)defaults[typeof(NavigationView)]!;
    });

    [Fact]
    public void AZeroWidthClosedPaneIsHiddenWhileCompactAndOpenPanesRemainVisible()
    {
        var header = new Button { Content = "Brand action" };
        var content = new Button { Content = "Content action" };
        var item = new NavigationViewItem { Content = "Page" };
        var navigation = new NavigationView { Style = Default.Value, PaneHeader = header, Content = content, CompactPaneLength = 0 };
        navigation.MenuItems.Add(item);
        Layout(navigation);
        var pane = Assert.IsType<Grid>(navigation.FindName("PART_PaneContainer"));
        Assert.True(pane.IsVisible);
        navigation.IsPaneOpen = false;
        Assert.Equal(0, navigation.PaneWidth);
        Assert.Equal(Visibility.Collapsed, pane.Visibility);
        Assert.False(header.IsVisible);
        Assert.False(item.IsVisible);
        Assert.True(content.IsVisible);
        Assert.Equal("PaneHidden", VisualStateManager.GetCurrentStateName(navigation, "PaneStates"));
        navigation.CompactPaneLength = 48;
        Assert.Equal(48, navigation.PaneWidth);
        Assert.True(pane.IsVisible);
        Assert.Equal("PaneCompact", VisualStateManager.GetCurrentStateName(navigation, "PaneStates"));
        navigation.IsPaneOpen = true;
        Assert.Equal("PaneOpen", VisualStateManager.GetCurrentStateName(navigation, "PaneStates"));
        navigation.OpenPaneLength = 0;
        Assert.Equal("PaneHidden", VisualStateManager.GetCurrentStateName(navigation, "PaneStates"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PaneLengthsRejectValuesThatCannotBeArranged(double length)
    {
        var navigation = new NavigationView();
        Assert.Throws<ArgumentException>(() => navigation.OpenPaneLength = length);
        Assert.Throws<ArgumentException>(() => navigation.CompactPaneLength = length);
    }

    [Fact]
    public void TemplateReplacementReleasesHeaderFooterAndMenuItemsForTheNextTemplate()
    {
        var header = new Button { Content = "Header" };
        var footer = new Button { Content = "Footer" };
        var item = new NavigationViewItem { Content = "Page" };
        var footerItem = new NavigationViewItem { Content = "Settings" };
        var navigation = new NavigationView { Style = Default.Value, PaneHeader = header, PaneFooter = footer };
        navigation.MenuItems.Add(item);
        navigation.FooterMenuItems.Add(footerItem);
        Layout(navigation);
        navigation.SelectedItem = item;
        var oldHost = Assert.IsType<StackPanel>(navigation.FindName("PART_MenuItemsHost"));
        var oldHeader = Assert.IsType<ContentPresenter>(navigation.FindName("PART_PaneHeaderHost"));
        var original = navigation.Template;
        navigation.Template = null;
        Assert.Empty(oldHost.Children);
        Assert.Null(oldHeader.Content);
        Assert.Null(item.VisualParent);
        Assert.Null(header.VisualParent);
        navigation.Template = original;
        Layout(navigation);
        Assert.NotSame(oldHost, item.VisualParent);
        Assert.Same(item, navigation.SelectedItem);
        Assert.True(item.IsSelected);
        Assert.Same(header, Assert.IsType<ContentPresenter>(navigation.FindName("PART_PaneHeaderHost")).Content);
        Assert.Same(footer, Assert.IsType<ContentPresenter>(navigation.FindName("PART_PaneFooterHost")).Content);
        Assert.Same(footerItem, Assert.Single(Assert.IsType<StackPanel>(navigation.FindName("PART_FooterItemsHost")).Children.Cast<UIElement>()));
    }

    [Fact]
    public void AConsumerStyleDoesNotPromoteTheDefaultTemplateToALocalValue()
    {
        var navigation = new NavigationView { Style = Default.Value };
        Layout(navigation);
        Assert.NotNull(navigation.Template);
        Assert.False(navigation.HasLocalValue(Control.TemplateProperty));
        var minimal = new ControlTemplate(typeof(NavigationView));
        minimal.SetVisualTree(() => new Border());
        navigation.Style = new Style(typeof(NavigationView)) { Setters = { new Setter(Control.TemplateProperty, minimal) } };
        Layout(navigation);
        Assert.Same(minimal, navigation.Template);
        navigation.MenuItems.Add(new NavigationViewItem { Content = "Optional parts" });
        navigation.IsPaneOpen = false;
        Assert.Null(navigation.FindName("PART_MenuItemsHost"));
    }

    [Fact]
    public void AutomationReportsSelectionAndPreservesThePaneBinding()
    {
        var model = new PaneModel();
        var item = new NavigationViewItem { Content = "Page" };
        var navigation = new NavigationView { Style = Default.Value };
        navigation.MenuItems.Add(item);
        navigation.SetBinding(NavigationView.IsPaneOpenProperty, new Binding(nameof(PaneModel.Open)) { Source = model, Mode = BindingMode.TwoWay });
        Layout(navigation);
        navigation.SelectedItem = item;
        var peer = Assert.IsType<NavigationViewAutomationPeer>(FrameworkElementAutomationPeer.CreatePeerForElement(navigation));
        Assert.Same(FrameworkElementAutomationPeer.CreatePeerForElement(item), Assert.Single(peer.GetSelection()));
        Assert.Single(((ISelectionProvider)peer).GetSelection());
        ((IExpandCollapseProvider)peer).Collapse();
        Assert.False(model.Open);
        Assert.NotNull(navigation.GetBindingExpression(NavigationView.IsPaneOpenProperty));
        ((IExpandCollapseProvider)peer).Expand();
        Assert.True(model.Open);
        navigation.SelectedItem = null;
        Assert.Empty(peer.GetSelection());
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(600, 400));
        element.Arrange(new Rect(0, 0, 600, 400));
        element.UpdateLayout();
    }
    private sealed class PaneModel : INotifyPropertyChanged
    {
        private bool _open = true;
        public bool Open { get => _open; set { _open = value; PropertyChanged?.Invoke(this, new(nameof(Open))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
