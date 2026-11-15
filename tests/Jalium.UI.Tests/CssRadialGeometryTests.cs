using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssRadialGeometryTests
{
    private static RadialGradientBrush Resolve(string css, double width = 200, double height = 100)
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:" + css);
        var source = Assert.IsType<RadialGradientBrush>(box.Background);
        Assert.NotNull(source.CssGradientLayout);
        return Assert.IsType<RadialGradientBrush>(source.CssGradientLayout.Resolve(width, height));
    }

    [Theory]
    [InlineData("radial-gradient(red, blue)", 141.4213562373, 70.7106781187)]
    [InlineData("radial-gradient(ellipse farthest-corner, red, blue)", 141.4213562373, 70.7106781187)]
    [InlineData("radial-gradient(circle, red, blue)", 111.8033988750, 111.8033988750)]
    [InlineData("radial-gradient(ellipse closest-side, red, blue)", 100, 50)]
    [InlineData("radial-gradient(circle closest-side, red, blue)", 50, 50)]
    [InlineData("radial-gradient(ellipse farthest-side at 20% 30%, red, blue)", 160, 70)]
    [InlineData("radial-gradient(ellipse closest-corner at 20% 30%, red, blue)", 56.5685424949, 42.4264068712)]
    [InlineData("radial-gradient(circle farthest-corner at 20% 30%, red, blue)", 174.6424919657, 174.6424919657)]
    [InlineData("radial-gradient(ellipse 30% 20px, red, blue)", 60, 20)]
    [InlineData("radial-gradient(circle 50%, red, blue)", 79.0569415042, 79.0569415042)]
    [InlineData("radial-gradient(circle 40px, red, blue)", 40, 40)]
    public void CircleAndEllipseExtents_UseTheActualBoxAndCenter(string css, double expectedX, double expectedY)
    {
        var brush = Resolve(css);
        Assert.Equal(expectedX, brush.RadiusX * 200, 6);
        Assert.Equal(expectedY, brush.RadiusY * 100, 6);
    }

    [Fact]
    public void ExplicitPositionAndRadius_ResolveWithTheUsedDimensions()
    {
        var brush = Resolve("radial-gradient(circle 40px at 20px 30px, red, blue)");
        Assert.Equal(.1, brush.Center.X, 8);
        Assert.Equal(.3, brush.Center.Y, 8);
        Assert.Equal(40, brush.RadiusX * 200, 8);
        Assert.Equal(40, brush.RadiusY * 100, 8);

        var fourPart = Resolve("radial-gradient(ellipse closest-side at right 10px bottom 20px, red, blue)");
        Assert.Equal(.95, fourPart.Center.X, 8);
        Assert.Equal(.8, fourPart.Center.Y, 8);
        Assert.Equal(10, fourPart.RadiusX * 200, 8);
        Assert.Equal(20, fourPart.RadiusY * 100, 8);
        Assert.Equal(0, Resolve("radial-gradient(circle at center left, red, blue)").Center.X, 8);
        Assert.Equal(.2, Resolve("radial-gradient(circle at top 20px, red, blue)").Center.Y, 8);
    }

    [Fact]
    public void FontRelativeCircularRadius_UsesEachElementsCurrentFontSize()
    {
        static double Radius(double size)
        {
            var box = new TextBlock { FontSize = size, Width = 100, Height = 100 };
            Css.SetStyle(box, "background-image:radial-gradient(circle calc(2em), blue, yellow)");
            var source = Assert.IsType<RadialGradientBrush>(box.Background);
            Assert.NotNull(source.CssGradientLayout);
            return Assert.IsType<RadialGradientBrush>(source.CssGradientLayout.Resolve(100, 100)).RadiusX * 100;
        }
        Assert.Equal(20, Radius(10), 8);
        Assert.Equal(60, Radius(30), 8);
    }

    [Fact]
    public void ResizingTheSameElement_ChangesItsDefaultEndingShape()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:radial-gradient(red, blue)");
        var source = Assert.IsType<RadialGradientBrush>(box.Background);
        Assert.NotNull(source.CssGradientLayout);
        var square = Assert.IsType<RadialGradientBrush>(source.CssGradientLayout.Resolve(100, 100));
        var wide = Assert.IsType<RadialGradientBrush>(source.CssGradientLayout.Resolve(200, 100));
        Assert.Equal(100 / Math.Sqrt(2), square.RadiusX * 100, 6);
        Assert.Equal(200 / Math.Sqrt(2), wide.RadiusX * 200, 6);
        Assert.NotSame(square, wide);
    }

    [Fact]
    public void WptContainerRelativePosition_TracksTheContainingBox()
    {
        var inner = new Border { Width = 100, Height = 100 };
        var container = new Border { Width = 100, Height = 100, Child = inner };
        Css.SetStyle(container, "container-type:size");
        Css.SetStyle(inner,
            "background-image:radial-gradient(50px 50px ellipse at 50cqw 50px, green, blue)");
        CssBorderRadiusTests.Layout(container, 100, 100);
        var first = Assert.IsType<RadialGradientBrush>(inner.Background);
        Assert.NotNull(first.CssGradientLayout);
        Assert.Equal(.5, Assert.IsType<RadialGradientBrush>(first.CssGradientLayout.Resolve(100, 100)).Center.X, 8);
        container.Width = 200;
        CssBorderRadiusTests.Layout(container, 200, 100);
        var updated = Assert.IsType<RadialGradientBrush>(inner.Background);
        Assert.NotNull(updated.CssGradientLayout);
        Assert.Equal(1, Assert.IsType<RadialGradientBrush>(updated.CssGradientLayout.Resolve(100, 100)).Center.X, 8);
    }

    [Theory]
    [InlineData("radial-gradient(circle 30px 40px, red, blue)")]
    [InlineData("radial-gradient(ellipse 30px, red, blue)")]
    [InlineData("radial-gradient(circle -10px, red, blue)")]
    [InlineData("radial-gradient(circle calc(10px - 20px), red, blue)")]
    [InlineData("radial-gradient(circle closest-side farthest-side, red, blue)")]
    [InlineData("radial-gradient(at center circle, red, blue)")]
    public void InvalidShapeAndSizeCombinations_AreRejected(string css)
        => Assert.False(Css.Supports("background-image", css));
}
