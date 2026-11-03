using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssCascadeLayerTests
{
    private static Border Apply(string text, string? inline = null)
    {
        var element = new Border { Name = "hero" };
        Css.SetClass(element, "box"); Css.SetStyleSheet(element, text);
        if (inline is not null) Css.SetStyle(element, inline);
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        return element;
    }

    [Fact]
    public void Layers_PrecedeSpecificityAndUnlayeredRulesWinNormalDeclarations()
    {
        var element = Apply("@layer reset, app; @layer reset {#hero {opacity:.2}} @layer app {.box {opacity:.4}}");
        Assert.Equal(.4, element.Opacity);
        Css.SetStyleSheet(element, "@layer app {#hero {opacity:.2}} .box {opacity:.8}");
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        Assert.Equal(.8, element.Opacity);
    }

    [Fact]
    public void Important_ReversesLayerOrderAndInlineImportantStillWins()
    {
        const string text = "@layer reset, app; @layer reset {.box {opacity:.2 !important}} @layer app {#hero {opacity:.4 !important}} #hero {opacity:.8 !important}";
        Assert.Equal(.2, Apply(text).Opacity);
        Assert.Equal(.9, Apply(text, "opacity:.9 !important").Opacity);
    }

    [Fact]
    public void RevertLayer_RevealsThePreviousLayerIncludingCustomProperties()
    {
        var element = Apply("@layer base {.box {opacity:.2; --size:30px}} @layer app {.box {opacity:.8; opacity:revert-layer; --size:60px; --size:revert-layer}} .box {width:var(--size)}");
        Assert.Equal(.2, element.Opacity); Assert.Equal(30, element.Width);
    }

    [Fact]
    public void Revert_RemovesTheAuthorContributionAndRevealsTheHostStyle()
    {
        var element = new Border { Style = new Style(typeof(Border)) { Setters = { new Setter(UIElement.OpacityProperty, .6) } } };
        Css.SetStyle(element, "opacity:.2; opacity:revert");
        Assert.Equal(.6, element.Opacity);
    }

    [Fact]
    public void NestedLayers_StayWithinTheirParentLayer()
    {
        var element = Apply("@layer base, app; @layer base { @layer nested {#hero {opacity:.2}} } @layer app {.box {opacity:.8}}");
        Assert.Equal(.8, element.Opacity);
        element = Apply("@layer app { @layer nested {.box {opacity:.2}} .box {opacity:.6} }");
        Assert.Equal(.6, element.Opacity);
    }

    [Fact]
    public void LayerOrder_IsSharedAcrossStylesheetsInTheSameScope()
    {
        var element = new Border(); Css.SetClass(element, "box");
        Css.GetStyleSheets(element).Add(CssStyleSheet.Parse("@layer a,b; @layer b {.box {opacity:.4}}"));
        Css.GetStyleSheets(element).Add(CssStyleSheet.Parse("@layer a {.box {opacity:.8}}"));
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        Assert.Equal(.4, element.Opacity);
    }

    [Fact]
    public void AllInitial_ResetsCssPropertiesWithoutOverridingLocalValues()
    {
        var element = new Border { Width = 100 };
        Css.SetStyle(element, "opacity:.2; margin:10px; width:200px; all:initial");
        Assert.Equal(1, element.Opacity); Assert.Equal(default, element.Margin); Assert.Equal(100, element.Width);
    }
}
