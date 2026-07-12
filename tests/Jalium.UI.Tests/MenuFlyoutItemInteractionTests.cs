using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MenuFlyoutItemInteractionTests
{
    [Fact]
    public void ClickingMenuFlyoutItem_ShouldCloseContainingFlyout()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();

        try
        {
            var command = new MenuFlyoutItem { Text = "Open" };
            var fileItem = new MenuBarItem { Title = "File" };
            fileItem.Items.Add(command);

            var menuBar = new MenuBar();
            menuBar.Items.Add(fileItem);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 120,
                Content = menuBar
            };

            ArrangeWindow(window);

            fileItem.OpenMenuAndFocusFirstItem();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.True(fileItem.IsMenuOpen);

            command.RaiseEvent(CreateMouseDown(new Point(4, 4)));
            Dispatcher.GetForCurrentThread().ProcessQueue();

            Assert.False(fileItem.IsMenuOpen);
        }
        finally
        {
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void ClickingToggleMenuFlyoutItem_ShouldToggleIsCheckedAndCloseFlyout()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();

        try
        {
            var toggle = new ToggleMenuFlyoutItem { Text = "Show Grid", IsChecked = false };
            var viewItem = new MenuBarItem { Title = "View" };
            viewItem.Items.Add(toggle);

            var menuBar = new MenuBar();
            menuBar.Items.Add(viewItem);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 120,
                Content = menuBar
            };

            ArrangeWindow(window);

            viewItem.OpenMenuAndFocusFirstItem();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.True(viewItem.IsMenuOpen);
            Assert.False(toggle.IsChecked);

            int clickCount = 0;
            bool isCheckedAtClick = false;
            toggle.Click += (_, _) =>
            {
                clickCount++;
                isCheckedAtClick = toggle.IsChecked;
            };

            toggle.RaiseEvent(CreateMouseDown(new Point(4, 4)));
            Dispatcher.GetForCurrentThread().ProcessQueue();

            Assert.Equal(1, clickCount);
            Assert.True(isCheckedAtClick);
            Assert.True(toggle.IsChecked);
            Assert.False(viewItem.IsMenuOpen);
        }
        finally
        {
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void ClickingNestedMenuFlyoutItem_ShouldCloseEntireMenuChain()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();

        try
        {
            var leafCommand = new MenuFlyoutItem { Text = "Rename" };
            var refactorSubItem = new MenuFlyoutSubItem { Text = "Refactor" };
            refactorSubItem.Items.Add(leafCommand);

            var editItem = new MenuBarItem { Title = "Edit" };
            editItem.Items.Add(refactorSubItem);

            var menuBar = new MenuBar();
            menuBar.Items.Add(editItem);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 120,
                Content = menuBar
            };

            ArrangeWindow(window);

            editItem.OpenMenuAndFocusFirstItem();
            refactorSubItem.ShowSubMenu();
            Dispatcher.GetForCurrentThread().ProcessQueue();

            var subPopup = GetPrivateField<Popup>(typeof(MenuFlyoutSubItem), refactorSubItem, "_subPopup");
            Assert.True(editItem.IsMenuOpen);
            Assert.True(subPopup.IsOpen);

            leafCommand.RaiseEvent(CreateMouseDown(new Point(4, 4)));
            Dispatcher.GetForCurrentThread().ProcessQueue();

            Assert.False(subPopup.IsOpen);
            Assert.False(editItem.IsMenuOpen);
        }
        finally
        {
            ResetInputState();
            ResetApplicationState();
        }
    }

    [Fact]
    public void ClickingMenuFlyoutSubItem_ShouldNotCloseParentMenu()
    {
        ResetApplicationState();
        ResetInputState();
        _ = new Application();
        MenuBarItem? editItem = null;
        MenuFlyoutSubItem? refactorSubItem = null;

        try
        {
            var leafCommand = new MenuFlyoutItem { Text = "Rename" };
            refactorSubItem = new MenuFlyoutSubItem { Text = "Refactor" };
            refactorSubItem.Items.Add(leafCommand);

            editItem = new MenuBarItem { Title = "Edit" };
            editItem.Items.Add(refactorSubItem);

            var menuBar = new MenuBar();
            menuBar.Items.Add(editItem);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 120,
                Content = menuBar
            };

            ArrangeWindow(window);

            editItem.OpenMenuAndFocusFirstItem();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Assert.True(editItem.IsMenuOpen);

            refactorSubItem.RaiseEvent(CreateMouseDown(new Point(4, 4)));
            Dispatcher.GetForCurrentThread().ProcessQueue();

            Assert.True(editItem.IsMenuOpen);
            var subPopup = GetPrivateField<Popup>(typeof(MenuFlyoutSubItem), refactorSubItem, "_subPopup");
            Assert.True(subPopup.IsOpen);
        }
        finally
        {
            refactorSubItem?.HideSubMenu();
            editItem?.CloseMenu();
            ResetInputState();
            ResetApplicationState();
        }
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

    private static void ArrangeWindow(Window window)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    private static T GetPrivateField<T>(Type ownerType, object owner, string fieldName)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(owner);
        Assert.NotNull(value);
        return (T)value!;
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
