using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichTextChangedTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(0, 3)] [InlineData(3, 3)] [InlineData(6, 3)]
    public void ReplacingAWholeRun_KeepsVisibleTextFormattingAndLiveCaret(int start, int length)
    {
        var runs = new[] { new Run("abc"), new Run("DEF"), new Run("ghi") };
        var original = runs[start / 3]; original.FontSize = 31; original.FontStyle = FontStyles.Italic;
        var paragraph = new Paragraph(); foreach (var run in runs) paragraph.Inlines.Add(run);
        var box = new RichTextBox(new FlowDocument(paragraph));
        var ime = (IImeSupport)box;
        Assert.True(ime.TrySetImeSelection(start, length));
        Assert.True(ime.TryReplaceImeText(start, length, "Window"));
        Assert.Equal("abcDEFghi".Remove(start, length).Insert(start, "Window") + Environment.NewLine, box.Document.GetText());
        Assert.Equal("Window", original.Text); Assert.Same(paragraph, original.Parent);
        Assert.Equal(31, original.FontSize); Assert.Equal(FontStyles.Italic, original.FontStyle);
        Assert.Same(original, box.CaretPosition!.Parent); Assert.Equal(start + 6, box.CaretPosition!.DocumentOffset);
        Assert.True(box.Undo()); Assert.Equal("abcDEFghi" + Environment.NewLine, box.Document.GetText());
        Assert.Equal(new Inline[] { runs[0], runs[1], runs[2] }, paragraph.Inlines);
        Assert.True(box.Redo()); Assert.Equal("Window", original.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Replacement_NotifiesOnlyTheFinalTextAndSelection(bool typed)
    {
        var box = new Editor(); var ime = (IImeSupport)box; ime.TrySetImeSelection(0, 4);
        var texts = new List<(string Text, int Caret, string Selection, UndoAction Action)>();
        var selections = new List<string>();
        box.TextChanged += (_, e) => texts.Add((box.Document.GetText(), box.CaretPosition!.DocumentOffset, box.Selection.Text, e.UndoAction));
        box.SelectionChanged += (_, _) => selections.Add(box.Selection.Text);
        if (typed) box.Type("Window"); else Assert.True(ime.TryReplaceImeText(0, 4, "Window"));
        Assert.Equal(new[] { ("Window tail" + Environment.NewLine, 6, "", UndoAction.Create) }, texts);
        Assert.Equal(new[] { "" }, selections);
        Assert.True(box.Undo()); Assert.Equal(("MMMM tail" + Environment.NewLine, 4, "MMMM", UndoAction.Undo), texts[1]);
        Assert.True(box.Redo()); Assert.Equal(("Window tail" + Environment.NewLine, 6, "", UndoAction.Redo), texts[2]);
        Assert.Equal(3, texts.Count); Assert.Equal(3, selections.Count);
    }

    [Fact]
    public void TextChanges_DescribeTheCommittedReplacement()
    {
        var box = new Editor(); var events = Observe(box);
        Assert.True(((IImeSupport)box).TryReplaceImeText(0, 4, "Window"));
        var change = Assert.Single(Assert.Single(events).Changes);
        Assert.Equal(0, change.Offset); Assert.Equal(4, change.RemovedLength); Assert.Equal(6, change.AddedLength);
        Assert.True(box.Undo()); change = Assert.Single(events[1].Changes);
        Assert.Equal(0, change.Offset); Assert.Equal(6, change.RemovedLength); Assert.Equal(4, change.AddedLength);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DirectFormattingChanges_NotifyAndUndoWithoutReplacingNodes(int kind)
    {
        var box = new Editor(); var events = Observe(box); var paragraph = (Paragraph)box.Document.Blocks[0];
        switch (kind)
        {
            case 0: box.Run.FontSize = 29; break;
            case 1: box.Run.Background = Brushes.DarkBlue; break;
            case 2: paragraph.TextAlignment = TextAlignment.Right; break;
            case 3: box.Document.FontStyle = FontStyles.Italic; break;
        }
        Assert.Single(events); Assert.Empty(events[0].Changes); Assert.Equal(UndoAction.Create, events[0].UndoAction);
        Assert.True(box.Undo()); Assert.Same(box.Run, paragraph.Inlines[0]); Assert.Equal(UndoAction.Undo, events[1].UndoAction);
        Assert.True(box.Redo()); Assert.Equal(UndoAction.Redo, events[2].UndoAction); Assert.Equal(3, events.Count);
    }

    [Fact]
    public void DirectRunChanges_MoveTheCaretAndCreateOneUndoUnit()
    {
        var box = new Editor(); ((IImeSupport)box).TrySetImeSelection(9, 0);
        var observed = new List<int>(); box.TextChanged += (_, _) => observed.Add(box.CaretPosition!.DocumentOffset);
        box.Run.Text = "z";
        Assert.Equal(new[] { 1 }, observed); Assert.Same(box.Run, box.CaretPosition!.Parent);
        Assert.True(box.Undo()); Assert.Equal(9, box.CaretPosition!.DocumentOffset); Assert.Equal("MMMM tail", box.Run.Text);
        Assert.True(box.Redo()); Assert.Equal(1, box.CaretPosition!.DocumentOffset); Assert.Equal(3, observed.Count);
    }

    [Fact]
    public void NestedChanges_PublishOneFinalContentNotification()
    {
        var box = new Editor(); var events = Observe(box);
        using (box.DeclareChangeBlock())
        {
            ((IImeSupport)box).TryReplaceImeText(0, 4, "Window");
            using (box.DeclareChangeBlock()) { box.Run.FontSize = 29; ((IImeSupport)box).TryReplaceImeText(6, 0, "++"); }
            Assert.Empty(events);
        }
        Assert.Single(events); Assert.Equal("Window++ tail", box.Run.Text);
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", box.Run.Text); Assert.Equal(20, box.Run.FontSize);
        Assert.False(box.CanUndo); Assert.True(box.Redo()); Assert.Equal(3, events.Count);
    }

    [Fact]
    public void GroupedNodeReplacementWithIdenticalText_RebindsTheSelectionToLiveContent()
    {
        var box = new Editor(); var paragraph = (Paragraph)box.Document.Blocks[0];
        ((IImeSupport)box).TrySetImeSelection(5, 4);
        var replacement = new Run(box.Run.Text) { FontSize = 31 };
        var observed = new List<DependencyObject?>();
        box.TextChanged += (_, _) => observed.Add(box.CaretPosition!.Parent);
        using (box.DeclareChangeBlock()) { paragraph.Inlines.Clear(); paragraph.Inlines.Add(replacement); }
        Assert.Same(replacement, Assert.Single(observed)); Assert.Equal("tail", box.Selection.Text);
        Assert.Same(replacement, box.Selection.Start.Parent); Assert.Same(replacement, box.Selection.End.Parent);
        Assert.True(box.Undo()); Assert.Same(box.Run, box.CaretPosition!.Parent); Assert.Equal("tail", box.Selection.Text);
        Assert.True(box.Redo()); Assert.Same(replacement, box.CaretPosition!.Parent); Assert.Equal("tail", box.Selection.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PresentationAndViewportChanges_DoNotCreateContentNotifications(bool inGroup)
    {
        var box = new Editor(); var events = Observe(box);
        using (inGroup ? box.DeclareChangeBlock() : null)
        {
            box.Document.NotifyTextPresentationChanged();
            box.Document.ViewerPaginator.PageSize = new Size(600, 800);
            box.Measure(new Size(320, 200)); box.Arrange(new Rect(0, 0, 320, 200));
        }
        Assert.Empty(events); Assert.False(box.CanUndo);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisabledHistory_StillNotifiesContentChanges(bool zeroLimit)
    {
        var box = new Editor(); if (zeroLimit) box.UndoLimit = 0; else box.IsUndoEnabled = false;
        var events = Observe(box); ((IImeSupport)box).TryReplaceImeText(0, 4, "Window");
        Assert.Single(events); Assert.Equal(UndoAction.None, events[0].UndoAction); Assert.False(box.CanUndo);
    }

    [Fact]
    public void NoOpAndInvalidReplacement_DoNotNotifyOrDiscardRedo()
    {
        var box = new Editor(); var ime = (IImeSupport)box; ime.TryReplaceImeText(0, 4, "Window"); Assert.True(box.Undo());
        var events = Observe(box);
        Assert.False(ime.TryReplaceImeText(-1, 2, "bad")); Assert.True(ime.TryReplaceImeText(0, 4, "MMMM"));
        Assert.Empty(events); Assert.False(box.CanUndo); Assert.True(box.Redo());
    }

    [Fact]
    public void AssignedDocument_NotifiesOnceAndUnsubscribesTheOldDocument()
    {
        var box = new Editor(); var old = box.Run; var events = Observe(box);
        box.Document = FlowDocument.FromText("next");
        Assert.Single(events); Assert.Equal(UndoAction.Clear, events[0].UndoAction); Assert.False(box.CanUndo);
        old.Text = "detached"; old.FontSize = 27; Assert.Single(events);
        box.Document = box.Document; Assert.Single(events);
    }

    [Fact]
    public void ReentrantContentListener_CreatesItsOwnUndoUnitAndOneSelectionNotification()
    {
        var box = new Editor(); var events = Observe(box); var selections = new List<int>(); bool again = false;
        box.SelectionChanged += (_, _) => selections.Add(box.CaretPosition!.DocumentOffset);
        box.TextChanged += (_, _) => { if (!again) { again = true; box.Type("!"); } };
        box.Type("a");
        Assert.Equal("a!MMMM tail", box.Run.Text); Assert.Equal(2, events.Count); Assert.Equal(new[] { 2 }, selections);
        Assert.True(box.Undo()); Assert.Equal("aMMMM tail", box.Run.Text);
        Assert.True(box.Undo()); Assert.Equal("MMMM tail", box.Run.Text); Assert.False(box.CanUndo);
    }

    [Fact]
    public void ReentrantDocumentAssignment_IsNotOverwrittenByTheOldEdit()
    {
        var box = new Editor(); bool once = false; var next = FlowDocument.FromText("next");
        box.TextChanged += (_, _) => { if (!once) { once = true; box.Document = next; } };
        box.Type("a"); Assert.Same(next, box.Document); Assert.Equal("next" + Environment.NewLine, box.Document.GetText());
        Assert.Equal(0, box.CaretPosition!.DocumentOffset); Assert.False(box.CanUndo);
    }

    [Fact]
    public void ThrowingContentListener_LeavesTheEditClosedAndPublishesFinalSelection()
    {
        var box = new Editor(); int selections = 0;
        TextChangedEventHandler listener = (_, _) => throw new InvalidOperationException("listener failure");
        box.TextChanged += listener; box.SelectionChanged += (_, _) => selections++;
        Assert.Throws<InvalidOperationException>((Action)(() => box.Type("a")));
        box.TextChanged -= listener; Assert.Equal(1, selections); Assert.Equal("aMMMM tail", box.Run.Text);
        Assert.True(box.Undo()); Assert.False(box.CanUndo); box.Type("b"); Assert.True(box.Undo());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutomationContentNotification_ReadsTheCommittedTextAndSelection(bool rich)
    {
        TextBoxBase box = rich ? new Editor() : new TextBox { Text = "MMMM tail" };
        var previous = AutomationPeer.EventSink; var sink = new Sink(box);
        try
        {
            AutomationPeer.EventSink = sink;
            ((IImeSupport)box).TryReplaceImeText(0, 4, "Window");
            Assert.Single(sink.Content); Assert.Equal(("Window tail" + (rich ? Environment.NewLine : ""), 6, 0), sink.Content[0]);
        }
        finally { AutomationPeer.EventSink = previous; }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RichTextProvider_ExposesTextSelectionAndFormattingGeometry(bool readOnly)
    {
        var box = new Editor { IsReadOnly = readOnly };
        box.Measure(new Size(320, 200)); box.Arrange(new Rect(0, 0, 320, 200));
        var peer = box.GetAutomationPeer()!;
        var source = Assert.IsAssignableFrom<IAutomationTextProviderSource>(peer);
        var provider = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.ITextProvider>(peer.GetPattern(PatternInterface.Text));
        Assert.Equal("MMMM tail" + Environment.NewLine, provider.DocumentRange.GetText(-1));
        Assert.Equal(readOnly, source.IsReadOnly); source.Select(0, 4);
        Assert.Equal("MMMM", Assert.Single(provider.GetSelection()).GetText(-1));
        var rectangles = source.GetBoundingRectangles(0, 4);
        Assert.NotEmpty(rectangles); Assert.All(rectangles, rect => Assert.True(rect.Width > 0 && rect.Height > 0));
        Assert.Equal("MMMM", box.Selection.Text); Assert.Equal(4, box.CaretPosition!.DocumentOffset);
        Assert.False(box.CanUndo);
    }

    [Fact]
    public void ForwardInsertionAtANestedRunBoundary_UsesTheFollowingRun()
    {
        var first = new Run("abc"); var second = new Run("DEF") { FontSize = 31 };
        var paragraph = new Paragraph(first); paragraph.Inlines.Add(new Italic(second));
        var document = new FlowDocument(paragraph); var box = new RichTextBox(document);
        Assert.Same(first, document.GetPositionAtOffset(3, LogicalDirection.Backward)!.Parent);
        Assert.Same(second, document.GetPositionAtOffset(3, LogicalDirection.Forward)!.Parent);
        Assert.True(((IImeSupport)box).TryReplaceImeText(3, 3, "Window"));
        Assert.Equal("abcWindow" + Environment.NewLine, document.GetText());
        Assert.Equal("Window", second.Text); Assert.Equal(31, second.FontSize);
        Assert.Equal(FontStyles.Italic, second.GetEffectiveFontStyle()); Assert.True(box.Undo());
        Assert.Equal("DEF", second.Text); Assert.True(box.Redo()); Assert.Equal("Window", second.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PlainReplacement_ContentListenersSeeTheFinalCaretAndSelection(bool typed)
    {
        var box = new PlainEditor(); box.Select(0, 4);
        var events = new List<(string Text, int Caret, int Start, int Length)>();
        box.TextChanged += (_, _) => events.Add((box.Text, box.CaretIndex, box.SelectionStart, box.SelectionLength));
        if (typed) box.Type("Window"); else ((IImeSupport)box).TryReplaceImeText(0, 4, "Window");
        Assert.Equal(new[] { ("Window tail", 6, 6, 0) }, events);
    }

    [Fact]
    public void PlainUndoRedo_ContentListenersSeeTheRestoredSelectionAndAction()
    {
        var box = new PlainEditor(); box.Select(0, 4); box.Type("Window");
        var events = new List<(UndoAction Action, string Text, int Caret, int Start, int Length)>();
        int selections = 0;
        box.TextChanged += (_, e) => events.Add((e.UndoAction, box.Text, box.CaretIndex, box.SelectionStart, box.SelectionLength));
        box.SelectionChanged += (_, _) => selections++;
        Assert.True(box.Undo()); Assert.True(box.Redo());
        Assert.Equal(new[] { (UndoAction.Undo, "MMMM tail", 4, 0, 4), (UndoAction.Redo, "Window tail", 6, 6, 0) }, events);
        Assert.Equal(2, selections);
    }

    [Fact]
    public void PlainUndoListener_CanMakeAnIndependentUndoableEdit()
    {
        var box = new PlainEditor(); box.Type("a"); bool once = false;
        box.TextChanged += (_, _) => { if (!once) { once = true; box.Type("b"); } };
        Assert.True(box.Undo()); Assert.Equal("bMMMM tail", box.Text); Assert.Equal(1, box.CaretIndex);
        Assert.False(box.CanRedo); Assert.True(box.Undo()); Assert.Equal("MMMM tail", box.Text); Assert.False(box.CanUndo);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PlainDisabledHistory_UsesNoneForContentNotifications(bool zeroLimit)
    {
        var box = new PlainEditor(); if (zeroLimit) box.UndoLimit = 0; else box.IsUndoEnabled = false;
        var actions = new List<UndoAction>(); box.TextChanged += (_, e) => actions.Add(e.UndoAction);
        box.Type("a"); Assert.Equal(new[] { UndoAction.None }, actions); Assert.False(box.CanUndo);
    }

    private static List<TextChangedEventArgs> Observe(RichTextBox box)
    { var result = new List<TextChangedEventArgs>(); box.TextChanged += (_, e) => result.Add(e); return result; }

    private sealed class Editor : RichTextBox
    {
        internal Run Run { get; } = new("MMMM tail") { FontSize = 20 };
        internal Editor() { Document = new FlowDocument(new Paragraph(Run)); }
        internal void Type(string text) => InsertText(text);
    }

    private sealed class PlainEditor : TextBox
    {
        internal PlainEditor() { Text = "MMMM tail"; }
        internal void Type(string text) => InsertText(text);
    }

    private sealed class Sink(TextBoxBase box) : IAutomationEventSink
    {
        internal List<(string Text, int Caret, int SelectionLength)> Content { get; } = new();
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents id)
        {
            if (id != AutomationEvents.TextPatternOnTextChanged) return;
            var provider = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.ITextProvider>(peer.GetPattern(PatternInterface.Text));
            Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var context));
            Assert.Equal(context.Text, provider.DocumentRange.GetText(-1));
            Assert.Empty(Assert.Single(provider.GetSelection()).GetText(-1));
            Content.Add((context.Text, context.CursorIndex, Math.Abs(context.CursorIndex - context.AnchorIndex)));
        }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue) { }
        public void OnFocusChanged(AutomationPeer peer) { }
    }
}
