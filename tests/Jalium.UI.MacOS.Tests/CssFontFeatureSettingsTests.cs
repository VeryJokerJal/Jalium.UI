using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontFeatureSettingsTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("normal", "")]
    [InlineData("\"dlig\" 1", "dlig=1")]
    [InlineData("\"smcp\" on", "smcp=1")]
    [InlineData("'c2sc'", "c2sc=1")]
    [InlineData("\"liga\" off", "liga=0")]
    [InlineData("\"tnum\", 'hist'", "hist=1,tnum=1")]
    [InlineData("\"PKRN\"", "PKRN=1")]
    [InlineData("\"kern\" OFF,\"salt\" +2", "kern=0,salt=2")]
    [InlineData("\"salt\" calc(1 + 1)", "salt=2")]
    [InlineData("\"salt\" calc(1.5)", "salt=2")]
    [InlineData("\"salt\" 2147483647", "salt=2147483647")]
    [InlineData("\"salt\" -0", "salt=0")]
    [InlineData("\"tnum\" 0,\"tnum\" 3", "tnum=3")]
    [InlineData("\"dlig\" 1,\"smcp\" on,\"dlig\" 0", "dlig=0,smcp=1")]
    [InlineData("\"ABCD\" 2,\"abcd\" 3", "ABCD=2,abcd=3")]
    [InlineData("\"    \"", "    =1")]
    [InlineData("\"\\6c iga\" off", "liga=0")]
    [InlineData("\"liga\"/**/off,/**/'kern'", "kern=1,liga=0")]
    public void ComputedMapUsesCaseSensitiveTagsAndLastDuplicate(string css, string expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-feature-settings", css));
        var box = Box(); Apply(box, "font-feature-settings:" + css);
        Assert.Equal(expected, Describe(Settings(box).Features));
    }

    [Fact]
    public void PrintableEscapedPunctuationIsAFeatureTag()
    {
        var reader = new CssTokenReader("\"\\22\\7d\\2f\\2a\"");
        Assert.Equal(new FontFeature(0x227d2f2a, 1), Assert.Single(CssFontFeatureSettings.Read(ref reader)!.Features));
        reader = new CssTokenReader("\"\\22\\22\\22\\22\"");
        Assert.Equal(new FontFeature(0x22222222, 1), Assert.Single(CssFontFeatureSettings.Read(ref reader)!.Features));
    }

    [Theory]
    [InlineData("")] [InlineData("normal \"dlig\"")] [InlineData("\"c2sc\",normal")]
    [InlineData("\"tnum\" \"hist\"")] [InlineData("\"silly\" off")] [InlineData("dlig")]
    [InlineData("\"abc\"")] [InlineData("\"ab中d\"")] [InlineData("\"a\\7f bc\"")]
    [InlineData("\"liga\" -1")] [InlineData("\"liga\" 1.0")] [InlineData("\"liga\" 1e0")]
    [InlineData("\"liga\" 1px")] [InlineData("\"liga\" 10%")] [InlineData("\"liga\" 2147483648")]
    [InlineData("\"liga\" auto")] [InlineData("\"liga\" on off")] [InlineData("\"liga\" 1 2")]
    [InlineData(",\"liga\"")] [InlineData("\"liga\",")] [InlineData("\"liga\",,\"kern\"")]
    public void InvalidDeclarationDoesNotReplaceThePreviousMap(string css)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-feature-settings", css));
        var box = Box(); Apply(box, "font-feature-settings:'tnum';font-feature-settings:" + css);
        Assert.Equal("tnum=1", Describe(Settings(box).Features));
    }

    [Fact]
    public void MapEqualityAndCapacityKeepAllTagsRatherThanTheOldNumericLimit()
    {
        var a = new CssTokenReader("'tnum','kern' off,'tnum' on");
        var b = new CssTokenReader("'kern' 0,'tnum' 1");
        Assert.Equal(CssFontFeatureSettings.Read(ref a), CssFontFeatureSettings.Read(ref b));
        var tags = Enumerable.Range(0, 300).Select(i => $"'Q{(char)('A' + i / 26)}{(char)('A' + i % 26)}X'");
        a = new CssTokenReader(string.Join(',', tags)); var value = CssFontFeatureSettings.Read(ref a)!;
        Assert.Equal(300, value.Features.Length);
        var key = FontFeatureRenderingSource.Wrap("Seravek", value.Features);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(key, out var decoded, out var family));
        Assert.Equal(value.Features, decoded); Assert.Equal("Seravek", family);
    }

    [Theory]
    [InlineData("inherit", "kern=0,tnum=1")] [InlineData("unset", "kern=0,tnum=1")]
    [InlineData("initial", "")] [InlineData("normal", "")]
    [InlineData("all:initial", "")] [InlineData("all:unset", "kern=0,tnum=1")]
    [InlineData("font:32px Seravek", "")]
    public void WholeMapInheritanceAndResets(string value, string expected)
    {
        var child = Box(); var root = new Border { Child = child };
        Apply(root, "font-feature-settings:'tnum','kern' off");
        Apply(child, value.Contains(':') ? value : "font-feature-settings:" + value);
        Evaluate(root); Assert.Equal(expected, Describe(Settings(child).Features));
        Apply(child, "font-feature-settings:'salt' 2"); Assert.Equal("salt=2", Describe(Settings(child).Features));
    }

    [Fact]
    public void VariablesImportantRemovalAndNumericPrecedenceUseIndependentProperties()
    {
        var box = Box(); Apply(box, "--features:'tnum' 0,'salt' 2;font-variant-numeric:tabular-nums; font-feature-settings:var(--features)!important;font-feature-settings:'tnum' 1");
        Assert.Equal("salt=2,tnum=0", Describe(CssFontFeatureSettings.ForOwner(box)));
        Apply(box, "font-variant-numeric:tabular-nums;font-feature-settings:normal");
        Assert.Equal("tnum=1", Describe(CssFontFeatureSettings.ForOwner(box)));
        Apply(box, ""); Assert.Empty(CssFontFeatureSettings.ForOwner(box));
    }

    [Fact]
    public void ContextualIntegerMathRecomputesWhenItsContainerChanges()
    {
        var child = Box(); var parent = new Border { Child = child };
        Apply(parent, "container-type:inline-size;width:100px");
        Apply(child, "font-feature-settings:'liga' calc(10 + (sign(2cqw - 10px) * 5)), 'dlig' calc(20 + (sign(2cqw - 10px) * 5))");
        for (int i = 0; i < 3; i++) { parent.Measure(new(100, 240)); parent.Arrange(new(0, 0, 100, 240)); parent.UpdateLayout(); }
        Assert.Equal("dlig=15,liga=5", Describe(Settings(child).Features));
        Apply(parent, "container-type:inline-size;width:600px");
        for (int i = 0; i < 3; i++) { parent.Measure(new(600, 240)); parent.Arrange(new(0, 0, 600, 240)); parent.UpdateLayout(); }
        Assert.Equal("dlig=25,liga=15", Describe(Settings(child).Features));
    }

    [Fact]
    public void CompiledBuilderRetainsBothLegacyAndFeatureDescriptorOverloads()
    {
        var builder = new CssStyleSheetBuilder(1);
        builder.FontFace("Legacy", ["Seravek"], [true], [null], [[]], 400, 400, 0, 100, 100, [0, 0x10ffff], "auto", 0, -1, null);
        builder.FontFace("Featured", ["Seravek"], [true], [null], [[]], 400, 400, 0, 100, 100, [0, 0x10ffff], "auto", 0, -1, null, "'salt' 2,'tnum'");
        var faces = builder.Build().FontFaces;
        Assert.Empty(faces[0].FeatureSettings.Features); Assert.Equal("salt=2,tnum=1", Describe(faces[1].FeatureSettings.Features));
    }

    [Theory]
    [InlineData("'tnum' 1", "tnum", 1)] [InlineData("'tnum' 0", "tnum", 0)]
    [InlineData("'pnum'", "pnum", 1)] [InlineData("'kern' off", "kern", 0)]
    [InlineData("'liga' off", "liga", 0)] [InlineData("'salt' 2", "salt", 2)]
    [InlineData("'ABCD' 3", "ABCD", 3)]
    public void CssMeasurementMatchesIndependentNativeFeatures(string css, string tag, uint value)
    {
        const string sample = "111111 AV office 1/2"; var box = Box(sample); Apply(box, "font-feature-settings:" + css);
        using var reference = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, reference.SetFontFeatures([new(FontFeature.TagOf(tag), value)]));
        var measured = new FormattedText(sample, box.FontFamily.GetRenderingSource(box), 32);
        Assert.True(TextMeasurement.MeasureText(measured));
        Assert.Equal(reference.MeasureText(sample, 100000, 1000).Width, measured.Width, 3);
        Assert.Equal("Seravek", box.FontFamily.Source);
    }

    [Fact]
    public void DescriptorValidationKeepsEarlierValidMapAndIgnoresWideVariablesAndImportant()
    {
        var rule = CssFontFaceRule.Parse("", "font-family: FeatureFace;src:local(Seravek);font-feature-settings:'tnum';font-feature-settings:'salt' -1;font-feature-settings:inherit;font-feature-settings:var(--x);font-feature-settings:'kern' off!important", 0)!;
        Assert.Equal("tnum=1", Describe(rule.FeatureSettings.Features));
        rule = CssFontFaceRule.Parse("", "font-family: FeatureFace;src:local(Seravek);font-feature-settings:'tnum';font-feature-settings:normal", 0)!;
        Assert.Empty(rule.FeatureSettings.Features);
    }

    [Fact]
    public void FacePlansRoundTripDifferentFeaturesAndGlobalSettingsRemainOutsideThem()
    {
        var plan = new CssFontRenderingPlan(0, 0,
            [new("Seravek", 400, [new(0x30, 0x39)], false, false) { Features = [new(FontFeature.TagOf("tnum"), 0)] },
             new("Seravek", 400, [new(0x41, 0x5a)], false, false) { Features = [new(FontFeature.TagOf("salt"), 2)] }]);
        var encoded = FontFeatureRenderingSource.Wrap(plan.Encode(), [new(FontFeature.TagOf("tnum"), 1)]);
        Assert.True(CssFontRenderingPlan.TryDecode(encoded, out var decoded));
        Assert.Equal(plan.Faces[0].Features, decoded.Faces[0].Features); Assert.Equal(plan.Faces[1].Features, decoded.Faces[1].Features);
        Assert.Equal("salt=2,tnum=1", Describe(FontFeatureRenderingSource.Merge(decoded.Faces[1].Features, [new(FontFeature.TagOf("tnum"), 1)])));
    }

    [Theory]
    [InlineData("normal", 1)] [InlineData("'tnum' 0", 0)] [InlineData("'tnum' 1", 1)]
    public void LoadedFaceDefaultsAreOverriddenByTheElementProperty(string settings, uint expected)
    {
        var text = new TextBlock { Text = "111111", FontSize = 32 };
        Css.SetStyleSheet(text, "@font-face{font-family:FeatureDefault;src:local(Seravek);font-feature-settings:'tnum'}");
        Apply(text, "font-family:FeatureDefault;font-feature-settings:" + settings);
        var source = text.FontFamily.GetRenderingSource(text);
        var pending = new FormattedText(text.Text, source, 32); Assert.True(TextMeasurement.MeasureText(pending));
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(text).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(text.Dispatcher); Evaluate(text);
        source = CssFontFaces.MaterializeSource(text.FontFamily.GetRenderingSource(text), text.Text);
        Assert.Empty(Css.GetFontLoadErrors(text));
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, source, 32, 400, 0);
        using var reference = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, reference.SetFontFeatures([new(FontFeature.TagOf("tnum"), expected)]));
        Assert.Equal(reference.MeasureText(text.Text, 100000, 1000).Width, actual.MeasureText(text.Text, 100000, 1000).Width, 3);
        Assert.Equal("FeatureDefault", text.FontFamily.Source);
    }

    [Fact]
    public void LegacyFeatureWrappersOverlayDescriptorsInsteadOfDroppingOtherTags()
    {
        var descriptor = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("tnum"), 1)]);
        var key = FontFeatureRenderingSource.Wrap(descriptor, [new(FontFeature.TagOf("kern"), 0)]);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(key, out var features, out _));
        Assert.Equal("kern=0,tnum=1", Describe(features));
    }

    [Fact]
    public void SharedDocumentAndInlineViewsReflowAndRestoreWithoutChangingTheirSource()
    {
        var run = new Run("111111 111111"); var span = new Span(run); var paragraph = new Paragraph(span);
        var document = new FlowDocument(paragraph) { FontFamily = "Seravek", FontSize = 32, PagePadding = new(0), LineHeight = 44 };
        Css.SetStyle(document, "font-family:Seravek;font-size:32px;line-height:44px;white-space:normal");
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation();
        one.SetDocument(document); two.SetDocument(document);
        Apply(span, "font-feature-settings:'pnum'"); Layout(one); Layout(two); var before = one.ExtentHeight;
        Apply(span, "font-feature-settings:'tnum'"); Layout(one); Layout(two);
        Assert.True(one.ExtentHeight > before + 20); Assert.Equal(one.ExtentHeight, two.ExtentHeight);
        Apply(span, "font-feature-settings:'pnum'"); Layout(one); Layout(two);
        Assert.Equal(before, one.ExtentHeight); Assert.Equal(before, two.ExtentHeight);
        Assert.Equal("111111 111111\n", document.GetText()); Assert.Same(span, run.Parent); Assert.Same(paragraph, span.Parent);
    }

    private static TextBlock Box(string text = "") => new() { Text = text, FontFamily = "Seravek", FontSize = 32 };
    private static CssFontFeatureSettings Settings(DependencyObject owner) => (CssFontFeatureSettings)owner.GetValue(CssFontFeatureSettings.SettingsProperty)!;
    private static string Describe(FontFeature[] features) => string.Join(',', features.Select(f => new string([(char)(f.Tag >> 24), (char)(f.Tag >> 16 & 255), (char)(f.Tag >> 8 & 255), (char)(f.Tag & 255)]) + "=" + f.Value));
    private static void Apply(DependencyObject owner, string css) { Css.SetStyle(owner, css); Evaluate(owner); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { view.Measure(new(170, 240)); view.Arrange(new(0, 0, 170, 240)); view.UpdateLayout(); } }
}
