using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class EditControlWordSelectionTests
{
    [Fact]
    public void EditControl_DoubleClickDrag_ShouldSelectWholeWords()
    {
        var editor = new EditControl
        {
            Width = 320,
            Height = 120
        };
        editor.LoadText("one two three");
        editor.Measure(new Size(320, 120));
        editor.Arrange(new Rect(0, 0, 320, 120));

        var metrics = (IEditorViewMetrics)editor;
        var pointInTwo = OffsetPoint(metrics, 5, editor.ShowLineNumbers);
        var pointInThree = OffsetPoint(metrics, 10, editor.ShowLineNumbers);

        editor.RaiseEvent(CreateMouseDown(pointInTwo));
        editor.RaiseEvent(CreateMouseUp(pointInTwo));
        editor.RaiseEvent(CreateMouseDown(pointInTwo));
        editor.RaiseEvent(CreateMouseMove(pointInThree, MouseButtonState.Pressed));
        editor.RaiseEvent(CreateMouseUp(pointInThree));

        Assert.Equal("two three", editor.SelectedText);
    }

    private static Point OffsetPoint(IEditorViewMetrics metrics, int offset, bool showLineNumbers)
    {
        var point = metrics.GetPointFromOffset(offset, showLineNumbers);
        return new Point(point.X + 2, point.Y + Math.Max(2, metrics.LineHeight / 2));
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
