using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssContextualShapingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("中AVffi文", 2)] [InlineData("中12文", 2)] [InlineData("カAV文", 2)]
    [InlineData("中Ａ文", 0)] [InlineData("中\u3000AV文", 1)] [InlineData("中文", 0)]
    public void AutomaticSpacingRetainsWholeFontShaping(string sample, int gaps)
    {
        var text = Box(sample); Style(text); Layout(text);
        using var font = Font();
        Assert.Equal(Width(font, sample) + gaps * Ic(text) / 8, text.DesiredSize.Width, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Theory] [InlineData(2d, 5d)] [InlineData(-1d, 6d)] [InlineData(3d, -2d)]
    public void AutoWordAndLetterSpacingShareOneFontShape(double tracking, double word)
    {
        const string sample = "中AV ffi文"; var text = Box(sample); Style(text, tracking: tracking, word: word); Layout(text);
        using var font = Font();
        Assert.Equal(Width(font, sample) + Ic(text) / 4 + word + (sample.Length - 1) * tracking,
            text.DesiredSize.Width, 3);
    }

    [Theory]
    [InlineData("AVffi!", "\u202f")] [InlineData("AVffi:", "\u00a0")]
    [InlineData("«AVffi»", "\u00a0\u00a0")] [InlineData("AV?AV!", "\u202f\u202f")]
    public void FrenchPunctuationAddsTheCorrectSpaceWithoutBreakingPairShaping(string sample, string spaces)
    {
        var text = Box(sample); text.Language = XmlLanguage.GetLanguage("fr-FR"); Style(text, "punctuation"); Layout(text);
        using var font = Font();
        Assert.Equal(Width(font, sample) + spaces.Sum(space => Width(font, space.ToString())), text.DesiredSize.Width, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CommonAncestorOwnsTheGapAndDescendantPaintBoxesExcludeIt(bool nested)
    {
        var root = new StackPanel(); var frame = new StackPanel(); var a = Box("中A"); var b = Box("Vffi文");
        if (nested) { frame.Children.Add(a); frame.Children.Add(b); root.Children.Add(frame); Css.SetStyle(frame, "display:inline;font-size:24px"); }
        else { root.Children.Add(a); root.Children.Add(b); }
        Css.SetStyleSheet(root, "TextBlock{display:inline;font-size:32px}"); Style(root, size: 16);
        Css.SetStyle(root, Css.GetStyle(root) + ";display:block;width:400px");
        try
        {
            Layout(root); using var font = Font();
            // The boundary is A/V, so it must still kern across its source boxes.
            double expected = Width(font, "中AVffi文") + Ic(a) / 4;
            var last = Assert.Single(b.GetSourceRangeSegments(0, b.Text.Length));
            Assert.Equal(expected, b.TransformToAncestor(root).X + last.EndX, 3);
            a.Text = "中"; b.Text = "AVffi"; Layout(root);
            var left = Assert.Single(a.GetSourceRangeSegments(0, 1)); var right = Assert.Single(b.GetSourceRangeSegments(0, b.Text.Length));
            double gap = (nested ? Ic(frame) : Ic(root)) / 8;
            Assert.Equal(TextMeasurement.GetFontUnitMetrics("Seravek", nested ? 24 : 16, 400, 0).IdeographicAdvance / 8, gap, 3);
            Assert.Equal(gap, b.TransformToAncestor(root).X + right.StartX - a.TransformToAncestor(root).X - left.EndX, 3);
            Assert.Equal(Width(font, "中"), left.EndX - left.StartX, 3);
            Assert.Equal(Width(font, "AVffi"), right.EndX - right.StartX, 3);
            var point = new Point(right.StartX + .1, right.Baseline - 2);
            var hit = Assert.IsType<TextPointer>(b.GetPositionFromPoint(point, false));
            Assert.Equal(0, b.ContentStart.GetOffsetToPosition(hit));
            a.SelectAll(); b.SelectAll(); Assert.Equal("中AVffi", a.SelectedText + b.SelectedText);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Fact]
    public void SiblingRunStylesDoNotOverrideTheirCommonParentsBoundary()
    {
        var text = Box(""); var a = new Run("中"); var b = new Run("AVffi"); text.Inlines.Add(a); text.Inlines.Add(b);
        var firstParent = a.Parent; var secondParent = b.Parent; Style(text);
        Css.SetStyle(a, "text-autospace:no-autospace"); Css.SetStyle(b, "text-autospace:no-autospace");
        Layout(text); using var font = Font(); Assert.Equal(Width(font, "中AVffi") + Ic(text) / 8, text.DesiredSize.Width, 3);
        Assert.Same(firstParent, a.Parent); Assert.Same(secondParent, b.Parent);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReplacementKeepsOriginalSpacesAndAddsWordSpacingToTheUsedAdvance(bool split)
    {
        var root = new StackPanel(); var a = Box(split ? "中" : "中 AVffi"); var space = Box(" "); var b = Box("AVffi");
        root.Children.Add(a); if (split) { root.Children.Add(space); root.Children.Add(b); }
        Css.SetStyleSheet(root, "TextBlock{display:inline}"); Style(root, "ideograph-alpha replace", word: 3);
        Css.SetStyle(root, Css.GetStyle(root) + ";display:block;width:400px");
        try
        {
            Layout(root); using var font = Font();
            var last = split ? b : a; var end = last.GetSourceRangeSegments(0, last.Text.Length).Max(range => range.EndX) + last.TransformToAncestor(root).X;
            Assert.Equal(Width(font, "中AVffi") + Ic(root) / 8 + 3, end, 3);
            a.SelectAll(); space.SelectAll(); b.SelectAll();
            Assert.Equal("中 AVffi", split ? a.SelectedText + space.SelectedText + b.SelectedText : a.SelectedText);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Theory] [InlineData("中 AVffi")] [InlineData("中\u00a0AVffi")] [InlineData("中\u202fAVffi")]
    public void InsertDoesNotAddAnotherGapAroundAnExistingSpace(string sample)
    {
        var text = Box(sample); Style(text, "ideograph-alpha insert"); Layout(text); using var font = Font();
        Assert.Equal(Width(font, sample), text.DesiredSize.Width, 3);
    }

    [Fact]
    public void ModeAndLanguageChangesRebuildTheRetainedGeometry()
    {
        var text = Box("中AVffi!"); using var font = Font(); double natural = Width(font, text.Text);
        foreach (var item in new[] { ("normal", "en", Ic(text) / 8), ("punctuation", "fr", Width(font, "\u202f")),
                     ("no-autospace", "fr", 0d), ("normal", "en", Ic(text) / 8) })
        {
            text.Language = XmlLanguage.GetLanguage(item.Item2); Style(text, item.Item1); Layout(text);
            Assert.Equal(natural + item.Item3, text.DesiredSize.Width, 3);
        }
    }

    [Theory] [InlineData("אב中AV")] [InlineData("中אבAV")]
    public void BidiAutoSpacingFollowsVisualNeighbors(string sample)
    {
        var text = Box(sample); Style(text); Layout(text);
        var family = text.FontFamily.GetRenderingSource(text);
        using var natural = NativeTextParagraph.TryCreate([new(sample, family, 32, 400, 0, Colors.Black)], family, 32, 100000, 44,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(natural);
        var visual = Enumerable.Range(0, sample.Length).Select(i => (Position: i,
            Left: Math.Min(natural.Caret(0, i, false).X, natural.Caret(0, i + 1, true).X))).OrderBy(cell => cell.Left).ToArray();
        int count = Enumerable.Range(1, visual.Length - 1).Count(i => sample[visual[i - 1].Position] == '中' || sample[visual[i].Position] == '中');
        Assert.Equal(natural.Lines[0].Metrics.Line.Width + count * Ic(text) / 8, text.DesiredSize.Width, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Fact]
    public void AVisualReplacementCanAppearWhereLogicalNeighborsWouldNotReplaceASpace()
    {
        const string sample = "中\u202eA \u202cB"; var text = Box(sample); Style(text, "ideograph-alpha replace"); Layout(text);
        var family = text.FontFamily.GetRenderingSource(text);
        using var natural = NativeTextParagraph.TryCreate([new(sample, family, 32, 400, 0, Colors.Black)], family, 32, 100000, 44,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(natural);
        double separator = Math.Abs(natural.Caret(0, 4, true).X - natural.Caret(0, 3, false).X);
        // RLO moves the space between the Chinese character and A. Its
        // logical neighbors A/B would not request a replacement.
        Assert.Equal(32, natural.Caret(0, 1, true).X, 3);
        Assert.Equal(32, natural.Caret(0, 4, true).X, 3);
        Assert.Equal(natural.Lines[0].Metrics.Line.Width - separator + Ic(text) / 8, text.DesiredSize.Width, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void WrappedLinesDiscardVirtualBoundarySpaceAndKeepTheirWholeWord(bool replacement)
    {
        string sample = replacement ? "中 AVffi文" : "中AVffi文";
        var text = Box(sample); Style(text, replacement ? "ideograph-alpha replace" : "normal");
        Css.SetStyle(text, Css.GetStyle(text) + ";white-space:pre-wrap"); Layout(text, 70); using var font = Font();
        int wordStart = replacement ? 2 : 1;
        var first = Assert.Single(text.GetSourceRangeSegments(0, 1));
        var word = Assert.Single(text.GetSourceRangeSegments(wordStart, 5));
        var last = Assert.Single(text.GetSourceRangeSegments(wordStart + 5, 1));
        Assert.Equal(Width(font, "中"), first.EndX - first.StartX, 3);
        Assert.Equal(Width(font, "AVffi"), word.EndX - word.StartX, 3);
        Assert.Equal(Width(font, "文"), last.EndX - last.StartX, 3);
        Assert.True(first.Baseline < word.Baseline && word.Baseline < last.Baseline);
        Assert.Equal(0, word.StartX, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Fact]
    public void APhysicalInlineMarginSuppressesAutomaticSpacingAcrossThatBoundary()
    {
        var root = new StackPanel(); var a = Box("中"); var b = Box("AVffi"); root.Children.Add(a); root.Children.Add(b);
        Css.SetStyleSheet(root, "TextBlock{display:inline}"); Style(root); Css.SetStyle(root, Css.GetStyle(root) + ";display:block;width:400px");
        Css.SetStyle(a, "margin-right:8px");
        try
        {
            Layout(root); var left = Assert.Single(a.GetSourceRangeSegments(0, 1)); var right = Assert.Single(b.GetSourceRangeSegments(0, 5));
            Assert.Equal(8, b.TransformToAncestor(root).X + right.StartX - a.TransformToAncestor(root).X - left.EndX, 3);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    private NativeTextFormat Font()
    {
        var font = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, font.SetFontFeatures([new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)])); return font;
    }
    private static double Width(NativeTextFormat font, string text) => font.MeasureText(text, 100000, 1000).WidthIncludingTrailingWhitespace;
    private static double Ic(DependencyObject owner) => CssTextDecorationPainter.GetSourceFontMetrics(owner, 32).IdeographicAdvance;
    private static TextBlock Box(string text) => new() { Text = text, FontFamily = "Seravek", FontSize = 32, CssIntrinsicMeasure = true, IsTextSelectionEnabled = true };
    private static void Style(DependencyObject target, string mode = "normal", double size = 32, double tracking = 0, double word = 0) => Css.SetStyle(target,
        $"font-family:Seravek;font-size:{size}px;line-height:44px;white-space:pre;font-kerning:normal;font-feature-settings:'liga' 1;letter-spacing:{tracking}px;word-spacing:{word}px;text-autospace:{mode}");
    private static void Layout(FrameworkElement view, double width = 400)
    {
        for (int i = 0; i < 3; i++) { CssEvaluationScheduler.FlushIfPending(view.Dispatcher); view.Measure(new(width, 500)); view.Arrange(new(0, 0, width, 500)); view.UpdateLayout(); }
    }
}
