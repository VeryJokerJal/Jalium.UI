using Jalium.UI.Controls.Editor;

namespace Jalium.UI.Tests;

public class TextDocumentTests
{
    [Fact]
    public void EndUpdate_WithoutBeginUpdate_ShouldThrow()
    {
        var document = new TextDocument("abc");
        Assert.Throws<InvalidOperationException>(() => document.EndUpdate());
    }

    [Fact]
    public void BeginEndUpdate_ShouldRaiseSingleChangedEvent()
    {
        var document = new TextDocument("abc");
        int changedCount = 0;
        document.Changed += (_, _) => changedCount++;

        document.BeginUpdate();
        document.Insert(3, "d");
        document.Insert(4, "e");
        Assert.Equal(0, changedCount);

        document.EndUpdate();
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void NestedBeginEndUpdate_ShouldRaiseSingleChangedEventOnOuterEnd()
    {
        var document = new TextDocument("abc");
        int changedCount = 0;
        document.Changed += (_, _) => changedCount++;

        document.BeginUpdate();
        document.Insert(3, "d");
        document.BeginUpdate();
        document.Insert(4, "e");
        document.EndUpdate();
        Assert.Equal(0, changedCount);

        document.EndUpdate();
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void BeginEndUpdate_LineCountChanged_ShouldRaiseOnce()
    {
        var document = new TextDocument("a");
        int lineCountChanged = 0;
        document.LineCountChanged += (_, _) => lineCountChanged++;

        document.BeginUpdate();
        document.Insert(1, "\n");
        document.Insert(2, "b");
        document.EndUpdate();

        Assert.Equal(1, lineCountChanged);
        Assert.Equal(2, document.LineCount);
    }

    [Fact]
    public void Snapshot_ShouldRemainStableAfterDocumentMutation()
    {
        var document = new TextDocument("one\ntwo");
        var snapshot = document.CreateSnapshot();

        document.Insert(0, "z\n");

        Assert.Equal(2, snapshot.LineCount);
        Assert.Equal("one", snapshot.GetLineText(1));
        Assert.Equal("two", snapshot.GetLineText(2));
    }

    [Fact]
    public void Snapshot_LineOffsets_ShouldRemainStableAfterDocumentMutation()
    {
        var document = new TextDocument("aa\nbbb");
        var snapshot = document.CreateSnapshot();
        int line1Start = snapshot.GetLineStartOffset(1);
        int line2Start = snapshot.GetLineStartOffset(2);

        document.Insert(0, "prefix\n");

        Assert.Equal(0, line1Start);
        Assert.Equal(3, line2Start);
        Assert.Equal(2, snapshot.GetLineLength(1));
        Assert.Equal(3, snapshot.GetLineLength(2));
    }

    [Fact]
    public void Replace_OutOfRange_ShouldThrow()
    {
        var document = new TextDocument("abc");
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Replace(10, 1, "x"));
    }

    [Fact]
    public void TextSetter_ShouldClearUndoHistory()
    {
        var document = new TextDocument("abc");
        document.Insert(3, "d");
        Assert.True(document.UndoStack.CanUndo);

        document.Text = "reset";

        Assert.False(document.UndoStack.CanUndo);
        Assert.False(document.UndoStack.CanRedo);
    }

    [Fact]
    public void GetLineByOffset_ShouldHandleDocumentEndOffset()
    {
        var document = new TextDocument("a\nb");
        var endLine = document.GetLineByOffset(document.TextLength);
        Assert.Equal(2, endLine.LineNumber);
    }

    [Fact]
    public void CreateSnapshot_ShouldCaptureCurrentVersion()
    {
        var document = new TextDocument("abc");
        var beforeVersion = document.Version;
        var snapshot = document.CreateSnapshot();

        document.Insert(3, "x");

        Assert.Equal(beforeVersion, snapshot.Version);
        Assert.NotEqual(document.Version, snapshot.Version);
    }

    [Fact]
    public void EmptyDocument_ShouldContainSingleEmptyLine()
    {
        var document = new TextDocument();

        Assert.Equal(1, document.LineCount);
        Assert.Equal(0, document.TextLength);
        Assert.Equal(string.Empty, document.GetLineText(1));
    }

    [Fact]
    public void CRLF_Text_ShouldBuildCorrectLineMetadata()
    {
        var document = new TextDocument("a\r\nbb");

        var line1 = document.GetLineByNumber(1);
        var line2 = document.GetLineByNumber(2);

        Assert.Equal(2, document.LineCount);
        Assert.Equal(1, line1.Length);
        Assert.Equal(2, line1.DelimiterLength);
        Assert.Equal(2, line2.Length);
        Assert.Equal(0, line2.DelimiterLength);
        Assert.Equal("a", document.GetLineText(1));
        Assert.Equal("bb", document.GetLineText(2));
    }

    [Fact]
    public void GetLineByNumber_OutOfRange_ShouldThrow()
    {
        var document = new TextDocument("a");

        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetLineByNumber(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetLineByNumber(2));
    }

    [Fact]
    public void GetText_ShouldReturnRequestedSubstring()
    {
        var document = new TextDocument("abcdef");

        Assert.Equal("cd", document.GetText(2, 2));
    }

    [Fact]
    public void GetCharAt_ShouldReturnCharacterAtOffset()
    {
        var document = new TextDocument("abcdef");

        Assert.Equal('e', document.GetCharAt(4));
    }

    [Fact]
    public void BeginEndUpdate_WithoutChanges_ShouldNotRaiseEvents()
    {
        var document = new TextDocument("abc");
        int changedCount = 0;
        int lineCountChanged = 0;
        document.Changed += (_, _) => changedCount++;
        document.LineCountChanged += (_, _) => lineCountChanged++;

        document.BeginUpdate();
        document.EndUpdate();

        Assert.Equal(0, changedCount);
        Assert.Equal(0, lineCountChanged);
    }

    [Fact]
    public void Replace_ShouldRaiseChangingBeforeChanged()
    {
        var document = new TextDocument("abc");
        var events = new List<string>();
        document.Changing += (_, _) => events.Add("changing");
        document.Changed += (_, _) => events.Add("changed");

        document.Replace(1, 1, "X");

        Assert.Equal(["changing", "changed"], events);
    }

    [Fact]
    public void Snapshot_ToString_ShouldRemainOriginalAfterMutations()
    {
        var document = new TextDocument("line1\nline2");
        var snapshot = document.CreateSnapshot();

        document.Insert(0, "prefix\n");
        document.Remove(document.TextLength - 2, 2);

        Assert.Equal("line1\nline2", snapshot.ToString());
    }

    [Fact]
    public void Snapshot_GetLineText_OutOfRange_ShouldThrow()
    {
        var document = new TextDocument("only");
        var snapshot = document.CreateSnapshot();

        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetLineText(2));
    }

    [Fact]
    public void GetLineByOffset_EmptyDocumentAtZero_ShouldReturnFirstLine()
    {
        var document = new TextDocument();

        var line = document.GetLineByOffset(0);

        Assert.Equal(1, line.LineNumber);
        Assert.Equal(0, line.Offset);
    }
}
