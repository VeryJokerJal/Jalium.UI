using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("Application")]
public class MenuKeyboardTests
{
    [Fact]
    public void MenuBarItem_RightArrow_ShouldMoveFocusToSibling()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();
        try
        {
            var menuBar = new MenuBar();
            var fileItem = new MenuBarItem
            {
                Title = "File"
            };
            var editItem = new MenuBarItem
            {
                Title = "Edit"
            };
            menuBar.Items.Add(fileItem);
            menuBar.Items.Add(editItem);
            var host = new StackPanel();
            host.Children.Add(menuBar);
            ArrangeRoot(host);
            Assert.True(fileItem.Focus());
            fileItem.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Right, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Assert.Same(editItem, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuBarItem_DownArrow_ShouldOpenMenuAndFocusFirstCommand()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();
        MenuBarItem? fileItem = null;
        try
        {
            var fileCommand = new MenuFlyoutItem
            {
                Text = "Open"
            };
            fileItem = new MenuBarItem
            {
                Title = "File"
            };
            fileItem.Items.Add(fileCommand);
            var menuBar = new MenuBar();
            menuBar.Items.Add(fileItem);
            // Keyboard routing uses a visible headless tree. Native window focus
            // is verified separately; an unshown Window keeps template parts hidden.
            ArrangeRoot(menuBar);
            Assert.True(fileItem.Focus());
            fileItem.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Down, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.True(fileItem.IsMenuOpen);
            Assert.Same(fileCommand, Keyboard.FocusedElement);
        }
        finally
        {
            fileItem?.CloseMenu();
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuFlyoutItem_Escape_ShouldCloseContainingPopupAndRestoreOwnerFocus()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();
        try
        {
            var fileCommand = new MenuFlyoutItem
            {
                Text = "Open"
            };
            var fileItem = new MenuBarItem
            {
                Title = "File"
            };
            fileItem.Items.Add(fileCommand);
            var menuBar = new MenuBar();
            menuBar.Items.Add(fileItem);
            // Keyboard routing uses a visible headless tree. Native window focus
            // is verified separately; an unshown Window keeps template parts hidden.
            ArrangeRoot(menuBar);
            Assert.True(fileItem.Focus());
            fileItem.OpenMenuAndFocusFirstItem();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.Same(fileCommand, Keyboard.FocusedElement);
            fileCommand.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Escape, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.False(fileItem.IsMenuOpen);
            Assert.Same(fileItem, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuFlyoutItem_RightArrow_ShouldOpenNextTopLevelMenu()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();
        MenuBarItem? fileItem = null;
        MenuBarItem? editItem = null;
        try
        {
            var openItem = new MenuFlyoutItem
            {
                Text = "Open"
            };
            var pasteItem = new MenuFlyoutItem
            {
                Text = "Paste"
            };
            fileItem = new MenuBarItem
            {
                Title = "File"
            };
            fileItem.Items.Add(openItem);
            editItem = new MenuBarItem
            {
                Title = "Edit"
            };
            editItem.Items.Add(pasteItem);
            var menuBar = new MenuBar();
            menuBar.Items.Add(fileItem);
            menuBar.Items.Add(editItem);
            // Keyboard routing uses a visible headless tree. Native window focus
            // is verified separately; an unshown Window keeps template parts hidden.
            ArrangeRoot(menuBar);
            fileItem.OpenMenuAndFocusFirstItem();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.Same(openItem, Keyboard.FocusedElement);
            var key = new KeyEventArgs(UIElement.KeyDownEvent, Key.Right, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0);
            var before = DescribeParents(openItem);
            openItem.RaiseEvent(key);
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.False(fileItem.IsMenuOpen, $"handled={key.Handled}, editOpen={editItem.IsMenuOpen}, parentBarSame={ReferenceEquals(fileItem.ParentMenuBar, menuBar)}, before={before}, after={DescribeParents(openItem)}");
            Assert.True(editItem.IsMenuOpen);
            Assert.Same(pasteItem, Keyboard.FocusedElement);
        }
        finally
        {
            editItem?.CloseMenu();
            fileItem?.CloseMenu();
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void MenuFlyoutItem_DownArrow_ShouldMoveFocusToNextSibling()
    {
        ResetInputState();
        try
        {
            var first = new MenuFlyoutItem
            {
                Text = "First"
            };
            var second = new MenuFlyoutItem
            {
                Text = "Second"
            };
            var host = new StackPanel();
            host.Children.Add(first);
            host.Children.Add(second);
            ArrangeRoot(host);
            Assert.True(first.Focus());
            first.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Down, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Assert.Same(second, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    private static void ArrangeRoot(FrameworkElement root)
    {
        root.Measure(new Size(320, 120));
        root.Arrange(new Rect(0, 0, 320, 120));
    }

    private static string DescribeParents(Visual element)
    {
        var values = new List<string>();
        for (Visual? value = element; value != null; value = value.VisualParent)
            values.Add(value is MenuFlyoutPresenter presenter ? $"Presenter(target={presenter.OwnerTarget?.GetType().Name})" : value.GetType().Name);
        return string.Join(" > ", values);
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
    }
}
