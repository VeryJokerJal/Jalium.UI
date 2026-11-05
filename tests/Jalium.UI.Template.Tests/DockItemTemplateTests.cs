using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DockItemTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void HeaderTemplateOwnsChromeWhileThePanelRetainsTheSelectedDocument()
    {
        var document = new TextBlock { Text = "document content" }; var item = new DockItem { Header = "编辑器", Content = document }; var panel = Panel(item); Layout(panel);
        Assert.Equal("编辑器", Part<TextBlock>(item, "PART_HeaderText").Text); Assert.True(item.IsSelected);
        Assert.DoesNotContain(document, Descendants<Visual>(item)); Assert.Contains(document, Descendants<Visual>(panel));
        item.IsSelected = false; Assert.Equal(Visibility.Hidden, Part<Button>(item, "PART_CloseButton").Visibility);
        item.CanClose = false; Assert.Equal(Visibility.Collapsed, Part<Button>(item, "PART_CloseButton").Visibility);
    }
    [Fact]
    public void RetiredCloseButtonStopsClosingAndTheCurrentButtonRetainsPanelSelectionRules()
    {
        var a = new DockItem { Header = "A" }; var b = new DockItem { Header = "B" }; var c = new DockItem { Header = "C" }; var panel = Panel(a,b,c); panel.SelectedIndex = 1; Layout(panel);
        var old = Part<Button>(b, "PART_CloseButton"); var events = 0; panel.ItemClosed += (_, _) => events++;
        var blank = new ControlTemplate(typeof(DockItem)); blank.SetVisualTree(() => new Border()); b.Template = blank; Layout(panel);
        old.PerformClick(); Assert.Equal(3, panel.Items.Count); Assert.Equal(0, events);
        b.ClearValue(Control.TemplateProperty); Layout(panel); Part<Button>(b, "PART_CloseButton").PerformClick(); Layout(panel);
        Assert.Equal(2, panel.Items.Count); Assert.Equal(1, events); Assert.Same(c, panel.Items[panel.SelectedIndex]);
        c.CanClose = false; Assert.False(c.TryClose()); Assert.Equal(2, panel.Items.Count);
    }
    [Fact]
    public void VisualHeadersAndConsumerTemplatesSurviveRepeatedReplacement()
    {
        var header = new Border { Width = 40, Height = 18, Background = new SolidColorBrush(Colors.Red) }; var item = new DockItem { Header = header }; var panel = Panel(item); Layout(panel);
        Assert.Same(header, Part<ContentPresenter>(item, "PART_Header").Content); Assert.Equal(Visibility.Visible, Part<ContentPresenter>(item, "PART_Header").Visibility);
        for (var pass = 0; pass < 2; pass++)
        {
            var blank = new ControlTemplate(typeof(DockItem)); blank.SetVisualTree(() => new Border()); item.Template = blank; Layout(panel);
            item.ClearValue(Control.TemplateProperty); Layout(panel); Assert.Contains(header, Descendants<Visual>(item));
        }
        var template = new DataTemplate(); template.SetVisualTree(() => new TextBlock { Text = "custom tab" }); item.Header = "data"; item.HeaderTemplate = template; Layout(panel);
        Assert.Contains(Descendants<TextBlock>(item), text => text.Text == "custom tab");
    }
    [Fact]
    public void ChromePlacementAndSelectionBrushesRemainIndependentPerItem()
    {
        var a = new DockItem { Header = "A", SelectedBackground = new SolidColorBrush(Colors.Red) }; var b = new DockItem { Header = "B", SelectedBackground = new SolidColorBrush(Colors.Blue) }; var panel = Panel(a,b); Layout(panel);
        Assert.Same(a.SelectedBackground, Part<Border>(a, "PART_Background").Background);
        foreach (var placement in new[] { Dock.Top, Dock.Bottom, Dock.Left, Dock.Right })
        {
            panel.TabStripPlacement = placement; Layout(panel); Assert.Equal(placement, a.TabStripPlacement); Assert.Equal(placement.ToString(), VisualStateManager.GetCurrentStateName(a,"PlacementStates"));
        }
        panel.SelectedIndex = 1; Layout(panel); Assert.Same(b.SelectedBackground, Part<Border>(b,"PART_Background").Background); Assert.False(a.IsSelected); Assert.True(b.IsSelected);
    }
    private static DockTabPanel Panel(params DockItem[] items)
    {
        var panel = new DockTabPanel(); panel.Resources.MergedDictionaries.Add(Defaults.Value); foreach (var item in items) panel.Items.Add(item); return panel;
    }
    private static T Part<T>(Control control, string name) where T : class => Assert.IsAssignableFrom<T>(control.FindName(name));
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (var i=0;i<root.VisualChildrenCount;i++) { var child = root.GetVisualChild(i); if(child is T match) yield return match; if(child is not null) foreach(var item in Descendants<T>(child)) yield return item; }
    }
    private static void Layout(FrameworkElement element)
    {
        for(var pass=0;pass<3;pass++) { element.InvalidateMeasure(); element.Measure(new Size(720,480)); element.Arrange(new Rect(0,0,720,480)); element.UpdateLayout(); }
    }
}
