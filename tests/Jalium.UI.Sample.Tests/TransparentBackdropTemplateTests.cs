using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.TransparentBackdropDemo;

namespace Jalium.UI.Sample.Tests;

public sealed class TransparentBackdropTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void EveryEffectAndTintAppliesToTheOwnerWithoutChangingThePalette()
    {
        var owner = new Window(); var surface = Create(); surface.OwnerWindow = owner; Layout(surface);
        var selector = Assert.IsType<ListBox>(surface.FindName("PART_Effects"));
        Assert.Equal(5, selector.Items.Count);
        foreach (var choice in surface.Effects)
        {
            selector.SelectedItem = choice;
            Assert.Equal(choice.Type, surface.SelectedEffect); Assert.Equal(choice.Type, owner.SystemBackdrop);
        }
        Assert.Null(owner.Background);
        var button = Assert.IsType<Button>(surface.FindName("PART_ToggleBackground"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        Assert.Same(surface.TintBrush, owner.Background); Assert.True(surface.IsTinted);
        Assert.Equal("Tinted", VisualStateManager.GetCurrentStateName(surface, "BackgroundStates"));
        var replacement = new SolidColorBrush(Colors.Blue); surface.TintBrush = replacement; Assert.Same(replacement, owner.Background);
        surface.ToggleBackground(); Assert.Null(owner.Background);
        Assert.Contains("null", surface.StatusText);
        surface.OwnerWindow = null;
    }

    [Fact]
    public void ReplacingTemplateDisconnectsRetiredSelectorAndButtonAndRetainsSelection()
    {
        var surface = Create(); Layout(surface); surface.SelectedEffect = WindowBackdropType.Mica;
        var selector = Assert.IsType<ListBox>(surface.FindName("PART_Effects"));
        var button = Assert.IsType<Button>(surface.FindName("PART_ToggleBackground"));
        var blank = new ControlTemplate(typeof(TransparentBackdropSurface)); blank.SetVisualTree(() => new Border());
        surface.Template = blank; Layout(surface);
        selector.SelectedItem = surface.Effects.Last(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        Assert.Equal(WindowBackdropType.Mica, surface.SelectedEffect); Assert.False(surface.IsTinted);
        surface.ClearValue(Control.TemplateProperty); Layout(surface);
        Assert.Equal(WindowBackdropType.Mica, Assert.IsType<TransparentBackdropSurface.BackdropChoice>(Assert.IsType<ListBox>(surface.FindName("PART_Effects")).SelectedItem).Type);
        Assert.True(Assert.IsType<Button>(surface.FindName("PART_ToggleBackground")).RenderSize.Width <= 216);
        surface.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(surface, "CommonStates"));
    }

    private static TransparentBackdropSurface Create()
    {
        var surface = new TransparentBackdropSurface(); surface.Resources.MergedDictionaries.Add(Defaults.Value); return surface;
    }
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(320, 540)); element.Arrange(new Rect(0, 0, 320, 540)); element.UpdateLayout();
    }
}
