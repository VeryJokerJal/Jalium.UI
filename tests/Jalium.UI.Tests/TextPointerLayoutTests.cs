using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class TextPointerLayoutTests
{
    [Fact]
    public void UnhostedDocument_HasNoFabricatedGeometryOrVisualLines()
    {
        var document = new FlowDocument(P("中文🙂")); var position = document.ContentStart;
        Assert.False(position.HasValidLayout);
        Assert.True(position.GetCharacterRect(LogicalDirection.Forward).IsEmpty);
        Assert.True(position.GetCharacterRect(LogicalDirection.Backward).IsEmpty);
        Assert.False(position.IsAtLineStartPosition);
        Assert.Null(position.GetLineStartPosition(0));
    }

    [Theory, InlineData(180), InlineData(420), InlineData(760)]
    public void CharacterBounds_FollowActualFirstLineRangeAndLogicalDirection(int width)
    {
        var paragraph = P("前缀 Miii 中文 e\u0301 🙂 后续文字"); paragraph.Margin = new Thickness(11, 13, 3, 5);
        var editor = Editor(new FlowDocument(paragraph), width);
        var text = editor.Document.GetText(); var ime = (IImeSupport)editor;
        for (int at = 0; at < text.Length - Environment.NewLine.Length;)
        {
            int end = GraphemeClusters.NextBoundary(text, at);
            Assert.True(ime.TryGetImeTextRangeGeometry(at, end - at, false, out var expected));
            var forward = editor.Document.GetPositionAtOffset(at, LogicalDirection.Forward)!;
            var backward = editor.Document.GetPositionAtOffset(end, LogicalDirection.Backward)!;
            Assert.True(forward.HasValidLayout);
            Equal(expected.Rectangle, forward.GetCharacterRect(LogicalDirection.Forward));
            Equal(expected.Rectangle, backward.GetCharacterRect(LogicalDirection.Backward));
            for (int inside = at + 1; inside < end; inside++)
                Equal(expected.Rectangle, editor.Document.GetPositionAtOffset(inside, LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward));
            at = end;
        }
    }

    [Fact]
    public void ASecondRunOnTheSameLine_IsNotAnotherLineStart()
    {
        var first = new Run("甲乙"); var second = new Run("丙丁"); var paragraph = new Paragraph();
        paragraph.Inlines.Add(first); paragraph.Inlines.Add(second); var editor = Editor(new FlowDocument(paragraph));
        Assert.True(first.ContentStart.IsAtLineStartPosition);
        Assert.False(second.ContentStart.IsAtLineStartPosition);
        Assert.Equal(first.ContentStart.DocumentOffset, second.ContentStart.GetLineStartPosition(0)!.DocumentOffset);
    }

    [Fact]
    public void SoftWrapBoundary_UsesThePointersAffinity()
    {
        var paragraph = P(string.Concat(Enumerable.Repeat("Miii中文🙂", 12)));
        var editor = Editor(new FlowDocument(paragraph), 180); var ime = (IImeSupport)editor;
        Assert.True(ime.TryGetImeTextRangeGeometry(0, 1, false, out var first));
        int boundary = Enumerable.Range(1, paragraph.ContentEnd.DocumentOffset - 1).First(at =>
            ime.TryGetImeTextRangeGeometry(at, 1, false, out var rect) && rect.Rectangle.Top > first.Rectangle.Top + 1);
        var forward = editor.Document.GetPositionAtOffset(boundary, LogicalDirection.Forward)!;
        var backward = editor.Document.GetPositionAtOffset(boundary, LogicalDirection.Backward)!;
        Assert.True(forward.IsAtLineStartPosition); Assert.False(backward.IsAtLineStartPosition);
        Assert.Equal(boundary, forward.GetLineStartPosition(0)!.DocumentOffset);
        Assert.Equal(0, backward.GetLineStartPosition(0)!.DocumentOffset);
        Assert.True(forward.GetCharacterRect(LogicalDirection.Forward).Top > backward.GetCharacterRect(LogicalDirection.Backward).Top);
    }

    [Fact]
    public void ExplicitLineBreak_AndOutOfRangeOverloadsUseVisualLines()
    {
        var paragraph = P("甲\n乙\n丙"); var editor = Editor(new FlowDocument(paragraph));
        var middle = editor.Document.GetPositionAtOffset(2, LogicalDirection.Forward)!;
        Assert.True(middle.IsAtLineStartPosition);
        Assert.Equal(0, middle.GetLineStartPosition(-1, out int moved)!.DocumentOffset); Assert.Equal(-1, moved);
        Assert.Equal(4, middle.GetLineStartPosition(int.MaxValue, out moved)!.DocumentOffset); Assert.Equal(1, moved);
        Assert.Null(middle.GetLineStartPosition(int.MaxValue));
        Assert.Equal(0, middle.GetLineStartPosition(int.MinValue, out moved)!.DocumentOffset); Assert.Equal(-1, moved);
        Assert.Null(middle.GetLineStartPosition(int.MinValue));
    }

    [Fact]
    public void TableLineNavigation_MovesVerticallyWithinTheSameColumn()
    {
        var document = RichTextTableLayoutTests.Model(0); var table = document.Blocks.OfType<Table>().Single();
        var editor = Editor(document); var start = table.RowGroups[0].Rows[0].Cells[1].Blocks[0].ContentStart;
        var next = start.GetLineStartPosition(1, out int moved);
        Assert.Equal(1, moved); Assert.Same(table.RowGroups[0].Rows[1].Cells[1].Blocks[0], next!.Paragraph);
    }

    [Fact]
    public void VerticalLineNavigation_UsesCellClipsForOverflowingGlyphs()
    {
        var document = RichTextTableLayoutTests.Model(0); var table = document.Blocks.OfType<Table>().Single();
        table.Columns[0].Width = new GridLength(100); table.Columns[1].Width = new GridLength(100);
        var huge = (Run)((Paragraph)table.RowGroups[0].Rows[1].Cells[0].Blocks[0]).Inlines[0];
        huge.Text = "M"; huge.FontSize = 600;
        var editor = Editor(document); var start = table.RowGroups[0].Rows[0].Cells[1].Blocks[0].ContentStart;
        var next = start.GetLineStartPosition(1, out int moved);
        Assert.Equal(1, moved); Assert.Same(table.RowGroups[0].Rows[1].Cells[1].Blocks[0], next!.Paragraph);
    }

    [Fact]
    public void EmptyCell_ExposesItsOwnZeroWidthCaretRectangle()
    {
        var document = RichTextTableLayoutTests.Model(5); var table = document.Blocks.OfType<Table>().Single();
        var empty = table.RowGroups[0].Rows[0].Cells[0]; Assert.Empty(empty.Blocks); var editor = Editor(document);
        var position = empty.ContentStart; Assert.True(position.HasValidLayout);
        var rect = position.GetCharacterRect(LogicalDirection.Forward);
        Assert.False(rect.IsEmpty); Assert.Equal(0, rect.Width); Assert.True(rect.Height > 0);
        Assert.Equal(position.DocumentOffset,
            editor.GetPositionFromPoint(new Point(rect.X + 0.1, rect.Y + rect.Height / 2), true)!.DocumentOffset);
    }

    [Fact]
    public void Mutation_InvalidatesAndRegeneratesTheCurrentLayout()
    {
        var paragraph = P("短行"); var editor = Editor(new FlowDocument(paragraph), 180); var position = paragraph.ContentStart;
        Assert.True(position.HasValidLayout); ((Run)paragraph.Inlines[0]).Text = new string('长', 80);
        Assert.False(position.HasValidLayout); Assert.False(position.GetCharacterRect(LogicalDirection.Forward).IsEmpty);
        Assert.True(position.HasValidLayout); Assert.True(paragraph.ContentEnd.GetLineStartPosition(0)!.DocumentOffset > 0);
    }

    [Fact]
    public void Scrolling_TranslatesCharacterBoundsByTheActualOffsets()
    {
        var document = RichTextTableLayoutTests.Model(0); var table = document.Blocks.OfType<Table>().Single();
        table.Columns[0].Width = new GridLength(500);
        ((Run)((Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks[0]).Inlines[0]).Text = new string('M', 2000);
        var editor = Editor(document); var position = table.RowGroups[0].Rows[0].Cells[0].ContentStart;
        var before = position.GetCharacterRect(LogicalDirection.Forward);
        Assert.False(before.IsEmpty);
        editor.ScrollToHorizontalOffset(20); editor.ScrollToVerticalOffset(3);
        Assert.True(editor.HorizontalOffset > 0 && editor.VerticalOffset > 0);
        var after = position.GetCharacterRect(LogicalDirection.Forward);
        Assert.False(after.IsEmpty);
        Assert.Equal(before.X - editor.HorizontalOffset, after.X, 4);
        Assert.Equal(before.Y - editor.VerticalOffset, after.Y, 4);
    }

    [Fact]
    public void FontChanges_InvalidateAndRefreshCharacterHeight()
    {
        var paragraph = P("Miii中文"); var editor = Editor(new FlowDocument(paragraph));
        var before = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        ((Run)paragraph.Inlines[0]).FontSize = 36;
        Assert.False(paragraph.ContentStart.HasValidLayout);
        var after = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Assert.True(after.Height > before.Height); Assert.True(paragraph.ContentStart.HasValidLayout);
    }

    [Fact]
    public void ReplacedDocument_AndDetachedRunHaveNoCurrentGeometry()
    {
        var paragraph = P("旧文本"); var document = new FlowDocument(paragraph); var editor = Editor(document);
        var old = paragraph.ContentStart; editor.Document = new FlowDocument(P("新文本"));
        Assert.False(old.HasValidLayout); Assert.True(old.GetCharacterRect(LogicalDirection.Forward).IsEmpty);
        var run = (Run)((Paragraph)editor.Document.Blocks[0]).Inlines[0]; var detached = run.ContentStart;
        ((Paragraph)editor.Document.Blocks[0]).Inlines.Remove(run);
        Assert.False(detached.HasValidLayout); Assert.True(detached.GetCharacterRect(LogicalDirection.Forward).IsEmpty);
    }

    [Fact]
    public void EmptyDocument_HasAnInsertionRectangleWithoutCreatingContent()
    {
        var document = new FlowDocument(); var editor = Editor(document);
        Assert.Empty(document.Blocks); Assert.True(document.ContentStart.HasValidLayout);
        var rect = document.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Assert.False(rect.IsEmpty); Assert.Equal(0, rect.Width); Assert.True(rect.Height > 0);
        Assert.True(document.ContentStart.IsAtLineStartPosition);
        Assert.Equal(0, document.ContentStart.GetLineStartPosition(0)!.DocumentOffset);
    }

    [Fact]
    public void LayoutCallbackReplacingTheDocument_RetiresTheOldQuery()
    {
        var document = new FlowDocument(P("原文")); var editor = new SwitchingEditor { Document = document, Width = 180, Height = 100 };
        editor.Measure(new Size(180, 100)); editor.Arrange(new Rect(0, 0, 180, 100));
        var position = document.ContentStart;
        editor.Replacement = new FlowDocument(P("新的文档")); editor.Switch = true; editor.InvalidateMeasure();
        Assert.True(position.GetCharacterRect(LogicalDirection.Forward).IsEmpty);
        Assert.Same(editor.Replacement, editor.Document);
    }

    [Theory, InlineData(-1), InlineData(2), InlineData(int.MaxValue)]
    public void InvalidLogicalDirection_IsRejected(int value)
    {
        var position = new FlowDocument(P("文本")).ContentStart;
        Assert.Throws<ArgumentException>(() => position.GetCharacterRect((LogicalDirection)value));
    }

    private static Paragraph P(string text) => new(new Run(text));
    private sealed class SwitchingEditor : RichTextBox
    {
        internal bool Switch;
        internal FlowDocument? Replacement;
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Switch) { Switch = false; Document = Replacement!; }
            return base.MeasureOverride(availableSize);
        }
    }
    private static RichTextBox Editor(FlowDocument document, double width = 420)
    {
        document.FontSize = 15;
        var editor = new RichTextBox(document) { Width = width, Height = 260, Padding = new Thickness(5), BorderThickness = new Thickness(2) };
        editor.Measure(new Size(width, 260)); editor.Arrange(new Rect(0, 0, width, 260));
        Assert.True(((IImeSupport)editor).TryGetImeTextRangeGeometry(0, 0, false, out _));
        return editor;
    }
    private static void Equal(Rect expected, Rect actual)
    {
        Assert.False(actual.IsEmpty);
        Assert.Equal(expected.X, actual.X, 4); Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Width, actual.Width, 4); Assert.Equal(expected.Height, actual.Height, 4);
    }
}
