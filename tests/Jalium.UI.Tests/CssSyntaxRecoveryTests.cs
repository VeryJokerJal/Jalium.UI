using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssSyntaxRecoveryTests
{
    [Fact]
    public void UnknownAtRuleKeepsBracketedSemicolonsInItsPrelude()
    {
        var sheet = CssStyleSheet.Parse("@unknown [x;y]; .good { width: 7px }");

        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("good", Assert.Single(rule.Selectors[0].Compounds[0].Classes!));
        Assert.DoesNotContain(sheet.Diagnostics, diagnostic => diagnostic.Severity == CssDiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("broken [x; width: 99px;]; color: red")]
    [InlineData("broken \\; width: 99px; color: red")]
    [InlineData("broken extra /* ; */ width: 99px; color: red")]
    public void MalformedInlineDeclarationDoesNotLeakNestedDeclarations(string css)
    {
        var diagnostics = new List<CssParseDiagnostic>();
        var declarations = CssParser.ParseInlineDeclarations(css, diagnostics);

        var declaration = Assert.Single(declarations);
        Assert.Equal("color", declaration.PropertyName);
        Assert.Equal("red", declaration.RawValue);
        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Severity == CssDiagnosticSeverity.Warning));
    }

    [Fact]
    public void BadStringEndsAtNewlineSoFollowingRuleCanParse()
    {
        var sheet = CssStyleSheet.Parse(".bad { --x: \"unfinished\n} .good { width: 8px }");

        Assert.Contains(sheet.Rules, rule => rule.Selectors[0].Compounds[0].Classes?.Contains("good") == true);
    }

    [Fact]
    public void ClosingBraceInsideBracketDoesNotEndStyleRule()
    {
        var sheet = CssStyleSheet.Parse(".one { --x: [}]; color: red } .two { width: 9px }");

        Assert.Contains(sheet.Rules, rule =>
            rule.Selectors[0].Compounds[0].Classes?.Contains("one") == true &&
            rule.Declarations.Any(declaration => declaration.PropertyName == "color" && declaration.RawValue == "red"));
        Assert.Contains(sheet.Rules, rule => rule.Selectors[0].Compounds[0].Classes?.Contains("two") == true);
    }

    [Fact]
    public void TrailingBackslashInStringDoesNotThrow()
    {
        var exception = Record.Exception(() => CssStyleSheet.Parse(".a { --x: /*c*/ \"tail\\"));
        Assert.Null(exception);
    }

    [Fact]
    public void EscapedNewlinesInStringsAndIdentifiersCountForDiagnostics()
    {
        var sheet = CssStyleSheet.Parse(".a { --x: \"first\\\nsecond\"; }\n.b { width:; }");
        Assert.Contains(sheet.Diagnostics, diagnostic =>
            diagnostic.Line == 3 && diagnostic.Message.Contains("empty value", StringComparison.Ordinal));

        var diagnostics = new List<CssParseDiagnostic>();
        CssParser.ParseInlineDeclarations("w\\69\ndth: 1px;\nheight:;", diagnostics);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Line == 3 && diagnostic.Message.Contains("empty value", StringComparison.Ordinal));
    }

    [Fact]
    public void HexEscapedNewlineRemainsInsideString()
    {
        var sheet = CssStyleSheet.Parse(".a { --x: \"\\61\nb\"; color: red } .b { width: 10px }");

        Assert.Contains(sheet.Rules, rule =>
            rule.Selectors[0].Compounds[0].Classes?.Contains("a") == true &&
            rule.Declarations.Any(declaration => declaration.PropertyName == "color"));
        Assert.Contains(sheet.Rules, rule => rule.Selectors[0].Compounds[0].Classes?.Contains("b") == true);
    }
}
