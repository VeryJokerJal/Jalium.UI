using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class PointerPointGetPositionTests
{
    [Fact]
    public void GetPosition_NullRelativeTo_ReturnsOriginalPosition()
    {
        var pp = new PointerPoint(1, new Point(7, 9), PointerDeviceType.Touch, true, new PointerPointProperties(), 0);
        Assert.Equal(new Point(7, 9), pp.GetPosition(null));
    }

    [Fact]
    public void GetPosition_NoSourceElement_ReturnsOriginalPosition()
    {
        var element = new Border();
        var pp = new PointerPoint(1, new Point(7, 9), PointerDeviceType.Touch, true, new PointerPointProperties(), 0);
        // SourceElement intentionally null.
        Assert.Equal(new Point(7, 9), pp.GetPosition(element));
    }

    [Fact]
    public void GetPosition_RelativeToSelf_IsIdentity()
    {
        var element = new Border();
        var pp = new PointerPoint(1, new Point(7, 9), PointerDeviceType.Touch, true, new PointerPointProperties(), 0)
        {
            SourceElement = element
        };
        Assert.Equal(new Point(7, 9), pp.GetPosition(element));
    }

    [Fact]
    public void GetCurrentPoint_RelativeTo_StampsSourceElement()
    {
        var src = new Border();
        var dst = new Border();
        var pp = new PointerPoint(2, new Point(1, 2), PointerDeviceType.Touch, true, new PointerPointProperties(), 0)
        {
            SourceElement = src
        };
        var args = new PointerMoveEventArgs(pp, ModifierKeys.None, 0);
        var transformed = args.GetCurrentPoint(dst);
        Assert.Same(dst, transformed.SourceElement);
        Assert.Equal(pp.PointerId, transformed.PointerId);
    }
}
