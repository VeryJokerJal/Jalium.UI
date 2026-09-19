using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssParserTests
{
    [Fact]
    public void SimpleRule_ParsesSelectorAndDeclarations()
    {
        var sheet = CssStyleSheet.Parse("Button { background-color: red; opacity: 0.5 }");
        var rule = Assert.Single(sheet.Rules);
        var selector = Assert.Single(rule.Selectors);
        var compound = Assert.Single(selector.Compounds);
        Assert.Equal("Button", compound.TypeName);

        Assert.Equal(2, rule.Declarations.Length);
        Assert.Equal("background-color", rule.Declarations[0].PropertyName);
        Assert.Equal("red", rule.Declarations[0].RawValue);
        Assert.Equal("opacity", rule.Declarations[1].PropertyName);
        Assert.Equal("0.5", rule.Declarations[1].RawValue);
        Assert.Empty(sheet.Diagnostics);
    }

    [Fact]
    public void PropertyNames_AreLowercased_ValuesKeepCase()
    {
        var sheet = CssStyleSheet.Parse("Button { Background-COLOR: Red }");
        Assert.Equal("background-color", sheet.Rules[0].Declarations[0].PropertyName);
        Assert.Equal("Red", sheet.Rules[0].Declarations[0].RawValue);
    }

    [Fact]
    public void Important_IsDetectedAndStripped()
    {
        var sheet = CssStyleSheet.Parse("Button { color: red !important; width: 10px }");
        Assert.True(sheet.Rules[0].Declarations[0].Important);
        Assert.Equal("red", sheet.Rules[0].Declarations[0].RawValue);
        Assert.False(sheet.Rules[0].Declarations[1].Important);
    }

    [Fact]
    public void Comments_AreIgnoredEverywhere()
    {
        var sheet = CssStyleSheet.Parse("/* lead */ Button /* mid */ { /* a */ color: /* b */ red; /* c */ }");
        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("red", Assert.Single(rule.Declarations).RawValue);
    }

    [Fact]
    public void ConditionalRules_AreRetainedAndImportsAreDeferred()
    {
        var sheet = CssStyleSheet.Parse(
            "@import url('x.css');\n" +
            "@media (min-width: 600px) { Button { color: red } }\n" +
            "TextBlock { color: blue }");
        Assert.Equal(2, sheet.Rules.Length);
        Assert.NotNull(sheet.Rules[0].Condition);
        Assert.Equal("TextBlock", sheet.Rules[1].Selectors[0].Compounds[0].TypeName);
        Assert.Single(sheet.Imports);
        Assert.Single(sheet.Diagnostics);
        Assert.All(sheet.Diagnostics, d => Assert.Equal(CssDiagnosticSeverity.Info, d.Severity));
    }

    [Fact]
    public void MalformedDeclaration_SkipsToNextSemicolonOnly()
    {
        var sheet = CssStyleSheet.Parse("Button { color red; opacity: 0.5; ; width: 10px }");
        var rule = Assert.Single(sheet.Rules);
        Assert.Equal(2, rule.Declarations.Length);
        Assert.Equal("opacity", rule.Declarations[0].PropertyName);
        Assert.Equal("width", rule.Declarations[1].PropertyName);
        Assert.Contains(sheet.Diagnostics, d => d.Severity == CssDiagnosticSeverity.Warning);
    }

    [Fact]
    public void InvalidSelector_DropsWholeRuleAndRecovers()
    {
        var sheet = CssStyleSheet.Parse("Button[attr=] { color: red } TextBlock { color: blue }");
        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("TextBlock", rule.Selectors[0].Compounds[0].TypeName);
    }

    [Fact]
    public void InvalidSelectorInCommaGroup_InvalidatesWholeGroup()
    {
        var sheet = CssStyleSheet.Parse("Button, ::before { color: red }");
        Assert.Empty(sheet.Rules);
    }

    [Fact]
    public void UnknownPseudoClass_DropsRule()
    {
        var sheet = CssStyleSheet.Parse("Button:visited { color: red }");
        Assert.Empty(sheet.Rules);
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("visited"));
    }

    [Fact]
    public void SiblingCombinators_AreParsed()
    {
        var sheet = CssStyleSheet.Parse("A ~ B { color: red } A + B { color: red } A > B { color: blue }");
        Assert.Equal(3, sheet.Rules.Length);
        Assert.Equal(CssCombinator.GeneralSibling, sheet.Rules[0].Selectors[0].Combinators[0]);
        Assert.Equal(CssCombinator.AdjacentSibling, sheet.Rules[1].Selectors[0].Combinators[0]);
        Assert.Equal(CssCombinator.Child, sheet.Rules[2].Selectors[0].Combinators[0]);
    }

    [Fact]
    public void ComplexSelector_CompoundsAreRightmostFirst()
    {
        var sheet = CssStyleSheet.Parse(".card > Button.primary:hover { color: red }");
        var selector = Assert.Single(Assert.Single(sheet.Rules).Selectors);

        Assert.Equal(2, selector.Compounds.Length);
        var subject = selector.Compounds[0];
        Assert.Equal("Button", subject.TypeName);
        Assert.Equal("primary", Assert.Single(subject.Classes!));
        Assert.Equal(CssPseudoClass.Hover, Assert.Single(subject.Pseudos!));

        var ancestor = selector.Compounds[1];
        Assert.Null(ancestor.TypeName);
        Assert.Equal("card", Assert.Single(ancestor.Classes!));

        Assert.Equal(CssCombinator.Child, Assert.Single(selector.Combinators));
        Assert.Equal(CssStateMask.Hover, selector.RightmostStates);
        Assert.Equal(CssStateMask.None, selector.AncestorStates);
        Assert.True(selector.HasCombinators);
    }

    [Fact]
    public void AncestorPseudo_LandsInAncestorStates()
    {
        var sheet = CssStyleSheet.Parse(".card:hover .title { color: red }");
        var selector = sheet.Rules[0].Selectors[0];
        Assert.Equal(CssStateMask.None, selector.RightmostStates);
        Assert.Equal(CssStateMask.Hover, selector.AncestorStates);
        Assert.Equal(CssStateMask.Hover, sheet.AncestorStateUnion);
        Assert.Equal(CssCombinator.Descendant, Assert.Single(selector.Combinators));
    }

    [Theory]
    [InlineData("Button", 0, 0, 1)]
    [InlineData("*", 0, 0, 0)]
    [InlineData(".a", 0, 1, 0)]
    [InlineData("#x", 1, 0, 0)]
    [InlineData("Button.a.b:hover", 0, 3, 1)]
    [InlineData("#x .a Button", 1, 1, 1)]
    public void Specificity_MatchesCssRules(string selectorText, int ids, int classes, int types)
    {
        var sheet = CssStyleSheet.Parse(selectorText + " { color: red }");
        var selector = Assert.Single(Assert.Single(sheet.Rules).Selectors);
        Assert.Equal(CssSelector.PackSpecificity(ids, classes, types), selector.Specificity);
    }

    [Fact]
    public void Specificity_OrderingIsIdOverClassOverType()
    {
        var id = CssSelector.PackSpecificity(1, 0, 0);
        var manyClasses = CssSelector.PackSpecificity(0, 30, 0);
        var manyTypes = CssSelector.PackSpecificity(0, 0, 1000);
        Assert.True(id > manyClasses);
        Assert.True(manyClasses > manyTypes);
    }

    [Fact]
    public void CommaGroup_ExpandsToMultipleSelectors()
    {
        var sheet = CssStyleSheet.Parse("Button, TextBlock, .warn { opacity: 1 }");
        Assert.Equal(3, Assert.Single(sheet.Rules).Selectors.Length);
    }

    [Fact]
    public void SheetLevelFlags_AreComputed()
    {
        var sheet = CssStyleSheet.Parse("Button { color: red }");
        Assert.False(sheet.HasCombinators);
        Assert.False(sheet.UsesId);
        Assert.Equal(CssStateMask.None, sheet.AnyStateUnion);

        var sheet2 = CssStyleSheet.Parse("#panel Button:active { color: red }");
        Assert.True(sheet2.HasCombinators);
        Assert.True(sheet2.UsesId);
        Assert.Equal(CssStateMask.Active, sheet2.AnyStateUnion);
        Assert.Equal(CssStateMask.None, sheet2.AncestorStateUnion);
    }

    [Fact]
    public void InlineDeclarations_ParseWithoutBraces()
    {
        var declarations = CssParser.ParseInlineDeclarations(
            "background-color: red; margin: 4px 8px; opacity: .5", null);
        Assert.Equal(3, declarations.Count);
        Assert.Equal("margin", declarations[1].PropertyName);
        Assert.Equal("4px 8px", declarations[1].RawValue);
    }

    [Fact]
    public void InlineDeclarations_ValueWithFunctionContainingSemicolonSafeChars()
    {
        var declarations = CssParser.ParseInlineDeclarations(
            "background: linear-gradient(90deg, red, blue); color: white", null);
        Assert.Equal(2, declarations.Count);
        Assert.Equal("linear-gradient(90deg, red, blue)", declarations[0].RawValue);
    }

    [Fact]
    public void UnterminatedBlock_ReportsWarning()
    {
        var sheet = CssStyleSheet.Parse("Button { color: red");
        Assert.Single(sheet.Rules);
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("unterminated"));
    }

    [Fact]
    public void GarbageInput_NeverThrows()
    {
        foreach (var text in new[] { "", "}", "{}", "@", "@media {", "a{b:c", "((({{{", "/*", "a b c", "\"str" })
        {
            _ = CssStyleSheet.Parse(text);
        }
    }

    [Fact]
    public void LineNumbers_AreTracked()
    {
        var sheet = CssStyleSheet.Parse("Button { color: red }\n\n@unknown x { }\n");
        var diag = Assert.Single(sheet.Diagnostics);
        Assert.Equal(3, diag.Line);
    }

    [Fact]
    public void RuleIndices_FollowDocumentOrder()
    {
        var sheet = CssStyleSheet.Parse("A { color: red } B { color: blue } C { color: green }");
        Assert.Equal(new[] { 0, 1, 2 }, sheet.Rules.Select(r => r.RuleIndex));
    }
}
