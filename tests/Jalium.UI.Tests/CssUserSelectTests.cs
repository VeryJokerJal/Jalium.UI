using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssUserSelectTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("text")]
    [InlineData("none")]
    [InlineData("contain")]
    [InlineData("all")]
    public void SupportsTheCssUiKeywordGrammar(string value)
        => Assert.True(Css.Supports("user-select", value));

    [Theory]
    [InlineData("inheritance")]
    [InlineData("auto text")]
    [InlineData("none, text")]
    public void RejectsInvalidKeywordGrammar(string value)
        => Assert.False(Css.Supports("user-select", value));

    [Fact]
    public void TextBlockCssTextEnablesDragSelectionAndNoneBlocksIt()
    {
        var text = new TextBlock { Text = "Hello world", Width = 220, Height = 32 };
        var root = Host(text);
        Layout(root);
        Drag(text);
        Assert.Equal(0, text.SelectionLength);

        Css.SetStyle(text, "user-select:text");
        Layout(root);
        Drag(text);
        Assert.Equal("Hello world", text.SelectedText);

        text.ClearSelection();
        Css.SetStyle(text, "user-select:none");
        Layout(root);
        Drag(text);
        Assert.Equal(0, text.SelectionLength);
    }

    [Fact]
    public void AutoFollowsNoneOrAllAncestorAndTextOverridesIt()
    {
        var text = new TextBlock { Text = "Selectable", Width = 220, Height = 32 };
        var root = Host(text);
        Css.SetStyle(root, "user-select:none");
        Css.SetStyle(text, "user-select:auto");
        Layout(root);
        Assert.Equal(CssUserSelect.None, CssUserSelectProperties.Resolve(text));
        Drag(text);
        Assert.Equal(0, text.SelectionLength);

        Css.SetStyle(text, "user-select:text");
        Layout(root);
        Assert.Equal(CssUserSelect.Text, CssUserSelectProperties.Resolve(text));
        Drag(text);
        Assert.Equal("Selectable", text.SelectedText);

        text.ClearSelection();
        Css.SetStyle(root, "user-select:all");
        Css.SetStyle(text, "user-select:auto");
        Layout(root);
        Assert.Equal(CssUserSelect.All, CssUserSelectProperties.Resolve(text));
        text.RaiseEvent(MouseDown(new Point(5, 12)));
        Assert.Equal("Selectable", text.SelectedText);
    }

    [Fact]
    public void ContainSelectsWithinTheNativeTextOwner()
    {
        var text = new TextBlock { Text = "Bounded range", Width = 220, Height = 32 };
        var root = Host(text);
        Css.SetStyle(text, "user-select:contain");
        Layout(root);
        Drag(text);
        Assert.Equal("Bounded range", text.SelectedText);
    }

    [Fact]
    public void CssWideKeywordsFollowTheComputedAndUsedValueRules()
    {
        var text = new TextBlock { Text = "Wide keywords", Width = 220, Height = 32 };
        var root = Host(text);
        Css.SetStyle(root, "user-select:none");
        Css.SetStyle(text, "user-select:inherit");
        Layout(root);
        Assert.Equal(CssUserSelect.None, CssUserSelectProperties.Resolve(text));

        Css.SetStyle(root, "user-select:all");
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Layout(root);
        Assert.Equal(CssUserSelect.All, CssUserSelectProperties.Resolve(text));

        Css.SetStyle(text, "user-select:unset");
        Layout(root);
        Assert.Equal(CssUserSelect.All, CssUserSelectProperties.Resolve(text));

        Css.SetStyle(text, "user-select:initial");
        Layout(root);
        Assert.Equal(CssUserSelect.All, CssUserSelectProperties.Resolve(text));
    }

    [Fact]
    public void NativeLocalSelectionSettingKeepsPriority()
    {
        var text = new TextBlock { Text = "Native value", Width = 220, Height = 32,
            IsTextSelectionEnabled = false };
        var root = Host(text);
        Css.SetStyle(text, "user-select:text");
        Layout(root);
        Drag(text);
        Assert.Equal(0, text.SelectionLength);

        text.IsTextSelectionEnabled = true;
        Css.SetStyle(text, "user-select:none");
        Layout(root);
        Drag(text);
        Assert.Equal("Native value", text.SelectedText);
    }

    [Fact]
    public void AncestorChangeStopsActiveDragWithoutClearingExistingSelection()
    {
        var text = new TextBlock { Text = "Keep selection", Width = 220, Height = 32 };
        var root = Host(text);
        Css.SetStyle(root, "user-select:text");
        Layout(root);
        Drag(text);
        Assert.Equal("Keep selection", text.SelectedText);

        Css.SetStyle(root, "user-select:none");
        Layout(root);
        text.RaiseEvent(MouseDown(new Point(5, 12)));
        Assert.Equal("Keep selection", text.SelectedText);

        Css.SetStyle(root, "user-select:text");
        Layout(root);
        text.ClearSelection();
        text.RaiseEvent(MouseDown(new Point(5, 12)));
        Assert.True(text.IsMouseCaptured);
        Css.SetStyle(root, "user-select:none");
        Layout(root);
        Assert.False(text.IsMouseCaptured);
    }

    [Fact]
    public void LabelDirectTextUsesCssAllOnSingleClick()
    {
        var label = new Label { Content = "Copy label", Width = 220, Height = 32,
            Template = new ControlTemplate(typeof(Label)) };
        var root = Host(label);
        Css.SetStyle(label, "user-select:all");
        Layout(root);
        label.RaiseEvent(MouseDown(new Point(5, 12)));
        Assert.Equal("Copy label", label.SelectedText);
    }

    [Fact]
    public void LabelTemplateTextUsesAncestorCssSelection()
    {
        var template = new ControlTemplate(typeof(Label));
        template.SetVisualTree(static () => new Border
        {
            Name = "LabelBorder",
            Child = new TextBlock { Text = "Template label", Width = 220, Height = 32 },
        });
        var label = new Label { Content = "Template label", Width = 220, Height = 32,
            Template = template };
        var root = Host(label);
        Css.SetStyle(label, "user-select:all");
        Layout(root);
        var border = Assert.IsType<Border>(label.FindName("LabelBorder"));
        var text = Assert.IsType<TextBlock>(border.Child);
        text.RaiseEvent(MouseDown(new Point(5, 12)));
        Assert.Equal("Template label", label.SelectedText);

        Css.SetStyle(label, "user-select:none");
        Layout(root);
        Assert.Equal("Template label", label.SelectedText);
        text.RaiseEvent(MouseDown(new Point(5, 12)));
        Assert.Equal("Template label", label.SelectedText);
    }

    [Fact]
    public void TemplateTextBlockNativeSelectionSettingKeepsPriority()
    {
        var template = new ControlTemplate(typeof(Label));
        template.SetVisualTree(static () => new Border
        {
            Name = "LabelBorder",
            Child = new TextBlock { Text = "Template local", Width = 220, Height = 32 },
        });
        var label = new Label { Content = "Template local", Width = 220, Height = 32,
            Template = template };
        var root = Host(label);
        Layout(root);
        var border = Assert.IsType<Border>(label.FindName("LabelBorder"));
        var text = Assert.IsType<TextBlock>(border.Child);
        text.IsTextSelectionEnabled = true;
        Assert.True(text.HasLocalValue(TextBlock.IsTextSelectionEnabledProperty));
        Css.SetStyle(label, "user-select:none");
        Layout(root);
        Assert.True(text.HasLocalValue(TextBlock.IsTextSelectionEnabledProperty));
        Drag(text);
        Assert.Equal("Template local", label.SelectedText);
    }

    [Fact]
    public void NativeCursorSetWhileHoveredKeepsPriority()
    {
        var text = new TextBlock { Text = "Cursor", Width = 220, Height = 32 };
        var label = new Label { Content = "Label cursor", Width = 220, Height = 32,
            Template = new ControlTemplate(typeof(Label)) };
        var root = new StackPanel { Width = 240, Height = 80 };
        root.Children.Add(text);
        root.Children.Add(label);
        Css.SetStyle(text, "user-select:text");
        Css.SetStyle(label, "user-select:text");
        root.Measure(new Size(240, 80));
        root.Arrange(new Rect(0, 0, 240, 80));

        text.RaiseEvent(MouseEnter(new Point(5, 12)));
        label.RaiseEvent(MouseEnter(new Point(5, 12)));
        Assert.Same(Cursors.IBeam, text.Cursor);
        Assert.Same(Cursors.IBeam, label.Cursor);

        text.Cursor = Cursors.Hand;
        label.Cursor = Cursors.Hand;
        text.RaiseEvent(MouseEnter(new Point(5, 12)));
        label.RaiseEvent(MouseEnter(new Point(5, 12)));
        Assert.Same(Cursors.Hand, text.Cursor);
        Assert.Same(Cursors.Hand, label.Cursor);

        Css.SetStyle(text, "user-select:none");
        Css.SetStyle(label, "user-select:none");
        Assert.Same(Cursors.Hand, text.Cursor);
        Assert.Same(Cursors.Hand, label.Cursor);
    }

    [Fact]
    public void AuthoredCssCursorKeepsPriorityOverSelectionCursor()
    {
        var text = new TextBlock { Text = "Cursor", Width = 220, Height = 32 };
        var label = new Label { Content = "Label cursor", Width = 220, Height = 32,
            Template = new ControlTemplate(typeof(Label)) };
        var root = new StackPanel { Width = 240, Height = 80 };
        root.Children.Add(text);
        root.Children.Add(label);
        Css.SetStyle(text, "user-select:text;cursor:pointer");
        Css.SetStyle(label, "user-select:text;cursor:pointer");
        root.Measure(new Size(240, 80));
        root.Arrange(new Rect(0, 0, 240, 80));

        text.RaiseEvent(MouseEnter(new Point(5, 12)));
        label.RaiseEvent(MouseEnter(new Point(5, 12)));
        Assert.Same(Cursors.Hand, text.Cursor);
        Assert.Same(Cursors.Hand, label.Cursor);
        Assert.False(text.HasLocalValue(FrameworkElement.CursorProperty));
        Assert.False(label.HasLocalValue(FrameworkElement.CursorProperty));

        Css.SetStyle(text, "user-select:none;cursor:pointer");
        Css.SetStyle(label, "user-select:none;cursor:pointer");
        Assert.Same(Cursors.Hand, text.Cursor);
        Assert.Same(Cursors.Hand, label.Cursor);
    }

    [Fact]
    public void AllowDiscreteTransitionChangesSelectionAtMidpoint()
    {
        var text = new TextBlock { Text = "Transition", Width = 220, Height = 32 };
        var root = Host(text);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(text, "user-select:none;transition:user-select 1s linear allow-discrete");
            Layout(root);
            Css.SetStyle(text, "user-select:text;transition:user-select 1s linear allow-discrete");
            Layout(root);
            Assert.True(text.HasAutomaticTransition(CssUserSelectProperties.ValueProperty));
            Assert.Equal(CssUserSelect.None, CssUserSelectProperties.Resolve(text));
            Drag(text);
            Assert.Equal(0, text.SelectionLength);

            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Equal(CssUserSelect.Text, CssUserSelectProperties.Resolve(text));
            Drag(text);
            Assert.Equal("Transition", text.SelectedText);
        }
        finally
        {
            text.StopAutomaticTransition(CssUserSelectProperties.ValueProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    private static StackPanel Host(UIElement child)
    {
        var root = new StackPanel { Width = 240, Height = 48 };
        root.Children.Add(child);
        return root;
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(240, 48));
        root.Arrange(new Rect(0, 0, 240, 48));
    }

    private static void Drag(TextBlock text)
    {
        text.RaiseEvent(MouseDown(new Point(0, 12)));
        text.RaiseEvent(new MouseEventArgs(UIElement.MouseMoveEvent, new Point(210, 12),
            MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 2));
        text.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseUpEvent, new Point(210, 12),
            MouseButton.Left, MouseButtonState.Released, 1, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, ModifierKeys.None, 3));
    }

    private static MouseButtonEventArgs MouseDown(Point position) => new(
        UIElement.MouseDownEvent, position, MouseButton.Left, MouseButtonState.Pressed, 1,
        MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1);

    private static MouseEventArgs MouseEnter(Point position) => new(
        UIElement.MouseEnterEvent, position, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
        MouseButtonState.Released, ModifierKeys.None, 1);
}
