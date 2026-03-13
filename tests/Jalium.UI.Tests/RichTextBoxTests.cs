using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Reflection;

namespace Jalium.UI.Tests;

public class RichTextBoxTests
{
    #region Document Tests

    [Fact]
    public void FlowDocument_DefaultConstructor_CreatesEmptyDocument()
    {
        // Arrange & Act
        var doc = new FlowDocument();

        // Assert
        Assert.NotNull(doc.Blocks);
        Assert.Empty(doc.Blocks);
    }

    [Fact]
    public void FlowDocument_FromText_CreatesParagraphs()
    {
        // Arrange
        var text = "Line 1\nLine 2\nLine 3";

        // Act
        var doc = FlowDocument.FromText(text);

        // Assert
        Assert.Equal(3, doc.Blocks.Count);
        Assert.All(doc.Blocks, block => Assert.IsType<Paragraph>(block));
    }

    [Fact]
    public void FlowDocument_GetText_ReturnsPlainText()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello")));
        doc.Blocks.Add(new Paragraph(new Run("World")));

        // Act
        var text = doc.GetText();

        // Assert
        Assert.Contains("Hello", text);
        Assert.Contains("World", text);
    }

    [Fact]
    public void FlowDocument_ContentStart_ReturnsValidPointer()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Test")));

        // Act
        var start = doc.ContentStart;

        // Assert
        Assert.NotNull(start);
        Assert.Same(doc, start.Document);
    }

    [Fact]
    public void FlowDocument_ContentEnd_ReturnsValidPointer()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Test")));

        // Act
        var end = doc.ContentEnd;

        // Assert
        Assert.NotNull(end);
        Assert.Same(doc, end.Document);
    }

    [Fact]
    public void FlowDocument_GetPositionAtOffset_ReturnsValidPointer()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello")));

        // Act
        var position = doc.GetPositionAtOffset(2, LogicalDirection.Forward);

        // Assert
        Assert.NotNull(position);
        Assert.Equal(2, position.DocumentOffset);
    }

    #endregion

    #region TextPointer Tests

    [Fact]
    public void TextPointer_DocumentOffset_ReturnsCorrectValue()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello World")));

        // Act
        var position = doc.GetPositionAtOffset(5, LogicalDirection.Forward);

        // Assert
        Assert.NotNull(position);
        Assert.Equal(5, position.DocumentOffset);
    }

    [Fact]
    public void TextPointer_CompareTo_ReturnsCorrectOrder()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello World")));

        var pos1 = doc.GetPositionAtOffset(2, LogicalDirection.Forward);
        var pos2 = doc.GetPositionAtOffset(5, LogicalDirection.Forward);

        // Assert
        Assert.NotNull(pos1);
        Assert.NotNull(pos2);
        Assert.True(pos1.CompareTo(pos2) < 0);
        Assert.True(pos2.CompareTo(pos1) > 0);
    }

    [Fact]
    public void TextPointer_GetNextInsertionPosition_MovesForward()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Test")));
        var start = doc.ContentStart;

        // Act
        var next = start.GetNextInsertionPosition(LogicalDirection.Forward);

        // Assert
        Assert.NotNull(next);
        Assert.True(next.DocumentOffset > start.DocumentOffset);
    }

    [Fact]
    public void TextPointer_Equals_ReturnsTrueForSamePosition()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Test")));

        var pos1 = doc.GetPositionAtOffset(2, LogicalDirection.Forward);
        var pos2 = doc.GetPositionAtOffset(2, LogicalDirection.Forward);

        // Assert
        Assert.NotNull(pos1);
        Assert.NotNull(pos2);
        Assert.Equal(pos1, pos2);
    }

    #endregion

    #region TextRange Tests

    [Fact]
    public void TextRange_Constructor_SetsStartAndEnd()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello World")));
        var start = doc.GetPositionAtOffset(0, LogicalDirection.Forward);
        var end = doc.GetPositionAtOffset(5, LogicalDirection.Forward);

        // Act
        var range = new TextRange(start!, end!);

        // Assert
        Assert.Equal(start, range.Start);
        Assert.Equal(end, range.End);
    }

    [Fact]
    public void TextRange_IsEmpty_ReturnsTrueForEmptyRange()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Test")));
        var start = doc.ContentStart;

        // Act
        var range = new TextRange(start, start);

        // Assert
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void TextRange_Text_ReturnsSelectedText()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello World")));
        var start = doc.GetPositionAtOffset(0, LogicalDirection.Forward);
        var end = doc.GetPositionAtOffset(5, LogicalDirection.Forward);

        // Act
        var range = new TextRange(start!, end!);

        // Assert
        Assert.Equal("Hello", range.Text);
    }

    [Fact]
    public void TextRange_Contains_ReturnsTrueForPositionInRange()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello World")));
        var start = doc.GetPositionAtOffset(0, LogicalDirection.Forward);
        var end = doc.GetPositionAtOffset(10, LogicalDirection.Forward);
        var middle = doc.GetPositionAtOffset(5, LogicalDirection.Forward);

        var range = new TextRange(start!, end!);

        // Assert
        Assert.True(range.Contains(middle!));
    }

    [Fact]
    public void TextRange_Contains_ReturnsFalseForPositionOutsideRange()
    {
        // Arrange
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Hello World")));
        var start = doc.GetPositionAtOffset(0, LogicalDirection.Forward);
        var end = doc.GetPositionAtOffset(5, LogicalDirection.Forward);
        var outside = doc.GetPositionAtOffset(8, LogicalDirection.Forward);

        var range = new TextRange(start!, end!);

        // Assert
        Assert.False(range.Contains(outside!));
    }

    #endregion

    #region RichTextBox Control Tests

    [Fact]
    public void RichTextBox_DefaultConstructor_CreatesEmptyDocument()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.NotNull(rtb.Document);
        Assert.NotNull(rtb.CaretPosition);
        Assert.NotNull(rtb.Selection);
    }

    [Fact]
    public void RichTextBox_DocumentProperty_CanBeSet()
    {
        // Arrange
        var rtb = new RichTextBox();
        var doc = new FlowDocument(new Paragraph(new Run("Test")));

        // Act
        rtb.Document = doc;

        // Assert
        Assert.Same(doc, rtb.Document);
    }

    [Fact]
    public void RichTextBox_GetText_ReturnsDocumentText()
    {
        // Arrange
        var doc = FlowDocument.FromText("Hello World");
        var rtb = new RichTextBox(doc);

        // Act
        var text = rtb.GetText();

        // Assert
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public void RichTextBox_SetText_UpdatesDocument()
    {
        // Arrange
        var rtb = new RichTextBox();

        // Act
        rtb.SetText("New Text");

        // Assert
        var text = rtb.GetText();
        Assert.Contains("New Text", text);
    }

    [Fact]
    public void RichTextBox_IsReadOnly_DefaultsToFalse()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.False(rtb.IsReadOnly);
    }

    [Fact]
    public void RichTextBox_IsReadOnly_CanBeSet()
    {
        // Arrange
        var rtb = new RichTextBox();

        // Act
        rtb.IsReadOnly = true;

        // Assert
        Assert.True(rtb.IsReadOnly);
    }

    [Fact]
    public void RichTextBox_SelectAll_SelectsAllContent()
    {
        // Arrange
        var doc = FlowDocument.FromText("Hello World");
        var rtb = new RichTextBox(doc);

        // Act
        rtb.SelectAll();

        // Assert
        Assert.False(rtb.Selection.IsEmpty);
    }

    [Fact]
    public void RichTextBox_ClearSelection_EmptiesSelection()
    {
        // Arrange
        var doc = FlowDocument.FromText("Hello World");
        var rtb = new RichTextBox(doc);
        rtb.SelectAll();

        // Act
        rtb.ClearSelection();

        // Assert
        Assert.True(rtb.Selection.IsEmpty);
    }

    [Fact]
    public void RichTextBox_SelectionBrush_CanBeSet()
    {
        // Arrange
        var rtb = new RichTextBox();
        var brush = new SolidColorBrush(Color.Red);

        // Act
        rtb.SelectionBrush = brush;

        // Assert
        Assert.Same(brush, rtb.SelectionBrush);
    }

    [Fact]
    public void RichTextBox_CaretBrush_CanBeSet()
    {
        // Arrange
        var rtb = new RichTextBox();
        var brush = new SolidColorBrush(Color.Blue);

        // Act
        rtb.CaretBrush = brush;

        // Assert
        Assert.Same(brush, rtb.CaretBrush);
    }

    [Fact]
    public void RichTextBox_Focus_ShouldRegisterImeTarget()
    {
        InputMethod.SetTarget(null);
        Keyboard.ClearFocus();

        try
        {
            var rtb = new RichTextBox();
            var updateFocusMethod = typeof(UIElement).GetMethod("UpdateIsKeyboardFocused", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateFocusMethod);

            updateFocusMethod!.Invoke(rtb, [true]);
            Assert.Same(rtb, InputMethod.Current);

            updateFocusMethod.Invoke(rtb, [false]);
            Assert.Null(InputMethod.Current);
        }
        finally
        {
            InputMethod.CancelComposition();
            InputMethod.SetTarget(null);
            Keyboard.ClearFocus();
        }
    }

    [Fact]
    public void RichTextBox_ImeCompositionState_ShouldToggleAndAdvanceCaretWindow()
    {
        var rtb = new RichTextBox
        {
            Width = 320,
            Height = 120
        };
        rtb.SetText("hello");
        rtb.CaretPosition = rtb.Document.ContentEnd;
        rtb.Measure(new Size(320, 120));
        rtb.Arrange(new Rect(0, 0, 320, 120));

        rtb.OnImeCompositionStart();
        Assert.True(rtb.IsImeComposing);

        var startPoint = rtb.GetImeCaretPosition();
        rtb.OnImeCompositionUpdate("zhong", 2);
        var updatedPoint = rtb.GetImeCaretPosition();

        Assert.True(updatedPoint.X > startPoint.X);

        rtb.OnImeCompositionEnd("中");
        Assert.False(rtb.IsImeComposing);
    }

    [Fact]
    public void RichTextBox_ImeCommit_ShouldInsertTextViaTextInput()
    {
        var rtb = new RichTextBox
        {
            Width = 320,
            Height = 120
        };
        rtb.Measure(new Size(320, 120));
        rtb.Arrange(new Rect(0, 0, 320, 120));

        rtb.OnImeCompositionStart();
        rtb.OnImeCompositionUpdate("zhong", 2);
        rtb.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "中", 1));
        rtb.OnImeCompositionEnd("中");

        Assert.Contains("中", rtb.GetText());
        Assert.False(rtb.IsImeComposing);
    }

    [Fact]
    public void RichTextBox_ImeComposition_ShouldNotHandleEnterAsEditorInput()
    {
        var rtb = new RichTextBox();
        rtb.SetText("hello");
        rtb.OnImeCompositionStart();
        var before = rtb.GetText();

        var keyArgs = new KeyEventArgs(UIElement.KeyDownEvent, Key.Enter, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 1);
        rtb.RaiseEvent(keyArgs);

        Assert.False(keyArgs.Handled);
        Assert.Equal(before, rtb.GetText());
        rtb.OnImeCompositionEnd(null);
    }

    [Fact]
    public void RichTextBox_ImeComposition_ShouldNotHandleCtrlFormattingShortcut()
    {
        var rtb = new RichTextBox();
        rtb.SetText("hello");
        rtb.OnImeCompositionStart();
        var before = rtb.GetText();

        var keyArgs = new KeyEventArgs(UIElement.KeyDownEvent, Key.B, ModifierKeys.Control, isDown: true, isRepeat: false, timestamp: 1);
        rtb.RaiseEvent(keyArgs);

        Assert.False(keyArgs.Handled);
        Assert.Equal(before, rtb.GetText());
        rtb.OnImeCompositionEnd(null);
    }

    #endregion

    #region TextElement Tests

    [Fact]
    public void Run_Text_CanBeSet()
    {
        // Arrange
        var run = new Run();

        // Act
        run.Text = "Hello";

        // Assert
        Assert.Equal("Hello", run.Text);
    }

    [Fact]
    public void Run_FontWeight_CanBeSet()
    {
        // Arrange
        var run = new Run("Test");

        // Act
        run.FontWeight = FontWeights.Bold;

        // Assert
        Assert.Equal(FontWeights.Bold, run.FontWeight);
    }

    [Fact]
    public void Run_FontStyle_CanBeSet()
    {
        // Arrange
        var run = new Run("Test");

        // Act
        run.FontStyle = FontStyles.Italic;

        // Assert
        Assert.Equal(FontStyles.Italic, run.FontStyle);
    }

    [Fact]
    public void Paragraph_Inlines_CanContainMultipleRuns()
    {
        // Arrange
        var paragraph = new Paragraph();

        // Act
        paragraph.Inlines.Add(new Run("Hello"));
        paragraph.Inlines.Add(new Run(" "));
        paragraph.Inlines.Add(new Run("World"));

        // Assert
        Assert.Equal(3, paragraph.Inlines.Count);
    }

    [Fact]
    public void Bold_AppliesBoldWeight()
    {
        // Arrange & Act
        var bold = new Bold(new Run("Test"));

        // Assert
        Assert.Equal(FontWeights.Bold, bold.FontWeight);
    }

    [Fact]
    public void Italic_AppliesItalicStyle()
    {
        // Arrange & Act
        var italic = new Italic(new Run("Test"));

        // Assert
        Assert.Equal(FontStyles.Italic, italic.FontStyle);
    }

    [Fact]
    public void Underline_AppliesUnderlineDecoration()
    {
        // Arrange & Act
        var underline = new Underline(new Run("Test"));

        // Assert
        Assert.NotNull(underline.TextDecorations);
        Assert.True(underline.TextDecorations.HasDecoration(TextDecorationLocation.Underline));
    }

    #endregion

    #region TextDecoration Tests

    [Fact]
    public void TextDecoration_DefaultLocation_IsUnderline()
    {
        // Arrange
        var decoration = new TextDecoration { Location = TextDecorationLocation.Underline };

        // Assert
        Assert.Equal(TextDecorationLocation.Underline, decoration.Location);
    }

    [Fact]
    public void TextDecorationCollection_HasDecoration_ReturnsTrue_WhenContainsLocation()
    {
        // Arrange
        var collection = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline }
        };

        // Assert
        Assert.True(collection.HasDecoration(TextDecorationLocation.Underline));
        Assert.False(collection.HasDecoration(TextDecorationLocation.Strikethrough));
    }

    [Fact]
    public void TextDecorationCollection_RemoveDecoration_RemovesCorrectLocation()
    {
        // Arrange
        var collection = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline },
            new TextDecoration { Location = TextDecorationLocation.Strikethrough }
        };

        // Act
        collection.RemoveDecoration(TextDecorationLocation.Underline);

        // Assert
        Assert.False(collection.HasDecoration(TextDecorationLocation.Underline));
        Assert.True(collection.HasDecoration(TextDecorationLocation.Strikethrough));
    }

    [Fact]
    public void TextDecorations_Underline_ReturnsUnderlineCollection()
    {
        // Act
        var decorations = TextDecorations.Underline;

        // Assert
        Assert.NotEmpty(decorations);
        Assert.True(decorations.HasDecoration(TextDecorationLocation.Underline));
    }

    [Fact]
    public void TextDecorations_Strikethrough_ReturnsStrikethroughCollection()
    {
        // Act
        var decorations = TextDecorations.Strikethrough;

        // Assert
        Assert.NotEmpty(decorations);
        Assert.True(decorations.HasDecoration(TextDecorationLocation.Strikethrough));
    }

    [Fact]
    public void Run_TextDecorations_CanBeSet()
    {
        // Arrange
        var run = new Run("Test");

        // Act
        run.TextDecorations = TextDecorations.Underline;

        // Assert
        Assert.NotNull(run.TextDecorations);
        Assert.True(run.TextDecorations.HasDecoration(TextDecorationLocation.Underline));
    }

    #endregion

    #region Undo/Redo Tests

    [Fact]
    public void RichTextBox_CanUndo_InitiallyFalse()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.False(rtb.CanUndo);
    }

    [Fact]
    public void RichTextBox_CanRedo_InitiallyFalse()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.False(rtb.CanRedo);
    }

    [Fact]
    public void RichTextBox_UndoLimit_DefaultsTo100()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.Equal(100, rtb.UndoLimit);
    }

    [Fact]
    public void RichTextBox_IsUndoEnabled_DefaultsToTrue()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.True(rtb.IsUndoEnabled);
    }

    #endregion

    #region Scroll Tests

    [Fact]
    public void RichTextBox_HorizontalOffset_DefaultsToZero()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.Equal(0, rtb.HorizontalOffset);
    }

    [Fact]
    public void RichTextBox_VerticalOffset_DefaultsToZero()
    {
        // Arrange & Act
        var rtb = new RichTextBox();

        // Assert
        Assert.Equal(0, rtb.VerticalOffset);
    }

    [Fact]
    public void RichTextBox_HorizontalOffset_CanBeSet()
    {
        // Arrange
        var rtb = new RichTextBox();

        // Act
        rtb.HorizontalOffset = 50;

        // Assert
        Assert.Equal(50, rtb.HorizontalOffset);
    }

    [Fact]
    public void RichTextBox_VerticalOffset_CanBeSet()
    {
        // Arrange
        var rtb = new RichTextBox();

        // Act
        rtb.VerticalOffset = 100;

        // Assert
        Assert.Equal(100, rtb.VerticalOffset);
    }

    [Fact]
    public void RichTextBox_HorizontalOffset_CannotBeNegative()
    {
        // Arrange
        var rtb = new RichTextBox();

        // Act
        rtb.HorizontalOffset = -50;

        // Assert
        Assert.Equal(0, rtb.HorizontalOffset);
    }

    [Fact]
    public void RichTextBox_VerticalOffset_CannotBeNegative()
    {
        // Arrange
        var rtb = new RichTextBox();

        // Act
        rtb.VerticalOffset = -100;

        // Assert
        Assert.Equal(0, rtb.VerticalOffset);
    }

    #endregion
}
