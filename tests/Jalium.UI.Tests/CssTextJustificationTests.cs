using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Text;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextJustificationTests
{
    [Fact]
    public void WrappedLineDistributesSpaceAndKeepsTheLastLineNatural()
    {
        var justified = CreateBlock("text-align: justify; white-space: normal");
        var left = CreateBlock("text-align: left; white-space: normal");
        justified.Select(4, 3);
        left.Select(4, 3);

        var justifiedDraw = Render(justified, 120);
        var leftDraw = Render(left, 120);
        var selectedJustified = Assert.Single(justifiedDraw.Rectangles);
        var selectedLeft = Assert.Single(leftDraw.Rectangles);
        Assert.True(selectedJustified.X > selectedLeft.X + 10,
            $"Second word stayed at x={selectedJustified.X}; left x={selectedLeft.X}");
        Assert.InRange(selectedJustified.Right, 117, 121);

        var rows = justifiedDraw.Texts.GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length >= 2);
        Assert.True(rows[0].Count() >= 2);
        Assert.Single(rows[^1]);
        Assert.DoesNotContain(justifiedDraw.Texts, item => item.Text.Text.EndsWith(' '));

        var secondWord = justifiedDraw.Texts.First(item => item.Text.Text == "two");
        var pointer = justified.GetPositionFromPoint(
            new Point(secondWord.X + 10, secondWord.Y + 5), true);
        Assert.NotNull(pointer);
        Assert.InRange(pointer.Offset, 4, 7);
    }

    [Fact]
    public void ExplicitFinalLineIsNotJustified()
    {
        var block = new TextBlock
        {
            Text = "one two\nthree four", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };
        Css.SetStyle(block, "text-align: justify");

        var draw = Render(block, 300);
        Assert.Equal(new[] { "one two", "three four" },
            draw.Texts.Select(item => item.Text.Text));
    }

    [Fact]
    public void CjkLineDistributesInterCharacterSpace()
    {
        var block = new TextBlock
        {
            Text = "你好世界你好世界", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };
        Css.SetStyle(block, "text-align: justify");

        var draw = Render(block, 65);
        var rows = draw.Texts.GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length >= 2);
        Assert.True(rows[0].Count() >= 2);
        Assert.All(rows[0], item => Assert.Single(item.Text.Text.EnumerateRunes()));
    }

    [Fact]
    public void TextDecorationCoversTheJustifiedLine()
    {
        var block = CreateBlock(
            "text-align: justify; white-space: normal; text-decoration: underline solid red 2px");

        var draw = Render(block, 120);
        Assert.True(draw.Lines.Count >= 2);
        Assert.InRange(draw.Lines[0].End.X, 119, 121);
        Assert.True(draw.Lines[^1].End.X < 120);
    }

    [Fact]
    public void JustifiedLineKeepsRunTypographyAndForeground()
    {
        var first = new Run("one ");
        var second = new Run("two ") { Foreground = Brushes.Blue };
        var block = new TextBlock(first)
        {
            FontSize = 14, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };
        block.Inlines.Add(second);
        block.Inlines.Add(new Run("three four five"));
        Css.SetStyle(second, "font-size: 28px; font-weight: 700");
        Css.SetStyle(block, "text-align: justify");

        var draw = Render(block, 100);
        var firstWord = draw.Texts.First(item => item.Text.Text == "one");
        var secondWord = draw.Texts.First(item => item.Text.Text == "two");
        Assert.Equal(28, secondWord.Text.FontSize);
        Assert.Equal(700, secondWord.Text.FontWeight);
        Assert.Equal(Colors.Blue,
            Assert.IsType<SolidColorBrush>(secondWord.Text.Foreground).Color);
        Assert.True(secondWord.X > firstWord.X + firstWord.Text.Width + 8);
    }

    [Fact]
    public void SpanDecorationEndsAtTheJustifiedBoundary()
    {
        var span = new Span(new Run("one two three four five"));
        var block = new TextBlock(span)
        {
            FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };
        Css.SetStyle(block, "text-align: justify");
        Css.SetStyle(span, "text-decoration: underline solid red 2px");

        var draw = Render(block, 120);
        Assert.True(draw.Lines.Count >= 2);
        Assert.InRange(draw.Lines[0].End.X, 119, 121);
        Assert.True(draw.Lines[^1].End.X < 120);
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("none", true)]
    [InlineData("inter-word", true)]
    [InlineData("inter-character", true)]
    [InlineData("distribute", true)]
    [InlineData("inter-grapheme", false)]
    public void SupportsTheTextJustifyGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-justify", value));

    [Fact]
    public void NoneLeavesAJustifiedLineUnstretchedAndHonorsLastLineFallback()
    {
        var block = new TextBlock
        {
            Text = "AB", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.NoWrap,
        };
        Css.SetStyle(block, "text-align:justify-all; text-justify:none");

        var centered = Assert.Single(Render(block, 120).Texts);
        Assert.InRange(centered.X, 35, 55);

        Css.SetStyle(block, "text-align:justify; text-align-last:justify; text-justify:auto");
        var natural = Assert.Single(Render(block, 120).Texts);
        Assert.InRange(natural.X, 35, 55);
    }

    [Fact]
    public void NoneSuppressesExistingWordOpportunities()
    {
        var block = CreateBlock("white-space:normal; text-align:justify; text-justify:auto");
        block.Select(4, 3);
        var expandedX = Assert.Single(Render(block, 120).Rectangles).X;

        Css.SetStyle(block, "white-space:normal; text-align:justify; text-justify:none");
        Assert.Equal(CssTextJustification.None,
            block.GetValue(CssFlowProperties.TextJustifyProperty));
        var natural = Render(block, 120);
        Assert.True(expandedX > Assert.Single(natural.Rectangles).X + 10,
            $"Expanded x={expandedX}, natural x={natural.Rectangles[0].X}");
        Assert.Single(natural.Texts.GroupBy(item => item.Y).First());
    }

    [Fact]
    public void AutoUsesWordAndCjkOpportunitiesOnTheSameLine()
    {
        var block = new TextBlock
        {
            Text = "甲 乙丙", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.NoWrap,
        };
        Css.SetStyle(block, "text-align:justify-all; text-justify:auto");
        Assert.Contains(Render(block, 120).Texts, item => item.Text.Text == "乙");

        Css.SetStyle(block, "text-align:justify-all; text-justify:inter-word");
        Assert.Contains(Render(block, 120).Texts, item => item.Text.Text == "乙丙");
    }

    [Fact]
    public void InterWordDoesNotStretchCjkCharacterBoundaries()
    {
        var block = new TextBlock
        {
            Text = "你好世界你好世界", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };
        Css.SetStyle(block, "text-align:justify; text-justify:inter-word");
        var wordsOnly = Render(block, 65);
        Assert.Single(wordsOnly.Texts.GroupBy(item => item.Y).First());

        Css.SetStyle(block, "text-align:justify; text-justify:auto");
        var automatic = Render(block, 65);
        Assert.True(automatic.Texts.GroupBy(item => item.Y).First().Count() >= 2);
    }

    [Theory]
    [InlineData("inter-character")]
    [InlineData("distribute")]
    public void InterCharacterSpreadsGraphemesAndKeepsSelectionAndHitTestingAligned(string method)
    {
        var block = new TextBlock
        {
            Text = "e\u0301B", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.NoWrap,
        };
        block.Select(2, 1);
        Css.SetStyle(block, $"text-align:justify-all; text-justify:{method}");

        var draw = Render(block, 120);
        Assert.Equal(new[] { "e\u0301", "B" }, draw.Texts.Select(item => item.Text.Text));
        Assert.InRange(draw.Texts[1].X, 95, 120);
        Assert.InRange(Assert.Single(draw.Rectangles).X, draw.Texts[1].X - 1, draw.Texts[1].X + 1);
        var first = block.GetPositionFromPoint(new Point(draw.Texts[0].X + 2, draw.Texts[0].Y + 5), true);
        var second = block.GetPositionFromPoint(new Point(draw.Texts[1].X + 2, draw.Texts[1].Y + 5), true);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second.Offset > first.Offset);

        Css.SetStyle(block, "text-align:justify-all; text-justify:inter-word");
        var wordOnly = Render(block, 120);
        Assert.Single(wordOnly.Texts);
        Assert.InRange(wordOnly.Texts[0].X, 35, 55);
    }

    [Fact]
    public void TextJustifyInheritsAndAnExplicitInitialResetsIt()
    {
        var parent = new StackPanel();
        var child = new TextBlock
        {
            Text = "AB", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.NoWrap,
        };
        parent.Children.Add(child);
        Css.SetStyle(parent, "text-justify:inter-character");
        Css.SetStyle(child, "text-align:justify-all");
        Assert.Equal(2, Render(child, 120).Texts.Count);

        Css.SetStyle(child, "text-align:justify-all; text-justify:initial");
        Assert.Single(Render(child, 120).Texts);
    }

    [Theory]
    [InlineData("inter-character")]
    [InlineData("distribute")]
    public void SplitCaseExpansionJustifiesPaintedCharactersAndSourceSelection(string method)
    {
        var block = new TextBlock
        {
            Text = "ßA", FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(block, $"white-space:normal; text-transform:uppercase; " +
            $"line-break:anywhere; text-align:justify; text-justify:{method}; " +
            "text-decoration:underline solid red 2px");
        block.Select(0, 1);
        var reference = new TextBlock { Text = "SS", FontSize = 20 };
        reference.Measure(new Size(double.PositiveInfinity, 180));
        var width = reference.DesiredSize.Width + 8;
        var draw = Render(block, width);
        var rows = draw.Texts.GroupBy(item => item.Y).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal(new[] { "S", "S" }, rows[0].Select(item => item.Text.Text));
        Assert.Equal("A", Assert.Single(rows[1]).Text.Text);
        Assert.True(rows[0].Last().X > rows[0].First().X + 17);
        Assert.InRange(Assert.Single(draw.Rectangles).Right, width - 1, width + 1);
        Assert.InRange(draw.Lines[0].End.X, width - 1, width + 1);
        Assert.Equal("ß", block.SelectedText);
    }

    [Fact]
    public void SplitCaseExpansionJustifiesWordGapBeforeNextSourceCharacter()
    {
        static TextBlock Block(string align)
        {
            var block = new TextBlock
            {
                Text = "ß Aß", FontSize = 20, Foreground = Brushes.Black,
            };
            Css.SetStyle(block, "white-space:normal; text-transform:uppercase; " +
                $"line-break:anywhere; text-align:{align}; text-justify:inter-word");
            block.Select(2, 1);
            return block;
        }

        var reference = new TextBlock { Text = "SS A", FontSize = 20 };
        reference.Measure(new Size(double.PositiveInfinity, 180));
        var width = reference.DesiredSize.Width + 5;
        var justified = Block("justify");
        var stretched = Render(justified, width);
        var natural = Render(Block("left"), width);

        var selected = Assert.Single(stretched.Rectangles);
        Assert.True(selected.X >
            Assert.Single(natural.Rectangles).X + 3);
        Assert.Equal("SS A", string.Concat(stretched.Texts
            .GroupBy(item => item.Y).First().Select(item => item.Text.Text)));
        Assert.Equal(2, justified.GetPositionFromPoint(
            new Point(selected.X + 2, selected.Y + 5), true)?.Offset);
    }

    [Fact]
    public void SplitCaseExpansionJustifiesFinalLineWithRunFontAndTracking()
    {
        var run = new Run("ß") { FontSize = 28, Foreground = Brushes.Blue };
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "white-space:normal; line-break:anywhere; " +
            "text-align:justify-all; text-justify:inter-character; " +
            "letter-spacing:6px; text-decoration:underline solid red 2px");
        Css.SetStyle(run, "text-transform:uppercase");
        block.SelectAll();
        var draw = Render(block, 120);

        Assert.Equal(new[] { "S", "S" }, draw.Texts.Select(item => item.Text.Text));
        Assert.All(draw.Texts, item =>
        {
            Assert.Equal(28, item.Text.FontSize);
            Assert.Equal(Colors.Blue,
                Assert.IsType<SolidColorBrush>(item.Text.Foreground).Color);
        });
        Assert.InRange(draw.Texts[1].X, 95, 120);
        Assert.InRange(Assert.Single(draw.Rectangles).Right, 119, 121);
        Assert.InRange(draw.Lines[0].End.X, 119, 121);
        Assert.Equal("ß", block.SelectedText);
    }

    [Fact]
    public void UnwrappedCaseExpansionAlsoProvidesPaintedJustificationOpportunity()
    {
        var block = new TextBlock
        {
            Text = "ß", FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(block, "white-space:nowrap; text-transform:uppercase; " +
            "text-align:justify-all; text-justify:inter-character");
        block.SelectAll();
        var draw = Render(block, 120);

        Assert.Equal(new[] { "S", "S" }, draw.Texts.Select(item => item.Text.Text));
        Assert.InRange(draw.Texts[1].X, 95, 120);
        Assert.InRange(Assert.Single(draw.Rectangles).Right, 119, 121);
        Assert.Equal("ß", block.SelectedText);
    }

    [Fact]
    public void NormalWrappingKeepsItsSpaceBreakWhileJustifyingAnExpansion()
    {
        var block = new TextBlock
        {
            Text = "ß A", FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase; " +
            "text-align:justify; text-justify:inter-character");
        block.Select(0, 1);
        var reference = new TextBlock { Text = "SS", FontSize = 20 };
        reference.Measure(new Size(double.PositiveInfinity, 180));
        var width = reference.DesiredSize.Width + 8;
        var draw = Render(block, width);
        var rows = draw.Texts.GroupBy(item => item.Y).ToArray();

        Assert.Equal(new[] { "SS ", "A" }, rows.Select(row =>
            string.Concat(row.Select(item => item.Text.Text))));
        Assert.InRange(Assert.Single(draw.Rectangles).Right, width - 1, width + 1);
    }

    private static TextBlock CreateBlock(string style)
    {
        var block = new TextBlock
        {
            Text = "one two three four five", FontSize = 20,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(block, style);
        return block;
    }

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
        public List<(Point Start, Point End)> Lines { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X, origin.Y));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add((point0, point1));
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
