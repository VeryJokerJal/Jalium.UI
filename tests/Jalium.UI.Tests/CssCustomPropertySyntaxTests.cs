using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssCustomPropertySyntaxTests
{
    [Theory]
    [Trait("WPT", "css/css-variables/wide-keyword-fallback-002.html")]
    [InlineData("inherit")]
    [InlineData("unset")]
    public void VariableFallbackWideKeywordInheritsCustomProperty(string keyword)
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(root, "--size:29px");
        Css.SetStyle(child, $"--size:var(--missing, {keyword});width:var(--size)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(29, child.Width);

        Css.SetStyle(root, "--size:41px");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(41, child.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/wide-keyword-fallback-002.html")]
    public void InitialFallbackMakesCustomPropertyGuaranteedInvalid()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(root, "--size:29px");
        Css.SetStyle(child, "--size:var(--missing, initial);width:var(--size, 17px)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(17, child.Width);
        Assert.False(CssNode.Get(child).CssRuntimeState!.CustomProperties!.ContainsKey("--size"));
    }

    [Theory]
    [InlineData("inherit", 29)]
    [InlineData("unset", 5)]
    [InlineData("initial", 5)]
    public void RegisteredNonInheritedPropertyHonorsSubstitutedWideKeyword(string keyword, double expected)
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@property --size {syntax:'<length>';inherits:false;initial-value:5px}");
        Css.SetStyle(root, "--size:29px");
        Css.SetStyle(child, $"--size:var(--missing, {keyword});width:var(--size)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(expected, child.Width);
    }

    [Theory]
    [Trait("WPT", "css/css-variables/variable-css-wide-keywords-after-substitution.html")]
    [InlineData("initial", 17)]
    [InlineData("inherit", 29)]
    [InlineData("unset", 29)]
    [InlineData("revert", 29)]
    public void EmptyVariableBeforeWideKeywordKeepsItsKeywordMeaning(string keyword, double expected)
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(root, "--size:29px");
        Css.SetStyle(child, $"--empty:;--size:var(--empty) {keyword};width:var(--size, 17px)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(expected, child.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/variable-css-wide-keywords-after-substitution.html")]
    public void EmptyVariableBeforeRevertLayerSelectsEarlierCustomValue()
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, """
            @layer a {#hero {--size:11px}}
            @layer b {#hero {--size:var(--empty) revert-layer}}
            #hero {--empty:;width:var(--size)}
            """);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(11, border.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/variable-css-wide-keywords-after-substitution.html")]
    public void EmptyValueFromRevertLayerCanPrecedeAnotherBackgroundToken()
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, """
            @layer a {#hero {--color:}}
            @layer b {#hero {--color:var(--empty) revert-layer}}
            #hero {--empty:;background:var(--color) green}
            """);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(string.Empty, CssNode.Get(border).CssRuntimeState!.CustomProperties!["--color"]);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(border.Background).Color);
    }

    [Theory]
    [InlineData("one ! two")]
    [InlineData("one ]")]
    [InlineData("one )")]
    [InlineData("[one ) two]")]
    [InlineData("\"bad\nstring")]
    [InlineData("url(foo bar)")]
    [InlineData("url(foo\"bar)")]
    [InlineData("url(foo\\\nbar)")]
    [InlineData("u\\72l(foo bar)")]
    [InlineData("url(foo\u0007bar)")]
    [InlineData("fn(url(foo bar))")]
    [InlineData("var()")]
    public void InvalidDeclarationValuesAreDropped(string value)
    {
        var diagnostics = new List<CssParseDiagnostic>();
        var declarations = CssParser.ParseInlineDeclarations("--x: " + value, diagnostics);

        Assert.Empty(declarations);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CssDiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("one !important")]
    [InlineData("fn(!)")]
    [InlineData("[!]")]
    [InlineData("\"!\"")]
    [InlineData("\\!")]
    [InlineData("url(foo!bar)")]
    [InlineData("url(\"foo bar\")")]
    [InlineData("url( \"foo bar\")")]
    [InlineData("url(  \"foo\")")]
    [InlineData("url( \t  'foo')")]
    [InlineData("url(foo\\ bar)")]
    [InlineData("1url(foo bar)")]
    [InlineData("#url(foo bar)")]
    [InlineData("@url(foo bar)")]
    [InlineData("fn(")]
    [InlineData("var(--missing)")]
    [InlineData("var(--missing,)")]
    [InlineData("var(--missing, var(--other))")]
    [InlineData(@"v\61 r(--missing)")]
    [InlineData("var(foo)")]
    [InlineData("var(--)")]
    [InlineData("var(--missing extra)")]
    [InlineData("var(--missing / 2)")]
    [InlineData("calc(var(foo) + 1px)")]
    [InlineData("var(--missing, var(foo))")]
    [InlineData(@"v\61 r(foo)")]
    [InlineData("\"var(foo)\"")]
    [InlineData("url(\"var(foo)\")")]
    public void ValidDeclarationValuesRemainAvailable(string value)
    {
        var declaration = Assert.Single(CssParser.ParseInlineDeclarations("--x: " + value, null));
        Assert.Equal("--x", declaration.PropertyName);
    }

    [Fact]
    public void InvalidLaterCustomPropertyDoesNotHideEarlierValue()
    {
        var sheet = CssStyleSheet.Parse(
            ".box { --size: 4px; --size: 9px ! nope; width: var(--size, 1px) }");
        var border = new Border();
        Css.SetClass(border, "box");
        Css.GetStyleSheets(border).Add(sheet);

        border.Measure(new(100, 100));
        border.UpdateLayout();

        Assert.Equal(4, border.Width);
    }

    [Fact]
    public void InvalidComputedVariableNameHidesEarlierCustomProperty()
    {
        var sheet = CssStyleSheet.Parse(
            ".box { --size: 4px; --size: calc(var(foo) + 1px); width: var(--size, 1px) }");
        var border = new Border();
        Css.SetClass(border, "box");
        Css.GetStyleSheets(border).Add(sheet);

        border.Measure(new(100, 100));
        border.UpdateLayout();

        Assert.Equal(1, border.Width);
    }

    [Fact]
    public void MissingVariableRemainsValidUntilComputedValueTime()
    {
        var sheet = CssStyleSheet.Parse(
            ".box { --size: 4px; --size: var(--missing); width: var(--size, 1px) }");
        var border = new Border();
        Css.SetClass(border, "box");
        Css.GetStyleSheets(border).Add(sheet);

        border.Measure(new(100, 100));
        border.UpdateLayout();

        Assert.Equal(1, border.Width);
    }

    [Fact]
    public void InvalidComputedVariableNameOverridesEarlierOrdinaryDeclaration()
    {
        var diagnostics = new List<CssParseDiagnostic>();
        var declarations = CssParser.ParseInlineDeclarations("width: 4px; width: var(foo)", diagnostics);

        Assert.Equal(2, declarations.Count);
        Assert.Empty(diagnostics);

        var border = new Border();
        Css.SetStyle(border, "width: 4px; width: var(foo)");
        Assert.True(double.IsNaN(border.Width));
    }

    [Theory]
    [InlineData("#var(foo)")]
    [InlineData("@var(foo)")]
    [InlineData("1var(foo)")]
    public void VariableTextInsideOtherTokensIsNotAFunction(string value)
    {
        Assert.Single(CssParser.ParseInlineDeclarations("--token: " + value, null));
        var computed = CssCustomProperties.Compute(null,
            new Dictionary<string, string> { ["--token"] = value });

        Assert.Equal(value, computed["--token"]);
        Assert.Empty(CssCustomProperties.References(value));
    }

    [Fact]
    public void UnclosedVariableFunctionAtEndOfInputUsesFallback()
    {
        var border = new Border();
        Css.SetStyle(border, "width:var(--missing, 4px");

        Assert.Equal(4, border.Width);

        var custom = new Border();
        Css.SetStyleSheet(custom, "Border { width: var(--size) }");
        Css.SetStyle(custom, "--size:var(--missing, 4px");

        Assert.Equal(4, custom.Width);
    }

    [Fact]
    public void BareDoubleHyphenIsReservedRatherThanAPropertyName()
    {
        var diagnostics = new List<CssParseDiagnostic>();
        var declarations = CssParser.ParseInlineDeclarations("--: red; --valid: blue", diagnostics);

        Assert.Equal("--valid", Assert.Single(declarations).PropertyName);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CssDiagnosticSeverity.Warning);
    }
}
