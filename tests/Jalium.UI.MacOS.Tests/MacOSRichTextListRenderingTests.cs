using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichTextListRenderingTests
{
    public static IEnumerable<object[]> Styles()
    {
        yield return [TextMarkerStyle.None, "", ""];
        yield return [TextMarkerStyle.Disc, "•", "•"];
        yield return [TextMarkerStyle.Circle, "◦", "◦"];
        yield return [TextMarkerStyle.Square, "▪", "▪"];
        yield return [TextMarkerStyle.Box, "▫", "▫"];
        yield return [TextMarkerStyle.Decimal, "3.", "4."];
        yield return [TextMarkerStyle.LowerLatin, "c.", "d."];
        yield return [TextMarkerStyle.UpperLatin, "C.", "D."];
        yield return [TextMarkerStyle.LowerRoman, "iii.", "iv."];
        yield return [TextMarkerStyle.UpperRoman, "III.", "IV."];
    }

    [Theory, MemberData(nameof(Styles))]
    public void MarkersArePaintedOnceWithoutBecomingAuthoredText(TextMarkerStyle style, string first, string second)
    {
        var list = List(style, 3, P("body"), P("next")); var document = new FlowDocument(list);
        var editor = Editor(document); var paint = Paint(editor);
        Assert.Equal(new[] { first, second }.Where(x => x.Length != 0), paint.Text.Where(x => x.Value is not ("body" or "next")).Select(x => x.Value));
        Assert.Equal("body\nnext\n", document.GetText());
        Assert.Equal(2, list.ListItems.Count);
        Assert.Same(list, document.Blocks[0]);
    }

    [Fact]
    public void OneItemWithSeveralParagraphsDoesNotRepeatItsNumber()
    {
        var list = List(TextMarkerStyle.Decimal, 9, P("first"), P("last"));
        list.ListItems[0].Blocks.Add(P("continued"));
        var paint = Paint(Editor(new FlowDocument(list)));
        Assert.Equal(1, paint.Text.Count(x => x.Value == "9."));
        Assert.Equal(1, paint.Text.Count(x => x.Value == "10."));
        Assert.DoesNotContain(paint.Text, x => x.Value == "11.");
        Assert.True(paint.Text.Single(x => x.Value == "10.").Origin.Y > paint.Text.Single(x => x.Value == "continued").Origin.Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GutterFitsWideNumbersAndKeepsCaretGeometryInTheContent(bool rtl)
    {
        var paragraph = P("body");
        var list = List(TextMarkerStyle.Decimal, 999, paragraph, P("next"));
        list.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        list.MarkerOffset = 12;
        var editor = Editor(new FlowDocument(list), 260); var paint = Paint(editor);
        var marker = paint.Text.Single(x => x.Value == "999.");
        Assert.True(((IImeSupport)editor).TryGetImeTextRangeGeometry(paragraph.ContentStart.DocumentOffset, 4, false, out var range));
        if (rtl) Assert.True(marker.Origin.X >= range.Rectangle.Right + 11.9);
        else Assert.True(marker.Origin.X + marker.Width <= range.Rectangle.Left - 11.9);
        var first = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var hit = editor.GetPositionFromPoint(new Point(first.Left + .01, first.Top + .01), true);
        Assert.NotNull(hit); Assert.InRange(hit.DocumentOffset, 0, 1);
        Assert.Contains(paint.Text, x => x.Value == "1000.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkerUsesTheCurrentEditorOrItemForeground(bool local)
    {
        var list = List(TextMarkerStyle.Box, 1, P("body"));
        var editor = Editor(new FlowDocument(list)); editor.Foreground = Brushes.Red;
        if (local) list.ListItems[0].Foreground = Brushes.Blue;
        var marker = Paint(editor).Text.Single(x => x.Value == "▫");
        Assert.Same(local ? Brushes.Blue : Brushes.Red, marker.Brush);
    }

    [Fact]
    public void MarkerChangesReflowTheSameDocumentAndPreserveSelection()
    {
        var paragraph = P("body"); var list = List(TextMarkerStyle.Decimal, 3, paragraph);
        var document = new FlowDocument(list); var editor = Editor(document);
        editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
        var before = Paint(editor).Text.Single(x => x.Value == "body").Origin;
        list.MarkerOffset = 40; list.MarkerStyle = TextMarkerStyle.Box;
        var after = Paint(editor);
        Assert.Contains(after.Text, x => x.Value == "▫"); Assert.DoesNotContain(after.Text, x => x.Value == "3.");
        Assert.True(after.Text.Single(x => x.Value == "body").Origin.X > before.X + 10);
        Assert.Equal("body", editor.Selection.Text); Assert.Same(paragraph, list.ListItems[0].Blocks[0]);
    }

    [Fact]
    public void EmptyItemsHaveAVisibleMarkerWithoutAddingParagraphsToTheModel()
    {
        var list = new DocList { MarkerStyle = TextMarkerStyle.Disc }; list.ListItems.Add(new ListItem());
        var document = new FlowDocument(list); var paint = Paint(Editor(document));
        Assert.Single(paint.Text, x => x.Value == "•");
        Assert.Empty(list.ListItems[0].Blocks); Assert.Equal("", document.GetText());
    }

    [Fact]
    public void NestedListsAndFirstBlockTablesRetainTheirOwnMarkers()
    {
        var inner = List(TextMarkerStyle.Circle, 1, P("inside"));
        var table = new Table(); var group = new TableRowGroup(); var row = new TableRow();
        row.Cells.Add(new TableCell(P("cell"))); group.Rows.Add(row); table.RowGroups.Add(group);
        var outer = List(TextMarkerStyle.Decimal, 3, table); outer.ListItems[0].Blocks.Add(inner);
        var document = new FlowDocument(outer); string original = document.GetText();
        var paint = Paint(Editor(document));
        Assert.Single(paint.Text, x => x.Value == "3."); Assert.Single(paint.Text, x => x.Value == "◦");
        Assert.True(paint.Text.Single(x => x.Value == "◦").Origin.X > paint.Text.Single(x => x.Value == "3.").Origin.X);
        Assert.Equal(original, document.GetText());
    }

    [Fact]
    public void NumberingDoesNotWrapAtInt32MaxValue()
    {
        var list = List(TextMarkerStyle.Decimal, int.MaxValue, P("body"), P("next"));
        var paint = Paint(Editor(new FlowDocument(list), 420));
        Assert.Contains(paint.Text, x => x.Value == "2147483647.");
        Assert.Contains(paint.Text, x => x.Value == "2147483648.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnboundedDocumentLayoutDoesNotCreateAnArtificialWideRtlSurface(bool rtl)
    {
        var list = List(TextMarkerStyle.Decimal, 3, P("body"));
        list.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var document = new FlowDocument(list) { FontSize = 18, FontFamily = "Helvetica Neue" };
        var editor = new RichTextBox(document) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var layout = typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [double.PositiveInfinity])!;
        double width = (double)layout.GetType().GetProperty("TotalWidth")!.GetValue(layout)!;
        Assert.InRange(width, 20, 200);
    }

    private static Paragraph P(string value) => new(new Run(value));
    private static DocList List(TextMarkerStyle style, int start, params Block[] blocks)
    {
        var list = new DocList { MarkerStyle = style, StartIndex = start };
        foreach (var block in blocks) { var item = new ListItem(); item.Blocks.Add(block); list.ListItems.Add(item); } return list;
    }
    private static RichTextBox Editor(FlowDocument document, double width = 260)
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        document.FontSize = 18; document.FontFamily = "Helvetica Neue";
        var editor = new RichTextBox(document) { Width = width, Height = 300,
            Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        editor.Measure(new Size(width, 300)); editor.Arrange(new Rect(0, 0, width, 300)); return editor;
    }
    private static Recorder Paint(RichTextBox editor)
    {
        var paint = new Recorder(); typeof(RichTextBox).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(DrawingContext)])!.Invoke(editor, [paint]); return paint;
    }
    private sealed class Recorder : DrawingContextAdapter
    {
        internal readonly List<(string Value, Point Origin, double Width, Brush? Brush)> Text = new();
        public override void DrawText(FormattedText text, Point origin) => Text.Add((text.Text, origin, text.Width, text.Foreground));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect bounds) { }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRoundedRectangle(Brush? b, Pen? p, Rect r, double x, double y) { }
        public override void DrawEllipse(Brush? b, Pen? p, Point c, double x, double y) { }
        public override void DrawGeometry(Brush? b, Pen? p, Geometry g) { }
        public override void DrawImage(ImageSource image, Rect bounds) { }
        public override void DrawBackdropEffect(Rect bounds, IBackdropEffect effect, CornerRadius corner) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
