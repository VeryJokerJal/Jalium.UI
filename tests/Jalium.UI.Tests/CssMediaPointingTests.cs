using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssMediaPointingTests
{
    [Theory]
    [InlineData(0, "pointer", "none", CssQueryResult.True)]
    [InlineData(0, "pointer", null, CssQueryResult.False)]
    [InlineData(0, "hover", "none", CssQueryResult.True)]
    [InlineData(0, "any-pointer", "none", CssQueryResult.True)]
    [InlineData(0, "any-hover", "none", CssQueryResult.True)]
    [InlineData(1, "pointer", "coarse", CssQueryResult.True)]
    [InlineData(1, "hover", "none", CssQueryResult.True)]
    [InlineData(1, "any-hover", null, CssQueryResult.False)]
    [InlineData(2, "pointer", "fine", CssQueryResult.True)]
    [InlineData(2, "hover", "none", CssQueryResult.True)]
    [InlineData(2, "any-hover", "none", CssQueryResult.True)]
    [InlineData(6, "pointer", "fine", CssQueryResult.True)]
    [InlineData(6, "hover", "hover", CssQueryResult.True)]
    [InlineData(7, "pointer", "coarse", CssQueryResult.True)]
    [InlineData(7, "pointer", "fine", CssQueryResult.False)]
    [InlineData(7, "hover", "none", CssQueryResult.True)]
    [InlineData(7, "hover", "hover", CssQueryResult.False)]
    [InlineData(7, "any-pointer", "coarse", CssQueryResult.True)]
    [InlineData(7, "any-pointer", "fine", CssQueryResult.True)]
    [InlineData(7, "any-hover", "hover", CssQueryResult.True)]
    [InlineData(7, "any-hover", "none", CssQueryResult.False)]
    [InlineData(15, "pointer", "coarse", CssQueryResult.False)]
    [InlineData(15, "pointer", "fine", CssQueryResult.True)]
    [InlineData(15, "hover", "hover", CssQueryResult.True)]
    [InlineData(15, "any-pointer", "coarse", CssQueryResult.True)]
    [InlineData(15, "any-pointer", "fine", CssQueryResult.True)]
    [InlineData(7, "pointer", "stylus", CssQueryResult.Unknown)]
    [InlineData(7, "unknown", "none", CssQueryResult.Unknown)]
    public void PointingFeatures_SeparatePrimaryFromAttachedDevices(
        int capabilities, string feature, string? keyword, object expected)
    {
        Assert.Equal(expected, CssMediaQuery.EvaluatePointingFeature(feature, keyword, capabilities));
    }

    [Theory]
    [InlineData("not (pointer: stylus)")]
    [InlineData("not (pointer > fine)")]
    [InlineData("not (min-pointer: fine)")]
    [InlineData("not (hover: coarse)")]
    [InlineData("not (any-hover: fine)")]
    [InlineData("not (any-pointer: hover)")]
    public void InvalidPointingSyntax_StaysUnknownUnderNegation(string query)
    {
        Assert.False(new CssCondition("media", query).Evaluate(new Border()));
    }

    [Fact]
    public void NativePointingCapabilities_SelectRulesOnTheControlTree()
    {
        var nativeResult = NativeMethods.InputGetPointingCapabilities(out var capabilities);
        if (OperatingSystem.IsWindows()) Assert.Equal(JaliumResult.Ok, nativeResult);
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .9 }
            @media (pointer: none) { .card { opacity: .2 } }
            @media (pointer: coarse) { .card { opacity: .3 } }
            @media (pointer: fine) { .card { opacity: .4 } }
            """);
        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var expected = nativeResult != JaliumResult.Ok ? .9
            : CssMediaQuery.EvaluatePointingFeature("pointer", "fine", capabilities) == CssQueryResult.True ? .4
            : CssMediaQuery.EvaluatePointingFeature("pointer", "coarse", capabilities) == CssQueryResult.True ? .3
            : .2;
        Assert.Equal(expected, child.Opacity);

        foreach (var feature in new[] { "pointer", "hover", "any-pointer", "any-hover" })
        {
            var keyword = feature.EndsWith("hover", StringComparison.Ordinal) ? "hover" : "fine";
            var expectedMatch = nativeResult == JaliumResult.Ok &&
                CssMediaQuery.EvaluatePointingFeature(feature, keyword, capabilities) == CssQueryResult.True;
            Assert.Equal(expectedMatch,
                new CssCondition("media", $"({feature}: {keyword})").Evaluate(child));
        }
    }
}
