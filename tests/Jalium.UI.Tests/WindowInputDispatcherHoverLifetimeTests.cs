using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowInputDispatcherHoverLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyleSwapBeforeNextMouseMove_ClearsOldButtonHover(bool leaveWindow)
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var input = new WindowInputDispatcher(host);
        var (button, normal, selected) = CreateButton();
        var sibling = new Border();
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(sibling);
        int leaveCount = 0;
        button.MouseLeave += (_, _) => leaveCount++;

        try
        {
            var oldRoot = Assert.IsType<Border>(button.FindName("RootBorder"));
            Move(input, host, oldRoot, 1);
            Assert.True(button.IsMouseOver);

            // Navigation replaces Style inside Click, before the next mouse sample.
            button.Style = selected;
            button.ApplyTemplate();
            Assert.NotSame(oldRoot, button.FindName("RootBorder"));
            Assert.Null(oldRoot.VisualParent);

            if (leaveWindow)
                input.HandleMouseLeave();
            else
                Move(input, host, sibling, 2);

            button.Style = normal;
            Assert.False(button.IsMouseOver);
            Assert.False(oldRoot.IsMouseOver);
            Assert.Same(Brushes.Transparent, button.Background);
            Assert.Equal(1, leaveCount);
            Assert.Equal(!leaveWindow, panel.IsMouseOver);
            Assert.Equal(!leaveWindow, sibling.IsMouseOver);
        }
        finally
        {
            input.HandleMouseLeave();
        }
    }

    [Fact]
    public void DetachedTemplate_MoveToReplacement_KeepsCommonButtonHoveredWithoutDuplicateEnter()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var input = new WindowInputDispatcher(host);
        var (button, _, selected) = CreateButton();
        int enterCount = 0;
        int leaveCount = 0;
        button.MouseEnter += (_, _) => enterCount++;
        button.MouseLeave += (_, _) => leaveCount++;

        try
        {
            var oldRoot = Assert.IsType<Border>(button.FindName("RootBorder"));
            Move(input, host, oldRoot, 1);
            button.Style = selected;
            button.ApplyTemplate();
            var newRoot = Assert.IsType<Border>(button.FindName("RootBorder"));
            Move(input, host, newRoot, 2);

            Assert.False(oldRoot.IsMouseOver);
            Assert.True(newRoot.IsMouseOver);
            Assert.True(button.IsMouseOver);
            Assert.Equal(1, enterCount);
            Assert.Equal(0, leaveCount);
        }
        finally
        {
            input.HandleMouseLeave();
        }
    }

    [Fact]
    public void SameContentReparentedByTemplateSwap_RefreshesAncestorHoverOnNextMove()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var input = new WindowInputDispatcher(host);
        var (button, _, selected) = CreateButton();
        var content = new Border { Width = 12, Height = 12 };
        button.Content = content;
        button.Measure(new Size(100, 40));
        button.Arrange(new Rect(0, 0, 100, 40));
        int enters = 0;
        content.MouseEnter += (_, _) => enters++;

        try
        {
            var oldRoot = Assert.IsType<Border>(button.FindName("RootBorder"));
            Move(input, host, content, 1);

            button.Style = selected;
            button.ApplyTemplate();
            button.Measure(new Size(100, 40));
            button.Arrange(new Rect(0, 0, 100, 40));
            var newRoot = Assert.IsType<Border>(button.FindName("RootBorder"));
            Assert.NotSame(oldRoot, newRoot);

            // The hit-test leaf is identical; only its template ancestors changed.
            Move(input, host, content, 2);
            Assert.False(oldRoot.IsMouseOver);
            Assert.True(newRoot.IsMouseOver);
            Assert.True(content.IsMouseOver);
            Assert.True(button.IsMouseOver);
            Assert.Equal(1, enters);
        }
        finally
        {
            input.HandleMouseLeave();
        }
    }

    [Fact]
    public void SameLeafMovedBetweenParents_UpdatesOnlyChangedAncestors()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var input = new WindowInputDispatcher(host);
        var leaf = new Border();
        var first = new Border { Child = leaf };
        var second = new Border();
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        int leafEnters = 0;
        int panelLeaves = 0;
        leaf.MouseEnter += (_, _) => leafEnters++;
        panel.MouseLeave += (_, _) => panelLeaves++;

        try
        {
            Move(input, host, leaf, 1);
            first.Child = null;
            second.Child = leaf;
            Move(input, host, leaf, 2);

            Assert.False(first.IsMouseOver);
            Assert.True(second.IsMouseOver);
            Assert.True(leaf.IsMouseOver);
            Assert.True(panel.IsMouseOver);
            Assert.Equal(1, leafEnters);
            Assert.Equal(0, panelLeaves);
        }
        finally
        {
            input.HandleMouseLeave();
        }
    }

    [Fact]
    public void MouseLeaveHandlerRedirectsHover_OuterTransitionDoesNotOverwriteNewTarget()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var input = new WindowInputDispatcher(host);
        var first = new Border();
        var second = new Border();
        var replacement = new Border();
        int firstLeaves = 0;
        first.MouseLeave += (_, _) =>
        {
            firstLeaves++;
            if (firstLeaves == 1)
                input.UpdateMouseOverState(replacement, 3);
        };

        try
        {
            input.UpdateMouseOverState(first, 1);
            input.UpdateMouseOverState(second, 2);

            Assert.Equal(1, firstLeaves);
            Assert.Same(replacement, input.LastMouseOverElement);
            Assert.Same(replacement, UIElement.MouseDirectlyOverElement);
            Assert.True(replacement.IsMouseOver);
            Assert.False(first.IsMouseOver);
            Assert.False(second.IsMouseOver);
        }
        finally
        {
            input.HandleMouseLeave();
        }
    }

    private static (Button Button, Style Normal, Style Selected) CreateButton()
    {
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(() =>
        {
            var root = new Border { Name = "RootBorder", Child = new ContentPresenter() };
            root.SetTemplateBinding(Border.BackgroundProperty, Control.BackgroundProperty);
            return root;
        });
        var common = new Style(typeof(Button));
        common.Setters.Add(new Setter(Control.TemplateProperty, template));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Gray));
        common.Triggers.Add(hover);

        var normal = new Style(typeof(Button), common);
        normal.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var selected = new Style(typeof(Button), common);
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Blue));
        var button = new Button { Style = normal };
        button.ApplyTemplate();
        return (button, normal, selected);
    }

    private static void Move(WindowInputDispatcher input, CountingInputHost host, UIElement target, int timestamp)
    {
        host.HitTarget = target;
        input.HandleMouseMove(new Point(10, 10), MouseButtonStates.AllReleased, ModifierKeys.None, timestamp);
    }
}
