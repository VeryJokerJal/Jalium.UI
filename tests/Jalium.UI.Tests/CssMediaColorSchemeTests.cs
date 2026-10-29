using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMediaColorSchemeTests
{
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "light", true)]
    [InlineData(false, "dark", false)]
    [InlineData(true, null, true)]
    [InlineData(true, "light", false)]
    [InlineData(true, "dark", true)]
    public void ColorScheme_BooleanAndKeywordSemantics(bool dark, string? value,
        bool expected)
        => Assert.Equal(expected ? CssQueryResult.True : CssQueryResult.False,
            CssMediaQuery.EvaluateColorScheme(dark, value));

    [Fact]
    public void ColorScheme_InvalidKeywordIsUnknown()
        => Assert.Equal(CssQueryResult.Unknown,
            CssMediaQuery.EvaluateColorScheme(true, "invalid"));

    [Fact]
    public void ColorScheme_UsesSystemPreferenceAndRefreshesOnNotification()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .3 }
            @media (prefers-color-scheme: light) { .card { opacity: .4 } }
            @media (prefers-color-scheme: dark) { .card { opacity: .8 } }
            """);

        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var dark = SystemParameters.PrefersDarkColorScheme;
        Assert.Equal(dark ? .8 : .4, child.Opacity);

        SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.PrefersDarkColorScheme));
        Assert.True(CssEvaluationScheduler.HasPending(root.Dispatcher));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(SystemParameters.PrefersDarkColorScheme ? .8 : .4, child.Opacity);

        SystemParameters.NotifyStaticPropertyChanged(string.Empty);
        Assert.True(CssEvaluationScheduler.HasPending(root.Dispatcher));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.True(new CssCondition("media", "(prefers-color-scheme)").Evaluate(root));
        Assert.Equal(SystemParameters.PrefersDarkColorScheme,
            new CssCondition("media", "(prefers-color-scheme: dark)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-color-scheme: invalid)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-color-scheme > dark)").Evaluate(root));
    }
}
