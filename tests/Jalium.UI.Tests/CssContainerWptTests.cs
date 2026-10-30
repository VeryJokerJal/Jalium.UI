using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssContainerWptTests
{
    [Fact]
    [Trait("WPT", "css/css-conditional/container-queries/container-units-small-viewport-fallback.html")]
    public void SmallViewportFallback_ResolvesAllSixUnitsBeforeAndAfterResize()
    {
        // The upstream six computed offset/margin lengths are represented by bar widths;
        // an independent Canvas adds a pixel check without HTML or script dependencies.
        var container = new StackPanel { Width = 70, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Border { Child = container };
        Css.SetStyle(container, "container-type:inline-size");
        var units = new[] { "cqw", "cqh", "cqi", "cqb", "cqmax", "cqmin" };
        foreach (var unit in units)
        {
            var bar = new Border { HorizontalAlignment = HorizontalAlignment.Left };
            container.Children.Add(bar); Css.SetStyle(bar, $"width:10{unit}; height:4px; background-color:green");
        }
        Compare(200, 40, [7, 4, 7, 4, 7, 4]);
        Compare(400, 80, [7, 8, 7, 8, 8, 7]);

        void Compare(int width, int height, int[] expected)
        {
            root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
            var reference = new Canvas();
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], ((Border)container.Children[i]).ActualWidth);
                var box = new Border { Width = expected[i], Height = 4, Background = Brushes.Green };
                Canvas.SetLeft(box, 0); Canvas.SetTop(box, i * 4); reference.Children.Add(box);
            }
            reference.Measure(new(width, height)); reference.Arrange(new(0, 0, width, height));
            Assert.Equal(Pixels(reference, width, height), Pixels(root, width, height));
        }
    }

    [Fact]
    [Trait("WPT", "css/css-conditional/container-queries/query-container-name-dynamic.html")]
    public void NameOnlyQueries_TrackClassChangesWithoutSizeContainment()
    {
        var inner = new Border { Name = "inner" };
        var innerContainer = new Border { Child = inner };
        var outer = new Border { Name = "outer", Child = innerContainer };
        var root = new Border { Child = outer };
        Css.SetClass(root, "container");
        Css.SetStyleSheet(root, """
            .container {container-name:--foo}
            #inner {--match-inner:no} #outer {--match-outer:no}
            @container --foo {#inner {--match-inner:yes}}
            @container --foo {#outer {--match-outer:yes}}
            """);
        Check("yes", "yes"); Css.SetClass(root, ""); Check("no", "no");
        Css.SetClass(innerContainer, "container"); Check("yes", "no");

        void Check(string innerExpected, string outerExpected)
        {
            root.Measure(new(200, 100)); root.UpdateLayout();
            Assert.Equal(innerExpected, CssNode.Get(inner).CssRuntimeState!.CustomProperties!["--match-inner"]);
            Assert.Equal(outerExpected, CssNode.Get(outer).CssRuntimeState!.CustomProperties!["--match-outer"]);
        }
    }

    private static byte[] Pixels(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        bitmap.Clear(Colors.White); bitmap.Render(visual);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(new(0, 0, width, height), pixels, width * 4, 0);
        return pixels;
    }
}
