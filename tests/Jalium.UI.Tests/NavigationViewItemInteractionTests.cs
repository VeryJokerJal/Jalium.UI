using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class NavigationViewItemInteractionTests
{
    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void NavigationViewItem_SelectedItem_PressShouldApplyPressedBackground()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        ResetInputState();

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native
            };

            var item = new NavigationViewItem { Content = "Item" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(NavigationViewItem)]);
            window.Content = item;
            item.ApplyTemplate();
            item.IsSelected = true;

            var itemBorder = Assert.IsType<Border>(item.FindName("PART_ItemBorder"));
            var selectedBrush = Assert.IsType<SolidColorBrush>(app.Resources["SelectionBackground"]);
            var pressedBrush = Assert.IsType<SolidColorBrush>(app.Resources["AccentBrushPressed"]);

            Assert.Same(selectedBrush, itemBorder.Background);
            Assert.True(item.CaptureMouse());

            InvokeMouseButtonDown(window, MouseButton.Left, x: 10, y: 10);
            Assert.True(item.IsPressed);
            Assert.Same(pressedBrush, itemBorder.Background);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 10, y: 10);
            Assert.False(item.IsPressed);
            Assert.Same(selectedBrush, itemBorder.Background);
        }
        finally
        {
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationViewItem_PreviewMouseUpHandledByAncestor_ShouldResetPressedState()
    {
        ResetInputState();

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native
            };
            var item = new NavigationViewItem();
            window.Content = item;

            window.AddHandler(UIElement.PreviewMouseUpEvent, new RoutedEventHandler((_, e) =>
            {
                e.Handled = true;
            }));

            Assert.True(item.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 10, y: 10);
            Assert.True(item.IsPressed);

            // Even when PreviewMouseUp handles the event (bubble MouseUp suppressed),
            // the window-level pressed chain must still clear.
            InvokeMouseButtonUp(window, MouseButton.Left, x: 10, y: 10);

            Assert.False(item.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void NavigationViewItem_ShouldEnterPressedState_OnEveryPressCycle()
    {
        ResetInputState();

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native
            };
            var item = new NavigationViewItem();
            window.Content = item;
            Assert.True(item.CaptureMouse());

            InvokeMouseButtonDown(window, MouseButton.Left, x: 10, y: 10);
            Assert.True(item.IsPressed);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 10, y: 10);
            Assert.False(item.IsPressed);

            Assert.True(item.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 10, y: 10);
            Assert.True(item.IsPressed);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 10, y: 10);
            Assert.False(item.IsPressed);
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

    private static void InvokeMouseButtonUp(Window window, MouseButton button, int x, int y)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = nint.Zero;
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam });
    }

    private static nint PackPointToLParam(int x, int y)
    {
        int packed = (y << 16) | (x & 0xFFFF);
        return (nint)packed;
    }
}
