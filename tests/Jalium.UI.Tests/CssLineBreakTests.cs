using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssLineBreakTests
{
    [Theory]
    [InlineData("auto", true)]
    [InlineData("loose", true)]
    [InlineData("normal", true)]
    [InlineData("strict", true)]
    [InlineData("anywhere", true)]
    [InlineData("break-word", false)]
    [InlineData("strict loose", false)]
    public void SupportsTheLineBreakGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("line-break", value));

    [Fact]
    public void AnywhereUsesLastFittingGraphemeWithoutPreferringWhitespace()
    {
        var block = Block("ab cd");
        Css.SetStyle(block, "line-break:auto");
        var automatic = Render(block, 40);

        Css.SetStyle(block, "line-break:anywhere");
        var anywhere = Render(block, 40);

        Assert.Equal("ab ", automatic[0]);
        Assert.Equal("ab c", anywhere[0]);
        Assert.Equal(block.Text, string.Concat(anywhere));
    }

    [Theory]
    [InlineData("\u2060")]
    [InlineData("\u00A0")]
    public void AnywhereIgnoresNonbreakingCharactersAndKeepAllButPreservesGraphemeClusters(string separator)
    {
        var block = Block("e\u0301" + separator + "abcdef");
        Css.SetStyle(block, "line-break:anywhere; word-break:keep-all; overflow-wrap:normal");
        var lines = Render(block, 5);

        Assert.Equal("e\u0301", lines[0]);
        Assert.Equal(block.Text, string.Concat(lines));
    }

    [Fact]
    public void AnywhereReducesIntrinsicWidthAndRespectsNoWrap()
    {
        var block = Block("abcdefghijklmno");
        Css.SetStyle(block, "line-break:normal");
        block.Measure(new Size(0, 400));
        var normalWidth = block.DesiredSize.Width;

        Css.SetStyle(block, "line-break:anywhere");
        block.Measure(new Size(0, 400));
        var anywhereWidth = block.DesiredSize.Width;
        Assert.True(normalWidth > anywhereWidth + 50);

        Css.SetStyle(block, "line-break:anywhere; white-space:nowrap");
        Assert.Single(Render(block, 40));

        block.TextWrapping = TextWrapping.NoWrap;
        Css.SetStyle(block, "line-break:anywhere; white-space:normal");
        Assert.Single(Render(block, 40));
    }

    [Fact]
    public void LoosePermitsBreakBeforeSmallKanaAndIterationMarks()
    {
        var block = Block("漢字ぁい");
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:loose");
        Assert.Equal("漢字", Render(block, 45)[0]);

        block.Text = "漢字々い";
        Assert.Equal("漢字", Render(block, 45)[0]);
        Css.SetStyle(block, "line-break:strict");
        Assert.Equal("漢", Render(block, 45)[0]);
    }

    [Fact]
    public void LoosePermitsBreakBeforeHyphenAndCenteredPunctuation()
    {
        var block = Block("漢字‐かな");
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:loose");
        Assert.Equal("漢字", Render(block, 45)[0]);

        block.Language = XmlLanguage.GetLanguage("ja");
        block.Text = "漢字・かな";
        Assert.Equal("漢字", Render(block, 45)[0]);
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);
    }

    [Theory]
    [InlineData("あ")]
    [InlineData("😀")]
    public void LooseHyphenBreakUsesUnicodeIdeographicClass(string preceding)
    {
        var first = Block(preceding);
        first.Measure(new Size(double.PositiveInfinity, 400));
        var width = first.DesiredSize.Width + 0.5;
        var block = Block(preceding + "‐い");

        Css.SetStyle(block, "line-break:normal");
        Assert.Equal(preceding + "‐", Render(block, width)[0]);

        Css.SetStyle(block, "line-break:loose");
        Assert.Equal(preceding, Render(block, width)[0]);
    }

    [Fact]
    public void BreakAllTreatsLatinLetterAsIdeographicBeforeLooseHyphen()
    {
        var first = Block("a");
        first.Measure(new Size(double.PositiveInfinity, 400));
        var width = first.DesiredSize.Width + 0.5;
        var block = Block("a‐字");

        Css.SetStyle(block, "line-break:loose; word-break:normal");
        Assert.Equal("a‐", Render(block, width)[0]);

        Css.SetStyle(block, "line-break:loose; word-break:break-all");
        Assert.Equal("a", Render(block, width)[0]);
    }

    [Theory]
    [InlineData("％")]
    [InlineData("‰")]
    public void WidePostfixBreakRequiresLooseCjkWritingSystem(string postfix)
    {
        var block = Block("漢字" + postfix + "い");
        block.Language = XmlLanguage.GetLanguage("ja");
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:loose");
        Assert.Equal("漢字", Render(block, 45)[0]);

        block.Language = XmlLanguage.GetLanguage("en");
        Assert.Equal("漢", Render(block, 45)[0]);
    }

    [Fact]
    public void WidePrefixBreakRequiresLooseCjkWritingSystem()
    {
        var block = Block("漢￥字");
        block.Language = XmlLanguage.GetLanguage("zh");
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:loose");
        Assert.Equal("漢￥", Render(block, 45)[0]);

        block.Language = XmlLanguage.GetLanguage("zh-Latn");
        Assert.Equal("漢", Render(block, 45)[0]);
    }

    [Fact]
    public void LoosePermitsUnicodeInseparablePunctuationBoundary()
    {
        var block = Block("漢字⋯い");
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:loose");
        Assert.Equal("漢字", Render(block, 45)[0]);
    }

    [Fact]
    public void CjkHyphenBreakDependsOnStrictnessAndWritingSystem()
    {
        var block = Block("漢字〜い");
        Css.SetStyle(block, "line-break:normal");
        Assert.Equal("漢", Render(block, 45)[0]);

        block.Language = XmlLanguage.GetLanguage("ja");
        Assert.Equal("漢字", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:strict");
        Assert.Equal("漢", Render(block, 45)[0]);

        Css.SetStyle(block, "line-break:normal");
        block.Language = XmlLanguage.GetLanguage("ja-Latn");
        Assert.Equal("漢", Render(block, 45)[0]);

        block.Language = XmlLanguage.GetLanguage("en-Hani");
        Assert.Equal("漢字", Render(block, 45)[0]);
    }

    [Fact]
    public void ParentChangesAndInitialResetRebuildDescendantLines()
    {
        var parent = new StackPanel();
        var block = Block("abcdefghijklmno");
        parent.Children.Add(block);
        Css.SetStyle(parent, "line-break:normal");
        Assert.Single(Render(block, 40));

        Css.SetStyle(parent, "line-break:anywhere");
        Assert.True(Render(block, 40).Length > 1);

        Css.SetStyle(block, "line-break:initial");
        Assert.Single(Render(block, 40));
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
        return context.Texts.ToArray();
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<string> Texts { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add(formattedText.Text);
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
