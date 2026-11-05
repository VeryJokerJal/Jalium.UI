using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssMediaCapabilityTests
{
    [Theory]
    [InlineData("(color)", true)]
    [InlineData("(color: 8)", true)]
    [InlineData("(color: calc(4 + 4))", true)]
    [InlineData("(min-color: 9)", false)]
    [InlineData("(color >= 8)", true)]
    [InlineData("(color: 7)", false)]
    [InlineData("(color > -1)", true)]
    [InlineData("(color < 0)", false)]
    [InlineData("not (color: 8px)", false)]
    [InlineData("(monochrome)", false)]
    [InlineData("(monochrome: 0)", true)]
    [InlineData("(color-index)", false)]
    [InlineData("(color-index: 0)", true)]
    [InlineData("(color-gamut)", true)]
    [InlineData("(color-gamut: srgb)", true)]
    [InlineData("(color-gamut: p3)", false)]
    [InlineData("(color-gamut: rec2020)", false)]
    [InlineData("(scripting)", false)]
    [InlineData("(scripting: none)", true)]
    [InlineData("(scripting: enabled)", false)]
    [InlineData("not (scripting: enabled)", true)]
    [InlineData("not (scripting: unsupported)", false)]
    [InlineData("not (color-gamut > srgb)", false)]
    [InlineData("(dynamic-range)", true)]
    [InlineData("(dynamic-range: standard)", true)]
    [InlineData("(dynamic-range: high)", false)]
    [InlineData("not (dynamic-range: high)", true)]
    [InlineData("not (dynamic-range: unknown)", false)]
    [InlineData("not (min-dynamic-range: standard)", false)]
    [InlineData("(environment-blending)", true)]
    [InlineData("(environment-blending: opaque)", true)]
    [InlineData("(environment-blending: additive)", false)]
    [InlineData("(environment-blending: subtractive)", false)]
    [InlineData("not (environment-blending: unknown)", false)]
    [InlineData("not (environment-blending > opaque)", false)]
    [InlineData("(grid)", false)]
    [InlineData("(grid: 0)", true)]
    [InlineData("(grid: -0)", true)]
    [InlineData("(grid: 1)", false)]
    [InlineData("not (grid)", true)]
    [InlineData("not (grid: 2)", false)]
    [InlineData("not (grid: 0.0)", false)]
    [InlineData("not (min-grid: 0)", false)]
    [InlineData("(video-color-gamut)", true)]
    [InlineData("(video-color-gamut: srgb)", true)]
    [InlineData("(video-color-gamut: p3)", false)]
    [InlineData("(video-color-gamut: rec2020)", false)]
    [InlineData("not (video-color-gamut: unknown)", false)]
    [InlineData("not (min-video-color-gamut: srgb)", false)]
    [InlineData("(video-dynamic-range)", true)]
    [InlineData("(video-dynamic-range: standard)", true)]
    [InlineData("(video-dynamic-range: high)", false)]
    [InlineData("not (video-dynamic-range: high)", true)]
    [InlineData("not (video-dynamic-range: unknown)", false)]
    [InlineData("not (video-dynamic-range > standard)", false)]
    [InlineData("screen and (color-gamut: srgb) and (scripting: none)", true)]
    public void MediaFeatures_ReflectNativeOutputAndDocumentCapabilities(string query, bool expected)
    {
        var target = new Border { Width = 100, Height = 50 };
        Assert.Equal(expected, new CssCondition("media", query).Evaluate(target));
    }

    [Fact]
    public void MediaCapabilities_SelectRulesOnTheNativeControlTree()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .9 }
            @media (color-gamut: srgb) and (scripting: none) { .card { opacity: .4 } }
            @media (color-gamut: p3) { .card { opacity: .2 } }
            """);

        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Equal(.4, child.Opacity);
    }

    [Fact]
    public void DisplayCapabilities_SelectNativeControlStyles()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .9 }
            @media (dynamic-range: standard) and (environment-blending: opaque) and (grid: 0) {
                .card { opacity: .4 }
            }
            @media (dynamic-range: high), (environment-blending: additive), (grid: 1) {
                .card { opacity: .2 }
            }
            """);

        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Equal(.4, child.Opacity);
    }

    [Fact]
    public void VideoCapabilities_SelectStylesOnTheCompositedOutput()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "video-card");
        Css.SetStyleSheet(root, """
            .video-card { opacity: .9 }
            @media (video-color-gamut: srgb) and (video-dynamic-range: standard) {
                .video-card { opacity: .4 }
            }
            @media (video-color-gamut: p3), (video-dynamic-range: high) {
                .video-card { opacity: .2 }
            }
            """);

        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Equal(.4, child.Opacity);
    }
}
