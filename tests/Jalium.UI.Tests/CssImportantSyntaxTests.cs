using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssImportantSyntaxTests
{
    [Theory]
    [InlineData("red!important")]
    [InlineData("red ! ImPoRtAnT")]
    [InlineData("red !\\69mportant")]
    [InlineData("red !impor\\74 ant")]
    [InlineData("red !impor\\000074ant")]
    [InlineData("red !/**/important")]
    public void TrailingDelimiterAndIdentifierSetImportant(string value)
    {
        var declaration = Assert.Single(CssParser.ParseInlineDeclarations("color: " + value, null));

        Assert.True(declaration.Important);
        Assert.Equal("red", declaration.RawValue);
    }

    [Theory]
    [InlineData("\\!important")]
    [InlineData("\"!important\"")]
    [InlineData("fn(!important)")]
    [InlineData("[!important]")]
    public void NonterminalOrNondelimiterTextDoesNotSetImportant(string value)
    {
        var declaration = Assert.Single(CssParser.ParseInlineDeclarations("--x: " + value, null));

        Assert.False(declaration.Important);
    }

    [Theory]
    [InlineData("!im/**/portant")]
    [InlineData("!important-extra")]
    [InlineData("!important()")]
    [InlineData("!important more")]
    [InlineData("red !important\u00A0")]
    [InlineData("red !\u0131mportant")]
    public void NonpriorityTopLevelBangInvalidatesCustomProperty(string value)
    {
        Assert.False(CssParser.TryStripImportant(value, out _));
        Assert.Empty(CssParser.ParseInlineDeclarations("--x: " + value, null));
    }

    [Fact]
    public void EscapedImportantIdentifierWinsCascade()
    {
        var sheet = CssStyleSheet.Parse(".box { width: 5px !\\69mportant; width: 10px }");
        var border = new Border();
        Css.SetClass(border, "box");
        Css.GetStyleSheets(border).Add(sheet);

        border.Measure(new(100, 100));
        border.UpdateLayout();

        Assert.Equal(5, border.Width);
    }
}
