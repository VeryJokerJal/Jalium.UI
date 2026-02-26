using Jalium.UI;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class ThumbTests
{
    [Fact]
    public void DragDelta_ShouldUseStableCoordinates_WhenThumbMovesDuringDrag()
    {
        var thumb = new Thumb();
        thumb.Arrange(new Rect(20, 20, 10, 30));

        int dragDeltaCount = 0;
        double totalHorizontalDelta = 0;
        thumb.DragDelta += (_, e) =>
        {
            dragDeltaCount++;
            totalHorizontalDelta += e.HorizontalChange;
        };

        thumb.RaiseEvent(CreateMouseDown(new Point(25, 30)));
        thumb.RaiseEvent(CreateMouseMove(new Point(29, 30), MouseButtonState.Pressed));

        // Simulate the thumb being re-arranged by its parent after the first delta.
        thumb.Arrange(new Rect(24, 20, 10, 30));

        thumb.RaiseEvent(CreateMouseMove(new Point(33, 30), MouseButtonState.Pressed));
        thumb.RaiseEvent(CreateMouseUp(new Point(33, 30)));

        Assert.Equal(2, dragDeltaCount);
        Assert.Equal(8.0, totalHorizontalDelta, 3);
    }

    [Fact]
    public void DragCompleted_ShouldRaiseOnce_OnMouseUp()
    {
        var thumb = new Thumb();
        thumb.Arrange(new Rect(0, 0, 12, 12));

        int dragCompletedCount = 0;
        DragCompletedEventArgs? completedArgs = null;
        thumb.DragCompleted += (_, e) =>
        {
            dragCompletedCount++;
            completedArgs = e;
        };

        thumb.RaiseEvent(CreateMouseDown(new Point(6, 6)));
        thumb.RaiseEvent(CreateMouseMove(new Point(11, 6), MouseButtonState.Pressed));
        thumb.RaiseEvent(CreateMouseUp(new Point(11, 6)));

        Assert.Equal(1, dragCompletedCount);
        Assert.NotNull(completedArgs);
        Assert.False(completedArgs!.Canceled);
        Assert.Equal(5.0, completedArgs.HorizontalChange, 3);
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }

    private static MouseButtonEventArgs CreateMouseUp(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseUpEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Released,
            clickCount: 1,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1);
    }

    private static MouseEventArgs CreateMouseMove(Point position, MouseButtonState leftButton)
    {
        return new MouseEventArgs(
            UIElement.MouseMoveEvent,
            position,
            leftButton,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 2);
    }
}
