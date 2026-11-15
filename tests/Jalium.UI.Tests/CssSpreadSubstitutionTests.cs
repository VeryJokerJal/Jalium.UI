using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssSpreadSubstitutionTests
{
    private static Border Apply(string style)
    {
        var border = new Border { Name = "hero" };
        Css.SetStyle(border, style);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        return border;
    }

    [Theory]
    [InlineData("--x:yes;--branch:style(--x:yes):10px;width:if(...var(--branch);else:20px)", 10)]
    [InlineData("--x:no;--branch:style(--x:yes):10px;width:if(...var(--branch);else:20px)", 20)]
    [InlineData("--x:yes;width:if(...env(missing-branches, style(--x:no):10px;style(--x:yes):30px);else:20px)", 30)]
    [InlineData("--branch:else:10px;width:if(...var(--branch))", 10)]
    [InlineData("--x:yes;width:if(...env(missing-branch, style(--x:yes):10px);else:20px)", 10)]
    [InlineData("--x:yes;--branch:style(--x:yes):10px;width:if(...var(--branch);else:env(missing-branch))", 10)]
    [InlineData("--x:yes;--condition:--x:yes;width:if(style(...var(--condition)):10px;else:20px)", 10)]
    [InlineData("--x:yes;width:if(...if(style(--x:yes):else:10px;else:else:20px))", 10)]
    public void EarlySubstitutionCanSupplyConditionalBranches(string style, double expected)
        => Assert.Equal(expected, Apply(style).Width);

    [Fact]
    public void EarlySubstitutionCanSupplyVariableFallbackComma()
    {
        Assert.Equal(30, Apply("--pair:--missing,30px;width:var(...var(--pair))").Width);
        Assert.True(double.IsNaN(Apply("--pair:--missing,30px;width:var(var(--pair))").Width));
    }

    [Fact]
    public void InvalidEarlySubstitutionInvalidatesTheWholeFunction()
        => Assert.True(double.IsNaN(Apply("width:if(...var(--missing);else:20px)").Width));

    [Fact]
    public void NestedSpreadInAnUnselectedConditionalStaysLazy()
        => Assert.Equal(10, Apply("--x:yes;width:if(style(--x:yes):10px;else:if(...var(--missing)))").Width);

    [Fact]
    public void EnvironmentFallbackKeepsUnselectedConditionalLazy()
    {
        Assert.Equal(10, Apply("--x:yes;width:env(missing,if(style(--x:yes):10px;else:env(missing-deep)))").Width);
        Assert.Equal(10, Apply("--x:yes;width:if(...env(missing,if(style(--x:yes):else:10px;else:env(missing-deep))))").Width);
    }

    [Fact]
    public void SpreadRequiresThreeAdjacentDotsAndAnAdjacentFunction()
    {
        const string branch = "--branch:else:10px;";
        Assert.Equal(4, Apply(branch + "width:4px;width:if(var(--branch);else:20px)").Width);
        Assert.Equal(4, Apply(branch + "width:4px;width:if(... var(--branch);else:20px)").Width);
        Assert.Equal(4, Apply(branch + "width:4px;width:if(. . .var(--branch);else:20px)").Width);
        Assert.True(double.IsNaN(Apply("--size:10px;width:...var(--size)").Width));
    }

    [Fact]
    public void AttributeCanSupplySpreadBranch()
    {
        var border = new Border { Name = "hero" };
        Css.SetAttribute(border, "data-branch", "else:10px");
        Css.SetStyle(border, "width:if(...attr(data-branch type(*)))");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);
    }
}
