using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class TextBoxBaseParityTests
{
    [Fact]
    public void SelectionProperties_HaveWpfDefaultsAndStoreValues()
    {
        var textBox = new TextBox();
        var selectedTextBrush = new SolidColorBrush(Color.White);

        Assert.False(textBox.AutoWordSelection);
        Assert.False(textBox.IsReadOnlyCaretVisible);
        Assert.False(textBox.IsInactiveSelectionHighlightEnabled);
        Assert.False(textBox.IsSelectionActive);
        Assert.Equal(1.0, textBox.SelectionOpacity);
        Assert.Null(textBox.SelectionTextBrush);

        textBox.AutoWordSelection = true;
        textBox.IsReadOnlyCaretVisible = true;
        textBox.IsInactiveSelectionHighlightEnabled = true;
        textBox.SelectionOpacity = 0.4;
        textBox.SelectionTextBrush = selectedTextBrush;

        Assert.True(textBox.AutoWordSelection);
        Assert.True(textBox.IsReadOnlyCaretVisible);
        Assert.True(textBox.IsInactiveSelectionHighlightEnabled);
        Assert.Equal(0.4, textBox.SelectionOpacity);
        Assert.Same(selectedTextBrush, textBox.SelectionTextBrush);
        Assert.True(TextBoxBase.IsSelectionActiveProperty.ReadOnly);
    }

    [Fact]
    public void AppendText_AppendsAndMovesCaret()
    {
        var textBox = new TextBox { Text = "abc" };

        textBox.AppendText("def");
        textBox.AppendText(null!);

        Assert.Equal("abcdef", textBox.Text);
        Assert.Equal(6, textBox.CaretIndex);
        Assert.Equal(0, textBox.SelectionLength);
    }

    [Fact]
    public void UndoAndRedo_ReportWhetherAnOperationRan()
    {
        var textBox = new TextBox();

        Assert.False(textBox.Undo());
        Assert.False(textBox.Redo());
    }

    [Fact]
    public void ChangeBlock_CoalescesNestedEditsIntoOneUndoUnit()
    {
        var textBox = new TestTextBox();

        using (textBox.DeclareChangeBlock())
        {
            textBox.Insert("a");
            textBox.BeginChange();
            textBox.Insert("b");
            textBox.EndChange();
        }

        Assert.Equal("ab", textBox.Text);
        Assert.True(textBox.Undo());
        Assert.Equal(string.Empty, textBox.Text);
        Assert.False(textBox.Undo());
        Assert.True(textBox.Redo());
        Assert.Equal("ab", textBox.Text);
    }

    [Fact]
    public void EndChange_RejectsAnUnmatchedCall()
    {
        var textBox = new TextBox();

        Assert.Throws<InvalidOperationException>(textBox.EndChange);
    }

    [Fact]
    public void ScrollOffsetMethods_ValidateNaNAndUpdateFallbackOffsets()
    {
        var textBox = new TextBox();

        textBox.ScrollToHorizontalOffset(12);
        textBox.ScrollToVerticalOffset(8);

        Assert.Equal(12, textBox.HorizontalOffset);
        Assert.Equal(8, textBox.VerticalOffset);
        Assert.Throws<ArgumentOutOfRangeException>(() => textBox.ScrollToHorizontalOffset(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => textBox.ScrollToVerticalOffset(double.NaN));
    }

    [Fact]
    public void ProtectedEventHooks_RaiseTheSuppliedEvents()
    {
        var textBox = new TestTextBox();
        int selectionChanged = 0;
        int textChanged = 0;
        textBox.SelectionChanged += (_, _) => selectionChanged++;
        textBox.TextChanged += (_, _) => textChanged++;

        textBox.RaiseSelectionChanged();
        textBox.RaiseTextChanged();

        Assert.Equal(1, selectionChanged);
        Assert.Equal(1, textChanged);
    }

    private sealed class TestTextBox : TextBox
    {
        public void Insert(string text) => InsertText(text);

        public void RaiseSelectionChanged() =>
            OnSelectionChanged(new RoutedEventArgs(SelectionChangedEvent, this));

        public void RaiseTextChanged() =>
            OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
    }
}
