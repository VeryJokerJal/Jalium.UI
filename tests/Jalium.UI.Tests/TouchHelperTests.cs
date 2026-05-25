using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class TouchHelperTests
{
    [Fact]
    public void IsRippleEnabled_DefaultsFalse_ForGenericElement()
    {
        var border = new Border();
        Assert.False(TouchHelper.GetIsRippleEnabled(border));
    }

    [Fact]
    public void IsRippleEnabled_DefaultsTrue_ForButtonBase()
    {
        var button = new Button();
        Assert.True(TouchHelper.GetIsRippleEnabled(button));
    }

    [Fact]
    public void IsRippleEnabled_CanBeDisabledPerElement()
    {
        var button = new Button();
        TouchHelper.SetIsRippleEnabled(button, false);
        Assert.False(TouchHelper.GetIsRippleEnabled(button));
    }

    [Fact]
    public void IsTouchInteractive_DefaultsTrue()
    {
        Assert.True(TouchHelper.GetIsTouchInteractive(new Border()));
    }

    [Fact]
    public void GetDeviceType_TouchEventArgs_ReturnsTouch()
    {
        var device = Touch.RegisterTouchPoint(999, new Point(0, 0), null);
        try
        {
            var args = new TouchEventArgs(device, 0);
            Assert.Equal(PointerDeviceType.Touch, TouchHelper.GetDeviceType(args));
            Assert.True(TouchHelper.IsFromTouch(args));
            Assert.False(TouchHelper.IsFromMouse(args));
            Assert.True(TouchHelper.IsTouchLike(args));
        }
        finally
        {
            Touch.UnregisterTouchPoint(999);
        }
    }

    [Fact]
    public void GetDeviceType_PointerEventArgs_ReturnsPointerDeviceType()
    {
        var pp = new PointerPoint(1, new Point(0, 0), PointerDeviceType.Pen, true,
            new PointerPointProperties(), 0);
        var args = new PointerMoveEventArgs(pp, ModifierKeys.None, 0);
        Assert.Equal(PointerDeviceType.Pen, TouchHelper.GetDeviceType(args));
        Assert.True(TouchHelper.IsFromPen(args));
        Assert.True(TouchHelper.IsTouchLike(args));
    }

    [Fact]
    public void GetDeviceType_UnknownEvent_FallsBackToMouse()
    {
        var args = new RoutedEventArgs();
        Assert.Equal(PointerDeviceType.Mouse, TouchHelper.GetDeviceType(args));
    }
}
