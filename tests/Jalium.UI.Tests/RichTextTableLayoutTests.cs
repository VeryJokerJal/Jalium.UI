using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

public class RichTextTableLayoutTests
{
    public static IEnumerable<object[]> ModelsAndWidths =>
        from model in Enumerable.Range(0, 6) from width in new[] { 180, 420, 760 } select new object[] { model, width };

    [Theory, MemberData(nameof(ModelsAndWidths))]
    public void PaintAndPointQueries_ReachEverySourceCell(int model, int width)
    {
        var document = Model(model);
        var runs = document.EnumerateParagraphs().SelectMany(p => p.Inlines.OfType<Run>()).Where(r => r.Text.Length != 0).ToArray();
        var brushes = new Dictionary<Run, Brush>();
        for (int i = 0; i < runs.Length; i++)
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(20 + i), 70, 130));
            runs[i].Foreground = brush; brushes.Add(runs[i], brush);
        }
        var editor = Editor(document, width); string before = document.GetText(); var painter = Paint(editor);
        foreach (var run in runs)
        {
            var pieces = painter.Text.Where(t => ReferenceEquals(t.Brush, brushes[run])).ToArray();
            Assert.NotEmpty(pieces); Assert.Equal(run.Text, string.Concat(pieces.Select(t => t.Value)));
            int consumed = 0;
            foreach (var text in pieces)
            {
                Assert.True(double.IsFinite(text.Origin.X) && double.IsFinite(text.Origin.Y));
                var point = new Point(text.Origin.X + 0.01, text.Origin.Y + 0.01);
                var position = editor.GetPositionFromPoint(point, true);
                Assert.NotNull(position); Assert.Equal(run.ContentStart.DocumentOffset + consumed, position.DocumentOffset);
                Assert.NotNull(editor.GetPositionFromPoint(point, false));
                consumed += text.Value.Length;
            }
        }
        Assert.Equal(before, document.GetText());
    }

    [Fact]
    public void RowsAndColumns_KeepParallelCellOrigins()
    {
        var editor = Editor(Model(0)); var p = Paint(editor);
        var a = Text(p, "甲🙂"); var b = Text(p, "乙"); var c = Text(p, "丙"); var d = Text(p, "丁");
        Assert.Equal(a.Y, b.Y, 5); Assert.True(b.X > a.X + 50);
        Assert.Equal(c.Y, d.Y, 5); Assert.True(c.Y > a.Y + 10);
        Assert.Equal(a.X, c.X, 5); Assert.Equal(b.X, d.X, 5);
    }

    [Fact]
    public void RowSpan_DoesNotOccupyTheFollowingRowsNextCell()
    {
        var p = Paint(Editor(Model(1))); var a = Text(p, "跨行"); var b = Text(p, "上"); var c = Text(p, "下");
        Assert.Equal(a.Y, b.Y, 5); Assert.Equal(b.X, c.X, 5);
        Assert.True(c.Y > b.Y && b.X > a.X);
    }

    [Fact]
    public void ColumnSpan_FillsBothTracksBeforeTheNextRow()
    {
        var d = Model(2); var t = d.Blocks.OfType<Table>().Single(); var marker = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        t.RowGroups[0].Rows[0].Cells[0].Background = marker;
        var p = Paint(Editor(d, 420)); var rectangle = Assert.Single(p.Rectangles.Where(x => ReferenceEquals(x.Brush, marker))).Bounds;
        Assert.Equal(420, rectangle.Width, 3);
        Assert.True(Text(p, "左").Y > Text(p, "标题").Y); Assert.True(Text(p, "右").X > Text(p, "左").X);
    }

    [Fact]
    public void Selection_PaintsBeforeOpaqueGlyphsAndAfterCellSurfaces()
    {
        var d = Model(0); var editor = Editor(d); var runs = d.EnumerateParagraphs().SelectMany(p => p.Inlines.OfType<Run>()).ToArray();
        var selection = new SolidColorBrush(Color.FromRgb(30, 80, 150)); editor.SelectionBrush = selection;
        editor.Selection.Select(runs[0].ContentStart, runs[1].ContentEnd);
        var p = Paint(editor); var selected = p.Rectangles.Where(r => ReferenceEquals(r.Brush, selection)).ToArray();
        Assert.True(selected.Length >= 2); Assert.True(selected.All(r => r.Bounds.Width > 0));
        Assert.True(selected.Max(r => r.Order) < p.Text.Min(t => t.Order));
        Assert.True(p.Rectangles.Where(r => !ReferenceEquals(r.Brush, selection)).Min(r => r.Order) < selected.Min(r => r.Order));
    }

    [Fact]
    public void ColumnWidthAndCellPadding_ReflowWithoutReparentingContent()
    {
        var d = Model(0); var t = d.Blocks.OfType<Table>().Single(); var c = t.RowGroups[0].Rows[0].Cells[1]; var editor = Editor(d);
        var old = Text(Paint(editor), "乙"); var paragraph = c.Blocks[0]; t.Columns[0].Width = new GridLength(80);
        var changed = Text(Paint(editor), "乙"); Assert.True(changed.X < old.X - 40);
        c.Padding = new Thickness(20, 14, 20, 14); var padded = Text(Paint(editor), "乙");
        Assert.True(padded.X > changed.X && padded.Y > changed.Y); Assert.Same(paragraph, c.Blocks[0]);
    }

    [Fact]
    public void TableChanges_InvalidateTheOwningDocumentAndDetachCleanly()
    {
        var d = Model(0); var t = d.Blocks.OfType<Table>().Single(); int changes = 0; d.ContentChanged += (_, _) => changes++;
        void Changed(Action action) { int old = changes; action(); Assert.True(changes > old); }
        Changed(() => t.Columns[0].Width = new GridLength(90));
        var row = t.RowGroups[0].Rows[0]; var cell = row.Cells[0];
        Changed(() => cell.Padding = new Thickness(8));
        Changed(() => ((Run)((Paragraph)cell.Blocks[0]).Inlines[0]).Text = "变更");
        Changed(() => row.Cells.Remove(cell)); int detached = changes;
        cell.Padding = new Thickness(16); Assert.Equal(detached, changes);
        var column = t.Columns[0]; Changed(() => t.Columns.Remove(column)); detached = changes;
        column.Width = new GridLength(11); Assert.Equal(detached, changes);
    }

    [Fact]
    public void CellRowAndTableEndpoints_IncludeTheirOwnProjectedSeparators()
    {
        var d = Model(0); var t = d.Blocks.OfType<Table>().Single(); var row = t.RowGroups[0].Rows[0]; var cell = row.Cells[0];
        Assert.Equal("甲🙂\n", new TextRange(cell.ContentStart, cell.ContentEnd).Text);
        Assert.Equal("甲🙂\n\t乙\n\t", new TextRange(row.ContentStart, row.ContentEnd).Text);
        Assert.Equal(d.GetText(), new TextRange(t.ContentStart, t.ContentEnd).Text);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2), InlineData(3), InlineData(4)]
    public void CompletedCollectionMutations_NotifyTheDocument(int kind)
    {
        var d = Model(0); var t = d.Blocks.OfType<Table>().Single(); int changes = 0; d.ContentChanged += (_, _) => changes++;
        switch (kind)
        {
            case 0: t.Columns.Add(new TableColumn()); break;
            case 1: t.RowGroups.Add(new TableRowGroup()); break;
            case 2: t.RowGroups[0].Rows.Add(new TableRow()); break;
            case 3: t.RowGroups[0].Rows[0].Cells.Add(Cell("新增")); break;
            case 4: t.RowGroups[0].Rows[0].Cells.Clear(); break;
        }
        Assert.True(changes > 0);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5)]
    public void EveryProjectedRange_ReadsTheSameUtf16Slice(int model)
    {
        var document = Model(model); string text = document.GetText();
        for (int start = 0; start <= text.Length; start++)
            for (int end = start; end <= text.Length; end++)
                Assert.Equal(text[start..end], new TextRange(
                    new TextPointer(document, null, start, LogicalDirection.Forward),
                    new TextPointer(document, null, end, LogicalDirection.Backward)).Text);
    }

    [Fact]
    public void FormattingRange_TraversesTablesAndKeepsFollowingRootsUnchanged()
    {
        var document = Model(0); var before = P("前方"); document.Blocks.Insert(0, before);
        var after = P("后方"); document.Blocks.Add(after);
        var table = document.Blocks.OfType<Table>().Single();
        var first = (Run)((Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks[0]).Inlines[0];
        var second = (Run)((Paragraph)table.RowGroups[0].Rows[0].Cells[1].Blocks[0]).Inlines[0];
        var range = new TextRange(first.ContentStart, first.ContentEnd);
        range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        Assert.Equal(FontWeights.Bold, first.FontWeight);
        Assert.NotEqual(FontWeights.Bold, second.FontWeight);
        Assert.NotEqual(FontWeights.Bold, ((Run)before.Inlines[0]).FontWeight);
        Assert.NotEqual(FontWeights.Bold, ((Run)after.Inlines[0]).FontWeight);
        Assert.Equal(FontWeights.Bold, range.GetPropertyValue(TextElement.FontWeightProperty));
        Assert.Equal(DependencyProperty.UnsetValue,
            new TextRange(first.ContentStart, second.ContentEnd).GetPropertyValue(TextElement.FontWeightProperty));
        range.ClearAllProperties(); Assert.NotEqual(FontWeights.Bold, first.FontWeight);
    }

    [Fact]
    public void PaddingHit_StaysInItsCellInsteadOfJumpingToNearbyNeighborText()
    {
        var document = Model(0); var table = document.Blocks.OfType<Table>().Single();
        table.Columns[0].Width = new GridLength(100); var editor = Editor(document);
        var p = Paint(editor); var first = Text(p, "甲🙂");
        var position = editor.GetPositionFromPoint(new Point(98, first.Y + 1), true);
        Assert.NotNull(position); Assert.Same(table.RowGroups[0].Rows[0].Cells[0].Blocks[0], position.Paragraph);
    }

    [Fact]
    public void DownArrow_UsesTheNextVisualRowInTheSameColumn()
    {
        var document = Model(0); var table = document.Blocks.OfType<Table>().Single(); var editor = Editor(document);
        var start = ((Paragraph)table.RowGroups[0].Rows[0].Cells[1].Blocks[0]).ContentStart;
        editor.Selection.Select(start, start);
        var destination = (TextPointer)typeof(RichTextBox).GetMethod("FindPositionOnAdjacentLine", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(editor, new object[] { 1 })!;
        Assert.Same(table.RowGroups[0].Rows[1].Cells[1].Blocks[0], destination.Paragraph);
    }

    [Fact]
    public void ScrollOffsets_MovePaintCaretAndImeRangesTogether()
    {
        var document = Model(0); var table = document.Blocks.OfType<Table>().Single(); table.Columns[0].Width = new GridLength(500);
        var editor = Editor(document, 420); var run = (Run)((Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks[0]).Inlines[0];
        var old = Text(Paint(editor), run.Text);
        typeof(RichTextBox).GetField("_horizontalOffset", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, 20d);
        typeof(RichTextBox).GetField("_verticalOffset", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, 3d);
        var moved = Text(Paint(editor), run.Text); Assert.Equal(old.X - 20, moved.X, 5); Assert.Equal(old.Y - 3, moved.Y, 5);
        Assert.True(((IImeSupport)editor).TryGetImeTextRangeGeometry(run.ContentStart.DocumentOffset, run.Text.Length, false, out var geometry));
        Assert.Equal(moved.X, geometry.Rectangle.X, 5); Assert.Equal(moved.Y, geometry.Rectangle.Y, 5);
    }

    [Fact]
    public void EmptyCellTyping_CreatesItsParagraphAndPreservesTheNeighbor()
    {
        var document = Model(5); var table = document.Blocks.OfType<Table>().Single(); var cell = table.RowGroups[0].Rows[0].Cells[0];
        var editor = Editor(document); var position = document.GetPositionAtOffset(0, LogicalDirection.Forward)!;
        Assert.Same(cell, position.Parent); Assert.True(position.IsAtInsertionPosition);
        editor.CaretPosition = position;
        var method = typeof(RichTextBox).GetMethod("TryReplaceImeText", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)method.Invoke(editor, new object[] { 0, 0, "新🙂" })!);
        Assert.Single(document.Blocks); Assert.Equal("新🙂", ((Run)Assert.IsType<Paragraph>(Assert.Single(cell.Blocks)).Inlines[0]).Text);
        Assert.Equal("空格旁", ((Run)((Paragraph)table.RowGroups[0].Rows[0].Cells[1].Blocks[0]).Inlines[0]).Text);
        Assert.True(editor.CanUndo); editor.Undo(); Assert.Empty(cell.Blocks); Assert.Same(table, Assert.Single(document.Blocks));
        editor.Redo(); Assert.Single(cell.Blocks);
    }

    [Fact]
    public void PublicSelection_UpdatesCaretAndAnOldSelectionCannotMoveItLater()
    {
        var document = Model(0); var editor = Editor(document); var paragraphs = document.EnumerateParagraphs().ToArray();
        var old = editor.Selection; old.Select(paragraphs[1].ContentStart, paragraphs[1].ContentEnd);
        Assert.Equal(paragraphs[1].ContentEnd.DocumentOffset, editor.CaretPosition!.DocumentOffset);
        editor.SelectAll(); int caret = editor.CaretPosition!.DocumentOffset;
        old.Select(paragraphs[0].ContentStart, paragraphs[0].ContentStart);
        Assert.Equal(caret, editor.CaretPosition!.DocumentOffset);
    }

    [Fact]
    public void ListsAndParagraphMargins_UseTheSamePaintAndHitMapWithoutTables()
    {
        var document = new FlowDocument(); var paragraph = P("缩进"); paragraph.Margin = new Thickness(9, 13, 3, 7);
        var item = new ListItem(); item.Blocks.Add(paragraph); var list = new DocList(); list.ListItems.Add(item); document.Blocks.Add(list);
        var editor = Editor(document); var point = Text(Paint(editor), "缩进"); Assert.True(point.X >= 29 && point.Y >= 13);
        Assert.Equal(paragraph.ContentStart.DocumentOffset,
            editor.GetPositionFromPoint(new Point(point.X + 0.01, point.Y + 0.01), true)!.DocumentOffset);
    }

    [Fact]
    public void NarrowCell_ClipsItsSelectionBeforeTheNeighbor()
    {
        var document = Model(0); var table = document.Blocks.OfType<Table>().Single(); var cell = table.RowGroups[0].Rows[0].Cells[0];
        cell.Padding = new Thickness(0); table.Columns[0].Width = new GridLength(12);
        var editor = Editor(document); var brush = new SolidColorBrush(Color.FromRgb(12, 180, 40)); editor.SelectionBrush = brush;
        editor.Selection.Select(cell.ContentStart, cell.ContentEnd); var p = Paint(editor);
        var rectangles = p.Rectangles.Where(x => ReferenceEquals(x.Brush, brush)).ToArray();
        Assert.NotEmpty(rectangles); Assert.All(rectangles, rect => Assert.True(rect.Bounds.Right <= 11.001));
    }

    [Fact]
    public void WrappedFallbackRun_PaintsEachSourceSliceOnce()
    {
        var document = new FlowDocument(); const string value = "one two three four five 中文🙂";
        var cell = Cell(value); document.Blocks.Add(T(R(cell, Cell("边")))); var editor = Editor(document, 180);
        var texts = Paint(editor).Text.Where(t => t.Value != "边").ToArray();
        Assert.True(texts.Length > 1); Assert.Equal(value, string.Concat(texts.Select(t => t.Value)));
    }

    private static RichTextBox Editor(FlowDocument document, double width = 420)
    {
        document.FontSize = 15;
        var editor = new RichTextBox { Document = document, Width = width, Height = 260,
            Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        editor.Measure(new Size(width, 260)); editor.Arrange(new Rect(0, 0, width, 260)); return editor;
    }
    private static Recorder Paint(RichTextBox editor)
    {
        var p = new Recorder(); typeof(RichTextBox).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic,
            new[] { typeof(DrawingContext) })!.Invoke(editor, new object[] { p }); return p;
    }
    private static Point Text(Recorder p, string text) => Assert.Single(p.Text.Where(t => t.Value == text)).Origin;
    private static Paragraph P(string text) => new(new Run(text));
    private static TableCell Cell(string text) => new(P(text));
    private static Table T(params TableRow[] rows)
    {
        var t = new Table(); t.Columns.Add(new TableColumn()); t.Columns.Add(new TableColumn());
        var g = new TableRowGroup(); foreach (var row in rows) g.Rows.Add(row); t.RowGroups.Add(g); return t;
    }
    private static TableRow R(params TableCell[] cells) { var r = new TableRow(); foreach (var c in cells) r.Cells.Add(c); return r; }
    internal static FlowDocument Model(int model)
    {
        var d = new FlowDocument(); var basic = T(R(Cell("甲🙂"), Cell("乙")), R(Cell("丙"), Cell("丁")));
        if (model == 0) d.Blocks.Add(basic);
        else if (model == 1) d.Blocks.Add(T(R(new TableCell(P("跨行")) { RowSpan = 2 }, Cell("上")), R(Cell("下"))));
        else if (model == 2) d.Blocks.Add(T(R(new TableCell(P("标题")) { ColumnSpan = 2 }), R(Cell("左"), Cell("右"))));
        else if (model == 3) { var cell = new TableCell(); cell.Blocks.Add(P("之前")); cell.Blocks.Add(basic); cell.Blocks.Add(P("之后")); d.Blocks.Add(T(R(cell, Cell("外侧")))); }
        else if (model == 4) { var list = new DocList(); var item = new ListItem(); item.Blocks.Add(P("列表开头")); item.Blocks.Add(basic); item.Blocks.Add(P("列表结尾")); list.ListItems.Add(item); d.Blocks.Add(list); }
        else d.Blocks.Add(T(R(new TableCell(), Cell("空格旁")), R(Cell("中文🙂"), new TableCell())));
        return d;
    }
    private sealed class Recorder : DrawingContextAdapter
    {
        private int _order;
        internal readonly List<(string Value, Point Origin, int Order, Brush? Brush)> Text = new();
        internal readonly List<(Brush? Brush, Rect Bounds, int Order)> Rectangles = new();
        public override void DrawText(FormattedText text, Point origin) => Text.Add((text.Text, origin, _order++, text.Foreground));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect bounds) => Rectangles.Add((brush, bounds, _order++));
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRoundedRectangle(Brush? b, Pen? p, Rect r, double x, double y) { }
        public override void DrawEllipse(Brush? b, Pen? p, Point c, double x, double y) { }
        public override void DrawGeometry(Brush? b, Pen? p, Geometry g) { }
        public override void DrawImage(ImageSource i, Rect r) { }
        public override void DrawBackdropEffect(Rect r, IBackdropEffect e, CornerRadius c) { }
        public override void PushTransform(Transform t) { }
        public override void PushClip(Geometry g) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
