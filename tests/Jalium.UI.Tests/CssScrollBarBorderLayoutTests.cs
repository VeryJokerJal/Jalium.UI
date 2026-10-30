using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssScrollBarBorderLayoutTests
{
    [Theory]
    [InlineData(Orientation.Vertical, 60, 200, 14, 12, 32, 32, 14, 44, 32, 112, 14, 156, 32, 32)]
    [InlineData(Orientation.Horizontal, 200, 60, 14, 12, 36, 36, 50, 12, 100, 36, 150, 12, 36, 36)]
    public void CssBorderAndPadding_ReserveSpaceForDesktopParts(
        Orientation orientation, double outerWidth, double outerHeight,
        double firstX, double firstY, double firstWidth, double firstHeight,
        double trackX, double trackY, double trackWidth, double trackHeight,
        double lastX, double lastY, double lastWidth, double lastHeight)
    {
        var bar = new ScrollBar { Orientation = orientation, IsOverlayStyle = false };
        Css.SetStyle(bar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(bar, outerWidth, outerHeight);

        AssertPart(bar, 0, firstX, firstY, firstWidth, firstHeight);
        AssertPart(bar, 1, trackX, trackY, trackWidth, trackHeight);
        AssertPart(bar, 2, lastX, lastY, lastWidth, lastHeight);
    }

    [Theory]
    [InlineData(Orientation.Vertical, 60, 200, 14, 15, 32, 170)]
    [InlineData(Orientation.Horizontal, 200, 60, 17, 12, 166, 36)]
    public void CssBorderAndPadding_ReserveSpaceForHostedOverlayTrack(
        Orientation orientation, double outerWidth, double outerHeight,
        double trackX, double trackY, double trackWidth, double trackHeight)
    {
        var bar = new ScrollBar
        {
            Orientation = orientation,
            IsOverlayStyle = true,
            IsHostOwnedLayout = true,
        };
        Css.SetStyle(bar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(bar, outerWidth, outerHeight);

        AssertPart(bar, 1, trackX, trackY, trackWidth, trackHeight);
        Assert.Equal(default, Assert.IsAssignableFrom<UIElement>(bar.GetVisualChild(0)).RenderSize);
        Assert.Equal(default, Assert.IsAssignableFrom<UIElement>(bar.GetVisualChild(2)).RenderSize);
    }

    [Fact]
    public void CssRemovalAndNativeBorderThickness_ReflowTrack()
    {
        var bar = new ScrollBar { Orientation = Orientation.Vertical, IsOverlayStyle = false };
        Css.SetStyle(bar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(bar, 60, 200);

        Css.SetStyle(bar, "padding:2px");
        CssBorderRadiusTests.Layout(bar, 60, 200);
        AssertPart(bar, 1, 2, 58, 56, 84);

        Css.SetStyle(bar, "");
        CssBorderRadiusTests.Layout(bar, 60, 200);
        AssertPart(bar, 1, 0, 60, 60, 80);

        bar.Padding = new Thickness(2);
        CssBorderRadiusTests.Layout(bar, 60, 200);
        AssertPart(bar, 1, 2, 58, 56, 84);

        bar.ClearValue(ScrollBar.PaddingProperty);
        CssBorderRadiusTests.Layout(bar, 60, 200);
        AssertPart(bar, 1, 0, 60, 60, 80);

        bar.BorderThickness = new Thickness(2, 3, 4, 5);
        Css.SetStyle(bar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(bar, 60, 200);
        AssertPart(bar, 1, 8, 49, 42, 100);
    }

    [Theory]
    [InlineData(Orientation.Vertical, 60, 200, 43, 20, 57, 20)]
    [InlineData(Orientation.Horizontal, 200, 60, 20, 45, 20, 57)]
    public void HostedOverlayIndicator_HitRegionFollowsInsetTrack(
        Orientation orientation, double outerWidth, double outerHeight,
        double indicatorX, double indicatorY, double borderX, double borderY)
    {
        var bar = new ScrollBar
        {
            Orientation = orientation,
            IsOverlayStyle = true,
            IsHostOwnedLayout = true,
        };
        Css.SetStyle(bar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(bar, outerWidth, outerHeight);

        Assert.True(HasThumbAncestor(bar.HitTest(new Point(indicatorX, indicatorY))?.VisualHit as Visual));
        Assert.Null(bar.HitTest(new Point(borderX, borderY)));
    }

    [Theory]
    [InlineData(Orientation.Vertical, 44, 96)]
    [InlineData(Orientation.Horizontal, 100, 40)]
    public void AutoSizedBar_IncludesAuthoredBoxEdges(
        Orientation orientation, double expectedWidth, double expectedHeight)
    {
        var bar = new ScrollBar { Orientation = orientation, IsOverlayStyle = false };
        Css.SetStyle(bar, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(bar, 60, 200);

        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(new Size(expectedWidth, expectedHeight), bar.DesiredSize);
    }

    private static bool HasThumbAncestor(Visual? visual)
    {
        for (var current = visual; current != null; current = current.VisualParent)
        {
            if (current is Thumb) return true;
        }

        return false;
    }

    private static void AssertPart(ScrollBar bar, int index,
        double x, double y, double width, double height)
    {
        var part = Assert.IsAssignableFrom<UIElement>(bar.GetVisualChild(index));
        Assert.Equal(new Vector(x, y), VisualTreeHelper.GetOffset(part));
        Assert.Equal(new Size(width, height), part.RenderSize);
    }
}
