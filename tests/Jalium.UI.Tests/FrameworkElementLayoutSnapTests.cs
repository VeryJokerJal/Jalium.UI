using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class FrameworkElementLayoutSnapTests
{
    [Fact]
    public void ArrangeCore_ShouldCenterExplicitSize_WhenAlignmentIsStretch()
    {
        var host = new Border
        {
            Width = 100,
            Height = 80
        };
        var child = new Border
        {
            Width = 52,
            Height = 24
        };
        host.Child = child;

        host.Measure(new Size(100, 80));
        host.Arrange(new Rect(0, 0, 100, 80));

        Assert.Equal(24, child.VisualBounds.X);
        Assert.Equal(28, child.VisualBounds.Y);
        Assert.Equal(52, child.RenderSize.Width);
        Assert.Equal(24, child.RenderSize.Height);
    }

    [Fact]
    public void ArrangeCore_ShouldKeepCenteredChildOriginAsFloat_NotSnapped()
    {
        // Centered alignment may legitimately produce a fractional origin (e.g.
        // (20-15)/2 = 2.5). Rounding it to a whole pixel would turn smooth
        // animations — anything that drives a continuous Margin/position — into
        // 1px step jitter, since adjacent frames straddle the rounding boundary
        // and snap to different integer rows.
        //
        // The renderer is the right place to handle pixel alignment (sub-pixel
        // AA / device-pixel rounding at draw time), not the layout pass. This
        // also matches WPF, where layout rounding is opt-in via
        // UseLayoutRounding (default off).
        var host = new Border
        {
            Width = 44,
            Height = 20,
            BorderThickness = new Thickness(1)
        };

        var child = new Border
        {
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0)
        };

        host.Child = child;

        host.Measure(new Size(44, 20));
        host.Arrange(new Rect(0, 0, 44, 20));

        // Border content rect starts at (BorderThickness, BorderThickness) = (1, 1).
        // child.Margin.Left = 3 → x = 1 + 3 = 4 (integer here, happens to land whole).
        // Vertical: content height 18, child 15, Center → top = (18-15)/2 = 1.5;
        //   plus border inset 1 → y = 2.5. Must stay 2.5, not be rounded to 2 or 3.
        Assert.Equal(4.0, child.VisualBounds.X);
        Assert.Equal(2.5, child.VisualBounds.Y);
    }
}
