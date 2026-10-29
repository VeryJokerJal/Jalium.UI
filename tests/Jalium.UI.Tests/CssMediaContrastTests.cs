using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMediaContrastTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "no-preference", true)]
    [InlineData(false, "more", false)]
    [InlineData(false, "less", false)]
    [InlineData(false, "custom", false)]
    [InlineData(true, null, true)]
    [InlineData(true, "no-preference", false)]
    [InlineData(true, "more", true)]
    [InlineData(true, "less", false)]
    [InlineData(true, "custom", false)]
    public void Contrast_BooleanAndKeywordSemantics(bool prefersMore, string? value, bool expected)
        => Assert.Equal(expected ? CssQueryResult.True : CssQueryResult.False,
            CssMediaQuery.EvaluateContrast(prefersMore, value));

    [Fact]
    public void Contrast_InvalidKeywordIsUnknown()
        => Assert.Equal(CssQueryResult.Unknown,
            CssMediaQuery.EvaluateContrast(true, "invalid"));

    [Fact]
    public void Contrast_UsesSystemSettingAndRefreshesOnNotification()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .3 }
            @media (prefers-contrast: no-preference) { .card { opacity: .4 } }
            @media (prefers-contrast: more) { .card { opacity: .8 } }
            """);

        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var more = SystemParameters.HighContrast;
        Assert.Equal(more ? .8 : .4, child.Opacity);

        SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.HighContrast));
        Assert.True(CssEvaluationScheduler.HasPending(root.Dispatcher));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(SystemParameters.HighContrast ? .8 : .4, child.Opacity);

        Assert.Equal(SystemParameters.HighContrast,
            new CssCondition("media", "(prefers-contrast)").Evaluate(root));
        Assert.False(new CssCondition("media", "(prefers-contrast: less)").Evaluate(root));
        Assert.True(new CssCondition("media", "not (prefers-contrast: less)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-contrast: invalid)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-contrast > more)").Evaluate(root));
    }
}
