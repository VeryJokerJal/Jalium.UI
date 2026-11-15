using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontCapsSynthesisTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    private static void Evaluate(DependencyObject owner)
    {
        CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));
        CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);
    }

    [Theory]
    [InlineData("auto", true)] [InlineData("none", true)] [InlineData("AUTO", true)]
    [InlineData("initial", true)] [InlineData("inherit", true)] [InlineData("unset", true)]
    [InlineData("auto none", false)] [InlineData("false", false)] [InlineData("small-caps", false)]
    public void SynthesisSyntax(string value, bool valid)
        => Assert.Equal(valid, Css.Supports("font-synthesis-small-caps", value));

    [Theory]
    [InlineData(1, "aßi", "ASSI")]
    [InlineData(2, "aAßI", "AASSI")]
    [InlineData(3, "aßi", "ASSI")]
    [InlineData(4, "aAßI", "AASSI")]
    [InlineData(5, "ABC", "ABC")]
    public void MissingNativeFeaturesUseFullCaseMappingAndScaledFont(int mode, string source, string expected)
    {
        string family = FontFeatureRenderingSource.Wrap("Arial", [], (CssCapsVariant)mode);
        using var format = TextMeasurement.CreateTextFormatFromFamilyList(_context, family, 32, 400, 0);
        using var reference = _context.CreateTextFormat("Arial", 25.6f, 400, 0);
        Assert.True(format.TryGetFontFeatureSupport([FontFeature.TagOf("smcp"), FontFeature.TagOf("c2sc"), FontFeature.TagOf("pcap"), FontFeature.TagOf("unic")], out var available));
        Assert.All(available, value => Assert.Equal(0, value));
        Assert.Equal(reference.MeasureText(expected, 10000, 1000).Width, format.MeasureText(source, 10000, 1000).Width, 3);
        using var ordinary = _context.CreateTextFormat("Arial", 32, 400, 0);
        Assert.Equal(ordinary.GetFontMetrics().Baseline, format.GetFontMetrics().Baseline);
    }

    [Fact]
    public void ExplicitFeatureOffStillSynthesizesOnlyWhenTheFaceLacksTheFeature()
    {
        var off = new[] { new FontFeature(FontFeature.TagOf("smcp"), 0) };
        using var missing = TextMeasurement.CreateTextFormatFromFamilyList(_context,
            FontFeatureRenderingSource.Wrap("Arial", off, CssCapsVariant.SmallCaps), 32, 400, 0);
        using var small = _context.CreateTextFormat("Arial", 25.6f, 400, 0);
        Assert.Equal(small.MeasureText("ASSI", 10000, 1000).Width, missing.MeasureText("aßi", 10000, 1000).Width, 3);
        using var native = TextMeasurement.CreateTextFormatFromFamilyList(_context,
            FontFeatureRenderingSource.Wrap("Seravek", off, CssCapsVariant.SmallCaps), 32, 400, 0);
        using var plain = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(plain.MeasureText("aßi", 10000, 1000).Width, native.MeasureText("aßi", 10000, 1000).Width, 3);
    }

    [Fact]
    public void ContextSurvivesWidthConversionMaterializationAndFeatureMerges()
    {
        var context = new FontFeatureRenderingSource.CapsContext("tr", true);
        string source = FontFeatureRenderingSource.Wrap(CssFontFaces.BlockPrefix + "Arial", [], CssCapsVariant.SmallCaps, context);
        source = CssFontFaces.MaterializeSource(source, "iı");
        source = FontWidthRenderingSource.ForFormattedText(source, 4);
        source = FontFeatureRenderingSource.Wrap(source, [new(FontFeature.TagOf("kern"), 0)]);
        Assert.True(CssFontFaces.IsBlocked(source));
        Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family, out var caps, out var decoded));
        Assert.Equal(context, decoded); Assert.Equal(CssCapsVariant.SmallCaps, caps); Assert.Single(features);
        Assert.True(FontWidthRenderingSource.TryUnwrap(family, out var width, out family));
        Assert.Equal(87.5, width); Assert.Equal("Arial", family);
    }

    [Fact]
    public void LanguageAndSynthesisChangesRebuildRenderingIdentityWithoutChangingSource()
    {
        var parent = new StackPanel(); var child = new TextBlock { Text = "iıß", FontFamily = "Arial" }; parent.Children.Add(child);
        Css.SetStyle(parent, "font-variant-caps:small-caps;font-synthesis-small-caps:none"); Evaluate(parent);
        parent.Language = XmlLanguage.GetLanguage("tr");
        string blocked = child.FontFamily.GetRenderingSource(child);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(blocked, out _, out _, out var caps, out var context));
        Assert.Equal(CssCapsVariant.SmallCaps, caps); Assert.True(context.DisableSynthesis); Assert.Equal("tr", context.Language);
        using var disabled = TextMeasurement.CreateTextFormatFromFamilyList(_context, blocked, 32, 400, 0);
        using var normal = _context.CreateTextFormat("Arial", 32, 400, 0);
        Assert.Equal(normal.MeasureText(child.Text!, 10000, 1000).Width, disabled.MeasureText(child.Text!, 10000, 1000).Width, 3);
        Css.SetStyle(parent, "font-variant-caps:small-caps;font-synthesis-small-caps:auto"); Evaluate(parent);
        string automatic = child.FontFamily.GetRenderingSource(child); Assert.NotEqual(blocked, automatic);
        using var enabled = TextMeasurement.CreateTextFormatFromFamilyList(_context, automatic, 32, 400, 0);
        using var reference = _context.CreateTextFormat("Arial", 25.6f, 400, 0);
        Assert.Equal(reference.MeasureText("İISS", 10000, 1000).Width, enabled.MeasureText(child.Text!, 10000, 1000).Width, 3);
        var node = CssNode.Get(child); var lengths = CssEngine.BuildLengthContext(node);
        Assert.True(CssOrdinaryStyleQuery.Matches("font-synthesis-small-caps", "auto", node, lengths));
        Assert.False(CssOrdinaryStyleQuery.Matches("font-synthesis-small-caps", "none", node, lengths));
        Assert.Equal("iıß", child.Text); Assert.Equal("Arial", child.FontFamily.Source);
    }

    [Fact]
    public void CaselessTextRemainsFullSize()
    {
        using var caps = TextMeasurement.CreateTextFormatFromFamilyList(_context,
            FontFeatureRenderingSource.Wrap("Arial", [], CssCapsVariant.AllSmallCaps), 32, 400, 0);
        using var normal = _context.CreateTextFormat("Arial", 32, 400, 0);
        const string source = "中文 😀 0123";
        Assert.Equal(normal.MeasureText(source, 10000, 1000).Width, caps.MeasureText(source, 10000, 1000).Width, 3);
    }

    [Fact]
    public void UnicaseCanUseRealSmallCapsWithoutOverridingElementFeatureSettings()
    {
        using var reference = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.True(reference.TryGetFontFeatureSupport([FontFeature.TagOf("smcp"), FontFeature.TagOf("unic")], out var available));
        Assert.Equal(new byte[] { 1, 0 }, available);
        foreach (bool disabled in new[] { false, true })
        {
            var overrides = disabled ? new[] { new FontFeature(FontFeature.TagOf("smcp"), 0) } : [];
            using var format = TextMeasurement.CreateTextFormatFromFamilyList(_context,
                FontFeatureRenderingSource.Wrap("Seravek", overrides, CssCapsVariant.Unicase), 32, 400, 0);
            reference.SetFontFeatures([new(FontFeature.TagOf("smcp"), disabled ? 0u : 1u)]);
            Assert.Equal(reference.MeasureText("abc", 10000, 1000).Width, format.MeasureText("ABC", 10000, 1000).Width, 3);
            var unicodeFeature = new FontFeature(FontFeature.TagOf("unic"), 1);
            string upperFamily = FontFeatureRenderingSource.Wrap("Seravek", [unicodeFeature, new(FontFeature.TagOf("smcp"), disabled ? 0u : 1u)]);
            string lowerFamily = FontFeatureRenderingSource.Wrap("Seravek", disabled ? [unicodeFeature, new(FontFeature.TagOf("smcp"), 0)] : [unicodeFeature]);
            var spans = new[] { new NativeTextParagraph.Span("abc", upperFamily, 32, 400, 0, Colors.Black),
                new NativeTextParagraph.Span("xyz", lowerFamily, 32, 400, 0, Colors.Black) };
            var units = Enumerable.Range(0, 6).Select(i => new NativeMethods.ParagraphSpacingUnit
                { TextPosition = (uint)i, Length = 1 }).ToArray();
            using var mixed = NativeTextParagraph.TryCreateContextSpaced(spans, "Seravek", 32, 10000, 44, FlowDirection.LeftToRight, units);
            Assert.NotNull(mixed);
            Assert.Equal(mixed.Lines[0].Metrics.Line.Width, format.MeasureText("ABCxyz", 10000, 1000).Width, 3);
        }
    }

    [Theory]
    [InlineData("1:2:tr::Arial")] [InlineData("1:1:en_US::Arial")]
    [InlineData("1:1:::Arial")] [InlineData("7:1:tr::Arial")] [InlineData("1:1:tr:garbage:Arial")]
    public void MalformedContextsDoNotPublishPartialValues(string value)
    {
        string input = "\u0004font-features-caps-context:" + value;
        Assert.False(FontFeatureRenderingSource.TryUnwrap(input, out var features, out var family, out var caps, out var context));
        Assert.Empty(features); Assert.Equal(input, family); Assert.Equal(CssCapsVariant.Normal, caps); Assert.Equal(default, context);
    }
}
