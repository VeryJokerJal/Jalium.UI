using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSToolTipInputLifetimeTests
{
    private static ToolTip? Current => (ToolTip?)typeof(ToolTipService)
        .GetField("_currentToolTip", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
    private static object? ShowTimer(ToolTip tip) => typeof(ToolTip)
        .GetField("_showTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tip);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeactivatingTheOwnerWindowCancelsItsPendingOrVisibleTooltip(bool visible)
    {
        ToolTipService.Cleanup();
        var (window, owner, _) = CreateWindow();
        var tip = new ToolTip { Content = "old window hint", InitialShowDelay = 60000 };
        try
        {
            ToolTipService.ShowToolTip(owner, tip, new Point(10, 10));
            Assert.Same(tip, Current);
            Assert.NotNull(ShowTimer(tip));
            if (visible) tip.IsOpen = true;

            window.Deactivate();

            Assert.Null(Current);
            Assert.Null(ShowTimer(tip));
            Assert.False(tip.IsOpen);
        }
        finally { ToolTipService.Cleanup(); }
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void MouseButtonInputCancelsAPendingTooltipInTheSameWindow(MouseButton button)
    {
        ToolTipService.Cleanup();
        var (_, owner, other) = CreateWindow();
        var tip = new ToolTip { Content = "pending hint", InitialShowDelay = 60000 };
        try
        {
            ToolTipService.ShowToolTip(owner, tip, new Point(10, 10));
            Assert.NotNull(ShowTimer(tip));

            MouseDown(other, button);

            Assert.Null(Current);
            Assert.Null(ShowTimer(tip));
            Assert.False(tip.IsOpen);
        }
        finally { ToolTipService.Cleanup(); }
    }

    [Fact]
    public void InputInAnotherWindowPreservesTheCurrentTooltip()
    {
        ToolTipService.Cleanup();
        var (_, owner, _) = CreateWindow();
        var (otherWindow, _, other) = CreateWindow();
        var tip = new ToolTip { Content = "other window hint", InitialShowDelay = 60000 };
        try
        {
            ToolTipService.ShowToolTip(owner, tip, new Point(10, 10));

            MouseDown(other, MouseButton.Left);
            otherWindow.Deactivate();

            Assert.Same(tip, Current);
            Assert.NotNull(ShowTimer(tip));
        }
        finally { ToolTipService.Cleanup(); }
    }

    [Fact]
    public void ClosingTheOwnerWindowCancelsThePendingTooltip()
    {
        ToolTipService.Cleanup();
        var (window, owner, _) = CreateWindow();
        var tip = new ToolTip { Content = "closing window hint", InitialShowDelay = 60000 };
        try
        {
            ToolTipService.ShowToolTip(owner, tip, new Point(10, 10));

            window.CloseForTest();

            Assert.Null(Current);
            Assert.Null(ShowTimer(tip));
        }
        finally { ToolTipService.Cleanup(); }
    }

    [Fact]
    public void DeactivationDetachesTheOldOwnerBeforeAClosingCallbackReplacesTheTooltip()
    {
        ToolTipService.Cleanup();
        var (firstWindow, firstOwner, _) = CreateWindow();
        var (secondWindow, secondOwner, _) = CreateWindow();
        var first = new ToolTip { Content = "first", InitialShowDelay = 60000 };
        var second = new ToolTip { Content = "replacement", InitialShowDelay = 60000 };
        var replaced = false;
        ToolTipEventHandler closing = (_, _) =>
        {
            if (replaced) return;
            replaced = true;
            ToolTipService.ShowToolTip(secondOwner, second, new Point(20, 20));
        };
        ToolTipService.AddToolTipClosingHandler(firstOwner, closing);
        try
        {
            ToolTipService.ShowToolTip(firstOwner, first, new Point(10, 10));

            firstWindow.Deactivate();

            Assert.True(replaced);
            Assert.Same(second, Current);
            Assert.Null(ShowTimer(first));
            Assert.NotNull(ShowTimer(second));
            firstWindow.Deactivate();
            Assert.Same(second, Current);
            secondWindow.Deactivate();
            Assert.Null(Current);
            Assert.Null(ShowTimer(second));
        }
        finally
        {
            ToolTipService.RemoveToolTipClosingHandler(firstOwner, closing);
            ToolTipService.Cleanup();
        }
    }

    [Fact]
    public void CleanupCannotLeaveAReentrantTooltipTimerOrWindowSubscription()
    {
        ToolTipService.Cleanup();
        var (_, owner, other) = CreateWindow();
        var first = new ToolTip { Content = "visible", InitialShowDelay = 60000 };
        var replacement = new ToolTip { Content = "during cleanup", InitialShowDelay = 60000 };
        var attempted = false;
        first.Closed += (_, _) =>
        {
            attempted = true;
            ToolTipService.ShowToolTip(other, replacement, new Point(20, 20));
        };
        try
        {
            ToolTipService.ShowToolTip(owner, first, new Point(10, 10));
            first.IsOpen = true;

            ToolTipService.Cleanup();

            Assert.True(attempted);
            Assert.Null(Current);
            Assert.Null(ShowTimer(first));
            Assert.Null(ShowTimer(replacement));
        }
        finally { ToolTipService.Cleanup(); }
    }

    private static (TestWindow Window, Button Owner, Button Other) CreateWindow()
    {
        var owner = new Button { Content = "opens another window", Width = 160, Height = 32 };
        var other = new Button { Content = "other input", Width = 160, Height = 32 };
        var panel = new StackPanel(); panel.Children.Add(owner); panel.Children.Add(other);
        var window = new TestWindow { Width = 800, Height = 600, Content = panel };
        window.Measure(new Size(800, 600)); window.Arrange(new Rect(0, 0, 800, 600));
        Assert.Same(window, Window.GetWindow(owner));
        return (window, owner, other);
    }

    private static void MouseDown(UIElement target, MouseButton button) => target.RaiseEvent(
        new MouseButtonEventArgs(UIElement.PreviewMouseDownEvent, new Point(1, 1), button,
            MouseButtonState.Pressed, 1, button == MouseButton.Left ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Right ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Middle ? MouseButtonState.Pressed : MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0));

    private sealed class TestWindow : Window
    {
        public void Deactivate() => OnDeactivated(EventArgs.Empty);
        public void CloseForTest() => OnClosed(EventArgs.Empty);
    }
}
