using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class TextBlockSelectionTests
{
    [Fact]
    public void TextBlock_DragSelection_ShouldSelectDraggedRange()
    {
        var textBlock = new TextBlock
        {
            Text = "Hello world",
            IsTextSelectionEnabled = true,
            Width = 240,
            Height = 32
        };
        textBlock.Arrange(new Rect(0, 0, 240, 32));

        textBlock.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        textBlock.RaiseEvent(CreateMouseMove(new Point(220, 12), MouseButtonState.Pressed));
        textBlock.RaiseEvent(CreateMouseUp(new Point(220, 12)));

        Assert.Equal(0, textBlock.SelectionStart);
        Assert.Equal(textBlock.Text.Length, textBlock.SelectionLength);
        Assert.Equal(textBlock.Text, textBlock.SelectedText);
    }

    [Fact]
    public void TextBlock_MouseSelection_ShouldBeDisabledByDefault()
    {
        var textBlock = new TextBlock
        {
            Text = "Hello world",
            Width = 240,
            Height = 32
        };
        textBlock.Arrange(new Rect(0, 0, 240, 32));

        textBlock.RaiseEvent(CreateMouseEnter(new Point(4, 12)));
        textBlock.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        textBlock.RaiseEvent(CreateMouseMove(new Point(220, 12), MouseButtonState.Pressed));
        textBlock.RaiseEvent(CreateMouseUp(new Point(220, 12)));

        Assert.Null(textBlock.Cursor);
        Assert.Equal(0, textBlock.SelectionLength);
        Assert.Equal(string.Empty, textBlock.SelectedText);
    }

    [Fact]
    public void TextBlock_InsideButton_ShouldNotStartTextSelectionByDefault()
    {
        var textBlock = new TextBlock { Text = "Press me" };
        var button = new Button { Content = textBlock };
        var root = new StackPanel();
        root.Children.Add(button);
        root.Measure(new Size(160, 40));
        root.Arrange(new Rect(0, 0, 160, 40));

        textBlock.RaiseEvent(CreateMouseDown(new Point(8, 12)));

        Assert.Equal(0, textBlock.SelectionLength);
    }

    [Fact]
    public void TextBlock_ShouldExposeSelectedText_AndUseIBeamCursor()
    {
        var textBlock = new TextBlock
        {
            Text = "Copy me",
            IsTextSelectionEnabled = true,
            Width = 200,
            Height = 32
        };
        textBlock.Arrange(new Rect(0, 0, 200, 32));

        textBlock.RaiseEvent(CreateMouseEnter(new Point(4, 12)));
        textBlock.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        textBlock.RaiseEvent(CreateMouseMove(new Point(180, 12), MouseButtonState.Pressed));

        Assert.Same(Jalium.UI.Cursors.IBeam, textBlock.Cursor);
        Assert.Equal("Copy me", textBlock.SelectedText);
    }

    [Fact]
    public void TextBlock_Selection_ShouldPersist_WhenFocusMovesElsewhere()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();

        var textBlock = new TextBlock
        {
            Text = "Persist me",
            IsTextSelectionEnabled = true,
            Width = 220,
            Height = 32
        };
        var button = new Button();
        var root = new StackPanel();
        root.Children.Add(textBlock);
        root.Children.Add(button);
        root.Measure(new Size(240, 80));
        root.Arrange(new Rect(0, 0, 240, 80));

        textBlock.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        textBlock.RaiseEvent(CreateMouseMove(new Point(210, 12), MouseButtonState.Pressed));
        textBlock.RaiseEvent(CreateMouseUp(new Point(210, 12)));

        Assert.Equal("Persist me", textBlock.SelectedText);
        Assert.True(button.Focus());
        Assert.Equal("Persist me", textBlock.SelectedText);

        Keyboard.ClearFocus();
    }

    [Fact]
    public void Label_SingleClickOnText_ShouldStillFocusTarget()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();

        var target = new Button();
        var label = new Label
        {
            Content = "Username",
            Target = target,
            Width = 200,
            Height = 28
        };
        var root = new StackPanel();
        root.Children.Add(label);
        root.Children.Add(target);
        root.Measure(new Size(220, 80));
        root.Arrange(new Rect(0, 0, 220, 80));

        label.RaiseEvent(CreateMouseDown(new Point(8, 12)));
        label.RaiseEvent(CreateMouseUp(new Point(8, 12)));

        Assert.True(target.IsFocused);
        Keyboard.ClearFocus();
    }

    [Fact]
    public void Label_DirectText_ShouldExposeSelectedText_AndUseIBeamCursor()
    {
        var label = new Label
        {
            Content = "Copy label",
            IsTextSelectionEnabled = true,
            Template = new ControlTemplate(typeof(Label)),
            Width = 220,
            Height = 28
        };
        label.Arrange(new Rect(0, 0, 220, 28));

        label.RaiseEvent(CreateMouseEnter(new Point(4, 12)));
        label.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        label.RaiseEvent(CreateMouseMove(new Point(210, 12), MouseButtonState.Pressed));
        Assert.True(GetPrivateField<int>(label, "_directSelectionLength") > 0);

        Assert.Same(Jalium.UI.Cursors.IBeam, label.Cursor);
        Assert.Equal("Copy label", label.SelectedText);
    }

    [Fact]
    public void Label_DirectSelection_ShouldPersist_WhenFocusMovesElsewhere()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();

        var label = new Label
        {
            Content = "Persist label",
            IsTextSelectionEnabled = true,
            Template = new ControlTemplate(typeof(Label)),
            Width = 240,
            Height = 28
        };
        var button = new Button();
        var root = new StackPanel();
        root.Children.Add(label);
        root.Children.Add(button);
        root.Measure(new Size(260, 80));
        root.Arrange(new Rect(0, 0, 260, 80));

        label.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        label.RaiseEvent(CreateMouseMove(new Point(220, 12), MouseButtonState.Pressed));
        label.RaiseEvent(CreateMouseUp(new Point(220, 12)));

        Assert.Equal("Persist label", label.SelectedText);
        Assert.True(button.Focus());
        Assert.Equal("Persist label", label.SelectedText);

        Keyboard.ClearFocus();
    }

    [Fact]
    public void Label_DragOnDirectText_ShouldKeepSelectionInsteadOfFocusingTarget()
    {
        var target = new Button();
        var label = new Label
        {
            Content = "Selectable text",
            IsTextSelectionEnabled = true,
            Target = target,
            Template = new ControlTemplate(typeof(Label)),
            Width = 240,
            Height = 28
        };
        var root = new StackPanel();
        root.Children.Add(label);
        root.Children.Add(target);
        root.Measure(new Size(260, 80));
        root.Arrange(new Rect(0, 0, 260, 80));

        label.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        label.RaiseEvent(CreateMouseMove(new Point(180, 12), MouseButtonState.Pressed));
        label.RaiseEvent(CreateMouseUp(new Point(180, 12)));

        Assert.False(target.IsFocused);
        Assert.True(GetPrivateField<int>(label, "_directSelectionLength") > 0);
    }

    [Fact]
    public void Label_DirectTextSelection_ShouldBeDisabledByDefault()
    {
        var label = new Label
        {
            Content = "Copy label",
            Template = new ControlTemplate(typeof(Label)),
            Width = 220,
            Height = 28
        };
        label.Arrange(new Rect(0, 0, 220, 28));

        label.RaiseEvent(CreateMouseEnter(new Point(4, 12)));
        label.RaiseEvent(CreateMouseDown(new Point(0, 12)));
        label.RaiseEvent(CreateMouseMove(new Point(210, 12), MouseButtonState.Pressed));
        label.RaiseEvent(CreateMouseUp(new Point(210, 12)));

        Assert.Null(label.Cursor);
        Assert.Equal(string.Empty, label.SelectedText);
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

    private static MouseEventArgs CreateMouseEnter(Point position)
    {
        return new MouseEventArgs(
            UIElement.MouseEnterEvent,
            position,
            MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 2);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }
}
