using Jalium.UI.Interop;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontCapsUnicodeTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("ΟΣ", "ος", "el")]
    [InlineData("ΟΣΑ", "οσα", "el")]
    [InlineData("Ο'Σ", "ο'ς", "el")]
    [InlineData("ΟΣ'Α", "οσ'α", "el")]
    [InlineData("ΟΣ\nΑ", "ος\nα", "el")]
    [InlineData("I\u0307", "i", "tr")]
    [InlineData("I\u0323\u0307", "i\u0323", "az-Latn")]
    public void UnicaseUsesFullSourceContextAndRetainsSourceIndices(string source, string expected, string language)
    {
        FontFeature[] features = [new(FontFeature.TagOf("smcp"), 0)];
        string family = FontFeatureRenderingSource.Wrap("Seravek", features, CssCapsVariant.Unicase, new(language));
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, family, 32, 400, 0);
        using var reference = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(reference.MeasureText(expected, 10000, 1000).Width, actual.MeasureText(source, 10000, 1000).Width, 3);
        var spans = new[] { new NativeTextParagraph.Span(source, family, 32, 400, 0, Colors.Black) };
        using var paragraph = NativeTextParagraph.TryCreate(spans, "Seravek", 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(paragraph);
        Assert.Equal(source, paragraph.Text);
        Assert.Equal(0u, paragraph.Lines[0].Metrics.Line.TextPosition);
        var last = paragraph.Lines[^1].Metrics.Line;
        Assert.Equal((uint)source.Length, last.TextPosition + last.Length);
    }

    [Theory]
    [InlineData("Ο", "Σ", "ς")]
    [InlineData("ΟΣ'", "Α", "α")]
    [InlineData("Ο", "ΣΑ", "σα")]
    public void ContextCrossesRunsWithDifferentCapsAndPaint(string prefix, string source, string expected)
    {
        FontFeature[] settings = [new(FontFeature.TagOf("smcp"), 0), new(FontFeature.TagOf("unic"), 1)];
        string plain = FontFeatureRenderingSource.Wrap("Seravek", settings);
        string caps = FontFeatureRenderingSource.Wrap("Seravek", settings, CssCapsVariant.Unicase, new("el"));
        var actualSpans = new[] { new NativeTextParagraph.Span(prefix, plain, 32, 400, 0, Colors.Black),
            new NativeTextParagraph.Span(source, caps, 32, 400, 0, Colors.Red) };
        var expectedSpans = new[] { new NativeTextParagraph.Span(prefix, plain, 32, 400, 0, Colors.Black),
            new NativeTextParagraph.Span(expected, plain, 32, 400, 0, Colors.Red) };
        using var actual = NativeTextParagraph.TryCreate(actualSpans, "Seravek", 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
        using var reference = NativeTextParagraph.TryCreate(expectedSpans, "Seravek", 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(actual); Assert.NotNull(reference);
        Assert.Equal(reference.Lines[0].Metrics.Line.Width, actual.Lines[0].Metrics.Line.Width, 3);
        Assert.Equal(prefix + source, actual.Text);
    }

    [Theory]
    [InlineData("Ο", "Σ", "Ος")]
    [InlineData("Ο", "ΣΑ", "Οσα")]
    public void OrdinaryTextBlockSharesCapsContextAcrossRuns(string prefix, string source, string expected)
    {
        var text = new TextBlock(); var reference = new TextBlock { Text = expected };
        Css.SetStyle(text, "font-family:Seravek;font-size:32px");
        Css.SetStyle(reference, "font-family:Seravek;font-size:32px");
        var first = new Run(prefix); var second = new Run(source); text.Inlines.Add(first); text.Inlines.Add(second);
        var firstParent = first.Parent; var secondParent = second.Parent;
        Css.SetStyle(second, "font-variant-caps:unicase;font-feature-settings:'smcp' off");
        Layout(text); Layout(reference);
        Assert.Equal(reference.DesiredSize.Width, text.DesiredSize.Width, 3);
        text.SelectAll(); Assert.Equal(prefix + source, text.SelectedText);
        Assert.Equal(prefix, first.Text); Assert.Equal(source, second.Text);
        Assert.Same(firstParent, first.Parent); Assert.Same(secondParent, second.Parent);
        Assert.Same(first, text.Inlines[0]); Assert.Same(second, text.Inlines[1]);
        Css.SetStyle(second, "font-variant-caps:normal"); reference.Text = prefix + source;
        Layout(text); Layout(reference);
        Assert.Equal(reference.DesiredSize.Width, text.DesiredSize.Width, 3);
    }

    private static void Layout(TextBlock text)
    {
        for (int i = 0; i < 3; ++i) {
            text.Measure(new(10000, 1000)); text.Arrange(new(0, 0, 10000, 1000)); text.UpdateLayout();
        }
    }
}
