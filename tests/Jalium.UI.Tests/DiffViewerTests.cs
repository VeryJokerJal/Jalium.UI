using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DiffViewerTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void DiffViewer_DefaultProperties()
    {
        var viewer = new DiffViewer();

        Assert.Equal(DiffViewMode.SideBySide, viewer.ViewMode);
        Assert.True(viewer.IsReadOnly);
        Assert.True(viewer.ShowLineNumbers);
        Assert.Equal(60.0, viewer.GutterWidth);
        Assert.Equal(3, viewer.ContextLines);
        Assert.Equal("", viewer.OriginalText);
        Assert.Equal("", viewer.ModifiedText);
        Assert.True(viewer.ShowMinimap);
        Assert.False(viewer.EnableInlineEdit);
    }

    [Fact]
    public void DiffViewer_ViewMode_CanBeSetToUnified()
    {
        var viewer = new DiffViewer();
        viewer.ViewMode = DiffViewMode.Unified;
        Assert.Equal(DiffViewMode.Unified, viewer.ViewMode);
    }

    [Fact]
    public void DiffViewer_GetChangeCount_ReturnsZero_WhenNoTextSet()
    {
        var viewer = new DiffViewer();
        Assert.Equal(0, viewer.GetChangeCount());
    }

    [Fact]
    public void DiffComputer_ComputeDiff_IdenticalTexts_AllUnchanged()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "hello\nworld", "hello\nworld" })!;

        Assert.Equal(2, result.Count);
        Assert.All(result, line => Assert.Equal(DiffLineType.Unchanged, line.LineType));
    }

    [Fact]
    public void DiffComputer_ComputeDiff_AddedLines()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "a", "a\nb" })!;

        Assert.Equal(2, result.Count);
        Assert.Equal(DiffLineType.Unchanged, result[0].LineType);
        Assert.Equal("a", result[0].OriginalText);
        Assert.Equal(DiffLineType.Added, result[1].LineType);
        Assert.Equal("b", result[1].ModifiedText);
    }

    [Fact]
    public void DiffComputer_ComputeDiff_RemovedLines()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "a\nb", "a" })!;

        Assert.Equal(2, result.Count);
        Assert.Equal(DiffLineType.Unchanged, result[0].LineType);
        Assert.Equal(DiffLineType.Removed, result[1].LineType);
        Assert.Equal("b", result[1].OriginalText);
    }

    [Fact]
    public void DiffComputer_ComputeDiff_ModifiedLines_PairedAsModified()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "hello world", "hello earth" })!;

        // The single line was changed, so it should be Modified
        Assert.Single(result);
        Assert.Equal(DiffLineType.Modified, result[0].LineType);
        Assert.Equal("hello world", result[0].OriginalText);
        Assert.Equal("hello earth", result[0].ModifiedText);
        Assert.NotNull(result[0].WordDiffs);
    }

    [Fact]
    public void DiffComputer_ComputeDiff_EmptyOriginal_AllAdded()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "", "a\nb\nc" })!;

        Assert.Equal(3, result.Count);
        Assert.All(result, line => Assert.Equal(DiffLineType.Added, line.LineType));
    }

    [Fact]
    public void DiffComputer_ComputeDiff_EmptyModified_AllRemoved()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "a\nb", "" })!;

        Assert.Equal(2, result.Count);
        Assert.All(result, line => Assert.Equal(DiffLineType.Removed, line.LineType));
    }

    [Fact]
    public void DiffComputer_ComputeDiff_BothEmpty_ReturnsEmpty()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "", "" })!;

        Assert.Empty(result);
    }

    [Fact]
    public void DiffComputer_ComputeWordDiff_IdenticalLines_AllUnchanged()
    {
        var computeWordDiff = typeof(DiffComputer).GetMethod("ComputeWordDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<WordDiff>)computeWordDiff.Invoke(null, new object[] { "hello world", "hello world" })!;

        Assert.All(result, wd => Assert.Equal(DiffLineType.Unchanged, wd.Type));
    }

    [Fact]
    public void DiffComputer_ComputeWordDiff_DifferentWords_ProducesAddedRemoved()
    {
        var computeWordDiff = typeof(DiffComputer).GetMethod("ComputeWordDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<WordDiff>)computeWordDiff.Invoke(null, new object[] { "foo bar", "foo baz" })!;

        Assert.Contains(result, wd => wd.Type == DiffLineType.Unchanged && wd.Text == "foo");
        Assert.Contains(result, wd => wd.Type == DiffLineType.Removed && wd.Text == "bar");
        Assert.Contains(result, wd => wd.Type == DiffLineType.Added && wd.Text == "baz");
    }

    [Fact]
    public void DiffComputer_ComputeDiff_LineNumbers_AreCorrect()
    {
        var computeDiff = typeof(DiffComputer).GetMethod("ComputeDiff",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = (List<DiffLine>)computeDiff.Invoke(null, new object[] { "a\nb\nc", "a\nc" })!;

        // a is unchanged (line 1 in both), b is removed (line 2 orig), c is unchanged (line 3 orig, line 2 mod)
        var unchanged1 = result[0];
        Assert.Equal(DiffLineType.Unchanged, unchanged1.LineType);
        Assert.Equal(1, unchanged1.OriginalLineNumber);
        Assert.Equal(1, unchanged1.ModifiedLineNumber);

        var removed = result.First(l => l.LineType == DiffLineType.Removed);
        Assert.Equal(2, removed.OriginalLineNumber);
        Assert.Null(removed.ModifiedLineNumber);
    }

    [Fact]
    public void DiffViewer_NavigateToNextChange_DoesNotThrow_WhenNoChanges()
    {
        var viewer = new DiffViewer();
        var ex = Record.Exception(() => viewer.NavigateToNextChange());
        Assert.Null(ex);
    }

    [Fact]
    public void DiffViewer_NavigateToPreviousChange_DoesNotThrow_WhenNoChanges()
    {
        var viewer = new DiffViewer();
        var ex = Record.Exception(() => viewer.NavigateToPreviousChange());
        Assert.Null(ex);
    }

    [Fact]
    public void DiffViewer_GutterWidth_CanBeSet()
    {
        var viewer = new DiffViewer();
        viewer.GutterWidth = 80.0;
        Assert.Equal(80.0, viewer.GutterWidth);
    }

    [Fact]
    public void DiffViewer_ContextLines_CanBeSet()
    {
        var viewer = new DiffViewer();
        viewer.ContextLines = 5;
        Assert.Equal(5, viewer.ContextLines);
    }

    [Fact]
    public void DiffViewer_BrushProperties_DefaultToNull()
    {
        var viewer = new DiffViewer();
        Assert.Null(viewer.AddedLineBrush);
        Assert.Null(viewer.RemovedLineBrush);
        Assert.Null(viewer.ModifiedLineBrush);
        Assert.Null(viewer.AddedWordBrush);
        Assert.Null(viewer.RemovedWordBrush);
        Assert.Null(viewer.GutterBackground);
        Assert.Null(viewer.LineNumberForeground);
        Assert.Null(viewer.SelectionBrush);
        Assert.Null(viewer.CaretBrush);
    }

    [Fact]
    public void DiffViewer_IsFocusable()
    {
        var viewer = new DiffViewer();
        Assert.True(viewer.Focusable);
    }
}
