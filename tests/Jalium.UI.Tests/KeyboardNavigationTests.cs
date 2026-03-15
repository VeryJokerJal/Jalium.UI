using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class KeyboardNavigationTests
{
    [Fact]
    public void MoveFocus_ShouldRespectTabIndexBeforeVisualOrder()
    {
        ResetInputState();

        try
        {
            var root = new StackPanel();
            var first = new FocusTarget();
            var second = new FocusTarget();
            var third = new FocusTarget();

            KeyboardNavigation.SetTabIndex(first, 1);
            KeyboardNavigation.SetTabIndex(second, 0);
            KeyboardNavigation.SetTabIndex(third, 1);

            root.Children.Add(first);
            root.Children.Add(second);
            root.Children.Add(third);
            ArrangeRoot(root);

            Assert.True(second.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(second));
            Assert.Same(first, Keyboard.FocusedElement);

            Assert.True(KeyboardNavigation.MoveFocus(first));
            Assert.Same(third, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void MoveFocus_WithCycleContainer_ShouldWrapWithinContainer()
    {
        ResetInputState();

        try
        {
            var root = new StackPanel();
            var before = new FocusTarget();
            var cycleContainer = new StackPanel();
            var first = new FocusTarget();
            var last = new FocusTarget();
            var after = new FocusTarget();

            KeyboardNavigation.SetTabNavigation(cycleContainer, KeyboardNavigationMode.Cycle);

            cycleContainer.Children.Add(first);
            cycleContainer.Children.Add(last);
            root.Children.Add(before);
            root.Children.Add(cycleContainer);
            root.Children.Add(after);
            ArrangeRoot(root);

            Assert.True(last.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(last));
            Assert.Same(first, Keyboard.FocusedElement);

            Assert.True(KeyboardNavigation.MoveFocus(first, reverse: true));
            Assert.Same(last, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void MoveFocus_WithContainedContainer_ShouldStopAtBoundary()
    {
        ResetInputState();

        try
        {
            var root = new StackPanel();
            var containedContainer = new StackPanel();
            var first = new FocusTarget();
            var last = new FocusTarget();

            KeyboardNavigation.SetTabNavigation(containedContainer, KeyboardNavigationMode.Contained);

            containedContainer.Children.Add(first);
            containedContainer.Children.Add(last);
            root.Children.Add(containedContainer);
            ArrangeRoot(root);

            Assert.True(last.Focus());
            Assert.False(KeyboardNavigation.MoveFocus(last));
            Assert.Same(last, Keyboard.FocusedElement);

            Assert.True(first.Focus());
            Assert.False(KeyboardNavigation.MoveFocus(first, reverse: true));
            Assert.Same(first, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void MoveFocus_ShouldSkipDescendantsOfNoneContainer()
    {
        ResetInputState();

        try
        {
            var root = new StackPanel();
            var before = new FocusTarget();
            var blockedContainer = new StackPanel();
            var blocked = new FocusTarget();
            var after = new FocusTarget();

            KeyboardNavigation.SetTabNavigation(blockedContainer, KeyboardNavigationMode.None);

            blockedContainer.Children.Add(blocked);
            root.Children.Add(before);
            root.Children.Add(blockedContainer);
            root.Children.Add(after);
            ArrangeRoot(root);

            Assert.True(before.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(before));
            Assert.Same(after, Keyboard.FocusedElement);

            Assert.True(blocked.Focus());
            Assert.False(KeyboardNavigation.MoveFocus(blocked));
            Assert.Same(blocked, Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    private static void ArrangeRoot(FrameworkElement root)
    {
        root.Measure(new Size(400, 400));
        root.Arrange(new Rect(0, 0, 400, 400));
    }

    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
    }

    private sealed class FocusTarget : Border
    {
        public FocusTarget()
        {
            Width = 40;
            Height = 24;
            Focusable = true;
        }
    }
}
