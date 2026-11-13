using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class NavigationChromeTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void HeadingSupportsVisualContentAndAnIndependentContentTemplate()
    {
        var content = new Border { Background = new SolidColorBrush(Colors.Red), Width = 20, Height = 10 };
        var heading = new NavigationViewItemHeader { Content = content }; Layout(heading);
        Assert.Contains(content, Descendants<Border>(heading)); Assert.False(heading.Focusable);
        var template = new DataTemplate(); template.SetVisualTree(() => new TextBlock { Text = "custom heading" });
        heading.Content = "section"; heading.ContentTemplate = template; Layout(heading);
        Assert.Contains(Descendants<TextBlock>(heading), text => text.Text == "custom heading");
        var blank = new ControlTemplate(typeof(NavigationViewItemHeader)); blank.SetVisualTree(() => new Border()); heading.Template = blank; Layout(heading);
        Assert.Empty(Descendants<TextBlock>(heading));
        heading.ClearValue(Control.TemplateProperty); Layout(heading); Assert.Contains(Descendants<TextBlock>(heading), text => text.Text == "custom heading");
    }
    [Fact]
    public void SeparatorChromeUsesItsLiveBrushAndCanBeReplaced()
    {
        var brush = new SolidColorBrush(Colors.Blue); var separator = new NavigationViewItemSeparator { Background = brush }; Layout(separator);
        Assert.Same(brush, Assert.Single(Descendants<Border>(separator)).Background);
        var next = new SolidColorBrush(Colors.Red); separator.Background = next; Assert.Same(next, Assert.Single(Descendants<Border>(separator)).Background);
        var blank = new ControlTemplate(typeof(NavigationViewItemSeparator)); blank.SetVisualTree(() => new TextBlock { Text = "alternative" }); separator.Template = blank; Layout(separator); Assert.Empty(Descendants<Border>(separator));
    }
    [Fact]
    public void SplitterChromeUsesIndependentStateBrushesAndHonorsDisabledState()
    {
        var split = new Split { Background = new SolidColorBrush(Colors.Blue), HoverBrush = new SolidColorBrush(Colors.Red), DraggingBrush = new SolidColorBrush(Colors.Green) }; Layout(split);
        var chrome = Assert.IsType<Border>(split.FindName("PART_Chrome")); Assert.Same(split.Background, chrome.Background);
        Assert.True(VisualStateManager.GoToState(split, "PointerOver", false)); Assert.Same(split.HoverBrush, chrome.Background);
        Assert.True(VisualStateManager.GoToState(split, "Pressed", false)); Assert.Same(split.DraggingBrush, chrome.Background);
        split.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(split, "CommonStates")); Assert.Equal(0.5, chrome.Opacity);
        var blank = new ControlTemplate(typeof(Split)); blank.SetVisualTree(() => new TextBlock()); split.Template = blank; Layout(split); Assert.Empty(Descendants<Border>(split));
    }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (var index = 0; index < root.VisualChildrenCount; index++)
        {
            var child = root.GetVisualChild(index); if (child is T match) yield return match;
            if (child is not null) foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private static void Layout(Control element)
    {
        if (!element.Resources.MergedDictionaries.Contains(Defaults.Value)) element.Resources.MergedDictionaries.Add(Defaults.Value);
        for (var pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(640, 240)); element.Arrange(new Rect(0, 0, 640, 240)); element.UpdateLayout(); }
    }
}
