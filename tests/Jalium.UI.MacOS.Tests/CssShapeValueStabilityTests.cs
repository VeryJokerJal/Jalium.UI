using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssShapeValueStabilityTests
{
    [Theory] [InlineData("shared")] [InlineData("nested")]
    public void StableShapeLayoutKeepsItsMeasureInputRevision(string path)
    {
        using var fixture = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words + " " + CssParagraphWrapStyleTests.Words);
        var floating = AddFloat(fixture, path);
        fixture.SetStyle("auto", 180, "display:flow-root;");
        fixture.Layout();
        var rootVersion = fixture.Root.MeasureInputVersion;
        var floatVersion = floating.MeasureInputVersion;
        var lines = fixture.Lines();
        for (var pass = 0; pass < 3; pass++)
        {
            fixture.Layout();
            Assert.Equal(rootVersion, fixture.Root.MeasureInputVersion);
            Assert.Equal(floatVersion, floating.MeasureInputVersion);
            Assert.Equal(lines, fixture.Lines());
        }
        fixture.AssertSourceAndHits();
    }

    [Theory] [InlineData("shared")] [InlineData("nested")]
    public void AChangedShapeStillInvalidatesAndUpdatesTheRealExclusion(string path)
    {
        using var fixture = new WrapStyleFixture(path, CssParagraphWrapStyleTests.Words + " " + CssParagraphWrapStyleTests.Words);
        var floating = AddFloat(fixture, path);
        fixture.SetStyle("auto", 180, "display:flow-root;");
        fixture.Layout();
        var before = fixture.Lines();
        var version = floating.MeasureInputVersion;
        Css.SetStyle(floating, "float:left;width:30px;height:48px;shape-outside:circle(0%);");
        Assert.True(floating.MeasureInputVersion > version);
        fixture.Layout();
        Assert.True(fixture.Lines()[0].Left < before[0].Left - .01);
        Css.SetStyle(floating, "float:left;width:30px;height:48px;shape-outside:circle(50%);");
        fixture.Layout();
        Assert.Equal(before, fixture.Lines());
        fixture.AssertSourceAndHits();
    }

    [Theory]
    [InlineData("shape-outside", "circle(50%)", "circle(50.0%)")]
    [InlineData("clip-path", "inset(2px round 4px)", "inset(2.0px round 4.0px)")]
    [InlineData("shape-outside", "polygon(0 0,100% 0,100% 100%)", "polygon(0px 0px,100% 0px,100% 100%)")]
    public void EquivalentCompiledBasicShapesHaveValueEqualityAndMatchingHashes(string property, string first, string second)
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root;width:200px;");
        var a = new Border(); var b = new Border();
        root.Children.Add(a); root.Children.Add(b);
        Css.SetStyle(a, "width:30px;height:48px;" + property + ":" + first + ";");
        Css.SetStyle(b, "width:30px;height:48px;" + property + ":" + second + ";");
        WrapStyleFixture.Layout(root, 200);
        var storage = property == "shape-outside" ? CssShapeOutsideProperties.ValueProperty : CssClipPathProperties.ValueProperty;
        var x = Assert.IsType<CssClipPathValue>(a.GetValue(storage));
        var y = Assert.IsType<CssClipPathValue>(b.GetValue(storage));
        Assert.NotSame(x, y);
        Assert.Equal(x, y);
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
        Assert.Single(new HashSet<CssClipPathValue> { x, y });
    }

    private static Border AddFloat(WrapStyleFixture fixture, string path)
    {
        var root = Assert.IsType<StackPanel>(fixture.Root);
        var parent = path == "nested" ? Assert.IsType<StackPanel>(Assert.IsType<Border>(Assert.Single(root.Children)).Child) : root;
        var floating = new Border();
        Css.SetStyle(floating, "float:left;width:30px;height:48px;shape-outside:circle(50%);");
        parent.Children.Insert(0, floating);
        return floating;
    }
}
