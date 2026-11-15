using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontFaceMetricsTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private readonly CssNativeFontResource _primary = Load("Primary"), _secondary = Load("Secondary");
    private static CssNativeFontResource Load(string name) => CssNativeFontResource.Create(File.ReadAllBytes(CssFontSizeAdjustTests.Fixture(name)))!;
    public void Dispose() { _primary.Dispose(); _secondary.Dispose(); _context.Dispose(); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement owner) { for (int i = 0; i < 3; ++i) { owner.Measure(new(1000, 1000)); owner.Arrange(new(0, 0, 1000, 1000)); owner.UpdateLayout(); } }
    private static CssFontFaceRule Parse(string descriptors) => CssFontFaceRule.Parse("", "font-family:TestFace;src:local(Arial);" + descriptors, 0)!;
    private string Plan(CssFontFaceMetrics first, CssFontFaceMetrics? second = null, bool identical = false) => new CssFontRenderingPlan(0, 0,
        [new(_primary.Family, 400, second is null ? [new(0, 0x10ffff)] : [new('x', 'x')], false, false) { Metrics = first },
         ..second is null ? Array.Empty<CssRenderingFace>() : [new(identical ? _primary.Family : _secondary.Family, 400, [new(0, 0x10ffff)], false, false) { Metrics = second }]]).Encode();
    private NativeTextFormat Format(CssFontFaceMetrics first, CssFontFaceMetrics? second = null, bool identical = false)
        => TextMeasurement.CreateTextFormatFromFamilyList(_context, Plan(first, second, identical), 32, 400, 0);
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0%", 0)] [InlineData("150%", 1.5)] [InlineData("12.5%", .125)] [InlineData("1e3%", 10)]
    [InlineData("calc(50% + 25%)", .75)] [InlineData("calc(-10%)", 0)] [InlineData("min(100%, 125%)", 1)]
    public void SizeAdjustAcceptsPercentagesAndClampsCalculatedNegativeValues(string css, double expected)
    { Assert.Equal(expected, Parse("size-adjust:" + css).Metrics.SizeAdjust, 7); }

    [Theory]
    [InlineData("normal", -1, -1)] [InlineData("0%", 0, -1)] [InlineData("80%", .8, -1)]
    [InlineData("80% 90%", .8, .9)] [InlineData("normal 25%", -1, .25)] [InlineData("20% normal", .2, -1)]
    [InlineData("NORMAL NORMAL", -1, -1)] [InlineData("calc(-1%) calc(200% - 25%)", 0, 1.75)]
    public void OverridesKeepTheSecondAxisIndependentAndDefaultItToNormal(string css, double horizontal, double vertical)
    {
        var m = Parse("ascent-override:" + css + ";descent-override:" + css + ";line-gap-override:" + css).Metrics;
        Assert.Equal(new CssFontFaceMetrics(1, horizontal, horizontal, horizontal, vertical, vertical, vertical), m);
    }

    [Theory]
    [InlineData("")] [InlineData("-10%")] [InlineData("1px")] [InlineData("1")] [InlineData("auto")]
    [InlineData("inherit")] [InlineData("initial")] [InlineData("var(--metric)")] [InlineData("80%!important")]
    [InlineData("normal normal normal")] [InlineData("20%,30%")] [InlineData("20% 30% 40%")]
    public void InvalidDescriptorsPreserveEarlierValidValues(string invalid)
    {
        var m = Parse("size-adjust:150%;ascent-override:80% 90%;descent-override:20%;line-gap-override:5%;" +
            "size-adjust:" + invalid + ";ascent-override:" + invalid + ";descent-override:" + invalid + ";line-gap-override:" + invalid).Metrics;
        Assert.Equal(new CssFontFaceMetrics(1.5, .8, .2, .05, .9), m);
    }
    [Fact]
    public void LastValidDescriptorResetsNormalAndDoesNotBecomeAnElementProperty()
    {
        Assert.Equal(new CssFontFaceMetrics(0), Parse("ascent-override:80% 90%;ascent-override:normal;size-adjust:0%").Metrics);
        Assert.False(CssAuthoring.IsValueSupported("size-adjust", "150%"));
        Assert.False(CssAuthoring.IsValueSupported("ascent-override", "100%"));
    }
    [Fact]
    public void BuilderPreservesLegacyOverloadsAndRejectsInvalidMetricArrays()
    {
        var builder = new CssStyleSheetBuilder(1);
        builder.FontFace("Legacy", ["Arial"], [true], [null], [[]], 400, 400, 0, 100, 100, [0, 0x10ffff], "auto", 0, -1, null);
        var expected = new CssFontFaceMetrics(1.5, .8, .2, .1, .9, .3, 0);
        builder.FontFace("Metrics", ["Arial"], [true], [null], [[]], 400, 400, 0, 100, 100, [0, 0x10ffff], "auto", 0, -1, null, "normal", false, "normal", expected.Values);
        var faces = builder.Build().FontFaces; Assert.Equal(CssFontFaceMetrics.Initial, faces[0].Metrics); Assert.Equal(expected, faces[1].Metrics);
        foreach (double[] invalid in new double[][] { [], [1, -2, 0, 0, 0, 0, 0], [double.NaN, 0, 0, 0, 0, 0, 0], [-1, 0, 0, 0, 0, 0, 0] })
            Assert.Throws<ArgumentException>(() => builder.FontFace("Bad", ["Arial"], [true], [null], [[]], 400, 400, 0, 100, 100, [0, 0x10ffff], "auto", 0, -1, null, "normal", false, "normal", invalid));
    }
    [Fact]
    public void PlansAndPrivateWrappersPreserveIndependentDescriptorsAndOldVersionsDecode()
    {
        var first = new CssFontFaceMetrics(1.5, .8, .2, .1, .9, .3, 0); var second = new CssFontFaceMetrics(2, 0, 0, 0);
        var plan = Plan(first, second); Assert.True(CssFontRenderingPlan.TryDecode(plan, out var actual)); Assert.Equal(first, actual.Faces[0].Metrics); Assert.Equal(second, actual.Faces[1].Metrics);
        var wrapped = FontFeatureRenderingSource.Wrap(_primary.Family, [], CssCapsVariant.SmallCaps, new(FaceMetrics: first, OpticalSizing: 2, SizeAdjust: new(1, .75)));
        wrapped = FontFeatureRenderingSource.WithCapsFragment(wrapped, 3, 1, 2, 1); wrapped = FontWidthRenderingSource.ForFormattedText(wrapped, 3);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(wrapped, out _, out _, out _, out var context)); Assert.Equal(first, context.FaceMetrics); Assert.Equal(new CssFontSizeAdjust(1, .75), context.SizeAdjust);
        for (byte version = 1; version <= 4; ++version)
        {
            using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            { writer.Write(version); writer.Write(0L); writer.Write(0); writer.Write(1); writer.Write("Arial"); writer.Write(400); if (version >= 2) writer.Write(100d); if (version >= 3) writer.Write(0); if (version >= 4) writer.Write(0); writer.Write(false); writer.Write(false); writer.Write(1); writer.Write(0); writer.Write(0x10ffff); }
            Assert.True(CssFontRenderingPlan.TryDecode("\u0002css-font-plan:" + Convert.ToBase64String(stream.ToArray()), out actual)); Assert.Equal(CssFontFaceMetrics.Initial, actual.Faces[0].Metrics);
        }
    }
    [Theory] [InlineData(0)] [InlineData(.5)] [InlineData(1.5)] [InlineData(2)]
    public void DescriptorScalesGlyphsAndFontUnitsWithoutChangingTheComputedSize(double factor)
    {
        using var format = Format(new(factor)); using var expected = _context.CreateTextFormat(_primary.Family, 32, 400, 0);
        Assert.Equal(32 * factor, format.UsedFontSize, 4); Assert.Equal(expected.MeasureText("x0", 1000, 1000).Width * factor, format.MeasureText("x0", 1000, 1000).Width, 4);
        var units = TextMeasurement.GetFontUnitMetrics(Plan(new(factor)), 32); Assert.Equal(16 * factor, units.XHeight, 4); Assert.Equal(19.2 * factor, units.ZeroAdvance, 4);
        Assert.Equal(64, Marshal.SizeOf<NativeFontFaceMetrics>());
    }
    [Theory] [InlineData(.5)] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void PropertyAdjustmentRunsAfterDescriptorAndCancelsTheDescriptorScale(double factor)
    {
        var source = FontFeatureRenderingSource.Wrap(Plan(new(factor)), [], CssCapsVariant.Normal, new(SizeAdjust: new(1, .75)));
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, source, 32, 400, 0); Assert.Equal(48, actual.UsedFontSize, 4);
        using var expected = _context.CreateTextFormat(_primary.Family, 48, 400, 0); Assert.Equal(expected.MeasureText("x", 1000, 1000).Width, actual.MeasureText("x", 1000, 1000).Width, 4);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void DifferentDescriptorsRemainAttachedToEachSelectedFaceEvenForIdenticalGraphicsFonts(bool identical)
    {
        using var actual = Format(new(1.5), new(2), identical); using var first = _context.CreateTextFormat(_primary.Family, 48, 400, 0); using var second = _context.CreateTextFormat(identical ? _primary.Family : _secondary.Family, 64, 400, 0);
        string text = identical ? "x0x0" : "xAxA"; string one = identical ? "0" : "A";
        double x = first.MeasureText("x", 1000, 1000).Width, y = second.MeasureText(one, 1000, 1000).Width;
        Assert.Equal(2 * (x + y), actual.MeasureText(text, 1000, 1000).Width, 3);
        Assert.True(actual.HitTestTextPosition(text, 1000, 1000, 1, false, out var hit)); Assert.Equal(x, hit.CaretX, 3);
    }
    [Theory] [InlineData((byte)0)] [InlineData((byte)1)] [InlineData((byte)2)]
    public void SynthesizedCapitalsRetainTheSelectedFallbackDescriptorAndSourcePositions(byte position)
    {
        var source=FontFeatureRenderingSource.Wrap(Plan(new(1.5,1,.5,.2),new(2,.5,.25,.1)),[],CssCapsVariant.SmallCaps,new(Position:position));
        using var actual=TextMeasurement.CreateTextFormatFromFamilyList(_context,source,32,400,0);
        double scale=position==0?1:.65;using var x=_context.CreateTextFormat(_secondary.Family,(float)(64*.8*scale),400,0);using var a=_context.CreateTextFormat(_secondary.Family,(float)(64*scale),400,0);
        double first=x.MeasureText("X",1000,1000).Width;Assert.Equal(first+a.MeasureText("A",1000,1000).Width,actual.MeasureText("xA",1000,1000).Width,3);
        Assert.True(actual.HitTestTextPosition("xA",1000,1000,1,false,out var caret));Assert.Equal(first,caret.CaretX,3);
    }
    [Theory] [InlineData(.5)] [InlineData(1.5)] [InlineData(2)]
    public void ImplicitSystemFallbackDoesNotInheritThePrimaryDescriptor(double factor)
    {
        using var actual = Format(new(factor)); using var fallback = _context.CreateTextFormat(_primary.Family, 32, 400, 0);
        Assert.Equal(fallback.MeasureText("漢", 1000, 1000).Width, actual.MeasureText("漢", 1000, 1000).Width, 3);
    }
    [Theory] [InlineData(1, -1, 32)] [InlineData(1.5, -1, 48)] [InlineData(2, .75, 48)] [InlineData(.5, .75, 48)]
    public void LineOverridesUseFinalEffectiveSizeAndLeaveGlyphRulersAlone(double factor, double property, double used)
    {
        var descriptors = new CssFontFaceMetrics(factor, 1, .5, .2);
        string source = Plan(descriptors); if (property >= 0) source = FontFeatureRenderingSource.Wrap(source, [], CssCapsVariant.Normal, new(SizeAdjust: new(1, property)));
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, source, 32, 400, 0);
        var metrics = actual.MeasureText("x", 1000, 1000); Assert.Equal(used, metrics.Ascent, 3); Assert.Equal(.5 * used, metrics.Descent, 3); Assert.Equal(.2 * used, metrics.LineGap, 3); Assert.Equal(1.7 * used, metrics.LineHeight, 3);
        var units = TextMeasurement.GetFontUnitMetrics(source, 32); Assert.Equal(.5 * used, units.XHeight, 3); Assert.Equal(.6 * used, units.ZeroAdvance, 3);
    }
    [Fact]
    public void ZeroOverridesHaveZeroLogicalHeightButPreserveInkAndSourceCarets()
    {
        using var actual = Format(new(1, 0, 0, 0)); var metrics = actual.MeasureText("x", 1000, 1000);
        Assert.Equal(0, metrics.LineHeight); Assert.Equal(0, metrics.Ascent); Assert.Equal(0, metrics.Descent);
        Assert.True(actual.TryGetFontInkBounds("x", out var ink)); Assert.True(ink.Height >= 15.9); Assert.True(ink.Top < -15.9);
        Assert.True(actual.HitTestTextPosition("x", 1000, 1000, 1, false, out var caret)); Assert.Equal(19.2, caret.CaretX, 3);
    }
    [Theory] [InlineData("normal", 54.4, 35.2)] [InlineData("1.25", 40, 28)] [InlineData("0", 0, 8)]
    public void CssLineHeightUsesOverridesAndSignedHalfLeading(string lineHeight, double height, double baseline)
    {
        var metrics = TextMeasurement.GetFontMetrics(Plan(new(1, 1, .5, .2)), 32);
        var box = new TextBlock { Text = "x" }; Css.SetStyle(box, "font-size:32px;line-height:" + lineHeight); Evaluate(box);
        var layout = CssComputedLineHeightValue.LayoutBounds(box, 32, metrics); Assert.Equal(height, layout.Height, 3); Assert.Equal(baseline, layout.Baseline, 3);
    }
    [Theory] [InlineData("none")] [InlineData("from-font")] [InlineData(".5")]
    public void LoadedDescriptorParticipatesInFromFontAndRepeatedSheetReplacement(string adjustment)
    {
        var box = new TextBlock { Text = "x", IsTextSelectionEnabled = true };
        foreach (double factor in new[] { 1.5, 2d, 1.5 })
        {
            Css.SetStyleSheet(box, "@font-face{font-family:Adjusted;src:local('" + _primary.Family + "');size-adjust:" + Number(factor * 100) + "%;ascent-override:100%;descent-override:50%;line-gap-override:20%}");
            Css.SetStyle(box, "display:inline-block;width:max-content;font-family:Adjusted;font-size:32px;line-height:normal;font-size-adjust:" + adjustment); Evaluate(box); Layout(box);
#pragma warning disable xUnit1031
            Css.WaitForFontsAsync(box).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            CssFontFaces.FlushNotifications(box.Dispatcher); Evaluate(box); Layout(box); Assert.Empty(Css.GetFontLoadErrors(box));
            using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, CssFontFaces.MaterializeSource(box.FontFamily.GetRenderingSource(box), box.Text), 32, 400, 0);
            double effective = adjustment == "none" || adjustment == "from-font" ? 32 * factor : 32;
            Assert.Equal(effective, actual.UsedFontSize, 3); Assert.Equal(32, box.FontSize); Assert.Equal(effective * .6, box.DesiredSize.Width, 3);
            if (adjustment == "from-font") Assert.Equal(factor * .5, Assert.IsType<CssFontSizeAdjust>(box.GetValue(CssFontSizeAdjust.ValueProperty)).Aspect, 4);
            box.SelectAll(); Assert.Equal("x", box.SelectedText);
        }
        Css.SetStyleSheet(box, ""); Css.SetStyle(box, "font-family:'" + _primary.Family + "';font-size:32px;line-height:normal"); Evaluate(box); Layout(box);
        Assert.False(CssFontFaces.HasMetricOverrides(box.FontFamily.GetRenderingSource(box)));
    }
}
