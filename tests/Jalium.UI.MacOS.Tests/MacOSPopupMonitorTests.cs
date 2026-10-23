using Jalium.UI.Interop;
using Monitor = Jalium.UI.Interop.NativeMethods.NativeMonitorInfo;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupMonitorTests
{
    public static IEnumerable<object[]> Layouts()
    {
        foreach (double ownerScale in new[] { 1d, 1.5, 2d })
        foreach (string direction in new[] { "right", "left", "above", "below" })
        foreach (bool nearest in new[] { false, true }) yield return [ownerScale, direction, nearest];
    }

    [Theory, MemberData(nameof(Layouts))]
    public void MonitorSelectionAndReturnedWorkAreaShareTheOwnersCoordinateSpace(double ownerScale, string direction, bool nearest)
    {
        var primary = Screen(0, 0, 3000, 2000, 2);
        var second = direction switch
        {
            "right" => Screen(3000, 0, 1920, 1080, 1), "left" => Screen(-1920, 0, 1920, 1080, 1),
            "above" => Screen(0, -1080, 1920, 1080, 1), _ => Screen(0, 2000, 1920, 1080, 1)
        };
        Point physical = new(second.X + 100, second.Y + 100);
        if (nearest) physical = direction switch
        {
            "right" => new(second.X + second.Width + 200, 100), "left" => new(second.X - 200, 100),
            "above" => new(100, second.Y - 200), _ => new(100, second.Y + second.Height + 200)
        };
        var result = Window.GetMacOSMonitorWorkArea(new Point(physical.X / ownerScale, physical.Y / ownerScale), ownerScale, new[] { primary, second });
        AssertArea(second, ownerScale, result);
    }

    [Theory]
    [InlineData(0f)] [InlineData(1f)] [InlineData(2f)] [InlineData(4f)] [InlineData(float.NaN)]
    public void BackingDensityDoesNotRescaleTheSharedDesktopOrigin(float density)
    {
        var primary = Screen(0, 0, 3000, 2000, 2); var right = Screen(3000, 0, 1920, 1080, density);
        var result = Window.GetMacOSMonitorWorkArea(new Point(1600, 100), 2, new[] { primary, right });
        AssertArea(right, 2, result);
    }

    [Theory] [InlineData(0d)] [InlineData(-2d)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void AnInvalidOwnerScaleUsesOneCoordinateUnit(double ownerScale)
    {
        var right = Screen(3000, 0, 1920, 1080, 2);
        AssertArea(right, 1, Window.GetMacOSMonitorWorkArea(new Point(3200, 100), ownerScale, new[] { right }));
    }

    [Theory] [InlineData("width")] [InlineData("height")] [InlineData("work-width")] [InlineData("work-height")]
    public void InvalidMonitorRectanglesCannotDisplaceAValidWorkArea(string field)
    {
        var invalid = Screen(0, 0, 3000, 2000, 2);
        if (field == "width") invalid.Width = 0;
        else if (field == "height") invalid.Height = -1;
        else if (field == "work-width") invalid.WorkWidth = 0;
        else invalid.WorkHeight = 0;
        var right = Screen(3000, 0, 1920, 1080, 1);
        AssertArea(right, 2, Window.GetMacOSMonitorWorkArea(new Point(100, 100), 2, new[] { invalid, right }));
    }

    [Theory] [InlineData(1d)] [InlineData(1.5)] [InlineData(2d)]
    public void MissingMonitorsKeepTheFallbackAtTheOriginalAnchor(double ownerScale)
    {
        var point = new Point(-150, 230);
        Assert.Equal(new Rect(point, new Size(1, 1)), Window.GetMacOSMonitorWorkArea(point, ownerScale, Array.Empty<Monitor>()));
    }

    [Fact]
    public void EquallyNearMonitorsKeepTheNativeEnumerationOrder()
    {
        var left = Screen(-200, 0, 100, 100, 1); var right = Screen(100, 0, 100, 100, 2);
        AssertArea(left, 1, Window.GetMacOSMonitorWorkArea(new Point(0, 50), 1, new[] { left, right }));
        AssertArea(right, 1, Window.GetMacOSMonitorWorkArea(new Point(0, 50), 1, new[] { right, left }));
    }

    private static Monitor Screen(int x, int y, int width, int height, float scale) => new()
    { X = x, Y = y, Width = width, Height = height, WorkX = x + 10, WorkY = y + 20,
        WorkWidth = width - 20, WorkHeight = height - 40, Scale = scale };
    private static void AssertArea(Monitor screen, double scale, Rect area)
    {
        Assert.Equal(screen.WorkX / scale, area.X, 6); Assert.Equal(screen.WorkY / scale, area.Y, 6);
        Assert.Equal(screen.WorkWidth / scale, area.Width, 6); Assert.Equal(screen.WorkHeight / scale, area.Height, 6);
        Assert.Equal(screen.WorkX, area.X * scale, 6); Assert.Equal(screen.WorkY, area.Y * scale, 6);
    }
}
