using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class CrossPlatformPointerStateTests
{
    [Fact]
    public void BuildPointerInputData_PenHover_PreservesRangeWithoutContact()
    {
        var window = new Window();
        var platformEvent = new PlatformEvent
        {
            Type = PlatformEventType.PointerMove,
            PointerId = 17,
            PointerType = 2,
            PointerX = 24,
            PointerY = 36,
            PointerFlags = 1u << 0,
            PointerToolType = 1
        };

        PointerInputData data = window.BuildPointerInputData(platformEvent);

        Assert.True(data.IsInRange);
        Assert.False(data.Point.IsInContact);
        Assert.Equal(0, data.Point.Properties.Pressure);
        Assert.False(data.Point.Properties.IsLeftButtonPressed);
        Assert.Equal(PointerUpdateKind.Other, data.Point.Properties.PointerUpdateKind);
    }

    [Fact]
    public void BuildPointerInputData_EraserContact_PreservesToolFlagsAndButtons()
    {
        var window = new Window();
        var platformEvent = new PlatformEvent
        {
            Type = PlatformEventType.PointerDown,
            PointerId = 23,
            PointerType = 2,
            Pressure = 0.75f,
            TiltX = 12,
            TiltY = -8,
            Twist = 45,
            PointerFlags = (1u << 0) | (1u << 1) | (1u << 2) |
                           (1u << 3) | (1u << 4) | (1u << 5),
            PointerToolType = 2,
            PointerButtons = (1u << 0) | (1u << 1) | (1u << 3) | (1u << 4)
        };

        PointerInputData data = window.BuildPointerInputData(platformEvent);
        PointerPointProperties properties = data.Point.Properties;

        Assert.True(data.IsInRange);
        Assert.True(data.Point.IsInContact);
        Assert.True(properties.IsPrimary);
        Assert.True(properties.IsLeftButtonPressed);
        Assert.True(properties.IsRightButtonPressed);
        Assert.True(properties.IsBarrelButtonPressed);
        Assert.True(properties.IsEraser);
        Assert.True(properties.IsInverted);
        Assert.Equal(0.75f, properties.Pressure);
        Assert.Equal(12, properties.XTilt);
        Assert.Equal(-8, properties.YTilt);
        Assert.Equal(45, properties.Twist);
        Assert.Equal(PointerUpdateKind.RightButtonPressed, properties.PointerUpdateKind);
    }

    [Fact]
    public void BuildPointerInputData_UsesPlatformEventTimeWithoutRestamping()
    {
        const long EventTimeMillis = 4_500_000_123;
        var window = new Window();
        var platformEvent = new PlatformEvent
        {
            Type = PlatformEventType.PointerMove,
            PointerId = 29,
            PointerType = 1,
            PointerFlags = (1u << 0) | (1u << 1),
            PointerTimestampMillis = EventTimeMillis
        };

        PointerInputData data = window.BuildPointerInputData(platformEvent);

        Assert.Equal((ulong)EventTimeMillis, data.Point.Timestamp);
        Assert.Equal(
            unchecked((int)EventTimeMillis),
            Window.ToPointerInputTimestamp(EventTimeMillis));
    }
}
