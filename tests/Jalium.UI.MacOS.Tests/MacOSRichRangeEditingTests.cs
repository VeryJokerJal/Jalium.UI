using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichRangeEditingTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectAllReplacement_RemovesEveryParagraphAndRestoresTheOriginalTree(bool nested)
    {
        var first = new Run("first") { FontSize = 27, FontStyle = FontStyles.Italic };
        var second = new Run("second") { FontWeight = FontWeights.Bold };
        var paragraphs = new[] { new Paragraph(first), new Paragraph(second), new Paragraph(new Run("third")) };
        var document = new FlowDocument();
        var section = new Section();
        if (nested) { document.Blocks.Add(section); foreach (var p in paragraphs) section.Blocks.Add(p); }
        else foreach (var p in paragraphs) document.Blocks.Add(p);
        var box = new Editor(document); box.SelectAll();
        Assert.Equal(document.GetText(), box.Selection.Text);
        var events = Observe(box);
        box.Type("Window");
        Assert.Equal("Window\n", document.GetText()); Assert.Equal(6, box.CaretPosition!.DocumentOffset);
        Assert.Equal(27, Assert.IsType<Run>(box.CaretPosition.Parent).GetEffectiveFontSize());
        Assert.Equal(FontStyles.Italic, Assert.IsType<Run>(box.CaretPosition.Parent).GetEffectiveFontStyle());
        Assert.Single(events); Assert.True(box.Selection.IsEmpty);
        Assert.True(box.Undo()); Assert.Equal("first\nsecond\nthird\n", document.GetText());
        Assert.Equal(paragraphs.Cast<Block>(), nested ? section.Blocks : document.Blocks);
        Assert.Same(first, paragraphs[0].Inlines[0]); Assert.Equal(document.GetText(), box.Selection.Text);
        Assert.False(box.CanUndo); Assert.True(box.Redo()); Assert.Equal("Window\n", document.GetText());
    }

    [Theory]
    [InlineData("")] [InlineData("X")]
    public void CrossParagraphReplacement_KeepsBothSurvivingStylesAndOneUndoUnit(string replacement)
    {
        var prefix = new Run("abc") { FontWeight = FontWeights.Bold };
        var discarded = new Run("middle");
        var suffix = new Run("XYZ");
        var italic = new Italic(suffix);
        var first = new Paragraph(prefix) { FontSize = 19, Margin = new Thickness(1) };
        var middle = new Paragraph(discarded);
        var last = new Paragraph(italic) { FontSize = 31, Foreground = Brushes.DarkBlue };
        var document = new FlowDocument(); document.Blocks.Add(first); document.Blocks.Add(middle); document.Blocks.Add(last);
        var box = new Editor(document); var ime = (IImeSupport)box; ime.TrySetImeSelection(2, 10);
        var observations = new List<(string Text, int Caret, string Selection)>();
        box.TextChanged += (_, _) => observations.Add((document.GetText(), box.CaretPosition!.DocumentOffset, box.Selection.Text));
        Assert.True(ime.TryReplaceImeText(2, 10, replacement));
        Assert.Equal($"ab{replacement}YZ\n", document.GetText()); Assert.Same(first, Assert.Single(document.Blocks));
        Assert.Same(italic, suffix.Parent); Assert.Same(first, italic.Parent);
        Assert.Equal(31, suffix.GetEffectiveFontSize()); Assert.Equal(FontStyles.Italic, suffix.GetEffectiveFontStyle());
        Assert.Equal(FontWeights.Bold, prefix.GetEffectiveFontWeight()); Assert.Same(Brushes.DarkBlue, suffix.GetEffectiveForeground());
        Assert.Equal(new[] { ($"ab{replacement}YZ\n", 2 + replacement.Length, "") }, observations);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(box.Undo()); Assert.Equal("abc\nmiddle\nXYZ\n", document.GetText());
            Assert.Equal(new Block[] { first, middle, last }, document.Blocks);
            Assert.Same(prefix, first.Inlines[0]); Assert.Same(italic, last.Inlines[0]); Assert.Same(suffix, italic.Inlines[0]);
            Assert.Equal("c\nmiddle\nX", box.Selection.Text); Assert.False(box.CanUndo);
            Assert.True(box.Redo()); Assert.Equal($"ab{replacement}YZ\n", document.GetText());
            Assert.Equal(31, suffix.GetEffectiveFontSize());
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParagraphBoundaryDelete_JoinsTheParagraphsInEitherDirection(bool backward)
    {
        var first = new Paragraph(new Run("abc")); var second = new Paragraph(new Run("DEF") { FontSize = 31 });
        var document = new FlowDocument(); document.Blocks.Add(first); document.Blocks.Add(second);
        var box = new Editor(document); ((IImeSupport)box).TrySetImeSelection(backward ? 4 : 3, 0);
        box.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, backward ? Key.Back : Key.Delete,
            ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 1));
        Assert.Equal("abcDEF\n", document.GetText()); Assert.Same(first, Assert.Single(document.Blocks));
        Assert.Equal(3, box.CaretPosition!.DocumentOffset); Assert.True(box.Undo());
        Assert.Equal("abc\nDEF\n", document.GetText()); Assert.Same(second, document.Blocks[1]);
        Assert.False(box.CanUndo); Assert.True(box.Redo()); Assert.Equal("abcDEF\n", document.GetText());
    }

    [Theory]
    [InlineData(0)] [InlineData(2)] [InlineData(4)]
    public void ParagraphBreakInsideNestedSpans_PreservesStructureAndTypingFormat(int offset)
    {
        var run = new Run("abcd") { FontSize = 29 };
        var italic = new Italic(run); var bold = new Bold(italic);
        bold.Typography.Kerning = false;
        var paragraph = new Paragraph(bold) { TextAlignment = TextAlignment.Right, Margin = new Thickness(3) };
        var document = new FlowDocument(paragraph); var box = new Editor(document);
        ((IImeSupport)box).TrySetImeSelection(offset, 0);
        var events = Observe(box); box.Break();
        Assert.Equal("abcd"[..offset] + "\n" + "abcd"[offset..] + "\n", document.GetText());
        Assert.Equal(2, document.Blocks.Count); var next = Assert.IsType<Paragraph>(document.Blocks[1]);
        Assert.Equal(TextAlignment.Right, next.TextAlignment); Assert.Equal(new Thickness(3), next.Margin);
        Assert.Equal(offset + 1, box.CaretPosition!.DocumentOffset); Assert.Single(events);
        box.Type("Q");
        var typedRun = Assert.IsType<Run>(box.CaretPosition!.Parent);
        Assert.Equal(29, typedRun.GetEffectiveFontSize()); Assert.Equal(FontWeights.Bold, typedRun.GetEffectiveFontWeight());
        Assert.Equal(FontStyles.Italic, typedRun.GetEffectiveFontStyle()); Assert.False(typedRun.Typography.Kerning);
        Assert.True(box.Undo()); Assert.True(box.Undo()); Assert.Same(paragraph, Assert.Single(document.Blocks));
        Assert.Same(bold, paragraph.Inlines[0]); Assert.Same(italic, bold.Inlines[0]); Assert.Same(run, italic.Inlines[0]);
        Assert.Equal("abcd", run.Text); Assert.True(box.Redo()); Assert.True(box.Redo());
    }

    [Theory]
    [InlineData("A\nB", "A\nB", 2)]
    [InlineData("A\r\nB", "A\nB", 2)]
    [InlineData("A\rB", "A\nB", 2)]
    [InlineData("A\n\nB\n", "A\n\nB\n", 4)]
    public void MultilineInsertion_CreatesParagraphsAndPreservesTheStyledSuffix(string input, string normalized, int paragraphCount)
    {
        var run = new Run("abcd") { FontSize = 29 }; var italic = new Italic(run); var bold = new Bold(italic);
        var original = new Paragraph(bold) { Margin = new Thickness(2) };
        var document = new FlowDocument(original); var box = new Editor(document);
        ((IImeSupport)box).TrySetImeSelection(2, 0); var events = Observe(box); box.Type(input);
        Assert.Equal("ab" + normalized + "cd\n", document.GetText()); Assert.Equal(paragraphCount, document.Blocks.Count);
        Assert.Equal(2 + normalized.Length, box.CaretPosition!.DocumentOffset); Assert.Single(events);
        foreach (var paragraph in document.Blocks.Cast<Paragraph>())
        {
            Assert.Equal(new Thickness(2), paragraph.Margin);
            foreach (var textRun in Runs(paragraph.Inlines))
            {
                Assert.DoesNotContain('\n', textRun.Text); Assert.DoesNotContain('\r', textRun.Text);
                Assert.Equal(29, textRun.GetEffectiveFontSize()); Assert.Equal(FontWeights.Bold, textRun.GetEffectiveFontWeight());
                Assert.Equal(FontStyles.Italic, textRun.GetEffectiveFontStyle());
            }
        }
        for (int i = 0; i < 3; i++)
        {
            Assert.True(box.Undo()); Assert.Same(original, Assert.Single(document.Blocks));
            Assert.Same(bold, original.Inlines[0]); Assert.Same(italic, bold.Inlines[0]); Assert.Same(run, italic.Inlines[0]);
            Assert.Equal("abcd", run.Text); Assert.False(box.CanUndo); Assert.True(box.Redo());
            Assert.Equal("ab" + normalized + "cd\n", document.GetText());
        }
    }

    [Fact]
    public void DirectTextRangeReplacement_SelectsInsertedTextAndCommitsOnce()
    {
        var left = new Run("abc") { FontWeight = FontWeights.Bold };
        var right = new Run("DEF") { FontStyle = FontStyles.Italic };
        var paragraph = new Paragraph(left); paragraph.Inlines.Add(right);
        var document = new FlowDocument(paragraph); var box = new Editor(document);
        var range = new TextRange(document.GetPositionAtOffset(2, LogicalDirection.Forward)!,
            document.GetPositionAtOffset(5, LogicalDirection.Backward)!);
        var events = Observe(box); range.Text = "X\nY";
        Assert.Equal("abX\nYF\n", document.GetText()); Assert.Equal("X\nY", range.Text);
        Assert.Equal(2, range.Start.DocumentOffset); Assert.Equal(5, range.End.DocumentOffset); Assert.Single(events);
        Assert.True(box.Undo()); Assert.Equal("abcDEF\n", document.GetText());
        Assert.Same(left, paragraph.Inlines[0]); Assert.Same(right, paragraph.Inlines[1]); Assert.False(box.CanUndo);
        Assert.True(box.Redo()); Assert.Equal("abX\nYF\n", document.GetText());
    }

    [Fact]
    public void DeletingNestedInlineContent_RemovesSelectedLineBreaksAndKeepsUnselectedNodes()
    {
        var first = new Run("abc"); var last = new Run("DEF") { FontStyle = FontStyles.Italic };
        var lineBreak = new LineBreak(); var bold = new Bold(first); bold.Inlines.Add(lineBreak); bold.Inlines.Add(last);
        var paragraph = new Paragraph(bold); var document = new FlowDocument(paragraph); var box = new Editor(document);
        ((IImeSupport)box).TrySetImeSelection(2, 3); var events = Observe(box);
        Assert.True(((IImeSupport)box).TryReplaceImeText(2, 3, ""));
        Assert.Equal("abEF\n", document.GetText()); Assert.DoesNotContain(lineBreak, bold.Inlines);
        Assert.Same(bold, last.Parent); Assert.Single(events); Assert.True(box.Undo());
        Assert.Equal(new Inline[] { first, lineBreak, last }, bold.Inlines); Assert.Equal("abc\nDEF\n", document.GetText());
        Assert.False(box.CanUndo); Assert.True(box.Redo()); Assert.Equal("abEF\n", document.GetText());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CrossContainerDelete_PrunesOnlyEmptySelectedContainers(bool listItems)
    {
        var first = new Paragraph(new Run("abc")); var lastRun = new Run("DEF"); var last = new Paragraph(lastRun);
        var after = new Paragraph(new Run("tail")); var document = new FlowDocument();
        var sectionA = new Section(first); var sectionB = new Section(last); sectionB.Blocks.Add(after);
        var list = new Jalium.UI.Documents.List(); var itemA = new ListItem(); var itemB = new ListItem();
        if (listItems)
        {
            sectionA.Blocks.Remove(first); sectionB.Blocks.Remove(last); sectionB.Blocks.Remove(after);
            itemA.Blocks.Add(first); itemB.Blocks.Add(last); itemB.Blocks.Add(after);
            list.ListItems.Add(itemA); list.ListItems.Add(itemB); document.Blocks.Add(list);
        }
        else
        {
            document.Blocks.Add(sectionA); document.Blocks.Add(sectionB);
        }
        var box = new Editor(document); Assert.True(((IImeSupport)box).TryReplaceImeText(2, 4, ""));
        Assert.Equal("abF\ntail\n", document.GetText()); Assert.Same(first, lastRun.Parent);
        Assert.Equal(listItems ? (BlockCollection)itemB.Blocks : sectionB.Blocks, after.SiblingBlocks);
        Assert.True(box.Undo()); Assert.Equal("abc\nDEF\ntail\n", document.GetText()); Assert.Same(last, after.PreviousBlock);
        Assert.True(box.Redo()); Assert.Equal("abF\ntail\n", document.GetText());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HyperlinkMultilineInput_DoesNotSplitTheLinkOrFlattenItsContents(bool throughRange)
    {
        var run = new Run("abcd") { FontSize = 29 }; var link = new Hyperlink(run) { NavigateUri = new Uri("https://example.com/") };
        var paragraph = new Paragraph(link); var document = new FlowDocument(paragraph); var box = new Editor(document);
        var events = Observe(box);
        if (throughRange) new TextRange(document.GetPositionAtOffset(2, LogicalDirection.Forward)!,
            document.GetPositionAtOffset(2, LogicalDirection.Forward)!).Text = "X\nY";
        else Assert.True(((IImeSupport)box).TryReplaceImeText(2, 0, "X\nY"));
        Assert.Equal("abX Ycd\n", document.GetText()); Assert.Same(paragraph, Assert.Single(document.Blocks));
        Assert.Same(link, paragraph.Inlines[0]); Assert.Same(run, Assert.Single(link.Inlines)); Assert.Single(events);
        Assert.True(box.Undo()); Assert.Equal("abcd", run.Text); Assert.False(box.CanUndo); Assert.True(box.Redo());
        Assert.Equal("abX Ycd", run.Text);
    }

    [Theory]
    [InlineData("A\nB", "A\nB\n", 2)]
    [InlineData("A\n", "A\n", 1)]
    public void EmptyDocumentMultilineInput_UsesTheImplicitFinalParagraph(string input, string expected, int paragraphs)
    {
        var document = new FlowDocument(); var box = new Editor(document); var events = Observe(box); box.Type(input);
        Assert.Equal(expected, document.GetText()); Assert.Equal(paragraphs, document.Blocks.Count);
        Assert.All(Runs(document.Blocks.Cast<Paragraph>().SelectMany(p => p.Inlines)), r => Assert.DoesNotContain('\n', r.Text));
        Assert.Single(events); Assert.True(box.Undo()); Assert.Empty(document.Blocks); Assert.False(box.CanUndo);
        Assert.True(box.Redo()); Assert.Equal(expected, document.GetText()); Assert.Equal(paragraphs, document.Blocks.Count);
    }

    [Fact]
    public void DocumentFinalPosition_HasTheExactPlainTextOffsetAndSelectAllIncludesTheFinalBreak()
    {
        var section = new Section(new Paragraph(new Run("abc"))); section.Blocks.Add(new Paragraph(new Run("DEF")));
        var document = new FlowDocument(section); var box = new Editor(document);
        for (int i = 0; i <= document.GetText().Length; i++)
            Assert.Equal(i, document.GetPositionAtOffset(i, LogicalDirection.Forward)!.DocumentOffset);
        Assert.Equal(document.GetText().Length, document.ContentEnd.DocumentOffset);
        box.SelectAll(); Assert.Equal(document.GetText(), box.Selection.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WholeDocumentRangeWithATrailingNewline_DoesNotAddAnExtraParagraph(bool initiallyEmpty)
    {
        var document = initiallyEmpty ? new FlowDocument() : FlowDocument.FromText("before\nsecond");
        var box = new Editor(document); var range = new TextRange(document.ContentStart, document.ContentEnd);
        var events = Observe(box); range.Text = "A\r\n";
        Assert.Equal("A\n", document.GetText()); Assert.Single(document.Blocks); Assert.Equal("A\n", range.Text);
        Assert.Single(events); Assert.True(box.Undo());
        Assert.Equal(initiallyEmpty ? "" : "before\nsecond\n", document.GetText()); Assert.False(box.CanUndo);
        Assert.True(box.Redo()); Assert.Equal("A\n", document.GetText());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DeletingIntoAnEmptyContainer_RemovesItsShellAndUndoRestoresItsOwners(bool listItems)
    {
        var first = new Paragraph(new Run("abc")); var last = new Paragraph(new Run("DEF"));
        var document = new FlowDocument(); var sectionA = new Section(); var sectionB = new Section();
        var list = new Jalium.UI.Documents.List(); var itemA = new ListItem(); var itemB = new ListItem();
        if (listItems)
        {
            itemA.Blocks.Add(first); itemB.Blocks.Add(last); list.ListItems.Add(itemA); list.ListItems.Add(itemB);
            document.Blocks.Add(list);
        }
        else
        {
            sectionA.Blocks.Add(first); sectionB.Blocks.Add(last); document.Blocks.Add(sectionA); document.Blocks.Add(sectionB);
        }
        var box = new Editor(document); Assert.True(((IImeSupport)box).TryReplaceImeText(2, 4, ""));
        Assert.Equal("abF\n", document.GetText());
        if (listItems) { Assert.Same(itemA, Assert.Single(list.ListItems)); Assert.Null(itemB.Parent); }
        else { Assert.Same(sectionA, Assert.Single(document.Blocks)); Assert.Null(sectionB.Parent); }
        Assert.True(box.Undo()); Assert.Equal("abc\nDEF\n", document.GetText());
        Assert.Same(listItems ? itemB.Blocks : sectionB.Blocks, last.SiblingBlocks);
        Assert.True(box.Redo()); Assert.Equal("abF\n", document.GetText());
    }

    [Fact]
    public void EnterInAnInitiallyEmptyDocument_CreatesTwoParagraphsInOneUndoUnit()
    {
        var document = new FlowDocument(); var box = new Editor(document); var events = Observe(box);
        box.Break(); Assert.Equal("\n\n", document.GetText()); Assert.Equal(2, document.Blocks.Count);
        Assert.Equal(1, box.CaretPosition!.DocumentOffset); Assert.Single(events);
        Assert.True(box.Undo()); Assert.Empty(document.Blocks); Assert.False(box.CanUndo);
        Assert.True(box.Redo()); Assert.Equal("\n\n", document.GetText());
    }

    [Fact]
    public void EnterOverSelectAll_InsertsAParagraphBreakAndRestoresTheOriginalSelection()
    {
        var document = FlowDocument.FromText("first\nsecond"); var box = new Editor(document); box.SelectAll();
        var events = Observe(box); box.Break();
        Assert.Equal("\n\n", document.GetText()); Assert.Equal(2, document.Blocks.Count);
        Assert.Equal(1, box.CaretPosition!.DocumentOffset); Assert.Single(events);
        Assert.True(box.Undo()); Assert.Equal("first\nsecond\n", document.GetText());
        Assert.Equal(document.GetText(), box.Selection.Text); Assert.False(box.CanUndo);
        Assert.True(box.Redo()); Assert.Equal("\n\n", document.GetText());
    }

    [Fact]
    public void BackspaceFromDocumentEnd_DeletesTheLastCharacter()
    {
        var document = FlowDocument.FromText("abcd"); var box = new Editor(document); box.CaretPosition = document.ContentEnd;
        box.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Back, ModifierKeys.None, true, false, 1));
        Assert.Equal("abc\n", document.GetText()); Assert.Equal(3, box.CaretPosition!.DocumentOffset);
        Assert.True(box.Undo()); Assert.Equal("abcd\n", document.GetText()); Assert.False(box.CanUndo);
    }

    [Fact]
    public void BoundRunReplacement_WritesOnlyTheCommittedValueToItsSource()
    {
        var source = new BoundSource(); var run = new Run() { FontSize = 23 };
        var binding = new Binding(nameof(BoundSource.Text)) { Source = source, Mode = BindingMode.TwoWay };
        BindingOperations.SetBinding(run, Run.TextProperty, binding);
        var paragraph = new Paragraph(run); var document = new FlowDocument(paragraph); var box = new Editor(document);
        Assert.True(((IImeSupport)box).TryReplaceImeText(1, 2, "X"));
        Assert.Equal(new[] { "aXd" }, source.Writes); Assert.Same(binding, BindingOperations.GetBindingBase(run, Run.TextProperty));
        Assert.True(box.Undo()); Assert.Equal(new[] { "aXd", "abcd" }, source.Writes);
        Assert.True(box.Redo()); Assert.Equal(new[] { "aXd", "abcd", "aXd" }, source.Writes);
        Assert.Equal(23, run.FontSize); Assert.Same(run, Assert.Single(paragraph.Inlines));
    }

    private sealed class BoundSource
    {
        private string _text = "abcd";
        public List<string> Writes { get; } = new();
        public string Text { get => _text; set { if (_text != value) { _text = value; Writes.Add(value); } } }
    }

    [Theory]
    [InlineData("abcd")] [InlineData("ab👩‍👩‍👧‍👦")]
    public void LastRunEnd_HasNoExtraRightArrowStopAndLeftUsesAGrapheme(string text)
    {
        var run = new Run(text); var document = new FlowDocument(new Paragraph(run)); var box = new Editor(document);
        box.CaretPosition = run.ContentEnd;
        box.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Right, ModifierKeys.None, true, false, 1));
        Assert.Equal(text.Length, box.CaretPosition!.DocumentOffset); Assert.True(box.Selection.IsEmpty);
        box.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Left, ModifierKeys.None, true, false, 2));
        Assert.Equal(GraphemeClusters.PreviousBoundary(text, text.Length), box.CaretPosition!.DocumentOffset);
        Assert.True(box.Selection.IsEmpty);
    }

    [Fact]
    public void AssigningTheDocumentEndCaret_AppendsTextAndSynchronizesAnEmptySelection()
    {
        var document = FlowDocument.FromText("abcd"); var box = new Editor(document); box.CaretPosition = document.ContentEnd;
        Assert.Equal(4, box.CaretPosition!.DocumentOffset); Assert.Equal(4, box.Selection.Start.DocumentOffset);
        box.Type("!"); Assert.Equal("abcd!\n", document.GetText()); Assert.Equal(5, box.CaretPosition!.DocumentOffset);
        Assert.True(box.Undo()); Assert.Equal("abcd\n", document.GetText()); Assert.False(box.CanUndo);
    }

    private static List<UndoAction> Observe(RichTextBox box)
    {
        var events = new List<UndoAction>(); box.TextChanged += (_, e) => events.Add(e.UndoAction); return events;
    }

    private static IEnumerable<Run> Runs(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span) foreach (var child in Runs(span.Inlines)) yield return child;
        }
    }

    private sealed class Editor(FlowDocument document) : RichTextBox(document)
    {
        internal void Type(string text) => InsertText(text);
        internal void Break() => InsertParagraphBreak();
    }
}
