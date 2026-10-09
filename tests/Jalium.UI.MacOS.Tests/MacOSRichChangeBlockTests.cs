using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichChangeBlockTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedChanges_UndoRedoAsOneUnit(bool rich)
    {
        var box = Create(rich);
        Select(box, 0, 4);
        using (box.DeclareChangeBlock())
        {
            Insert(box, "Window");
            using (box.DeclareChangeBlock()) Insert(box, "++");
        }
        Assert.Equal("Window++ tail", Text(box));
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
        Assert.False(box.CanUndo);
        Assert.Equal("MMMM", SelectedText(box));
        Assert.True(box.Redo()); Assert.Equal("Window++ tail", Text(box));
        Assert.False(box.CanRedo);
        if (box is RichEditor editor) Assert.Same(editor.OriginalRun, editor.Document.Blocks.OfType<Paragraph>().Single().Inlines.Single());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GroupCapturesSelectionBeforeTheFirstEdit(bool rich)
    {
        var box = Create(rich);
        Select(box, 0, 4);
        using (box.DeclareChangeBlock())
        {
            Select(box, 5, 4);
            Insert(box, "edited");
        }
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
        Assert.Equal("MMMM", SelectedText(box));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyGroup_PreservesTheRedoHistory(bool rich)
    {
        var box = Create(rich);
        Insert(box, "a"); Assert.True(box.Undo());
        using (box.DeclareChangeBlock()) { }
        Assert.False(box.CanUndo); Assert.True(box.Redo());
        Assert.Equal("aMMMM tail", Text(box));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExceptionInDeclaredBlock_StillClosesOneUndoUnit(bool rich)
    {
        var box = Create(rich);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var change = box.DeclareChangeBlock();
            Insert(box, "a"); Insert(box, "b");
            throw new InvalidOperationException("test edit failure");
        }));
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
        Assert.False(box.CanUndo);
        Insert(box, "c"); Assert.True(box.Undo());
        Assert.Equal("MMMM tail", Text(box));
    }

    [Fact]
    public void RichFormattingAndText_GroupUndoKeepsTheOriginalDocumentAndInheritance()
    {
        var box = new RichEditor();
        var document = box.Document;
        Select(box, 0, 4);
        using (box.DeclareChangeBlock())
        {
            box.SetFontSize(29);
            box.ToggleItalic();
            Insert(box, "Window");
        }
        Assert.True(box.Undo());
        Assert.Same(document, box.Document); Assert.Equal("MMMM tail", Text(box));
        Assert.Equal(FontStyles.Italic, box.OriginalRun.FontStyle);
        Assert.Equal(20, box.OriginalRun.FontSize); Assert.False(box.CanUndo);
        Assert.True(box.Redo()); Assert.Equal("Window tail", Text(box));
        Assert.Equal(FontStyles.Normal, box.OriginalRun.FontStyle); Assert.Equal(29, box.OriginalRun.FontSize);
    }

    [Fact]
    public void DirectRichDocumentChangesInAGroup_AreUndoableWithoutReplacingNodes()
    {
        var box = new RichEditor();
        using (box.DeclareChangeBlock())
        {
            box.OriginalRun.Text = "direct change";
            box.OriginalRun.FontWeight = FontWeights.Normal;
        }
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
        Assert.Equal(FontWeights.Black, box.OriginalRun.FontWeight);
        Assert.True(box.Redo()); Assert.Equal("direct change", Text(box));
        Assert.Equal(FontWeights.Normal, box.OriginalRun.FontWeight);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectionChanged_IsDeferredUntilTheOuterGroupEnds(bool rich)
    {
        var box = Create(rich);
        var observed = new List<string>();
        box.SelectionChanged += (_, _) => observed.Add(SelectedText(box));
        using (box.DeclareChangeBlock())
        {
            Select(box, 0, 4);
            using (box.DeclareChangeBlock()) Select(box, 5, 4);
            Assert.Empty(observed);
        }
        Assert.Equal(new[] { "tail" }, observed);
    }

    [Fact]
    public void PlainTextChanged_IsDeferredAndIncludesBothEdits()
    {
        var box = new PlainEditor();
        var observed = new List<TextChangedEventArgs>();
        box.TextChanged += (_, e) => observed.Add(e);
        using (box.DeclareChangeBlock())
        {
            Insert(box, "a"); Insert(box, "b");
            Assert.Empty(observed);
        }
        Assert.Single(observed); Assert.Equal(2, observed[0].Changes.Count);
        Assert.Equal("abMMMM tail", box.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnlimitedHistory_DoesNotThrowAndRetainsEveryEdit(bool rich)
    {
        var box = Create(rich); box.UndoLimit = -1;
        for (int index = 0; index < 110; index++) Insert(box, "x");
        for (int index = 0; index < 110; index++) Assert.True(box.Undo());
        Assert.Equal("MMMM tail", Text(box)); Assert.False(box.CanUndo);
        for (int index = 0; index < 110; index++) Assert.True(box.Redo());
        Assert.Equal(new string('x', 110) + "MMMM tail", Text(box));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void ChangingTheHistoryLimit_ClearsUndoAndRedo(bool rich, bool redo)
    {
        var box = Create(rich);
        Insert(box, "a"); if (redo) Assert.True(box.Undo());
        string current = Text(box);
        box.SetValue(TextBoxBase.UndoLimitProperty, 20);
        Assert.False(box.CanUndo); Assert.False(box.CanRedo);
        Assert.False(box.Undo()); Assert.False(box.Redo()); Assert.Equal(current, Text(box));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void DisablingUndo_ClearsBothHistoriesBeforeReenable(bool rich, bool redo)
    {
        var box = Create(rich);
        Insert(box, "a"); if (redo) Assert.True(box.Undo());
        box.SetValue(TextBoxBase.IsUndoEnabledProperty, false);
        Assert.False(box.CanUndo); Assert.False(box.CanRedo);
        box.IsUndoEnabled = true;
        Assert.False(box.Undo()); Assert.False(box.Redo());
        Insert(box, "b"); Assert.True(box.Undo());
        Assert.Equal(redo ? "MMMM tail" : "aMMMM tail", Text(box));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ZeroHistoryLimit_DisablesNewUndoRecords(bool rich)
    {
        var box = Create(rich); box.UndoLimit = 0;
        using (box.DeclareChangeBlock()) { Insert(box, "a"); Insert(box, "b"); }
        Assert.False(box.CanUndo); Assert.False(box.Undo()); Assert.False(box.Redo());
        Assert.Equal("abMMMM tail", Text(box));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HistoryLimit_RejectsValuesLessThanMinusOne(bool rich)
    {
        var box = Create(rich);
        Assert.Throws<ArgumentException>(() => box.SetValue(TextBoxBase.UndoLimitProperty, -2));
        Assert.Equal(100, box.UndoLimit);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UndoAndRedo_CannotConsumeAnOpenChangeUnit(bool rich)
    {
        var box = Create(rich);
        using (box.DeclareChangeBlock())
        {
            Insert(box, "a");
            Assert.False(box.Undo()); Assert.False(box.Redo());
            Insert(box, "b");
        }
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void ClrUndoSettings_CannotChangeAnOpenUndoUnit(bool rich, bool limit)
    {
        var box = Create(rich);
        using (box.DeclareChangeBlock())
        {
            Insert(box, "a");
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (limit) box.UndoLimit = 1; else box.IsUndoEnabled = false;
            });
            Assert.Equal(100, box.UndoLimit); Assert.True(box.IsUndoEnabled);
            Insert(box, "b");
        }
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
    }

    [Fact]
    public void ThrowingDeferredListener_DoesNotLeaveTheChangeBlockOpen()
    {
        var box = new PlainEditor();
        TextChangedEventHandler listener = (_, _) => throw new InvalidOperationException("test listener failure");
        box.TextChanged += listener;
        box.BeginChange(); Insert(box, "a"); Insert(box, "b");
        Assert.Throws<InvalidOperationException>(() => box.EndChange());
        box.TextChanged -= listener;
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
        using (box.DeclareChangeBlock()) { Insert(box, "c"); Insert(box, "d"); }
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", Text(box));
        Assert.Throws<InvalidOperationException>(() => box.EndChange());
    }

    [Fact]
    public void AssigningADocumentInsideAGroup_DropsOnlyTheOldDocumentHistory()
    {
        var box = new RichEditor(); var old = box.Document;
        var replacement = new FlowDocument(new Paragraph(new Run("next")));
        using (box.DeclareChangeBlock())
        {
            Insert(box, "old"); box.Document = replacement;
            Insert(box, "a"); Insert(box, "b");
        }
        Assert.True(box.Undo()); Assert.Same(replacement, box.Document);
        Assert.Equal("next", Text(box)); Assert.False(box.CanUndo); Assert.Null(old.Parent);
    }

    private static TextBoxBase Create(bool rich) => rich ? new RichEditor() : new PlainEditor();
    private static string Text(TextBoxBase box) => box is TextBox plain ? plain.Text : ((RichTextBox)box).Document.GetText().TrimEnd('\r', '\n');
    private static string SelectedText(TextBoxBase box) => box is TextBox plain ? plain.SelectedText : ((RichTextBox)box).Selection.Text;
    private static void Select(TextBoxBase box, int offset, int length) => Assert.True(((IImeSupport)box).TrySetImeSelection(offset, length));
    private static void Insert(TextBoxBase box, string text)
    {
        if (box is PlainEditor plain) plain.Insert(text); else ((RichEditor)box).Insert(text);
    }
    private sealed class PlainEditor : TextBox
    {
        internal PlainEditor() { Text = "MMMM tail"; }
        internal void Insert(string text) => InsertText(text);
    }
    private sealed class RichEditor : RichTextBox
    {
        internal Run OriginalRun { get; } = new("MMMM tail")
        { FontSize = 20, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic };
        internal RichEditor() { Document = new FlowDocument(new Paragraph(OriginalRun)); }
        internal void Insert(string text) => InsertText(text);
    }
}
