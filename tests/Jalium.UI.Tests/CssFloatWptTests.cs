using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssFloatWptTests
{
    [Fact]
    [Trait("WPT", "css/CSS2/floats/floats-placement-001.html")]
    public void FloatsPlacement001_FillsTheGreenReferenceSquare()
    {
        var root = Flow("width:100px; height:100px; background-color:red");
        Box(root, "float:left; width:20px");
        Box(root, "float:right; width:30px; height:50px; background-color:green");
        Box(root, "float:right; clear:right; width:100px; height:50px; background-color:green");
        Box(root, "display:inline-block; width:50px; height:50px; background-color:green");
        Box(root, "position:absolute; width:20px; height:50px; top:0; left:50px; background-color:green");
        CompareGreen(root);
    }

    [Fact]
    [Trait("WPT", "css/CSS2/floats/floats-placement-002.html")]
    public void FloatsPlacement002_PreservesEarlierInlineContentAndClearance()
    {
        var root = Flow("width:100px; height:100px; background-color:red");
        Box(root, "float:left; width:20px");
        var line = Flow("display:block", root);
        Box(line, "display:inline-block; width:100px; height:20px; background-color:green");
        Box(root, "float:right; width:20px; height:80px; background-color:green");
        Box(root, "float:right; clear:right; width:30px");
        Box(root, "display:inline-block; width:60px; height:60px; background-color:green");
        Box(root, "position:absolute; width:20px; height:80px; top:20px; right:20px; background-color:green");
        Box(root, "position:absolute; width:60px; height:20px; bottom:0; left:0; background-color:green");
        CompareGreen(root);
    }

    [Fact]
    [Trait("WPT", "css/CSS2/floats/floats-placement-005.html")]
    public void FloatsPlacement005_RespectsFloatSourceOrderAcrossBlocks()
    {
        var root = Flow("width:150px");
        Box(root, "float:left; clear:left; width:50px; height:50px; background-color:green");
        Flow("display:block; height:40px", root);
        Box(root, "float:right; clear:right; width:50px; height:50px; background-color:green");
        Box(root, "float:right; clear:right; width:50px; height:50px; background-color:green");
        Box(root, "float:left; clear:left; width:50px; height:50px; background-color:green");
        Box(root, "float:right; clear:right; width:50px; height:50px; background-color:green");
        var line = Flow("display:block; margin-top:10px", root);
        Box(line, "display:inline-block; width:50px; height:40px; background-color:cyan");
        Box(line, "display:inline-block; width:50px; height:40px; background-color:cyan");
        var reference = new Canvas();
        Paint(reference, new(0, 0, 50, 50), Colors.Green); Paint(reference, new(100, 40, 50, 150), Colors.Green);
        Paint(reference, new(0, 90, 50, 50), Colors.Green); Paint(reference, new(0, 50, 100, 40), Colors.Cyan);
        Compare(root, reference, 150, 190);
    }

    private static StackPanel Flow(string css, Panel? parent = null)
    {
        var root = new StackPanel(); parent?.Children.Add(root); Css.SetStyle(root, "display:flow-root; line-height:0; " + css); return root;
    }
    private static void Box(Panel parent, string css)
    {
        var box = new Border(); parent.Children.Add(box); Css.SetStyle(box, css);
    }
    private static void CompareGreen(Panel root)
    {
        var reference = new Canvas(); Paint(reference, new(0, 0, 100, 100), Colors.Green); Compare(root, reference, 100, 100);
    }
    private static void Paint(Canvas root, Rect rect, Color color)
    {
        var box = new Border { Width = rect.Width, Height = rect.Height, Background = new SolidColorBrush(color) };
        Canvas.SetLeft(box, rect.X); Canvas.SetTop(box, rect.Y); root.Children.Add(box);
    }
    private static void Compare(Panel source, Panel reference, int width, int height)
    {
        CssEvaluationScheduler.FlushIfPending(source.Dispatcher);
        source.Measure(new Size(width, height)); source.Arrange(new Rect(0, 0, width, height));
        reference.Measure(new Size(width, height)); reference.Arrange(new Rect(0, 0, width, height));
        Assert.Equal(Pixels(reference, width, height), Pixels(source, width, height));
    }
    private static byte[] Pixels(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(visual);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0); return pixels;
    }
}
