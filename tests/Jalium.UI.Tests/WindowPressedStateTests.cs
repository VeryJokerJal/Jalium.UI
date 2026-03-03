using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class WindowPressedStateTests
{
    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
    }

    [Fact]
    public void MouseDownUp_ShouldSetAndClearPressedState_OnHitChain()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.CaptureMouse());

            InvokeMouseButtonDown(window, MouseButton.Left, x: 20, y: 20);

            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 20, y: 20);

            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void PreviewMouseUpHandled_ShouldStillClearPressedState()
    {
        ResetInputState();

        try
        {
            var (window, _, leaf) = CreateWindowTree();
            window.AddHandler(UIElement.PreviewMouseUpEvent, new RoutedEventHandler((_, e) =>
            {
                e.Handled = true;
            }));

            Assert.True(leaf.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 30, y: 30);
            Assert.True(leaf.IsPressed);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 30, y: 30);
            Assert.False(leaf.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void CaptureChanged_ShouldClearPressedState()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 40, y: 40);

            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);

            InvokeWndProc(window, msg: 0x0215, wParam: nint.Zero, lParam: nint.Zero); // WM_CAPTURECHANGED

            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void SpaceKey_ShouldSetAndClearPressedState_OnFocusChain()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());

            InvokeKeyDown(window, Key.Space, lParam: nint.Zero);
            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);

            InvokeKeyUp(window, Key.Space, lParam: nint.Zero);
            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void RepeatedSpaceKeyDown_ShouldNotActivatePressedState()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());

            nint repeatLParam = (nint)(1L << 30);
            InvokeKeyDown(window, Key.Space, repeatLParam);

            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    private static (Window window, TestPanel host, TestElement leaf) CreateWindowTree()
    {
        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native
        };

        var host = new TestPanel();
        var leaf = new TestElement();
        host.AddChild(leaf);
        window.Content = host;

        return (window, host, leaf);
    }

    private static void InvokeMouseButtonDown(Window window, MouseButton button, int x, int y, int clickCount = 1)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = (nint)0x0001; // MK_LBUTTON
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam, clickCount });
    }

    private static void InvokeMouseButtonUp(Window window, MouseButton button, int x, int y)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = nint.Zero;
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam });
    }

    private static void InvokeKeyDown(Window window, Key key, nint lParam)
    {
        var method = typeof(Window).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)(int)key, lParam });
    }

    private static void InvokeKeyUp(Window window, Key key, nint lParam)
    {
        var method = typeof(Window).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)(int)key, lParam });
    }

    private static void InvokeWndProc(Window window, uint msg, nint wParam, nint lParam)
    {
        var method = typeof(Window).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        _ = method!.Invoke(window, new object[] { nint.Zero, msg, wParam, lParam });
    }

    private static nint PackPointToLParam(int x, int y)
    {
        int packed = (y << 16) | (x & 0xFFFF);
        return (nint)packed;
    }

    private sealed class TestPanel : FrameworkElement
    {
        public void AddChild(UIElement child)
        {
            AddVisualChild(child);
        }
    }

    private sealed class TestElement : FrameworkElement
    {
        public TestElement()
        {
            Focusable = true;
        }
    }
}
