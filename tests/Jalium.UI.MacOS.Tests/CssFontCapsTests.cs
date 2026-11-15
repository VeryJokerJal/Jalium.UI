using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontCapsTests
{
    public static IEnumerable<object[]> Variants()
    {
        yield return ["normal", (int)CssCapsVariant.Normal, ""];
        yield return ["small-caps", (int)CssCapsVariant.SmallCaps, "smcp=1"];
        yield return ["all-small-caps", (int)CssCapsVariant.AllSmallCaps, "c2sc=1,smcp=1"];
        yield return ["petite-caps", (int)CssCapsVariant.PetiteCaps, "pcap=1"];
        yield return ["all-petite-caps", (int)CssCapsVariant.AllPetiteCaps, "c2pc=1,pcap=1"];
        yield return ["unicase", (int)CssCapsVariant.Unicase, "unic=1"];
        yield return ["titling-caps", (int)CssCapsVariant.TitlingCaps, "titl=1"];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void KeywordsPreserveComputedValuesAndComposeTheirFontFeatures(
        string keyword, int variant, string expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-caps", keyword));
        Assert.Contains(keyword, CssAuthoring.GetValues("font-variant-caps"));
        var box = new TextBlock { Text = "Aaß Ελληνικά 中文", FontFamily = "Seravek" };
        Style(box, "font-variant-caps:" + keyword);
        Assert.Equal((CssCapsVariant)variant, Value(box));
        Assert.Equal(expected, Describe(CssFontFeatureSettings.ForOwner(box)));
        Assert.Equal("Aaß Ελληνικά 中文", box.Text);
        Assert.Equal("Seravek", box.FontFamily.Source);
    }

    [Theory]
    [InlineData("")] [InlineData("none")] [InlineData("normal small-caps")]
    [InlineData("small-caps all-small-caps")] [InlineData("small-caps small-caps")]
    [InlineData("'small-caps'")] [InlineData("small-caps()")] [InlineData("small-caps,normal")]
    [InlineData("all-petite-caps 1")] [InlineData("all-caps")] [InlineData("uppercase")]
    [InlineData("inherit unicase")] [InlineData("small-caps / normal")]
    public void InvalidValuesDoNotDiscardAnEarlierValidDeclaration(string invalid)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant-caps", invalid));
        var box = new TextBlock(); Style(box, "font-variant-caps:unicase;font-variant-caps:" + invalid);
        Assert.Equal(CssCapsVariant.Unicase, Value(box));
    }

    [Fact]
    public void EscapedCaseInsensitiveKeywordsAndCompiledBuilderUseTheSameParser()
    {
        var box = new TextBlock(); var builder = new CssStyleSheetBuilder(1);
        var compound = builder.Compound("TextBlock", false, null, false, true, false, null, null, null, 0, null, null);
        builder.Rule([builder.Selector([compound], [], 1, 0, 0, false, false)],
            [builder.Declaration("font-variant-caps", "AlL\\2d SmAlL\\2d CaPs/**/", false)], -1, null, -1);
        Css.GetStyleSheets(box).Add(builder.Build()); Evaluate(box);
        Assert.Equal(CssCapsVariant.AllSmallCaps, Value(box));
        Assert.Equal("c2sc=1,smcp=1", Describe(CssFontFeatureSettings.ForOwner(box)));
    }

    [Theory]
    [InlineData("inherit", true)] [InlineData("unset", true)] [InlineData("initial", false)]
    [InlineData("normal", false)] [InlineData("font:inherit", true)] [InlineData("font:unset", true)]
    [InlineData("font:initial", false)] [InlineData("font:32px Seravek", false)]
    [InlineData("font:menu", false)] [InlineData("all:initial", false)] [InlineData("all:unset", true)]
    public void FontAndAllResetsUseTheInheritedCapsValue(string declaration, bool inherited)
    {
        var child = new TextBlock(); var root = new Border { Child = child };
        Style(root, "font-variant-caps:all-petite-caps");
        Style(child, declaration.Contains(':') ? declaration : "font-variant-caps:" + declaration); Evaluate(root);
        Assert.Equal(inherited ? CssCapsVariant.AllPetiteCaps : CssCapsVariant.Normal, Value(child));
    }

    [Theory]
    [InlineData("small-caps italic 600 condensed 32px/1.5 Seravek")]
    [InlineData("italic 600 condensed small-caps 32px/1.5 Seravek")]
    [InlineData("normal small-caps normal normal 32px/1.5 Seravek")]
    public void FontShorthandAcceptsTheCss2CapsComponentAndResetsOtherFeatures(string font)
    {
        var box = new TextBlock();
        Style(box, "font-variant-east-asian:ruby;font-variant-numeric:tabular-nums;font-feature-settings:'salt' 3;font:" + font);
        Assert.Equal(CssCapsVariant.SmallCaps, Value(box));
        Assert.Equal("smcp=1", Describe(CssFontFeatureSettings.ForOwner(box)));
        Assert.Equal(32, box.FontSize); Assert.Equal("Seravek", box.FontFamily.Source);
    }

    [Theory]
    [InlineData("small-caps small-caps 32px Seravek")]
    [InlineData("small-caps normal normal normal normal 32px Seravek")]
    [InlineData("all-small-caps 32px Seravek")] [InlineData("petite-caps 32px Seravek")]
    [InlineData("unicase 32px Seravek")] [InlineData("titling-caps 32px Seravek")]
    [InlineData("small-caps menu")]
    public void FontShorthandRejectsNonCss2CapsAndDuplicatePrefixComponents(string font)
    {
        Assert.False(CssAuthoring.IsValueSupported("font", font));
        var box = new TextBlock(); Style(box, "font-variant-caps:unicase;font:" + font);
        Assert.Equal(CssCapsVariant.Unicase, Value(box));
    }

    [Fact]
    public void AKeywordAfterTheShorthandSizeIsAFontFamily()
    {
        var box = new TextBlock(); Style(box, "font:32px small-caps");
        Assert.Equal(CssCapsVariant.Normal, Value(box)); Assert.Equal("small-caps", box.FontFamily.Source);
    }

    [Fact]
    public void LowLevelSettingsOverrideIndividualTagsAndOtherVariantsRemainIndependent()
    {
        var box = new TextBlock();
        Style(box, "font-variant-caps:all-small-caps;font-variant-numeric:tabular-nums;font-feature-settings:'smcp' off,'c2sc' 3");
        Assert.Equal(CssCapsVariant.AllSmallCaps, Value(box));
        Assert.Equal("c2sc=3,smcp=0,tnum=1", Describe(CssFontFeatureSettings.ForOwner(box)));
        Style(box, "font-variant-caps:normal;font-feature-settings:'smcp' on");
        Assert.Equal("smcp=1", Describe(CssFontFeatureSettings.ForOwner(box)));
        Style(box, ""); Assert.Empty(CssFontFeatureSettings.ForOwner(box));
    }

    [Fact]
    public void VariablesLayersImportanceRevertAndInvalidComputedValuesFollowTheCascade()
    {
        var box = new TextBlock(); var root = new Border { Child = box };
        Style(root, "--caps:small-caps;font-variant-caps:unicase");
        Style(box, "font-variant-caps:var(--caps)!important;font-variant-caps:normal"); Evaluate(root);
        Assert.Equal(CssCapsVariant.SmallCaps, Value(box));
        Style(box, "font-variant-caps:var(--missing)"); Assert.Equal(CssCapsVariant.Unicase, Value(box));
        Style(box, "");
        Css.SetStyleSheet(root, "@layer base,top;@layer base{TextBlock{font-variant-caps:petite-caps}}@layer top{TextBlock{font-variant-caps:revert-layer}}");
        try { Evaluate(root); Assert.Equal(CssCapsVariant.PetiteCaps, Value(box)); }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Fact]
    public void SharedDocumentsAndMarkersInheritWithoutChangingTheOriginalTextOrNodes()
    {
        var run = new Run("Aaß Ελληνικά 中文"); var span = new Span(run); var document = new FlowDocument(new Paragraph(span));
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation();
        one.SetDocument(document); two.SetDocument(document);
        foreach (string keyword in new[] { "small-caps", "all-small-caps", "normal", "petite-caps", "unicase" })
        {
            Style(document, "font-variant-caps:" + keyword); Layout(one); Layout(two);
            Assert.Equal(Value(document), Value(run)); Assert.Same(span, run.Parent);
            Assert.Same(document, one.Document); Assert.Same(document, two.Document);
            Assert.Equal("Aaß Ελληνικά 中文\n", document.GetText());
        }
        var box = new TextBlock { Text = "正文" }; Style(box, "display:list-item;font-variant-caps:small-caps");
        var marker = Assert.Single(CssGeneratedContent.Markers(CssNode.Get(box)));
        Assert.Equal(CssCapsVariant.SmallCaps, Value(marker));
        Css.SetStyleSheet(box, "TextBlock::marker{font-variant-caps:all-small-caps}");
        try { Evaluate(box); Assert.Equal(CssCapsVariant.AllSmallCaps, Value(marker)); }
        finally { Css.SetStyleSheets(box, null); }
    }

    [Fact]
    public void StyleQueriesCompareComputedKeywordsAndReevaluateOnInheritedChanges()
    {
        var root = new StackPanel(); var parent = new StackPanel(); var child = new TextBlock();
        root.Children.Add(parent); parent.Children.Add(child); Css.SetClass(child, "target");
        Css.SetStyleSheet(root, "@container style(font-variant-caps:all-small-caps){.target{opacity:.25}}");
        try
        {
            foreach (var value in new[] { ("all-small-caps", .25), ("small-caps", 1d), ("normal", 1d), ("all-small-caps", .25) })
            {
                Style(root, "font-variant-caps:" + value.Item1); Layout(root);
                Assert.Equal(value.Item2, child.Opacity, 3);
                var node = CssNode.Get(parent); var lengths = CssEngine.BuildLengthContext(node);
                Assert.True(CssOrdinaryStyleQuery.Matches("font-variant-caps", value.Item1, node, lengths));
                Assert.Equal(value.Item1 != "normal", CssOrdinaryStyleQuery.Matches("font-variant-caps", null, node, lengths));
            }
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Fact]
    public void DiscreteTransitionsAndKeyframesKeepTheWholeKeywordUntilHalfway()
    {
        var box = new TextBlock(); Style(box, "font-variant-caps:small-caps"); var from = Value(box);
        Style(box, "font-variant-caps:all-petite-caps;transition:font-variant-caps 1s linear .2s allow-discrete"); var to = Value(box);
        var dp = CssFontCaps.VariantProperty;
        Assert.Null(CssTransitions.Create(box, dp, from, to, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, new("linear"), false)));
        var data = Assert.IsType<CssTransitionData>(box.GetValue(CssTransitions.DataProperty)).Find(box, dp)!;
        Assert.True(data.AllowDiscrete); Assert.Equal(TimeSpan.FromMilliseconds(200), data.Delay);
        var animation = Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(box, dp, from, to, data)); var clock = animation.CreateClock();
        foreach (double progress in new[] { 0, .49, .5, 1, .1 })
        {
            clock.SetProgressForSampling(progress); box.SetCssAnimatedValue(dp, animation.GetCurrentValue(from, to, clock));
            Assert.Equal(progress < .5 ? from : to, Value(box));
        }
        box.ClearCssAnimatedValue(dp);
        Css.SetStyleSheet(box, "@keyframes caps{from{font-variant-caps:small-caps}to{font-variant-caps:all-petite-caps}}");
        try
        {
            Evaluate(box); var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "caps")!);
            var sampler = new CssPropertySampler(dp, tracks[dp]);
            Assert.Equal(from, sampler.Sample(.49, new("linear"))); Assert.Equal(to, sampler.Sample(.5, new("linear")));
        }
        finally { Css.SetStyleSheets(box, null); }
    }

    private static CssCapsVariant Value(DependencyObject owner) => (CssCapsVariant)owner.GetValue(CssFontCaps.VariantProperty)!;
    private static void Style(DependencyObject owner, string css) { Css.SetStyle(owner, css); Evaluate(owner); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { Evaluate(view); view.Measure(new(500, 240)); view.Arrange(new(0, 0, 500, 240)); view.UpdateLayout(); } }
    private static string Describe(FontFeature[] features) => string.Join(',', features.OrderBy(f => f.Tag).Select(f => new string([(char)(f.Tag >> 24), (char)(f.Tag >> 16 & 255), (char)(f.Tag >> 8 & 255), (char)(f.Tag & 255)]) + "=" + f.Value));
}
