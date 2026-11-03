using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextAutospaceTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("auto", true)]
    [InlineData("no-autospace", true)]
    [InlineData("ideograph-alpha", true)]
    [InlineData("ideograph-numeric", true)]
    [InlineData("ideograph-numeric ideograph-alpha", true)]
    [InlineData("insert ideograph-alpha", true)]
    [InlineData("ideograph-alpha insert ideograph-numeric", true)]
    [InlineData("insert", true)]
    [InlineData("ideograph-alpha ideograph-alpha", false)]
    [InlineData("normal ideograph-alpha", false)]
    [InlineData("no-autospace insert", false)]
    [InlineData("punctuation", true)]
    [InlineData("ideograph-alpha replace", true)]
    [InlineData("replace ideograph-numeric punctuation", true)]
    [InlineData("punctuation ideograph-alpha ideograph-numeric insert", true)]
    [InlineData("replace punctuation ideograph-alpha ideograph-numeric", true)]
    [InlineData("replace insert", false)]
    [InlineData("replace replace", false)]
    [InlineData("punctuation punctuation", false)]
    public void SupportsAutospaceGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-autospace", value));

    [Theory]
    [InlineData("中A文", "normal", 2)]
    [InlineData("中1文", "normal", 2)]
    [InlineData("カA", "normal", 1)]
    [InlineData("中1文", "ideograph-alpha", 0)]
    [InlineData("中A文", "ideograph-numeric", 0)]
    [InlineData("中Ａ文", "normal", 0)]
    [InlineData("中９", "normal", 0)]
    [InlineData("가A", "normal", 0)]
    [InlineData("中ᄀ", "normal", 0)]
    [InlineData("㋿A", "normal", 1)]
    [InlineData("·A", "normal", 1)]
    [InlineData("中😀A", "normal", 0)]
    [InlineData("中 A", "normal", 0)]
    public void SpacesOnlyAtSelectedDirectInterscriptBoundaries(
        string text, string mode, int gaps)
    {
        var block = Block(text);
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(block, $"text-autospace:{mode}; white-space:nowrap");
        var difference = Width(block) - baseline;
        if (gaps == 0) Assert.InRange(difference, -0.5, 0.5);
        else Assert.InRange(difference, gaps * 1.5, gaps * 4.5);
    }

    [Fact]
    public void AutospaceAddsToLetterSpacingAndTracksSelection()
    {
        var block = Block("中A");
        Css.SetStyle(block,
            "text-autospace:no-autospace; letter-spacing:10px; white-space:nowrap");
        var tracked = Width(block);
        block.Select(1, 1);
        Css.SetStyle(block,
            "text-autospace:normal; letter-spacing:10px; white-space:nowrap");
        Assert.InRange(Width(block) - tracked, 1.5, 4.5);
        var draw = Render(block, 200);
        var latin = Assert.Single(draw.Texts, item => item.Text == "A");
        var selection = Assert.Single(draw.Rectangles);
        Assert.InRange(selection.X, latin.X - 1, latin.X + 1);
        Assert.Equal(1, block.GetPositionFromPoint(new Point(latin.X + 2, latin.Y + 5), true)?.Offset);
        Assert.Equal("中A", block.Text);
        Assert.Equal("A", block.SelectedText);
    }

    [Fact]
    public void CommonSpanControlsBoundaryBetweenRuns()
    {
        var left = new Run("中");
        var right = new Run("A");
        var span = new Span(left);
        span.Inlines.Add(right);
        var block = new TextBlock(span) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(span, "text-autospace:normal");
        Assert.InRange(Width(block) - baseline, 1.5, 4.5);
        Css.SetStyle(span, "text-autospace:no-autospace");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void InheritedModeAndInitialKeywordRebuildTheText()
    {
        var parent = new StackPanel();
        var block = Block("中A");
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-autospace:no-autospace");
        var unspaced = Width(block);
        Css.SetStyle(parent, "text-autospace:normal");
        Assert.InRange(Width(block) - unspaced, 1.5, 4.5);
        Css.SetStyle(block, "text-autospace:no-autospace");
        Assert.InRange(Width(block) - unspaced, -0.5, 0.5);
        Css.SetStyle(block, "text-autospace:initial");
        Assert.InRange(Width(block) - unspaced, 1.5, 4.5);
    }

    [Fact]
    public void ForcedBreakHasNoTrailingOrLeadingAutospace()
    {
        var block = Block("中\nA");
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:pre");
        var baseline = Width(block);
        Css.SetStyle(block, "text-autospace:normal; white-space:pre");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void AddedAdvanceCanChangeEmergencyWrapWithoutLeavingLineEdgeSpace()
    {
        var block = Block("中A");
        Css.SetStyle(block,
            "text-autospace:no-autospace; white-space:normal; overflow-wrap:anywhere");
        // DesiredSize reserves two pixels beyond the actual text advance.
        var narrowWidth = Width(block) - 0.5;
        var unspaced = Render(block, narrowWidth);
        Assert.Single(unspaced.Texts.Select(item => item.Y).Distinct());

        Css.SetStyle(block,
            "text-autospace:normal; white-space:normal; overflow-wrap:anywhere");
        Assert.True(Width(block) > narrowWidth);
        var spaced = Render(block, narrowWidth);
        Assert.Equal(2, spaced.Texts.Select(item => item.Y).Distinct().Count());
        var latin = Assert.Single(spaced.Texts, item => item.Text == "A");
        Assert.InRange(latin.X, -0.5, 0.5);
    }

    [Fact]
    public void CaseExpandedPaintKeepsOneSourceAutospaceGap()
    {
        var block = Block("中ß");
        Css.SetStyle(block,
            "text-autospace:no-autospace; text-transform:uppercase; white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(block,
            "text-autospace:normal; text-transform:uppercase; white-space:nowrap");
        Assert.InRange(Width(block) - baseline, 1.5, 4.5);
        var draw = Render(block, 200);
        Assert.Equal("中SS", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Equal("中ß", block.Text);
    }

    [Fact]
    public void AutospaceParticipatesInCaseExpandedPaintWrapMeasurement()
    {
        var block = Block("中ß");
        Css.SetStyle(block,
            "text-autospace:no-autospace; text-transform:uppercase; " +
            "white-space:normal; overflow-wrap:anywhere");
        var unspacedWidth = Width(block);
        Css.SetStyle(block,
            "text-autospace:normal; text-transform:uppercase; " +
            "white-space:normal; overflow-wrap:anywhere");
        Assert.InRange(Width(block) - unspacedWidth, 1.5, 4.5);
        var spaced = Render(block, 200);
        Assert.Single(spaced.Texts.Select(item => item.Y).Distinct());
        Assert.Equal("中SS", string.Concat(spaced.Texts.Select(item => item.Text)));
        Assert.Equal("中ß", block.Text);
    }

    [Theory]
    [InlineData("中 A", "ideograph-alpha replace", "中A")]
    [InlineData("A 中文", "ideograph-alpha replace", "A中文")]
    [InlineData("中 1", "ideograph-numeric replace", "中1")]
    [InlineData("中 1", "ideograph-alpha ideograph-numeric replace", "中1")]
    public void ReplaceUsesAutospaceAdvanceWithoutChangingSource(
        string source, string mode, string unspaced)
    {
        var replaced = Block(source);
        Css.SetStyle(replaced, $"text-autospace:{mode}; white-space:pre");
        var direct = Block(unspaced);
        Css.SetStyle(direct, "text-autospace:normal; white-space:pre");
        Assert.InRange(Width(replaced) - Width(direct), -0.5, 0.5);
        Assert.Equal(source, replaced.Text);
        replaced.SelectAll();
        Assert.Equal(source, replaced.SelectedText);
        Assert.Contains(Render(replaced, 200).Texts, item => item.Text.Contains('\u00A0'));
    }

    [Fact]
    public void ReplaceAndWordSpacingAddAtTheSourceSpace()
    {
        var direct = Block("中A");
        Css.SetStyle(direct, "text-autospace:normal; white-space:pre");
        var baseline = Width(direct);
        var block = Block("中 A");
        Css.SetStyle(block,
            "text-autospace:ideograph-alpha replace; word-spacing:4px; white-space:pre");
        Assert.InRange(Width(block) - baseline, 3.5, 4.5);
        block.Select(2, 1);
        var latin = Assert.Single(Render(block, 200).Texts,
            item => item.Text == "A");
        Assert.Equal(2, block.GetPositionFromPoint(new Point(latin.X + 2, latin.Y + 5), true)?.Offset);
    }

    [Fact]
    public void ReplacedSpaceRetainsSelectableSourceOffset()
    {
        var block = Block("中 A");
        block.Select(1, 1);
        Css.SetStyle(block,
            "text-autospace:ideograph-alpha replace; white-space:pre");
        var draw = Render(block, 200);
        var selected = Assert.Single(draw.Rectangles);
        Assert.InRange(selected.Width, 1.5, 4.5);
        Assert.Equal(" ", block.SelectedText);
        Assert.Equal(1, block.GetPositionFromPoint(
            new Point(selected.X + selected.Width / 2, selected.Y + 5), true)?.Offset);
    }

    [Fact]
    public void ReplaceUsesTheVisibleSpaceAfterCssWhitespaceCollapse()
    {
        var block = Block("中  A");
        Css.SetStyle(block,
            "text-autospace:ideograph-alpha replace; white-space:normal");
        var direct = Block("中A");
        Css.SetStyle(direct, "text-autospace:normal; white-space:normal");
        Assert.InRange(Width(block) - Width(direct), -0.5, 0.5);
        block.SelectAll();
        Assert.Equal("中  A", block.SelectedText);
    }

    [Fact]
    public void ReplaceCombinesWithTrackingAcrossTheRemovedSpace()
    {
        var direct = Block("中A");
        Css.SetStyle(direct,
            "text-autospace:normal; letter-spacing:6px; white-space:pre");
        var replaced = Block("中 A");
        Css.SetStyle(replaced,
            "text-autospace:ideograph-alpha replace; letter-spacing:6px; white-space:pre");
        Assert.InRange(Width(replaced) - Width(direct), -0.5, 0.5);
    }

    [Fact]
    public void ReplaceUsesTheCommonSpanAcrossThreeRuns()
    {
        var span = new Span(new Run("中"));
        span.Inlines.Add(new Run(" "));
        span.Inlines.Add(new Run("A"));
        var block = new TextBlock(span) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:pre");
        Css.SetStyle(span, "text-autospace:ideograph-alpha replace");
        var direct = Block("中A");
        Css.SetStyle(direct, "text-autospace:normal; white-space:pre");
        Assert.InRange(Width(block) - Width(direct), -0.5, 0.5);
    }

    [Fact]
    public void ReplaceMeasuresExpandedPaintTextWithOriginalOffsets()
    {
        var direct = Block("中ß");
        Css.SetStyle(direct,
            "text-autospace:normal; text-transform:uppercase; overflow-wrap:anywhere");
        var replaced = Block("中 ß");
        Css.SetStyle(replaced,
            "text-autospace:ideograph-alpha replace; text-transform:uppercase; " +
            "overflow-wrap:anywhere");
        Assert.InRange(Width(replaced) - Width(direct), -0.5, 0.5);
        replaced.SelectAll();
        Assert.Equal("中 ß", replaced.SelectedText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("; line-break:anywhere")]
    public void ReplacingASpaceRetainsTheAdjacentIdeographLineBreak(string breakStyle)
    {
        var direct = Block("中A");
        Css.SetStyle(direct, "text-autospace:normal; white-space:normal" + breakStyle);
        var replaced = Block("中 A");
        Css.SetStyle(replaced,
            "text-autospace:ideograph-alpha replace; white-space:normal" + breakStyle);
        var directDraw = Render(direct, 25);
        var replacedDraw = Render(replaced, 25);
        var directRows = directDraw.Texts.Select(item => item.Y).Distinct().Count();
        var replacedRows = replacedDraw.Texts.Select(item => item.Y).Distinct().Count();
        Assert.Equal(2, directRows);
        Assert.Equal(directRows, replacedRows);
        Assert.InRange(replacedDraw.Texts[0].X - directDraw.Texts[0].X, -0.5, 0.5);
        Assert.InRange(replacedDraw.Texts[^1].X - directDraw.Texts[^1].X, -0.5, 0.5);
    }

    [Fact]
    public void ExpandedPaintWrapDoesNotKeepReplacementAtTheLineEdge()
    {
        var direct = Block("中ß");
        Css.SetStyle(direct,
            "text-autospace:normal; text-transform:uppercase; " +
            "overflow-wrap:anywhere; text-align:right");
        var replaced = Block("中 ß");
        Css.SetStyle(replaced,
            "text-autospace:ideograph-alpha replace; text-transform:uppercase; " +
            "overflow-wrap:anywhere; text-align:right");
        var directDraw = Render(direct, 25);
        var replacedDraw = Render(replaced, 25);
        Assert.Equal(directDraw.Texts.Select(item => item.Y).Distinct().Count(),
            replacedDraw.Texts.Select(item => item.Y).Distinct().Count());
        var directHan = Assert.Single(directDraw.Texts, item => item.Text == "中");
        var replacedHan = Assert.Single(replacedDraw.Texts, item => item.Text == "中");
        Assert.InRange(replacedHan.X - directHan.X, -0.5, 0.5);
    }

    [Theory]
    [InlineData("中\u00A0A")]
    [InlineData("中\u2002A")]
    [InlineData("中  A")]
    public void OtherOrRepeatedSpacesSuppressReplacement(string source)
    {
        var block = Block(source);
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:pre");
        var baseline = Width(block);
        Css.SetStyle(block, "text-autospace:ideograph-alpha replace; white-space:pre");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
        Assert.Equal(source, block.Text);
    }

    [Theory]
    [InlineData("Salut!", "Salut\u202F!")]
    [InlineData("Salut?", "Salut\u202F?")]
    [InlineData("Salut;", "Salut\u202F;")]
    [InlineData("Titre:", "Titre\u00A0:")]
    [InlineData("«Salut»", "«\u00A0Salut\u00A0»")]
    public void FrenchPunctuationUsesTheSpecifiedNoBreakSpaceWidths(
        string source, string withSpaces)
    {
        var automatic = Block(source);
        automatic.Language = XmlLanguage.GetLanguage("fr");
        Css.SetStyle(automatic, "text-autospace:punctuation; white-space:nowrap");
        var explicitSpaces = Block(withSpaces);
        Css.SetStyle(explicitSpaces, "text-autospace:no-autospace; white-space:nowrap");
        Assert.InRange(Width(automatic) - Width(explicitSpaces), -0.5, 0.5);
        Assert.Equal(source, automatic.Text);
        automatic.SelectAll();
        Assert.Equal(source, automatic.SelectedText);
    }

    [Theory]
    [InlineData("Salut !")]
    [InlineData("Salut\u202F!")]
    [InlineData("Salut\u00A0!")]
    [InlineData("Salut\u2002!")]
    public void ExistingSpacesSuppressFrenchPunctuationInsertion(string source)
    {
        var block = Block(source);
        block.Language = XmlLanguage.GetLanguage("fr");
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:pre");
        var baseline = Width(block);
        Css.SetStyle(block, "text-autospace:punctuation; white-space:pre");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void PunctuationOnlyAppliesInFrenchContent()
    {
        var block = Block("Salut!");
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(block, "text-autospace:punctuation; white-space:nowrap");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
        block.Language = XmlLanguage.GetLanguage("fr-CA");
        Assert.True(Width(block) > baseline + 1);
        block.Language = XmlLanguage.GetLanguage("en-GB");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Theory]
    [InlineData("Salut !", "Salut\u202F!", '\u202F')]
    [InlineData("Titre :", "Titre\u00A0:", '\u00A0')]
    [InlineData("« mot", "«\u00A0mot", '\u00A0')]
    public void FrenchReplaceKeepsSourceSpaceAndUsesTheSpecifiedGlyph(
        string source, string withSpace, char glyph)
    {
        var block = Block(source);
        block.Language = XmlLanguage.GetLanguage("fr");
        Css.SetStyle(block, "text-autospace:punctuation replace; white-space:pre");
        var explicitSpace = Block(withSpace);
        Css.SetStyle(explicitSpace, "text-autospace:no-autospace; white-space:pre");
        Assert.InRange(Width(block) - Width(explicitSpace), -0.5, 0.5);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
        Assert.Contains(Render(block, 200).Texts, item => item.Text.Contains(glyph));
    }

    [Fact]
    public void FrenchReplacementAddsWordSpacingAndKeepsTheSpaceSelectable()
    {
        var block = Block("Salut !");
        block.Language = XmlLanguage.GetLanguage("fr");
        Css.SetStyle(block, "text-autospace:punctuation replace; white-space:pre");
        var baseline = Width(block);
        block.Select(5, 1);
        Css.SetStyle(block,
            "text-autospace:punctuation replace; word-spacing:4px; white-space:pre");
        Assert.InRange(Width(block) - baseline, 3.5, 4.5);
        Assert.Equal(" ", block.SelectedText);
        var selection = Assert.Single(Render(block, 200).Rectangles);
        Assert.True(selection.Width > 4);
    }

    [Fact]
    public void CommonSpanLanguageControlsFrenchPunctuationAcrossRuns()
    {
        var span = new Span(new Run("«"));
        span.Inlines.Add(new Run("mot"));
        var block = new TextBlock(span) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:nowrap");
        Css.SetStyle(span, "text-autospace:punctuation");
        var baseline = Width(block);
        span.Language = XmlLanguage.GetLanguage("fr");
        Assert.True(Width(block) > baseline + 1);
        span.Language = XmlLanguage.GetLanguage("en");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void FrenchQuoteInnerSpacingKeepsItsNeighborOnTheSameLine()
    {
        var block = Block("«中");
        block.Language = XmlLanguage.GetLanguage("fr");
        Css.SetStyle(block, "text-autospace:no-autospace; white-space:normal");
        var narrowWidth = Width(Block("«")) + 1;
        var unspaced = Render(block, narrowWidth);
        Assert.Equal(2, unspaced.Texts.Select(item => item.Y).Distinct().Count());
        Css.SetStyle(block, "text-autospace:punctuation; white-space:normal");
        var spaced = Render(block, narrowWidth);
        Assert.Single(spaced.Texts.Select(item => item.Y).Distinct());
    }

    [Fact]
    public void FrenchPunctuationMeasuresCaseExpandedPaintWrap()
    {
        var block = Block("ß!");
        block.Language = XmlLanguage.GetLanguage("fr");
        Css.SetStyle(block,
            "text-autospace:no-autospace; text-transform:uppercase; " +
            "white-space:normal; overflow-wrap:anywhere");
        var baseline = Width(block);
        Css.SetStyle(block,
            "text-autospace:punctuation; text-transform:uppercase; " +
            "white-space:normal; overflow-wrap:anywhere");
        Assert.True(Width(block) > baseline + 1);
        var draw = Render(block, 200);
        Assert.Equal("SS!", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Equal("ß!", block.Text);
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static double Width(TextBlock block)
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
