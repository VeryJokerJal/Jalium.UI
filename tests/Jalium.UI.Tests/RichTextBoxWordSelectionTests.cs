using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class RichTextBoxWordSelectionTests
{
    [Fact]
    public void RichTextBox_DoubleClickDrag_ShouldSelectWholeWords()
    {
        var richTextBox = new RichTextBox
        {
            Width = 320,
            Height = 120
        };
        richTextBox.SetText("one two three");
        richTextBox.Measure(new Size(320, 120));
        richTextBox.Arrange(new Rect(0, 0, 320, 120));

        var pointInTwo = GetPointFromOffset(richTextBox, 5);
        var pointInThree = GetPointFromOffset(richTextBox, 10);

        richTextBox.RaiseEvent(CreateMouseDown(pointInTwo));
        richTextBox.RaiseEvent(CreateMouseUp(pointInTwo));
        richTextBox.RaiseEvent(CreateMouseDown(pointInTwo));
        richTextBox.RaiseEvent(CreateMouseMove(pointInThree, MouseButtonState.Pressed));
        richTextBox.RaiseEvent(CreateMouseUp(pointInThree));

        var selectionField = typeof(RichTextBox).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(selectionField);
        var selection = Assert.IsType<TextRange>(selectionField!.GetValue(richTextBox));
        Assert.Equal("two three", selection.Text);
    }

    private static Point GetPointFromOffset(RichTextBox richTextBox, int offset)
    {
        var document = richTextBox.Document;
        var position = document.GetPositionAtOffset(offset, LogicalDirection.Forward);
        Assert.NotNull(position);

        var contentBoundsMethod = typeof(RichTextBox).GetMethod("GetContentBounds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(contentBoundsMethod);
        var contentBounds = Assert.IsType<Rect>(contentBoundsMethod!.Invoke(richTextBox, null));

        var getPointMethod = typeof(RichTextBox).GetMethod(
            "GetCaretScreenPosition",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Rect), typeof(TextPointer)],
            modifiers: null);
        Assert.NotNull(getPointMethod);
        var point = Assert.IsType<Point>(getPointMethod!.Invoke(richTextBox, [contentBounds, position!]));
        return new Point(point.X + 2, point.Y + 6);
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
