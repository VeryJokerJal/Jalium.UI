using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssWhiteSpaceProcessingTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("nowrap", true)]
    [InlineData("pre", true)]
    [InlineData("pre-wrap", true)]
    [InlineData("pre-line", true)]
    [InlineData("break-spaces", true)]
    [InlineData("collapse", true)]
    [InlineData("preserve nowrap", true)]
    [InlineData("nowrap preserve", true)]
    [InlineData("discard", true)]
    [InlineData("preserve-spaces", true)]
    [InlineData("discard-inner", true)]
    [InlineData("preserve nowrap discard-before discard-after discard-inner", true)]
    [InlineData("normal pre", false)]
    [InlineData("pre discard-inner", false)]
    [InlineData("collapse preserve", false)]
    [InlineData("wrap nowrap", false)]
    [InlineData("none discard-inner", false)]
    public void SupportsWhiteSpaceKeywords(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("white-space", value));

    [Theory]
    [InlineData("collapse", true)]
    [InlineData("discard", true)]
    [InlineData("preserve", true)]
    [InlineData("preserve-breaks", true)]
    [InlineData("preserve-spaces", true)]
    [InlineData("break-spaces", true)]
    [InlineData("normal", false)]
    [InlineData("preserve nowrap", false)]
    public void SupportsWhiteSpaceCollapseLonghand(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("white-space-collapse", value));

    [Theory]
    [InlineData("wrap", true)]
    [InlineData("nowrap", true)]
    [InlineData("balance", false)]
    [InlineData("wrap nowrap", false)]
    public void SupportsTextWrapModeLonghand(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-wrap-mode", value));

    [Theory]
    [InlineData("auto", true)]
    [InlineData("balance", true)]
    [InlineData("stable", true)]
    [InlineData("pretty", true)]
    [InlineData("avoid-short-last-line", true)]
    [InlineData("nowrap", false)]
    [InlineData("balance pretty", false)]
    public void SupportsTextWrapStyleLonghand(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-wrap-style", value));

    [Theory]
    [InlineData("wrap", true)]
    [InlineData("balance", true)]
    [InlineData("nowrap pretty", true)]
    [InlineData("avoid-short-last-line nowrap", true)]
    [InlineData("balance pretty", false)]
    [InlineData("wrap nowrap", false)]
    public void SupportsTextWrapShorthand(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-wrap", value));

    [Theory]
    [InlineData("balance")]
    [InlineData("pretty")]
    [InlineData("avoid-short-last-line")]
    public void TextWrapStyleImprovesShortLastLineWithoutAddingLines(string style)
    {
        const string source = "Alpha Beta Gamma Delta";
        var block = Block(source);
        var width = MeasureWidth(BlockWithStyle("Alpha Beta Gamma", "white-space:normal")) + 5;
        Css.SetStyle(block, "text-wrap:auto");
        var greedy = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text)).Trim()).ToArray();
        Assert.Equal(2, greedy.Length);

        Css.SetStyle(block, $"text-wrap:{style}");
        var improved = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text)).Trim()).ToArray();
        Assert.Equal(greedy.Length, improved.Length);
        Assert.True(improved[^1].Length > greedy[^1].Length);
        Assert.Equal(source.Replace(" ", "", StringComparison.Ordinal),
            string.Concat(improved).Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public void StableKeepsGreedyBreaksAndNoWrapSuppressesBalancing()
    {
        const string source = "Alpha Beta Gamma Delta";
        var block = Block(source);
        var width = MeasureWidth(BlockWithStyle("Alpha Beta Gamma", "white-space:normal")) + 5;
        Css.SetStyle(block, "text-wrap:wrap auto");
        var greedy = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Css.SetStyle(block, "text-wrap:stable");
        var stable = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();
        Assert.Equal(greedy, stable);

        Css.SetStyle(block, "text-wrap:nowrap balance");
        Assert.Single(Render(block, width).Texts.Select(item => item.Y).Distinct());

        block.TextWrapping = TextWrapping.NoWrap;
        Css.SetStyle(block, "text-wrap:balance");
        Assert.Single(Render(block, width).Texts.Select(item => item.Y).Distinct());
    }

    [Fact]
    public void BalanceTreatsForcedBreakGroupsIndependentlyAndReflowsOnResize()
    {
        const string phrase = "Alpha Beta Gamma Delta";
        var width = MeasureWidth(BlockWithStyle("Alpha Beta Gamma", "white-space:normal")) + 5;
        var block = Block(phrase + "\n" + phrase);
        Css.SetStyle(block, "white-space-collapse:preserve-breaks; text-wrap:balance");
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text)).Trim()).ToArray();

        Assert.Equal(4, rows.Length);
        Assert.Equal(rows[0], rows[2]);
        Assert.Equal(rows[1], rows[3]);

        block.Select(phrase.Length + 1, 5);
        var wideRows = Render(block, MeasureWidth(BlockWithStyle(phrase, "white-space:nowrap")) + 20)
            .Texts.GroupBy(item => item.Y).ToArray();
        Assert.Equal(2, wideRows.Length);
        Assert.Equal("Alpha", block.SelectedText);
    }

    [Fact]
    public void TextWrapShorthandResetsStyleWhilePreservingWhiteSpaceCollapse()
    {
        var block = Block("Alpha  Beta Gamma Delta");
        var width = MeasureWidth(BlockWithStyle("Alpha  Beta Gamma", "white-space:pre")) + 5;
        Css.SetStyle(block, "white-space:pre; text-wrap:balance");
        var balanced = Render(block, width);
        Assert.True(balanced.Texts.Select(item => item.Y).Distinct().Count() > 1);
        Assert.Equal("Alpha  Beta Gamma Delta",
            string.Concat(balanced.Texts.Select(item => item.Text)));

        Css.SetStyle(block, "white-space:pre; text-wrap:balance; text-wrap:nowrap");
        Assert.Equal(CssTextWrapStyle.Auto,
            block.GetValue(CssFlowProperties.TextWrapStyleProperty));
        Assert.Single(Render(block, width).Texts.Select(item => item.Y).Distinct());
    }

    [Fact]
    public void InheritedTextWrapStyleTracksParentAndInitialRestoresGreedyBreaks()
    {
        const string source = "Alpha Beta Gamma Delta";
        var width = MeasureWidth(BlockWithStyle("Alpha Beta Gamma", "white-space:normal")) + 5;
        var parent = new StackPanel();
        var block = Block(source);
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-wrap:balance");
        Css.SetStyle(block, "text-wrap-style:inherit");
        var balanced = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text)).Trim()).ToArray();

        Css.SetStyle(parent, "text-wrap:auto");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        var greedy = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text)).Trim()).ToArray();
        Assert.Equal(2, balanced.Length);
        Assert.Equal(2, greedy.Length);
        Assert.True(balanced[^1].Length > greedy[^1].Length);

        Css.SetStyle(parent, "text-wrap:balance");
        Css.SetStyle(block, "text-wrap:initial");
        Assert.Equal(CssTextWrapStyle.Auto,
            block.GetValue(CssFlowProperties.TextWrapStyleProperty));
        Assert.Equal(CssTextWrapMode.Wrap,
            block.GetValue(CssFlowProperties.TextWrapModeProperty));
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("discard-before", true)]
    [InlineData("discard-after discard-inner", true)]
    [InlineData("discard-inner discard-after discard-before", true)]
    [InlineData("discard-inner discard-inner", false)]
    [InlineData("none discard-after", false)]
    [InlineData("discard", false)]
    public void SupportsWhiteSpaceTrimLonghand(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("white-space-trim", value));

    [Fact]
    public void BlockDiscardInnerTrimsOnlyThroughOuterSegmentBreaks()
    {
        const string source = " \n  alpha\nbeta\n \n";
        var block = Block(source);
        Css.SetStyle(block, "white-space:pre; white-space-trim:discard-inner");
        var rows = Render(block, 400).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "  alpha", "beta" }, rows);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);

        var noBreak = Block("  alpha  ");
        Css.SetStyle(noBreak, "white-space:pre; white-space-trim:discard-inner");
        Assert.Equal("  alpha  ", string.Concat(Render(noBreak, 400).Texts.Select(item => item.Text)));
    }

    [Fact]
    public void InlineTrimFlagsRemoveAdjacentAndOwnWhitespaceWithoutChangingSource()
    {
        var span = new Span(new Run("  body  "));
        var block = new TextBlock(new Run("prefix ")) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(span);
        block.Inlines.Add(new Run(" suffix"));
        Css.SetStyle(block, "white-space:pre");
        Css.SetStyle(span, "white-space-trim:discard-inner");
        Assert.Equal("prefix body suffix",
            string.Concat(Render(block, 400).Texts.Select(item => item.Text)));

        Css.SetStyle(span, "white-space-trim:discard-before discard-after discard-inner");
        Assert.Equal("prefixbodysuffix",
            string.Concat(Render(block, 400).Texts.Select(item => item.Text)));
        block.SelectAll();
        Assert.Equal("prefix   body   suffix", block.SelectedText);
    }

    [Fact]
    public void WhiteSpaceShorthandResetsTrimAndAllInitialRemovesIt()
    {
        var block = Block("\n  alpha\n");
        Css.SetStyle(block, "white-space:pre; white-space-trim:discard-inner");
        Assert.Equal("  alpha", string.Concat(Render(block, 400).Texts.Select(item => item.Text)));
        var trimmedHeight = block.DesiredSize.Height;

        Css.SetStyle(block, "white-space-trim:discard-inner; white-space:pre");
        Assert.Equal("  alpha", string.Concat(Render(block, 400).Texts.Select(item => item.Text)));
        Assert.True(block.DesiredSize.Height > trimmedHeight);

        Css.SetStyle(block, "white-space:pre; white-space-trim:discard-inner; all:initial");
        Assert.Equal("alpha", string.Concat(Render(block, 400).Texts.Select(item => item.Text)));
    }

    [Fact]
    public void ExplicitlyInheritedTrimTracksItsParent()
    {
        var parent = new StackPanel();
        var block = Block("\n  alpha\n");
        parent.Children.Add(block);
        Css.SetStyle(parent, "white-space-trim:discard-inner");
        Css.SetStyle(block, "white-space:pre; white-space-trim:inherit");
        _ = Render(block, 400);
        var trimmedHeight = block.DesiredSize.Height;

        Css.SetStyle(parent, "white-space-trim:none");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        _ = Render(block, 400);
        Assert.True(block.DesiredSize.Height > trimmedHeight);
    }

    [Fact]
    public void AllUnsetInheritsCollapseAndWrapButResetsTrim()
    {
        var parent = new StackPanel();
        var block = Block("\n  alpha\n");
        parent.Children.Add(block);
        Css.SetStyle(parent, "white-space:pre; white-space-trim:discard-inner");
        Css.SetStyle(block, "all:unset");
        _ = Render(block, 400);

        Assert.Equal(CssWhiteSpaceCollapse.Preserve,
            block.GetValue(CssFlowProperties.WhiteSpaceProperty));
        Assert.Equal(CssTextWrapMode.NoWrap,
            block.GetValue(CssFlowProperties.TextWrapModeProperty));
        Assert.Equal(CssWhiteSpaceTrim.None,
            block.GetValue(CssFlowProperties.WhiteSpaceTrimProperty));
    }

    [Fact]
    public void WhiteSpaceLonghandsCascadeIndependentlyOfShorthand()
    {
        var block = Block("a  b c");
        var width = MeasureWidth(BlockWithStyle("a  b", "white-space:pre")) + 1;

        Css.SetStyle(block, "white-space:pre; text-wrap-mode:wrap");
        var preservedRows = Render(block, width).Texts.Select(item => item.Y).Distinct().Count();
        Assert.True(preservedRows > 1);
        Assert.Equal("a  b c", string.Concat(Render(block, width).Texts.Select(item => item.Text)));

        Css.SetStyle(block, "white-space:pre; white-space-collapse:collapse");
        var collapsed = Render(block, width);
        Assert.Single(collapsed.Texts.Select(item => item.Y).Distinct());
        Assert.Equal("a b c", string.Concat(collapsed.Texts.Select(item => item.Text)));

        Css.SetStyle(block, "white-space-collapse:preserve; text-wrap-mode:wrap; white-space:nowrap");
        var reset = Render(block, width);
        Assert.Single(reset.Texts.Select(item => item.Y).Distinct());
        Assert.Equal("a b c", string.Concat(reset.Texts.Select(item => item.Text)));
    }

    [Fact]
    public void WhiteSpaceLonghandInitialAndInheritTrackParentChanges()
    {
        var parent = new StackPanel();
        var block = Block("a  b c");
        parent.Children.Add(block);
        var width = MeasureWidth(BlockWithStyle("a b", "white-space:normal")) + 1;
        Css.SetStyle(parent, "white-space:pre");
        Css.SetStyle(block, "white-space-collapse:initial; text-wrap-mode:inherit");

        var before = Render(block, width);
        Assert.Equal("a b c", string.Concat(before.Texts.Select(item => item.Text)));
        Assert.Single(before.Texts.Select(item => item.Y).Distinct());

        Css.SetStyle(parent, "white-space:pre-wrap");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.True(Render(block, width).Texts.Select(item => item.Y).Distinct().Count() > 1);
    }

    [Fact]
    public void AllInitialResetsBothWhiteSpaceLonghands()
    {
        var parent = new StackPanel();
        var block = Block("a  b c");
        parent.Children.Add(block);
        Css.SetStyle(parent, "white-space:pre");
        Css.SetStyle(block, "all:initial");

        var width = MeasureWidth(BlockWithStyle("a b", "white-space:normal")) + 1;
        var rows = Render(block, width).Texts;
        Assert.Equal("a b c", string.Concat(rows.Select(item => item.Text)));
        Assert.True(rows.Select(item => item.Y).Distinct().Count() > 1);
    }

    [Fact]
    public void TextWrapModeMirrorsNativeTextWrappingWithoutOverridingLocalValue()
    {
        var box = new TextBox { Text = "a b c" };
        Css.SetStyle(box, "text-wrap-mode:nowrap");
        Assert.Equal(TextWrapping.NoWrap, box.TextWrapping);

        Css.SetStyle(box, "text-wrap-mode:wrap");
        Assert.Equal(TextWrapping.Wrap, box.TextWrapping);

        box.TextWrapping = TextWrapping.NoWrap;
        Css.SetStyle(box, "text-wrap-mode:wrap");
        Assert.Equal(TextWrapping.NoWrap, box.TextWrapping);
    }

    [Fact]
    public void RemovingWrapOnlyStyleRestoresNativeWhitespaceProcessing()
    {
        var block = Block("a   b");
        Css.SetStyle(block, "text-wrap-mode:wrap");
        Assert.Equal("a b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));

        Css.SetStyle(block, "");
        Assert.Equal("a   b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));
    }

    [Fact]
    public void PreserveSpacesAndDiscardKeepSourceOffsetsAndExplicitLineBreaks()
    {
        const string source = "a \t\r\n  b";
        var block = Block(source);
        Css.SetStyle(block, "white-space-collapse:preserve-spaces");
        Assert.Equal("a     b", string.Concat(Render(block, 400).Texts.Select(item => item.Text)));

        Css.SetStyle(block, "white-space-collapse:discard");
        Assert.Equal("ab", string.Concat(Render(block, 400).Texts.Select(item => item.Text)));
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);

        var inlineBlock = new TextBlock(new Run("a ")) { FontSize = 20, Foreground = Brushes.Black };
        inlineBlock.Inlines.Add(new LineBreak());
        inlineBlock.Inlines.Add(new Run(" b"));
        Css.SetStyle(inlineBlock, "white-space-collapse:discard");
        Assert.Equal(new[] { "a", "b" }, Render(inlineBlock, 400).Texts
            .GroupBy(item => item.Y).Select(row => string.Concat(row.Select(item => item.Text))));
    }

    [Fact]
    public void NormalCollapsesSpacesTabsAndNewlinesWithoutChangingSourceText()
    {
        const string source = "a  \t\n b";
        var block = Block(source);
        Css.SetStyle(block, "white-space:normal");
        var draw = Render(block, 200);

        Assert.Equal("a b", Assert.Single(draw.Texts).Text);
        Assert.Equal(MeasureWidth(BlockWithStyle("a b", "white-space:normal")),
            MeasureWidth(block), 1);
        block.SelectAll();
        Assert.Equal(source, block.SelectedText);
        Assert.Equal(source, block.Text);
    }

    [Fact]
    public void NormalRemovesLeadingAndTrailingCollapsibleSpace()
    {
        var block = Block("  a \t ");
        Css.SetStyle(block, "white-space:normal");
        var draw = Render(block, 200);

        Assert.Equal("a", Assert.Single(draw.Texts).Text);
        Assert.Equal(MeasureWidth(BlockWithStyle("a", "white-space:normal")),
            MeasureWidth(block), 1);
    }

    [Fact]
    public void NoWrapCollapsesSpaceWithoutAllowingSoftWraps()
    {
        var block = Block("a  b c");
        Css.SetStyle(block, "white-space:nowrap");
        var draw = Render(block, 30);

        Assert.Single(draw.Texts.Select(item => item.Y).Distinct());
        Assert.Equal("a b c", string.Concat(draw.Texts.Select(item => item.Text)));
    }

    [Fact]
    public void PreLinePreservesForcedBreaksAndRemovesAdjacentSpace()
    {
        var block = Block("a \n  b");
        Css.SetStyle(block, "white-space:pre-line");
        var rows = Render(block, 200).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "a", "b" }, rows);
    }

    [Fact]
    public void PrePreservesSpacesAndLineBreaks()
    {
        var block = Block("a  b\nc");
        Css.SetStyle(block, "white-space:pre");
        var rows = Render(block, 200).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "a  b", "c" }, rows);
        Assert.True(MeasureWidth(BlockWithStyle("a  b", "white-space:pre")) >
                    MeasureWidth(BlockWithStyle("a  b", "white-space:normal")));
    }

    [Fact]
    public void PreWrapPreservesSpacesWhileAllowingSoftWraps()
    {
        var width = MeasureWidth(BlockWithStyle("a ", "white-space:pre-wrap")) + 1;
        var block = Block("a  b");
        Css.SetStyle(block, "white-space:pre-wrap");
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "a  ", "b" }, rows);
    }

    [Fact]
    public void BreakSpacesWrapsAfterEachPreservedSpace()
    {
        var width = MeasureWidth(BlockWithStyle("A ", "white-space:break-spaces")) + 1;
        var block = Block("A   B");
        Css.SetStyle(block, "white-space:break-spaces");
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.True(rows.Length >= 2);
        Assert.Equal("A ", rows[0]);
        Assert.Equal("A   B", string.Concat(rows));
    }

    [Fact]
    public void CollapsedWordSeparatorsReceiveOneWordSpacingAdjustment()
    {
        var block = Block("a   b");
        Css.SetStyle(block, "white-space:normal; word-spacing:normal");
        var normal = MeasureWidth(block);
        Css.SetStyle(block, "white-space:normal; word-spacing:10px");

        Assert.InRange(MeasureWidth(block) - normal, 9.5, 10.5);
    }

    [Fact]
    public void SelectionAndHitTestingKeepSourceOffsets()
    {
        var block = Block("a   b");
        block.Select(4, 1);
        Css.SetStyle(block, "white-space:normal");
        var draw = Render(block, 200);
        var selected = Assert.Single(draw.Rectangles);

        Assert.Equal("b", block.SelectedText);
        Assert.InRange(selected.X, 5, 40);
        Assert.Equal(4, block.GetPositionFromPoint(
            new Point(selected.X + 2, selected.Y + 5), true)?.Offset);
    }

    [Fact]
    public void ParentStyleChangesRebuildTheChildAndRemovalRestoresNativeText()
    {
        var parent = new StackPanel();
        var block = Block("a   b");
        parent.Children.Add(block);
        var native = MeasureWidth(block);
        Assert.Equal("a   b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));

        Css.SetStyle(parent, "white-space:normal");
        var collapsed = MeasureWidth(block);
        Assert.True(collapsed < native);
        Css.SetStyle(parent, "white-space:pre");
        Assert.InRange(MeasureWidth(block) - native, -0.5, 0.5);
        Css.SetStyle(parent, "");
        Assert.InRange(MeasureWidth(block) - native, -0.5, 0.5);
    }

    [Fact]
    public void RemovingNormalStyleRestoresNativeSpacesEvenWhenComputedValueIsUnchanged()
    {
        var parent = new StackPanel();
        var block = Block("a   b");
        parent.Children.Add(block);
        var native = MeasureWidth(block);
        _ = Render(block, 200);

        Css.SetStyle(parent, "white-space:normal");
        Assert.True(MeasureWidth(block) < native);
        Css.SetStyle(parent, "");

        Assert.InRange(MeasureWidth(block) - native, -0.5, 0.5);
        Assert.Equal("a   b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));
    }

    [Fact]
    public void InitialOnChildOverridesInheritedPreWithNormalCollapsing()
    {
        var parent = new StackPanel();
        var block = Block("a   b");
        parent.Children.Add(block);
        Css.SetStyle(parent, "white-space:pre");
        Css.SetStyle(block, "white-space:initial");

        Assert.Equal("a b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));
    }

    [Fact]
    public void InheritCopiesParentsWhiteSpaceModeRatherThanOnlyItsWrapping()
    {
        var parent = new StackPanel();
        var block = Block("a   b");
        parent.Children.Add(block);
        Css.SetStyle(parent, "white-space:pre");
        Css.SetStyle(block, "white-space:inherit");

        Assert.Equal("a   b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));
        Css.SetStyle(parent, "white-space:normal");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal("a b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));
    }

    [Fact]
    public void FullyCollapsedTextDoesNotMeasureOrPaintPlaceholderGlyphs()
    {
        var block = Block(" \t\n  ");
        Css.SetStyle(block, "white-space:normal");

        Assert.InRange(MeasureWidth(block) - MeasureWidth(BlockWithStyle(" ", "white-space:normal")),
            -0.5, 0.5);
        Assert.All(Render(block, 200).Texts, item => Assert.Empty(item.Text));
        block.SelectAll();
        Assert.Equal(" \t\n  ", block.SelectedText);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("nowrap")]
    public void ExplicitInlineLineBreakRemainsForcedAfterWhitespaceCollapse(string mode)
    {
        var block = new TextBlock(new Run("a ")) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new LineBreak());
        block.Inlines.Add(new Run(" b"));
        Css.SetStyle(block, $"white-space:{mode}");
        var rows = Render(block, 200).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(new[] { "a", "b" }, rows);
        Assert.Equal("a \n b", block.Text);
    }

    [Fact]
    public void CollapsingSpansAdjacentRunsWithoutChangingTheirSourceText()
    {
        var block = new TextBlock(new Run("a ")) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("  b"));
        Css.SetStyle(block, "white-space:normal");

        Assert.Equal("a b", string.Concat(Render(block, 200).Texts.Select(item => item.Text)));
        block.SelectAll();
        Assert.Equal("a   b", block.SelectedText);
    }

    [Fact]
    public void WrappedCollapsedSpacesDoNotMakeAnEmptyVisualLine()
    {
        var width = MeasureWidth(BlockWithStyle("a ", "white-space:normal")) + 1;
        var block = Block("a   b");
        Css.SetStyle(block, "white-space:normal");
        var rows = Render(block, width).Texts.GroupBy(item => item.Y)
            .Select(row => string.Concat(row.Select(item => item.Text))).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal("a b", string.Concat(rows));
    }

    [Fact]
    public void NativeLocalNoWrapStillWinsWhileCssCollapsesSpaces()
    {
        var block = Block("a   b c");
        block.TextWrapping = TextWrapping.NoWrap;
        Css.SetStyle(block, "white-space:normal");
        var draw = Render(block, 30);

        Assert.Single(draw.Texts.Select(item => item.Y).Distinct());
        Assert.Equal("a b c", string.Concat(draw.Texts.Select(item => item.Text)));
    }

    private static TextBlock BlockWithStyle(string text, string style)
    {
        var block = Block(text);
        Css.SetStyle(block, style);
        return block;
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
