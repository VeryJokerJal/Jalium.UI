using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontLigaturesTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("auto", 0)] [InlineData("normal", 1)] [InlineData("none", 2)]
    [InlineData("NoNe", 2)] [InlineData("n\\6f rmal", 1)]
    public void KerningKeywordsAreComputedValues(string value, int expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-kerning", value));
        var box = Box(); Apply(box, "font-kerning:" + value);
        Assert.Equal((CssFontKerning)expected, box.GetValue(CssFontLigatures.KerningProperty));
    }

    [Theory]
    [InlineData("normal", 0)] [InlineData("none", 256)]
    [InlineData("common-ligatures", 1)] [InlineData("no-common-ligatures", 2)]
    [InlineData("discretionary-ligatures", 4)] [InlineData("no-discretionary-ligatures", 8)]
    [InlineData("historical-ligatures", 16)] [InlineData("no-historical-ligatures", 32)]
    [InlineData("contextual", 64)] [InlineData("no-contextual", 128)]
    [InlineData("COMMON-LIGATURES/**/no-contextual", 129)]
    [InlineData("no\\2d common-ligatures contextual", 66)]
    [InlineData("no-contextual historical-ligatures discretionary-ligatures common-ligatures", 149)]
    public void LigatureKeywordsAndGroupsAreComputedValues(string value, int expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-ligatures", value));
        var box = Box(); Apply(box, "font-variant-ligatures:" + value);
        Assert.Equal((CssLigatureVariant)expected, box.GetValue(CssFontLigatures.VariantProperty));
    }

    public static IEnumerable<object[]> LegalCombinations()
    {
        string[][] groups = [["", "common-ligatures", "no-common-ligatures"],
            ["", "discretionary-ligatures", "no-discretionary-ligatures"],
            ["", "historical-ligatures", "no-historical-ligatures"], ["", "contextual", "no-contextual"]];
        int id = 0;
        foreach (string a in groups[0]) foreach (string b in groups[1])
        foreach (string c in groups[2]) foreach (string d in groups[3])
        {
            var words = new[] { a, b, c, d }.Where(s => s.Length != 0).ToArray();
            yield return [id++, words.Length == 0 ? "normal" : string.Join(' ', words)];
            if (words.Length > 1) yield return [id++, string.Join(' ', words.Reverse())];
        }
    }

    [Theory] [MemberData(nameof(LegalCombinations))]
    public void IndependentGroupsAcceptEitherOrder(int caseId, string value)
    { _ = caseId; Assert.True(CssAuthoring.IsValueSupported("font-variant-ligatures", value), value); }

    [Theory]
    [InlineData("font-kerning", "")] [InlineData("font-kerning", "0")] [InlineData("font-kerning", "on")]
    [InlineData("font-kerning", "auto none")] [InlineData("font-kerning", "'normal'")]
    [InlineData("font-kerning", "normal()")] [InlineData("font-kerning", "normal,none")]
    [InlineData("font-variant-ligatures", "")] [InlineData("font-variant-ligatures", "auto")]
    [InlineData("font-variant-ligatures", "normal contextual")] [InlineData("font-variant-ligatures", "contextual normal")]
    [InlineData("font-variant-ligatures", "none common-ligatures")] [InlineData("font-variant-ligatures", "contextual none")]
    [InlineData("font-variant-ligatures", "contextual contextual")] [InlineData("font-variant-ligatures", "none none")]
    [InlineData("font-variant-ligatures", "common-ligatures no-common-ligatures")]
    [InlineData("font-variant-ligatures", "no-common-ligatures common-ligatures")]
    [InlineData("font-variant-ligatures", "discretionary-ligatures no-discretionary-ligatures")]
    [InlineData("font-variant-ligatures", "historical-ligatures no-historical-ligatures")]
    [InlineData("font-variant-ligatures", "contextual no-contextual")]
    [InlineData("font-variant-ligatures", "'common-ligatures'")] [InlineData("font-variant-ligatures", "contextual,common-ligatures")]
    [InlineData("font-variant-ligatures", "contextual 1")] [InlineData("font-variant-ligatures", "inherit contextual")]
    public void InvalidDeclarationsPreserveEarlierValues(string property, string invalid)
    {
        Assert.False(CssAuthoring.IsValueSupported(property, invalid));
        var box = Box(); Apply(box, "font-kerning:none;font-variant-ligatures:contextual;" + property + ":" + invalid);
        Assert.Equal(CssFontKerning.None, box.GetValue(CssFontLigatures.KerningProperty));
        Assert.Equal(CssLigatureVariant.Contextual, box.GetValue(CssFontLigatures.VariantProperty));
    }

    [Theory]
    [InlineData("inherit", 2, 149)] [InlineData("unset", 2, 149)] [InlineData("initial", 0, 0)]
    [InlineData("all:initial", 0, 0)] [InlineData("all:unset", 2, 149)]
    [InlineData("font:32px Seravek", 0, 0)] [InlineData("font:inherit", 2, 149)] [InlineData("font:unset", 2, 149)]
    public void InheritanceAndFontResetsUpdateBothProperties(string value, int kern, int liga)
    {
        var child = Box(); var parent = new Border { Child = child };
        Apply(parent, "font-kerning:none;font-variant-ligatures:common-ligatures discretionary-ligatures historical-ligatures no-contextual");
        Apply(child, value.Contains(':') ? value : "font-kerning:" + value + ";font-variant-ligatures:" + value);
        Evaluate(parent);
        Assert.Equal((CssFontKerning)kern, child.GetValue(CssFontLigatures.KerningProperty));
        Assert.Equal((CssLigatureVariant)liga, child.GetValue(CssFontLigatures.VariantProperty));
    }

    [Fact]
    public void VariablesImportanceRevertAndRemovalRestoreInheritedValues()
    {
        var box = Box(); var parent = new Border { Child = box };
        Apply(parent, "--kern:none;--liga:no-common-ligatures;font-kerning:normal;font-variant-ligatures:historical-ligatures");
        Apply(box, "font-kerning:var(--kern)!important;font-kerning:auto;font-variant-ligatures:var(--liga)");
        Assert.Equal("clig=0,kern=0,liga=0", Describe(CssFontFeatureSettings.ForOwner(box)));
        Apply(box, "font-kerning:revert;font-variant-ligatures:revert");
        Assert.Equal("hlig=1,kern=1", Describe(CssFontFeatureSettings.ForOwner(box)));
        Apply(box, ""); Apply(parent, "font-kerning:none;font-variant-ligatures:none"); Evaluate(parent);
        Assert.Equal("calt=0,clig=0,dlig=0,hlig=0,kern=0,liga=0", Describe(CssFontFeatureSettings.ForOwner(box)));
    }

    [Theory]
    [InlineData("font-variant-ligatures:none", "calt=0,clig=0,dlig=0,hlig=0,liga=0")]
    [InlineData("font-kerning:normal;font-variant-ligatures:common-ligatures contextual", "calt=1,clig=1,kern=1,liga=1")]
    [InlineData("font-kerning:none;font-feature-settings:'kern' 1", "kern=1")]
    [InlineData("font-variant-ligatures:none;font-feature-settings:'liga','rlig'", "calt=0,clig=0,dlig=0,hlig=0,liga=1,rlig=1")]
    [InlineData("letter-spacing:2px;font-variant-ligatures:common-ligatures discretionary-ligatures historical-ligatures contextual", "calt=1,clig=0,dlig=0,hlig=0,liga=0")]
    [InlineData("letter-spacing:-1px;font-variant-ligatures:common-ligatures;font-feature-settings:'liga','clig'", "clig=1,dlig=0,hlig=0,liga=1")]
    [InlineData("letter-spacing:0;font-variant-ligatures:common-ligatures", "clig=1,liga=1")]
    public void FeaturePrecedencePreservesRequiredLigaturesAndExplicitOverrides(string style, string expected)
    {
        var box = Box(); Apply(box, style);
        Assert.Equal(expected, Describe(CssFontFeatureSettings.ForOwner(box)));
        Assert.Equal("Seravek", box.FontFamily.Source);
    }

    [Fact]
    public void CompiledBuilderAndAuthoringMetadataExposeBothLonghands()
    {
        var box = Box(); var b = new CssStyleSheetBuilder(1);
        var c = b.Compound("TextBlock", false, null, false, true, false, null, null, null, 0, null, null);
        b.Rule([b.Selector([c], [], 1, 0, 0, false, false)],
            [b.Declaration("font-kerning", "none", false), b.Declaration("font-variant-ligatures", "common-ligatures no-contextual", false)], -1, null, -1);
        Css.GetStyleSheets(box).Add(b.Build()); Evaluate(box);
        Assert.Equal("calt=0,clig=1,kern=0,liga=1", Describe(CssFontFeatureSettings.ForOwner(box)));
        foreach (string property in new[] { "font-kerning", "font-variant-ligatures" })
            Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty(property).Support);
        Assert.Contains("none", CssAuthoring.GetValues("font-kerning"));
        Assert.Contains("no-contextual", CssAuthoring.GetValues("font-variant-ligatures"));
    }

    [Theory]
    [InlineData("font-kerning:none", "kern", 0)] [InlineData("font-kerning:normal", "kern", 1)]
    [InlineData("font-variant-ligatures:no-common-ligatures", "liga", 0)]
    [InlineData("font-variant-ligatures:discretionary-ligatures", "dlig", 1)]
    [InlineData("font-variant-ligatures:historical-ligatures", "hlig", 1)]
    [InlineData("font-variant-ligatures:no-contextual", "calt", 0)]
    public void MeasurementAndCaretsMatchAnIndependentlyConfiguredNativeFont(string style, string tag, uint value)
    {
        const string sample = "AV office affine ffl AVATAR";
        var box = Box(sample); Apply(box, style);
        var source = box.FontFamily.GetRenderingSource(box);
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, source, 32, 400, 0);
        using var reference = _context.CreateTextFormat("Seravek", 32, 400, 0);
        FontFeature[] features = tag == "liga" ? [new(FontFeature.TagOf("liga"), value), new(FontFeature.TagOf("clig"), value)] : [new(FontFeature.TagOf(tag), value)];
        Assert.Equal(JaliumResult.Ok, reference.SetFontFeatures(features));
        Assert.Equal(reference.MeasureText(sample, 100000, 1000).Width, actual.MeasureText(sample, 100000, 1000).Width, 3);
        foreach (uint position in new uint[] { 0, 1, 4, 8, (uint)sample.Length })
        {
            Assert.True(reference.HitTestTextPosition(sample, 100000, 1000, position, false, out var expected));
            Assert.True(actual.HitTestTextPosition(sample, 100000, 1000, position, false, out var hit));
            Assert.Equal(expected.CaretX, hit.CaretX, 3);
        }
    }

    [Theory]
    [InlineData("none", 0)] [InlineData("normal", 1)] [InlineData("no-common-ligatures contextual", 2)]
    public void FaceDefaultsRemainBelowHighLevelProperties(string variant, int scenario)
    {
        var box = new TextBlock { Text = "office", FontSize = 32 }; Css.SetStyleSheet(box, "@font-face{font-family:LigatureDefault;src:local(Seravek);font-feature-settings:'liga' 0,'dlig' 1}");
        Apply(box, "font-family:LigatureDefault;font-variant-ligatures:" + variant);
        _ = TextMeasurement.MeasureText(new FormattedText(box.Text, box.FontFamily.GetRenderingSource(box), 32));
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(box).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(box.Dispatcher); Evaluate(box);
        var source = CssFontFaces.MaterializeSource(box.FontFamily.GetRenderingSource(box), box.Text);
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, source, 32, 400, 0);
        using var expected = _context.CreateTextFormat("Seravek", 32, 400, 0);
        var features = FontFeatureRenderingSource.Merge([new(FontFeature.TagOf("liga"), 0), new(FontFeature.TagOf("dlig"), 1)], CssFontFeatureSettings.ForOwner(box));
        Assert.Equal(JaliumResult.Ok, expected.SetFontFeatures(features));
        Assert.Equal(expected.MeasureText(box.Text, 100000, 1000).Width, actual.MeasureText(box.Text, 100000, 1000).Width, 3);
        Assert.Empty(Css.GetFontLoadErrors(box)); _ = scenario;
    }

    [Fact]
    public void InlineAndSharedDocumentViewsReflowAndRestoreWithoutChangingText()
    {
        var run = new Run("office office office"); var span = new Span(run); var paragraph = new Paragraph(span);
        var doc = new FlowDocument(paragraph) { FontFamily = "Seravek", FontSize = 32, PagePadding = new(0), LineHeight = 44 };
        Apply(doc, "font-family:Seravek;font-size:32px;line-height:44px;white-space:normal");
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation();
        one.SetDocument(doc); two.SetDocument(doc);
        Apply(span, "font-variant-ligatures:common-ligatures"); Layout(one); Layout(two); double before = one.ExtentHeight;
        Apply(span, "font-variant-ligatures:no-common-ligatures"); Layout(one); Layout(two);
        Assert.Equal(one.ExtentHeight, two.ExtentHeight);
        Apply(span, "font-variant-ligatures:common-ligatures"); Layout(one); Layout(two);
        Assert.Equal(before, one.ExtentHeight); Assert.Equal(before, two.ExtentHeight);
        Assert.Equal("office office office\n", doc.GetText()); Assert.Same(span, run.Parent);
        var text = Box(); text.Inlines.Add(span = new Span(new Run("office")));
        Apply(text, "font-kerning:none;font-variant-ligatures:none"); Evaluate(text);
        Assert.Equal("calt=0,clig=0,dlig=0,hlig=0,kern=0,liga=0", Describe(CssFontFeatureSettings.ForOwner(span.Inlines[0])));
    }

    [Theory]
    [InlineData("font-kerning", "normal", "none")]
    [InlineData("font-variant-ligatures", "common-ligatures", "none")]
    [InlineData("font-variant-numeric", "proportional-nums", "tabular-nums")]
    [InlineData("font-feature-settings", "normal", "'kern' 0,'liga' 0,'clig' 0")]
    public void FontTransitionsRequireExplicitDiscreteBehavior(string property, string before, string after)
    {
        var box = Box(); var dp = CssPropertyRegistry.Lookup(property)!.StorageProperty!;
        Apply(box, property + ":" + before); var from = box.GetValue(dp)!;
        Apply(box, property + ":" + after); var to = box.GetValue(dp)!;
        Assert.Null(CssTransitions.Create(box, dp, from, to, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, new("linear"), false)));
    }

    [Theory]
    [InlineData("font-kerning", "normal", "none")]
    [InlineData("font-variant-ligatures", "common-ligatures", "none")]
    [InlineData("font-variant-numeric", "proportional-nums", "tabular-nums")]
    [InlineData("font-feature-settings", "normal", "'kern' 0,'liga' 0,'clig' 0")]
    public void AuthoredDiscreteTransitionsSwitchWholeFontValuesAtTheMidpoint(string property, string before, string after)
    {
        var box = Box("AV office 111111"); var dp = CssPropertyRegistry.Lookup(property)!.StorageProperty!;
        Apply(box, property + ":" + before); var from = box.GetValue(dp)!; var beforeWidth = Width();
        Apply(box, property + ":" + after); var to = box.GetValue(dp)!; var afterWidth = Width();
        Assert.True(Math.Abs(beforeWidth - afterWidth) > .5);
        Apply(box, property + ":" + after + ";transition:" + property + " 1s linear .2s allow-discrete");
        var transition = Assert.IsType<CssTransitionData>(box.GetValue(CssTransitions.DataProperty)).Find(box, dp);
        Assert.NotNull(transition); Assert.True(transition.AllowDiscrete); Assert.Equal(TimeSpan.FromMilliseconds(200), transition.Delay);
        var animation = Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(box, dp, from, to, transition));
        Assert.Equal(dp.PropertyType, animation.TargetPropertyType); Assert.Equal(transition.Delay, animation.BeginTime);
        var clock = animation.CreateClock();
        foreach (double progress in new[] { 0.0, .49, .5, .9, 1.0, .1 })
        {
            clock.SetProgressForSampling(progress);
            var value = animation.GetCurrentValue(from, to, clock);
            Assert.Equal(progress < .5 ? from : to, value);
            box.SetCssAnimatedValue(dp, value); Layout(box);
            Assert.Equal(progress < .5 ? beforeWidth : afterWidth, Width(), 3);
        }
        box.ClearCssAnimatedValue(dp); Layout(box); Assert.Equal(afterWidth, Width(), 3);
        Assert.Equal("AV office 111111", box.Text);
        double Width()
        {
            var formatted = new FormattedText(box.Text, box.FontFamily.GetRenderingSource(box), 32);
            Assert.True(TextMeasurement.MeasureText(formatted)); return formatted.Width;
        }
    }

    private static TextBlock Box(string text = "") => new() { Text = text, FontFamily = "Seravek", FontSize = 32 };
    private static string Describe(FontFeature[] features) => string.Join(',', features.OrderBy(f => f.Tag).Select(f => new string([(char)(f.Tag >> 24), (char)(f.Tag >> 16 & 255), (char)(f.Tag >> 8 & 255), (char)(f.Tag & 255)]) + "=" + f.Value));
    private static void Apply(DependencyObject owner, string css) { Css.SetStyle(owner, css); Evaluate(owner); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { view.Measure(new(150, 240)); view.Arrange(new(0, 0, 150, 240)); view.UpdateLayout(); } }
}
