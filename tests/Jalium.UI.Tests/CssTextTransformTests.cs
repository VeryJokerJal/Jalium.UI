using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextTransformTests
{
    [Theory]
    [InlineData("none", true)]
    [InlineData("capitalize", true)]
    [InlineData("uppercase", true)]
    [InlineData("lowercase", true)]
    [InlineData("full-width", true)]
    [InlineData("full-size-kana", true)]
    [InlineData("uppercase full-width", true)]
    [InlineData("full-size-kana full-width lowercase", true)]
    [InlineData("full-width capitalize full-size-kana", true)]
    [InlineData("uppercase lowercase", false)]
    [InlineData("uppercase uppercase", false)]
    [InlineData("full-width full-width", false)]
    [InlineData("none full-width", false)]
    [InlineData("full-size-kana none", false)]
    [InlineData("full-width unknown", false)]
    public void SupportsTextTransformGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-transform", value));

    [Theory]
    [InlineData("none", true)]
    [InlineData("space", true)]
    [InlineData("ideographic-space", true)]
    [InlineData("space auto-phrase", true)]
    [InlineData("auto-phrase ideographic-space", true)]
    [InlineData("auto-phrase", false)]
    [InlineData("none space", false)]
    [InlineData("space ideographic-space", false)]
    [InlineData("space space", false)]
    [InlineData("space auto-phrase auto-phrase", false)]
    [InlineData("space unknown", false)]
    public void SupportsWordSpaceTransformGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("word-space-transform", value));

    [Fact]
    public void WordSpaceTransformReplacesZeroWidthSpaceAfterCollapsing()
    {
        const string source = "A\u200B  B\u200BC";
        var block = Block(source);
        Css.SetStyle(block, "white-space:normal; word-space-transform:space");

        Assert.Equal("A  B C", DrawnText(block));
        Assert.Equal(MeasureWidth(Block("A  B C")), MeasureWidth(block), 1);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);

        Css.SetStyle(block, "white-space:normal; word-space-transform:ideographic-space");
        Assert.Equal("A\u3000 B\u3000C", DrawnText(block));
        Css.SetStyle(block, "white-space:normal; word-space-transform:none");
        Assert.Equal(source.Replace("  ", " "), DrawnText(block));
    }

    [Fact]
    public void GeneratedSpaceCombinesWithFullWidthAndWordSpacing()
    {
        var block = Block("a\u200Bb");
        Css.SetStyle(block, "word-space-transform:space; text-transform:full-width");
        Assert.Equal("ａ\u3000ｂ", DrawnText(block));

        Css.SetStyle(block, "word-space-transform:space; word-spacing:10px");
        var literal = Block("a b");
        Css.SetStyle(literal, "word-spacing:10px");
        Assert.Equal(MeasureWidth(literal), MeasureWidth(block), 1);
        block.SelectAll();
        Assert.Equal("a\u200Bb", block.SelectedText);
    }

    [Fact]
    public void GeneratedSpaceWrapsLikeAnAuthoredSpace()
    {
        var transformed = Block("alpha\u200Bbeta gamma");
        var literal = Block("alpha beta gamma");
        Css.SetStyle(transformed, "white-space:normal; word-space-transform:space");
        Css.SetStyle(literal, "white-space:normal");
        var width = MeasureWidth(Block("alpha beta")) + 1;

        string[] Rows(TextBlock block) => Render(block, width).Texts
            .GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        var expected = Rows(literal);
        Assert.True(expected.Length > 1);
        Assert.Equal(expected, Rows(transformed));
        transformed.SelectAll();
        Assert.Equal("alpha\u200Bbeta gamma", transformed.SelectedText);
    }

    [Fact]
    public void WordSpaceTransformUsesRunOverrideAndInheritedParentStyle()
    {
        var first = new Run("a\u200B");
        var second = new Run("b\u200B");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        var parent = new StackPanel();
        parent.Children.Add(block);
        Css.SetStyle(parent, "word-space-transform:space");
        Css.SetStyle(second, "word-space-transform:none");

        Assert.Equal("a b\u200B", DrawnText(block));
        Css.SetStyle(second, "word-space-transform:inherit");
        Css.SetStyle(parent, "word-space-transform:ideographic-space");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal("a\u3000b\u3000", DrawnText(block));
        block.SelectAll();
        Assert.Equal("a\u200Bb\u200B", block.SelectedText);
    }

    [Fact]
    public void WordSpaceTransformHonorsInitialUnsetAndAll()
    {
        var parent = new StackPanel();
        var block = Block("a\u200Bb");
        parent.Children.Add(block);
        Css.SetStyle(parent, "word-space-transform:space");

        Css.SetStyle(block, "word-space-transform:initial");
        Assert.Equal("a\u200Bb", DrawnText(block));
        Css.SetStyle(block, "word-space-transform:unset");
        Assert.Equal("a b", DrawnText(block));
        Css.SetStyle(block, "all:initial");
        Assert.Equal("a\u200Bb", DrawnText(block));
        Css.SetStyle(block, "all:unset");
        Assert.Equal("a b", DrawnText(block));
    }

    [Fact]
    public void WordSpaceTransformSkipsSeparatorsAdjacentToForcedBreaks()
    {
        var block = Block("a\u200B\nb\n\u200Bc");
        Css.SetStyle(block, "white-space:pre; word-space-transform:space");

        var rendered = DrawnText(block);
        Assert.Equal(2, rendered.Count(static value => value == '\u200B'));
        Assert.DoesNotContain(' ', rendered);
        block.SelectAll();
        Assert.Equal("a\u200B\nb\n\u200Bc", block.SelectedText);
    }

    [Fact]
    public void AccessTextInheritsWordSpaceTransformIntoItsTextBlock()
    {
        var access = new AccessText
        {
            Text = "_A\u200BB", FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(access, "word-space-transform:space");

        var draw = RenderElement(access);
        Assert.Equal("A B", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Single(draw.Lines);
        Assert.Equal("_A\u200BB", access.Text);
    }

    [Fact]
    public void FullWidthUsesUnicodeWideAndNarrowMappings()
    {
        const string source = "Ab! 1 ｶﾞ";
        var block = Block(source);
        Css.SetStyle(block, "text-transform:full-width; white-space:normal");

        Assert.Equal("Ａｂ！ １ カ\u3099", DrawnText(block));
        Assert.Equal(source, block.Text);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
        Assert.Equal(MeasureWidth(Block("Ａｂ！ １ カ\u3099")), MeasureWidth(block), 1);
    }

    [Theory]
    [InlineData("normal", "ａ ｂ")]
    [InlineData("nowrap", "ａ ｂ")]
    [InlineData("pre-line", "ａ ｂ")]
    [InlineData("pre", "ａ　　ｂ")]
    [InlineData("pre-wrap", "ａ　　ｂ")]
    [InlineData("break-spaces", "ａ　　ｂ")]
    public void FullWidthTransformsOnlyPreservedSpaces(string whiteSpace, string expected)
    {
        var block = Block("a  b");
        Css.SetStyle(block, $"white-space:{whiteSpace}; text-transform:full-width");
        Assert.Equal(expected, DrawnText(block));
    }

    [Fact]
    public void FullSizeKanaUsesNormativeSmallKanaMappingIncludingSupplementary()
    {
        const string source = "じゅう ァ ㇰ ｧ 𛄲";
        var block = Block(source);
        Css.SetStyle(block, "text-transform:full-size-kana");

        Assert.Equal("じゆう ア ク ｱ こ", DrawnText(block));
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
    }

    [Fact]
    public void CombinedWidthAndKanaTransformsRunInSpecifiedOrder()
    {
        var block = Block("ßaｧゅ");
        Css.SetStyle(block, "text-transform:full-size-kana uppercase full-width");
        Assert.Equal("ＳＳＡアゆ", DrawnText(block));
        block.SelectAll();
        Assert.Equal("ßaｧゅ", block.SelectedText);

        Css.SetStyle(block, "text-transform:full-size-kana");
        Assert.Equal("ßaｱゆ", DrawnText(block));
    }

    [Fact]
    public void RunOverrideKeepsSourceRangesAndSelection()
    {
        var first = new Run("a");
        var second = new Run("ｧ");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(block, "text-transform:full-width full-size-kana");
        Assert.Equal("ａア", DrawnText(block));

        Css.SetStyle(second, "text-transform:full-size-kana");
        Assert.Equal("ａｱ", DrawnText(block));
        block.Select(1, 1);
        Assert.Equal("ｧ", block.SelectedText);
    }

    [Fact]
    public void FullWidthClassificationAllowsIdeographicLineBreaks()
    {
        var block = Block("AB");
        Css.SetStyle(block, "white-space:normal; text-transform:full-width");
        var width = MeasureWidth(Block("Ａ")) + 1;
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "Ａ", "Ｂ" }, rows);
        block.SelectAll();
        Assert.Equal("AB", block.SelectedText);
    }

    [Theory]
    [InlineData("A𛄲", "こ")]
    [InlineData("Aｧ", "ｱ")]
    public void SmallKanaUsesTransformedLineBreakClass(string source, string transformed)
    {
        var block = Block(source);
        Css.SetStyle(block, "white-space:normal; text-transform:full-size-kana");
        var width = MeasureWidth(Block("A")) + 1;
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "A", transformed }, rows);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
    }

    [Fact]
    public void UppercaseUsesFullUnicodeMappingsWithoutRewritingSource()
    {
        const string source = "straße ﬃ";
        var block = Block(source);
        Css.SetStyle(block, "text-transform:uppercase");

        Assert.Equal("STRASSE FFI", DrawnText(block));
        Assert.Equal(MeasureWidth(Block("STRASSE FFI")), MeasureWidth(block), 1);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
        Assert.Equal(source, block.Text);
    }

    [Fact]
    public void TrackingIncludesCharactersIntroducedByCaseExpansion()
    {
        var block = Block("ß");
        Css.SetStyle(block, "text-transform:uppercase; letter-spacing:normal; white-space:nowrap");
        var natural = MeasureWidth(block);
        Css.SetStyle(block, "text-transform:uppercase; letter-spacing:12px; white-space:nowrap");
        var draw = Render(block);

        Assert.InRange(MeasureWidth(block) - natural, 11.5, 12.5);
        Assert.Equal(new[] { "S", "S" }, draw.Texts.Select(item => item.Text));
        Assert.InRange(draw.Texts[1].X - draw.Texts[0].X -
            (MeasureWidth(Block("S")) - 2), 11.5, 12.5);
        block.SelectAll();
        Assert.Equal("ß", block.SelectedText);

        var singleCluster = Block("İ");
        Css.SetStyle(singleCluster,
            "text-transform:lowercase; letter-spacing:normal; white-space:nowrap");
        var singleWidth = MeasureWidth(singleCluster);
        Css.SetStyle(singleCluster,
            "text-transform:lowercase; letter-spacing:12px; white-space:nowrap");
        Assert.InRange(MeasureWidth(singleCluster) - singleWidth, -0.5, 0.5);
    }

    [Fact]
    public void ExpandedTrackingMatchesEquivalentPaintAndSourceSelection()
    {
        var expanded = Block("ﬃA");
        expanded.Select(1, 1);
        Css.SetStyle(expanded,
            "text-transform:uppercase; letter-spacing:10px; white-space:nowrap");
        var plain = Block("FFIA");
        Css.SetStyle(plain, "letter-spacing:10px; white-space:nowrap");
        var draw = Render(expanded);

        Assert.Equal(MeasureWidth(plain), MeasureWidth(expanded), 1);
        Assert.Equal("FFIA", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Equal("A", expanded.SelectedText);
        var selected = Assert.Single(draw.Rectangles);
        var last = draw.Texts.Last(item => item.Text == "A");
        Assert.InRange(selected.X, last.X - 1, last.X + 1);
        Assert.Equal(1, expanded.GetPositionFromPoint(
            new Point(last.X + 2, last.Y + 5), true)?.Offset);
    }

    [Fact]
    public void RunTrackingAndNegativeTrackingApplyInsideExpansion()
    {
        var first = new Run("ß") { FontSize = 27 };
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("A"));
        Css.SetStyle(block, "text-transform:uppercase; white-space:nowrap");
        Css.SetStyle(first, "letter-spacing:20px");

        var plainFirst = new Run("SS") { FontSize = 27 };
        var plain = new TextBlock(plainFirst) { FontSize = 20, Foreground = Brushes.Black };
        plain.Inlines.Add(new Run("A"));
        Css.SetStyle(plain, "white-space:nowrap");
        Css.SetStyle(plainFirst, "letter-spacing:20px");
        Assert.Equal(MeasureWidth(plain), MeasureWidth(block), 1);

        Css.SetStyle(first, "letter-spacing:-100px");
        var draw = Render(block);
        Assert.True(MeasureWidth(block) >= 0);
        Assert.All(draw.Texts, item => Assert.True(item.X >= -0.5));
    }

    [Fact]
    public void ExpandedTrackingAffectsWrappingAndWholeSourceEllipsis()
    {
        var expanded = Block("ß A");
        Css.SetStyle(expanded,
            "text-transform:uppercase; letter-spacing:12px; white-space:normal");
        var plain = Block("SS A");
        Css.SetStyle(plain, "letter-spacing:12px; white-space:normal");
        var firstLine = Block("SS");
        Css.SetStyle(firstLine, "letter-spacing:12px; white-space:normal");
        var width = MeasureWidth(firstLine) + 2;
        string[] Rows(TextBlock block) => Render(block, width).Texts
            .GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "SS ", "A" }, Rows(expanded));
        Assert.Equal(Rows(plain), Rows(expanded));

        Css.SetStyle(expanded, "text-transform:uppercase; letter-spacing:12px; " +
            "word-spacing:8px; white-space:normal");
        Css.SetStyle(plain,
            "letter-spacing:12px; word-spacing:8px; white-space:normal");
        Assert.Equal(MeasureWidth(plain), MeasureWidth(expanded), 1);

        var prefix = Block("SS…");
        Css.SetStyle(prefix, "letter-spacing:12px; white-space:nowrap");
        var trimmed = Block("ßßß");
        Css.SetStyle(trimmed, "text-transform:uppercase; letter-spacing:12px; " +
            "white-space:nowrap; text-overflow:ellipsis");
        Assert.Equal("SS…", DrawnTextAtWidth(trimmed, MeasureWidth(prefix) + 1));
        trimmed.SelectAll();
        Assert.Equal("ßßß", trimmed.SelectedText);
    }

    [Fact]
    public void LowercaseUsesFinalSigmaAndExpandsDottedCapitalI()
    {
        var greek = Block("ΟΣ ΟΣΑ");
        Css.SetStyle(greek, "text-transform:lowercase");
        Assert.Equal("ος οσα", DrawnText(greek));

        var dotted = Block("İ");
        Css.SetStyle(dotted, "text-transform:lowercase");
        Assert.Equal("i\u0307", DrawnText(dotted));
        Assert.Equal(MeasureWidth(Block("i\u0307")), MeasureWidth(dotted), 1);
    }

    [Fact]
    public void TurkishCaseRulesFollowContentLanguage()
    {
        var upper = Block("i ı");
        upper.Language = XmlLanguage.GetLanguage("tr");
        Css.SetStyle(upper, "text-transform:uppercase");
        Assert.Equal("İ I", DrawnText(upper));

        var lower = Block("I İ I\u0307");
        lower.Language = XmlLanguage.GetLanguage("tr");
        Css.SetStyle(lower, "text-transform:lowercase");
        Assert.Equal("ı i i", DrawnText(lower));
    }

    [Fact]
    public void LithuanianLowercaseRetainsDotsBeforeAboveAccents()
    {
        const string source = "I\u0301 J\u0328\u0301 \u012E\u0301 ÌÍĨ";
        var block = Block(source);
        block.Language = XmlLanguage.GetLanguage("lt");
        Css.SetStyle(block, "text-transform:lowercase");

        Assert.Equal("i\u0307\u0301 j\u0307\u0328\u0301 \u012F\u0307\u0301 " +
            "i\u0307\u0300i\u0307\u0301i\u0307\u0303", DrawnText(block));
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
    }

    [Fact]
    public void LithuanianUppercaseRemovesContextualDotAbove()
    {
        var block = Block("i\u0307 j\u0328\u0307 i\u0301\u0307");
        block.Language = XmlLanguage.GetLanguage("lt");
        Css.SetStyle(block, "text-transform:uppercase");

        Assert.Equal("I J\u0328 I\u0301\u0307", DrawnText(block));
    }

    [Fact]
    public void LithuanianCapitalizeRemovesDotOnlyAfterTitlecasedBase()
    {
        var block = Block("i\u0307 hi\u0307 j\u0328\u0307");
        block.Language = XmlLanguage.GetLanguage("lt");
        Css.SetStyle(block, "text-transform:capitalize");

        Assert.Equal("I Hi\u0307 J\u0328", DrawnText(block));
    }

    [Fact]
    public void TurkicDotContextAllowsInterveningNonAboveMark()
    {
        var block = Block("I\u0328\u0307 I\u0301\u0307");
        block.Language = XmlLanguage.GetLanguage("tr");
        Css.SetStyle(block, "text-transform:lowercase");

        Assert.Equal("i\u0328 ı\u0301\u0307", DrawnText(block));
    }

    [Fact]
    public void ConditionalCasingContextCrossesRunBoundary()
    {
        var block = new TextBlock(new Run("I\u0328"))
            { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("\u0307"));
        block.Language = XmlLanguage.GetLanguage("tr");
        Css.SetStyle(block, "text-transform:lowercase");

        Assert.Equal("i\u0328", DrawnText(block));
        block.SelectAll();
        Assert.Equal("I\u0328\u0307", block.SelectedText);
    }

    [Fact]
    public void FinalSigmaIgnoresUnicodeCaseIgnorableApostrophe()
    {
        var block = Block("Α’Σ Α’ΣΑ");
        Css.SetStyle(block, "text-transform:lowercase");
        Assert.Equal("α’ς α’σα", DrawnText(block));
    }

    [Fact]
    public void ChangingRunLanguageRebuildsItsCaseMapping()
    {
        var run = new Run("i");
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "text-transform:uppercase");
        Assert.Equal("I", DrawnText(block));

        run.Language = XmlLanguage.GetLanguage("tr");
        Assert.Equal("İ", DrawnText(block));
    }

    [Fact]
    public void ChangingBlockLanguageRebuildsItsCaseMapping()
    {
        var block = Block("i");
        Css.SetStyle(block, "text-transform:uppercase");
        Assert.Equal("I", DrawnText(block));

        block.Language = XmlLanguage.GetLanguage("tr");
        Assert.Equal("İ", DrawnText(block));
    }

    [Fact]
    public void CapitalizeFindsWordsAcrossRunBoundaries()
    {
        var block = new TextBlock(new Run("he")) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("llo wo"));
        block.Inlines.Add(new Run("rld"));
        Css.SetStyle(block, "text-transform:capitalize");

        Assert.Equal("Hello World", DrawnText(block));
        block.SelectAll();
        Assert.Equal("hello world", block.SelectedText);

        var punctuation = Block("don't re-open");
        Css.SetStyle(punctuation, "text-transform:capitalize");
        Assert.Equal("Don't Re-Open", DrawnText(punctuation));
    }

    [Fact]
    public void RunOverrideAndInitialKeepStylingOnSourceRanges()
    {
        var first = new Run("ab");
        var second = new Run("cd");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(block, "text-transform:uppercase");
        Css.SetStyle(second, "text-transform:none");
        Assert.Equal("ABcd", DrawnText(block));

        Css.SetStyle(second, "text-transform:initial");
        Assert.Equal("ABcd", DrawnText(block));
        Css.SetStyle(second, "");
        Assert.Equal("ABCD", DrawnText(block));

        Css.SetStyle(block, "text-transform:none");
        Css.SetStyle(second, "text-transform:uppercase");
        Assert.Equal("abCD", DrawnText(block));
    }

    [Fact]
    public void TransformAndWhitespaceCollapseShareSourceSelectionOffsets()
    {
        var block = Block("a  ß");
        block.Select(3, 1);
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase");
        var draw = Render(block);

        Assert.Equal("A SS", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Equal("ß", block.SelectedText);
        var selected = Assert.Single(draw.Rectangles);
        Assert.InRange(selected.X, 7, 70);
        Assert.Equal(3, block.GetPositionFromPoint(
            new Point(selected.X + 2, selected.Y + 5), true)?.Offset);
    }

    [Fact]
    public void InheritedTransformTracksParentAndExplicitInherit()
    {
        var parent = new StackPanel();
        var block = Block("ab");
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-transform:uppercase");
        Assert.Equal("AB", DrawnText(block));

        Css.SetStyle(block, "text-transform:inherit");
        Css.SetStyle(parent, "text-transform:lowercase");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal("ab", DrawnText(block));
    }

    [Fact]
    public void ExpansionChangesMeasuredWidthAndRestoresAfterStyleRemoval()
    {
        var block = Block("ßß");
        var native = MeasureWidth(block);
        Css.SetStyle(block, "text-transform:uppercase");
        Assert.Equal("SSSS", DrawnText(block));
        Assert.Equal(MeasureWidth(Block("SSSS")), MeasureWidth(block), 1);
        Assert.True(MeasureWidth(block) > native);

        Css.SetStyle(block, "");
        Assert.Equal("ßß", DrawnText(block));
        Assert.Equal(native, MeasureWidth(block), 1);
    }

    [Fact]
    public void EllipsisUsesWholeSourceClustersAfterCaseExpansion()
    {
        var width = MeasureWidth(Block("SS…")) + 1;
        var block = Block("ßßß");
        Css.SetStyle(block,
            "text-transform:uppercase; white-space:nowrap; text-overflow:ellipsis");

        Assert.Equal("SS…", DrawnTextAtWidth(block, width));
        block.SelectAll();
        Assert.Equal("ßßß", block.SelectedText);
    }

    [Fact]
    public void ExpandedPaintWidthAffectsSoftWrapping()
    {
        var width = MeasureWidth(Block("SS ")) + 1;
        var block = Block("ß ß");
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase");
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal("SS SS", string.Concat(rows));
    }

    [Fact]
    public void AnywhereWrapsInsideCaseExpansionWithoutSplittingSourceSelection()
    {
        var block = Block("ß");
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase; line-break:anywhere");
        var width = MeasureWidth(Block("S")) + 1;
        block.SelectAll();
        var draw = Render(block, width);
        var rows = draw.Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "S", "S" }, rows);
        Assert.Equal("ß", block.SelectedText);
        Assert.Equal(2, draw.Rectangles.Count);
        Assert.Equal(0, block.GetPositionFromPoint(
            new Point(draw.Texts[1].X + 1, draw.Texts[1].Y + 5), true)?.Offset);
    }

    [Fact]
    public void AnywhereUsesLastFittingPaintedUnitBesideOtherSourceCharacters()
    {
        var block = Block("AßB");
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase; line-break:anywhere");
        block.Select(1, 1);
        var draw = Render(block, MeasureWidth(Block("AS")) + 1);
        var rows = draw.Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "AS", "SB" }, rows);
        Assert.Equal("ß", block.SelectedText);
        Assert.Equal(2, draw.Rectangles.Count);
        var last = draw.Texts.Last();
        Assert.Equal(2, block.GetPositionFromPoint(
            new Point(last.X + MeasureWidth(Block("S")) + 1, last.Y + 5), true)?.Offset);
    }

    [Fact]
    public void AnywhereKeepsPaintedCombiningClusterTogether()
    {
        var dotted = new Run("İ");
        var expanded = new Run("ß");
        var block = new TextBlock(dotted) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(expanded);
        Css.SetStyle(block, "white-space:normal; line-break:anywhere");
        Css.SetStyle(dotted, "text-transform:lowercase");
        Css.SetStyle(expanded, "text-transform:uppercase");
        var rows = Render(block, MeasureWidth(Block("i\u0307")) + 1).Texts
            .GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "i\u0307", "S", "S" }, rows);
    }

    [Fact]
    public void AnywhereSplitRespectsRunFontAndTrackingWithoutLineEdgeGap()
    {
        var run = new Run("ß") { FontSize = 28, Foreground = Brushes.Red };
        var block = new TextBlock(run) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "white-space:normal; line-break:anywhere");
        Css.SetStyle(run, "text-transform:uppercase; letter-spacing:12px");
        var reference = new TextBlock(new Run("S") { FontSize = 28 })
            { FontSize = 20, Foreground = Brushes.Black };
        block.SelectAll();
        var draw = Render(block, MeasureWidth(reference) + 1);
        var rows = draw.Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "S", "S" }, rows);
        Assert.Equal("ß", block.SelectedText);
        Assert.Equal(2, draw.Rectangles.Count);
        Assert.InRange(Math.Abs(draw.Rectangles[0].Width - draw.Rectangles[1].Width), 0, 1);
    }

    [Theory]
    [InlineData("overflow-wrap:anywhere")]
    [InlineData("overflow-wrap:break-word")]
    [InlineData("word-break:break-all")]
    public void EmergencyAndBreakAllWrappingCanUsePaintedExpansionBoundaries(string policy)
    {
        var block = Block("ß");
        Css.SetStyle(block, $"white-space:normal; text-transform:uppercase; {policy}");
        var rows = Render(block, MeasureWidth(Block("S")) + 1).Texts
            .GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "S", "S" }, rows);
        block.SelectAll();
        Assert.Equal("ß", block.SelectedText);
    }

    [Fact]
    public void EmergencyWrappingStillPrefersAStandardSpaceBreak()
    {
        var block = Block("ß ß");
        Css.SetStyle(block,
            "white-space:normal; text-transform:uppercase; overflow-wrap:anywhere");
        var width = MeasureWidth(Block("SS S")) + 1;
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "SS ", "SS" }, rows);

        Css.SetStyle(block,
            "white-space:normal; text-transform:uppercase; line-break:anywhere");
        rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();
        Assert.Equal(new[] { "SS S", "S" }, rows);
    }

    [Fact]
    public void PaintedExpansionUsesCorrectIntrinsicWidthForEmergencyPolicies()
    {
        var block = Block("ß");
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase; overflow-wrap:anywhere");
        block.Measure(new Size(0, 400));
        var anywhereWidth = block.DesiredSize.Width;

        Css.SetStyle(block, "white-space:normal; text-transform:uppercase; overflow-wrap:break-word");
        block.Measure(new Size(0, 400));
        var breakWordWidth = block.DesiredSize.Width;

        Assert.Equal(MeasureWidth(Block("S")), anywhereWidth, 1);
        Assert.Equal(MeasureWidth(Block("SS")), breakWordWidth, 1);
    }

    [Fact]
    public void ThreeCharacterExpansionCanWrapOnThreeRowsAfterAForcedBreak()
    {
        var block = Block("A\nﬃ");
        Css.SetStyle(block, "white-space:pre-wrap; text-transform:uppercase; line-break:anywhere");
        block.Select(2, 1);
        var draw = Render(block, MeasureWidth(Block("F")) + 1);
        var rows = draw.Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "A", "F", "F", "I" }, rows);
        Assert.Equal("ﬃ", block.SelectedText);
        Assert.Equal(3, draw.Rectangles.Count);
    }

    [Fact]
    public void CollapsedWhitespaceAndSplitExpansionKeepOriginalSourceOffset()
    {
        var block = Block("a  ß");
        Css.SetStyle(block, "white-space:normal; text-transform:uppercase; line-break:anywhere");
        block.Select(3, 1);
        var draw = Render(block, MeasureWidth(Block("S")) + 1);

        Assert.Equal("A SS", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Equal("ß", block.SelectedText);
        Assert.Equal(2, draw.Rectangles.Count);
        Assert.Equal(3, block.GetPositionFromPoint(
            new Point(draw.Texts[^1].X + 1, draw.Texts[^1].Y + 5), true)?.Offset);
    }

    [Fact]
    public void AccessKeyUnderlineFollowsTransformedPaintWithoutChangingItsSource()
    {
        var access = new AccessText
        {
            Text = "a_ßc", FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(access, "text-transform:uppercase");
        var draw = RenderElement(access);
        var underline = Assert.Single(draw.Lines);
        var start = MeasureWidth(Block("A")) - 2;
        var end = MeasureWidth(Block("ASS")) - 2;

        Assert.Equal("ASSC", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.InRange(underline.Start.X, start - 1, start + 1);
        Assert.InRange(underline.End.X, end - 1, end + 1);
        Assert.Equal("a_ßc", access.Text);
        Assert.Equal('ß', access.AccessKey);

        Css.SetStyle(access, "");
        var restored = RenderElement(access);
        Assert.Equal("aßc", string.Concat(restored.Texts.Select(item => item.Text)));
        var restoredEnd = MeasureWidth(Block("aß")) - 2;
        Assert.InRange(Assert.Single(restored.Lines).End.X,
            restoredEnd - 1, restoredEnd + 1);
    }

    [Fact]
    public void AccessKeyUnderlineSpansEachWrappedPartOfAnExpansion()
    {
        var access = new AccessText
        {
            Text = "_ß", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };
        Css.SetStyle(access, "text-transform:uppercase; line-break:anywhere");
        var draw = RenderElement(access, MeasureWidth(Block("S")) + 1);
        var rows = draw.Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "S", "S" }, rows);
        Assert.Equal(2, draw.Lines.Count);
        Assert.All(draw.Lines, line =>
            Assert.InRange(line.End.X - line.Start.X,
                MeasureWidth(Block("S")) - 3, MeasureWidth(Block("S")) - 1));
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

    private static string DrawnText(TextBlock block)
        => string.Concat(Render(block).Texts.Select(item => item.Text));

    private static string DrawnTextAtWidth(TextBlock block, double width)
        => string.Concat(Render(block, width).Texts.Select(item => item.Text));

    private static CaptureContext Render(TextBlock block, double width = 300)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var context = new CaptureContext();
        block.Render(context);
        return context;
    }

    private static CaptureContext RenderElement(FrameworkElement element, double width = 300)
    {
        element.Measure(new Size(width, 400));
        element.Arrange(new Rect(0, 0, width, 400));
        var context = new CaptureContext();
        element.Render(context);
        return context;
    }

    private sealed class CaptureContext : DrawingContextAdapter
    {
        public List<(string Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public List<(Point Start, Point End)> Lines { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText.Text, origin.X, origin.Y));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add((point0, point1));
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
