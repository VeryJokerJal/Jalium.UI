using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFlyoutKeyboardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuWithoutEnabledItems_StillEscapesAndReturnsFocus(bool disabledItem)
    {
        var target = new Button { Content = "菜单入口" };
        var window = new DisplayedTestWindow { Width = 320, Height = 200, Content = target };
        var menu = new MenuFlyout();
        if (disabledItem) menu.Items.Add(new MenuFlyoutItem { Text = "不可用", IsEnabled = false });
        try
        {
            window.Measure(new Size(320, 200)); window.Arrange(new Rect(0, 0, 320, 200));
            Assert.True(target.Focus());
            menu.ShowAt(target); Dispatcher.GetForCurrentThread().ProcessQueue();
            var focused = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
            Assert.NotSame(target, focused);
            focused.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Escape, ModifierKeys.None, true, false, 0));
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.False(menu.IsOpen);
            Assert.Same(target, Keyboard.FocusedElement);
        }
        finally { menu.Hide(); window.Close(); }
    }

    [Fact]
    public void ContextMenu_TakesInitialFocusSkipsDisabledItemsAndEscapesToItsOwner()
    {
        var target = new Button { Content = "文件树位置" };
        var window = new DisplayedTestWindow { Width = 320, Height = 200, Content = target };
        var menu = new MenuFlyout();
        var first = new MenuFlyoutItem { Text = "不可用", IsEnabled = false };
        var second = new MenuFlyoutItem { Text = "属性" };
        var third = new MenuFlyoutItem { Text = "新建" };
        menu.Items.Add(first); menu.Items.Add(second); menu.Items.Add(third);
        try
        {
            window.Measure(new Size(320, 200)); window.Arrange(new Rect(0, 0, 320, 200));
            Assert.True(target.Focus());
            menu.ShowAt(target);
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.True(menu.IsOpen);
            Assert.Same(second, Keyboard.FocusedElement);
            second.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Down, ModifierKeys.None, true, false, 0));
            Assert.Same(third, Keyboard.FocusedElement);
            third.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Escape, ModifierKeys.None, true, false, 0));
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.False(menu.IsOpen);
            Assert.Same(target, Keyboard.FocusedElement);

            menu.ShowAt(target); menu.Hide();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.Same(target, Keyboard.FocusedElement); // A closed menu must not steal focus later.
        }
        finally { menu.Hide(); window.Close(); }
    }
}
