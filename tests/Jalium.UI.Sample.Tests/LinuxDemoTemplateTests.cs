using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.LinuxDemo;

namespace Jalium.UI.Sample.Tests;

public sealed class LinuxDemoTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void TemplateConnectsInputSelectionAndProgressToThePublicProperties()
    {
        var surface = Create(); Layout(surface);
        var text = Assert.IsType<TextBox>(surface.FindName("PART_Text")); text.Text = "中文输入"; Assert.Equal(text.Text, surface.Text);
        var combo = Assert.IsType<ComboBox>(surface.FindName("PART_Backends")); Assert.Equal(4, combo.Items.Count);
        combo.SelectedIndex = 2; Assert.Equal(2, surface.SelectedBackendIndex); Assert.Contains("软件渲染", surface.StatusText);
        var slider = Assert.IsType<Slider>(surface.FindName("PART_Slider")); slider.Value = 72;
        Assert.Equal(72, surface.SliderValue); Assert.Equal(72, Assert.IsType<ProgressBar>(surface.FindName("PART_Progress")).Value);
        var feature = Assert.IsType<CheckBox>(surface.FindName("PART_Feature")); feature.IsChecked = false;
        Assert.False(surface.FeatureEnabled); Assert.Equal("CheckBox: off", surface.StatusText);
        var list = Assert.IsType<ListBox>(surface.FindName("PART_Integrations")); list.SelectedIndex = 1;
        Assert.Equal(list.SelectedItem, surface.SelectedIntegration); Assert.Contains("列表选择", surface.StatusText);
        var other = Create(); Assert.NotEqual(surface.RadioGroupName, other.RadioGroupName);
    }
    [Fact]
    public void MissingPartsAndRepeatedTemplatesRetainDataAndDisconnectOldActions()
    {
        var surface = Create(); Layout(surface);
        var old = Assert.IsType<Button>(surface.FindName("PART_Click")); old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old)); Assert.Equal(1, surface.ClickCount);
        surface.Text = "保留"; surface.SliderValue = 55; surface.SelectedBackendIndex = 2; surface.SelectedIntegration = surface.Integrations[2];
        var blank = new ControlTemplate(typeof(LinuxDemoSurface)); blank.SetVisualTree(() => new Border());
        surface.Template = blank; Layout(surface); old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old)); Assert.Equal(1, surface.ClickCount);
        surface.ClearValue(Control.TemplateProperty); Layout(surface);
        Assert.Equal("保留", Assert.IsType<TextBox>(surface.FindName("PART_Text")).Text);
        Assert.Equal(55, Assert.IsType<ProgressBar>(surface.FindName("PART_Progress")).Value);
        Assert.Equal(2, surface.SelectedBackendIndex);
        Assert.Equal(2, Assert.IsType<ComboBox>(surface.FindName("PART_Backends")).SelectedIndex);
        Assert.Equal(surface.Integrations[2], surface.SelectedIntegration);
        Assert.Equal(surface.Integrations[2], Assert.IsType<ListBox>(surface.FindName("PART_Integrations")).SelectedItem);
        var current = Assert.IsType<Button>(surface.FindName("PART_Click")); surface.OnApplyTemplate();
        current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, current)); Assert.Equal(2, surface.ClickCount);
        var about = Assert.IsType<MenuItem>(surface.FindName("PART_About")); about.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, about)); Assert.Equal("ContextMenu → 关于", surface.StatusText);
        surface.IsEnabled = false; current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, current)); Assert.Equal(2, surface.ClickCount);
    }
    private static LinuxDemoSurface Create()
    {
        var surface = new LinuxDemoSurface(); surface.Resources.MergedDictionaries.Add(Defaults.Value); return surface;
    }
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(420, 560)); element.Arrange(new Rect(0, 0, 420, 560)); element.UpdateLayout();
    }
}
