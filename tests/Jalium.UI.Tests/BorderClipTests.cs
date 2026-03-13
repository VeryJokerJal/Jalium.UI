using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class BorderClipTests
{
    [Fact]
    public void Border_ClipToBounds_WithRoundedCorners_UsesFullRenderBounds()
    {
        var border = new TestBorder
        {
            ClipToBounds = true,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(8)
        };

        border.Measure(new Size(120, 40));
        border.Arrange(new Rect(0, 0, 120, 40));

        var clip = Assert.IsType<RectangleGeometry>(border.InvokeGetLayoutClip());

        Assert.Equal(new Rect(0, 0, 120, 40), clip.Rect);
        Assert.Equal(8, clip.RadiusX);
        Assert.Equal(8, clip.RadiusY);
    }

    private sealed class TestBorder : Border
    {
        public object? InvokeGetLayoutClip() => GetLayoutClip();
    }
}
