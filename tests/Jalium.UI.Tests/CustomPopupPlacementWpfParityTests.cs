using Jalium.UI.Controls.Primitives;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CustomPopupPlacementWpfParityTests
{
    [Fact]
    public void EqualityAndHashUsePointAndPrimaryAxis()
    {
        var first = new CustomPopupPlacement(new Point(3, 4), PopupPrimaryAxis.Horizontal);
        var equal = new CustomPopupPlacement(new Point(3, 4), PopupPrimaryAxis.Horizontal);
        var otherAxis = new CustomPopupPlacement(new Point(3, 4), PopupPrimaryAxis.Vertical);

        Assert.Equal(first, equal);
        Assert.True(first == equal);
        Assert.False(first != equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, otherAxis);
    }
}
