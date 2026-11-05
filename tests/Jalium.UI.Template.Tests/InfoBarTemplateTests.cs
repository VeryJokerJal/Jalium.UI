using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class InfoBarTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void SeverityTextAndOptionalIconAreOwnedByTheTemplate()
    {
        var bar = Create(); Layout(bar); bar.Title = "完成"; bar.Message = "内容已经保存"; bar.Severity = InfoBarSeverity.Success;
        Assert.Equal("完成", Part<TextBlock>(bar, "PART_Title").Text); Assert.Equal("内容已经保存", Part<TextBlock>(bar, "PART_Message").Text);
        Assert.Equal("Success", VisualStateManager.GetCurrentStateName(bar, "SeverityStates"));
        bar.IsIconVisible = false; Assert.Equal(Visibility.Collapsed, Part<Grid>(bar, "PART_IconHost").Visibility);
        var icon = new FontIconSource { Glyph = "A" }; bar.IconSource = icon; bar.IsIconVisible = true;
        Assert.Same(icon, Part<IconSourceElement>(bar, "PART_CustomIcon").IconSource); Assert.Equal(Visibility.Collapsed, Part<Jalium.UI.Shapes.Path>(bar, "PART_DefaultIcon").Visibility);
        bar.Title = null; bar.Message = null; Assert.Equal(Visibility.Collapsed, Part<TextBlock>(bar, "PART_Title").Visibility); Assert.Equal(Visibility.Collapsed, Part<TextBlock>(bar, "PART_Message").Visibility);
        Assert.Contains("关闭", Part<Button>(bar, "PART_CloseButton").GetAutomationPeer()!.GetName());
    }
    [Fact]
    public void ClosePreservesTheOpenBindingAndRetiredButtonsStopClosingTheControl()
    {
        var model = new InfoBar { IsOpen = true }; var bar = Create();
        BindingOperations.SetBinding(bar, InfoBar.IsOpenProperty, new Binding(nameof(InfoBar.IsOpen)) { Source = model, Mode = BindingMode.TwoWay });
        Layout(bar); var binding = BindingOperations.GetBindingExpression(bar, InfoBar.IsOpenProperty); var events = new List<string>();
        bar.CloseButtonClick += (_, _) => events.Add("click"); bar.Closed += (_, _) => events.Add("closed");
        var old = Part<Button>(bar, "PART_CloseButton"); old.PerformClick(); Assert.False(bar.IsOpen); Assert.False(model.IsOpen); Assert.Equal(new[] { "click", "closed" }, events); Assert.Same(binding, BindingOperations.GetBindingExpression(bar, InfoBar.IsOpenProperty));
        model.IsOpen = true; Assert.True(bar.IsOpen); var blank = new ControlTemplate(typeof(InfoBar)); blank.SetVisualTree(() => new Border()); bar.Template = blank; Layout(bar);
        old.PerformClick(); Assert.True(bar.IsOpen); Assert.Equal(2, events.Count);
        bar.ClearValue(Control.TemplateProperty); Layout(bar); Part<Button>(bar, "PART_CloseButton").PerformClick(); Assert.False(bar.IsOpen); Assert.Equal(4, events.Count);
    }
    [Fact]
    public void ContentAndActionVisualsSurviveRepeatedTemplateReplacement()
    {
        var content = new TextBlock { Text = "details" }; var action = new Button { Content = "Retry" }; var bar = Create(); bar.Content = content; bar.ActionButton = action; Layout(bar);
        Assert.Same(content, Part<ContentPresenter>(bar, "PART_Content").Content); Assert.Same(action, Part<ContentPresenter>(bar, "PART_Action").Content);
        for (var pass = 0; pass < 3; pass++)
        {
            var blank = new ControlTemplate(typeof(InfoBar)); blank.SetVisualTree(() => new Border()); bar.Template = blank; Layout(bar);
            bar.ClearValue(Control.TemplateProperty); Layout(bar); Assert.Same(content, Part<ContentPresenter>(bar, "PART_Content").Content); Assert.Same(action, Part<ContentPresenter>(bar, "PART_Action").Content);
        }
        bar.IsClosable = false; Assert.False(bar.TryClose()); Assert.Equal(Visibility.Collapsed, Part<Button>(bar, "PART_CloseButton").Visibility);
        bar.IsClosable = true; bar.IsEnabled = false; Assert.False(bar.TryClose());
    }
    [Fact]
    public void ClosedStateCoercesVisibilityWithoutReplacingTheConsumersValueOrBinding()
    {
        var bar = Create(); bar.Visibility = Visibility.Hidden; Layout(bar);
        bar.IsOpen = false; Assert.Equal(Visibility.Collapsed, bar.Visibility);
        bar.IsOpen = true; Assert.Equal(Visibility.Hidden, bar.Visibility);
        bar.Visibility = Visibility.Visible; bar.IsOpen = false; Assert.Equal(Visibility.Collapsed, bar.Visibility);
        bar.IsOpen = true; Assert.Equal(Visibility.Visible, bar.Visibility);
    }
    private static T Part<T>(Control control, string name) where T : class => Assert.IsAssignableFrom<T>(control.FindName(name));
    private static InfoBar Create() { var bar = new InfoBar(); bar.Resources.MergedDictionaries.Add(Defaults.Value); return bar; }
    private static void Layout(FrameworkElement element)
    {
        for (var pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(360, 240)); element.Arrange(new Rect(0, 0, 360, 240)); element.UpdateLayout(); }
    }
}
