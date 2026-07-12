using Jalium.UI.Controls.Shapes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class ShapeStrokeDashArrayWpfParityTests
{
    [Fact]
    public void StrokeDashArrayUsesDoubleCollectionAndTracksSubpropertyChanges()
    {
        Assert.Equal(typeof(DoubleCollection), Shape.StrokeDashArrayProperty.PropertyType);
        Assert.Equal(
            typeof(DoubleCollection),
            typeof(Shape).GetProperty(nameof(Shape.StrokeDashArray))!.PropertyType);

        var dashes = new DoubleCollection { 2, 1 };
        var shape = new ProbeShape { StrokeDashArray = dashes };
        shape.Measure(new Size(30, 20));
        shape.Arrange(new Rect(0, 0, 30, 20));
        Assert.True(shape.IsMeasureValid);

        dashes.Add(3);

        Assert.False(shape.IsMeasureValid);
    }

    private sealed class ProbeShape : Shape;
}
