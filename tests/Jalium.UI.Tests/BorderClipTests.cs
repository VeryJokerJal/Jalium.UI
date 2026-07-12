using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class BorderClipTests
{
    [Fact]
    public void Border_ClipToBounds_WithRoundedCorners_UsesInnerRenderBounds()
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

        Assert.Equal(new Rect(3, 3, 114, 34), clip.Rect);
        Assert.Equal(5, clip.RadiusX);
        Assert.Equal(5, clip.RadiusY);
    }

    [Fact]
    public void Border_Child_VisualBounds_OffsetByBorderThickness()
    {
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(4),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(4, 4, 92, 92), child.VisualBounds);
    }

    [Fact]
    public void Border_Child_VisualBounds_OffsetByPaddingAndBorderThickness()
    {
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(8, 8, 84, 84), child.VisualBounds);
    }

    [Fact]
    public void Border_Child_VisualBounds_RespectsAsymmetricBorderThickness()
    {
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(left: 8, top: 0, right: 0, bottom: 4),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(8, 0, 92, 96), child.VisualBounds);
    }

    [Fact]
    public void Border_Child_VisualBounds_StaysConsistentWithFractionalBorderThickness()
    {
        // Pixel snapping is disabled, so a fractional BorderThickness like 1.5
        // passes straight through to the child's _visualBounds instead of being
        // rounded onto the physical-pixel grid. The child rect and the rect
        // OnRender paints the background/stroke into are computed from the same
        // raw BorderThickness, so they still agree — just at the fractional
        // position. The renderer handles sub-pixel placement / AA at draw time.
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(1.5),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        // BT=1.5 passes through unchanged (no rounding): inset 1.5 on every side.
        Assert.Equal(new Rect(1.5, 1.5, 97, 97), child.VisualBounds);
    }

    private sealed class TestBorder : Border
    {
        public object? InvokeGetLayoutClip() => GetLayoutClip();
    }
}
