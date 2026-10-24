using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

// Staged until Core342 finishes; regression candidates, not a confirmed defect.
// These are managed input/layout contracts, not physical desktop acceptance.
[Collection("macOS Window globals")]
public sealed class ListBoxVirtualizedNavigationTests
{
    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void FocusOnlyEndReachesTheRealizedTargetAfterLayout(ModifierKeys primary)
    {
        using var host = new Host();
        Assert.Null(host.List.ItemContainerGenerator.ContainerFromIndex(499));
        host.Key(Key.End, primary);
        Assert.Equal(new object[] { "item-0" }, host.List.SelectedItems.Cast<object>());
        host.Layout();
        var last = Assert.IsType<ListBoxItem>(host.List.ItemContainerGenerator.ContainerFromIndex(499));
        Assert.Same(last, Keyboard.FocusedElement);
        host.Key(Key.Space, primary);
        Assert.Equal(new object[] { "item-0", "item-499" }, host.List.SelectedItems.Cast<object>());
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void QueuedNavigationKeepsThePendingLogicalTarget(ModifierKeys primary)
    {
        using var host = new Host();
        Assert.Null(host.List.ItemContainerGenerator.ContainerFromIndex(499));
        host.Key(Key.End, primary);
        // Multiple key packets may reach a window before its next layout pass.
        host.Key(Key.Up, primary);
        host.Layout();
        host.Key(Key.Space, primary);
        Assert.Equal(new object[] { "item-0", "item-498" }, host.List.SelectedItems.Cast<object>());
        Assert.Equal(498, host.List.SelectedIndex);
    }

    private sealed class InputList : ListBox { public Panel? HostPanel => ItemsHost; }

    private sealed class Host : IDisposable
    {
        private readonly DisplayedTestWindow _window;
        public InputList List { get; }
        public Host()
        {
            Keyboard.Initialize();
            Keyboard.ClearFocus();
            List = new InputList { SelectionMode = SelectionMode.Extended, Width = 360, Height = 180 };
            for (int i = 0; i < 500; i++) List.Items.Add("item-" + i);
            _window = new DisplayedTestWindow { Content = List };
            Layout();
            var first = Assert.IsType<ListBoxItem>(List.ItemContainerGenerator.ContainerFromIndex(0));
            first.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseDownEvent,
                new Point(10, 10), MouseButton.Left, MouseButtonState.Pressed, 1,
                MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0));
            Assert.Same(first, Keyboard.FocusedElement);
            Assert.Equal(0, List.SelectedIndex);
        }
        public void Layout()
        {
            _window.Measure(new Size(360, 180));
            _window.Arrange(new Rect(0, 0, 360, 180));
            if (List.HostPanel is { } panel)
            {
                panel.Measure(new Size(360, 180));
                panel.Arrange(new Rect(0, 0, 360, 180));
            }
            Dispatcher.GetForCurrentThread().ProcessQueue();
        }
        public void Key(Key key, ModifierKeys modifiers)
        {
            var args = new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 0);
            List.RaiseEvent(args);
            Assert.True(args.Handled);
        }
        public void Dispose()
        {
            Mouse.Capture(null);
            Keyboard.ClearFocus();
            _window.Close();
        }
    }
}

