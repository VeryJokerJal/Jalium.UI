using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInitialLetterTests
{
    [Theory]
    [InlineData("normal", 0, 0)]
    [InlineData("2", 2, 2)]
    [InlineData("2.51", 2.51, 2)]
    [InlineData("2.51 3", 2.51, 3)]
    [InlineData("3 raise", 3, 1)]
    [InlineData("RAISE 3", 3, 1)]
    [InlineData("3.5 drop", 3.5, 3)]
    [InlineData("drop 3.5", 3.5, 3)]
    [InlineData("calc(1 + 2)", 3, 3)]
    [InlineData("calc(.25)", 1, 1)]
    [InlineData("2 calc(1 + 1.5)", 2, 3)]
    [InlineData("2 calc(-1)", 2, 1)]
    [InlineData("2 100000000000", 2, 100000000000)]
    public void SizeAndSinkKeepTheirComputedValues(string value, double size, double sink)
    {
        Assert.True(CssAuthoring.IsValueSupported("initial-letter", value));
        var text = new TextBlock(); Style(text, "initial-letter:" + value);
        Assert.Equal(new CssInitialLetter(size, sink), text.GetValue(CssInitialLetterProperties.ValueProperty));
    }

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData(".5")] [InlineData("-1")] [InlineData("2 0")]
    [InlineData("2 .5")] [InlineData("2 1.0")] [InlineData("2 1e1")] [InlineData("raise")]
    [InlineData("normal 2")] [InlineData("raise 2 1")] [InlineData("drop raise 2")] [InlineData("2 raise 3")]
    [InlineData("2%")] [InlineData("2px")] [InlineData("calc(2px)")] [InlineData("2 calc(0px)")]
    [InlineData("1e999")] [InlineData("2,3")] [InlineData("2 3 drop")]
    public void InvalidSizesDoNotReplaceAnEarlierDeclaration(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("initial-letter", value));
        var text = new TextBlock(); Style(text, "initial-letter:3 2;initial-letter:" + value);
        Assert.Equal(new CssInitialLetter(3, 2), text.GetValue(CssInitialLetterProperties.ValueProperty));
    }

    [Theory]
    [InlineData("alphabetic", 0, false)]
    [InlineData("ideographic", 1, false)]
    [InlineData("hanging", 2, false)]
    [InlineData("leading", 3, false)]
    [InlineData("border-box", 0, true)]
    [InlineData("border-box alphabetic", 0, true)]
    [InlineData("BORDER-BOX IDEOGRAPHIC", 1, true)]
    [InlineData("border-box hanging", 2, true)]
    [InlineData("border-box leading", 3, true)]
    public void AlignmentPreservesItsTwoIndependentChoices(string value, int alignment, bool border)
    {
        Assert.True(CssAuthoring.IsValueSupported("initial-letter-align", value));
        var text = new TextBlock(); Style(text, "initial-letter-align:" + value);
        Assert.Equal(new CssInitialLetterAlign((CssInitialLetterAlignment)alignment, border), text.GetValue(CssInitialLetterProperties.AlignProperty));
    }

    [Theory]
    [InlineData("")] [InlineData("auto")] [InlineData("normal")] [InlineData("alphabetic border-box")]
    [InlineData("border-box border-box")] [InlineData("ideographic hanging")] [InlineData("border-box alphabetic leading")]
    public void InvalidAlignmentKeepsThePreviousChoice(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("initial-letter-align", value));
        var text = new TextBlock(); Style(text, "initial-letter-align:leading;initial-letter-align:" + value);
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Leading), text.GetValue(CssInitialLetterProperties.AlignProperty));
    }

    [Theory]
    [InlineData("none", 0, 0)]
    [InlineData("first", 1, 0)]
    [InlineData("all", 2, 0)]
    [InlineData("grid", 3, 0)]
    [InlineData("-40%", 4, -40)]
    [InlineData("2em", 4, 40)]
    [InlineData("calc(10% - 2px)", 4, 8)]
    public void WrapUsesKeywordsOrAnUnclampedLengthPercentage(string value, int kind, double offset)
    {
        Assert.True(CssAuthoring.IsValueSupported("initial-letter-wrap", value));
        var text = new TextBlock { FontSize = 20 }; Style(text, "initial-letter-wrap:" + value);
        var wrap = Assert.IsType<CssInitialLetterWrap>(text.GetValue(CssInitialLetterProperties.WrapProperty));
        Assert.Equal((CssInitialLetterWrapKind)kind, wrap.Kind);
        if ((CssInitialLetterWrapKind)kind == CssInitialLetterWrapKind.Offset) Assert.Equal(offset, wrap.Offset.Resolve(100, 0), 6);
    }

    [Fact]
    public void InheritanceGlobalKeywordsAndAllResetTheCorrectLonghands()
    {
        var child = new TextBlock(); var root = new Border { Child = child };
        Style(root, "initial-letter:3;initial-letter-align:ideographic;initial-letter-wrap:grid");
        Assert.Equal(default(CssInitialLetter), child.GetValue(CssInitialLetterProperties.ValueProperty));
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Ideographic), child.GetValue(CssInitialLetterProperties.AlignProperty));
        Assert.Equal(new CssInitialLetterWrap(CssInitialLetterWrapKind.Grid), child.GetValue(CssInitialLetterProperties.WrapProperty));
        Style(child, "initial-letter:inherit;initial-letter-align:initial;initial-letter-wrap:initial");
        Assert.Equal(new CssInitialLetter(3, 3), child.GetValue(CssInitialLetterProperties.ValueProperty));
        Assert.Equal(default(CssInitialLetterAlign), child.GetValue(CssInitialLetterProperties.AlignProperty));
        Assert.Equal(default(CssInitialLetterWrap), child.GetValue(CssInitialLetterProperties.WrapProperty));
        Style(child, "all:unset");
        Assert.Equal(default(CssInitialLetter), child.GetValue(CssInitialLetterProperties.ValueProperty));
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Ideographic), child.GetValue(CssInitialLetterProperties.AlignProperty));
        Style(child, "all:initial");
        Assert.Equal(default(CssInitialLetterAlign), child.GetValue(CssInitialLetterProperties.AlignProperty));
    }

    [Theory]
    [InlineData("initial-letter:3", "initial-letter:3 3")]
    [InlineData("initial-letter-align:border-box", "initial-letter-align:border-box alphabetic")]
    [InlineData("initial-letter-wrap:2em", "initial-letter-wrap:40px")]
    [InlineData("initial-letter-wrap:calc(5% + 5%)", "initial-letter-wrap:10%")]
    public void StyleQueriesCompareComputedValues(string declarations, string query)
    {
        var child = new TextBlock(); var root = new Border { Child = child };
        Css.SetClass(child, "child"); Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@container style(" + query + "){.child {opacity:.4}}"));
        Style(root, "font-size:20px;" + declarations); Assert.Equal(.4, child.Opacity, 6);
    }

    [Fact]
    public void SizeAndIntegerSinkInterpolateWhileNormalAndAlignmentRemainDiscrete()
    {
        var text = new TextBlock(); Style(text, "transition:initial-letter 1s linear;initial-letter:2");
        var transition = ((CssTransitionData)text.GetValue(CssTransitions.DataProperty)!).Find(text, CssInitialLetterProperties.ValueProperty)!;
        var timeline = Assert.IsType<CssInitialLetterAnimation>(CssTransitions.Create(text, CssInitialLetterProperties.ValueProperty,
            new CssInitialLetter(2, 1), new CssInitialLetter(4, 4), transition));
        var clock = timeline.CreateClock();
        foreach (var (progress, size, sink) in new[] { (0d, 2d, 1d), (.5, 3d, 3d), (1d, 4d, 4d) })
        { clock.SetProgressForSampling(progress); Assert.Equal(new CssInitialLetter(size, sink), timeline.GetCurrentValue(null!, null!, clock)); }
        Assert.Null(CssTransitions.Create(text, CssInitialLetterProperties.ValueProperty, default(CssInitialLetter), new CssInitialLetter(3, 3), transition));
        Assert.NotNull(CssTransitions.Create(text, CssInitialLetterProperties.ValueProperty, default(CssInitialLetter), new CssInitialLetter(3, 3), transition with { AllowDiscrete = true }));
    }

    [Theory]
    [InlineData("3", 3, 3)]
    [InlineData("3 1", 3, 1)]
    [InlineData("3 2", 3, 2)]
    [InlineData("2.5 3", 2.5, 3)]
    public void InitialLetterUsesBodyMetricsAndItsSinkWithoutChangingComputedFonts(string value, double size, double sink)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron.";
        var text = new TextBlock { IsTextSelectionEnabled = true }; var original = new Run(source); text.Inlines.Add(original);
        Css.SetClass(text, "body");
        Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(".body {display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
            ".body::first-letter {initial-letter:" + value + ";font-size:200px;color:green;}"));
        Layout(text, 300, 240);
        var capture = new TextCapture(); text.Render(capture);
        var letter = Assert.Single(capture.Paints.Where(p => p.Text == "A" && p.Color == Colors.Green));
        var body = capture.Paints.First(p => p.Text.Contains("lpha"));
        var units = TextMeasurement.GetFontUnitMetrics("Georgia", 16);
        var expected = ((size - 1) * 24 + units.CapHeight) / units.CapHeight * 16;
        Assert.Equal(expected, letter.Size, 3);
        Assert.Equal((Math.Min(size, sink) - 1) * 24, letter.Baseline - body.Baseline, 3);
        Assert.Equal(16, original.FontSize); Assert.Equal(16, text.FontSize);
        Assert.Same(original, Assert.Single(text.Inlines)); Assert.Equal(source, original.Text);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
        Assert.NotNull(text.DocumentSharedLine(0, 1)); Assert.Same(text.DocumentSharedLine(0, 1), text.DocumentSharedLine(1, 5));
        var headLine = Assert.Single(text.DocumentTextLineBounds(0, 1));
        var tailLine = text.DocumentTextLineBounds(1, 4)[0];
        Assert.Equal(tailLine.Top, headLine.Top, 3); Assert.Equal(24, headLine.Height, 3);
        Assert.True(Assert.Single(text.DocumentTextBounds(0, 1)).Height > headLine.Height);
        Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
    }

    [Fact]
    public void OffsetWrapInterpolatesMixedPercentagesAndLengths()
    {
        var text = new TextBlock(); Style(text, "transition:initial-letter-wrap 1s linear;initial-letter-wrap:0px");
        var transition = ((CssTransitionData)text.GetValue(CssTransitions.DataProperty)!).Find(text, CssInitialLetterProperties.WrapProperty)!;
        var timeline = Assert.IsType<CssInitialLetterWrapAnimation>(CssTransitions.Create(text, CssInitialLetterProperties.WrapProperty,
            new CssInitialLetterWrap(CssInitialLetterWrapKind.Offset, CssLayoutLength.Percent(-.4)),
            new CssInitialLetterWrap(CssInitialLetterWrapKind.Offset, CssLayoutLength.Px(10)), transition));
        var clock = timeline.CreateClock(); clock.SetProgressForSampling(.5);
        var value = Assert.IsType<CssInitialLetterWrap>(timeline.GetCurrentValue(null!, null!, clock));
        Assert.Equal(-15, value.Offset.Resolve(100, 0), 6); Assert.Equal(-35, value.Offset.Resolve(200, 0), 6);
    }

    [Theory]
    [InlineData("zh-CN", 1)] [InlineData("ja-JP", 1)] [InlineData("ko", 1)]
    [InlineData("hi", 2)] [InlineData("mr", 2)] [InlineData("zh-Latn", 0)] [InlineData("hi-Cyrl", 0)] [InlineData("hi-Hani", 1)]
    public void LanguageDefaultsParticipateInTheCascadeAndRefresh(string language, int alignment)
    {
        var text = new TextBlock { Language = Markup.XmlLanguage.GetLanguage(language) };
        Style(text, "initial-letter:3");
        Assert.Equal(new CssInitialLetterAlign((CssInitialLetterAlignment)alignment), text.GetValue(CssInitialLetterProperties.AlignProperty));
        Style(text, "initial-letter-align:leading");
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Leading), text.GetValue(CssInitialLetterProperties.AlignProperty));
        Style(text, "initial-letter:3"); text.Language = Markup.XmlLanguage.GetLanguage("en-US");
        CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        Assert.Equal(default(CssInitialLetterAlign), text.GetValue(CssInitialLetterProperties.AlignProperty));
    }

    [Fact]
    public void AnInheritedLanguageDoesNotOverrideAnAuthoredAlignment()
    {
        var text = new TextBlock(); var root = new Border { Child = text, Language = Markup.XmlLanguage.GetLanguage("zh-CN") };
        Style(root, "initial-letter-align:leading"); Style(text, "initial-letter:3");
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Leading), text.GetValue(CssInitialLetterProperties.AlignProperty));
        text.Language = Markup.XmlLanguage.GetLanguage("hi"); CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Hanging), text.GetValue(CssInitialLetterProperties.AlignProperty));
        text.ClearValue(FrameworkElement.LanguageProperty); CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        Assert.Equal(new CssInitialLetterAlign(CssInitialLetterAlignment.Leading), text.GetValue(CssInitialLetterProperties.AlignProperty));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InsetsChangeTheExclusionButOnlyBorderBoxAlignmentChangesTheUsedFont(bool borderBox)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { Text = "Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu." };
        Css.SetClass(text, "body"); Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(
            ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
            ".body::first-letter{initial-letter:3;initial-letter-align:" + (borderBox ? "border-box " : "") +
            "alphabetic;padding:3px;border:2px solid green;margin-right:7px;color:green;}"));
        Layout(text, 300, 240); var capture = new TextCapture(); text.Render(capture);
        var letter = Assert.Single(capture.Paints, p => p.Text == "A"); var body = capture.Paints.First(p => p.Text.Contains("lpha"));
        var cap = TextMeasurement.GetFontUnitMetrics("Georgia", 16).CapHeight;
        Assert.Equal((48 + cap - (borderBox ? 10 : 0)) / cap * 16, letter.Size, 3);
        Assert.Equal(48 - (borderBox ? 5 : 0), letter.Baseline - body.Baseline, 3);
        Assert.True(TextMeasurement.TryGetFontInkBounds(new FormattedText("A", "Georgia", letter.Size), out var ink));
        Assert.Equal(ink.Width + 17, body.X, 3);
    }

    [Fact]
    public void WrapModesChangeTheImpactedLinesAndPreserveTheFollowingWordSpace()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var none = Example("none"); var first = Example("first"); var all = Example("all");
        var offset = Example("-40%"); var positive = Example("12px"); var grid = Example("grid");
        Assert.True(first[0].X < none[0].X - 1);
        Assert.Equal(none[1].X, first[1].X, 3);
        Assert.Equal(first[0].X, all[0].X, 3);
        Assert.True(all[1].X < none[1].X - 1);
        Assert.Equal(none[0].X * .6, offset[0].X, 3);
        Assert.Equal(none[0].X + 12, positive[0].X, 3);
        Assert.Equal(none[1].X, offset[1].X, 3);
        Assert.True(grid[0].X >= none[0].X);
        Assert.Equal(grid[0].X, grid[1].X, 3);
        Assert.Equal(Example("none", true)[0].X, Example("first", true)[0].X, 3);
        List<Paint> Example(string wrap, bool space = false)
        {
            var text = new TextBlock { Text = "A" + (space ? " " : "") + string.Join(' ', Enumerable.Repeat("lpha beta gamma delta epsilon zeta", 8)) };
            Css.SetClass(text, "body"); Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(
                ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
                ".body::first-letter{initial-letter:3;initial-letter-wrap:" + wrap + ";color:green;}"));
            Layout(text, 300, 500); var capture = new TextCapture(); text.Render(capture);
            var paints = capture.Paints.Where(p => p.Color == Colors.Black && p.Text.Length > 0).ToList();
            if (space) Assert.StartsWith(" ", paints[0].Text);
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid); return paints;
        }
    }

    [Fact]
    public void RemovingAndRestoringThePseudoStyleRetiresItsPrivateUsedFont()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var original = new Run("Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu.");
        var text = new TextBlock { IsTextSelectionEnabled = true }; text.Inlines.Add(original); Css.SetClass(text, "body");
        var sheet = CssStyleSheet.Parse(".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
            ".body.drop::first-letter{initial-letter:3;color:green;}"); Css.GetStyleSheets(text).Add(sheet);
        foreach (var enabled in new[] { true, false, true, false })
        {
            Css.SetClass(text, enabled ? "body drop" : "body"); Layout(text, 280, 240);
            var capture = new TextCapture(); text.Render(capture);
            Assert.Equal(enabled, capture.Paints.Any(p => p.Color == Colors.Green && p.Size > 16));
            if (!enabled) Assert.All(capture.Paints, paint => Assert.Equal(16, paint.Size));
            text.SelectAll(); Assert.Equal(original.Text, text.SelectedText); Assert.Same(original, Assert.Single(text.Inlines));
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
        }
    }

    [Fact]
    public void AnAuthoredInlineIsEligibleOnlyWhileItStartsTheContainingLine()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "A" }; var tail = new TextBlock { Text = "lpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetClass(root, "body"); Css.SetClass(head, "initial"); Css.SetClass(tail, "tail");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;}" +
            ".initial{display:inline;initial-letter:3;color:green;}.tail{display:inline;color:black;}"));
        Layout(root, 300, 240); var capture = new TextCapture(); root.Render(capture);
        Assert.True(Assert.Single(capture.Paints, p => p.Color == Colors.Green).Size > 16); Assert.Equal(16, head.FontSize);
        var before = new TextBlock { Text = "Before ", Foreground = new SolidColorBrush(Colors.Black) }; Style(before, "display:inline");
        root.Children.Insert(0, before); Layout(root, 300, 240); capture = new(); root.Render(capture);
        Assert.Equal(16, Assert.Single(capture.Paints, p => p.Color == Colors.Green).Size);
        root.Children.Remove(before); Layout(root, 300, 240); capture = new(); root.Render(capture);
        Assert.True(Assert.Single(capture.Paints, p => p.Color == Colors.Green).Size > 16);
        Assert.Equal("A", head.Text); Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Fact]
    public void LargerInlineContentDoesNotChangeTheInitialsHypotheticalBodyMetrics()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var regular = Example(16); var larger = Example(32);
        Assert.Equal(regular.Size, larger.Size, 3); Assert.Equal(regular.Baseline, larger.Baseline, 3);
        Paint Example(double tailSize)
        {
            var text = new TextBlock(); text.Inlines.Add(new Run("A")); text.Inlines.Add(new Run("lpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu.") { FontSize = tailSize });
            Css.SetClass(text, "body"); Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(
                ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
                ".body::first-letter{initial-letter:3;color:green;}"));
            Layout(text, 300, 300); var capture = new TextCapture(); text.Render(capture);
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
            return Assert.Single(capture.Paints, p => p.Color == Colors.Green && p.Text == "A");
        }
    }

    [Fact]
    public void AnAtomicInlineKeepsItsFontAndReceivesTheInitialContentHeight()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "A" }; var tail = new TextBlock { Text = "lpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetClass(root, "body"); Css.SetClass(head, "initial"); Css.SetClass(tail, "tail");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;}" +
            ".initial{display:inline-block;initial-letter:3;color:green;}.tail{display:inline;color:black;}"));
        Layout(root, 300, 240); var capture = new TextCapture(); root.Render(capture);
        Assert.Equal(16, Assert.Single(capture.Paints, p => p.Color == Colors.Green).Size);
        Assert.Equal(48 + TextMeasurement.GetFontUnitMetrics("Georgia", 16).CapHeight, head.RenderSize.Height, 3);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("A")] [InlineData("j")] [InlineData("T")]
    public void KernedInitialSelectionDoesNotExtendIntoTheFollowingText(string head)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { Text = head + "lpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu.", IsTextSelectionEnabled = true };
        Css.SetClass(text, "body"); Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(
            ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;} .body::first-letter{initial-letter:3;}"));
        Layout(text, 300, 240);
        var initial = Assert.Single(text.DocumentTextBounds(0, 1)); var tail = text.DocumentTextBounds(1, 1)[0];
        Assert.True(initial.Right <= tail.Left + .001, $"Initial selection {initial} overlaps body {tail}");
        text.Select(0, 1); Assert.Equal(head, text.SelectedText);
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        for (var pass = 0; pass < 6; pass++)
        { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout(); }
    }

    [Theory]
    [InlineData("3", "none", 48)]
    [InlineData("3 raise", "none", 0)]
    [InlineData("3 2", "none", 24)]
    [InlineData("2.5 3", "none", 36)]
    [InlineData("2 4", "none", 24)]
    [InlineData("3", "first", 48)]
    [InlineData("3", "all", 48)]
    [InlineData("3", "grid", 48)]
    [InlineData("3", "10%", 48)]
    public void LongInitialWrapsAndTheBodyStartsBesideItsLastLargeLine(string value, string wrap, double baselineDelta)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "Alpha beta gamma delta eta", IsTextSelectionEnabled = true };
        var body = new TextBlock { Text = " a continuation with ordinary text across the remaining lines." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(body);
        Css.SetClass(root, "body"); Css.SetClass(head, "initial"); Css.SetClass(body, "tail");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;}" +
            ".initial{display:inline;initial-letter:" + value + ";initial-letter-wrap:" + wrap + ";color:green;} .tail{display:inline;color:black;}"));
        Layout(root, 300, 900); var capture = new TextCapture(); root.Render(capture);
        var large = capture.Paints.Where(p => p.Color == Colors.Green).ToArray();
        Assert.True(large.Length > 1, "The long initial must wrap at its containing block.");
        Assert.True(large[^1].Baseline > large[0].Baseline);
        var ordinary = capture.Paints.First(p => p.Color == Colors.Black);
        Assert.Equal(baselineDelta, large[^1].Baseline - ordinary.Baseline, 3);
        Assert.Equal(head.Text.Replace(" ", ""), string.Concat(large.Select(p => p.Text)).Replace(" ", ""));
        head.SelectAll(); Assert.Equal(head.Text, head.SelectedText); Assert.Equal(16, head.FontSize);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Fact]
    public void ALongInitialReflowsAfterResizeAndRestoresItsAuthoredFontWhenDisabled()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "Alpha beta gamma delta eta", IsTextSelectionEnabled = true };
        var tail = new TextBlock { Text = " continues with ordinary text across the remaining lines." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;color:green;"); Css.SetStyle(tail, "display:inline;color:black;");
        foreach (var width in new[] { 300, 1600, 300 })
        {
            Layout(root, width, 900); var capture = new TextCapture(); root.Render(capture);
            var lines = capture.Paints.Where(p => p.Color == Colors.Green).ToArray();
            Assert.Equal(width == 1600, lines.Length == 1);
            Assert.Equal(48, lines[^1].Baseline - capture.Paints.First(p => p.Color == Colors.Black).Baseline, 3);
            Assert.All(lines, line => Assert.True(line.Size > 60));
            Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
        }
        Css.SetStyle(head, "display:inline;initial-letter:normal;color:green;"); Layout(root, 300, 300);
        var ordinary = new TextCapture(); root.Render(ordinary);
        Assert.All(ordinary.Paints, paint => Assert.Equal(16, paint.Size));
        head.SelectAll(); Assert.Equal(head.Text, head.SelectedText); Assert.Equal(16, head.FontSize);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData(false, 40)] [InlineData(true, 40)]
    [InlineData(false, 100)] [InlineData(true, 100)]
    public void AtomicDescendantsContributeToAnInlineInitialsAutoSize(bool includeText, double atomHeight)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var embeddedText = new TextBlock { Text = "x", FontSize = 12 };
        var atom = new Border { Width = 32, Height = atomHeight, Background = new SolidColorBrush(Colors.Blue), Child = embeddedText };
        var contents = new StackPanel(); if (includeText) contents.Children.Add(new TextBlock { Text = "A" }); contents.Children.Add(atom);
        var initial = new Border { Child = contents }; var tail = new TextBlock { Text = "lpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu." };
        var root = new StackPanel(); root.Children.Add(initial); root.Children.Add(tail);
        Css.SetClass(root, "body"); Css.SetClass(initial, "initial"); Css.SetClass(contents, "contents"); Css.SetClass(atom, "atom"); Css.SetClass(tail, "tail");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;} .initial{display:inline;initial-letter:3;color:green;}" +
            ".contents{display:contents;} .contents TextBlock,.tail{display:inline;} .tail{color:black;} .atom{display:inline-block;}"));
        Layout(root, 300, 300); var capture = new TextCapture(); root.Render(capture);
        var ordinary = capture.Paints.First(p => p.Color == Colors.Black); var at = atom.TransformToAncestor(root);
        Assert.True(ordinary.X >= at.X + atom.RenderSize.Width - .001, $"Body x={ordinary.X} overlaps atom ending at {at.X + atom.RenderSize.Width}.");
        Assert.True(initial.RenderSize.Width >= 32); Assert.True(initial.RenderSize.Height >= atomHeight);
        Assert.Equal(32, atom.Width); Assert.Equal(atomHeight, atom.Height);
        Assert.Equal(12, Assert.Single(capture.Paints, p => p.Text == "x").Size); Assert.Equal(12, embeddedText.FontSize);
        Assert.Same(contents, initial.Child); Assert.Same(atom, contents.Children[^1]);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("none")] [InlineData("grid")]
    public void InitialAlignmentUsesTheRootInlineFontOfAStyledFirstLine(string wrap)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { Text = "Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu." };
        Css.SetClass(text, "body"); Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(
            ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
            ".body::first-line{font-family:Arial;font-size:22px;line-height:32px;} .body::first-letter{initial-letter:3;initial-letter-wrap:" + wrap + ";color:green;}"));
        Layout(text, 300, 300); var capture = new TextCapture(); text.Render(capture);
        var letter = Assert.Single(capture.Paints, p => p.Text == "A" && p.Color == Colors.Green);
        var ordinary = capture.Paints.First(p => p.Text.Contains("lpha"));
        var cap = TextMeasurement.GetFontUnitMetrics("Arial", 22).CapHeight;
        Assert.Equal((48 + cap) / cap * 22, letter.Size, 3); Assert.Equal(22, ordinary.Size);
        Assert.Equal(48, letter.Baseline - ordinary.Baseline, 3); Assert.Equal(16, text.FontSize);
        if (wrap == "grid")
        {
            Assert.True(TextMeasurement.TryGetFontInkBounds(new FormattedText("A", "Arial", letter.Size), out var ink));
            var unit = TextMeasurement.GetFontUnitMetrics("Georgia", 16).IdeographicAdvance;
            Assert.Equal(Math.Ceiling(ink.Width / unit) * unit, ordinary.X, 3);
        }
        Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
    }

    [Theory]
    [InlineData("min-width:100px", 100)]
    [InlineData("max-width:40px", 40)]
    [InlineData("width:100px", 100)]
    public void TheInitialsUsedWidthConstrainsItsFirstLineAdvance(string constraint, double width)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { Text = "Alpha beta gamma delta epsilon zeta eta theta." };
        Css.SetClass(text, "body"); Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(
            ".body{display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;}" +
            ".body::first-letter{initial-letter:3;color:green;" + constraint + ";}"));
        Layout(text, 300, 300); var capture = new TextCapture(); text.Render(capture);
        Assert.Equal(width, capture.Paints.First(p => p.Color == Colors.Black).X, 3);
        Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
    }

    [Fact]
    public void PositionedDescendantsKeepTheirOwnFontAndDoNotEnlargeTheInitial()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var ghost = new TextBlock { Text = "positioned", FontSize = 12 }; Css.SetStyle(ghost,
            "display:block;position:absolute;left:200px;top:150px;color:blue;");
        var letter = new TextBlock { Text = "A" }; Css.SetStyle(letter, "display:inline;");
        var contents = new StackPanel(); Css.SetStyle(contents, "display:contents;"); contents.Children.Add(letter); contents.Children.Add(ghost);
        var initial = new Border { Child = contents }; Css.SetStyle(initial, "display:inline;initial-letter:3;color:green;");
        var tail = new TextBlock { Text = "lpha beta gamma delta epsilon zeta eta." }; Css.SetStyle(tail, "display:inline;color:black;");
        var root = new StackPanel(); Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        root.Children.Add(initial); root.Children.Add(tail); Layout(root, 300, 300);
        var capture = new TextCapture(); root.Render(capture);
        Assert.True(initial.RenderSize.Width < 100); Assert.True(initial.RenderSize.Height < 100);
        Assert.Equal(12, Assert.Single(capture.Paints, p => p.Text == "positioned").Size); Assert.Equal(12, ghost.FontSize);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("baseline")] [InlineData("top")] [InlineData("bottom")] [InlineData("middle")]
    public void AligningAnEmbeddedBoxDoesNotCreateAnotherInitialLine(string alignment)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var letter = new TextBlock { Text = "A" }; Css.SetStyle(letter, "display:inline;");
        var atom = new Border { Width = 16, Height = 20, Background = new SolidColorBrush(Colors.Blue) };
        Css.SetStyle(atom, "display:inline-block;vertical-align:" + alignment + ";");
        var contents = new StackPanel(); Css.SetStyle(contents, "display:contents;"); contents.Children.Add(letter); contents.Children.Add(atom);
        var initial = new Border { Child = contents }; Css.SetStyle(initial, "display:inline;initial-letter:3;color:green;");
        var tail = new TextBlock { Text = "lpha beta gamma delta epsilon zeta eta." }; Css.SetStyle(tail, "display:inline;color:black;");
        var root = new StackPanel(); Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        root.Children.Add(initial); root.Children.Add(tail); Layout(root, 300, 300);
        var capture = new TextCapture(); root.Render(capture); var body = capture.Paints.First(p => p.Color == Colors.Black);
        var metrics = TextMeasurement.GetFontMetrics("Georgia", 16);
        Assert.Equal(metrics.Ascent + (24 - metrics.Ascent - metrics.Descent) / 2, body.Baseline, 3);
        Assert.Equal(48, Assert.Single(capture.Paints, p => p.Text == "A").Baseline - body.Baseline, 3);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }
    [Theory]
    [InlineData("slice")] [InlineData("clone")]
    public void ALongInlineInitialPublishesItsActualDecorationFragments(string decoration)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "Alpha beta eta", IsTextSelectionEnabled = true };
        var tail = new TextBlock { Text = " a short continuation with ordinary text." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;color:green;background:#e1eee4;padding:3px;border:2px solid green;box-decoration-break:" + decoration + ";");
        Css.SetStyle(tail, "display:inline;color:black;"); Layout(root, 260, 900);
        var capture = new TextCapture(); root.Render(capture);
        var large = capture.Paints.Where(p => p.Color == Colors.Green).ToArray(); Assert.Equal(3, large.Length);
        Assert.True(head.IsCssInlineFrame, "A non-atomic initial must keep its fragments in the containing inline formatter.");
        var allocation = Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox); Assert.Same(root, allocation.Owner);
        Assert.Equal(large.Length, allocation.Fragments.Length);
        for (var index = 0; index < large.Length; index++)
        {
            var fragment = allocation.Fragments[index];
            var left = decoration == "clone" || index == 0; var right = decoration == "clone" || index == large.Length - 1;
            Assert.Equal(left, fragment.LeftEdge); Assert.Equal(right, fragment.RightEdge);
            Assert.True(TextMeasurement.TryGetFontInkBounds(new FormattedText(large[index].Text,"Georgia",large[index].Size),out var ink));
            Assert.Equal(ink.Width + (left ? 5 : 0) + (right ? 5 : 0), fragment.Rect.Width, 2);
            var cap = TextMeasurement.GetFontUnitMetrics("Georgia",large[index].Size).CapHeight;
            // Block-start border/padding includes ascenders above the over
            // alignment point; an unpadded initial instead ignores that ink.
            Assert.Equal(Math.Max(cap,cap + ink.Bottom) - Math.Min(0,cap + ink.Top) + 10, fragment.Rect.Height, 2);
        }
        var delta = head.TransformToAncestor(root); var last = allocation.Fragments[^1].Rect;
        Assert.True(capture.Paints.First(p => p.Color == Colors.Black).X >= delta.X + last.Right - .001);
        head.SelectAll(); Assert.Equal(head.Text,head.SelectedText); Assert.Equal(16,head.FontSize);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("left")] [InlineData("center")] [InlineData("right")]
    public void AutomaticInitialFragmentsIgnoreTheirOwnTextAlignment(string alignment)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "Alpha beta eta" }; var tail = new TextBlock { Text = " a short continuation." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root,"display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head,"display:inline;initial-letter:3;text-align:" + alignment + ";color:green;");
        Css.SetStyle(tail,"display:inline;color:black;"); Layout(root,260,900);
        var capture = new TextCapture();root.Render(capture);var large=capture.Paints.Where(p=>p.Color==Colors.Green).ToArray();Assert.Equal(3,large.Length);
        foreach(var line in large)
        {
            Assert.True(TextMeasurement.TryGetFontInkBounds(new FormattedText(line.Text,"Georgia",line.Size),out var ink));
            Assert.Equal(0,line.X+ink.Left,2);
        }
        Assert.True(root.IsMeasureValid);Assert.True(root.IsArrangeValid);
    }

    [Fact]
    public void EachLargeLineUsesItsOwnInkHeightBeforeTheNextFragment()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head=new TextBlock{Text="Alpha beta eta"};var tail=new TextBlock{Text=" a short continuation."};var root=new StackPanel();root.Children.Add(head);root.Children.Add(tail);
        Css.SetStyle(root,"display:block;font-family:Georgia;font-size:16px;line-height:24px;");Css.SetStyle(head,"display:inline;initial-letter:3;color:green;");Css.SetStyle(tail,"display:inline;color:black;");Layout(root,260,900);
        var capture=new TextCapture();root.Render(capture);var large=capture.Paints.Where(p=>p.Color==Colors.Green).ToArray();Assert.Equal(3,large.Length);
        for(var index=1;index<large.Length;index++)
        {
            Assert.True(TextMeasurement.TryGetFontInkBounds(new FormattedText(large[index-1].Text,"Georgia",large[index-1].Size),out var ink));
            var cap=TextMeasurement.GetFontUnitMetrics("Georgia",large[index-1].Size).CapHeight;
            Assert.Equal(Math.Max(cap,cap+ink.Bottom),large[index].Baseline-large[index-1].Baseline,2);
        }
        Assert.Equal(48,large[^1].Baseline-capture.Paints.First(p=>p.Color==Colors.Black).Baseline,3);
        Assert.True(root.IsMeasureValid);Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("left")] [InlineData("center")] [InlineData("right")]
    public void ADefiniteInitialWidthHonorsTextAlignmentWithoutChangingItsAdvance(string alignment)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "A" }; var tail = new TextBlock { Text = "lpha beta gamma." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;width:160px;text-align:" + alignment + ";color:green;");
        Css.SetStyle(tail, "display:inline;color:black;"); Layout(root, 300, 300);
        var capture = new TextCapture(); root.Render(capture);
        var letter = Assert.Single(capture.Paints, paint => paint.Color == Colors.Green);
        var formatted = new FormattedText("A", "Georgia", letter.Size); Assert.True(TextMeasurement.MeasureText(formatted));
        var free = 160 - formatted.Width;
        Assert.Equal(alignment == "right" ? free : alignment == "center" ? free / 2 : 0, letter.X, 2);
        Assert.Equal(160, capture.Paints.First(paint => paint.Color == Colors.Black).X, 3);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Fact]
    public void AForcedTerminalBreakCreatesTheFinalEmptyInitialFragment()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "Alpha\nbeta\n", IsTextSelectionEnabled = true };
        var tail = new TextBlock { Text = "continues on the last initial line." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;white-space:pre-wrap;color:green;");
        Css.SetStyle(tail, "display:inline;color:black;"); Layout(root, 260, 900);
        var capture = new TextCapture(); root.Render(capture);
        Assert.Equal(2, capture.Paints.Count(paint => paint.Color == Colors.Green && paint.Text.Length > 0));
        var allocation = Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox); Assert.Equal(3, allocation.Fragments.Length);
        Assert.Equal(0, allocation.Fragments[^1].Rect.Width, 3);
        Assert.Equal(0, capture.Paints.First(paint => paint.Color == Colors.Black).X, 3);
        head.SelectAll(); Assert.Equal(head.Text, head.SelectedText);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("slice")] [InlineData("clone")]
    public void AFragmentedInitialsSelectionEndsAtEachFragmentsOwnEdge(string decoration)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "Alpha beta eta", IsTextSelectionEnabled = true };
        var tail = new TextBlock { Text = " continues with body text." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;box-decoration-break:" + decoration + ";");
        Css.SetStyle(tail, "display:inline;"); Layout(root, 260, 900);
        var allocation = Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox);
        var bounds = head.DocumentTextBounds(0, head.Text.Length); Assert.Equal(3, bounds.Count);
        for (var index = 0; index < bounds.Count; index++)
            Assert.True(bounds[index].Right <= allocation.Fragments[index].Rect.Right + .001);
        Assert.NotSame(head.DocumentSharedLine(0, 5), head.DocumentSharedLine(6, 10));
        head.SelectAll(); Assert.Equal(head.Text, head.SelectedText);
    }

    [Fact]
    public void AnAncestorInlinesBorderStartsBeyondTheInitial()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "A" }; var tail = new TextBlock { Text = "lpha beta gamma." };
        var contents = new StackPanel(); contents.Children.Add(head); contents.Children.Add(tail);
        Css.SetStyle(contents, "display:contents;");
        var wrapper = new Border { Child = contents }; var root = new StackPanel(); root.Children.Add(wrapper);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(wrapper, "display:inline;padding:2px;border:2px solid blue;");
        Css.SetStyle(head, "display:inline;initial-letter:3;color:green;"); Css.SetStyle(tail, "display:inline;color:black;");
        Layout(root, 300, 300);
        var initial = Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox);
        var ancestor = Assert.IsType<CssInlineBoxAllocation>(wrapper.CssInlineBox);
        var headOffset = head.TransformToAncestor(root); var parentOffset = wrapper.TransformToAncestor(root);
        Assert.True(parentOffset.X + ancestor.Fragments[0].Rect.Left >= headOffset.X + initial.Fragments[0].Rect.Right - .001);
        var capture = new TextCapture(); root.Render(capture);
        Assert.Equal(parentOffset.X + ancestor.Fragments[0].Rect.Left + 4, capture.Paints.First(paint => paint.Color == Colors.Black).X, 3);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData("start", 0)] [InlineData("center", .5)] [InlineData("end", 1)]
    public void ADefiniteInitialHeightUsesTypographicBearingsForContentAlignment(string alignment, double factor)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock { Text = "A" }; var tail = new TextBlock { Text = "lpha beta gamma." };
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;initial-letter-align:border-box;height:160px;align-content:" + alignment + ";color:green;");
        Css.SetStyle(tail, "display:inline;color:black;"); Layout(root, 300, 300);
        var capture = new TextCapture(); root.Render(capture);
        var letter = Assert.Single(capture.Paints, paint => paint.Color == Colors.Green);
        var font = TextMeasurement.GetFontMetrics("Georgia", letter.Size);
        var expected = 48 - 160 + font.Ascent + (160 - font.Ascent - font.Descent) * factor;
        Assert.Equal(expected, letter.Baseline - capture.Paints.First(paint => paint.Color == Colors.Black).Baseline, 3);
        Assert.Equal(160, Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox).Fragments[0].Rect.Height, 3);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RightToLeftInitialFragmentsStayAtTheInlineStart(bool separateSources)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = new TextBlock();
        if (separateSources) { head.Inlines.Add(new Run("Alpha ")); head.Inlines.Add(new Run("beta eta")); }
        else head.Text = "Alpha beta eta";
        var tail = new TextBlock { Text = "continues with ordinary text." };
        var root = new StackPanel { FlowDirection = FlowDirection.RightToLeft }; root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;");
        Css.SetStyle(head, "display:inline;initial-letter:3;color:green;"); Css.SetStyle(tail, "display:inline;color:black;");
        Layout(root, 260, 900);
        var allocation = Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox); var delta = head.TransformToAncestor(root);
        Assert.Equal(3, allocation.Fragments.Length);
        Assert.All(allocation.Fragments, fragment => Assert.Equal(260, fragment.Rect.Right + delta.X, 2));
        var capture = new TextCapture(); root.Render(capture);
        Assert.Equal(48, capture.Paints.Last(paint => paint.Color == Colors.Green).Baseline - capture.Paints.First(paint => paint.Color == Colors.Black).Baseline, 3);
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    private sealed record Paint(string Text, double Size, double Baseline, Color Color, double X);
    private sealed class TextCapture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<Paint> Paints = [];
        public override void DrawText(FormattedText text, Point point)
        {
            // A shared paragraph supplies its own baseline. Measuring a source
            // fragment again replaces that with the fragment font's ascent.
            if (!text.IsMeasured && text.PlatformTextLine is null) TextMeasurement.MeasureText(text);
            Paints.Add(new(text.Text, text.FontSize, point.Y + Offset.Y + text.Baseline,
                text.Foreground is SolidColorBrush brush ? brush.Color : Colors.Transparent, point.X + Offset.X));
        }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }

    private static void Style(DependencyObject target, string text)
    { Css.SetStyle(target, text); CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(target)); CssEvaluationScheduler.FlushIfPending(target.Dispatcher); }
}
