using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssShapedSpacingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("AV", 2.0)] [InlineData("AV", -1.0)] [InlineData("AV", 0.0)]
    [InlineData("To WA", 2.0)] [InlineData("a\u0301b", 3.0)] [InlineData("🙂🙂", 2.0)]
    [InlineData("AVATAR", -1.5)] [InlineData("AV", .0002)]
    public void TrackingAddsSpacingToWholeShapedTextRatherThanIsolatedCharacters(string sample, double spacing)
    {
        var text = Box(sample); Apply(text, $"font-kerning:normal;letter-spacing:{spacing}px;text-autospace:no-autospace");
        Layout(text);
        using var reference = Reference();
        var natural = reference.MeasureText(sample, 100000, 1000).Width;
        int units = GraphemeClusters.GetBoundaries(sample).Length - 1;
        Assert.Equal(natural + (units - 1) * spacing, text.DesiredSize.Width, 3);
        Assert.Equal(sample, text.Text);
    }

    [Theory]
    [InlineData("مرحبا")] [InlineData("لا")] [InlineData("العربية")]
    public void CursiveWordsKeepTheirShapeAndNaturalWidth(string sample)
    {
        var text = Box(sample); text.FlowDirection = FlowDirection.RightToLeft;
        Apply(text, "font-kerning:normal;letter-spacing:3px;text-autospace:no-autospace"); Layout(text);
        using var reference = Reference();
        Assert.Equal(reference.MeasureText(sample, 100000, 1000).Width, text.DesiredSize.Width, 3);
    }

    [Fact]
    public void AForcedLigatureKeepsItsWholeGlyphAndReceivesItsTotalCellSpacing()
    {
        var text = Box("ffi"); Apply(text, "letter-spacing:2px;font-feature-settings:'liga' 1;text-autospace:no-autospace"); Layout(text);
        using var reference = _context.CreateTextFormat("Seravek", 32, 400, 0);
        reference.SetFontFeatures([new(FontFeature.TagOf("liga"), 1)]);
        Assert.Equal(reference.MeasureText("ffi", 100000, 1000).Width + 4, text.DesiredSize.Width, 3);
    }

    [Fact]
    public void AdjacentInlineValuesAverageAtTheirBoundaryAndRestore()
    {
        var a = new Run("A"); var v = new Run("V"); var text = Box(""); text.Inlines.Add(a); text.Inlines.Add(v); var aParent = a.Parent; var vParent = v.Parent;
        Apply(text, "font-kerning:normal;text-autospace:no-autospace");
        Apply(a, "letter-spacing:2px"); Apply(v, "letter-spacing:6px"); Layout(text);
        using var reference = Reference(); var natural = reference.MeasureText("AV", 100000, 1000).Width;
        Assert.Equal(natural + 4, text.DesiredSize.Width, 3);
        Apply(a, "letter-spacing:4px"); Layout(text); Assert.Equal(natural + 5, text.DesiredSize.Width, 3);
        Apply(a, "letter-spacing:2px"); Layout(text); Assert.Equal(natural + 4, text.DesiredSize.Width, 3);
        Assert.Same(aParent, a.Parent); Assert.Same(vParent, v.Parent);
    }

    [Fact]
    public void ImmutableSpacedParagraphSharesPairKerningAndCaretGeometry()
    {
        var text = Box("AV"); Apply(text, "font-kerning:normal;letter-spacing:2px");
        var source = text.FontFamily.GetRenderingSource(text);
        using var paragraph = NativeTextParagraph.TryCreateSpaced([new("AV", source, 32, 400, 0, Colors.Black)], source, 32, 1000, 44,
            TextAlignment.Left, FlowDirection.LeftToRight, spacing: [new() { TextPosition = 0, Length = 1, Spacing = 2 }, new() { TextPosition = 1, Length = 1, Spacing = 2 }]);
        Assert.NotNull(paragraph); using var reference = Reference();
        Assert.True(reference.HitTestTextPosition("AV", 100000, 1000, 1, false, out var native));
        Assert.Equal(native.CaretX + 1, paragraph.Caret(0, 1, false).X, 3);
        Assert.Equal(reference.MeasureText("AV", 100000, 1000).Width + 2, paragraph.Lines[0].Metrics.Line.Width, 3);
    }

    [Theory]
    [InlineData("A\u200BV")] [InlineData("A\u200EV")] [InlineData("A\u2060V")]
    public void FormattingCharactersDoNotAddAnotherTrackingGap(string sample)
    {
        var text = Box(sample); Apply(text, "font-kerning:normal;letter-spacing:2px;text-autospace:no-autospace"); Layout(text);
        using var reference = Reference();
        Assert.Equal(reference.MeasureText("AV", 100000, 1000).Width + 2, text.DesiredSize.Width, 3);
    }

    [Fact]
    public void CursiveSuppressionDoesNotSuppressSpacingAcrossSpacesOrLatinText()
    {
        const string sample = "لا لا AV";
        var text = Box(sample); Apply(text, "font-kerning:normal;letter-spacing:3px;text-autospace:no-autospace"); Layout(text);
        using var reference = Reference();
        // Seven inter-character boundaries, two within cursive words.
        Assert.Equal(reference.MeasureText(sample, 100000, 1000).Width + 15, text.DesiredSize.Width, 3);
    }

    [Fact]
    public void OrdinaryLtrFormattingKeepsVisibleSourceRangesAcrossSpacingChanges()
    {
        var text = Box("M\u2060M");
        using var reference = Reference(); double natural = reference.MeasureText("M", 100000, 1000).Width;
        foreach (int spacing in new[] { 0, 2, 0 })
        {
            Apply(text, $"font-kerning:normal;letter-spacing:{spacing}px;white-space:pre"); Layout(text);
            Assert.Equal(natural + spacing / 2.0, Assert.Single(text.GetSourceRangeSegments(0, 1)).EndX, 3);
            Assert.Single(text.GetSourceRangeSegments(2, 1));
        }
    }

    [Fact]
    public void BidirectionalTrackingUsesVisualOrderAndPreservesBothBoundaryAffinities()
    {
        const string sample = "אבAB";
        var text = Box(sample); Apply(text, "font-kerning:normal;letter-spacing:3px");
        var source = text.FontFamily.GetRenderingSource(text);
        NativeTextParagraph.Span[] spans = [new(sample, source, 32, 400, 0, Colors.Black)];
        using var natural = NativeTextParagraph.TryCreate(spans, source, 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        using var spaced = NativeTextParagraph.TryCreateSpaced(spans, source, 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight,
            Enumerable.Range(0, sample.Length).Select(i => new NativeMethods.ParagraphSpacingUnit { TextPosition = (uint)i, Length = 1, Spacing = 3 }).ToArray());
        Assert.NotNull(natural); Assert.NotNull(spaced);
        Assert.Equal(natural.Lines[0].Metrics.Line.Width + 9, spaced.Lines[0].Metrics.Line.Width, 3);
        Assert.Equal(natural.Caret(0, 0, false).X + 4.5, spaced.Caret(0, 0, false).X, 3);
        Assert.Equal(natural.Caret(0, 2, true).X, spaced.Caret(0, 2, true).X, 3);
        Assert.Equal(natural.Caret(0, 2, false).X + 4.5, spaced.Caret(0, 2, false).X, 3);
        var oldSelection = Assert.Single(natural.Selection(0, 2, 1));
        var selection = Assert.Single(spaced.Selection(0, 2, 1));
        Assert.Equal(oldSelection.X + 4.5, selection.X, 3);
        Assert.Equal(oldSelection.Width + 3, selection.Width, 3);
        double x = spaced.Caret(0, 2, false).X;
        var hit = spaced.HitTest(0, x);
        Assert.Contains(hit.TextPosition, new uint[] { 0, 2 });
        Assert.Equal(x, spaced.Caret(0, (int)hit.TextPosition, hit.BackwardAffinity != 0).X, 3);
    }

    [Theory]
    [InlineData(2.0)] [InlineData(-1.0)] [InlineData(30.0)]
    public void WrappedPairsKeepKerningLineEdgesSelectionAndPublicHits(double spacing)
    {
        const string sample = "AV AV AV";
        var text = Box(sample); text.IsTextSelectionEnabled = true;
        Apply(text, $"font-kerning:normal;letter-spacing:{spacing}px;white-space:normal;line-height:44px;text-autospace:no-autospace");
        using var reference = Reference(); double expected = reference.MeasureText("AV", 100000, 1000).Width + spacing;
        double width = expected + .1;
        for (int i = 0; i < 3; i++) { text.Measure(new(width, 500)); text.Arrange(new(0, 0, width, 500)); text.UpdateLayout(); }
        double previousBaseline = double.NegativeInfinity;
        foreach (int start in new[] { 0, 3, 6 })
        {
            var row = Assert.Single(text.GetSourceRangeSegments(start, 2));
            Assert.Equal(expected, row.EndX - row.StartX, 3);
            Assert.True(row.Baseline > previousBaseline); previousBaseline = row.Baseline;
            var v = Assert.Single(text.GetSourceRangeSegments(start + 1, 1));
            var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(v.StartX + .1, v.Baseline - 2), false));
            Assert.Equal(start + 1, text.ContentStart.GetOffsetToPosition(hit));
        }
        text.SelectAll(); Assert.Equal(sample, text.SelectedText);
    }

    private NativeTextFormat Reference()
    {
        var value = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, value.SetFontFeatures([new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 0),
            new(FontFeature.TagOf("clig"), 0), new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)]));
        return value;
    }
    private static TextBlock Box(string text) => new() { Text = text, FontFamily = "Seravek", FontSize = 32, CssIntrinsicMeasure = true };
    private static void Apply(DependencyObject owner, string style) { Css.SetStyle(owner, style); CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { view.Measure(new(1000, 1000)); view.Arrange(new(0, 0, 1000, 1000)); view.UpdateLayout(); } }
}
