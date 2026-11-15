using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssWordShapingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("AV ffi AV", 6d)] [InlineData("AV ffi AV", -3d)]
    [InlineData("AV  ffi", 5d)] [InlineData(" AV ", 4d)]
    [InlineData("AV\u00a0ffi", 7d)] [InlineData("مرحبا مرحبا", 8d)]
    [InlineData("אב AV אב", 5d)] [InlineData("AV\u3000ffi", 7d)]
    public void WordSpacingKeepsWholeFontShapingAndOnlyExpandsSeparators(string sample, double word)
    {
        var text = Box(sample); Style(text, word); Layout(text);
        using var reference = Font();
        int separators = sample.Count(c => c is ' ' or '\u00a0');
        Assert.Equal(reference.MeasureText(sample, 100000, 1000).WidthIncludingTrailingWhitespace + separators * word,
            text.DesiredSize.Width, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Theory] [InlineData(2d, 6d)] [InlineData(-1d, 5d)] [InlineData(3d, -2d)]
    public void WordSpacingAndTrackingRetainForcedLigatures(double tracking, double word)
    {
        const string sample = "AV ffi AV";
        var text = Box(sample); Style(text, word, tracking); Layout(text); using var reference = Font();
        Assert.Equal(reference.MeasureText(sample, 100000, 1000).Width + 2 * word + (sample.Length - 1) * tracking,
            text.DesiredSize.Width, 3);
    }

    [Fact]
    public void NegativeWordSpacingClampsAtZeroAndRestoresWithoutLosingKerning()
    {
        var text = Box("AV AV"); using var reference = Font();
        double natural = reference.MeasureText("AV AV", 100000, 1000).Width;
        double space = reference.MeasureText(" ", 100000, 1000).WidthIncludingTrailingWhitespace;
        foreach (double word in new[] { 6d, -100d, 6d, 0d })
        {
            Style(text, word); Layout(text);
            Assert.Equal(natural + Math.Max(-space, word), text.DesiredSize.Width, 3);
            Assert.Equal("AV AV", text.Text);
        }
    }

    [Fact]
    public void PaintOnlyRunBoundariesKeepTheLigatureAndResetItsColorWithoutChangingWidth()
    {
        var text = Box(""); var first = new Run("AV f") { Foreground = Brushes.Red }; var last = new Run("fi AV") { Foreground = Brushes.Blue };
        text.Inlines.Add(first); text.Inlines.Add(last); var firstParent = first.Parent; var lastParent = last.Parent;
        using var font = Font(); double natural = font.MeasureText("AV ffi AV", 100000, 1000).Width;
        foreach (double word in new[] { 6d, 3d, 6d, 0d })
        {
            Style(text, word); Layout(text); Assert.Equal(natural + 2 * word, text.DesiredSize.Width, 3);
            first.Foreground = Brushes.Green; last.Foreground = Brushes.Red; Layout(text);
            Assert.Equal(natural + 2 * word, text.DesiredSize.Width, 3);
            Assert.Same(firstParent, first.Parent); Assert.Same(lastParent, last.Parent);
            text.SelectAll(); Assert.Equal("AV ffi AV", text.SelectedText);
        }
    }

    [Fact]
    public void ASeparatorRunResolvesInheritedPercentagesUsingItsOwnFontSize()
    {
        var text = Box(""); text.Inlines.Add(new Run("AV")); var separator = new Run(" ") { FontSize = 48 };
        text.Inlines.Add(separator); text.Inlines.Add(new Run("ffi")); var parent = separator.Parent;
        Style(text, 0); Css.SetStyle(text, "font-family:Seravek;font-size:32px;white-space:pre;word-spacing:25%;font-kerning:normal;font-feature-settings:'liga' 1;text-autospace:no-autospace");
        Layout(text); var family = text.FontFamily.GetRenderingSource(text);
        using var native = NativeTextParagraph.TryCreate([new("AV", family, 32, 400, 0, Colors.Black),
            new(" ", family, 48, 400, 0, Colors.Black), new("ffi", family, 32, 400, 0, Colors.Black)],
            family, 32, 100000, 60, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(native); Assert.Equal(native.Lines[0].Metrics.Line.Width + 12, text.DesiredSize.Width, 3);
        Assert.Same(parent, separator.Parent);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SplitSourcesKeepPairKerningAndTheSeparatorOwnsItsExtraAdvance(bool nested)
    {
        var root = new StackPanel(); var a = Box("A"); var b = Box("V "); var c = Box("ffi");
        if (nested) { var frame = new StackPanel(); frame.Children.Add(a); frame.Children.Add(b); root.Children.Add(frame); Css.SetStyle(frame, "display:inline"); }
        else { root.Children.Add(a); root.Children.Add(b); }
        root.Children.Add(c); Css.SetStyleSheet(root, "TextBlock{display:inline}");
        Css.SetStyle(root, "display:block;width:400px;font-family:Seravek;font-size:32px;line-height:44px;white-space:pre;word-spacing:6px;font-kerning:normal;font-feature-settings:'liga' 1;text-autospace:no-autospace");
        try
        {
            Layout(root); using var font = Font();
            var last = Assert.Single(c.GetSourceRangeSegments(0, 3));
            Assert.Equal(font.MeasureText("AV ffi", 100000, 1000).Width + 6, c.TransformToAncestor(root).X + last.EndX, 3);
            var space = Assert.Single(b.GetSourceRangeSegments(1, 1));
            Assert.Equal(font.MeasureText(" ", 100000, 1000).WidthIncludingTrailingWhitespace + 6, space.EndX - space.StartX, 3);
            var hit = Assert.IsType<TextPointer>(b.GetPositionFromPoint(new(space.StartX + .1, space.Baseline - 2), false));
            Assert.Equal(1, b.ContentStart.GetOffsetToPosition(hit));
            a.SelectAll(); b.SelectAll(); c.SelectAll(); Assert.Equal("AV ffi", a.SelectedText + b.SelectedText + c.SelectedText);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Theory] [InlineData(6d)] [InlineData(-2d)]
    public void WrappedLinesUseTheSameWholeWordShapesAndDiscardCollapsedTrailingSpacing(double word)
    {
        const string sample = "AV ffi AV ffi";
        var text = Box(sample); Style(text, word); Css.SetStyle(text, Css.GetStyle(text) + ";white-space:normal");
        using var font = Font(); double expected = font.MeasureText("AV ffi", 100000, 1000).Width + word;
        Layout(text, expected + .1);
        var first = Assert.Single(text.GetSourceRangeSegments(0, 6)); var second = Assert.Single(text.GetSourceRangeSegments(7, 6));
        Assert.Equal(expected, first.EndX - first.StartX, 3); Assert.Equal(expected, second.EndX - second.StartX, 3);
        Assert.True(second.Baseline > first.Baseline); text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Fact]
    public void NativeAdvancesPreserveSourceSpaceCellsAndBothSidesOfABidiBoundary()
    {
        const string sample = "אב AV"; var text = Box(sample); Style(text, 8); var family = text.FontFamily.GetRenderingSource(text);
        NativeTextParagraph.Span[] spans = [new(sample, family, 32, 400, 0, Colors.Black)];
        using var natural = NativeTextParagraph.TryCreate(spans, family, 32, 100000, 44, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        var units = Enumerable.Range(0, sample.Length).Select(i => new NativeMethods.ParagraphAdvanceUnit
            { TextPosition = (uint)i, Length = 1, Leading = i == 2 ? 4 : 0, Trailing = i == 2 ? 4 : 0, Flags = i == 2 ? 4u : 0u }).ToArray();
        using var advanced = NativeTextParagraph.TryCreateAdvanced(spans, family, 32, 100000, 44, FlowDirection.LeftToRight, units);
        Assert.NotNull(natural); Assert.NotNull(advanced);
        Assert.Equal(natural.Lines[0].Metrics.Line.Width + 8, advanced.Lines[0].Metrics.Line.Width, 3);
        var before = Assert.Single(natural.Selection(0, 2, 1)); var after = Assert.Single(advanced.Selection(0, 2, 1));
        Assert.Equal(before.Width + 8, after.Width, 3);
        for (int i = 0; i <= sample.Length; i++) foreach (bool backward in new[] { false, true })
        {
            var caret = advanced.Caret(0, i, backward); var hit = advanced.HitTest(0, caret.X);
            Assert.Equal(caret.X, advanced.Caret(0, (int)hit.TextPosition, hit.BackwardAffinity != 0).X, 3);
        }
    }

    [Theory] [InlineData(0d)] [InlineData(6d)]
    public void FrenchPunctuationAutospaceKeepsItsAdvanceAlongsideWordSpacing(double word)
    {
        const string sample = "AV? ffi AV";
        string style = $"font-family:Seravek;font-size:32px;line-height:44px;white-space:pre;font-kerning:none;font-feature-settings:'liga' 0;text-autospace:punctuation;word-spacing:{word}px;";
        var plain = Box(sample); plain.Language = XmlLanguage.GetLanguage("fr"); Css.SetStyle(plain, style);
        var root = new StackPanel { Language = XmlLanguage.GetLanguage("fr") }; var shared = new TextBlock { Text = sample };
        root.Children.Add(shared); Css.SetStyle(root, "display:block;width:400px;" + style); Css.SetStyle(shared, "display:inline");
        Layout(plain); Layout(root);
        using var font = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, font.SetFontFeatures([new(FontFeature.TagOf("kern"), 0), new(FontFeature.TagOf("liga"), 0)]));
        double expected = font.MeasureText(sample, 100000, 1000).Width + 2 * word +
            font.MeasureText("\u202f", 100000, 1000).WidthIncludingTrailingWhitespace;
        Assert.Equal(expected, plain.DesiredSize.Width, 3);
        Assert.Equal(expected, shared.GetSourceRangeSegments(0, sample.Length).Max(range => range.EndX) + shared.TransformToAncestor(root).X, 3);
        Assert.Equal(sample, shared.Text);
    }

    private NativeTextFormat Font()
    {
        var font = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, font.SetFontFeatures([new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)])); return font;
    }
    private static TextBlock Box(string text) => new() { Text = text, FontFamily = "Seravek", FontSize = 32, CssIntrinsicMeasure = true, IsTextSelectionEnabled = true };
    private static void Style(DependencyObject target, double word, double tracking = 0) => Css.SetStyle(target,
        $"font-family:Seravek;font-size:32px;line-height:44px;white-space:pre;font-kerning:normal;font-feature-settings:'liga' 1;letter-spacing:{tracking}px;word-spacing:{word}px;text-autospace:no-autospace");
    private static void Layout(FrameworkElement view, double width = 400)
    {
        for (int i = 0; i < 3; i++) { CssEvaluationScheduler.FlushIfPending(view.Dispatcher); view.Measure(new(width, 500)); view.Arrange(new(0, 0, width, 500)); view.UpdateLayout(); }
    }
}
