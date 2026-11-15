using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssLetterSpacingTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("0", true)]
    [InlineData("12px", true)]
    [InlineData("-3px", true)]
    [InlineData("1em", true)]
    [InlineData("calc(1em + 2px)", true)]
    [InlineData("10%", false)]
    [InlineData("1", false)]
    [InlineData("auto", false)]
    public void SupportsLetterSpacingGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("letter-spacing", value));

    [Fact]
    public void TrackingChangesWidthAndDrawnPositionWithoutAddingLineEdgeSpace()
    {
        var block = Block("AB");
        Css.SetStyle(block, "letter-spacing:normal; white-space:nowrap");
        var normalWidth = MeasureWidth(block);
        Css.SetStyle(block, "letter-spacing:12px; white-space:nowrap");
        var width = MeasureWidth(block);
        var drawn = Render(block, 160);
        var first = Assert.Single(drawn.Texts.Where(item => item.Text == "A"));
        var second = Assert.Single(drawn.Texts.Where(item => item.Text == "B"));

        Assert.InRange(width - normalWidth, 11.5, 12.5);
        Assert.InRange(first.X, -0.5, 0.5);
        Assert.InRange(second.X - first.X, 20, 30);
        Assert.InRange(MeasureWidth(Block("A")), 0, normalWidth);

        var single = Block("A");
        Css.SetStyle(single, "letter-spacing:12px; white-space:nowrap");
        Assert.InRange(MeasureWidth(single) - MeasureWidth(Block("A")), -0.5, 0.5);
    }

    [Fact]
    public void NegativeTrackingCanOverlapButKeepsWidthNonnegative()
    {
        var block = Block("AB");
        Css.SetStyle(block, "letter-spacing:normal; white-space:nowrap");
        var normal = MeasureWidth(block);
        Css.SetStyle(block, "letter-spacing:-3px; white-space:nowrap");
        Assert.InRange(normal - MeasureWidth(block), 2.5, 3.5);
        Css.SetStyle(block, "letter-spacing:-100px; white-space:nowrap");
        Assert.True(MeasureWidth(block) >= 0);
        Assert.True(MeasureWidth(block) < normal - 3);

        var uneven = Block("iW");
        Css.SetStyle(uneven, "letter-spacing:-100px; white-space:nowrap");
        Assert.True(Assert.Single(Render(uneven, 200).Texts.Where(item => item.Text == "W")).X >= 0);
    }

    [Theory]
    [InlineData("e\u0301B")]
    [InlineData("👨‍👩‍👧‍👦B")]
    [InlineData("🇨🇳B")]
    public void ExtendedGraphemeClusterReceivesOneInterUnitGap(string text)
    {
        var block = Block(text);
        Css.SetStyle(block, "letter-spacing:normal; white-space:nowrap");
        var normal = MeasureWidth(block);
        Css.SetStyle(block, "letter-spacing:10px; white-space:nowrap");
        var spaced = MeasureWidth(block);
        Assert.InRange(spaced - normal, 9.5, 10.5);
        Assert.Contains(Render(block, 200).Texts, item => item.Text.EndsWith('B'));
    }

    [Fact]
    public void InvisibleFormatCharacterDoesNotAcquireAnotherGap()
    {
        var plain = Block("AB");
        var formatted = Block("A\u200BB");
        Css.SetStyle(plain, "letter-spacing:10px; white-space:nowrap");
        Css.SetStyle(formatted, "letter-spacing:10px; white-space:nowrap");
        var plainDelta = MeasureWidth(plain) - MeasureWidth(Block("AB"));
        var formatDelta = MeasureWidth(formatted) - MeasureWidth(Block("A\u200BB"));
        Assert.InRange(plainDelta, 9.5, 10.5);
        Assert.InRange(formatDelta, 9.5, 10.5);
    }

    [Fact]
    public void AdjacentRunsAverageTheirComputedSpacing()
    {
        var first = new Run("A");
        var second = new Run("B");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(block, "white-space:nowrap");
        var normal = MeasureWidth(block);
        Css.SetStyle(first, "letter-spacing:10px");
        Css.SetStyle(second, "letter-spacing:30px");
        Assert.InRange(MeasureWidth(block) - normal, 19.5, 20.5);

        Css.SetStyle(second, "letter-spacing:normal");
        Assert.InRange(MeasureWidth(block) - normal, 4.5, 5.5);
    }

    [Fact]
    public void SpanStyleAndInheritedEmLengthRebuildTheLayout()
    {
        var span = new Span(new Run("AB"));
        var block = new TextBlock(span) { FontSize = 20, Foreground = Brushes.Black };
        var parent = new StackPanel();
        parent.Children.Add(block);
        Css.SetStyle(parent, "font-size:10px; letter-spacing:1em");
        Css.SetStyle(block, "white-space:nowrap");
        Assert.InRange(MeasureWidth(block) - MeasureWidth(Block("AB")), 9.5, 10.5);

        Css.SetStyle(span, "letter-spacing:18px");
        Assert.InRange(MeasureWidth(block) - MeasureWidth(Block("AB")), 17.5, 18.5);
        Css.SetStyle(span, "letter-spacing:normal");
        Assert.InRange(MeasureWidth(block) - MeasureWidth(Block("AB")), -0.5, 0.5);
    }

    [Fact]
    public void CalculatedLengthAndWordSpacingAreAdditive()
    {
        var block = Block("A B");
        Css.SetStyle(block, "white-space:nowrap; letter-spacing:normal; word-spacing:normal");
        var normal = MeasureWidth(block);
        Css.SetStyle(block,
            "white-space:nowrap; letter-spacing:calc(.5em + 2px); word-spacing:10px");
        Assert.InRange(MeasureWidth(block) - normal, 33.5, 34.5);
    }

    [Fact]
    public void TrackingChangesWrappingAndRestoresWithInitial()
    {
        var block = Block("ABCD");
        Css.SetStyle(block, "letter-spacing:normal; overflow-wrap:anywhere");
        var width = MeasureWidth(Block("ABCD")) + 5;
        var normal = Render(block, width);
        Css.SetStyle(block, "letter-spacing:18px; overflow-wrap:anywhere");
        var spaced = Render(block, width);
        Assert.True(spaced.Texts.Select(item => item.Y).Distinct().Count() >
                    normal.Texts.Select(item => item.Y).Distinct().Count());
        Css.SetStyle(block, "letter-spacing:initial; overflow-wrap:anywhere");
        Assert.Single(Render(block, width).Texts.Select(item => item.Y).Distinct());
    }

    [Fact]
    public void ForcedLineBreakDoesNotInsertTrackingAtTheNextLineStart()
    {
        var block = Block("AB\nCD");
        Css.SetStyle(block, "letter-spacing:14px; white-space:pre");
        var draw = Render(block, 200);
        var first = draw.Texts.Where(item => item.Y == draw.Texts[0].Y).ToArray();
        var second = draw.Texts.Where(item => item.Y != draw.Texts[0].Y).ToArray();
        Assert.Equal("A", first[0].Text);
        Assert.Equal("C", second[0].Text);
        Assert.InRange(second[0].X, -0.5, 0.5);
    }

    [Fact]
    public void SelectionAndHitTestingFollowTheDrawnCharacter()
    {
        var block = Block("AB");
        block.Select(1, 1);
        Css.SetStyle(block, "letter-spacing:20px; white-space:nowrap");
        var draw = Render(block, 200);
        var second = Assert.Single(draw.Texts.Where(item => item.Text == "B"));
        var selection = Assert.Single(draw.Rectangles);
        Assert.InRange(selection.X, second.X - 1, second.X + 1);
        Assert.Equal(1, block.GetPositionFromPoint(new Point(second.X + 2, second.Y + 5), true)?.Offset);
    }

    [Fact]
    public void JustificationAndEllipsisUseTrackedPositions()
    {
        var justified = Block("A B C");
        justified.Select(4, 1);
        Css.SetStyle(justified,
            "letter-spacing:12px; text-align:justify; text-align-last:justify; white-space:nowrap");
        var draw = Render(justified, 200);
        var last = Assert.Single(draw.Texts.Where(item => item.Text == "C"));
        var selection = Assert.Single(draw.Rectangles);
        Assert.InRange(selection.X, last.X - 1, last.X + 1);
        Assert.InRange(selection.Right, 199, 201);

        var trimmed = Block("ABCDEFGH");
        Css.SetStyle(trimmed,
            "letter-spacing:10px; white-space:nowrap; text-overflow:ellipsis");
        var clipped = Render(trimmed, 90);
        var ellipsis = Assert.Single(clipped.Texts.Where(item => item.Text == "…"));
        var lastPrefix = clipped.Texts.Last(item => item.Text != "…");
        Assert.InRange(ellipsis.X - lastPrefix.X - lastPrefix.Width,
            7, 13);
        Assert.InRange(ellipsis.X, 0, 90);
        Assert.Equal("ABCDEFGH", trimmed.Text);
    }

    [Fact]
    public void CjkInterCharacterJustificationKeepsTrackedSelectionAligned()
    {
        var block = Block("甲乙丙");
        block.Select(2, 1);
        Css.SetStyle(block,
            "letter-spacing:12px; text-align:justify; text-align-last:justify; " +
            "text-justify:inter-character; white-space:nowrap");
        var draw = Render(block, 200);
        var last = Assert.Single(draw.Texts.Where(item => item.Text == "丙"));
        var selection = Assert.Single(draw.Rectangles);
        Assert.InRange(selection.X, last.X - 1, last.X + 1);
        Assert.InRange(selection.Right, 199, 201);
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static double MeasureWidth(TextBlock block)
    {
        block.Measure(new Size(double.PositiveInfinity, 400));
        return block.DesiredSize.Width;
    }

    private static TextDrawingContext Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var context = new TextDrawingContext();
        block.Render(context);
        return context;
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(string Text, double X, double Y, double Width)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText.Text, origin.X, origin.Y,
                formattedText.WidthIncludingTrailingWhitespace));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
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
