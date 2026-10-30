using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

/// <summary>Native XAML box-geometry/paint ports; original WPT identifiers and adaptation scope are recorded in docs/css-wpt-cases.md.</summary>
public class CssFlowWptTests
{
    [Fact]
    [Trait("WPT", "css/CSS2/margin-padding-clear/margin-collapse-002.xht")]
    public void MarginCollapse002_UsesTheMaximumAdjoiningMargin()
    {
        var root = Flow("font-size:20px; width:5em; height:4em");
        Item(root, "height:1em; margin-bottom:2em; background-color:green");
        Item(root, "height:1em; margin-top:1em; background-color:green");
        var reference = new Canvas();
        Paint(reference, new(0, 0, 100, 20), Colors.Green); Paint(reference, new(0, 60, 100, 20), Colors.Green);
        Compare(root, reference, 100, 80);
    }

    [Fact]
    [Trait("WPT", "css/CSS2/margin-padding-clear/margin-collapse-003.xht")]
    public void MarginCollapse003_NegativeMarginsSubtractFromPositiveMargins()
    {
        var root = Flow("width:50px");
        Item(root, "height:20px; margin-bottom:2in; background-color:blue");
        Item(root, "height:20px; margin-top:-2in; background-color:orange");
        var reference = new Canvas();
        Paint(reference, new(0, 0, 50, 20), Colors.Blue); Paint(reference, new(0, 20, 50, 20), Colors.Orange);
        Compare(root, reference, 50, 40);
    }

    [Fact]
    [Trait("WPT", "css/CSS2/margin-padding-clear/margin-auto-on-block-box.html")]
    public void AutoMarginsOnBlockBoxes_MatchAllFortySixSourceCases()
    {
        var source = new Canvas(); var wrapper = Flow("width:100px"); source.Children.Add(wrapper); Canvas.SetLeft(wrapper, 250);
        var reference = new Canvas(); var row = 0;
        void Add(double width, string margins, double x)
        {
            Item(wrapper, $"width:{width}px; height:5px; background-color:black; margin:auto; {margins}");
            Paint(reference, new(250 + x, row++ * 5, width, 5), Colors.Black);
        }
        Add(50, "", 25); Add(200, "", 0);
        foreach (var width in new[] { 50, 200 })
        {
            var step = width == 50 ? 25 : 50;
            for (var i = -5; i <= 5; i++) Add(width, $"margin-left:{i * step}px", i * step);
        }
        foreach (var width in new[] { 50, 200 })
        {
            var step = width == 50 ? 25 : 50;
            for (var i = -5; i <= 5; i++) Add(width, $"margin-right:{i * step}px", Math.Max(0, 100 - width - i * step));
        }
        Assert.Equal(46, row);
        Compare(source, reference, 800, 230);
    }

    private static StackPanel Flow(string css)
    {
        var root = new StackPanel(); Css.SetStyle(root, "display:flow-root; " + css); return root;
    }
    private static void Item(Panel owner, string css)
    {
        var child = new Border(); owner.Children.Add(child); Css.SetStyle(child, css);
    }
    private static void Paint(Canvas owner, Rect rect, Color color)
    {
        var box = new Border { Width = rect.Width, Height = rect.Height, Background = new SolidColorBrush(color) };
        Canvas.SetLeft(box, rect.X); Canvas.SetTop(box, rect.Y); owner.Children.Add(box);
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
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32); image.Clear(Colors.White); image.Render(visual);
        var pixels = new byte[width * height * 4]; image.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0); return pixels;
    }
}
