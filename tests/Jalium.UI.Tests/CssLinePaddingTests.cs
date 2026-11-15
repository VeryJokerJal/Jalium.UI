using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssLinePaddingTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("10px", true)]
    [InlineData("-4px", true)]
    [InlineData("1em", true)]
    [InlineData("calc(1em + 2px)", true)]
    [InlineData("10%", false)]
    [InlineData("normal", false)]
    [InlineData("10px 20px", false)]
    [InlineData("5", false)]
    public void SupportsLengthOnly(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("line-padding", value));

    [Fact]
    public void LineEdgesAffectAlignmentAndDesiredWidth()
    {
        var block = Block("A\nB");
        Css.SetStyle(block, "white-space:pre; text-align:left; line-padding:0");
        var plain = Render(block, 200);
        var plainWidth = DesiredWidth(block);

        Css.SetStyle(block, "white-space:pre; text-align:left; line-padding:10px");
        var padded = Render(block, 200);
        Assert.Equal(2, padded.Texts.Count);
        Assert.InRange(padded.Texts[0].X - plain.Texts[0].X, 9.5, 10.5);
        Assert.InRange(padded.Texts[1].X - plain.Texts[1].X, 9.5, 10.5);
        Assert.InRange(DesiredWidth(block) - plainWidth, 19.5, 20.5);

        Css.SetStyle(block, "white-space:pre; text-align:right; line-padding:0");
        var right = Render(block, 200);
        Css.SetStyle(block, "white-space:pre; text-align:right; line-padding:10px");
        var rightPadded = Render(block, 200);
        Assert.InRange(rightPadded.Texts[0].X - right.Texts[0].X, -10.5, -9.5);

        Css.SetStyle(block, "white-space:pre; text-align:center; line-padding:0");
        var center = Render(block, 200);
        Css.SetStyle(block, "white-space:pre; text-align:center; line-padding:10px");
        var centerPadded = Render(block, 200);
        Assert.InRange(centerPadded.Texts[0].X - center.Texts[0].X, -0.5, 0.5);
    }

    [Fact]
    public void PaddingChangesWrappingAndSelectionUsesPaintedOrigin()
    {
        var block = Block("one two");
        Css.SetStyle(block, "white-space:normal; line-padding:0");
        var width = DesiredWidth(block) + 2;
        Assert.Single(Render(block, width).Texts);

        Css.SetStyle(block, "white-space:normal; line-padding:20px");
        var wrapped = Render(block, width);
        Assert.True(wrapped.Texts.Select(item => item.Y).Distinct().Count() > 1);

        var selectedBlock = Block("A B");
        Css.SetStyle(selectedBlock, "white-space:pre; line-padding:20px");
        selectedBlock.Select(2, 1);
        var selected = Render(selectedBlock, 200);
        var selection = Assert.Single(selected.Rectangles);
        Assert.True(selection.X > Assert.Single(selected.Texts).X + 5);
        var hit = selectedBlock.GetPositionFromPoint(new Point(
            selection.X + 2, selection.Y + selection.Height / 2), true)?.Offset;
        Assert.Equal(2, hit);
    }

    [Fact]
    public void AuthoredPaddingUsesCssDefaultWrapping()
    {
        var block = Block("one two");
        Css.SetStyle(block, "line-padding:20px");
        var drawing = Render(block, DesiredWidth(block) - 10);
        Assert.True(drawing.Texts.Select(item => item.Y).Distinct().Count() > 1);
    }

    [Fact]
    public void AtomicInlineEdgesConsumeWidthInOrdinaryFlow()
    {
        var root = Flow("line-padding:10px");
        var boxes = Enumerable.Range(0, 3).Select(_ => Inline(root, 100)).ToArray();
        Layout(root, 220);
        Assert.Equal(10, boxes[0].VisualBounds.X, 6);
        Assert.Equal(110, boxes[1].VisualBounds.X, 6);
        Assert.Equal(10, boxes[2].VisualBounds.X, 6);
        Assert.True(boxes[2].VisualBounds.Y > boxes[0].VisualBounds.Y);
        Assert.Equal(220, root.DesiredSize.Width, 6);

        Layout(root, 210);
        Assert.Equal(10, boxes[0].VisualBounds.X, 6);
        Assert.Equal(10, boxes[1].VisualBounds.X, 6);
        Assert.True(boxes[1].VisualBounds.Y > boxes[0].VisualBounds.Y);

        Css.SetStyle(root, "display:flow-root; line-height:0; text-align:left; line-padding:0");
        Layout(root, 210);
        Assert.Equal(0, boxes[0].VisualBounds.X, 6);
        Assert.Equal(100, boxes[1].VisualBounds.X, 6);
        Assert.Equal(boxes[0].VisualBounds.Y, boxes[1].VisualBounds.Y, 6);
    }

    [Fact]
    public void AtomicInlineEdgesFollowRtlStartAlignment()
    {
        var root = Flow("direction:rtl; text-align:start; line-padding:10px");
        var boxes = Enumerable.Range(0, 2).Select(_ => Inline(root, 90)).ToArray();
        Layout(root, 220);
        Assert.Equal(120, boxes[0].VisualBounds.X, 6);
        Assert.Equal(30, boxes[1].VisualBounds.X, 6);
    }

    [Fact]
    public void FloatExclusionRetainsPaddingOnBothLineEdges()
    {
        var root = Flow("line-padding:10px");
        var floating = new Border();
        root.Children.Add(floating);
        Css.SetStyle(floating, "float:left; width:80px; height:60px");
        var boxes = Enumerable.Range(0, 3).Select(_ => Inline(root, 90)).ToArray();
        Layout(root, 300);
        Assert.Equal(0, floating.VisualBounds.X, 6);
        Assert.Equal(90, boxes[0].VisualBounds.X, 6);
        Assert.Equal(180, boxes[1].VisualBounds.X, 6);
        Assert.Equal(90, boxes[2].VisualBounds.X, 6);
        Assert.True(boxes[2].VisualBounds.Y > boxes[0].VisualBounds.Y);
    }

    [Fact]
    public void TabsKeepTheirStopsRelativeToTheUnpaddedContentEdge()
    {
        var block = Block("A\tB");
        Css.SetStyle(block, "white-space:pre; tab-size:40px; line-padding:10px");
        var drawing = Render(block, 200);
        Assert.InRange(Assert.Single(drawing.Texts, item => item.Text == "A").X,
            9.5, 10.5);
        Assert.InRange(Assert.Single(drawing.Texts, item => item.Text == "B").X,
            39.5, 40.5);

        block.Select(2, 1);
        Assert.InRange(Assert.Single(Render(block, 200).Rectangles).X, 39.5, 40.5);
    }

    [Fact]
    public void JustificationAndEllipsisRespectTheReservedEdges()
    {
        var justified = Block("one two three four five");
        Css.SetStyle(justified,
            "white-space:normal; text-align:justify; line-padding:10px");
        justified.Select(4, 3);
        var drawing = Render(justified, 120);
        Assert.InRange(Assert.Single(drawing.Rectangles).Right, 109, 111);

        var trimmed = Block("MMMMMMMMMM");
        Css.SetStyle(trimmed,
            "white-space:nowrap; text-overflow:ellipsis; line-padding:10px");
        var ellipsis = Assert.Single(Render(trimmed, 60).Texts);
        Assert.EndsWith("…", ellipsis.Text);
        Assert.InRange(ellipsis.X, 9.5, 10.5);
        Assert.True(ellipsis.X + ellipsis.Width <= 50.5);
    }

    [Fact]
    public void InnermostRunControlsEachLineEdgeAndRespondsToStyleChanges()
    {
        var first = new Run("A");
        var span = new Span(first);
        var last = new Run("B");
        var block = new TextBlock(span) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(last);
        Css.SetStyle(block, "white-space:pre; line-padding:5px");
        var inheritedWidth = DesiredWidth(block);

        Css.SetStyle(span, "line-padding:16px");
        Css.SetStyle(last, "line-padding:20px");
        Assert.InRange(Render(block, 200).Texts[0].X, 15.5, 16.5);
        Assert.InRange(DesiredWidth(block) - inheritedWidth, 25.5, 26.5);

        Css.SetStyle(span, "line-padding:8px");
        Assert.InRange(Render(block, 200).Texts[0].X, 7.5, 8.5);
        Css.SetStyle(first, "line-padding:3px");
        Assert.InRange(Render(block, 200).Texts[0].X, 2.5, 3.5);
        Css.SetStyle(first, "line-padding:inherit");
        Assert.InRange(Render(block, 200).Texts[0].X, 7.5, 8.5);

        Css.SetStyle(block, "white-space:pre; text-align:right; line-padding:5px");
        var rightWithPadding = Render(block, 200).Texts[0].X;
        Css.SetStyle(last, "line-padding:0");
        Assert.InRange(rightWithPadding - Render(block, 200).Texts[0].X,
            -20.5, -19.5);
    }

    [Fact]
    public void CandidateEndPaddingCanShrinkAcrossRunBoundary()
    {
        var first = new Run("A ");
        var second = new Run("B");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(block, "white-space:normal; line-padding:0");
        Css.SetStyle(first, "line-padding:40px");
        Css.SetStyle(second, "line-padding:0");

        var width = DesiredWidth(block) - 1;
        var drawing = Render(block, width);
        Assert.Single(drawing.Texts.Select(item => item.Y).Distinct());
        Assert.InRange(drawing.Texts[0].X, 39.5, 40.5);
    }

    [Fact]
    public void WrappedRunBeginsAtItsOwnInheritedLinePadding()
    {
        var first = new Run("first ");
        var second = new Run("second");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(block, "white-space:normal; line-padding:0");
        Css.SetStyle(first, "line-padding:10px");
        Css.SetStyle(second, "line-padding:20px");

        var rows = Render(block, 120).Texts.GroupBy(item => item.Y).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.InRange(rows[0].Min(item => item.X), 9.5, 10.5);
        Assert.InRange(rows[1].Min(item => item.X), 19.5, 20.5);
    }

    [Fact]
    public void RunOnlyPaddingActivatesCssWrapping()
    {
        var run = new Run("one two");
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(run, "line-padding:20px");

        var rows = Render(block, DesiredWidth(block) - 10).Texts
            .GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length > 1);
        Assert.InRange(rows[0].Min(item => item.X), 19.5, 20.5);
    }

    [Fact]
    public void ExpandedPaintGraphemeKeepsRunPaddingOnEachVisualLine()
    {
        var run = new Run("ß");
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block,
            "text-transform:uppercase; line-break:anywhere; line-padding:0");
        Css.SetStyle(run, "line-padding:20px; background-color:red");

        var drawing = Render(block, 55);
        var rows = drawing.Texts.GroupBy(item => item.Y).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
            Assert.InRange(row.Min(item => item.X), 19.5, 20.5));
        var backgrounds = drawing.Fills.Where(item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Red).ToArray();
        Assert.Equal(2, backgrounds.Length);
        Assert.All(backgrounds, background =>
            Assert.InRange(background.Rect.X, -0.5, 0.5));
    }

    [Fact]
    public void RtlRunEdgesReserveTheirLogicalSides()
    {
        var first = new Run("A");
        var last = new Run("B");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(last);
        Css.SetStyle(first, "line-padding:10px");
        Css.SetStyle(last, "line-padding:20px");
        Css.SetStyle(block,
            "white-space:pre; direction:rtl; text-align:start; line-padding:0");

        var atStart = Assert.Single(Render(block, 200).Texts);
        Css.SetStyle(first, "line-padding:0");
        Css.SetStyle(last, "line-padding:0");
        var withoutPadding = Assert.Single(Render(block, 200).Texts);
        Assert.InRange(withoutPadding.X + withoutPadding.Width -
            atStart.X - atStart.Width, 9.5, 10.5);

        Css.SetStyle(first, "line-padding:10px");
        Css.SetStyle(last, "line-padding:20px");
        Css.SetStyle(block,
            "white-space:pre; direction:rtl; text-align:end; line-padding:0");
        var atEnd = Assert.Single(Render(block, 200).Texts);
        Css.SetStyle(last, "line-padding:0");
        Assert.InRange(atEnd.X - Assert.Single(Render(block, 200).Texts).X,
            19.5, 20.5);
    }

    [Fact]
    public void RunBackgroundCoversBothLinePaddingEdges()
    {
        var run = new Run("A");
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "white-space:pre; line-padding:10px");
        Css.SetStyle(run, "background-color:red");

        var drawing = Render(block, 100);
        var background = Assert.Single(drawing.Fills, item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Red);
        var text = Assert.Single(drawing.Texts);
        Assert.InRange(background.Rect.X, -0.5, 0.5);
        Assert.True(background.Rect.Right > text.X + text.Width + 5);

        block.Select(0, 1);
        var selected = Render(block, 100);
        Assert.True(selected.Fills.Count >= 2);
        Assert.Equal(Colors.Red,
            Assert.IsType<SolidColorBrush>(selected.Fills[0].Brush).Color);
        block.Select(0, 0);

        Css.SetStyle(run, "background-color:blue");
        Assert.Single(Render(block, 100).Fills, item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Blue);

        block.Foreground = null;
        Assert.Single(Render(block, 100).Fills, item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Blue);
    }

    [Fact]
    public void NestedSpanBackgroundsFollowTheInnermostLineEdges()
    {
        var outer = new Span(new Run("Here is "));
        var inner = new Span(new Run("some text"));
        outer.Inlines.Add(inner);
        var block = new TextBlock(outer) { FontSize = 20, Foreground = Brushes.White };
        Css.SetStyle(block, "white-space:normal; line-padding:10px");
        Css.SetStyle(outer, "background-color:black");
        Css.SetStyle(inner, "background-color:green");

        var drawing = Render(block, 150);
        var black = drawing.Fills.Where(item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Black).ToArray();
        var green = drawing.Fills.Where(item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Green).ToArray();
        Assert.Equal(2, black.Length);
        Assert.Equal(2, green.Length);
        Assert.True(green[0].Rect.X > black[0].Rect.X + 5);
        Assert.InRange(green[0].Rect.Right - black[0].Rect.Right, -0.5, 0.5);
        Assert.InRange(green[1].Rect.X - black[1].Rect.X, -0.5, 0.5);
        Assert.InRange(green[1].Rect.Right - black[1].Rect.Right, -0.5, 0.5);
        Assert.True(drawing.Fills.IndexOf(black[0]) < drawing.Fills.IndexOf(green[0]));

        Css.SetStyle(inner, "background-color:blue");
        Assert.Equal(2, Render(block, 150).Fills.Count(item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Blue));
    }

    [Fact]
    public void TrimmedInlineBackgroundExtendsPastTheEllipsis()
    {
        var run = new Run("MMMMMMMMMM");
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block,
            "white-space:nowrap; text-overflow:ellipsis; line-padding:10px");
        Css.SetStyle(run, "background-color:red");

        var drawing = Render(block, 60);
        var background = Assert.Single(drawing.Fills, item =>
            item.Brush is SolidColorBrush brush && brush.Color == Colors.Red);
        var ellipsis = Assert.Single(drawing.Texts);
        Assert.EndsWith("…", ellipsis.Text);
        Assert.InRange(background.Rect.X, -0.5, 0.5);
        Assert.True(background.Rect.Right - ellipsis.X - ellipsis.Width > 8);
        Assert.True(background.Rect.Right <= block.RenderSize.Width + 0.5);
    }

    [Fact]
    public void InheritanceCssWideKeywordsAndCalculatedLengthsResolve()
    {
        var parent = new StackPanel();
        var block = Block("A");
        parent.Children.Add(block);
        Css.SetStyle(parent, "line-padding:12px");
        Assert.InRange(Render(block, 200).Texts[0].X, 11.5, 12.5);

        Css.SetStyle(block, "line-padding:initial");
        Assert.InRange(Render(block, 200).Texts[0].X, -0.5, 0.5);
        Css.SetStyle(block, "line-padding:inherit");
        Assert.InRange(Render(block, 200).Texts[0].X, 11.5, 12.5);
        Css.SetStyle(block, "line-padding:unset");
        Assert.InRange(Render(block, 200).Texts[0].X, 11.5, 12.5);
        Css.SetStyle(block, "line-padding:calc(1em + 2px)");
        Assert.InRange(Render(block, 200).Texts[0].X, 21.5, 22.5);
        Css.SetStyle(block, "line-padding:5px; all:initial");
        Assert.InRange(Render(block, 200).Texts[0].X, -0.5, 0.5);
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static StackPanel Flow(string style)
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root; line-height:0; text-align:left; " + style);
        return root;
    }

    private static Border Inline(Panel root, double width)
    {
        var border = new Border();
        root.Children.Add(border);
        Css.SetStyle(border,
            $"display:inline-block; width:{width}px; height:20px; vertical-align:top");
        return border;
    }

    private static void Layout(Panel root, double width)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, 200));
        root.Arrange(new Rect(0, 0, width, 200));
    }

    private static double DesiredWidth(TextBlock block)
    {
        block.Measure(new Size(double.PositiveInfinity, 300));
        return block.DesiredSize.Width;
    }

    private static Capture Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 300));
        block.Arrange(new Rect(0, 0, width, block.DesiredSize.Height));
        var capture = new Capture();
        block.Render(capture);
        return capture;
    }

    private sealed class Capture : DrawingContextAdapter
    {
        internal List<(string Text, double X, double Y, double Width)> Texts { get; } = [];
        internal List<Rect> Rectangles { get; } = [];
        internal List<(Brush? Brush, Rect Rect)> Fills { get; } = [];
        public override void DrawText(FormattedText text, Point origin)
            => Texts.Add((text.Text, origin.X, origin.Y, text.WidthIncludingTrailingWhitespace));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            Rectangles.Add(rectangle);
            Fills.Add((brush, rectangle));
        }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
