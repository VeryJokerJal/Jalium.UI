using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichUndoTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void StyledWordReplacement_UndoRedoPreservesFontGeometryAndOriginalNodes(bool privateFont, bool italic)
    {
        using var resource = privateFont
            ? CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc")) : null;
        if (privateFont) Assert.NotNull(resource);
        var run = new Run("MMMM iii1 abc אבג xyz tail")
        {
            FontFamily = new FontFamily(resource?.Family ?? "Helvetica Neue"), FontSize = 24,
            FontWeight = FontWeights.Black, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = new SolidColorBrush(Colors.DarkBlue)
        };
        Css.SetStyle(run, "font-width:100%;text-decoration:underline");
        var paragraph = new Paragraph(run) { Margin = new Thickness(0), TextIndent = 11 };
        var document = new FlowDocument(paragraph) { FontSize = 18 };
        var box = new RichTextBox(document) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var support = (IImeSupport)box;
        Layout(box);
        Assert.True(support.TrySetImeSelection(0, 4));
        Rect before = support.GetImeCaretRectangle();
        Assert.True(support.TryReplaceImeText(0, 4, "Window"));
        Assert.StartsWith("Window iii1", document.GetText());
        Layout(box);
        Rect replaced = support.GetImeCaretRectangle();

        Assert.True(box.Undo());
        Assert.Same(document, box.Document);
        Assert.Same(box, document.Parent);
        Assert.Same(paragraph, Assert.Single(document.Blocks));
        Assert.Same(run, Assert.Single(paragraph.Inlines));
        Assert.Equal("MMMM iii1 abc אבג xyz tail", run.Text);
        Assert.Equal(FontWeights.Black, run.FontWeight);
        Assert.Equal(italic ? FontStyles.Italic : FontStyles.Normal, run.FontStyle);
        Assert.Equal(24, run.FontSize);
        Assert.Equal("MMMM", box.Selection.Text);
        Layout(box);
        Assert.Equal(before, support.GetImeCaretRectangle());

        Assert.True(box.Redo());
        Assert.Same(document, box.Document);
        Assert.Same(run, Assert.Single(paragraph.Inlines));
        Assert.Equal("Window iii1 abc אבג xyz tail", run.Text);
        Layout(box);
        Assert.Equal(replaced, support.GetImeCaretRectangle());
    }

    [Fact]
    public void DeleteWholeRun_UndoRestoresSiblingOwnershipAndRedoRemovesItAgain()
    {
        var left = new Run("abc") { FontWeight = FontWeights.Bold };
        var middle = new Run("DEF") { FontStyle = FontStyles.Italic };
        var right = new Run("ghi") { FontSize = 23 };
        var paragraph = new Paragraph(left);
        paragraph.Inlines.Add(middle); paragraph.Inlines.Add(right);
        var box = new RichTextBox(new FlowDocument(paragraph));
        var support = (IImeSupport)box;
        Assert.True(support.TryReplaceImeText(3, 3, ""));
        Assert.Equal("abcghi" + Environment.NewLine, box.Document.GetText());
        Assert.Null(middle.Parent);
        Assert.True(box.Undo());
        Assert.Equal(new Inline[] { left, middle, right }, paragraph.Inlines);
        Assert.Same(paragraph, middle.Parent);
        Assert.Same(paragraph.Inlines, middle.SiblingInlines);
        Assert.Same(left, middle.PreviousInline); Assert.Same(right, middle.NextInline);
        Assert.Same(middle, left.NextInline); Assert.Same(middle, right.PreviousInline);
        Assert.True(box.Redo());
        Assert.Equal(new Inline[] { left, right }, paragraph.Inlines);
        Assert.Null(middle.Parent); Assert.Null(middle.SiblingInlines);
    }

    [Fact]
    public void NestedSpanEdit_UndoRetainsHyperlinkAndFormattingContext()
    {
        var run = new Run("linked text");
        var link = new Hyperlink(run) { NavigateUri = new Uri("https://example.com/window") };
        var bold = new Bold(link);
        var italic = new Italic(bold);
        var paragraph = new Paragraph(italic);
        var document = new FlowDocument(paragraph);
        var box = new RichTextBox(document);
        Assert.True(((IImeSupport)box).TryReplaceImeText(0, 6, "edited"));
        Assert.True(box.Undo());
        Assert.Same(document, box.Document);
        Assert.Same(italic, Assert.Single(paragraph.Inlines));
        Assert.Same(bold, Assert.Single(italic.Inlines));
        Assert.Same(link, Assert.Single(bold.Inlines));
        Assert.Same(run, Assert.Single(link.Inlines));
        Assert.Equal("linked text", run.Text);
        Assert.Equal(FontWeights.Bold, run.GetEffectiveFontWeight());
        Assert.Equal(FontStyles.Italic, run.GetEffectiveFontStyle());
        Assert.Equal(new Uri("https://example.com/window"), link.NavigateUri);
        Assert.True(box.Redo()); Assert.Equal("edited text", run.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParagraphBreak_PreservesOrderingFormattingAndUndoOwnership(bool nestedSection)
    {
        var run = new Run("abcd") { FontSize = 25, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
        var tail = new Run("ef") { FontSize = 19 };
        var paragraph = new Paragraph(run) { TextIndent = 12, Margin = new Thickness(0) };
        paragraph.Inlines.Add(tail);
        var section = new Section(paragraph);
        var document = nestedSection ? new FlowDocument(section) : new FlowDocument();
        if (!nestedSection) { section.Blocks.Remove(paragraph); document.Blocks.Add(paragraph); }
        var box = new EditingRichTextBox(document);
        Assert.True(((IImeSupport)box).TrySetImeSelection(2, 0));
        box.InsertBreak();
        var siblings = nestedSection ? section.Blocks : document.Blocks;
        Assert.Equal(2, siblings.Count);
        var second = Assert.IsType<Paragraph>(siblings[1]);
        Assert.Equal("ab" + Environment.NewLine + "cdef" + Environment.NewLine, document.GetText());
        var trailing = Assert.IsType<Run>(second.Inlines[0]);
        Assert.Equal(25, trailing.FontSize); Assert.Equal(FontWeights.Bold, trailing.FontWeight);
        Assert.Equal(FontStyles.Italic, trailing.FontStyle); Assert.Equal(12, second.TextIndent);
        Assert.Same(tail, second.Inlines[1]);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(box.Undo());
            Assert.Same(paragraph, Assert.Single(siblings));
            Assert.Equal(new Inline[] { run, tail }, paragraph.Inlines);
            Assert.Equal("abcdef" + Environment.NewLine, document.GetText());
            Assert.Same(paragraph.Inlines, tail.SiblingInlines);
            Assert.True(box.Redo());
            Assert.Same(second, siblings[1]); Assert.Same(trailing, second.Inlines[0]);
            Assert.Same(tail, second.Inlines[1]); Assert.Same(second.Inlines, tail.SiblingInlines);
            Assert.Equal("ab" + Environment.NewLine + "cdef" + Environment.NewLine, document.GetText());
        }
    }

    [Theory]
    [InlineData("bold")] [InlineData("italic")] [InlineData("underline")]
    [InlineData("family")] [InlineData("size")] [InlineData("foreground")]
    public void FormattingCommand_UndoRestoresInheritanceAndRedoRestoresLocalValue(string command)
    {
        var run = new Run("format me");
        var paragraph = new Paragraph(run) { FontSize = 21, FontWeight = FontWeights.Normal };
        var box = new RichTextBox(new FlowDocument(paragraph));
        Assert.True(((IImeSupport)box).TrySetImeSelection(0, 9));
        DependencyProperty property;
        switch (command)
        {
            case "bold": property = TextElement.FontWeightProperty; box.ToggleBold(); break;
            case "italic": property = TextElement.FontStyleProperty; box.ToggleItalic(); break;
            case "underline": property = TextElement.TextDecorationsProperty; box.ToggleUnderline(); break;
            case "family": property = TextElement.FontFamilyProperty; box.SetFontFamily("Menlo"); break;
            case "size": property = TextElement.FontSizeProperty; box.SetFontSize(31); break;
            default: property = TextElement.ForegroundProperty; box.SetForeground(new SolidColorBrush(Colors.Red)); break;
        }
        object? formatted = run.ReadLocalValue(property);
        Assert.NotSame(DependencyProperty.UnsetValue, formatted);
        Assert.True(box.Undo());
        Assert.Same(run, Assert.Single(paragraph.Inlines));
        Assert.Same(DependencyProperty.UnsetValue, run.ReadLocalValue(property));
        Assert.Equal(21, run.GetEffectiveFontSize());
        Assert.Equal("format me", box.Selection.Text);
        Assert.True(box.Redo()); Assert.Equal(formatted, run.ReadLocalValue(property));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PlainTextReplacement_UndoPreservesStructuredDocumentAndHostedControls(bool documentEnabled)
    {
        var listParagraph = new Paragraph(new Run("list item") { FontWeight = FontWeights.Bold });
        var item = new ListItem(listParagraph);
        var list = new Jalium.UI.Documents.List(item);
        var cellParagraph = new Paragraph(new Run("cell text") { FontSize = 27 });
        var cell = new TableCell(cellParagraph);
        var row = new TableRow(); row.Cells.Add(cell);
        var group = new TableRowGroup(); group.Rows.Add(row);
        var table = new Table(); table.RowGroups.Add(group);
        var column = new TableColumn { Width = new GridLength(110) }; table.Columns.Add(column);
        var button = new Button { Content = "inline action" };
        var inlineUi = new InlineUIContainer(button);
        var floater = new Floater(new Paragraph(new Run("floating text")));
        var paragraph = new Paragraph(new Run("body"));
        paragraph.Inlines.Add(inlineUi); paragraph.Inlines.Add(floater);
        var blockButton = new Button { Content = "block action" };
        var blockUi = new BlockUIContainer(blockButton);
        var section = new Section(paragraph); section.Blocks.Add(list); section.Blocks.Add(table); section.Blocks.Add(blockUi);
        var document = new FlowDocument(section);
        var box = new RichTextBox(document) { IsDocumentEnabled = documentEnabled };
        string original = document.GetText();
        box.SetPlainText("replacement");
        var replacement = box.Document;
        Assert.NotSame(document, replacement); Assert.Null(document.Parent); Assert.Same(box, replacement.Parent);
        Assert.True(button.IsEnabled); Assert.True(blockButton.IsEnabled);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(box.Undo()); Assert.Same(document, box.Document); Assert.Same(box, document.Parent);
            Assert.Null(replacement.Parent); Assert.Equal(original, document.GetText());
            Assert.Equal(new Block[] { paragraph, list, table, blockUi }, section.Blocks);
            Assert.Same(item, Assert.Single(list.ListItems)); Assert.Same(listParagraph, Assert.Single(item.Blocks));
            Assert.Same(column, Assert.Single(table.Columns)); Assert.Same(table, column.Parent);
            Assert.Same(group, Assert.Single(table.RowGroups)); Assert.Same(row, Assert.Single(group.Rows));
            Assert.Same(cell, Assert.Single(row.Cells)); Assert.Same(cellParagraph, Assert.Single(cell.Blocks));
            Assert.Same(button, inlineUi.Child); Assert.Same(blockButton, blockUi.Child);
            Assert.Same(paragraph, floater.Parent); Assert.Equal(documentEnabled, button.IsEnabled);
            Assert.Equal(documentEnabled, blockButton.IsEnabled);
            Assert.True(box.Redo()); Assert.Same(replacement, box.Document);
            Assert.Null(document.Parent); Assert.Same(box, replacement.Parent);
            Assert.True(button.IsEnabled); Assert.True(blockButton.IsEnabled);
        }
    }

    [Fact]
    public void BoundRun_UndoRedoKeepsBindingAndUpdatesTwoWaySource()
    {
        var source = new TextSource { Text = "MMMM tail" };
        var run = new Run();
        var binding = new Binding(nameof(TextSource.Text)) { Source = source, Mode = BindingMode.TwoWay };
        run.SetBinding(Run.TextProperty, binding);
        var box = new RichTextBox(new FlowDocument(new Paragraph(run)));
        Assert.True(((IImeSupport)box).TryReplaceImeText(0, 4, "Window"));
        Assert.Equal("Window tail", source.Text);
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", source.Text);
        Assert.Same(binding, BindingOperations.GetBindingBase(run, Run.TextProperty));
        Assert.True(box.Redo()); Assert.Equal("Window tail", source.Text);
        source.Text = "external update";
        Assert.Equal("external update" + Environment.NewLine, box.Document.GetText());
    }

    [Fact]
    public void AssigningAnotherDocument_ClearsHistoryAndAllowsOldDocumentReuse()
    {
        var document = new FlowDocument(new Paragraph(new Run("original")));
        var box = new RichTextBox(document);
        Assert.True(((IImeSupport)box).TryReplaceImeText(0, 0, "edit "));
        Assert.True(box.CanUndo);
        var replacement = new FlowDocument(new Paragraph(new Run("other")));
        box.Document = replacement;
        Assert.Null(document.Parent); Assert.Same(box, replacement.Parent);
        Assert.False(box.CanUndo); Assert.False(box.CanRedo);
        var otherBox = new RichTextBox(document);
        Assert.False(box.Undo()); Assert.Same(otherBox, document.Parent);
    }

    [Fact]
    public void UndoLimitAndNewEdit_PreserveStylesAndDiscardOnlyTheExpectedHistory()
    {
        var run = new Run("abc") { FontWeight = FontWeights.Bold };
        var document = new FlowDocument(new Paragraph(run));
        var box = new RichTextBox(document) { UndoLimit = 2 };
        var support = (IImeSupport)box;
        Assert.True(support.TryReplaceImeText(3, 0, "d"));
        Assert.True(support.TryReplaceImeText(4, 0, "e"));
        Assert.True(support.TryReplaceImeText(5, 0, "f"));
        Assert.True(box.Undo()); Assert.Equal("abcde", run.Text);
        Assert.True(box.Undo()); Assert.Equal("abcd", run.Text);
        Assert.False(box.Undo()); Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.True(support.TryReplaceImeText(4, 0, "!"));
        Assert.False(box.CanRedo); Assert.False(box.Redo());
        Assert.True(box.Undo()); Assert.Equal("abcd", run.Text); Assert.Same(document, box.Document);
    }

    [Fact]
    public void ReversedSelection_UndoPreservesOriginalPointersAndAffinity()
    {
        var run = new Run("abc אבג tail") { FontSize = 23 };
        var document = new FlowDocument(new Paragraph(run));
        var box = new RichTextBox(document);
        var anchor = document.GetPositionAtOffset(7, LogicalDirection.Backward)!;
        var moving = document.GetPositionAtOffset(4, LogicalDirection.Forward)!;
        box.Selection.Select(anchor, moving); box.CaretPosition = moving;
        Assert.True(((IImeSupport)box).TryReplaceImeText(4, 3, "word"));
        Assert.True(box.Undo());
        Assert.Same(anchor, box.Selection.AnchorPosition);
        Assert.Same(moving, box.Selection.MovingPosition); Assert.Same(moving, box.CaretPosition);
        Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var selection));
        Assert.Equal(4, selection.CursorIndex); Assert.Equal(7, selection.AnchorIndex);
        Assert.Equal("אבג", box.Selection.Text);
    }

    [Fact]
    public void InitiallyEmptyDocument_UndoRedoDoesNotCreateExtraParagraphs()
    {
        var document = new FlowDocument();
        var box = new RichTextBox(document);
        Assert.True(((IImeSupport)box).TryReplaceImeText(0, 0, "text"));
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
        var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
        for (int i = 0; i < 4; i++)
        {
            Assert.True(box.Undo()); Assert.Same(document, box.Document);
            Assert.Empty(document.Blocks); Assert.Equal("", document.GetText());
            Assert.True(box.Redo()); Assert.Same(paragraph, Assert.Single(document.Blocks));
            Assert.Same(run, Assert.Single(paragraph.Inlines));
            Assert.Equal("text" + Environment.NewLine, document.GetText());
        }
    }

    [Fact]
    public void Undo_CannotMutateADocumentReusedByAnotherEditor()
    {
        var run = new Run("original") { FontSize = 23 };
        var document = new FlowDocument(new Paragraph(run));
        var box = new RichTextBox(document);
        box.SetPlainText("replacement");
        var replacement = box.Document;
        var otherBox = new RichTextBox(document);
        run.FontSize = 31;
        Assert.False(box.Undo()); Assert.Same(replacement, box.Document);
        Assert.Same(otherBox, document.Parent); Assert.Equal(31, run.FontSize);
        Assert.True(box.CanUndo); Assert.False(box.CanRedo);
        otherBox.Document = new FlowDocument();
        Assert.True(box.Undo()); Assert.Same(document, box.Document);
        Assert.Same(box, document.Parent); Assert.Equal(23, run.FontSize);
    }

    [Fact]
    public void Undo_CannotStealARunReparentedIntoAnotherDocument()
    {
        var run = new Run("original") { FontWeight = FontWeights.Bold };
        var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph);
        var box = new RichTextBox(document);
        Assert.True(((IImeSupport)box).TryReplaceImeText(0, 0, "edit "));
        paragraph.Inlines.Remove(run);
        var otherParagraph = new Paragraph(run);
        var otherBox = new RichTextBox(new FlowDocument(otherParagraph));
        run.FontWeight = FontWeights.Normal;
        Assert.False(box.Undo()); Assert.Empty(paragraph.Inlines);
        Assert.Same(otherParagraph, run.Parent); Assert.Equal("edit original", run.Text);
        Assert.Equal(FontWeights.Normal, run.FontWeight); Assert.True(box.CanUndo); Assert.False(box.CanRedo);
        otherParagraph.Inlines.Remove(run);
        Assert.True(box.Undo()); Assert.Same(run, Assert.Single(paragraph.Inlines));
        Assert.Equal("edit original", run.Text); Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.True(box.Undo());
        Assert.Equal("original", run.Text); Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.Same(otherBox, otherBox.Document.Parent);
    }

    private static void Layout(RichTextBox box)
    {
        box.Measure(new Size(650, 220)); box.Arrange(new Rect(0, 0, 650, 220));
    }

    private sealed class EditingRichTextBox(FlowDocument document) : RichTextBox(document)
    {
        internal void InsertBreak() => InsertParagraphBreak();
    }

    public sealed class TextSource : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text
        {
            get => _text;
            set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
