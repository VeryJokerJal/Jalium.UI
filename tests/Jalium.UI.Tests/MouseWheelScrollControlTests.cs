using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MouseWheelScrollControlTests
{
    [Fact]
    public void Editor_PreciseDiagonalAndShiftPackets_PreserveNativeDistance()
    {
        var editor = CreateEditor();
        var view = Field<EditorView>(editor, "_view");
        editor.RaiseEvent(Wheel(31.25, -50));
        Assert.Equal(12.5, view.HorizontalOffset, 6);
        Assert.Equal(20, view.VerticalOffset, 6);
        Assert.False(Field<bool>(editor, "_isScrollAnimating"));

        editor.RaiseEvent(Wheel(25, 0, ModifierKeys.Shift));
        Assert.Equal(22.5, view.HorizontalOffset, 6);
        Assert.Equal(20, view.VerticalOffset, 6);
        editor.RaiseEvent(Wheel(0, -25, ModifierKeys.Shift));
        Assert.Equal(32.5, view.HorizontalOffset, 6);
        Assert.Equal(20, view.VerticalOffset, 6);
    }

    [Fact]
    public void Editor_TouchpadInterruptsWheelAnimation_AndFractionalPacketsAccumulate()
    {
        var editor = CreateEditor();
        var view = Field<EditorView>(editor, "_view");
        editor.RaiseEvent(Wheel(0, -120, precise: false));
        Assert.True(Field<bool>(editor, "_isScrollAnimating"));
        double start = view.VerticalOffset;
        for (int index = 0; index < 20; index++) editor.RaiseEvent(Wheel(0, -0.125));
        Assert.Equal(start + 1, view.VerticalOffset, 6);
        Assert.False(Field<bool>(editor, "_isScrollAnimating"));
    }

    [Fact]
    public void Editor_AtVerticalEnd_BubblesOnlyUnconsumedAxisToParent()
    {
        var editor = CreateEditor();
        var content = new Canvas { Width = 600, Height = 900 };
        content.Children.Add(editor);
        var outer = new ScrollViewer
        {
            Content = content, IsScrollInertiaEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Arrange(outer, 240, 160);
        editor.UpdateScrollBarsForTesting(new Size(200, 100));
        var view = Field<EditorView>(editor, "_view");
        view.VerticalOffset = 1e6;
        editor.UpdateScrollBarsForTesting(new Size(200, 100));
        var wheel = Wheel(25, -50);
        editor.RaiseEvent(wheel);
        Assert.True(wheel.Handled);
        Assert.Equal(10, view.HorizontalOffset, 6);
        Assert.Equal(0, outer.HorizontalOffset, 6);
        Assert.Equal(20, outer.VerticalOffset, 6);
    }

    [Fact]
    public void TextBox_FineDiagonalWheel_DoesNotJumpThreeLines()
    {
        var textBox = new TextBox { Text = LongText(), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
        Arrange(textBox);
        for (int index = 0; index < 20; index++) textBox.RaiseEvent(Wheel(0.125, -0.125));
        Assert.Equal(1, textBox.HorizontalOffset, 6);
        Assert.Equal(1, textBox.VerticalOffset, 6);
        textBox.RaiseEvent(Wheel(25, 0));
        Assert.Equal(11, textBox.HorizontalOffset, 6);
        Assert.Equal(1, textBox.VerticalOffset, 6);
        textBox.RaiseEvent(Wheel(0, 1e6));
        var atTop = Wheel(0, 1);
        textBox.RaiseEvent(atTop);
        Assert.False(atTop.Handled);
    }

    [Fact]
    public void RichTextBox_FineWheel_StopsAtDocumentEdges()
    {
        var textBox = new RichTextBox();
        textBox.Document.Blocks.Clear();
        for (int index = 0; index < 40; index++)
            textBox.Document.Blocks.Add(new Paragraph(new Run($"Line {index}")));
        Arrange(textBox);
        for (int index = 0; index < 20; index++) textBox.RaiseEvent(Wheel(0, -0.125));
        Assert.Equal(1, textBox.VerticalOffset, 6);
        var horizontal = Wheel(25, 0);
        textBox.RaiseEvent(horizontal);
        Assert.Equal(1, textBox.VerticalOffset, 6);
        Assert.False(horizontal.Handled);
        textBox.RaiseEvent(Wheel(0, -1e6));
        double end = textBox.VerticalOffset;
        textBox.RaiseEvent(Wheel(0, -25));
        Assert.Equal(end, textBox.VerticalOffset);
        var atEnd = Wheel(0, -25);
        textBox.RaiseEvent(atEnd);
        Assert.False(atEnd.Handled);
    }

    [Fact]
    public void WrappedTextBox_HorizontalWheelDoesNotPanWrappedLines()
    {
        var textBox = new TextBox { Text = LongText(), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        Arrange(textBox);
        var horizontal = Wheel(25, 0);
        textBox.RaiseEvent(horizontal);
        Assert.False(horizontal.Handled);
        Assert.Equal(0, textBox.HorizontalOffset);
        var diagonal = Wheel(25, -50);
        textBox.RaiseEvent(diagonal);
        Assert.Equal(0, textBox.HorizontalOffset);
        Assert.Equal(20, textBox.VerticalOffset, 6);
        Assert.False(diagonal.Handled);
        Assert.True(diagonal.IsVerticalDeltaHandled);
        Assert.False(diagonal.IsHorizontalDeltaHandled);
    }

    [Fact]
    public void DiffViewer_PreciseAxes_AndUnscrollableContent()
    {
        var diff = new DiffViewer { OriginalText = LongText(), ModifiedText = LongText(), ShowMinimap = false };
        Arrange(diff);
        diff.RaiseEvent(Wheel(25, -50));
        Assert.Equal(10, Field<double>(diff, "_scrollOffsetX"), 6);
        Assert.Equal(20, Field<double>(diff, "_scrollOffsetY"), 6);
        var empty = new DiffViewer();
        Arrange(empty);
        var wheel = Wheel(25, -50);
        empty.RaiseEvent(wheel);
        Assert.False(wheel.Handled);
        Assert.Equal(0, Field<double>(empty, "_scrollOffsetX"));
        Assert.Equal(0, Field<double>(empty, "_scrollOffsetY"));
    }

    [Fact]
    public void Terminal_FineVerticalWheel_LeavesHorizontalInputForParent()
    {
        var terminal = new Terminal { AutoSize = false, AutoStartShell = false };
        var view = new TerminalView(terminal);
        typeof(Terminal).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(terminal, view);
        for (int index = 0; index < 100; index++) terminal.WriteLine($"Line {index}");
        Arrange(view);
        terminal.SetLoadedState(true);
        try
        {
            view.SetVerticalOffset(100);
            for (int index = 0; index < 20; index++) terminal.RaiseEvent(Wheel(0, -0.125));
            Assert.Equal(101, view.VerticalOffset, 6);
            var horizontal = Wheel(25, 0);
            terminal.RaiseEvent(horizontal);
            Assert.Equal(101, view.VerticalOffset, 6);
            Assert.False(horizontal.Handled);
            view.SetVerticalOffset(0);
            var atTop = Wheel(0, 25);
            terminal.RaiseEvent(atTop);
            Assert.False(atTop.Handled);
        }
        finally { terminal.SetLoadedState(false); }
    }

    [Fact]
    public void HexEditor_AccumulatesSubRowMovement_AndUsesScrollbarEnd()
    {
        var editor = new HexEditor { Data = new byte[4096] };
        Arrange(editor);
        double height = Field<double>(editor, "_rowHeight");
        for (int index = 0; index < 4; index++)
            editor.RaiseEvent(Wheel(0, -height / 4 * 120 / 48));
        Assert.Equal((long)editor.BytesPerRow, Field<long>(editor, "_scrollOffset"));
        var horizontal = Wheel(25, 0);
        editor.RaiseEvent(horizontal);
        Assert.False(horizontal.Handled);
        Assert.Equal((long)editor.BytesPerRow, Field<long>(editor, "_scrollOffset"));
        editor.RaiseEvent(Wheel(0, -1e6));
        var bar = Field<ScrollBar>(editor, "_verticalScrollBar");
        Assert.Equal((long)bar.Maximum * editor.BytesPerRow, Field<long>(editor, "_scrollOffset"));
        var atEnd = Wheel(0, -25);
        editor.RaiseEvent(atEnd);
        Assert.False(atEnd.Handled);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    public void InvalidOrEmptyWheel_DoesNotScrollOrConsume(double horizontal, double vertical)
    {
        UIElement[] controls = [CreateEditor(), new DiffViewer(), new TextBox { Text = LongText() }, new RichTextBox(), new HexEditor { Data = new byte[4096] }, new Terminal()];
        foreach (var control in controls)
        {
            Arrange(control);
            var wheel = Wheel(horizontal, vertical);
            control.RaiseEvent(wheel);
            Assert.False(wheel.Handled);
        }
    }

    [Fact]
    public void Menu_PreciseWheel_IsConsumedOnceAcrossPreviewAndBubble()
    {
        var menu = new MenuPopupScrollHost();
        for (int index = 0; index < 16; index++)
            menu.ItemsPanel.Children.Add(new MenuFlyoutItem { Text = $"Item {index}" });
        Arrange(menu, 220, 120);
        var viewer = Field<ScrollViewer>(menu, "_scrollViewer");
        var button = Field<RepeatButton>(menu, "_scrollDownButton");
        var wheel = Wheel(0, -0.125);
        wheel.RoutedEvent = UIElement.PreviewMouseWheelEvent;
        button.RaiseEvent(wheel);
        wheel.RoutedEvent = UIElement.MouseWheelEvent;
        button.RaiseEvent(wheel);
        Assert.True(wheel.Handled);
        Assert.Equal(0.05, viewer.VerticalOffset, 6);
    }

    private static EditControl CreateEditor()
    {
        var editor = new EditControl { Text = LongText(), ShowMinimap = false, Width = 200, Height = 100 };
        Arrange(editor);
        editor.UpdateScrollBarsForTesting(new Size(200, 100));
        return editor;
    }

    private static string LongText() => string.Join('\n', Enumerable.Repeat(new string('x', 100), 80));
    private static void Arrange(UIElement element, double width = 200, double height = 100)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }
    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static MouseWheelEventArgs Wheel(double horizontal, double vertical,
        ModifierKeys modifiers = ModifierKeys.None, bool precise = true) => new(
        UIElement.MouseWheelEvent, new Point(20, 20), horizontal, vertical, precise,
        MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released, modifiers, 1);
}
