using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class RichTextBoxInlineBackgroundTests
{
    [Theory]
    [InlineData("Miii中文🙂e\u0301")]
    [InlineData("abc")]
    [InlineData("אבגד")]
    [InlineData("العربية")]
    [InlineData("  spaces  ")]
    public void RunBackgroundUsesTheMeasuredTextArea(string text)
    {
        var brush = new SolidColorBrush(Colors.Yellow);
        var run = new Run(text) { Background = brush };
        var editor = Editor(new FlowDocument(new Paragraph(run)));
        var paint = Paint(editor);
        var rectangles = paint.Rectangles.Where(item => ReferenceEquals(item.Brush, brush)).ToArray();
        Assert.NotEmpty(rectangles);
        Assert.All(rectangles, item => Assert.True(item.Bounds.Width > 0 && item.Bounds.Height > 0));
        Assert.True(rectangles.Max(item => item.Order) < paint.Text.Min(item => item.Order));
        Assert.Equal(text + "\n", editor.Document.GetText());
    }

    [Fact]
    public void BackgroundBoundsFollowThePublicCaretAdvances()
    {
        var brush = new SolidColorBrush(Colors.Yellow);
        var run = new Run("Miii中文🙂e\u0301") { Background = brush };
        var editor = Editor(new FlowDocument(ParagraphWith(new Run("A"), run, new Run("Z"))));
        var rectangle = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush))).Bounds;
        var start = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var end = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        Assert.Equal(start.X, rectangle.Left, 2);
        Assert.Equal(end.Right - start.X, rectangle.Width, 2);
        Assert.Equal(start.Top, rectangle.Top, 2);
        Assert.Equal(start.Height, rectangle.Height, 2);
    }

    [Fact]
    public void OneTranslucentSpanPaintsAdjacentRunsOnlyOnce()
    {
        var brush = new SolidColorBrush(Color.FromArgb(128, 30, 90, 180));
        var span = new Span { Background = brush };
        span.Inlines.Add(new Run("Miii")); span.Inlines.Add(new Run("中文🙂"));
        var editor = Editor(new FlowDocument(new Paragraph(span)));
        var rectangle = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        Assert.Equal(.5, ((SolidColorBrush)rectangle.Brush!).Color.A / 255d, 2);
        Assert.True(rectangle.Bounds.Width > 20);
    }

    [Fact]
    public void InnerBackgroundOverlaysItsAncestorBeforeSelectionAndText()
    {
        var outer = new SolidColorBrush(Colors.Yellow); var inner = new SolidColorBrush(Colors.Blue);
        var selected = new SolidColorBrush(Colors.Magenta);
        var run = new Run("中文🙂") { Background = inner };
        var span = new Span { Background = outer }; span.Inlines.Add(new Run("前")); span.Inlines.Add(run); span.Inlines.Add(new Run("后"));
        var editor = Editor(new FlowDocument(new Paragraph(span))); editor.SelectionBrush = selected;
        editor.Selection.Select(run.ContentStart, run.ContentEnd);
        var paint = Paint(editor);
        var a = Assert.Single(paint.Rectangles.Where(item => ReferenceEquals(item.Brush, outer)));
        var b = Assert.Single(paint.Rectangles.Where(item => ReferenceEquals(item.Brush, inner)));
        var selection = paint.Rectangles.Where(item => ReferenceEquals(item.Brush, selected)).ToArray();
        Assert.NotEmpty(selection);
        Assert.True(a.Order < b.Order && b.Order < selection.Min(item => item.Order));
        Assert.True(selection.Max(item => item.Order) < paint.Text.Min(item => item.Order));
        Assert.True(a.Bounds.Contains(b.Bounds));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void MixedDirectionBackgroundDoesNotCoverTheUnrelatedRun(bool rtl)
    {
        var brush = new SolidColorBrush(Colors.Yellow);
        var first = new Run("אבג") { Background = brush }; var middle = new Run("MID");
        var last = new Run("דהו") { Background = brush };
        var paragraph = ParagraphWith(first, middle, last); paragraph.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var editor = Editor(new FlowDocument(paragraph));
        var paint = Paint(editor); var rectangles = paint.Rectangles.Where(item => ReferenceEquals(item.Brush, brush)).ToArray();
        Assert.NotEmpty(rectangles);
        var start = middle.ContentStart.GetPositionAtOffset(1)!.GetCharacterRect(LogicalDirection.Forward);
        var end = middle.ContentStart.GetPositionAtOffset(2)!.GetCharacterRect(LogicalDirection.Forward);
        double center = (start.X + end.X) / 2;
        Assert.DoesNotContain(rectangles, item => center > item.Bounds.Left + .1 && center < item.Bounds.Right - .1);
    }

    [Fact]
    public void WrappingPaintsSeparateLineFragments()
    {
        var brush = new SolidColorBrush(Colors.Yellow);
        var run = new Run("one two three four five 中文🙂") { Background = brush };
        var editor = Editor(new FlowDocument(new Paragraph(run)), 105);
        var rectangles = Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)).ToArray();
        Assert.True(rectangles.Length > 1);
        Assert.Equal(rectangles.Length, rectangles.Select(item => item.Bounds.Top).Distinct().Count());
    }

    [Fact]
    public void GradientBrushRetainsItsOriginalBoundsAndIdentity()
    {
        var brush = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
        var run = new Run("Miii中文") { Background = brush };
        var editor = Editor(new FlowDocument(new Paragraph(run)));
        var rectangle = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        Assert.Equal(run.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Right - run.ContentStart.GetCharacterRect(LogicalDirection.Forward).X,
            rectangle.Bounds.Width, 2);
    }

    [Fact]
    public void AddingChangingAndClearingBackgroundKeepsTextAndSelection()
    {
        var run = new Run("Miii中文🙂"); var editor = Editor(new FlowDocument(new Paragraph(run)));
        editor.Selection.Select(run.ContentStart, run.ContentEnd); string text = editor.Selection.Text;
        var first = new SolidColorBrush(Colors.Yellow);
        run.Background = first; Assert.NotEmpty(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, first)));
        first.Opacity = .25; Assert.All(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, first)), item => Assert.Equal(.25, item.Opacity));
        var second = new SolidColorBrush(Colors.Blue); run.Background = second;
        var updated = Paint(editor); Assert.DoesNotContain(updated.Rectangles, item => ReferenceEquals(item.Brush, first));
        Assert.NotEmpty(updated.Rectangles.Where(item => ReferenceEquals(item.Brush, second)));
        run.Background = null; Assert.DoesNotContain(Paint(editor).Rectangles, item => ReferenceEquals(item.Brush, second));
        Assert.Equal(text, editor.Selection.Text); Assert.Equal(text + "\n", editor.Document.GetText());
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2), InlineData(3)]
    public void InlineBackgroundSurvivesParagraphListAndNestedTables(int kind)
    {
        var brush = new SolidColorBrush(Colors.Yellow); var run = new Run("Miii中文🙂") { Background = brush };
        Block block = new Paragraph(run);
        if (kind == 1) { var list = new DocList(); var item = new ListItem(); item.Blocks.Add(block); list.ListItems.Add(item); block = list; }
        else if (kind >= 2) { block = TableWith(block); if (kind == 3) block = TableWith(block); }
        var editor = Editor(new FlowDocument(block));
        var rectangle = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        Assert.True(rectangle.Bounds.Width > 0 && rectangle.Bounds.Height > 0);
        Assert.Equal(run.ContentStart.GetCharacterRect(LogicalDirection.Forward).Y, rectangle.Bounds.Y, 2);
    }

    [Fact]
    public void CellSurfacesPrecedeInlineBackgroundAndSelection()
    {
        var background = new SolidColorBrush(Colors.Yellow); var cellBackground = new SolidColorBrush(Colors.Blue);
        var selection = new SolidColorBrush(Colors.Magenta); var run = new Run("中文🙂") { Background = background };
        var table = TableWith(new Paragraph(run)); table.RowGroups[0].Rows[0].Cells[0].Background = cellBackground;
        var editor = Editor(new FlowDocument(table)); editor.SelectionBrush = selection; editor.Selection.Select(run.ContentStart, run.ContentEnd);
        var paint = Paint(editor);
        int cell = paint.Rectangles.Where(item => ReferenceEquals(item.Brush, cellBackground)).Max(item => item.Order);
        int inline = Assert.Single(paint.Rectangles.Where(item => ReferenceEquals(item.Brush, background))).Order;
        int selected = paint.Rectangles.Where(item => ReferenceEquals(item.Brush, selection)).Min(item => item.Order);
        Assert.True(cell < inline && inline < selected && selected < paint.Text.Min(item => item.Order));
    }

    [Fact]
    public void CellBackgroundPaintHasItsOwnCellClip()
    {
        var brush = new LinearGradientBrush(Colors.Red, Colors.Blue, 0); var run = new Run("Miii") { Background = brush };
        var table = TableWith(new Paragraph(run)); var cell = table.RowGroups[0].Rows[0].Cells[0];
        cell.Padding = new Thickness(15); cell.BorderThickness = new Thickness(7); cell.BorderBrush = Brushes.Black;
        var editor = Editor(new FlowDocument(table));
        var rectangle = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        Assert.True(rectangle.Clips.Count >= 2);
        Assert.Contains(rectangle.Clips, clip => clip.Left >= 7 && clip.Right <= editor.RenderSize.Width - 7 && clip.Width < editor.RenderSize.Width);
        Assert.Equal(run.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Right - run.ContentStart.GetCharacterRect(LogicalDirection.Forward).X, rectangle.Bounds.Width, 2);
    }

    [Fact]
    public void SpanBackgroundKeepsTheTextAndAllocatedEmbeddedObjectRange()
    {
        var brush = new SolidColorBrush(Colors.Yellow); var span = new Span { Background = brush };
        var first = new Run("A"); var last = new Run("B"); span.Inlines.Add(first);
        var objectSlot = new InlineUIContainer(new Border { Width = 12, Height = 12 }); span.Inlines.Add(objectSlot); span.Inlines.Add(last);
        var editor = Editor(new FlowDocument(new Paragraph(span)));
        Assert.Equal(first.ContentEnd.DocumentOffset, objectSlot.ContentStart.DocumentOffset);
        Assert.Equal(objectSlot.ContentEnd.DocumentOffset, last.ContentStart.DocumentOffset);
        var rectangle = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        Assert.Equal(last.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Right - first.ContentStart.GetCharacterRect(LogicalDirection.Forward).X,
            rectangle.Bounds.Width, 2);
    }

    [Fact]
    public void ScrollingMovesBackgroundWithItsTextAndRetainsTheViewportClip()
    {
        var brush = new SolidColorBrush(Colors.Yellow); var run = new Run("Miii中文🙂") { Background = brush };
        var editor = Editor(new FlowDocument(new Paragraph(run))); editor.Padding = new Thickness(9);
        var original = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        editor.ScrollToHorizontalOffset(7); editor.ScrollToVerticalOffset(5);
        var moved = Assert.Single(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        Assert.Equal(original.Bounds.Left - 7, moved.Bounds.Left, 2); Assert.Equal(original.Bounds.Top - 5, moved.Bounds.Top, 2);
        Assert.Equal(original.Bounds.Width, moved.Bounds.Width, 2);
        Assert.Contains(moved.Clips, clip => clip.Left == 9 && clip.Top == 9 && clip.Width == editor.RenderSize.Width - 18);
    }

    [Fact]
    public void GlyphPreviewOmitsTheInlineSurfaceAndRestoresNormalPainting()
    {
        var brush = new SolidColorBrush(Colors.Yellow); var editor = Editor(new FlowDocument(new Paragraph(new Run("Miii中文🙂") { Background = brush })));
        Assert.NotEmpty(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
        // Enter the same thread-local presentation state as the SDK capture, without a GPU or injected input.
        var type = typeof(MacOSWritingToolsPresentation); var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var depth = type.GetField("_previewDepth", flags)!; var owner = type.GetField("_previewOwner", flags)!;
        object? savedDepth = depth.GetValue(null), savedOwner = owner.GetValue(null);
        try
        {
            depth.SetValue(null, 1); owner.SetValue(null, editor);
            var preview = Paint(editor); Assert.NotEmpty(preview.Text);
            Assert.DoesNotContain(preview.Rectangles, item => ReferenceEquals(item.Brush, brush));
        }
        finally { depth.SetValue(null, savedDepth); owner.SetValue(null, savedOwner); }
        Assert.NotEmpty(Paint(editor).Rectangles.Where(item => ReferenceEquals(item.Brush, brush)));
    }

    [Fact]
    public void EmptyRunDoesNotInventAColoredRectangle()
    {
        var brush = new SolidColorBrush(Colors.Yellow); var editor = Editor(new FlowDocument(new Paragraph(new Run("") { Background = brush })));
        Assert.DoesNotContain(Paint(editor).Rectangles, item => ReferenceEquals(item.Brush, brush));
    }

    [Fact]
    public void UnstyledRunKeepsTheEditorSurface()
    {
        var editor = Editor(new FlowDocument(new Paragraph(new Run("Miii中文🙂")))); editor.Background = Brushes.White;
        var paint = Paint(editor);
        Assert.NotEmpty(paint.Text);
        Assert.DoesNotContain(paint.Rectangles, item => item.Brush is not null && !ReferenceEquals(item.Brush, Brushes.White));
    }

    private static Paragraph ParagraphWith(params Inline[] inlines)
    {
        var paragraph = new Paragraph(); foreach (var inline in inlines) paragraph.Inlines.Add(inline); return paragraph;
    }
    private static Table TableWith(Block block)
    {
        var cell = new TableCell(block); var row = new TableRow(); row.Cells.Add(cell);
        var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table(); table.Columns.Add(new TableColumn()); table.RowGroups.Add(group); return table;
    }
    private static RichTextBox Editor(FlowDocument document, double width = 340)
    {
        document.FontSize = 18;
        var editor = new RichTextBox(document) { Width = width, Height = 260, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        editor.Measure(new Size(width, 260)); editor.Arrange(new Rect(0, 0, width, 260)); return editor;
    }
    private static Recorder Paint(RichTextBox editor)
    {
        var context = new Recorder(); typeof(RichTextBox).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic,
            new[] { typeof(DrawingContext) })!.Invoke(editor, new object[] { context }); return context;
    }
    private sealed class Recorder : DrawingContextAdapter
    {
        private int _order;
        private readonly List<Rect> _clips = [];
        private readonly Stack<int> _states = [];
        internal readonly List<(Brush? Brush, Rect Bounds, int Order, double Opacity, List<Rect> Clips)> Rectangles = [];
        internal readonly List<(string Value, Point Origin, int Order)> Text = [];
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => Rectangles.Add((brush, rect, _order++, brush?.Opacity ?? 1, new(_clips)));
        public override void DrawText(FormattedText text, Point point) => Text.Add((text.Text, point, _order++));
        public override void PushClip(Geometry clip) { _states.Push(_clips.Count); _clips.Add(clip.Bounds); }
        public override void PushTransform(Transform transform) => _states.Push(_clips.Count);
        public override void PushOpacity(double opacity) => _states.Push(_clips.Count);
        public override void Pop() { int count = _states.Pop(); _clips.RemoveRange(count, _clips.Count - count); }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void Close() { }
    }
}
