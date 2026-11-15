using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssIfSubstitutionTests
{
    private static Border Apply(string style, string? sheet = null)
    {
        var border = new Border { Name = "hero" };
        if (sheet is not null) Css.SetStyleSheet(border, sheet);
        Css.SetStyle(border, style);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        return border;
    }

    [Theory]
    [Trait("WPT", "css/css-values/if-conditionals.html")]
    [InlineData("--x:3;width:if(style(--x:3):10px;else:20px)", 10)]
    [InlineData("--x:0;width:if(style(--x:3):10px;else:20px)", 20)]
    [InlineData("--x:3;width:if(style(--x):10px;else:20px)", 10)]
    [InlineData("width:if(style(--missing):10px;else:20px)", 20)]
    [InlineData("width:if(style(--missing:initial):10px;else:20px)", 10)]
    [InlineData("--x:3;width:if(style(--x:1):10px;style(--x:3):30px;else:20px)", 30)]
    [InlineData("--x:3;width:if(not style(--x:0):10px;else:20px)", 10)]
    [InlineData("--x:3;--y:red;width:if(style(--x:0) or style(--y:red):10px;else:20px)", 10)]
    [InlineData("--x:3;width:if(style(--x:3):10px;else:var(--missing))", 10)]
    [InlineData("--x:0;width:if(style(--x:3):var(--missing);else:20px)", 20)]
    [InlineData("--x:3;width:if(style(--x:3):if(supports(width:10px):10px;else:15px);else:20px)", 10)]
    [InlineData("width:if(supports(width:10px):10px;else:20px)", 10)]
    [InlineData("width:if(supports(unknown-property:10px):10px;else:20px)", 20)]
    [InlineData("width:if(not supports():10px;else:20px)", 20)]
    [InlineData("width:if(media(screen):10px;else:20px)", 10)]
    [InlineData("width:if(media(print):10px;else:20px)", 20)]
    [InlineData("width:if(not media():10px;else:20px)", 20)]
    [InlineData("width:if(media(height <= 999999px):10px;else:20px)", 10)]
    [InlineData("width:if(media(min-color:1):10px;else:20px)", 10)]
    [InlineData("--condition:style(--x:3);--x:3;width:if(var(--condition):10px;else:20px)", 10)]
    public void ConditionalSubstitutionSelectsFirstMatchingBranch(string style, double expected)
        => Assert.Equal(expected, Apply(style).Width);

    [Fact]
    [Trait("WPT", "css/css-variables/variable-reference-name-substitution.html")]
    public void ConditionalResultCanProvideVariableName()
        => Assert.Equal(10, Apply("--x:yes;width:var(if(style(--x:yes):--other;else:--missing),20px)",
            "#hero {--other:10px}").Width);

    [Fact]
    public void SelfStyleConditionIsFalseWithoutInvalidatingElseBranch()
        => Assert.Equal(10, Apply("--self:if(style(--self:10px):20px;else:10px);width:var(--self)").Width);

    [Theory]
    [Trait("WPT", "css/css-values/if-cycle.html")]
    [InlineData("--x:yes;--a:if(style(--x:yes):10px;else:var(--a));width:var(--a,99px)", 10)]
    [InlineData("--x:yes;--a:if(style(--x:no):10px;else:var(--a,20px));width:var(--a,99px)", 99)]
    public void OnlySelectedBranchCanMakeCustomPropertyCyclic(string style, double expected)
        => Assert.Equal(expected, Apply(style).Width);

    [Fact]
    public void RegisteredPropertyCanContainConditionalValue()
        => Assert.Equal(10, Apply("--x:yes;--length:if(style(--x:yes):10px;else:20px);width:var(--length)",
            "@property --length {syntax:'<length>';inherits:false;initial-value:3px}").Width);

    [Theory]
    [Trait("WPT", "css/css-values/if-conditionals.html")]
    [InlineData("--length:3px;width:if(style(--length:3px):10px;else:20px)", 10)]
    [InlineData("--length:calc(1px + 2px);width:if(style(--length:3px):10px;else:20px)", 10)]
    [InlineData("--length:3px;width:if(style(--length:calc(1px + 2px)):10px;else:20px)", 10)]
    [InlineData("--length:3px;width:if(style(--length:3):10px;else:20px)", 20)]
    [InlineData("width:if(style(--length:initial):10px;else:20px)", 10)]
    public void StyleConditionComparesRegisteredComputedValue(string style, double expected)
        => Assert.Equal(expected, Apply(style,
            "@property --length {syntax:'<length>';inherits:false;initial-value:3px}").Width);

    [Fact]
    [Trait("WPT", "css/css-values/if-conditionals.html")]
    public void AttributeCanSupplyStyleConditionExpectedValue()
    {
        var border = new Border { Name = "hero" };
        Css.SetAttribute(border, "data-size", "30px");
        Css.SetStyle(border, "--x:30px;width:if(style(--x:attr(data-size type(<length>))):10px;else:20px)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-values/attr-security-if.html")]
    public void AttributeTaintedConditionsCannotChooseUrlContent()
    {
        var border = new Border { Name = "hero" };
        Css.SetAttribute(border, "data-size", "3");
        Css.SetStyle(border, "--x:3");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        var node = CssNode.Get(border);
        string? Resolve(string name) => node.CssRuntimeState?.CustomProperties?.GetValueOrDefault(name);

        Assert.False(CssCustomProperties.Substitute(
            "url(if(style(--x:attr(data-size type(*))):asset.png;else:other.png))",
            Resolve, out _, element: node));
        Assert.False(CssCustomProperties.Substitute(
            "if(style(--x:attr(data-size type(*))):url(asset.png);else:10px)",
            Resolve, out _, element: node));
        Assert.True(CssCustomProperties.Substitute(
            "if(style(--x:3):url(asset.png);style(--x:attr(data-size type(*))):url(other.png))",
            Resolve, out _, element: node));
    }

    [Theory]
    [Trait("WPT", "css/css-values/if-function-revert-rule.html")]
    [InlineData("--x:3;background:if(style(--x:1000):red;else:revert-rule)")]
    [InlineData("--x:1000;background:if(style(--x:1000):revert-rule;else:red)")]
    public void ConditionalRevertRuleSelectsPreviousRule(string style)
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, "#hero {background:green} #hero {background:red;" + style + "}");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(border.Background).Color);
    }

    [Fact]
    public void ConditionalBranchTracksChangedCustomProperty()
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, "#hero {--x:yes;width:if(style(--x:yes):10px;else:20px)}");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);

        Css.SetStyle(border, "--x:no");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(20, border.Width);
    }

    [Fact]
    public void IfFunctionRemainsValidAtParseTime()
    {
        Assert.Single(CssParser.ParseInlineDeclarations("width:if(style(--x:yes):10px;else:20px)", null));
        Assert.True(Css.Supports("width", "if(style(--x:yes):10px;else:20px)"));
        Assert.Single(CssParser.ParseInlineDeclarations("width:if(else:)", null));
    }

    [Theory]
    [InlineData("if()")]
    [InlineData("if(style(--x) 10px)")]
    [InlineData("if(:10px)")]
    [InlineData("if(else:10px;;style(--x):20px)")]
    [InlineData("if(else:10px!)")]
    [InlineData("if(!style(--x):10px)")]
    public void MalformedConditionalBranchesAreRejectedAtParseTime(string value)
    {
        Assert.Empty(CssParser.ParseInlineDeclarations("width:" + value, null));
        Assert.False(Css.Supports("width", value));
    }
}
