using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class StackPanelLayoutRoundingTests
{
    [Fact]
    public void ArrangeOverride_ShouldSnapSiblingOffsetsToWholePixels()
    {
        var panel = new StackPanel();
        var first = new FractionalElement(31.6);
        var second = new FractionalElement(31.6);

        panel.Children.Add(first);
        panel.Children.Add(second);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        Assert.Equal(0, first.VisualBounds.Y);
        Assert.Equal(32, second.VisualBounds.Y);
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
