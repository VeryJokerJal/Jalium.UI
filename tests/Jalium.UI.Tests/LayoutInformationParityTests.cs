using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class LayoutInformationParityTests
{
    [Fact]
    public void GetLayoutExceptionElement_ReturnsElementWhoseMeasureFailed()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var element = new ThrowingMeasureElement();

        Assert.Throws<InvalidOperationException>(() => element.Measure(new Size(100, 100)));

        Assert.Same(element, LayoutInformation.GetLayoutExceptionElement(dispatcher));
    }

    [Fact]
    public void GetLayoutExceptionElement_ReturnsInnermostFailingElement()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var child = new ThrowingMeasureElement();
        var parent = new MeasuringParentElement(child);

        Assert.Throws<InvalidOperationException>(() => parent.Measure(new Size(100, 100)));

        Assert.Same(child, LayoutInformation.GetLayoutExceptionElement(dispatcher));
    }

    [Fact]
    public void GetLayoutExceptionElement_ReturnsElementWhoseArrangeFailed()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var element = new ThrowingArrangeElement();
        element.Measure(new Size(100, 100));

        Assert.Throws<InvalidOperationException>(() => element.Arrange(new Rect(0, 0, 100, 100)));

        Assert.Same(element, LayoutInformation.GetLayoutExceptionElement(dispatcher));
    }

    [Fact]
    public void SuccessfulLayout_ClearsPreviousExceptionElement()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var throwingElement = new ThrowingMeasureElement();
        Assert.Throws<InvalidOperationException>(() => throwingElement.Measure(new Size(100, 100)));

        var healthyElement = new HealthyElement();
        healthyElement.Measure(new Size(100, 100));

        Assert.Null(LayoutInformation.GetLayoutExceptionElement(dispatcher));
    }

    [Fact]
    public void GetLayoutExceptionElement_RejectsNullDispatcher()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LayoutInformation.GetLayoutExceptionElement((Threading.Dispatcher)null!));
    }

    private sealed class ThrowingMeasureElement : UIElement
    {
        protected override Size MeasureCore(Size availableSize)
        {
            throw new InvalidOperationException("measure failed");
        }
    }

    private sealed class HealthyElement : UIElement
    {
    }

    private sealed class ThrowingArrangeElement : UIElement
    {
        protected override void ArrangeCore(Rect finalRect)
        {
            throw new InvalidOperationException("arrange failed");
        }
    }

    private sealed class MeasuringParentElement : UIElement
    {
        private readonly UIElement _child;

        public MeasuringParentElement(UIElement child)
        {
            _child = child;
        }

        protected override Size MeasureCore(Size availableSize)
        {
            _child.Measure(availableSize);
            return _child.DesiredSize;
        }
    }
}
