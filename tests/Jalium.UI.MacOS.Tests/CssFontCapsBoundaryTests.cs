using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontCapsBoundaryTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    private const string Style = "font-family:Seravek;font-size:32px;line-height:44px;white-space:normal;word-break:break-all;overflow-wrap:anywhere;text-autospace:no-autospace;";

    [Fact]
    public void BoundaryIdentityIsBoundedAndSurvivesRenderingConversions()
    {
        const string old = "\u0004font-features-caps:5::Seravek";
        Assert.Equal(old, FontFeatureRenderingSource.Wrap("Seravek", [], CssCapsVariant.Unicase));
        for (byte flags = 0; flags < 64; ++flags)
        {
            var context = new FontFeatureRenderingSource.CapsContext("el", true, flags);
            string source = FontFeatureRenderingSource.Wrap(CssFontFaces.BlockPrefix + "Seravek", [], CssCapsVariant.Unicase, context);
            source = CssFontFaces.MaterializeSource(source, "Σ");
            source = FontWidthRenderingSource.ForFormattedText(source, 4);
            source = FontFeatureRenderingSource.Wrap(source, [new(FontFeature.TagOf("kern"), 0)]);
            Assert.True(CssFontFaces.IsBlocked(source));
            Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family, out var caps, out var decoded));
            Assert.Equal(context, decoded); Assert.Equal(CssCapsVariant.Unicase, caps); Assert.Single(features);
            Assert.True(FontWidthRenderingSource.TryUnwrap(family, out var width, out family));
            Assert.Equal(87.5, width); Assert.Equal("Seravek", family);
        }
        Assert.Equal(old, FontFeatureRenderingSource.WithCapsBoundaries(FontFeatureRenderingSource.WithCapsBoundaries(old, 3), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFeatureRenderingSource.WithCapsBoundaries(old, 64));
    }

    [Theory]
    [InlineData("64")] [InlineData("-1")] [InlineData("+1")] [InlineData("256")] [InlineData("")] [InlineData(" 1")]
    public void MalformedBoundaryIdentityFailsTransactionally(string flags)
    {
        string source = "\u0004font-features-caps-boundaries:5:1:el:" + flags + ":736D6370=0:Seravek";
        Assert.False(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family, out var caps, out var context));
        Assert.Empty(features); Assert.Equal(source, family); Assert.Equal(CssCapsVariant.Normal, caps); Assert.Equal(default, context);
    }

    [Theory]
    [InlineData("Ο", "ΣΑ", "Οσα")]
    [InlineData("Ο", "Σ", "Ος")]
    [InlineData("Ο'", "ΣΑ", "Ο'σα")]
    [InlineData("Ο", "Σ'Α", "Οσ'α")]
    [InlineData("Ο\u0301", "ΣΑ", "Ο\u0301σα")]
    [InlineData("Ο\n", "ΣΑ", "Ο\nσα")]
    public void MixedRunWrapAndResizeKeepOriginalParagraphContext(string prefix, string source, string expected)
    {
        var actual = new TextBlock(); var reference = new TextBlock { Text = expected };
        var first = new Run(prefix); var last = new Run(source); actual.Inlines.Add(first); actual.Inlines.Add(last);
        var firstParent = first.Parent; var lastParent = last.Parent;
        Css.SetStyle(actual, Style); Css.SetStyle(reference, Style);
        Css.SetStyle(last, "font-variant-caps:unicase;font-feature-settings:'smcp' off");
        foreach (int width in new[] { 24, 28, 32, 36, 40, 44, 48, 200, 40, 24 })
        {
            Layout(actual, width); Layout(reference, width);
            Assert.Equal(Lengths(reference), Lengths(actual));
            if (prefix == "Ο" && source == "ΣΑ" && width == 40) Assert.Equal(new[] { 1, 2 }, Lengths(actual));
            Assert.Equal(reference.DesiredSize.Height, actual.DesiredSize.Height, 3);
            actual.SelectAll(); Assert.Equal(prefix + source, actual.SelectedText);
        }
        Assert.Equal(prefix, first.Text); Assert.Equal(source, last.Text);
        Assert.Same(firstParent, first.Parent); Assert.Same(lastParent, last.Parent);
        Assert.Same(first, actual.Inlines[0]); Assert.Same(last, actual.Inlines[1]);
        Css.SetStyle(last, "font-variant-caps:normal"); reference.Text = prefix + source;
        Layout(actual, 40); Layout(reference, 40); Assert.Equal(Lengths(reference), Lengths(actual));
    }

    [Theory]
    [InlineData("ΟΣΑ", "οσα")]
    [InlineData("ΟΣ", "ος")]
    [InlineData("Ο'ΣΑ", "ο'σα")]
    [InlineData("ΟΣ\nΑ", "ος\nα")]
    public void PlainTextWrapKeepsContextWithAndWithoutSpacing(string source, string expected)
    {
        foreach (string spacing in new[] { "", "letter-spacing:1px;word-spacing:2px;" })
        {
            var actual = new TextBlock { Text = source }; var reference = new TextBlock { Text = expected };
            Css.SetStyle(actual, Style + spacing + "font-variant-caps:unicase;font-feature-settings:'smcp' off");
            Css.SetStyle(reference, Style + spacing);
            foreach (int width in new[] { 24, 28, 32, 36, 40, 44, 48, 200, 40 })
            {
                Layout(actual, width); Layout(reference, width);
                Assert.Equal(Lengths(reference), Lengths(actual));
                actual.SelectAll(); Assert.Equal(source, actual.SelectedText);
            }
            Assert.Equal(source, actual.Text);
        }
    }

    private static void Layout(TextBlock text, int width)
    {
        for (int i = 0; i < 3; ++i)
        { text.Measure(new(width, 1000)); text.Arrange(new(0, 0, width, 1000)); text.UpdateLayout(); }
    }
    private static int[] Lengths(TextBlock text) => ((IEnumerable)typeof(TextBlock)
        .GetField("_layoutLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(text)!)
        .Cast<object>().Select(line => (int)line.GetType().GetProperty("Length")!.GetValue(line)!).ToArray();
}
