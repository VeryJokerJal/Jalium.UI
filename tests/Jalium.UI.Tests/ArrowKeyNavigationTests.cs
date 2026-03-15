using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ArrowKeyNavigationTests
{
    [Fact]
    public void Window_RightAndLeftArrow_ShouldMoveFocusBetweenUnhandledControls()
    {
        ResetInputState();

        try
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            var left = new Button { Content = "Left", Width = 80, Height = 32 };
            var right = new Button { Content = "Right", Width = 80, Height = 32 };
            panel.Children.Add(left);
            panel.Children.Add(right);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 240,
                Height = 120,
                Content = panel
            };

            ArrangeWindow(window);
            Assert.True(left.Focus());

            InvokeKeyDown(window, Key.Right);
            Assert.Same(right, Keyboard.FocusedElement);

            InvokeKeyDown(window, Key.Left);
            Assert.Same(left, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void TreeView_ArrowKeys_ShouldFocusExpandAndCollapseItems()
    {
        ResetInputState();

        try
        {
            var treeView = new TreeView
            {
                Width = 320,
                Height = 240
            };

            var parent = new TreeViewItem { Header = "Parent" };
            var child = new TreeViewItem { Header = "Child" };
            var sibling = new TreeViewItem { Header = "Sibling" };
            parent.Items.Add(child);
            treeView.Items.Add(parent);
            treeView.Items.Add(sibling);

            ArrangeRoot(treeView);

            Assert.True(treeView.Focus());
            treeView.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Down, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Assert.Same(parent, Keyboard.FocusedElement);

            parent.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Right, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Assert.True(parent.IsExpanded);

            parent.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Right, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            AssertFocusedTreeItemHeader("Child");

            var focusedChild = Assert.IsType<TreeViewItem>(Keyboard.FocusedElement);
            focusedChild.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Left, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            AssertFocusedTreeItemHeader("Parent");

            var focusedParent = Assert.IsType<TreeViewItem>(Keyboard.FocusedElement);
            focusedParent.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Left, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            Assert.False(focusedParent.IsExpanded);

            focusedParent.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Down, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
            AssertFocusedTreeItemHeader("Sibling");
        }
        finally
        {
            ResetInputState();
        }
    }

    private static void ArrangeWindow(Window window)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    private static void ArrangeRoot(FrameworkElement root)
    {
        root.Measure(new Size(320, 240));
        root.Arrange(new Rect(0, 0, 320, 240));
    }

    private static void InvokeKeyDown(Window window, Key key)
    {
        var method = typeof(Window).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)(int)key, nint.Zero });
    }

    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
        Window.SetKeyStateProviderForTesting(null);
    }

    private static void AssertFocusedTreeItemHeader(string expectedHeader)
    {
        var focusedItem = Assert.IsType<TreeViewItem>(Keyboard.FocusedElement);
        Assert.Equal(expectedHeader, focusedItem.Header?.ToString());
    }
}
