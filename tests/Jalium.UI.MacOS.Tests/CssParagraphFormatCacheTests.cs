using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssParagraphFormatCacheTests
{
    [Theory]
    [InlineData(RenderBackend.Metal)]
    [InlineData(RenderBackend.Software)]
    public void NativeBackendPreservesZeroSizedEditorContracts(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
        var contracts = new CssZeroFontSizeTests();
        contracts.RichTextBoxDocumentZeroFontSizeDoesNotDrawGlyphs();
        contracts.SelfRenderedEditorsRespectZeroFontSize("number");
        contracts.NumberBoxHeaderUsesTheComputedZeroSize();
        contracts.ZeroSizedEditorHitTestingAvoidsZeroLineHeightDivision();
    }

    [Fact]
    public void ParagraphFontSizesKeepTheirOriginalSubMillipixelPrecision()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        TextMeasurement.ClearCache();
        var family = FrameworkElement.DefaultFontFamilyName;
        // Ordinary measurement historically coalesces sizes to 0.001px.
        // Paragraph formats previously came from an exact-size native create.
        TextMeasurement.GetLineHeight(family, 20, 400, 0);
        using var first = TextMeasurement.AcquireParagraphFormat(context, family, 20, 400, 0);
        using var second = TextMeasurement.AcquireParagraphFormat(context, family, 20.0004f, 400, 0);
        using var reference = TextMeasurement.CreateTextFormatFromFamilyList(context, family, 20.0004f, 400, 0);
        Assert.NotNull(first); Assert.NotNull(second);
        Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(second.Handle, out var actual));
        var expected = reference.GetFontMetrics();
        Assert.Equal(expected.Ascent, actual.Ascent); Assert.Equal(expected.Descent, actual.Descent);
        Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(first.Handle, out var previous));
        Assert.NotEqual(previous.Ascent, actual.Ascent);
    }

    [Fact]
    public void ClearingTheCacheKeepsAnInFlightParagraphFormatAlive()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        TextMeasurement.ClearCache();
        using var format = TextMeasurement.AcquireParagraphFormat(context, FrameworkElement.DefaultFontFamilyName, 20, 400, 0);
        Assert.NotNull(format);
        Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(format.Handle, out var before));
        TextMeasurement.ClearCache();
        Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(format.Handle, out var after));
        Assert.Equal(before.Ascent, after.Ascent); Assert.Equal(before.LineHeight, after.LineHeight);
    }

    [Fact]
    public void ParagraphGeometrySurvivesRepeatedFontCacheClears()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var family = FrameworkElement.DefaultFontFamilyName;
        var spans = new[] { new NativeTextParagraph.Span("Office 中文 ", family, 20, 400, 0, Colors.Black),
            new NativeTextParagraph.Span("italic text", family, 23, 700, 1, Colors.Red) };
        using var expected = NativeTextParagraph.TryCreate(spans, family, 20, 500, 24, TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(expected);
        for (var pass = 0; pass < 8; pass++)
        {
            TextMeasurement.ClearCache();
            using var actual = NativeTextParagraph.TryCreate(spans, family, 20, 500, 24, TextAlignment.Left, FlowDirection.LeftToRight);
            Assert.NotNull(actual); Assert.Equal(expected.Lines[0].Metrics.Line.Width, actual.Lines[0].Metrics.Line.Width);
            for (var index = 0; index <= expected.Text.Length; index++)
                Assert.Equal(expected.Caret(0, index, false).X, actual.Caret(0, index, false).X);
        }
    }

    [Fact]
    public void ANewContextUsesItsOwnFormatsWhileTheOldLeaseFinishes()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var old = TextMeasurement.AcquireParagraphFormat(first, FrameworkElement.DefaultFontFamilyName, 20, 400, 0);
        Assert.NotNull(old); first.Dispose();
        using var second = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        Assert.NotSame(first, second);
        using var fresh = TextMeasurement.AcquireParagraphFormat(second, FrameworkElement.DefaultFontFamilyName, 20, 400, 0);
        Assert.NotNull(fresh); Assert.NotEqual(old.Handle, fresh.Handle);
        Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(old.Handle, out var a));
        Assert.Equal(0, NativeMethods.TextFormatGetFontMetrics(fresh.Handle, out var b));
        Assert.Equal(a.LineHeight, b.LineHeight);
    }
}
