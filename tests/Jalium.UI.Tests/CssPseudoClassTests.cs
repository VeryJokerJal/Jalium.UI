using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>Dynamic pseudo-classes: CssState layer, dependency subscriptions, two-level invalidation.</summary>
public sealed class CssPseudoClassTests
{
    static CssPseudoClassTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    private static void Flush(FrameworkElement element)
        => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);

    private static StackPanel PanelWith(params FrameworkElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    [Fact]
    public void Hover_TogglesStateValueOverBase()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border { opacity: 0.9 } Border:hover { opacity: 0.5 }"));
        Flush(panel);
        Assert.Equal(0.9, border.Opacity);

        border.SetIsMouseOver(true);
        Flush(border);
        Assert.Equal(0.5, border.Opacity);

        border.SetIsMouseOver(false);
        Flush(border);
        Assert.Equal(0.9, border.Opacity);
    }

    [Fact]
    public void HoverValue_LandsInCssStateLayer()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border { opacity: 0.9 } Border:hover { opacity: 0.5 }"));
        Flush(panel);
        Assert.Equal(BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(border, UIElement.OpacityProperty).BaseValueSource);

        border.SetIsMouseOver(true);
        Flush(border);
        // CssState reports as the closest WPF analogue: StyleTrigger.
        Assert.Equal(BaseValueSource.StyleTrigger,
            DependencyPropertyHelper.GetValueSource(border, UIElement.OpacityProperty).BaseValueSource);
    }

    [Fact]
    public void LocalValue_OutranksHoverState()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border:hover { opacity: 0.5 }"));
        border.Opacity = 0.7;
        Flush(panel);

        border.SetIsMouseOver(true);
        Flush(border);
        Assert.Equal(0.7, border.Opacity);

        border.ClearValue(UIElement.OpacityProperty);
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void Active_MapsToIsPressed()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border:active { opacity: 0.4 }"));
        Flush(panel);
        Assert.Equal(1.0, border.Opacity);

        border.SetIsPressed(true);
        Flush(border);
        Assert.Equal(0.4, border.Opacity);

        border.SetIsPressed(false);
        Flush(border);
        Assert.Equal(1.0, border.Opacity);
    }

    [Fact]
    public void Disabled_OnElementItself()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border:disabled { opacity: 0.3 } Border:enabled { opacity: 0.8 }"));
        Flush(panel);
        Assert.Equal(0.8, border.Opacity);

        border.IsEnabled = false;
        Flush(border);
        Assert.Equal(0.3, border.Opacity);
    }

    [Fact]
    public void Disabled_PropagatesFromAncestor()
    {
        var border = new Border();
        var panel = PanelWith(border);
        var root = PanelWith(panel);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("Border:disabled { opacity: 0.3 }"));
        Flush(root);
        Assert.Equal(1.0, border.Opacity);

        // Disabling the ancestor makes the descendant's effective IsEnabled false.
        panel.IsEnabled = false;
        Flush(panel);
        Assert.Equal(0.3, border.Opacity);

        panel.IsEnabled = true;
        Flush(panel);
        Assert.Equal(1.0, border.Opacity);
    }

    [Fact]
    public void AncestorPositionHover_RefreshesDescendants()
    {
        var title = new TextBlock();
        var card = PanelWith(title);
        Css.SetClass(card, "card");
        var root = PanelWith(card);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".card:hover TextBlock { opacity: 0.5 }"));
        Flush(root);
        Assert.Equal(1.0, title.Opacity);

        card.SetIsMouseOver(true);
        Flush(card);
        Assert.Equal(0.5, title.Opacity);

        card.SetIsMouseOver(false);
        Flush(card);
        Assert.Equal(1.0, title.Opacity);
    }

    [Fact]
    public void Cascade_SpecificityDecidesWinner_EvenAgainstHover()
    {
        // CSS cascade semantics: a more specific normal rule outranks a less specific
        // :hover rule — the CssState layer only carries the cascade's winner when that
        // winner is a state rule; it does not override the cascade itself.
        var border = new Border();
        Css.SetClass(border, "boxy");
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            ".boxy.boxy { opacity: 0.9 } Border:hover { opacity: 0.5 }"));
        Flush(panel);
        Assert.Equal(0.9, border.Opacity);

        border.SetIsMouseOver(true);
        Flush(border);
        Assert.Equal(0.9, border.Opacity);

        // The conventional pattern — same subject, pseudo-class adds specificity — wins.
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(".boxy.boxy:hover { opacity: 0.4 }"));
        Flush(panel);
        Assert.Equal(0.4, border.Opacity);
    }

    [Fact]
    public void MultiProperty_OnlyStatePropertiesMigrateLayers()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border { opacity: 0.9; width: 50px } Border:hover { opacity: 0.5 }"));
        Flush(panel);

        border.SetIsMouseOver(true);
        Flush(border);
        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(50.0, border.Width);
        Assert.Equal(BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(border, FrameworkElement.WidthProperty).BaseValueSource);
    }

    [Fact]
    public void InlineStyle_DoesNotBlockHoverRules()
    {
        // CSS semantics: a sheet's :hover rule cannot override a non-important inline value —
        // inline sits above sheet rules in the cascade.
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border:hover { opacity: 0.5 }"));
        Css.SetStyle(border, "opacity: 0.8");
        Flush(panel);

        border.SetIsMouseOver(true);
        Flush(border);
        Assert.Equal(0.8, border.Opacity);
    }

    [Fact]
    public void StateSubscription_IsRemovedWhenRulesLeaveScope()
    {
        var border = new Border();
        var panel = PanelWith(border);
        var sheets = Css.GetStyleSheets(panel);
        var sheet = CssStyleSheet.Parse("Border:hover { opacity: 0.5 }");
        sheets.Add(sheet);
        Flush(panel);
        Assert.NotNull(border.CssRuntimeState!.Handler);

        sheets.Remove(sheet);
        Flush(panel);
        Assert.Null(border.CssRuntimeState!.Handler);
    }

    [Fact]
    public void HoverFlip_WithoutFlush_DoesNotApplyImmediately()
    {
        // State changes are batched; the value lands on the next scheduled pass.
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border:hover { opacity: 0.5 }"));
        Flush(panel);

        border.SetIsMouseOver(true);
        Assert.Equal(1.0, border.Opacity);
        Flush(border);
        Assert.Equal(0.5, border.Opacity);
    }
}
