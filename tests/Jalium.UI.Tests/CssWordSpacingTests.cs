using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssWordSpacingTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("0", true)]
    [InlineData("12px", true)]
    [InlineData("-3px", true)]
    [InlineData("1em", true)]
    [InlineData("calc(1em + 2px)", true)]
    [InlineData("10%", true)]
    [InlineData("-10%", true)]
    [InlineData("calc(50% + 2px)", true)]
    [InlineData("1", false)]
    [InlineData("auto", false)]
    public void SupportsWordSpacingGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("word-spacing", value));

    [Fact]
    public void PositiveSpacingChangesMeasuredWidthAndDrawnWordPosition()
    {
        var block = Block("one two");
        Css.SetStyle(block, "word-spacing:1px; white-space:nowrap");
        var narrowWidth = MeasureWidth(block);
        var narrow = Render(block, 200);
        var narrowSecond = Assert.Single(narrow.Texts.Where(item => item.Text == "two"));

        Css.SetStyle(block, "word-spacing:12px; white-space:nowrap");
        var wideWidth = MeasureWidth(block);
        var wide = Render(block, 200);
        var wideSecond = Assert.Single(wide.Texts.Where(item => item.Text == "two"));

        Assert.InRange(wideWidth - narrowWidth, 10.5, 11.5);
        Assert.InRange(wideSecond.X - narrowSecond.X, 10.5, 11.5);
        Assert.Equal("one two", block.Text);
    }

    [Fact]
    public void NegativeSpacingReducesWidthButDoesNotReverseASeparatorAdvance()
    {
        var block = Block("ab cd");
        Css.SetStyle(block, "word-spacing:normal; white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(block, "word-spacing:-3px; white-space:nowrap");
        var negative = MeasureWidth(block);
        Assert.InRange(normal - negative, 2.5, 3.5);

        Css.SetStyle(block, "word-spacing:-100px; white-space:nowrap");
        var clamped = MeasureWidth(block);
        Assert.True(clamped >= MeasureWidth(Block("abcd")) - 1);
        Assert.True(clamped < negative);
    }

    [Theory]
    [InlineData("\u00A0", true)]
    [InlineData("\u1361", true)]
    [InlineData("\U00010100", true)]
    [InlineData("\u3000", false)]
    [InlineData("\u2003", false)]
    [InlineData("\u200B", false)]
    public void RecognizesWordSeparatorsWithoutSpacingFixedWidthOrZeroWidthSpaces(
        string separator, bool receivesSpacing)
    {
        var block = Block("a" + separator + "b");
        Css.SetStyle(block, "word-spacing:normal; white-space:nowrap");
        var normal = MeasureWidth(block);
        Css.SetStyle(block, "word-spacing:10px; white-space:nowrap");
        var spaced = MeasureWidth(block);

        if (receivesSpacing)
            Assert.InRange(spaced - normal, 9.5, 10.5);
        else
            Assert.InRange(spaced - normal, -0.5, 0.5);
    }

    [Fact]
    public void SpacingChangesWrappingAndInheritedStyleResets()
    {
        var parent = new StackPanel();
        var block = Block("one two three");
        parent.Children.Add(block);
        Css.SetStyle(parent, "word-spacing:normal");
        var lineWidth = MeasureWidth(Block("one two")) + 10;
        var normal = Render(block, lineWidth);

        Css.SetStyle(parent, "word-spacing:20px");
        var spaced = Render(block, lineWidth);
        Assert.True(spaced.Texts.Where(item => item.Y == spaced.Texts[0].Y)
            .Sum(item => item.Text.Length) <
            normal.Texts.Where(item => item.Y == normal.Texts[0].Y)
                .Sum(item => item.Text.Length));

        Css.SetStyle(block, "word-spacing:initial");
        var reset = Render(block, lineWidth);
        Assert.Equal(normal.Texts.Where(item => item.Y == normal.Texts[0].Y)
                .Select(item => item.Text),
            reset.Texts.Where(item => item.Y == reset.Texts[0].Y)
                .Select(item => item.Text));
    }

    [Fact]
    public void RunStyleChangesInvalidateTheOwningTextBlock()
    {
        var first = new Run("one ");
        var block = new TextBlock(first)
        {
            FontSize = 20, Foreground = Brushes.Black,
        };
        block.Inlines.Add(new Run("two"));
        Css.SetStyle(block, "white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(first, "word-spacing:18px");
        var spaced = MeasureWidth(block);
        Assert.InRange(spaced - normal, 17.5, 18.5);

        Css.SetStyle(first, "word-spacing:4px");
        Assert.InRange(MeasureWidth(block) - normal, 3.5, 4.5);
    }

    [Fact]
    public void SelectionAndHitTestingUseTheDrawnWordPosition()
    {
        var block = Block("one two");
        block.Select(4, 3);
        Css.SetStyle(block, "word-spacing:20px; white-space:nowrap");
        var draw = Render(block, 200);
        var second = Assert.Single(draw.Texts.Where(item => item.Text == "two"));
        var selection = Assert.Single(draw.Rectangles);

        Assert.InRange(selection.X, second.X - 1, second.X + 1);
        Assert.InRange(selection.Right, second.X + 20, second.X + 40);
        var pointer = block.GetPositionFromPoint(new Point(second.X + 5, second.Y + 5), true);
        Assert.NotNull(pointer);
        Assert.InRange(pointer.Offset, 4, 7);
        Assert.Equal("two", block.SelectedText);
    }

    [Fact]
    public void InheritedEmSpacingKeepsTheParentsComputedLength()
    {
        var parent = new StackPanel();
        var block = Block("one two");
        parent.Children.Add(block);
        Css.SetStyle(parent, "font-size:10px; word-spacing:1em");
        Css.SetStyle(block, "word-spacing:initial; white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(block, "white-space:nowrap");
        Assert.InRange(MeasureWidth(block) - normal, 9.5, 10.5);
    }

    [Fact]
    public void CalculatedLengthChangesMeasuredWidth()
    {
        var block = Block("one two");
        Css.SetStyle(block, "word-spacing:normal; white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(block, "word-spacing:calc(1em + 2px); white-space:nowrap");
        Assert.InRange(MeasureWidth(block) - normal, 21.5, 22.5);
    }

    [Fact]
    public void PercentageAndMixedMathUseTheTextBlocksFontSize()
    {
        var block = Block("one two");
        Css.SetStyle(block, "word-spacing:normal; white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(block, "word-spacing:50%; white-space:nowrap");
        Assert.InRange(MeasureWidth(block) - normal, 9.5, 10.5);

        Css.SetStyle(block, "word-spacing:calc(50% + 2px); white-space:nowrap");
        Assert.InRange(MeasureWidth(block) - normal, 11.5, 12.5);

        Css.SetStyle(block, "word-spacing:-50%; white-space:nowrap");
        Assert.True(MeasureWidth(block) < normal);
    }

    [Fact]
    public void InheritedPercentageUsesTheDescendantsUsedFontSize()
    {
        var parent = new StackPanel();
        var block = Block("one two");
        parent.Children.Add(block);
        Css.SetStyle(parent, "font-size:10px; word-spacing:50%");
        Css.SetStyle(block, "word-spacing:initial; white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(block, "white-space:nowrap");
        Assert.InRange(MeasureWidth(block) - normal, 9.5, 10.5);

        Css.SetStyle(parent, "font-size:10px; word-spacing:calc(1em + 50%)");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.InRange(MeasureWidth(block) - normal, 19.5, 20.5);

        Css.SetStyle(parent, "font-size:15px; word-spacing:calc(1em + 50%)");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.InRange(MeasureWidth(block) - normal, 24.5, 25.5);
    }

    [Fact]
    public void RunPercentageUsesTheRunsFontSizeAndUpdatesWithIt()
    {
        var first = new Run("one ") { FontSize = 30 };
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("two"));
        Css.SetStyle(block, "white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(first, "word-spacing:50%");
        Assert.InRange(MeasureWidth(block) - normal, 14.5, 15.5);

        first.FontSize = 40;
        Css.SetStyle(first, "word-spacing:normal");
        var largerNormal = MeasureWidth(block);
        Css.SetStyle(first, "word-spacing:50%");
        Assert.InRange(MeasureWidth(block) - largerNormal, 19.5, 20.5);
    }

    [Fact]
    public void SpanSpacingRebuildsTheOwningTextBlocksLayout()
    {
        var span = new Span(new Run("one "));
        var block = new TextBlock(span)
        {
            FontSize = 20, Foreground = Brushes.Black,
        };
        block.Inlines.Add(new Run("two"));
        Css.SetStyle(block, "white-space:nowrap");
        var normal = MeasureWidth(block);

        Css.SetStyle(span, "word-spacing:16px");
        Assert.InRange(MeasureWidth(block) - normal, 15.5, 16.5);
        Css.SetStyle(span, "word-spacing:normal");
        Assert.InRange(MeasureWidth(block) - normal, -0.5, 0.5);
    }

    [Fact]
    public void JustifiedLineKeepsSelectionAtTheDrawnWord()
    {
        var block = Block("one two");
        block.Select(4, 3);
        Css.SetStyle(block,
            "word-spacing:14px; text-align:justify; text-align-last:justify; white-space:nowrap");
        var draw = Render(block, 200);
        var second = Assert.Single(draw.Texts.Where(item => item.Text == "two"));
        var selection = Assert.Single(draw.Rectangles);

        Assert.InRange(selection.X, second.X - 1, second.X + 1);
        Assert.InRange(selection.Right, 199, 201);
    }

    [Fact]
    public void EllipsisAccountsForWordSpacingWithoutChangingSourceText()
    {
        var block = Block("one two three four");
        const double width = 95;
        Css.SetStyle(block,
            "word-spacing:18px; white-space:nowrap; text-overflow:ellipsis");
        var draw = Render(block, width);
        var ellipsis = Assert.Single(draw.Texts.Where(item => item.Text == "…"));

        Assert.True(ellipsis.X > 0);
        Assert.True(ellipsis.X < width);
        Assert.Equal("one two three four", block.Text);
        Assert.DoesNotContain(draw.Texts, item => item.Text.Contains("four"));
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
        public List<(string Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText.Text, origin.X, origin.Y));
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
