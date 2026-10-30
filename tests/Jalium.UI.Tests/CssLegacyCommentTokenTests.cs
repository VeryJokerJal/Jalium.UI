using System.Text;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssLegacyCommentTokenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopLevelLegacyCommentTokensDoNotConsumeStyleRules(bool fromStream)
    {
        const string css = "<!-- .first { width: 1px } --> .second { width: 2px }";
        var sheet = fromStream
            ? CssStyleSheet.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(css)))
            : CssStyleSheet.Parse(css);

        Assert.Equal(["first", "second"], sheet.Rules
            .Select(rule => Assert.Single(rule.Selectors[0].Compounds[0].Classes!)));
        Assert.DoesNotContain(sheet.Diagnostics, diagnostic => diagnostic.Severity == CssDiagnosticSeverity.Warning);
    }

    [Fact]
    public void TopLevelLegacyCommentTokensPreserveImportPlacement()
    {
        var sheet = CssStyleSheet.Parse("<!-- @import \"base.css\"; --> .view { width: 3px }");

        Assert.Single(sheet.Imports);
        Assert.Single(sheet.Rules);
    }

    [Fact]
    public void LegacyCommentTokenInsideMediaBlockIsPartOfInvalidSelector()
    {
        var sheet = CssStyleSheet.Parse(
            "@media all { <!-- .hidden { width: 1px } .visible { width: 2px } }");

        var rule = Assert.Single(sheet.Rules);
        Assert.Equal("visible", Assert.Single(rule.Selectors[0].Compounds[0].Classes!));
    }
}
