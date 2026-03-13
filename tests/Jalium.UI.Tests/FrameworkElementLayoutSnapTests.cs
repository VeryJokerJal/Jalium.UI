using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class FrameworkElementLayoutSnapTests
{
    [Fact]
    public void ArrangeCore_ShouldSnapCenteredChildOriginToWholePixels()
    {
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

        Assert.Equal(4, child.VisualBounds.X);
        Assert.Equal(3, child.VisualBounds.Y);
    }
}
