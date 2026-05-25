using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class TouchesOverChainTests
{
    [Fact]
    public void AddOverTouchInternal_PopulatesTouchesOver()
    {
        var element = new Border();
        var device = Touch.RegisterTouchPoint(401, new Point(0, 0), element);
        try
        {
            Assert.False(element.AreAnyTouchesOver);
            element.AddOverTouchInternal(device);
            element.AddDirectlyOverTouchInternal(device);
            Assert.True(element.AreAnyTouchesOver);
            Assert.True(element.AreAnyTouchesDirectlyOver);
            Assert.Contains(device, element.TouchesOver);
            Assert.Contains(device, element.TouchesDirectlyOver);

            element.RemoveOverTouchInternal(device);
            element.RemoveDirectlyOverTouchInternal(device);
            Assert.False(element.AreAnyTouchesOver);
            Assert.False(element.AreAnyTouchesDirectlyOver);
        }
        finally
        {
            Touch.UnregisterTouchPoint(401);
        }
    }

    [Fact]
    public void TouchesCapturedWithin_IncludesDescendants()
    {
        var ancestor = new TouchOverChainTestPanel();
        var child = new Border();
        ancestor.AddChild(child);

        var device = Touch.RegisterTouchPoint(402, new Point(0, 0), child);
        child.CaptureTouch(device);

        try
        {
            Assert.True(ancestor.AreAnyTouchesCapturedWithin);
            Assert.Contains(device, ancestor.TouchesCapturedWithin);
            Assert.True(child.AreAnyTouchesCaptured);
        }
        finally
        {
            child.ReleaseTouchCapture(device);
            Touch.UnregisterTouchPoint(402);
        }
    }

    private sealed class TouchOverChainTestPanel : FrameworkElement
    {
        public void AddChild(Visual child) => AddVisualChild(child);
    }
}
