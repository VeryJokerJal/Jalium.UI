using Jalium.UI.AotWindowDemo;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Sample.Tests;

public sealed class AotCounterTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void GeneratedThemeExposesTheCounterAndWrapsAtNarrowWidths()
    {
        var control = Create(); Layout(control, 320);
        Assert.IsType<Border>(control.GetVisualChild(0));
        Assert.Equal("(idle)", Assert.IsType<TextBlock>(control.FindName("PART_StatusText")).Text);
        var button = Assert.IsType<Button>(control.FindName("PART_IncrementButton"));
        Assert.True(button.RenderSize.Width <= 256);
        var changes = 0; control.CountChanged += (_, _) => changes++;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        Assert.Equal(1, control.ClickCount); Assert.Equal(1, changes);
        Assert.Equal("1 click — UI thread is alive.", Assert.IsType<TextBlock>(control.FindName("PART_StatusText")).Text);
        Assert.Equal("Active", VisualStateManager.GetCurrentStateName(control, "CountStates"));
    }

    [Fact]
    public void TemplateReplacementRetainsCountAndDisconnectsOldButtons()
    {
        var control = Create(); Layout(control);
        var old = Assert.IsType<Button>(control.FindName("PART_IncrementButton")); control.Increment(); control.Increment();
        var blank = new ControlTemplate(typeof(AotCounterSurface)); blank.SetVisualTree(() => new Border());
        control.Template = blank; Layout(control); old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old));
        Assert.Equal(2, control.ClickCount);
        control.ClearValue(Control.TemplateProperty); Layout(control);
        Assert.Equal("2 clicks", Assert.IsType<TextBlock>(control.FindName("PART_StatusText")).Text);
        control.Reset(); Assert.Equal("Idle", VisualStateManager.GetCurrentStateName(control, "CountStates"));
        control.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(control, "CommonStates"));
    }

    private static AotCounterSurface Create()
    {
        var control = new AotCounterSurface(); control.Resources.MergedDictionaries.Add(Defaults.Value); return control;
    }
    private static void Layout(FrameworkElement element, double width = 600)
    {
        element.InvalidateMeasure(); element.Measure(new Size(width, 400)); element.Arrange(new Rect(0, 0, width, 400)); element.UpdateLayout();
    }
}
