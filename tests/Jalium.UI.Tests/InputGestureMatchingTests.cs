using Jalium.UI.Input;
using System.Globalization;

namespace Jalium.UI.Tests;

public sealed class InputGestureMatchingTests
{
    [Fact]
    public void KeyGesture_MatchesKeyAndExactModifiers()
    {
        var gesture = new KeyGesture(Key.K, ModifierKeys.Control | ModifierKeys.Shift);
        var matchingEvent = new KeyEventArgs(
            UIElement.KeyDownEvent,
            Key.K,
            ModifierKeys.Control | ModifierKeys.Shift,
            isDown: true,
            isRepeat: false,
            timestamp: 1);

        Assert.True(gesture.Matches(new object(), matchingEvent));
        Assert.False(gesture.Matches(
            new object(),
            new KeyEventArgs(UIElement.KeyDownEvent, Key.K, ModifierKeys.Control, true, false, 2)));
        Assert.False(gesture.Matches(
            new object(),
            new KeyEventArgs(UIElement.KeyDownEvent, Key.L, ModifierKeys.Control | ModifierKeys.Shift, true, false, 3)));
    }

    [Theory]
    [InlineData(MouseButton.Left, 1, MouseAction.LeftClick)]
    [InlineData(MouseButton.Left, 2, MouseAction.LeftDoubleClick)]
    [InlineData(MouseButton.Right, 1, MouseAction.RightClick)]
    [InlineData(MouseButton.Right, 2, MouseAction.RightDoubleClick)]
    [InlineData(MouseButton.Middle, 1, MouseAction.MiddleClick)]
    [InlineData(MouseButton.Middle, 2, MouseAction.MiddleDoubleClick)]
    public void MouseGesture_MatchesButtonActionAndExactModifiers(
        MouseButton button,
        int clickCount,
        MouseAction action)
    {
        var gesture = new MouseGesture(action, ModifierKeys.Control);
        MouseButtonEventArgs input = CreateMouseButtonEvent(button, clickCount, ModifierKeys.Control);

        Assert.True(gesture.Matches(new object(), input));
        Assert.False(gesture.Matches(
            new object(),
            CreateMouseButtonEvent(button, clickCount, ModifierKeys.None)));
    }

    [Fact]
    public void MouseGesture_MatchesWheelAndRejectsUnsupportedActions()
    {
        var wheelGesture = new MouseGesture(MouseAction.WheelClick, ModifierKeys.Alt);
        var wheelEvent = new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            new Point(4, 5),
            delta: 120,
            MouseButtonState.Released,
            MouseButtonState.Released,
            MouseButtonState.Released,
            MouseButtonState.Released,
            MouseButtonState.Released,
            ModifierKeys.Alt,
            timestamp: 10);

        Assert.True(wheelGesture.Matches(new object(), wheelEvent));

        var xButtonGesture = new MouseGesture(MouseAction.LeftClick);
        Assert.False(xButtonGesture.Matches(
            new object(),
            CreateMouseButtonEvent(MouseButton.XButton1, 1, ModifierKeys.None)));
        Assert.False(new MouseGesture(MouseAction.None).Matches(new object(), wheelEvent));
    }

    [Fact]
    public void GestureAndBindingApis_ExposeStronglyTypedWpfContracts()
    {
        Assert.NotNull(typeof(KeyGesture).GetConstructor([typeof(Key)]));
        Assert.NotNull(typeof(KeyGesture).GetConstructor([typeof(Key), typeof(ModifierKeys)]));
        Assert.NotNull(typeof(KeyGesture).GetConstructor([typeof(Key), typeof(ModifierKeys), typeof(string)]));
        Assert.Equal(typeof(Key), typeof(KeyGesture).GetProperty(nameof(KeyGesture.Key))!.PropertyType);
        Assert.Equal(typeof(ModifierKeys), typeof(KeyGesture).GetProperty(nameof(KeyGesture.Modifiers))!.PropertyType);
        Assert.NotNull(typeof(KeyGesture).GetMethod(
            nameof(KeyGesture.GetDisplayStringForCulture),
            [typeof(CultureInfo)]));

        Assert.NotNull(typeof(MouseGesture).GetConstructor([typeof(MouseAction), typeof(ModifierKeys)]));
        Assert.Equal(
            typeof(ModifierKeys),
            typeof(MouseGesture).GetProperty(nameof(MouseGesture.Modifiers))!.PropertyType);

        Assert.NotNull(typeof(KeyBinding).GetConstructor(
            [typeof(System.Windows.Input.ICommand), typeof(Key), typeof(ModifierKeys)]));
        Assert.Equal(typeof(Key), typeof(KeyBinding).GetProperty(nameof(KeyBinding.Key))!.PropertyType);
        Assert.Equal(typeof(ModifierKeys), typeof(KeyBinding).GetProperty(nameof(KeyBinding.Modifiers))!.PropertyType);
    }

    [Fact]
    public void MouseGestureConverter_RoundTripsActionAndModifiers()
    {
        var converter = new MouseGestureConverter();
        var gesture = new MouseGesture(
            MouseAction.LeftDoubleClick,
            ModifierKeys.Control | ModifierKeys.Shift);

        string text = Assert.IsType<string>(
            converter.ConvertTo(null, CultureInfo.InvariantCulture, gesture, typeof(string)));
        var roundTripped = Assert.IsType<MouseGesture>(
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));

        Assert.Equal("Ctrl+Shift+LeftDoubleClick", text);
        Assert.Equal(gesture.MouseAction, roundTripped.MouseAction);
        Assert.Equal(gesture.Modifiers, roundTripped.Modifiers);
    }

    private static MouseButtonEventArgs CreateMouseButtonEvent(
        MouseButton button,
        int clickCount,
        ModifierKeys modifiers)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            new Point(10, 20),
            button,
            MouseButtonState.Pressed,
            clickCount,
            button == MouseButton.Left ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Middle ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Right ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.XButton1 ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.XButton2 ? MouseButtonState.Pressed : MouseButtonState.Released,
            modifiers,
            timestamp: 1);
    }
}
