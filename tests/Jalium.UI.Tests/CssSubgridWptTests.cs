using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

/// <summary>
/// Box-geometry and paint ports of Mats Palmgren's public-domain WPT subgrid cases.
/// Decorative letter glyphs are replaced by transparent 16px line boxes; see docs/css-wpt-cases.md.
/// The reference uses native Canvas rectangles rather than the CSS formatter under test.
/// </summary>
public class CssSubgridWptTests
{
    [Fact]
    [Trait("WPT", "css/css-grid/subgrid/grid-gap-normal-001.html")]
    public void GridGapNormal001_MatchesReferenceBoxes()
    {
        var root = Scene(false);
        var reference = new Canvas();
        Paint(reference, new(0, 0, 260, 100), "#444"); Paint(reference, new(430, 0, 100, 100), "#444");
        Paint(reference, new(0, 110, 150, 210), "#444"); Paint(reference, new(160, 110, 370, 210), "#444");
        Paint(reference, new(180, 130, 80, 80), "#ccc"); Paint(reference, new(270, 130, 150, 80), "#ccc");
        Paint(reference, new(430, 130, 80, 80), "#ccc"); Paint(reference, new(180, 220, 80, 80), "#ccc");
        Paint(reference, new(270, 220, 150, 80), "#ccc");
        Compare(root, reference, 530, 320);
    }

    [Fact]
    [Trait("WPT", "css/css-grid/subgrid/grid-gap-smaller-001.html")]
    public void GridGapSmaller001_MatchesReferenceBoxes()
    {
        var root = Scene(true);
        var reference = new Canvas();
        Paint(reference, new(0, 0, 270, 56), "#444"); Paint(reference, new(460, 0, 100, 56), "#444");
        Paint(reference, new(0, 76, 150, 152), "#444"); Paint(reference, new(170, 76, 390, 152), "#444");
        Paint(reference, new(190, 96, 90, 56), "yellow"); Paint(reference, new(280, 96, 170, 56), "purple");
        Paint(reference, new(450, 96, 90, 56), "#ccc"); Paint(reference, new(190, 152, 90, 56), "#ccc");
        Paint(reference, new(280, 152, 170, 56), "blue");
        Compare(root, reference, 560, 228);
    }

    private static StackPanel Scene(bool smaller)
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:grid; grid-template-columns:150px 100px 150px 100px; grid-template-rows:" +
            (smaller ? "repeat(3,auto); gap:20px" : "repeat(3,minmax(100px,auto)); gap:10px"));
        Box(root, "grid-column:1/3", "#444"); Box(root, "grid-column:4", "#444");
        Box(root, "grid-column:1; grid-row:2/4", "#444");
        var subgrid = new StackPanel(); root.Children.Add(subgrid);
        Css.SetStyle(subgrid, "display:grid; grid:subgrid / subgrid; grid-column:2/5; grid-row:2/4; padding:20px; background-color:#444" +
            (smaller ? "; gap:0" : ""));
        Box(subgrid, "", smaller ? "yellow" : "#ccc"); Box(subgrid, "", smaller ? "purple" : "#ccc");
        Box(subgrid, "", "#ccc"); Box(subgrid, "", "#ccc"); Box(subgrid, "", smaller ? "blue" : "#ccc");
        return root;
    }

    private static void Box(Panel owner, string placement, string color)
    {
        var box = new Border { Child = new Border { Width = 16, Height = 16 } };
        owner.Children.Add(box);
        Css.SetStyle(box, $"padding:20px; background-color:{color}; {placement}");
    }

    private static void Paint(Canvas owner, Rect rect, string color)
    {
        Assert.True(CssValueParsing.TryParseColor(color, out var fill));
        var box = new Border { Width = rect.Width, Height = rect.Height, Background = new SolidColorBrush(fill) };
        Canvas.SetLeft(box, rect.X); Canvas.SetTop(box, rect.Y); owner.Children.Add(box);
    }

    private static void Compare(Panel source, Panel reference, int width, int height)
    {
        CssEvaluationScheduler.FlushIfPending(source.Dispatcher);
        source.Measure(new Size(width, double.PositiveInfinity));
        Assert.Equal(height, source.DesiredSize.Height, 6);
        source.Arrange(new Rect(0, 0, width, height));
        reference.Measure(new Size(width, height)); reference.Arrange(new Rect(0, 0, width, height));
        Assert.Equal(Pixels(reference, width, height), Pixels(source, width, height));
    }

    private static byte[] Pixels(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        bitmap.Clear(Color.FromRgb(255, 255, 255)); bitmap.Render(visual);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return pixels;
    }
}
