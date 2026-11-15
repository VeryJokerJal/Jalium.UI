using Jalium.UI.BorderlessDemo;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Sample.Tests;

public sealed class BorderlessDemoTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void CompiledThemeWrapsAndRetainsInstanceProperties()
    {
        var surface = Create(); surface.Title = "窗口模板"; surface.Description = "可复用的窗口操作";
        Layout(surface, 320);
        Assert.IsType<Grid>(surface.GetVisualChild(0));
        Assert.Equal("窗口模板", Assert.IsType<TitleBar>(surface.FindName("PART_TitleBar")).Title);
        var button = Assert.IsType<Button>(surface.FindName("PART_ToggleWindowStyle"));
        Assert.True(button.RenderSize.Width <= 256);
        surface.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(surface, "CommonStates"));
    }

    [Fact]
    public void OptionalPartsAndRetiredButtonsDoNotChangeTheOwner()
    {
        var owner = new Window { WindowStyle = WindowStyle.None };
        var surface = Create(); surface.OwnerWindow = owner; Layout(surface);
        var old = Assert.IsType<Button>(surface.FindName("PART_ToggleWindowStyle"));
        Click(old); Assert.Equal(WindowStyle.SingleBorderWindow, owner.WindowStyle);
        var blank = new ControlTemplate(typeof(BorderlessDemoSurface)); blank.SetVisualTree(() => new Border());
        surface.Template = blank; Layout(surface); Click(old);
        Assert.Equal(WindowStyle.SingleBorderWindow, owner.WindowStyle);
        surface.ClearValue(Control.TemplateProperty); Layout(surface);
        Click(Assert.IsType<Button>(surface.FindName("PART_ToggleWindowStyle")));
        Assert.Equal(WindowStyle.None, owner.WindowStyle);
        Assert.Contains("WindowStyle=None", surface.StatusText);
        surface.OwnerWindow = null;
    }

    [Fact]
    public void KeyboardAndStateFollowOnlyTheCurrentOwner()
    {
        var first = new Window(); var second = new Window();
        var surface = Create(); surface.OwnerWindow = first; Layout(surface);
        Press(first, Key.F11); Assert.Equal(WindowState.FullScreen, first.WindowState);
        Assert.Equal("FullScreen", VisualStateManager.GetCurrentStateName(surface, "WindowStates"));
        Press(first, Key.Escape); Assert.Equal(WindowState.Normal, first.WindowState);
        surface.OwnerWindow = second;
        Press(first, Key.F11); Assert.Equal(WindowState.Normal, first.WindowState);
        Press(second, Key.F11); Assert.Equal(WindowState.FullScreen, second.WindowState);
        surface.ToggleMaximize(); Assert.Equal(WindowState.Maximized, second.WindowState);
        Assert.True(Assert.IsType<TitleBar>(surface.FindName("PART_TitleBar")).IsMaximized);
        surface.ToggleMaximize(); Assert.Equal(WindowState.Normal, second.WindowState);
        surface.OwnerWindow = null;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        protected override CompositionTarget? GetCompositionTargetCore() => null;
        public override bool IsDisposed => false;
    }
    private static void Press(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new Source { RootVisual = window }, Environment.TickCount, key) { RoutedEvent = UIElement.KeyDownEvent, Source = window });
    private static BorderlessDemoSurface Create()
    {
        var surface = new BorderlessDemoSurface(); surface.Resources.MergedDictionaries.Add(Defaults.Value); return surface;
    }
    private static void Layout(FrameworkElement element, double width = 820)
    {
        element.InvalidateMeasure(); element.Measure(new Size(width, 560)); element.Arrange(new Rect(0, 0, width, 560)); element.UpdateLayout();
    }
}
