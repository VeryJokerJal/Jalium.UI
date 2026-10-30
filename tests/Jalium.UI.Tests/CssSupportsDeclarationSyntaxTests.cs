using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssSupportsDeclarationSyntaxTests
{
    [Theory]
    [InlineData("--token", "url(foo bar)")]
    [InlineData("--token", "red ]")]
    public void SupportProbeRejectsValuesThatStyleDeclarationsReject(string property, string value)
    {
        Assert.Empty(CssParser.ParseInlineDeclarations($"{property}: {value}", null));
        Assert.False(Css.Supports(property, value));
        Assert.False(Css.Supports($"({property}: {value})"));
    }

    [Theory]
    [InlineData("--token", "var(--missing)")]
    [InlineData("--token", "fn(")]
    [InlineData("width", "var(--missing)")]
    [InlineData("--token", "var(foo)")]
    [InlineData("--token", "var(--)")]
    [InlineData("width", "var(foo)")]
    [InlineData("width", "calc(var(foo) + 1px)")]
    public void SupportProbeAcceptsSyntacticallyValidDeclarations(string property, string value)
    {
        Assert.Single(CssParser.ParseInlineDeclarations($"{property}: {value}", null));
        Assert.True(Css.Supports(property, value));
    }

    [Fact]
    public void SupportsRuleUsesTheSameVariableSyntaxAsTheCascade()
    {
        var border = new Border();
        Css.SetStyleSheet(border, """
            :scope { width: 4px }
            @supports (--token: var(foo)) { :scope { width: 16px } }
            @supports (--token: var(--missing)) { :scope { height: 12px } }
            """);
        border.Measure(new(100, 100));
        border.UpdateLayout();

        Assert.Equal(16, border.Width);
        Assert.Equal(12, border.Height);
    }
}
