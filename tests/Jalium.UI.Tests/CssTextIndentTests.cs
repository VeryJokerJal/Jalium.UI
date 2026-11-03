using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextIndentTests
{
    [Theory]
    [InlineData("20px", true)]
    [InlineData("-2em", true)]
    [InlineData("25% hanging each-line", true)]
    [InlineData("each-line calc(10% + 2px) hanging", true)]
    [InlineData("hanging", false)]
    [InlineData("20px hanging hanging", false)]
    [InlineData("20px 10px", false)]
    [InlineData("2", false)]
    public void SupportsTheTextIndentGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-indent", value));

    [Fact]
    public void FirstLineIndentChangesWrappingDrawingSelectionAndHitTesting()
    {
        var block = Block("one two three four five", TextWrapping.Wrap);
        block.Select(0, 3);
        Css.SetStyle(block, "text-indent:0");
        var natural = Render(block, 120);
        var naturalRows = natural.Texts.GroupBy(item => item.Y).ToArray();

        Css.SetStyle(block, "text-indent:60px");
        var indented = Render(block, 120);
        var rows = indented.Texts.GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length > naturalRows.Length ||
            rows[0].First().Text.Text.Length < naturalRows[0].First().Text.Text.Length);
        Assert.InRange(rows[0].First().X, 59, 61);
        Assert.InRange(rows[1].First().X, 0, 1);
        Assert.InRange(Assert.Single(indented.Rectangles).X, 59, 61);
        var hit = block.GetPositionFromPoint(new Point(rows[0].First().X + 2, rows[0].First().Y + 5), true);
        Assert.NotNull(hit);
        Assert.InRange(hit.Offset, 0, 1);
    }

    [Fact]
    public void EachLineIndentsAfterForcedBreakAndStyleChangesRebuildLines()
    {
        var block = Block("abc\ndef", TextWrapping.NoWrap);
        Css.SetStyle(block, "text-indent:25px");
        var firstOnly = Render(block, 180);
        Assert.InRange(firstOnly.Texts[0].X, 24, 26);
        Assert.InRange(firstOnly.Texts[1].X, 0, 1);

        Css.SetStyle(block, "text-indent:25px each-line");
        var eachLine = Render(block, 180);
        Assert.All(eachLine.Texts, item => Assert.InRange(item.X, 24, 26));
    }

    [Fact]
    public void HangingIndentsSoftWrappedLines()
    {
        var block = Block("one two three four five", TextWrapping.Wrap);
        Css.SetStyle(block, "text-indent:hanging 25px");
        var rows = Render(block, 120).Texts.GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length >= 2);
        Assert.InRange(rows[0].First().X, 0, 1);
        Assert.All(rows.Skip(1), row => Assert.InRange(row.First().X, 24, 26));
    }

    [Fact]
    public void HangingEachLineLeavesEachForcedFirstLineFlush()
    {
        var block = Block("one two three four\nalpha beta gamma delta", TextWrapping.Wrap);
        Css.SetStyle(block, "text-indent:25px hanging each-line");
        var rows = Render(block, 120).Texts.GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length >= 4);
        Assert.InRange(rows[0].First().X, 0, 1);
        Assert.Contains(rows, row => row.First().Text.Text.StartsWith("alpha", StringComparison.Ordinal) &&
            row.First().X <= 1);
        Assert.Contains(rows, row => row.First().X is >= 24 and <= 26);
    }

    [Fact]
    public void PercentIndentInheritsSymbolicallyAndUsesChildContentWidth()
    {
        var parent = new StackPanel();
        var block = Block("abc", TextWrapping.NoWrap);
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-indent:25%");

        Assert.InRange(Assert.Single(Render(block, 200).Texts).X, 49, 51);
        Assert.InRange(Assert.Single(Render(block, 120).Texts).X, 29, 31);

        Css.SetStyle(block, "text-indent:initial");
        Assert.InRange(Assert.Single(Render(block, 120).Texts).X, 0, 1);
    }

    [Fact]
    public void RtlIndentMovesTheLogicalStartEdgeInward()
    {
        var block = Block("abc", TextWrapping.NoWrap);
        Css.SetStyle(block, "direction:rtl; text-align:start; text-indent:0");
        var flush = Assert.Single(Render(block, 180).Texts).X;

        Css.SetStyle(block, "direction:rtl; text-align:start; text-indent:25px");
        var indented = Assert.Single(Render(block, 180).Texts).X;
        Assert.InRange(flush - indented, 24, 26);
    }

    [Fact]
    public void CenterAlignmentUsesTheIndentedLineBox()
    {
        var block = Block("abc", TextWrapping.NoWrap);
        Css.SetStyle(block, "text-align:center; text-indent:0");
        var centered = Assert.Single(Render(block, 180).Texts).X;

        Css.SetStyle(block, "text-align:center; text-indent:40px");
        var indented = Assert.Single(Render(block, 180).Texts).X;
        Assert.InRange(indented - centered, 19, 21);
    }

    [Fact]
    public void JustificationFillsTheWidthRemainingAfterIndent()
    {
        var block = Block("one two three four five", TextWrapping.Wrap);
        block.Select(4, 3);
        Css.SetStyle(block, "text-align:justify; text-indent:20px");

        var drawing = Render(block, 120);
        var firstRow = drawing.Texts.GroupBy(item => item.Y).First().ToArray();
        Assert.True(firstRow.Length >= 2);
        Assert.InRange(firstRow[0].X, 19, 21);
        Assert.InRange(Assert.Single(drawing.Rectangles).Right, 117, 121);
    }

    [Fact]
    public void ChangingInheritedIndentRebuildsTheChildLineLayout()
    {
        var parent = new StackPanel();
        var block = Block("one two three four five", TextWrapping.Wrap);
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-indent:50%");
        var first = Render(block, 120).Texts.GroupBy(item => item.Y).ToArray();

        Css.SetStyle(parent, "text-indent:0");
        var second = Render(block, 120).Texts.GroupBy(item => item.Y).ToArray();
        Assert.InRange(first[0].First().X, 59, 61);
        Assert.InRange(second[0].First().X, 0, 1);
        Assert.True(second[0].First().Text.Text.Length > first[0].First().Text.Text.Length);
    }

    [Fact]
    public void OrdinaryFlowFirstInlineLineUsesTheIndentedWidth()
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root; line-height:0; text-indent:30px");
        var items = new Border[3];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = new Border();
            root.Children.Add(items[index]);
            Css.SetStyle(items[index], "display:inline-block; width:100px; height:20px");
        }
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(220, 100));
        root.Arrange(new Rect(0, 0, 220, 100));

        Assert.Equal(30, items[0].VisualBounds.X, 6);
        Assert.Equal(0, items[1].VisualBounds.X, 6);
        Assert.Equal(100, items[2].VisualBounds.X, 6);
    }

    [Fact]
    public void FloatExclusionKeepsPercentIndentBasedOnTheContainingBlock()
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root; line-height:0; text-indent:25%");
        var floating = new Border();
        root.Children.Add(floating);
        Css.SetStyle(floating, "float:left; width:80px; height:60px");
        var items = new Border[3];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = new Border();
            root.Children.Add(items[index]);
            Css.SetStyle(items[index], "display:inline-block; width:70px; height:20px; vertical-align:top");
        }
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(240, 100));
        root.Arrange(new Rect(0, 0, 240, 100));

        Assert.Equal(140, items[0].VisualBounds.X, 6);
        Assert.Equal(80, items[1].VisualBounds.X, 6);
        Assert.Equal(150, items[2].VisualBounds.X, 6);
        Assert.Equal(20, items[1].VisualBounds.Y, 6);
    }

    [Fact]
    public void FloatAfterIndentedInlineContentMovesBelowTheOccupiedLine()
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root; line-height:0; text-indent:40px");
        var inline = new Border();
        var floating = new Border();
        root.Children.Add(inline);
        root.Children.Add(floating);
        Css.SetStyle(inline, "display:inline-block; width:100px; height:20px; vertical-align:top");
        Css.SetStyle(floating, "float:left; width:100px; height:40px");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(220, 100));
        root.Arrange(new Rect(0, 0, 220, 100));

        Assert.Equal(40, inline.VisualBounds.X, 6);
        Assert.Equal(20, floating.VisualBounds.Y, 6);
    }

    private static TextBlock Block(string text, TextWrapping wrapping) => new()
    {
        Text = text, TextWrapping = wrapping, FontSize = 20, Foreground = Brushes.Black,
    };

    private static TextDrawingContext Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 180));
        block.Arrange(new Rect(0, 0, width, 180));
        var context = new TextDrawingContext();
        block.Render(context);
        return context;
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X, origin.Y));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
