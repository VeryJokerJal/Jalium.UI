using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontCapsFallbackTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void CapsIdentitySurvivesWidthConversionAndFontMaterialization(int mode)
    {
        var features = new[] { new FontFeature(FontFeature.TagOf("smcp"), 0) };
        string source = FontFeatureRenderingSource.Wrap(CssFontFaces.BlockPrefix + "Seravek", features, (CssCapsVariant)mode);
        source = CssFontFaces.MaterializeSource(source, "Aaß");
        source = FontWidthRenderingSource.ForFormattedText(source, 4);
        Assert.True(CssFontFaces.IsBlocked(source));
        Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out var decoded, out var family, out var caps));
        Assert.Equal((CssCapsVariant)mode, caps); Assert.Equal(features, decoded);
        Assert.True(FontWidthRenderingSource.TryUnwrap(family, out var width, out family));
        Assert.Equal(87.5, width); Assert.Equal("Seravek", family);
        source = FontFeatureRenderingSource.Wrap(source, [new(FontFeature.TagOf("kern"), 0)]);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out decoded, out _, out caps));
        Assert.Equal((CssCapsVariant)mode, caps); Assert.Equal(2, decoded.Length);
    }

    [Fact]
    public void AnEmptyFeatureListStillRetainsTheComputedCapsRequest()
    {
        string source = FontFeatureRenderingSource.Wrap("Seravek", [], CssCapsVariant.PetiteCaps);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family, out var caps));
        Assert.Empty(features); Assert.Equal("Seravek", family); Assert.Equal(CssCapsVariant.PetiteCaps, caps);
        var legacy = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("tnum"), 1)]);
        Assert.Equal("\u0004font-features:746E756D=1:Seravek", legacy);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(legacy, out features, out family, out caps));
        Assert.Equal(CssCapsVariant.Normal, caps);
    }

    [Theory]
    [InlineData("0::Seravek")] [InlineData("7::Seravek")] [InlineData("-1::Seravek")]
    [InlineData("1.5::Seravek")] [InlineData("1")] [InlineData("::Seravek")]
    [InlineData("3:00000000=1:Seravek")] [InlineData("3:736D6370=-1:Seravek")]
    [InlineData("3:736D6370=4294967296:Seravek")] [InlineData("3:garbage:Seravek")]
    public void MalformedCapsIdentitiesDoNotPublishPartialDecodedValues(string suffix)
    {
        string input = "\u0004font-features-caps:" + suffix;
        Assert.False(FontFeatureRenderingSource.TryUnwrap(input, out var features, out var family, out var caps));
        Assert.Empty(features); Assert.Equal(input, family); Assert.Equal(CssCapsVariant.Normal, caps);
    }

    [Theory]
    [InlineData("original", true, true)] [InlineData("no-petite", false, false)]
    [InlineData("lower-petite-only", true, false)] [InlineData("upper-petite-only", false, true)]
    public void AvailabilityComesFromTheSelectedFontAndDoesNotChangeWithFeatureSettings(
        string fixture, bool lower, bool upper)
    {
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes(Fixture(fixture)))!;
        Assert.NotNull(resource);
        using var format = _context.CreateTextFormat(resource.Family, 32, 400, 0, 100);
        uint[] tags = [FontFeature.TagOf("pcap"), FontFeature.TagOf("c2pc"), FontFeature.TagOf("smcp"), FontFeature.TagOf("c2sc"), FontFeature.TagOf("XXXX"), FontFeature.TagOf("pcap")];
        Assert.True(format.TryGetFontFeatureSupport(tags, out var before));
        Assert.Equal(new byte[] { lower ? (byte)1 : (byte)0, upper ? (byte)1 : (byte)0, 1, 1, 0, lower ? (byte)1 : (byte)0 }, before);
        Assert.True(format.TryGetFontFeatureSupport(tags, out var positioning, 2)); Assert.All(positioning, x => Assert.Equal(0, x));
        Assert.Equal(JaliumResult.Ok, format.SetFontFeatures([new(FontFeature.TagOf("pcap"), 0), new(FontFeature.TagOf("c2pc"), 1)]));
        format.MeasureText("J", 1000, 1000);
        Assert.True(format.TryGetFontFeatureSupport(tags, out var after)); Assert.Equal(before, after);
    }

    [Fact]
    public void RenderingDefersCapsButPreservesExplicitLowLevelOverridesAndComputedQueries()
    {
        var box = new TextBlock { Text = "J", FontFamily = "Seravek" };
        Css.SetStyle(box, "font-variant-caps:petite-caps;font-feature-settings:'smcp' off;font-variant-numeric:tabular-nums");
        CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(box)); CssEvaluationScheduler.FlushIfPending(box.Dispatcher);
        string encoded = box.FontFamily.GetRenderingSource(box);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(encoded, out var features, out _, out var caps));
        Assert.Equal(CssCapsVariant.PetiteCaps, caps);
        Assert.Equal(new[] { new FontFeature(FontFeature.TagOf("smcp"), 0), new FontFeature(FontFeature.TagOf("tnum"), 1) }, features);
        using var format = TextMeasurement.CreateTextFormatFromFamilyList(_context, encoded, 32, 400, 0);
        format.MeasureText("J", 1000, 1000);
        var node = CssNode.Get(box); var lengths = CssEngine.BuildLengthContext(node);
        Assert.True(CssOrdinaryStyleQuery.Matches("font-variant-caps", "petite-caps", node, lengths));
        Assert.False(CssOrdinaryStyleQuery.Matches("font-variant-caps", "small-caps", node, lengths));
        Assert.Equal("J", box.Text); Assert.Equal("Seravek", box.FontFamily.Source);
    }

    [Fact]
    public unsafe void NativeQueriesValidateBuffersCountsTagsAndTableMasksBeforeWriting()
    {
        using var format = _context.CreateTextFormat("Seravek", 32, 400, 0);
        var handle = format.Handle;
        try
        {
            uint tag = FontFeature.TagOf("pcap"); byte output = 123;
            Assert.Equal(0, NativeMethods.TextFormatGetFontFeatureSupport(handle, 1, null, 0, null));
            foreach (uint tables in new[] { 0u, 4u, uint.MaxValue })
                Assert.Equal((int)JaliumResult.InvalidArgument, NativeMethods.TextFormatGetFontFeatureSupport(handle, tables, &tag, 1, &output));
            Assert.Equal((int)JaliumResult.InvalidArgument, NativeMethods.TextFormatGetFontFeatureSupport(0, 1, &tag, 1, &output));
            Assert.Equal((int)JaliumResult.InvalidArgument, NativeMethods.TextFormatGetFontFeatureSupport(handle, 1, null, 1, &output));
            Assert.Equal((int)JaliumResult.InvalidArgument, NativeMethods.TextFormatGetFontFeatureSupport(handle, 1, &tag, 1, null));
            Assert.Equal((int)JaliumResult.InvalidArgument, NativeMethods.TextFormatGetFontFeatureSupport(handle, 1, &tag, 16385, &output));
            tag = 0;
            Assert.Equal((int)JaliumResult.InvalidArgument, NativeMethods.TextFormatGetFontFeatureSupport(handle, 1, &tag, 1, &output));
            Assert.Equal(123, output);
        }
        finally { GC.KeepAlive(format); }
    }

    internal static string Fixture(string name)
    {
        return MacOSTestAssets.PathTo(name == "original"
            ? "FontNumeric/FontWithFancyFeatures.otf" : "FontCapsFallback/" + name + ".otf");
    }
}
