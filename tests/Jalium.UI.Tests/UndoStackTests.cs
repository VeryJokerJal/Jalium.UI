using System.Threading;
using Jalium.UI.Controls.Editor;

namespace Jalium.UI.Tests;

public class UndoStackTests
{
    [Fact]
    public void UndoRedo_MultiChangeGroup_ShouldPreserveOrder()
    {
        var document = new TextDocument(string.Empty);

        document.BeginUpdate();
        document.Insert(0, "A");
        document.Insert(1, "B");
        document.Insert(2, "C");
        document.EndUpdate();

        Assert.Equal("ABC", document.Text);

        document.UndoStack.Undo(document);
        Assert.Equal(string.Empty, document.Text);

        document.UndoStack.Redo(document);
        Assert.Equal("ABC", document.Text);
    }

    [Fact]
    public void UndoRedo_ReplaceGroup_ShouldRestoreCorrectText()
    {
        var document = new TextDocument("abcd");

        document.BeginUpdate();
        document.Replace(1, 1, "X");
        document.Replace(2, 1, "Y");
        document.EndUpdate();

        Assert.Equal("aXYd", document.Text);

        document.UndoStack.Undo(document);
        Assert.Equal("abcd", document.Text);

        document.UndoStack.Redo(document);
        Assert.Equal("aXYd", document.Text);
    }

    [Fact]
    public void UndoLimit_ShouldApplyToMergedGroups()
    {
        var document = new TextDocument(string.Empty);
        document.UndoStack.UndoLimit = 1;

        document.BeginUpdate();
        document.Insert(0, "a");
        document.EndUpdate();

        document.BeginUpdate();
        document.Insert(1, "b");
        document.EndUpdate();

        Assert.Equal(1, document.UndoStack.UndoCount);
        Assert.True(document.UndoStack.CanUndo);
    }

    [Fact]
    public void UndoStack_ShouldExposeUndoAndRedoCount()
    {
        var document = new TextDocument("a");
        Assert.Equal(0, document.UndoStack.UndoCount);
        Assert.Equal(0, document.UndoStack.RedoCount);

        document.Insert(1, "b");
        Assert.Equal(1, document.UndoStack.UndoCount);
        Assert.Equal(0, document.UndoStack.RedoCount);

        document.UndoStack.Undo(document);
        Assert.Equal(0, document.UndoStack.UndoCount);
        Assert.Equal(1, document.UndoStack.RedoCount);
    }

    [Fact]
    public void EndMergeGroup_WithoutStart_ShouldThrow()
    {
        var undoStack = new UndoStack();
        Assert.Throws<InvalidOperationException>(() => undoStack.EndMergeGroup());
    }

    [Fact]
    public void NewEdit_AfterUndo_ShouldClearRedo()
    {
        var document = new TextDocument("a");
        document.Insert(1, "b");
        document.UndoStack.Undo(document);
        Assert.True(document.UndoStack.CanRedo);

        document.Insert(1, "c");
        Assert.False(document.UndoStack.CanRedo);
    }

    [Fact]
    public void SequentialTyping_ShouldMergeIntoSingleUndoGroup()
    {
        var document = new TextDocument(string.Empty);
        document.Insert(0, "a");
        document.Insert(1, "b");
        document.Insert(2, "c");

        Assert.Equal(1, document.UndoStack.UndoCount);

        document.UndoStack.Undo(document);
        Assert.Equal(string.Empty, document.Text);
    }

    [Fact]
    public void TypingAfterWhitespace_ShouldNotMergeAcrossBoundary()
    {
        var document = new TextDocument(string.Empty);
        document.Insert(0, "a");
        document.Insert(1, " ");
        document.Insert(2, "b");

        Assert.Equal(2, document.UndoStack.UndoCount);
    }

    [Fact]
    public void Undo_OnEmptyStack_ShouldBeNoOp()
    {
        var document = new TextDocument("abc");
        document.UndoStack.Undo(document);
        Assert.Equal("abc", document.Text);
    }

    [Fact]
    public void Redo_OnEmptyStack_ShouldBeNoOp()
    {
        var document = new TextDocument("abc");
        document.UndoStack.Redo(document);
        Assert.Equal("abc", document.Text);
    }

    [Fact]
    public void StateChanged_ShouldRaiseForPushUndoRedoAndClear()
    {
        var document = new TextDocument(string.Empty);
        int stateChanged = 0;
        document.UndoStack.StateChanged += (_, _) => stateChanged++;

        document.Insert(0, "a");
        document.UndoStack.Undo(document);
        document.UndoStack.Redo(document);
        document.UndoStack.Clear();

        Assert.True(stateChanged >= 4);
    }

    [Fact]
    public void BeginUpdate_WithMultipleChanges_ShouldCreateSingleUndoStep()
    {
        var document = new TextDocument(string.Empty);

        document.BeginUpdate();
        document.Insert(0, "A");
        document.Insert(1, "B");
        document.Insert(2, "C");
        document.EndUpdate();

        Assert.Equal(1, document.UndoStack.UndoCount);
        document.UndoStack.Undo(document);
        Assert.Equal(string.Empty, document.Text);
    }

    [Fact]
    public void NestedBeginUpdate_ShouldStillCreateSingleUndoStep()
    {
        var document = new TextDocument(string.Empty);

        document.BeginUpdate();
        document.Insert(0, "A");
        document.BeginUpdate();
        document.Insert(1, "B");
        document.EndUpdate();
        document.EndUpdate();

        Assert.Equal(1, document.UndoStack.UndoCount);
    }

    [Fact]
    public void StartEndMergeGroup_WithoutChanges_ShouldNotAddUndoEntries()
    {
        var undoStack = new UndoStack();

        undoStack.StartMergeGroup();
        undoStack.EndMergeGroup();

        Assert.Equal(0, undoStack.UndoCount);
        Assert.Equal(0, undoStack.RedoCount);
    }

    [Fact]
    public void Clear_ShouldResetUndoAndRedoCounts()
    {
        var document = new TextDocument("a");
        document.Insert(1, "b");
        document.UndoStack.Undo(document);
        Assert.True(document.UndoStack.RedoCount > 0);

        document.UndoStack.Clear();

        Assert.Equal(0, document.UndoStack.UndoCount);
        Assert.Equal(0, document.UndoStack.RedoCount);
    }

    [Fact]
    public void UndoLimit_ShouldTrimOldestGroupsAfterCommit()
    {
        var document = new TextDocument(string.Empty);
        document.UndoStack.UndoLimit = 2;

        document.BeginUpdate();
        document.Insert(document.TextLength, "a");
        document.EndUpdate();

        document.BeginUpdate();
        document.Insert(document.TextLength, "b");
        document.EndUpdate();

        document.BeginUpdate();
        document.Insert(document.TextLength, "c");
        document.EndUpdate();

        Assert.Equal(2, document.UndoStack.UndoCount);

        document.UndoStack.Undo(document);
        Assert.Equal("ab", document.Text);
        document.UndoStack.Undo(document);
        Assert.Equal("a", document.Text);
    }

    [Fact]
    public void UndoLimit_Zero_ShouldAllowUnlimitedHistory()
    {
        var document = new TextDocument(string.Empty);
        document.UndoStack.UndoLimit = 0;

        for (int i = 0; i < 5; i++)
        {
            document.BeginUpdate();
            document.Insert(document.TextLength, i.ToString());
            document.EndUpdate();
        }

        Assert.Equal(5, document.UndoStack.UndoCount);
    }

    [Fact]
    public void ConsecutiveBackspaceLikeDeletes_ShouldMergeIntoSingleUndoGroup()
    {
        var document = new TextDocument("abc");

        document.Remove(2, 1); // remove 'c'
        document.Remove(1, 1); // remove 'b'

        Assert.Equal(1, document.UndoStack.UndoCount);
        document.UndoStack.Undo(document);
        Assert.Equal("abc", document.Text);
    }

    [Fact]
    public void StartMergeGroup_Nested_ShouldRequireBalancedEnd()
    {
        var undoStack = new UndoStack();

        undoStack.StartMergeGroup();
        undoStack.StartMergeGroup();
        undoStack.EndMergeGroup();
        Assert.Equal(0, undoStack.UndoCount);

        undoStack.EndMergeGroup();
        Assert.Equal(0, undoStack.UndoCount);
    }

    [Fact]
    public void SequentialTyping_UndoRedo_ShouldRoundTripMergedGroup()
    {
        var document = new TextDocument(string.Empty);

        document.Insert(0, "h");
        document.Insert(1, "i");

        Assert.Equal(1, document.UndoStack.UndoCount);
        document.UndoStack.Undo(document);
        Assert.Equal(string.Empty, document.Text);
        document.UndoStack.Redo(document);
        Assert.Equal("hi", document.Text);
    }

    [Fact]
    public void MergeTypingWindow_Elapsed_ShouldNotMergeTypingGroups()
    {
        var document = new TextDocument(string.Empty);
        document.UndoStack.MergeTypingWindowMs = 1;

        document.Insert(0, "a");
        Thread.Sleep(20);
        document.Insert(1, "b");

        Assert.Equal(2, document.UndoStack.UndoCount);
    }
}
