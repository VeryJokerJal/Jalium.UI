using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class PointerRoutingTests
{
    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
    }

    [Fact]
    public void SourceHandled_ShouldSuppressPointerDown()
    {
        ResetInputState();

        try
        {
            var window = new Window();
            window.TitleBarStyle = WindowTitleBarStyle.Native;
            Assert.True(window.CaptureMouse());
            bool pointerDownRaised = false;

            window.AddHandler(UIElement.PreviewMouseDownEvent, new RoutedEventHandler((_, e) => e.Handled = true));
            window.AddHandler(UIElement.PointerDownEvent, new RoutedEventHandler((_, _) => pointerDownRaised = true));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 120, y: 160);

            Assert.False(pointerDownRaised);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void SourceCancel_ShouldRaisePointerCancelAndSuppressPointerDown()
    {
        ResetInputState();

        try
        {
            var window = new Window();
            window.TitleBarStyle = WindowTitleBarStyle.Native;
            Assert.True(window.CaptureMouse());
            int pointerCancelCount = 0;
            int pointerDownCount = 0;

            window.AddHandler(UIElement.PreviewMouseDownEvent, new RoutedEventHandler((_, e) =>
            {
                if (e is MouseButtonEventArgs mouseArgs)
                {
                    mouseArgs.Cancel = true;
                }
            }));
            window.AddHandler(UIElement.PointerCancelEvent, new RoutedEventHandler((_, _) => pointerCancelCount++));
            window.AddHandler(UIElement.PointerDownEvent, new RoutedEventHandler((_, _) => pointerDownCount++));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 140, y: 180);

            Assert.Equal(1, pointerCancelCount);
            Assert.Equal(0, pointerDownCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void SourceAndPointerOrder_ShouldBeSourceThenPointer()
    {
        ResetInputState();

        try
        {
            var window = new Window();
            window.TitleBarStyle = WindowTitleBarStyle.Native;
            Assert.True(window.CaptureMouse());
            var order = new List<string>();

            window.AddHandler(UIElement.PreviewMouseDownEvent, new Input.MouseButtonEventHandler((_, _) => order.Add("PreviewMouseDown")));
            window.AddHandler(UIElement.MouseDownEvent, new Input.MouseButtonEventHandler((_, _) => order.Add("MouseDown")));
            window.AddHandler(UIElement.PreviewPointerDownEvent, new Input.PointerDownEventHandler((_, _) => order.Add("PreviewPointerDown")));
            window.AddHandler(UIElement.PointerDownEvent, new Input.PointerDownEventHandler((_, _) => order.Add("PointerDown")));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 160, y: 200);

            Assert.Equal(new[] { "PreviewMouseDown", "MouseDown", "PreviewPointerDown", "PointerDown" }, order);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void LegacyAndNewPointerNames_ShouldReceiveSameInputStream()
    {
        ResetInputState();

        try
        {
            var window = new Window();
            window.TitleBarStyle = WindowTitleBarStyle.Native;
            Assert.True(window.CaptureMouse());
            uint downPointerId = 0;
            uint pressedPointerId = 0;
            int downCount = 0;
            int pressedCount = 0;

            window.AddHandler(UIElement.PointerDownEvent, new RoutedEventHandler((_, e) =>
            {
                downCount++;
                downPointerId = ((PointerEventArgs)e).Pointer.PointerId;
            }));
            window.AddHandler(UIElement.PointerPressedEvent, new RoutedEventHandler((_, e) =>
            {
                pressedCount++;
                pressedPointerId = ((PointerEventArgs)e).Pointer.PointerId;
            }));

            InvokeMouseButtonDown(window, MouseButton.Left, x: 180, y: 220);

            Assert.Equal(1, downCount);
            Assert.Equal(1, pressedCount);
            Assert.Equal(downPointerId, pressedPointerId);
        }
        finally
        {
            ResetInputState();
        }
    }

    private static void InvokeMouseButtonDown(Window window, MouseButton button, int x, int y, int clickCount = 1)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = (nint)0x0001; // MK_LBUTTON
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam, clickCount });
    }

    private static nint PackPointToLParam(int x, int y)
    {
        int packed = (y << 16) | (x & 0xFFFF);
        return (nint)packed;
    }
}
