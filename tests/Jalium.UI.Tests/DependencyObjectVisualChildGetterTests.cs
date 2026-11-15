using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class DependencyObjectVisualChildGetterTests
{
    [Fact]
    public void NonVisualObjectsShareTheirCompatibilityGetter()
    {
        var first = new DependencyObject();
        var second = new LineGeometry();

        Assert.Same(first.GetVisualChild, second.GetVisualChild);
        Assert.Throws<InvalidOperationException>(() => first.GetVisualChild(0));
        Assert.Throws<InvalidOperationException>(() => second.GetVisualChild(0));
    }

    [Fact]
    public void VisualGetterDispatchesToItsOwnOverride()
    {
        var first = new ParentVisual();
        var second = new ParentVisual();

        Assert.Same(first.Child, ((DependencyObject)first).GetVisualChild(0));
        Assert.Same(second.Child, ((DependencyObject)second).GetVisualChild(0));
        Assert.NotSame(((DependencyObject)first).GetVisualChild, ((DependencyObject)second).GetVisualChild);
        Assert.Throws<ArgumentOutOfRangeException>(() => ((DependencyObject)first).GetVisualChild(1));
    }

    private sealed class ParentVisual : Visual
    {
        internal Visual Child { get; } = new LeafVisual();
        protected override int VisualChildrenCount => 1;
        protected override Visual? GetVisualChild(int index)
            => index == 0 ? Child : throw new ArgumentOutOfRangeException(nameof(index));
    }

    private sealed class LeafVisual : Visual { }
}
