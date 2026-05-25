using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

/// <summary>
/// Verifies that interactive controls honour the native touch path: a TouchDown
/// → TouchUp pair drives Click / Selection identically to a mouse click, and
/// the activating touch contact is captured so a drift-off-and-back-on finger
/// still gets a clean release.
/// </summary>
[Collection("Application")]
public class ControlsTouchActivationTests
{
    private static TouchEventArgs SimulateTouchDown(UIElement target, int touchId, Point position)
    {
        var device = Touch.RegisterTouchPoint(touchId, position, target);
        var args = new TouchEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = UIElement.TouchDownEvent,
            Source = target
        };
        target.RaiseEvent(args);
        return args;
    }

    private static TouchEventArgs SimulateTouchUp(UIElement target, int touchId, Point position)
    {
        var device = Touch.GetDevice(touchId) ?? Touch.RegisterTouchPoint(touchId, position, target);
        var args = new TouchEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = UIElement.TouchUpEvent,
            Source = target
        };
        target.RaiseEvent(args);
        Touch.UnregisterTouchPoint(touchId);
        return args;
    }

    [Fact]
    public void Button_TouchDownTouchUp_FiresClickOnce()
    {
        var button = new Button { Width = 80, Height = 32 };
        int clickCount = 0;
        button.Click += (_, _) => clickCount++;

        SimulateTouchDown(button, 1, new Point(40, 16));
        // simulate the "touch is over button" semantic by adding the device
        // back to direct-over so AreAnyTouchesOver is true at touch-up time.
        var device = Touch.GetDevice(1)!;
        button.AddOverTouchInternal(device);
        button.AddDirectlyOverTouchInternal(device);

        SimulateTouchUp(button, 1, new Point(40, 16));

        Assert.Equal(1, clickCount);
        Assert.False(button.IsPressed);
        Assert.Null(UIElement.GetTouchCapture(1));
    }

    [Fact]
    public void Button_TouchDown_SetsIsPressed_DoesNotCapture()
    {
        // ButtonBase no longer captures the touch contact: leaving it free
        // lets a finger drift outside the button bounds hand control over
        // to an ancestor ScrollViewer's pointer-panning gate. Pressed
        // visual is still set immediately so the tap feels responsive.
        var button = new Button { Width = 80, Height = 32 };
        SimulateTouchDown(button, 2, new Point(0, 0));
        try
        {
            Assert.True(button.IsPressed);
            Assert.Null(UIElement.GetTouchCapture(2));
        }
        finally
        {
            SimulateTouchUp(button, 2, new Point(0, 0));
        }
    }

    [Fact]
    public void ListBoxItem_TouchUp_AfterShortTap_MarksItemHandled()
    {
        // ListBoxItem uses a panning gate: TouchDown marks the touch routed
        // event handled to suppress mouse-event synthesis (which would
        // otherwise invoke OnMouseDown → SelectItem instantly, bypassing the
        // gate). ScrollViewer's panning still works because PointerDown is
        // raised unconditionally by the dispatcher and bubbles to the
        // ancestor regardless of the TouchDown handled flag. The TouchUp
        // handler commits the click and marks the up event handled when it
        // was a stationary tap.
        var item = new ListBoxItem();
        var downArgs = SimulateTouchDown(item, 10, new Point(0, 0));
        Assert.True(downArgs.Handled);

        var upArgs = SimulateTouchUp(item, 10, new Point(0, 0));
        Assert.True(upArgs.Handled);
    }

    [Fact]
    public void TabItem_TouchDown_SelectsTab()
    {
        var tabControl = new TabControl();
        var tabA = new TabItem { Header = "A" };
        var tabB = new TabItem { Header = "B" };
        tabControl.Items.Add(tabA);
        tabControl.Items.Add(tabB);
        // Make sure A is selected initially.
        tabControl.SelectedIndex = 0;

        SimulateTouchDown(tabB, 20, new Point(0, 0));

        Assert.True(tabB.IsSelected);
        Assert.False(tabA.IsSelected);
    }
}
