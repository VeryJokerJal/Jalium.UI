using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>Style-sheet channel: scoped sheets, selectors with combinators, cascade order, hot swap.</summary>
public sealed class CssStyleSheetApplicationTests
{
    static CssStyleSheetApplicationTests()
    {
        // Type selectors resolve through TypeResolver.ResolveTypeByName, which the
        // Jalium.UI.Xaml module initializer wires to the XamlTypeRegistry. Production apps
        // always load that assembly; force it here so the resolver is present.
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
    public void TypeSelector_AppliesThroughScopedSheet()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.5 }"));
        Flush(panel);

        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(1.0, panel.Opacity);
    }

    [Fact]
    public void ClassSelector_MatchesCssClass()
    {
        var plain = new Border();
        var card = new Border();
        Css.SetClass(card, "card");
        var panel = PanelWith(plain, card);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(".card { opacity: 0.5 }"));
        Flush(panel);

        Assert.Equal(1.0, plain.Opacity);
        Assert.Equal(0.5, card.Opacity);
    }

    [Fact]
    public void IdSelector_MatchesElementName()
    {
        var hero = new Border { Name = "hero" };
        var other = new Border { Name = "other" };
        var panel = PanelWith(hero, other);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("#hero { opacity: 0.25 }"));
        Flush(panel);

        Assert.Equal(0.25, hero.Opacity);
        Assert.Equal(1.0, other.Opacity);
    }

    [Fact]
    public void DescendantCombinator_MatchesAtAnyDepth()
    {
        var deep = new Border();
        var inner = PanelWith(deep);
        var outer = PanelWith(inner);
        Css.SetClass(outer, "page");
        var root = PanelWith(outer);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".page Border { opacity: 0.5 }"));
        Flush(root);

        Assert.Equal(0.5, deep.Opacity);
    }

    [Fact]
    public void DescendantCombinator_DoesNotMatchOutsideScope()
    {
        var outside = new Border();
        var tagged = new StackPanel();
        Css.SetClass(tagged, "page");
        var root = PanelWith(tagged, outside);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".page Border { opacity: 0.5 }"));
        Flush(root);

        Assert.Equal(1.0, outside.Opacity);
    }

    [Fact]
    public void ChildCombinator_OnlyDirectChildren()
    {
        var child = new Border();
        var grandchild = new Border();
        var mid = PanelWith(grandchild);
        var parent = PanelWith(child, mid);
        Css.SetClass(parent, "box");
        var root = PanelWith(parent);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".box > Border { opacity: 0.5 }"));
        Flush(root);

        Assert.Equal(0.5, child.Opacity);
        Assert.Equal(1.0, grandchild.Opacity);
    }

    [Fact]
    public void Specificity_ClassBeatsType_IdBeatsClasses()
    {
        var border = new Border();
        Css.SetClass(border, "a b c");
        border.Name = "x";
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border { opacity: 0.9 } .a { opacity: 0.7 } .a.b.c { opacity: 0.6 } #x { opacity: 0.3 }"));
        Flush(panel);

        Assert.Equal(0.3, border.Opacity);
    }

    [Fact]
    public void DocumentOrder_BreaksSpecificityTies()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border { opacity: 0.9 } Border { opacity: 0.4 }"));
        Flush(panel);

        Assert.Equal(0.4, border.Opacity);
    }

    [Fact]
    public void LaterSheet_WinsTies()
    {
        var border = new Border();
        var panel = PanelWith(border);
        var sheets = Css.GetStyleSheets(panel);
        sheets.Add(CssStyleSheet.Parse("Border { opacity: 0.9 }"));
        sheets.Add(CssStyleSheet.Parse("Border { opacity: 0.4 }"));
        Flush(panel);

        Assert.Equal(0.4, border.Opacity);
    }

    [Fact]
    public void NearerScope_WinsOverOuterScope()
    {
        var border = new Border();
        var inner = PanelWith(border);
        var outer = PanelWith(inner);
        Css.GetStyleSheets(outer).Add(CssStyleSheet.Parse("Border { opacity: 0.9 }"));
        Css.GetStyleSheets(inner).Add(CssStyleSheet.Parse("Border { opacity: 0.4 }"));
        Flush(outer);

        Assert.Equal(0.4, border.Opacity);
    }

    [Fact]
    public void InlineStyle_OutranksSheetRules()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.9 }"));
        Css.SetStyle(border, "opacity: 0.5");
        Flush(panel);

        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void ImportantSheetRule_BeatsInline()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.9 !important }"));
        Css.SetStyle(border, "opacity: 0.5");
        Flush(panel);

        Assert.Equal(0.9, border.Opacity);
    }

    [Fact]
    public void MultiPropertyRule_MergesWithMoreSpecificOverride()
    {
        var border = new Border();
        Css.SetClass(border, "hot");
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border { opacity: 0.9; width: 100px } .hot { opacity: 0.5 }"));
        Flush(panel);

        // The class rule overrides opacity but the type rule's width still applies.
        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(100.0, border.Width);
    }

    [Fact]
    public void HotSwap_RemovingSheetRestoresDefaults()
    {
        var border = new Border();
        var panel = PanelWith(border);
        var sheets = Css.GetStyleSheets(panel);
        var sheet = CssStyleSheet.Parse("Border { opacity: 0.5; margin: 4px }");
        sheets.Add(sheet);
        Flush(panel);
        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(new Thickness(4), border.Margin);

        sheets.Remove(sheet);
        Flush(panel);
        Assert.Equal(1.0, border.Opacity);
        Assert.Equal(new Thickness(0), border.Margin);
    }

    [Fact]
    public void AddingChildLater_GetsStyledOnAttach()
    {
        var panel = new StackPanel();
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.5 }"));
        Flush(panel);

        var late = new Border();
        panel.Children.Add(late);
        Flush(panel);

        Assert.Equal(0.5, late.Opacity);
    }

    [Fact]
    public void DetachedSubtree_LosesAncestorScopedRules()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.5 }"));
        Flush(panel);
        Assert.Equal(0.5, border.Opacity);

        panel.Children.Remove(border);
        Flush(panel);
        Flush(border);
        Assert.Equal(1.0, border.Opacity);
    }

    [Fact]
    public void ClassChange_ReevaluatesElement()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(".warn { opacity: 0.5 }"));
        Flush(panel);
        Assert.Equal(1.0, border.Opacity);

        Css.SetClass(border, "warn");
        Flush(border);
        Assert.Equal(0.5, border.Opacity);

        Css.SetClass(border, string.Empty);
        Flush(border);
        Assert.Equal(1.0, border.Opacity);
    }

    [Fact]
    public void PseudoClassRules_AreInertUntilStateLayer()
    {
        // M4 scope: rules with dynamic pseudo-classes never match statically.
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border:hover { opacity: 0.5 } Border { width: 10px }"));
        Flush(panel);

        Assert.Equal(1.0, border.Opacity);
        Assert.Equal(10.0, border.Width);
    }

    [Fact]
    public void CommaGroup_EachSelectorMatchesIndependently()
    {
        var border = new Border();
        var block = new TextBlock();
        var panel = PanelWith(border, block);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border, TextBlock { opacity: 0.5 }"));
        Flush(panel);

        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(0.5, block.Opacity);
    }

    [Fact]
    public void UniversalSelector_MatchesEverything()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("* { opacity: 0.8 }"));
        Flush(panel);

        Assert.Equal(0.8, border.Opacity);
        Assert.Equal(0.8, panel.Opacity);
    }

    [Fact]
    public void DerivedType_MatchesBaseTypeSelector()
    {
        var button = new Button();
        var panel = PanelWith(button);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Control { opacity: 0.5 }"));
        Flush(panel);

        Assert.Equal(0.5, button.Opacity);
    }

    [Fact]
    public void DeclaredStyleSheet_AppliesToSubtree()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.SetStyleSheet(panel, "Border { opacity: 0.4 }");
        Flush(panel);

        Assert.Equal(0.4, border.Opacity);
        Assert.Single(Css.GetStyleSheets(panel));
    }

    [Fact]
    public void DeclaredStyleSheet_ResetReplacesInsteadOfAccumulating()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.SetStyleSheet(panel, "Border { opacity: 0.4 }");
        Flush(panel);
        Css.SetStyleSheet(panel, "Border { opacity: 0.7 }");
        Flush(panel);

        Assert.Equal(0.7, border.Opacity);
        Assert.Single(Css.GetStyleSheets(panel));
    }

    [Fact]
    public void DeclaredStyleSheet_ClearedRestoresDefault()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.SetStyleSheet(panel, "Border { opacity: 0.4 }");
        Flush(panel);
        Css.SetStyleSheet(panel, string.Empty);
        Flush(panel);

        Assert.Equal(1.0, border.Opacity);
        Assert.Empty(Css.GetStyleSheets(panel));
    }

    [Fact]
    public void DeclaredStyleSheet_CoexistsWithCollectionSheets()
    {
        var border = new Border();
        var panel = PanelWith(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.2 }"));
        Css.SetStyleSheet(panel, "Border { opacity: 0.9 }");
        Flush(panel);

        // The declared sheet is appended, so it wins the document-order tie.
        Assert.Equal(0.9, border.Opacity);
        Assert.Equal(2, Css.GetStyleSheets(panel).Count);

        Css.SetStyleSheet(panel, string.Empty);
        Flush(panel);
        Assert.Equal(0.2, border.Opacity);
        Assert.Single(Css.GetStyleSheets(panel));
    }
}
