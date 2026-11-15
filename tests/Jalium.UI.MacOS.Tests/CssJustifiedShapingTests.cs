using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssJustifiedShapingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory] [InlineData("AV ffi AV")] [InlineData("مرحبا مرحبا")]
    [InlineData("אב AV אב")] [InlineData("AV\u00a0ffi AV")]
    public void WordExpansionKeepsWholeFontAdvances(string sample)
    {
        var text = Box(sample); Style(text, "inter-word"); Layout(text);
        using var font = Font(); double natural = Width(font, sample), space = Width(font, " ");
        int count = sample.Count(c => c is ' ' or '\u00a0'); double extra = (240 - natural) / count;
        Assert.Equal(240, Extent(text).Right - Extent(text).Left, 3);
        int index = sample.IndexOfAny([' ', '\u00a0']); var separator = text.GetSourceRangeSegments(index, 1).Single();
        Assert.Equal(space + extra, separator.EndX - separator.StartX, 3);
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CharacterExpansionKeepsAForcedLigatureAndDividesItsCellsInHalf(bool colored)
    {
        var text = Box(""); var first = new Run("f"); var last = new Run("fi");
        text.Inlines.Add(first); text.Inlines.Add(last);
        if (colored) { first.Foreground = Brushes.Red; last.Foreground = Brushes.Blue; }
        var a = first.Parent; var b = last.Parent; Style(text, "inter-character"); Layout(text);
        var family = text.FontFamily.GetRenderingSource(text);
        using var native = NativeTextParagraph.TryCreate([new("ffi", family, 32, 400, 0, Colors.Black)], family, 32, 100000, 44,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(native); double extra = (240 - native.Lines[0].Metrics.Line.Width) / 2;
        for (int i = 0; i < 3; i++)
        {
            var range = text.GetSourceRangeSegments(i, 1).Single();
            double prefix = native.Caret(0, i, false).X + (i == 0 ? 0 : (i - .5) * extra);
            Assert.Equal(prefix, range.StartX, 3);
            var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(range.StartX + .1, range.Baseline - 2), true));
            Assert.Equal(i, text.ContentStart.GetOffsetToPosition(hit));
        }
        Assert.Equal(240, Extent(text).Right, 3); Assert.Same(a, first.Parent); Assert.Same(b, last.Parent);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SharedSourcesRetainCrossNodeKerningAndRefreshAfterResize(bool nested)
    {
        var root = new StackPanel(); var frame = new StackPanel(); var a = Box("A"); var b = Box("V ffi");
        if (nested) { root.Children.Add(frame); frame.Children.Add(a); frame.Children.Add(b); Css.SetStyle(frame, "display:inline;background:#edf5ef"); }
        else { root.Children.Add(a); root.Children.Add(b); }
        Css.SetStyleSheet(root, "TextBlock{display:inline}"); var firstParent = a.Parent; var lastParent = b.Parent;
        try
        {
            using var font = Font(); double pair = Width(font, "AV");
            foreach (double width in new[] { 240d, 190d, 320d, 240d })
            {
                Style(root, "inter-word", width); Layout(root, width);
                var right = b.GetSourceRangeSegments(0, b.Text.Length).Last();
                Assert.Equal(width, right.EndX + b.TransformToAncestor(root).X, 3);
                var v = b.GetSourceRangeSegments(0, 1).Single();
                Assert.Equal(pair, v.EndX + b.TransformToAncestor(root).X, 3);
            }
            Assert.Same(firstParent, a.Parent); Assert.Same(lastParent, b.Parent);
            a.SelectAll(); b.SelectAll(); Assert.Equal("AV ffi", a.SelectedText + b.SelectedText);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Theory] [InlineData(2d, 5d)] [InlineData(-1d, 3d)]
    public void AutoWordAndTrackingAdjustTheBaseBeforeDistributingRemainingSpace(double tracking, double word)
    {
        const string sample = "中AV ffi文"; var text = Box(sample);
        Style(text, "inter-word", tracking: tracking, word: word, auto: "normal"); Layout(text); using var font = Font();
        double natural = Width(font, sample) + 8 + word + (sample.Length - 1) * tracking;
        var space = text.GetSourceRangeSegments(3, 1).Single();
        Assert.Equal(Width(font, " ") + word + tracking + 240 - natural, space.EndX - space.StartX, 3);
        Assert.Equal(240, Extent(text).Right, 3);
    }

    [Fact]
    public void AJoinedCursiveWordKeepsItsShapeWhenNoExpansionIsPossible()
    {
        var text = Box("سلام"); Style(text, "inter-character"); Layout(text); using var font = Font();
        double natural = Width(font, text.Text); var range = Extent(text);
        Assert.Equal((240 - natural) / 2, range.Left, 3); Assert.Equal(natural, range.Right - range.Left, 3);
    }

    [Fact]
    public void DisplayChangesDiscardStalePaintPhasesForNestedInlineSources()
    {
        var root = new StackPanel(); var frame = new StackPanel(); var first = Box("AV ff"); var last = Box("i AV");
        root.Children.Add(frame); frame.Children.Add(first); root.Children.Add(last);
        Style(root, "inter-word"); Css.SetStyle(frame, "display:inline;background:#edf5ef;");
        // Populate the visual order while the following TextBlock is still a
        // block. Its later inline style must restore the original tree order.
        _ = VisualTreeHelper.GetChild(root, 0);
        Css.SetStyleSheet(root, "TextBlock{display:inline}");
        try
        {
            foreach (string display in new[] { "inline", "block", "inline" })
            {
                Css.SetStyle(last, "display:" + display); Layout(root);
                Assert.Same(display == "inline" ? frame : last, VisualTreeHelper.GetChild(root, 0));
            }
            first.SelectAll(); last.SelectAll(); Assert.Equal("AV ffi AV", first.SelectedText + last.SelectedText);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Fact]
    public void DisablingAndRestoringJustificationRetainsTheOriginalSources()
    {
        var text = Box("AV ffi AV"); using var font = Font(); double natural = Width(font, text.Text);
        foreach (string method in new[] { "inter-word", "none", "inter-word" })
        {
            Style(text, method); Layout(text); var range = Extent(text);
            Assert.Equal(method == "none" ? natural : 240, range.Right - range.Left, 3);
        }
    }

    private NativeTextFormat Font()
    {
        var font = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, font.SetFontFeatures([new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)])); return font;
    }
    private static (double Left, double Right) Extent(TextBlock text)
    { var ranges = text.GetSourceRangeSegments(0, text.Text.Length); return (ranges.Min(range => range.StartX), ranges.Max(range => range.EndX)); }
    private static double Width(NativeTextFormat font, string text) => font.MeasureText(text, 100000, 1000).WidthIncludingTrailingWhitespace;
    private static TextBlock Box(string text) => new() { Text = text, IsTextSelectionEnabled = true, FontFamily = "Seravek", FontSize = 32 };
    private static void Style(DependencyObject owner, string method, double width = 240, double tracking = 0, double word = 0, string auto = "no-autospace") =>
        Css.SetStyle(owner, $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;white-space:pre;text-align:justify-all;text-justify:{method};font-kerning:normal;font-feature-settings:'liga' 1;letter-spacing:{tracking}px;word-spacing:{word}px;text-autospace:{auto}");
    private static void Layout(FrameworkElement view, double width = 240)
    { for (int i = 0; i < 3; i++) { CssEvaluationScheduler.FlushIfPending(view.Dispatcher); view.Measure(new(width, 400)); view.Arrange(new(0, 0, width, 400)); view.UpdateLayout(); } }
}
