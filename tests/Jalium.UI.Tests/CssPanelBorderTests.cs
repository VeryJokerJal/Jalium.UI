using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssPanelBorderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("border-radius:20px")]
    public void Grid_PaintsCssSideColorsAndRetainsUsedBorderWidths(string radius)
    {
        var grid = new Grid();
        Css.SetStyle(grid,
            "display:grid; border:10px solid; border-color:red green blue yellow;" + radius);
        CssBorderRadiusTests.Layout(grid, 80, 60);
        Assert.Equal(new Thickness(10), CssBoxMetrics.BackgroundInsets(grid, 80).Border);
        var pixels = CssBorderRadiusTests.Pixels(grid, 80, 60);
        Assert.Equal((byte)255, pixels[(5 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)128, pixels[(30 * 80 + 75) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(55 * 80 + 40) * 4]);
        Assert.Equal((byte)255, pixels[(30 * 80 + 5) * 4 + 2]);
        Assert.Equal((byte)0, pixels[(30 * 80 + 40) * 4 + 3]);
        if (radius.Length > 0) Assert.Equal((byte)0, pixels[3]);
        Assert.Same(grid, grid.HitTest(new Point(5, 30))?.VisualHit);
    }

    [Fact]
    public void Grid_RemovingCssBorderRestoresItsContentInsetsAndPaint()
    {
        var grid = new Grid();
        Css.SetStyle(grid, "display:grid; border:10px solid red");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        Assert.Equal(new Thickness(10), CssBoxMetrics.ContentInsets(grid, 80));
        Css.SetStyle(grid, "display:grid");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        Assert.Equal(new Thickness(0), CssBoxMetrics.ContentInsets(grid, 80));
        var pixels = CssBorderRadiusTests.Pixels(grid, 80, 60);
        Assert.Equal((byte)0, pixels[(5 * 80 + 40) * 4 + 3]);
    }

    [Fact]
    public void Grid_PaintsCssDoubleBorderWithItsMiddleGap()
    {
        var grid = new Grid();
        Css.SetStyle(grid, "display:grid; border:9px double red; border-radius:20px");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(grid, 80, 60);
        Assert.Equal((byte)255, pixels[(1 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)0, pixels[(4 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(7 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)0, pixels[3]);
    }

    [Theory]
    [InlineData("dashed")]
    [InlineData("dotted")]
    public void Grid_PaintsPatternedBorderWithVisibleMarksAndGaps(string style)
    {
        var grid = new Grid();
        Css.SetStyle(grid, $"display:grid; border:8px {style} red");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(grid, 80, 60);
        Assert.Equal((byte)0, pixels[(30 * 80 + 40) * 4 + 3]);
        var visible = Enumerable.Range(12, 56).Count(x => pixels[(4 * 80 + x) * 4 + 3] > 200);
        Assert.InRange(visible, 8, 50);
    }

    [Fact]
    public void Grid_BackgroundContentClipUsesCssBorderAndPaddingInsets()
    {
        var grid = new Grid();
        Css.SetStyle(grid,
            "display:grid; border:10px solid blue; padding:5px; " +
            "background-color:red; background-clip:content-box");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(grid, 80, 60);
        Assert.Equal((byte)255, pixels[(5 * 80 + 40) * 4]);
        Assert.Equal((byte)0, pixels[(12 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(20 * 80 + 40) * 4 + 2]);
    }

    [Fact]
    public void Grid_ContentBoxWidthIncludesCssBorderAndPadding()
    {
        var grid = new Grid();
        Css.SetStyle(grid,
            "display:grid; width:20px; box-sizing:content-box; " +
            "border:5px solid red; padding:3px");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        Assert.Equal(36, grid.VisualBounds.Width);
    }

    [Fact]
    public void NativeStackPanel_PaintsCssBorderAndKeepsNativeChildPlacement()
    {
        var child = new Border { Width = 20, Height = 10 };
        var panel = new StackPanel();
        panel.Children.Add(child);
        Css.SetStyle(panel, "border:8px solid red");
        CssBorderRadiusTests.Layout(panel, 80, 60);
        Assert.Equal(new Thickness(8), CssBoxMetrics.BackgroundInsets(panel, 80).Border);
        Assert.Equal(0, child.VisualBounds.Y);
        var pixels = CssBorderRadiusTests.Pixels(panel, 80, 60);
        Assert.Equal((byte)255, pixels[(4 * 80 + 40) * 4 + 2]);
    }

    [Fact]
    public void Grid_TranslucentBorderCompositesOverItsBackground()
    {
        var grid = new Grid();
        Css.SetStyle(grid,
            "display:grid; background-color:blue; border:10px solid rgba(255,0,0,.5)");
        CssBorderRadiusTests.Layout(grid, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(grid, 80, 60);
        var edge = (5 * 80 + 40) * 4;
        Assert.Equal((byte)255, pixels[edge + 3]);
        Assert.InRange(pixels[edge + 2], (byte)120, (byte)136);
        Assert.InRange(pixels[edge], (byte)119, (byte)136);
        Assert.Equal((byte)255, pixels[(30 * 80 + 40) * 4]);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("flex")]
    [InlineData("block")]
    public void CssPanelLayout_ReservesBorderAndPaddingForChildren(string display)
    {
        var child = new Border { Width = 20, Height = 10, Background = Brushes.Lime };
        var panel = new Grid();
        panel.Children.Add(child);
        Css.SetStyle(panel,
            $"display:{display}; border:10px solid red; padding:5px");
        CssBorderRadiusTests.Layout(panel, 80, 60);
        Assert.Equal(new Thickness(15), CssBoxMetrics.ContentInsets(panel, 80));
        Assert.Equal(15, child.VisualBounds.X);
        Assert.Equal(15, child.VisualBounds.Y);
    }
}
