using Jalium.UI.Controls;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using TabDock = Jalium.UI.Controls.Dock;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TabControlTemplateTests
{
    public TabControlTemplateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Theory]
    [InlineData(TabDock.Top, "PART_IndicatorBottom")]
    [InlineData(TabDock.Bottom, "PART_IndicatorTop")]
    [InlineData(TabDock.Left, "PART_IndicatorRight")]
    [InlineData(TabDock.Right, "PART_IndicatorLeft")]
    public void PlacementControlsTheHeaderPanelAndTheContentFacingIndicator(TabDock placement, string edge)
    {
        var tabs = CreateTabs();
        tabs.TabStripPlacement = placement;
        Layout(tabs);
        var first = Assert.IsType<TabItem>(tabs.Items[0]);
        var panel = Assert.IsType<TabPanel>(tabs.HeaderPanel);
        Assert.Equal(0, Assert.IsType<SolidColorBrush>(panel.Background).Color.A);
        Assert.Equal(placement, panel.TabStripPlacement);
        Assert.Equal(placement, first.TabStripPlacement);
        Assert.Equal(placement, DockPanel.GetDock(Part<Grid>(tabs, "PART_HeaderDock")));
        Assert.Equal(Visibility.Visible, Part<Border>(first, edge).Visibility);
        var header = Bounds(Part<Grid>(tabs, "PART_HeaderDock"), tabs);
        var content = Bounds(Part<ContentPresenter>(tabs, "PART_SelectedContentHost"), tabs);
        Assert.True(content.Width > 0 && content.Height > 0);
        Assert.True(placement switch
        {
            TabDock.Top => header.Bottom <= content.Top + 0.1,
            TabDock.Bottom => content.Bottom <= header.Top + 0.1,
            TabDock.Left => header.Right <= content.Left + 0.1,
            _ => content.Right <= header.Left + 0.1
        });
        tabs.TabStripPlacement = TabDock.Top;
        Layout(tabs);
        Assert.Equal(TabDock.Top, panel.TabStripPlacement);
        Assert.Equal(Visibility.Visible, Part<Border>(first, "PART_IndicatorBottom").Visibility);
        if (edge != "PART_IndicatorBottom") Assert.Equal(Visibility.Collapsed, Part<Border>(first, edge).Visibility);
    }

    [Fact]
    public void HeadersUseTheirTemplatesAndContentKeepsItsLogicalOwnerAcrossReplacement()
    {
        var tabs = CreateTabs();
        var first = (TabItem)tabs.Items[0]!;
        first.Header = 12.34;
        first.HeaderStringFormat = "value={0:0.0}";
        Layout(tabs);
        var header = Part<ContentPresenter>(first, "PART_HeaderPresenter");
        Assert.Equal("value=12.3", Assert.IsType<TextBlock>(header.GetVisualChild(0)).Text);
        var template = new DataTemplate();
        template.SetVisualTree(() => new Border { Tag = "custom-header", Width = 130, Height = 22 });
        first.HeaderTemplate = template;
        Layout(tabs);
        Assert.Equal("custom-header", Assert.IsType<Border>(header.GetVisualChild(0)).Tag);
        var original = tabs.Template;
        var oldPresenter = Part<ContentPresenter>(tabs, "PART_SelectedContentHost");
        var content = Assert.IsType<TextBox>(first.Content);
        Assert.Same(first, content.Parent);
        var empty = new ControlTemplate(typeof(TabControl));
        empty.SetVisualTree(() => new Border { Name = "Replacement" });
        tabs.Template = empty;
        Layout(tabs);
        Assert.Null(tabs.FindName("PART_SelectedContentHost"));
        Assert.Null(content.VisualParent);
        tabs.SelectedIndex = 1;
        Assert.Equal("Beta", tabs.SelectedContent);
        tabs.Template = original;
        Layout(tabs);
        Assert.NotSame(oldPresenter, Part<ContentPresenter>(tabs, "PART_SelectedContentHost"));
        tabs.SelectedIndex = 0;
        Layout(tabs);
        Assert.Same(first, content.Parent);
        Assert.Same(content, Part<ContentPresenter>(tabs, "PART_SelectedContentHost").Content);
        Assert.Same(content, Part<ContentPresenter>(tabs, "PART_SelectedContentHost").GetVisualChild(0));
    }

    [Fact]
    public void GeneratedHeadersAndSelectedContentUseIndependentDataTemplates()
    {
        var header = new DataTemplate(); header.SetVisualTree(() => new Border { Tag = "header", Width = 88, Height = 22 });
        var content = new DataTemplate(); content.SetVisualTree(() => new Border { Tag = "body" });
        var tabs = new TabControl { ItemsSource = new[] { "Alpha", "Beta" }, ItemTemplate = header, ContentTemplate = content };
        Layout(tabs);
        var first = Assert.IsType<TabItem>(tabs.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal("header", Assert.IsType<Border>(Part<ContentPresenter>(first, "PART_HeaderPresenter").GetVisualChild(0)).Tag);
        Assert.Equal("body", Assert.IsType<Border>(Part<ContentPresenter>(tabs, "PART_SelectedContentHost").GetVisualChild(0)).Tag);
        Assert.Equal("Alpha", tabs.SelectedContent);
        tabs.SelectedIndex = 1;
        Assert.Equal("Beta", tabs.SelectedContent);
    }

    [Fact]
    public void SelectedAlignmentChangesAreExposedToDefaultAndConsumerTemplates()
    {
        var tabs = CreateTabs(); Layout(tabs);
        var first = (TabItem)tabs.Items[0]!;
        first.HorizontalContentAlignment = HorizontalAlignment.Right;
        first.VerticalContentAlignment = VerticalAlignment.Bottom;
        var presenter = Part<ContentPresenter>(tabs, "PART_SelectedContentHost");
        Assert.Equal(HorizontalAlignment.Right, tabs.SelectedContentHorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, tabs.SelectedContentVerticalAlignment);
        Assert.Equal(HorizontalAlignment.Right, presenter.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, presenter.VerticalAlignment);
        Assert.Throws<InvalidOperationException>(() => tabs.SetValue(TabControl.SelectedContentHorizontalAlignmentProperty, HorizontalAlignment.Left));
        tabs.SelectedIndex = -1;
        Assert.Null(presenter.Content);
        Assert.Equal(0, presenter.VisualChildrenCount);
    }

    [Fact]
    public void HeaderNavigationSkipsDisabledRowsAndDoesNotConsumeContentArrowKeys()
    {
        var tabs = CreateTabs();
        tabs.Items.Insert(1, new TabItem { Header = "Disabled", IsEnabled = false });
        Layout(tabs);
        SendKey(tabs, Key.Right);
        Assert.Equal(2, tabs.SelectedIndex);
        tabs.FlowDirection = FlowDirection.RightToLeft;
        SendKey(tabs, Key.Right);
        Assert.Equal(0, tabs.SelectedIndex);
        var editor = Assert.IsType<TextBox>(((TabItem)tabs.Items[0]!).Content);
        SendKey(editor, Key.Right);
        Assert.Equal(0, tabs.SelectedIndex);
        tabs.IsEnabled = false;
        SendKey(tabs, Key.End);
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void ReplacingAnItemTemplateDoesNotCreateASecondContentVisual()
    {
        var tabs = CreateTabs(); Layout(tabs);
        var first = (TabItem)tabs.Items[0]!;
        var header = Part<ContentPresenter>(first, "PART_HeaderPresenter");
        var content = Assert.IsType<TextBox>(first.Content);
        var original = first.Template;
        var empty = new ControlTemplate(typeof(TabItem)); empty.SetVisualTree(() => new Border());
        first.Template = empty; Layout(tabs);
        Assert.Null(first.FindName("PART_HeaderPresenter"));
        Assert.Same(first, content.Parent);
        Assert.Same(Part<ContentPresenter>(tabs, "PART_SelectedContentHost"), content.VisualParent);
        first.Template = original; Layout(tabs);
        Assert.NotSame(header, Part<ContentPresenter>(first, "PART_HeaderPresenter"));
        Assert.Equal(Visibility.Visible, Part<Border>(first, "PART_Selection").Visibility);
    }

    [Fact]
    public void GraphicalHeadersExposeTheirTextAndDisabledAutomationCannotSelect()
    {
        var tabs = CreateTabs();
        var first = (TabItem)tabs.Items[0]!;
        first.Header = new StackPanel { Children = { new Border { Width = 12 }, new TextBlock { Text = "Project plan" } } };
        Layout(tabs);
        var peer = UIElementAutomationPeer.CreatePeerForElement(first)!;
        Assert.Equal("Project plan", peer.GetName());
        AutomationProperties.SetName(first, "Open project plan");
        Assert.Equal("Open project plan", peer.GetName());
        var second = (TabItem)tabs.Items[1]!;
        second.IsEnabled = false;
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(UIElementAutomationPeer.CreatePeerForElement(second)!.GetPattern(PatternInterface.SelectionItem));
        Assert.Throws<InvalidOperationException>(selection.Select);
        Assert.Equal(0, tabs.SelectedIndex);
        second.IsEnabled = true;
        selection.Select();
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Empty(peer.GetChildren());
        Assert.Contains(UIElementAutomationPeer.CreatePeerForElement(second)!.GetChildren(), child => child.GetName() == "Beta");
    }

    private static ObservedTabs CreateTabs()
    {
        var tabs = new ObservedTabs();
        tabs.Items.Add(new TabItem { Header = "First", Content = new TextBox { Text = "Alpha" } });
        tabs.Items.Add(new TabItem { Header = "Second", Content = "Beta" });
        return tabs;
    }
    private sealed class ObservedTabs : TabControl { internal Panel? HeaderPanel => ItemsHost; }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static Rect Bounds(FrameworkElement element, Visual owner) => new(element.TransformToAncestor(owner), element.RenderSize);
    private static void Layout(UIElement element) { element.Measure(new Size(520, 320)); element.Arrange(new Rect(0, 0, 520, 320)); element.UpdateLayout(); }
    private static KeyEventArgs SendKey(UIElement target, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, new Source { RootVisual = target }, 1, key) { RoutedEvent = UIElement.KeyDownEvent, Source = target };
        target.RaiseEvent(e); return e;
    }
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
