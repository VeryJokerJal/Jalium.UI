using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontVariationSettingsTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private readonly CssNativeFontResource _font = CssNativeFontResource.Create(File.ReadAllBytes(Fixture))!;
    internal static string Fixture => MacOSTestAssets.PathTo("FontVariations/variabletest_box.ttf");
    public void Dispose() { _font.Dispose(); _context.Dispose(); }
    private static DependencyProperty Property => CssFontVariationSettings.SettingsProperty;
    private static CssFontVariationSettings Value(DependencyObject owner) => (CssFontVariationSettings)owner.GetValue(Property)!;
    private static CssFontVariationSettings Parse(string value) { var reader = new CssTokenReader(value); return CssFontVariationSettings.Read(ref reader)!; }
    private static void Style(DependencyObject owner, string css) { Css.SetStyle(owner, css); Evaluate(owner); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private TextBlock Box(string text = "AAABBBMMM") => new() { Text = text, FontFamily = _font.Family, FontSize = 32, IsTextSelectionEnabled = true };
    private static void Layout(FrameworkElement element, int width = 200) { for (int i = 0; i < 3; i++) { element.Measure(new(width, 400)); element.Arrange(new(0, 0, width, 400)); element.UpdateLayout(); } }

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("'UPWD' -4.25", "\"UPWD\" -4.25")]
    [InlineData("'wght' +650.25", "\"wght\" 650.25")]
    [InlineData("'wdth' 8.75e1", "\"wdth\" 87.5")]
    [InlineData("'slnt' calc(-4 * 3)", "\"slnt\" -12")]
    [InlineData("'wght' min(300, 600)", "\"wght\" 300")]
    [InlineData("'UPWD' 350,'UPWD' 75.5", "\"UPWD\" 75.5")]
    [InlineData("'wght' 300,'ABCD' 2,'abcd' 3", "\"ABCD\" 2, \"abcd\" 3, \"wght\" 300")]
    [InlineData("'    ' -0", "\"    \" 0")]
    [InlineData("'\\55 PWD'/**/175", "\"UPWD\" 175")]
    public void ComputedValueKeepsUnclampedNumbersCaseAndLastDuplicate(string css, string expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variation-settings", css));
        var box = Box(); Style(box, "font-variation-settings:" + css);
        Assert.Equal(expected, Value(box).ToCss());
    }

    [Theory]
    [InlineData("")] [InlineData("'wght'")] [InlineData("wght 500")] [InlineData("'abc' 1")]
    [InlineData("'abcde' 1")] [InlineData("'ab中d' 1")] [InlineData("'a\\7f bc' 1")]
    [InlineData("'wght' on")] [InlineData("'wght' 100px")] [InlineData("'wght' 50%")]
    [InlineData("normal,'wght' 1")] [InlineData("'wght' 1,")] [InlineData(",'wght' 1")]
    [InlineData("'wght' 1 2")] [InlineData("'wght' 1,, 'wdth' 2")]
    public void InvalidMapPreservesTheEarlierDeclaration(string css)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variation-settings", css));
        var box = Box(); Style(box, "font-variation-settings:'UPWD' 123;font-variation-settings:" + css);
        Assert.Equal(Parse("'UPWD' 123"), Value(box));
    }

    [Theory]
    [InlineData("inherit", "'UPWD' 123")] [InlineData("unset", "'UPWD' 123")]
    [InlineData("normal", "normal")] [InlineData("initial", "normal")]
    [InlineData("all:initial", "normal")] [InlineData("all:unset", "'UPWD' 123")]
    [InlineData("font:32px serif", "normal")] [InlineData("font-variant:small-caps", "'UPWD' 123")]
    public void InheritanceAndShorthandResetsReplaceTheWholeMap(string css, string expected)
    {
        var child = Box(); var root = new Border { Child = child }; Style(root, "font-variation-settings:'UPWD' 123");
        Style(child, css.Contains(':') ? css : "font-variation-settings:" + css); Evaluate(root);
        Assert.Equal(Parse(expected), Value(child));
        Style(root, "font-variation-settings:'UPWD' 234"); Evaluate(root);
        if (css is "inherit" or "unset" or "all:unset" or "font-variant:small-caps") Assert.Equal(Parse("'UPWD' 234"), Value(child));
    }

    [Fact]
    public void VariableImportantAndStateRemovalUpdateTheRenderingIdentity()
    {
        var box = Box(); Style(box, "--axes:'UPWD' 240;font-variation-settings:var(--axes)!important;font-variation-settings:normal");
        Assert.Equal(Parse("'UPWD' 240"), Value(box)); var before = box.FontFamily.GetRenderingSource(box);
        Style(box, "font-variation-settings:normal"); Assert.NotEqual(before, box.FontFamily.GetRenderingSource(box));
        Style(box, ""); Assert.Equal(CssFontVariationSettings.Normal, Value(box));
    }

    [Fact]
    public void StyleQueriesCompareComputedCoordinatesBeforePerFontClamping()
    {
        var root = new Border(); var child = Box(); root.Child = child;
        Css.SetStyleSheet(root, "@container style(font-variation-settings:'UPWD' 600,'wght' 500){TextBlock{opacity:.3}}");
        Style(root, "font-variation-settings:'wght' 500,'UPWD' 1,'UPWD' 600"); Evaluate(root); Assert.Equal(.3, child.Opacity, 6);
        Style(root, "font-variation-settings:'UPWD' 350,'wght' 500"); Evaluate(root); Assert.Equal(1, child.Opacity, 6);
    }

    [Fact]
    public void EncodingSurvivesWidthsCapsFragmentsEmojiAndFacePlans()
    {
        var box = Box(); Style(box, "font-variation-settings:'UPWD' 175.25;font-variant-caps:unicase;font-variant-position:super;font-variant-emoji:text;font-width:87.5%;font-feature-settings:'kern' 0");
        string key = box.FontFamily.GetRenderingSource(box);
        key = FontFeatureRenderingSource.WithCapsFragment(key, 7, 2, 1, 1);
        key = FontWidthRenderingSource.ForFormattedText(key, FontStretches.Expanded.ToOpenTypeStretch());
        Assert.True(FontFeatureRenderingSource.TryUnwrap(key, out var features, out var family, out var caps, out var context));
        Assert.Equal(Value(box).Axes, context.Variations); Assert.Equal(CssCapsVariant.Unicase, caps);
        Assert.Equal((byte)1, context.PositionRun); Assert.Equal((byte)1, context.EmojiAfter); Assert.NotEmpty(features);
        Assert.True(FontWidthRenderingSource.TryUnwrap(family, out _, out family)); Assert.Equal(_font.Family, family);
        var plan = new CssFontRenderingPlan(0, 0, [new(_font.Family, 400, [new(0, 0x10ffff)], false, false) { Variations = Value(box).Axes }]);
        Assert.True(CssFontRenderingPlan.TryDecode(plan.Encode(), out var decoded)); Assert.Equal(plan.Faces[0].Variations, decoded.Faces[0].Variations);
    }

    [Fact]
    public void TransitionAndKeyframesInterpolateMatchingTagsAndUseDiscreteForDifferentMaps()
    {
        var box = Box(); Style(box, "transition:font-variation-settings 1s linear .2s");
        var a = Parse("'UPWD' 50,'wght' 300"); var b = Parse("'wght' 700,'UPWD' 350");
        var data = ((CssTransitionData)box.GetValue(CssTransitions.DataProperty)!).Find(box, Property)!;
        var animation = Assert.IsType<CssFontVariationAnimation>(CssTransitions.Create(box, Property, a, b, data)); var clock = animation.CreateClock();
        foreach (double progress in new[] { 0, .25, .5, .75, 1, .1 }) { clock.SetProgressForSampling(progress); Assert.Equal(a.Interpolate(b, progress), animation.GetCurrentValue(a, b, clock)); }
        Assert.Null(CssTransitions.Create(box, Property, a, CssFontVariationSettings.Normal, data));
        Assert.NotNull(CssTransitions.Create(box, Property, a, CssFontVariationSettings.Normal, data with { AllowDiscrete = true }));
        Css.SetStyleSheet(box, "@keyframes axes{from{font-variation-settings:'UPWD' 50}to{font-variation-settings:'UPWD' 350}}"); Evaluate(box);
        var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "axes")!);
        var sampler = new CssPropertySampler(Property, tracks[Property]); Assert.Equal(Parse("'UPWD' 125"), sampler.Sample(.25, new("linear")));
    }

    [Theory] [InlineData(-100)] [InlineData(0)] [InlineData(75.5)] [InlineData(175)] [InlineData(350)] [InlineData(600)]
    public void DirectParagraphAndInlineWidthsUseTheSameCoordinates(double coordinate)
    {
        var box = Box(); Style(box, "font-variation-settings:'UPWD' " + coordinate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var direct = TextMeasurement.CreateTextFormatFromFamilyList(_context, box.FontFamily.GetRenderingSource(box), 32, 400, 0);
        using var expected = _context.CreateTextFormat(_font.Family, 32, 400, 0);
        Assert.True(expected.TrySetFontVariations([new(FontFeature.TagOf("UPWD"), Math.Clamp(coordinate, 0, 350))]));
        double width = expected.MeasureText(box.Text, 100000, 1000).Width;
        Assert.Equal(width, direct.MeasureText(box.Text, 100000, 1000).Width, 3);
        using var paragraph = NativeTextParagraph.TryCreate([new(box.Text, box.FontFamily.GetRenderingSource(box), 32, 400, 0, Colors.Black)], _font.Family, 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.Equal(width, paragraph!.Lines[0].Metrics.Line.Width, 3);
        var run = new Run(box.Text); box.Text = ""; box.Inlines.Add(new Span(run)); Layout(box, 80); box.SelectAll(); Assert.Equal(run.Text, box.SelectedText);
        var before = box.DesiredSize; Layout(box, 200); Layout(box, 80); Assert.Equal(before, box.DesiredSize); Assert.Same(box.Inlines[0], run.Parent);
    }

    [Fact]
    public void RegisteredFaceDescriptorsRemainBelowPropertyAndNormalKeepsFaceDefaults()
    {
        string data = Convert.ToBase64String(File.ReadAllBytes(Fixture)); var box = new TextBlock { Text = "AABBAA", FontSize = 32 };
        Css.SetStyleSheet(box, "@font-face{font-family:VariationFace;src:url(data:font/ttf;base64," + data + ");font-variation-settings:'UPWD' 250;font-variation-settings:'UPWD' on}");
        foreach (string property in new[] { "normal", "'UPWD' 50", "'UPWD' 300,'FVTT' 5" }) {
            Style(box, "font-family:VariationFace;font-variation-settings:" + property); Layout(box, 300); Css.WaitForFontsAsync(box).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); Evaluate(box);
            using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, CssFontFaces.MaterializeSource(box.FontFamily.GetRenderingSource(box), box.Text), 32, 400, 0);
            using var expected = _context.CreateTextFormat(_font.Family, 32, 400, 0); Assert.True(expected.TrySetFontVariations(CssFontVariationSettings.Merge(Parse("'UPWD' 250").Axes, Parse(property).Axes)));
            Assert.Equal(expected.MeasureText(box.Text, 10000, 1000).Width, actual.MeasureText(box.Text, 10000, 1000).Width, 3); Assert.Empty(Css.GetFontLoadErrors(box));
        }
    }

    [Fact]
    public void DescriptorAndPropertyCapacityComposeWithoutLosingCoordinates()
    {
        FontVariation[] Layer(int offset) => Enumerable.Range(offset, 4096).Select(i => new FontVariation(FontFeature.TagOf(
            "Q" + (char)('A' + i / (26 * 26)) + (char)('A' + i / 26 % 26) + (char)('A' + i % 26)), i)).ToArray();
        var first = new CssFontVariationSettings(Layer(0)); var last = new CssFontVariationSettings(Layer(4096));
        Assert.Equal(first, Parse(first.ToCss())); Assert.Equal(last, Parse(last.ToCss()));
        var composed = CssFontVariationSettings.Merge(first.Axes, last.Axes); Assert.Equal(8192, composed.Length);
        using var format = _context.CreateTextFormat(_font.Family, 32, 400, 0); Assert.True(format.TrySetFontVariations(composed));
        var tooMany = new CssFontVariationSettings(Layer(0).Append(last.Axes[0]).ToArray()); Assert.Null(Parse(tooMany.ToCss()));
    }

    [Fact]
    public void InterpolationHandlesLargeCoordinatesAndEasingOvershoot()
    {
        var from = Parse("'TEST' -1e308"); var to = Parse("'TEST' 1e308");
        Assert.Equal(0, from.Interpolate(to, .5).Axes[0].Value);
        Assert.Equal(-5e307, from.Interpolate(to, .25).Axes[0].Value);
        Assert.Equal(double.MaxValue, from.Interpolate(to, 1.5).Axes[0].Value);
    }

    [Fact]
    public void VariationTechnologyHintsLoadAndParticipateInSupportsQueries()
    {
        var box = new TextBlock { Text = "ABM", FontSize = 32 };
        Css.SetStyleSheet(box, "@supports font-tech(variations){TextBlock{opacity:.4}}"); Evaluate(box);
        Assert.Equal(.4, box.Opacity, 6);
        Assert.True(CssFontCapabilities.SupportsTechnology("variations"));
    }

    [Fact]
    public void FontRelativeLengthsUseTheSameVariedInstanceDuringCascadeAndAnimation()
    {
        foreach (bool reversed in new[] { false, true })
        {
            var child = new TextBlock { FontFamily = "SF Pro", FontSize = 32 }; var root = new Border { Child = child };
            Style(root, "font-variation-settings:'wdth' 75"); Style(child, "width:10ch;height:1ex");
            void CheckUnits() { Layout(root, 1000); var units = TextMeasurement.GetFontUnitMetrics(child.FontFamily.GetRenderingSource(child), 32); Assert.Equal(10 * units.ZeroAdvance, child.Width, 3); Assert.Equal(units.XHeight, child.Height, 3); }
            CheckUnits(); double before = child.Width;
            root.SetCssAnimatedValue(Property, Parse("'wdth' 125")); CheckUnits(); Assert.True(child.Width > before + 1);
            root.ClearCssAnimatedValue(Property); CheckUnits(); Assert.Equal(before, child.Width, 3);
            Style(child, reversed ? "font-variation-settings:'wdth' 125;width:10ch;height:1ex" : "width:10ch;height:1ex;font-variation-settings:'wdth' 125"); CheckUnits(); Assert.True(child.Width > before + 1);
            Style(child, "width:10ch;height:1ex;font-variation-settings:initial"); CheckUnits();
        }
    }

    [Fact]
    public void SharedDocumentsReflowAndRestoreWithSourceNodesIntact()
    {
        var run = new Run("MMMM MMMM MMMM MMMM MMMM"); var span = new Span(run); var paragraph = new Paragraph(span);
        var document = new FlowDocument(paragraph) { FontFamily = "SF Pro", FontSize = 32, PagePadding = new(0), LineHeight = 44 };
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation(); one.SetDocument(document); two.SetDocument(document);
        double PairWidth(double axis) { using var format = _context.CreateTextFormat("SF Pro", 32, 400, 0); Assert.True(format.TrySetFontVariations([new(FontFeature.TagOf("wdth"), axis)])); return format.MeasureText("MMMM MMMM", 10000, 1000).Width; }
        double narrow = PairWidth(75), wide = PairWidth(125); Assert.True(wide > narrow + 1); int width = (int)Math.Floor((narrow + wide) / 2);
        Style(span, "font-variation-settings:'wdth' 75"); Layout(one, width); Layout(two, width); double before = one.ExtentHeight;
        Style(span, "font-variation-settings:'wdth' 125"); Layout(one, width); Layout(two, width); Assert.True(one.ExtentHeight > before); Assert.Equal(one.ExtentHeight, two.ExtentHeight);
        Style(span, "font-variation-settings:'wdth' 75"); Layout(one, width); Layout(two, width); Assert.Equal(before, one.ExtentHeight); Assert.Equal(before, two.ExtentHeight);
        Assert.Equal("MMMM MMMM MMMM MMMM MMMM\n", document.GetText()); Assert.Same(span, run.Parent); Assert.Same(paragraph, span.Parent);
    }
}
