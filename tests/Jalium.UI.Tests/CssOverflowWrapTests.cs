using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssOverflowWrapTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("anywhere", true)]
    [InlineData("break-word", true)]
    [InlineData("break-all", false)]
    public void SupportsTheOverflowWrapGrammar(string value, bool expected)
    {
        Assert.Equal(expected, Css.Supports("overflow-wrap", value));
        Assert.Equal(expected, Css.Supports("word-wrap", value));
    }

    [Fact]
    public void NormalOverflowsAnUnbreakableWordWhileAnywhereBreaksIt()
    {
        var block = Block("supercalifragilisticexpialidocious tail");
        Css.SetStyle(block, "white-space:normal; overflow-wrap:normal");
        var normal = Render(block, 70);
        Assert.StartsWith("supercalifragilisticexpialidocious", normal[0]);
        Assert.Equal(2, normal.Length);

        Css.SetStyle(block, "white-space:normal; overflow-wrap:anywhere");
        var anywhere = Render(block, 70);
        Assert.True(anywhere.Length > normal.Length);
        Assert.Equal(block.Text, string.Concat(anywhere));
    }

    [Fact]
    public void BreakWordSharesUsedLineBreaksButKeepsTheNormalIntrinsicWidth()
    {
        var block = Block("abcdefghijklmno");
        Css.SetStyle(block, "overflow-wrap:anywhere");
        block.Measure(new Size(0, 200));
        var anywhereIntrinsic = block.DesiredSize.Width;
        var anywhere = Render(block, 55);

        Css.SetStyle(block, "overflow-wrap:break-word");
        block.Measure(new Size(0, 200));
        var breakWordIntrinsic = block.DesiredSize.Width;
        var breakWord = Render(block, 55);

        Assert.True(breakWordIntrinsic > anywhereIntrinsic + 30,
            $"anywhere={anywhereIntrinsic}, break-word={breakWordIntrinsic}");
        Assert.Equal(anywhere, breakWord);
        Assert.True(breakWord.Length > 1);
    }

    [Fact]
    public void EarlierWhitespaceBreakWinsBeforeEmergencyBreaks()
    {
        var block = Block("short abcdefghijklmnop");
        Css.SetStyle(block, "white-space:normal; overflow-wrap:anywhere");

        var lines = Render(block, 100);
        Assert.Equal("short ", lines[0]);
        Assert.True(lines.Length > 2);
    }

    [Fact]
    public void AnywhereKeepsExtendedGraphemeClustersWhole()
    {
        const string emoji = "👨‍👩‍👧‍👦";
        var block = Block(emoji + "X");
        Css.SetStyle(block, "overflow-wrap:anywhere");

        Assert.Equal(new[] { emoji, "X" }, Render(block, 5));
    }

    [Fact]
    public void WordWrapAndOverflowWrapShareOneCascadeSlot()
    {
        var block = Block("abcdefghijklmno");
        Css.SetStyle(block, "word-wrap:anywhere; overflow-wrap:normal");
        Assert.Single(Render(block, 55));

        Css.SetStyle(block, "overflow-wrap:normal; word-wrap:anywhere");
        Assert.True(Render(block, 55).Length > 1);

        Css.SetStyle(block, "overflow-wrap:anywhere; word-wrap:initial");
        Assert.Single(Render(block, 55));
    }

    [Fact]
    public void InheritedChangeRebuildsLinesAndNativeLocalWrappingWins()
    {
        var parent = new StackPanel();
        var block = Block("abcdefghijklmno");
        parent.Children.Add(block);
        Css.SetStyle(parent, "overflow-wrap:normal");
        Assert.Single(Render(block, 55));

        Css.SetStyle(parent, "overflow-wrap:anywhere");
        Assert.Equal(CssOverflowWrap.Anywhere,
            block.GetValue(CssFlowProperties.OverflowWrapProperty));
        Assert.NotEqual(BaseValueSource.Default,
            block.GetValueSourceInternal(CssFlowProperties.OverflowWrapProperty).BaseValueSource);
        Assert.True(Render(block, 55).Length > 1);

        block.TextWrapping = TextWrapping.WrapWithOverflow;
        Assert.Single(Render(block, 55));
    }

    [Fact]
    public void CssNoWrapDisablesOverflowWrap()
    {
        var block = Block("abcdefghijklmno");
        Css.SetStyle(block, "white-space:nowrap; overflow-wrap:anywhere");

        Assert.Single(Render(block, 55));
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static string[] Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var context = new TextDrawingContext();
        block.Render(context);
        return context.Texts.Select(item => item.Text).ToArray();
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(string Text, double Y)> Texts { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText.Text, origin.Y));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
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
