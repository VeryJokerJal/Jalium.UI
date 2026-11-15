using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TerminalTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void CharacterSurfaceIsOwnedByEachTemplateInstance()
    {
        var first = Create(); var second = Create(); Layout(first); Layout(second);
        var a = Assert.IsType<TerminalView>(first.FindName("PART_View")); var b = Assert.IsType<TerminalView>(second.FindName("PART_View"));
        Assert.Same(first, a.Owner); Assert.Same(second, b.Owner); Assert.NotSame(a, b);
        Assert.Same(a, Assert.IsType<ScrollViewer>(first.FindName("PART_ScrollViewer")).Content);
        first.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(first, "CommonStates"));
    }
    [Fact]
    public void ReplacingTheTemplateKeepsBufferAndScrollButReleasesTheRetiredSurface()
    {
        var terminal = Create(); Layout(terminal);
        for (var line = 0; line < 80; line++) terminal.WriteLine($"line-{line:D2}");
        Layout(terminal); var old = Assert.IsType<TerminalView>(terminal.FindName("PART_View")); old.SetVerticalOffset(100); Layout(terminal);
        var offset = old.VerticalOffset; Assert.True(offset > 0); var text = Text(terminal);
        var blank = new ControlTemplate(typeof(Terminal)); blank.SetVisualTree(() => new Border()); terminal.Template = blank; Layout(terminal);
        Assert.Null(old.Owner); Assert.Null(old.ScrollOwner); Assert.Equal(text, Text(terminal));
        terminal.ClearValue(Control.TemplateProperty); Layout(terminal);
        var current = Assert.IsType<TerminalView>(terminal.FindName("PART_View")); Assert.NotSame(old, current); Assert.Same(terminal, current.Owner);
        Assert.Equal(offset, current.VerticalOffset, 4); Assert.Equal(text, Text(terminal));
        terminal.WriteLine("retained-output"); Assert.Contains("retained-output", Text(terminal)); Assert.Null(old.Owner);
    }
    [Fact]
    public void OwnerlessSurfaceAndMissingOptionalPartsRemainEmpty()
    {
        var surface = new TerminalView(); Layout(surface); surface.LineDown(); surface.MouseWheelUp(); Assert.Equal(0, surface.VerticalOffset);
        var terminal = Create(); var scroller = new ScrollViewer(); var template = new ControlTemplate(typeof(Terminal)); template.SetVisualTree(() => scroller);
        terminal.Template = template; Layout(terminal); Assert.Null(scroller.Content); terminal.WriteLine("no-parts"); Assert.Contains("no-parts", Text(terminal));
    }
    private static Terminal Create()
    {
        var terminal = new Terminal { AutoStartShell = false, AutoSize = false, TerminalColumns = 40, TerminalRows = 8 };
        terminal.Resources.MergedDictionaries.Add(Defaults.Value); return terminal;
    }
    private static string Text(Terminal terminal) => Assert.IsAssignableFrom<ITextProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Text)).DocumentRange.GetText(-1);
    private static void Layout(FrameworkElement element)
    {
        for (var pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(640, 240)); element.Arrange(new Rect(0, 0, 640, 240)); element.UpdateLayout(); }
    }
}
