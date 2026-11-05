using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using TabDock = Jalium.UI.Controls.Dock;

namespace Jalium.UI.Tests;

public class CssTabControlBorderLayoutTests
{
    [Theory]
    [InlineData(TabDock.Top, 14, 12, 172, 36, 14, 48, 172, 80)]
    [InlineData(TabDock.Bottom, 14, 92, 172, 36, 14, 12, 172, 80)]
    [InlineData(TabDock.Left, 14, 12, 120, 116, 134, 12, 52, 116)]
    [InlineData(TabDock.Right, 66, 12, 120, 116, 14, 12, 52, 116)]
    public void CssBorderAndPadding_ReserveSpaceForStripAndSelectedContent(
        TabDock placement,
        double stripX, double stripY, double stripWidth, double stripHeight,
        double contentX, double contentY, double contentWidth, double contentHeight)
    {
        var tabs = CreateTabs(placement);
        CssBorderRadiusTests.Layout(tabs, 200, 140);

        var strip = Assert.IsAssignableFrom<UIElement>(tabs.GetVisualChild(0));
        var content = Assert.IsType<ContentPresenter>(tabs.GetVisualChild(1));
        Assert.Equal(new Vector(stripX, stripY), VisualTreeHelper.GetOffset(strip));
        Assert.Equal(new Size(stripWidth, stripHeight), strip.RenderSize);
        Assert.Equal(new Vector(contentX, contentY), VisualTreeHelper.GetOffset(content));
        Assert.Equal(new Size(contentWidth, contentHeight), content.RenderSize);
    }

    [Fact]
    public void TabStripBackground_StaysInsideCssPaddingEdge()
    {
        var tabs = CreateTabs(TabDock.Top);
        CssBorderRadiusTests.Layout(tabs, 200, 140);
        var pixels = CssBorderRadiusTests.Pixels(tabs, 200, 140);

        Assert.Equal((byte)255, pixels[(4 * 200 + 100) * 4 + 2]);
        Assert.Equal((byte)0, pixels[(10 * 200 + 100) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(20 * 200 + 100) * 4 + 1]);
    }

    [Fact]
    public void CssRemovalAndNativeBorderThickness_ReflowTabChildren()
    {
        var tabs = CreateTabs(TabDock.Top);
        CssBorderRadiusTests.Layout(tabs, 200, 140);
        var content = Assert.IsType<ContentPresenter>(tabs.GetVisualChild(1));
        Assert.Equal(new Vector(14, 48), VisualTreeHelper.GetOffset(content));

        Css.SetStyle(tabs, "padding:2px");
        CssBorderRadiusTests.Layout(tabs, 200, 140);
        Assert.Equal(new Vector(2, 38), VisualTreeHelper.GetOffset(content));
        Assert.Equal(new Size(196, 100), content.RenderSize);

        tabs.BorderThickness = new Thickness(2, 3, 4, 5);
        Css.SetStyle(tabs, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(tabs, 200, 140);
        Assert.Equal(new Vector(8, 43), VisualTreeHelper.GetOffset(content));
        Assert.Equal(new Size(182, 88), content.RenderSize);
    }

    private static TabControl CreateTabs(TabDock placement)
    {
        var tabs = new TabControl
        {
            Background = null,
            TabStripBackground = Brushes.Lime,
            TabStripPlacement = placement,
        };
        tabs.Items.Add(new TabItem
        {
            Header = "A",
            Content = new Border(),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        });
        Css.SetStyle(tabs, "border:8px solid red; padding:4px 6px");
        return tabs;
    }
}
