using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class TextWordSelectionGestureTests
{
    [Fact]
    public void TextBox_DoubleClickDrag_ShouldSelectWholeWords()
    {
        var textBox = new TextBox
        {
            Text = "one two three",
            Width = 240,
            Height = 32
        };
        textBox.Arrange(new Rect(0, 0, 240, 32));

        var pointInTwo = new Point(50, 12);
        var pointInThree = new Point(150, 12);

        textBox.RaiseEvent(CreateMouseDown(pointInTwo));
        textBox.RaiseEvent(CreateMouseUp(pointInTwo));
        textBox.RaiseEvent(CreateMouseDown(pointInTwo));
        textBox.RaiseEvent(CreateMouseMove(pointInThree, MouseButtonState.Pressed));
        textBox.RaiseEvent(CreateMouseUp(pointInThree));

        Assert.Equal("two three", textBox.SelectedText);
    }

    [Fact]
    public void TextBlock_DoubleClickDrag_ShouldSelectWholeWords()
    {
        var textBlock = new TextBlock
        {
            Text = "one two three",
            IsTextSelectionEnabled = true,
            Width = 240,
            Height = 32
        };
        textBlock.Arrange(new Rect(0, 0, 240, 32));

        var pointInTwo = new Point(50, 12);
        var pointInThree = new Point(150, 12);

        textBlock.RaiseEvent(CreateMouseDown(pointInTwo, clickCount: 2));
        textBlock.RaiseEvent(CreateMouseMove(pointInThree, MouseButtonState.Pressed));
        textBlock.RaiseEvent(CreateMouseUp(pointInThree));

        Assert.Equal("two three", textBlock.SelectedText);
    }

    [Fact]
    public void Label_DoubleClickDrag_ShouldSelectWholeWords()
    {
        var label = new Label
        {
            Content = "one two three",
            IsTextSelectionEnabled = true,
            Template = new ControlTemplate(typeof(Label)),
            Width = 240,
            Height = 28
        };
        label.Arrange(new Rect(0, 0, 240, 28));

        var pointInTwo = new Point(50, 12);
        var pointInThree = new Point(150, 12);

        label.RaiseEvent(CreateMouseDown(pointInTwo, clickCount: 2));
        label.RaiseEvent(CreateMouseMove(pointInThree, MouseButtonState.Pressed));
        label.RaiseEvent(CreateMouseUp(pointInThree));

        Assert.Equal("two three", label.SelectedText);
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position, int clickCount = 1)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount,
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
