using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTabletInputTests
{
    [Fact]
    public void NativeSnapshotRetainsTheExistingPointerLayout()
    {
        Assert.Equal(96, Marshal.SizeOf<MacOSTabletSnapshot>());
        Assert.Equal(8, Marshal.OffsetOf<MacOSTabletSnapshot>(nameof(MacOSTabletSnapshot.Type)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<MacOSTabletSnapshot>(nameof(MacOSTabletSnapshot.PointerId)).ToInt32());
        Assert.Equal(72, Marshal.OffsetOf<MacOSTabletSnapshot>(nameof(MacOSTabletSnapshot.TimestampMillis)).ToInt32());
        Assert.Equal(80, Marshal.OffsetOf<MacOSTabletSnapshot>(nameof(MacOSTabletSnapshot.MouseType)).ToInt32());
    }

    [Fact]
    public void NativeMouseMappingStaysSeparateFromPhysicalPenButtons()
    {
        var snapshot = new MacOSTabletSnapshot
        {
            Size = 96, Type = 52, PointerType = 2, PointerId = 7, X = 40, Y = 60,
            Pressure = 0, Flags = 1 | 32, Buttons = 2 | 4 | 8,
            MouseType = 31, MouseButton = 1, MouseClickCount = 2, MouseButtonStates = 0x80000002,
            TimestampMillis = 12345, Modifiers = 1 | 8
        };
        var pointer = snapshot.ToPointerEvent();
        var mouse = snapshot.ToMouseEvent();
        Assert.Equal(PlatformEventType.PointerMove, pointer.Type);
        Assert.Equal(14u, pointer.PointerButtons);
        Assert.Equal(0f, pointer.Pressure);
        Assert.Equal(12345, pointer.PointerTimestampMillis);
        Assert.Equal(PlatformEventType.MouseDown, mouse.Type);
        Assert.Equal(1, mouse.Button);
        Assert.Equal(2, mouse.ClickCount);
        Assert.True(mouse.HasMouseButtonStates);
        Assert.Equal(2u, mouse.MouseButtons);
        Assert.Equal(pointer.Modifiers, mouse.Modifiers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TipReleasePreservesRangeUntilThePhysicalProximityExit(bool inverted)
    {
        var target = new Border();
        using var rts = new RealTimeStylus(target) { UseRealTimeThread = false };
        var points = new StylusPointCollection([new StylusPoint(4, 6, .6f)]);
        RealTimeStylusProcessResult Send(StylusInputAction action, bool inAir, bool inRange) =>
            rts.ProcessTablet(7, target, action, points, 123, inAir, inRange, false, inverted, inverted, false);
        Assert.True(Send(StylusInputAction.InAirMove, true, true).EnteredRange);
        Assert.False(Send(StylusInputAction.Down, false, true).EnteredRange);
        var up = Send(StylusInputAction.Up, true, true);
        Assert.False(up.SessionEnded);
        Assert.False(up.ExitedRange);
        Assert.False(Send(StylusInputAction.Down, false, true).EnteredRange);
        _ = Send(StylusInputAction.Up, true, true);
        var exit = Send(StylusInputAction.InAirMove, true, false);
        Assert.True(exit.ExitedRange);
        Assert.True(exit.SessionEnded);
        Assert.True(Send(StylusInputAction.InAirMove, true, true).EnteredRange);
    }

    [Fact]
    public void ExistingPublicProcessingStillEndsItsSessionAtUp()
    {
        var target = new Border();
        using var rts = new RealTimeStylus(target) { UseRealTimeThread = false };
        var points = new StylusPointCollection([new StylusPoint(1, 2, .5f)]);
        _ = rts.Process(8, target, StylusInputAction.Down, points, 10, false, true, false, false, false, false);
        Assert.True(rts.Process(8, target, StylusInputAction.Up, points, 20, true, true, false, false, false, false).SessionEnded);
    }

    [Fact]
    public void CancelledTabletSessionCannotDonateRangeToItsReplacement()
    {
        var target = new Border();
        using var rts = new RealTimeStylus(target) { UseRealTimeThread = false };
        var points = new StylusPointCollection([new StylusPoint(1, 2, .5f)]);
        _ = rts.ProcessTablet(9, target, StylusInputAction.Down, points, 10, false, true, false, false, false, false);
        var cancelled = rts.ProcessTablet(9, target, StylusInputAction.Move, points, 20, false, true, false, false, false, true);
        Assert.True(cancelled.Canceled);
        Assert.True(cancelled.SessionEnded);
        Assert.True(rts.ProcessTablet(9, target, StylusInputAction.Down, points, 30, false, true, false, false, false, false).EnteredRange);
    }
}
