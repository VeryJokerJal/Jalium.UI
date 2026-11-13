using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssDisplayLayoutTests
{
    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(400, 120)); root.Arrange(new Rect(0, 0, 400, 120));
    }

    [Fact]
    public void DisplayFlex_ChangesAnExistingPanelWithoutReparentingItsChildren()
    {
        var host = new StackPanel(); var first = new Border(); var second = new Border();
        host.Children.Add(first); host.Children.Add(second);
        FlexPanel.SetBasis(first, 100);
        Css.SetStyle(second, "flex:1");
        Css.SetStyle(host, "display:flex; flex-direction:row; gap:10px");
        Layout(host);
        Assert.Equal(100, first.ActualWidth, 6);
        Assert.Equal(290, second.ActualWidth, 6);
        Assert.Equal(110, second.VisualBounds.X, 6);
        Assert.Same(host, first.VisualParent); Assert.Same(host, second.VisualParent);
        Assert.Same(first, host.Children[0]); Assert.Same(second, host.Children[1]);
    }

    [Fact]
    public void ClearingDisplayFlex_RestoresTheOriginalNativeLayout()
    {
        var host = new StackPanel(); var first = new Border { Height = 20 }; var second = new Border { Height = 20 };
        host.Children.Add(first); host.Children.Add(second);
        Css.SetStyle(host, "display:flex; flex-direction:row"); Layout(host);
        Assert.Equal(first.VisualBounds.Y, second.VisualBounds.Y, 6);
        Css.SetStyle(host, ""); Layout(host);
        Assert.Equal(20, second.VisualBounds.Y, 6);
    }

    [Fact]
    public void DisplayFlex_OnGrid_SupportsWrappingAndItemProperties()
    {
        var host = new Grid();
        var first = new Border(); var second = new Border(); var third = new Border();
        host.Children.Add(first); host.Children.Add(second); host.Children.Add(third);
        foreach (var child in new[] { first, second, third }) Css.SetStyle(child, "flex:0 0 180px; height:30px");
        Css.SetStyle(host, "display:flex; flex-wrap:wrap; align-content:flex-start; row-gap:8px; column-gap:10px");
        Layout(host);
        Assert.Equal(190, second.VisualBounds.X, 6);
        Assert.Equal(38, third.VisualBounds.Y, 6);
    }

    [Fact]
    public void DisplayFlex_PaddingOffsetsItemsAndBackgroundContentBox()
    {
        var host = new StackPanel();
        var first = new Border(); var second = new Border();
        host.Children.Add(first); host.Children.Add(second);
        FlexPanel.SetBasis(first, 100);
        Css.SetStyle(second, "flex:1");
        Css.SetStyle(host,
            "display:flex;flex-direction:row;gap:10px;padding:10px;" +
            "background-color:blue;background-clip:content-box");
        Layout(host);

        Assert.Equal(10, first.VisualBounds.X, 6);
        Assert.Equal(10, first.VisualBounds.Y, 6);
        Assert.Equal(120, second.VisualBounds.X, 6);
        Assert.Equal(270, second.ActualWidth, 6);
        Assert.Equal(100, first.ActualHeight, 6);
        var pixels = CssBorderRadiusTests.Pixels(host, 400, 120);
        Assert.Equal((byte)0, pixels[(5 * 400 + 5) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(11 * 400 + 11) * 4]);
    }

    [Theory]
    [InlineData("5px", 5, 30)]
    [InlineData("10%", 10, 20)]
    public void NativeFlexPanel_UsesCssPaddingWithoutADeclaredDisplay(
        string padding, double inset, double height)
    {
        var host = new FlexPanel();
        var child = new Border();
        host.Children.Add(child);
        FlexPanel.SetBasis(child, 20);
        Css.SetStyle(host, $"padding:{padding}");
        CssBorderRadiusTests.Layout(host, 100, 40);

        Assert.Equal(inset, child.VisualBounds.X, 6);
        Assert.Equal(inset, child.VisualBounds.Y, 6);
        Assert.Equal(20, child.ActualWidth, 6);
        Assert.Equal(height, child.ActualHeight, 6);
    }
}
