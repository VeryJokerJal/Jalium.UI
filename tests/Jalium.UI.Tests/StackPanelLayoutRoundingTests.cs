using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class StackPanelLayoutRoundingTests
{
    [Fact]
    public void ArrangeOverride_ShouldKeepSiblingOffsetsAsFloat_NotSnapped()
    {
        // Pixel snapping is disabled: stacked children keep their exact
        // fractional offsets instead of being rounded to whole pixels. A child
        // of height 31.6 places the next sibling at 31.6, not 32. This keeps
        // continuous layout animations smooth; the renderer handles sub-pixel
        // placement / AA. Adjacency (a sibling starts exactly where the previous
        // one ends) must still hold.
        var panel = new StackPanel();
        var first = new FractionalElement(31.6);
        var second = new FractionalElement(31.6);

        panel.Children.Add(first);
        panel.Children.Add(second);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        Assert.Equal(0, first.VisualBounds.Y);
        Assert.Equal(31.6, second.VisualBounds.Y);
        Assert.Equal(first.VisualBounds.Bottom, second.VisualBounds.Y);
    }

    private sealed class FractionalElement : FrameworkElement
    {
        private readonly double _height;

        public FractionalElement(double height)
        {
            _height = height;
        }

        protected override Size MeasureOverride(Size availableSize) => new(100, _height);
    }
}
