using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTextBoxShiftCaptureTests
{
    [Theory]
    [InlineData(ModifierKeys.Shift)]
    [InlineData(ModifierKeys.Shift | ModifierKeys.Control)]
    [InlineData(ModifierKeys.Shift | ModifierKeys.Alt)]
    public void ShiftClickReleasesItsSelectionCaptureAtMouseUp(ModifierKeys modifiers)
    {
        using var h = new Host();
        h.Button(true, new Point(20, 14), modifiers);
        Assert.Same(h.Box, Mouse.Captured);
        h.Button(false, new Point(20, 14), modifiers);
        Assert.Null(Mouse.Captured);
        Assert.Same(h.Box, Keyboard.FocusedElement);
        Assert.Equal("alpha beta gamma delta", h.Box.Text);
    }

    [Fact]
    public void ShiftDragExtendsSelectionFromTheExistingAnchorAndThenReleases()
    {
        using var h = new Host();
        h.Button(true, new Point(20, 14), ModifierKeys.Shift);
        int before = h.Box.SelectionLength;
        var position = h.Box.TranslatePoint(new Point(120, 14), h.Window);
        h.Box.RaiseEvent(new MouseEventArgs(UIElement.MouseMoveEvent, position,
            MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.Shift, 20));
        Assert.True(h.Box.SelectionLength > before);
        Assert.Equal(2, h.Box.SelectionStart);
        h.Button(false, new Point(120, 14), ModifierKeys.Shift);
        Assert.Null(Mouse.Captured);
    }

    private sealed class Host : IDisposable
    {
        internal readonly TextBox Box = new() { Text = "alpha beta gamma delta", Width = 320, Height = 36 };
        internal readonly DisplayedTestWindow Window;
        internal Host()
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Window = new DisplayedTestWindow { Width = 400, Height = 200, Content = Box,
                TitleBarStyle = WindowTitleBarStyle.Native };
            Window.Measure(new Size(400, 200)); Window.Arrange(new Rect(0, 0, 400, 200));
            Assert.True(Box.Focus()); Box.Select(2, 0);
        }
        internal void Button(bool down, Point point, ModifierKeys modifiers)
        {
            var position=Box.TranslatePoint(point,Window);
            Box.RaiseEvent(new MouseButtonEventArgs(down?UIElement.MouseDownEvent:UIElement.MouseUpEvent,
                position,MouseButton.Left,down?MouseButtonState.Pressed:MouseButtonState.Released,1,
                down?MouseButtonState.Pressed:MouseButtonState.Released,MouseButtonState.Released,
                MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,modifiers,10));
        }
        public void Dispose()
        {
            Mouse.Capture(null); Keyboard.ClearFocus(); Window.Close();
        }
    }
}
