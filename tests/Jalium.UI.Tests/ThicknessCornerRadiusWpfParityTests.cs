using Xunit;

namespace Jalium.UI.Tests;

public sealed class ThicknessCornerRadiusWpfParityTests
{
    [Fact]
    public void Thickness_ComponentPropertiesAreMutable()
    {
        var value = new Thickness(1, 2, 3, 4)
        {
            Left = 5,
            Top = 6,
            Right = 7,
            Bottom = 8,
        };

        Assert.Equal(new Thickness(5, 6, 7, 8), value);
    }

    [Fact]
    public void CornerRadius_ComponentPropertiesAreMutable()
    {
        var value = new CornerRadius(1, 2, 3, 4)
        {
            TopLeft = 5,
            TopRight = 6,
            BottomRight = 7,
            BottomLeft = 8,
        };

        Assert.Equal(new CornerRadius(5, 6, 7, 8), value);
    }
}
