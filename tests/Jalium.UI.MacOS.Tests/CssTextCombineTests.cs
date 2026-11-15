using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTextCombineTests
{
    [Theory]
    [InlineData("normal", " ")]
    [InlineData("normal", "   ")]
    [InlineData("pre", " ")]
    [InlineData("pre-wrap", "   ")]
    [InlineData("break-spaces", "   ")]
    [InlineData("preserve-spaces", "   ")]
    public void WhitespaceOnlyCompositionKeepsOneEmIncludingAtColumnEnd(string white, string source)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source);
        string declaration = white == "preserve-spaces" ? "white-space-collapse:preserve-spaces;" : $"white-space:{white};";
        Style(text, "vertical-rl", "mixed", "all", declaration); Layout(text, 8);
        Assert.Equal(32, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 3);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Theory]
    [InlineData("pre", "  12", "\u00a0\u00a012")]
    [InlineData("pre", "34  ", "34\u00a0\u00a0")]
    [InlineData("pre", "5    6", "5\u00a0\u00a0\u00a0\u00a06")]
    [InlineData("normal", "  12", "12")]
    [InlineData("normal", "34  ", "34")]
    [InlineData("normal", "5    6", "5\u00a06")]
    [InlineData("preserve-spaces", "  12", "\u00a0\u00a012")]
    public void CombinedWhitespaceFollowsInlineBlockProcessing(string white, string source, string equivalent)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Box(source); var reference = Box(equivalent);
        actual.FontFamily = reference.FontFamily = new FontFamily("Arial");
        string declaration = white == "preserve-spaces" ? "white-space-collapse:preserve-spaces;" : $"white-space:{white};";
        Style(actual, "vertical-rl", "mixed", "all", declaration);
        Style(reference, "vertical-rl", "mixed", "all", declaration); Layout(actual); Layout(reference);
        Assert.Equal(Pixels(reference.Render), Pixels(actual.Render));
        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target = context.CreateOffscreenRenderTarget(320, 620);
            target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
            target.EndDraw(); var bytes = new byte[320 * 620 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(bytes, 320 * 4, out _, out _)); return bytes;
        }
    }

    [Theory]
    [InlineData(RenderBackend.Software, false, false)]
    [InlineData(RenderBackend.Software, true, false)]
    [InlineData(RenderBackend.Software, false, true)]
    [InlineData(RenderBackend.Metal, false, false)]
    [InlineData(RenderBackend.Metal, true, false)]
    [InlineData(RenderBackend.Metal, false, true)]
    public void NativeCompositionHasOneEmAdvanceAndOriginalSourceGeometry(RenderBackend backend, bool upright, bool sideways)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        NativeTextParagraph.Span[] spans = [new("水123年", "PingFang SC", 32, 400, 0, Colors.Black)];
        NativeMethods.ParagraphCombineUnit[] units = [new() { TextPosition = 1, Length = 3, Em = 32, Direction = 0 }];
        using var paragraph = NativeTextParagraph.TryCreate(spans, "PingFang SC", 32, 1000, 40,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true, verticalForms: !sideways, uprightForms: upright, combined: units);
        Assert.NotNull(paragraph); Assert.Single(paragraph.Lines);
        var whole = Assert.Single(paragraph.Selection(0, 1, 3));
        Assert.Equal(32, whole.Width, 3); Assert.Equal(1u, whole.TextPosition); Assert.Equal(3u, whole.Length);
        foreach (int at in new[] { 1, 2, 3 })
        {
            var part = Assert.Single(paragraph.Selection(0, at, 1));
            Assert.Equal(whole.X, part.X, 3); Assert.Equal(whole.Width, part.Width, 3);
        }
        Assert.Contains(paragraph.HitTest(0, whole.X + 8).TextPosition, new uint[] { 1, 4 });
        Assert.Contains(paragraph.HitTest(0, whole.X + 24).TextPosition, new uint[] { 1, 4 });
    }

    [Theory]
    [InlineData("vertical-rl", "mixed")]
    [InlineData("vertical-lr", "mixed")]
    [InlineData("vertical-rl", "upright")]
    [InlineData("vertical-lr", "upright")]
    [InlineData("vertical-rl", "sideways")]
    [InlineData("vertical-lr", "sideways")]
    public void TextBlockDigitsUseAtomicCellsAndRestoreAfterStyleRemoval(string mode, string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box("水12年3月456日");
        Style(text, mode, orientation, "digits 2"); Layout(text);
        var combined = Assert.Single(text.DocumentTextBounds(1, 2));
        Assert.Equal(32, combined.Height, 3);
        Assert.Equal(combined, Assert.Single(text.DocumentTextBounds(2, 1)));
        text.Select(1, 2); Assert.Equal("12", text.SelectedText);
        text.SelectAll(); Assert.Equal(text.Text, text.SelectedText);
        Style(text, mode, orientation, "none"); Layout(text);
        Assert.True(text.DocumentTextBounds(1, 2).Sum(rect => rect.Height) > 32);
        Style(text, mode, orientation, "digits 2"); Layout(text);
        Assert.Equal(combined, Assert.Single(text.DocumentTextBounds(1, 2)));
    }

    [Theory]
    [InlineData("all", "A\r\nB", "A\r\nB")]
    [InlineData("all", "AB CD", "AB CD")]
    [InlineData("digits 4", "1234", "1234")]
    public void OversizedCombinationNeverSplitsOnEmergencyWrap(string combine, string source, string expected)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source); Style(text, "vertical-rl", "mixed", combine, "line-break:anywhere;white-space:pre-wrap;");
        Layout(text, 8);
        Assert.Single(text.GetSourceRangeGeometry(0, source.Length));
        Assert.Equal(32, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 3);
        text.SelectAll(); Assert.Equal(expected, text.SelectedText);
    }

    [Fact]
    public void InheritedInlineBoundaryDoesNotCombinePartialDigitSequences()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(""); text.Inlines.Add(new Run("12"));
        var span = new Span(); span.Inlines.Add(new Run("34")); text.Inlines.Add(span);
        Style(text, "vertical-rl", "upright", "digits 4"); Layout(text);
        Assert.Equal(128, text.DocumentTextBounds(0, 4).Sum(rect => rect.Height), 3);
        Css.SetStyle(text, "writing-mode:vertical-rl;text-orientation:upright;font-size:32px;text-combine-upright:none;");
        Css.SetStyle(span, "text-combine-upright:digits 4;"); Layout(text);
        Assert.Equal(32, Assert.Single(text.DocumentTextBounds(2, 2)).Height, 3);
    }

    [Fact]
    public void LetterSpacingAppliesBetweenCompositionsAndNeverBetweenTheirDigits()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box("12年34"); Style(text, "vertical-rl", "upright", "digits 2", "letter-spacing:8px;"); Layout(text);
        Assert.Equal(112, text.DocumentTextBounds(0, 5).Sum(rect => rect.Height), 3);
    }

    [Theory]
    [InlineData("digits 2", "12年123月1234日", 32, 96, 128)]
    [InlineData("digits 3", "12年123月1234日", 32, 32, 128)]
    [InlineData("digits 4", "12年123月1234日", 32, 32, 32)]
    public void DigitsAlwaysUseMaximalAsciiSequences(string combine, string source, double two, double three, double four)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source); Style(text, "vertical-rl", "upright", combine); Layout(text);
        Assert.Equal(two, text.DocumentTextBounds(0, 2).Sum(r => r.Height), 3);
        Assert.Equal(three, text.DocumentTextBounds(3, 3).Sum(r => r.Height), 3);
        Assert.Equal(four, text.DocumentTextBounds(7, 4).Sum(r => r.Height), 3);
    }

    [Theory]
    [InlineData("12年3", "text-transform:full-width;", 32)]
    [InlineData("１２年3", "", 64)]
    public void CandidateDigitsAreChosenBeforeFullWidthTransformation(string source, string extra, double advance)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source); Style(text, "vertical-rl", "upright", "digits 2", extra); Layout(text);
        Assert.Equal(advance, text.DocumentTextBounds(0, 2).Sum(r => r.Height), 3);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Theory]
    [InlineData("A\r\nB")]
    [InlineData("A\u2028B")]
    public void AllIgnoresForcedBreaksWithoutAnExplicitWhiteSpaceDeclaration(string source)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source); Style(text, "vertical-rl", "mixed", "all"); Layout(text, 8);
        Assert.Equal(32, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 3);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Fact]
    public void InheritedCombinationRebuildsWhenTheParentStyleChanges()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var parent = new StackPanel(); var text = Box("1234"); parent.Children.Add(text);
        Css.SetStyle(text, "writing-mode:vertical-rl;text-orientation:upright;font-size:32px;text-autospace:no-autospace;");
        Css.SetStyle(parent, "text-combine-upright:digits 4;"); Layout(text);
        var combined = Assert.Single(text.DocumentTextBounds(0, 4)); Assert.Equal(32, combined.Height, 3);
        Css.SetStyle(parent, "text-combine-upright:none;"); Layout(text);
        Assert.Equal(128, text.DocumentTextBounds(0, 4).Sum(r => r.Height), 3);
        Css.SetStyle(parent, "text-combine-upright:digits 4;"); Layout(text);
        Assert.Equal(combined, Assert.Single(text.DocumentTextBounds(0, 4)));
    }

    [Theory]
    [InlineData("AB", "all")]
    [InlineData("  AB  ", "all")]
    [InlineData("1\u00ad2", "all")]
    public void InternalLetterAndWordSpacingDoNotExpandAnAllComposition(string source, string combine)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source); Style(text, "vertical-rl", "mixed", combine, "letter-spacing:20px;word-spacing:80px;"); Layout(text);
        Assert.Equal(32, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 3);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Theory]
    [InlineData(RenderBackend.Software)]
    [InlineData(RenderBackend.Metal)]
    public void CombinedCellDoesNotForceSurroundingMixedTextToLeftToRight(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        NativeTextParagraph.Span[] spans = [new("אב12ג", "Arial", 32, 400, 0, Colors.Black)];
        NativeMethods.ParagraphCombineUnit[] combined = [new() { TextPosition = 2, Length = 2, Em = 32, Direction = 0 }];
        using var paragraph = NativeTextParagraph.TryCreate(spans, "Arial", 32, 1000, 40, TextAlignment.Left,
            FlowDirection.RightToLeft, noWrap: true, verticalForms: true, combined: combined);
        Assert.NotNull(paragraph);
        Assert.True(paragraph.Caret(0, 0, false).X > paragraph.Caret(0, 1, true).X);
        var cell = Assert.Single(paragraph.Selection(0, 2, 2)); Assert.Equal(32, cell.Width, 3);
        Assert.Contains(paragraph.HitTest(0, cell.X + cell.Width / 4).TextPosition, new uint[] { 2, 4 });
    }

    [Theory]
    [InlineData("ß1", "text-transform:uppercase;")]
    [InlineData("ß\r\nAB", "text-transform:uppercase;white-space:pre-wrap;")]
    [InlineData("ß1", "font-variant-caps:all-small-caps;")]
    [InlineData("a\u03081", "text-transform:uppercase;")]
    public void ExpandedPaintAndFontCapsKeepOneCellAndOriginalCopyText(string source, string extra)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box(source); text.FontFamily = new FontFamily("Arial");
        Style(text, "vertical-rl", "mixed", "all", extra + "line-break:anywhere;"); Layout(text, 8);
        Assert.Equal(32, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 3);
        Assert.Single(text.GetSourceRangeGeometry(0, source.Length));
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Theory]
    [InlineData("horizontal-tb")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void IneligibleWritingModesKeepTheirOrdinaryLayout(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box("1234"); Style(text, mode, "mixed", "none"); Layout(text);
        var ordinary = text.DocumentTextBounds(0, 4);
        Style(text, mode, "mixed", "all"); Layout(text);
        Assert.Equal(ordinary, text.DocumentTextBounds(0, 4));
    }

    [Fact]
    public void RecordedFrameKeepsItsImmutableCombinedGlyphsAfterContentChanges()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Box("12年34"); Style(text, "vertical-rl", "mixed", "digits 2"); Layout(text);
        var before = Pixels(text.Render);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        text.Text = "56789"; Style(text, "vertical-lr", "upright", "none"); Layout(text);
        Assert.Equal(before, Pixels(dc => DrawingReplayer.Replay(frame, dc)));
        byte[] Pixels(Action<DrawingContext> render)
        {
            using var target = context.CreateOffscreenRenderTarget(320, 620);
            target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { render(dc); dc.Close(); }
            target.EndDraw(); var pixels = new byte[320 * 620 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 320 * 4, out _, out _)); return pixels;
        }
    }

    private static TextBlock Box(string source) => new() { Text = source, FontFamily = new FontFamily("PingFang SC"),
        FontSize = 32, Foreground = Brushes.Black, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
    private static void Style(TextBlock text, string mode, string orientation, string combine, string extra = "")
        => Css.SetStyle(text, $"writing-mode:{mode};text-orientation:{orientation};font-size:32px;text-autospace:no-autospace;text-combine-upright:{combine};{extra}");
    private static void Layout(TextBlock text, double height = 600)
    { text.Measure(new Size(300, height)); text.Arrange(new Rect(0, 0, 300, height)); }
}
