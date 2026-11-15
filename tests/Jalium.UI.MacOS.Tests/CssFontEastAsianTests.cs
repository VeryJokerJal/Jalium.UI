using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontEastAsianTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    public static IEnumerable<object[]> Features()
    {
        yield return ["jis78", "jp78", 1]; yield return ["jis83", "jp83", 2];
        yield return ["jis90", "jp90", 4]; yield return ["jis04", "jp04", 8];
        yield return ["simplified", "smpl", 16]; yield return ["traditional", "trad", 32];
        yield return ["full-width", "fwid", 64]; yield return ["proportional-width", "pwid", 128];
        yield return ["ruby", "ruby", 256];
    }

    [Theory]
    [MemberData(nameof(Features))]
    public void EveryKeywordReachesTheNativeFontAndCompiledBuilder(string keyword, string tag, int flags)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-east-asian", keyword));
        var box = Box("日本語 ABC 123"); var builder = new CssStyleSheetBuilder(1);
        var compound = builder.Compound("TextBlock", false, null, false, true, false, null, null, null, 0, null, null);
        builder.Rule([builder.Selector([compound], [], 1, 0, 0, false, false)],
            [builder.Declaration("font-variant-east-asian", keyword, false)], -1, null, -1);
        Css.GetStyleSheets(box).Add(builder.Build()); Evaluate(box);
        Assert.Equal((CssEastAsianVariant)flags, Value(box));
        Assert.True(FontFeatureRenderingSource.TryUnwrap(box.FontFamily.GetRenderingSource(box), out var features, out var family));
        Assert.Equal(new FontFeature(FontFeature.TagOf(tag), 1), Assert.Single(features)); Assert.Equal("Hiragino Sans", family);
        // CSS uses explicit width matching, which can select a different face than
        // the legacy factory even at 100%. Configure the native tag independently.
        using var expected = _context.CreateTextFormat("Hiragino Sans", 32, 400, 0, 100);
        expected.SetNoWrap(true);
        Assert.Equal(JaliumResult.Ok, expected.SetFontFeatures([new(FontFeature.TagOf(tag), 1)]));
        var actual = new FormattedText(box.Text, box.FontFamily.GetRenderingSource(box), 32);
        Assert.True(TextMeasurement.MeasureText(actual)); Assert.Equal(expected.MeasureText(box.Text, 100000, 1000).Width, actual.Width, 3);
        Assert.Contains(keyword, CssAuthoring.GetValues("font-variant-east-asian"));
    }

    [Fact]
    public void EveryIndependentGroupCombinationAndPermutationIsValid()
    {
        foreach (string form in new[] { "", "jis78", "jis83", "jis90", "jis04", "simplified", "traditional" })
        foreach (string width in new[] { "", "full-width", "proportional-width" })
        foreach (string ruby in new[] { "", "ruby" })
        {
            string[] words = new[] { form, width, ruby }.Where(s => s.Length > 0).ToArray();
            if (words.Length == 0) { Assert.True(CssAuthoring.IsValueSupported("font-variant-east-asian", "normal")); continue; }
            foreach (var permutation in Permute(words)) Assert.True(CssAuthoring.IsValueSupported("font-variant-east-asian", string.Join(' ', permutation)));
        }
        var box = Box(); Style(box, "font-variant-east-asian:RuBy/**/full\\2d width JIS04");
        Assert.Equal(CssEastAsianVariant.Ruby | CssEastAsianVariant.FullWidth | CssEastAsianVariant.Jis04, Value(box));
        static IEnumerable<string[]> Permute(string[] words) => words.Length <= 1 ? [words] :
            words.SelectMany((word, index) => Permute(words.Where((_, at) => at != index).ToArray()).Select(rest => new[] { word }.Concat(rest).ToArray()));
    }

    [Theory]
    [InlineData("")] [InlineData("none")] [InlineData("normal ruby")] [InlineData("ruby normal")] [InlineData("normal normal")]
    [InlineData("jis78 jis83")] [InlineData("traditional simplified")] [InlineData("jis04 jis90")]
    [InlineData("full-width proportional-width")] [InlineData("ruby ruby")] [InlineData("jis78 jis78")]
    [InlineData("full-width full-width")] [InlineData("normal garbage")] [InlineData("normal none")]
    [InlineData("normal 30px")] [InlineData("full-width garbage")] [InlineData("full-width none")]
    [InlineData("full-width 30px")] [InlineData("'ruby'")] [InlineData("jis78,ruby")]
    [InlineData("ruby()")] [InlineData("inherit ruby")] [InlineData("jis78 / ruby")]
    public void InvalidDeclarationsLeaveTheValidCascadeValueIntact(string invalid)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant-east-asian", invalid));
        var box = Box(); Style(box, "font-variant-east-asian:traditional; font-variant-east-asian:" + invalid);
        Assert.Equal(CssEastAsianVariant.Traditional, Value(box));
    }

    [Theory]
    [InlineData("inherit", 400)] [InlineData("unset", 400)] [InlineData("initial", 0)] [InlineData("normal", 0)]
    [InlineData("all:initial", 0)] [InlineData("all:unset", 400)] [InlineData("font:32px Seravek", 0)]
    [InlineData("font:inherit", 400)] [InlineData("font:unset", 400)]
    public void InheritanceAndShorthandResetsReplaceTheWholeComputedValue(string value, int expected)
    {
        var child = Box(); var root = new Border { Child = child };
        Style(root, "font-variant-east-asian:simplified proportional-width ruby");
        Style(child, value.Contains(':') ? value : "font-variant-east-asian:" + value); Evaluate(root);
        Assert.Equal((CssEastAsianVariant)expected, Value(child));
    }

    [Fact]
    public void VariablesLayersImportanceAndInvalidComputedValuesFollowTheCascade()
    {
        var child = Box(); var root = new Border { Child = child };
        Style(root, "--variant:traditional full-width;font-variant-east-asian:ruby");
        Style(child, "font-variant-east-asian:var(--variant)!important;font-variant-east-asian:normal"); Evaluate(root);
        Assert.Equal(CssEastAsianVariant.Traditional | CssEastAsianVariant.FullWidth, Value(child));
        Style(child, "font-variant-east-asian:var(--missing)"); Assert.Equal(CssEastAsianVariant.Ruby, Value(child));
        Style(child, ""); Css.SetStyleSheet(root, "@layer base,top;@layer base{TextBlock{font-variant-east-asian:jis04 full-width}}@layer top{TextBlock{font-variant-east-asian:revert-layer}}");
        try { Evaluate(root); Assert.Equal(CssEastAsianVariant.Jis04 | CssEastAsianVariant.FullWidth, Value(child)); }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Theory]
    [InlineData("normal", "fwid=1,jp04=1,ruby=1,tnum=1")]
    [InlineData("'jp04' off,'ruby' 3", "fwid=1,jp04=0,ruby=3,tnum=1")]
    public void ExplicitFeatureSettingsOverlayHighLevelFeaturesPerTag(string settings, string expected)
    {
        var box = Box(); Style(box, "font-variant-east-asian:jis04 full-width ruby;font-variant-numeric:tabular-nums;font-feature-settings:" + settings);
        Assert.Equal(expected, Describe(CssFontFeatureSettings.ForOwner(box)));
        Style(box, "font-variant-east-asian:normal"); Assert.Empty(CssFontFeatureSettings.ForOwner(box));
    }

    [Fact]
    public void MarkerAndSharedDocumentInheritanceRetainOriginalSourceNodes()
    {
        var run = new Run("日本語 ABC"); var span = new Span(run); var paragraph = new Paragraph(span);
        var document = new FlowDocument(paragraph) { PagePadding = new(0), FontFamily = "Hiragino Sans", FontSize = 32 };
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation(); one.SetDocument(document); two.SetDocument(document);
        foreach (string value in new[] { "jis04 full-width ruby", "normal", "simplified", "jis04 full-width ruby" })
        {
            Style(document, "font-variant-east-asian:" + value); Layout(one); Layout(two);
            Assert.Equal(Value(document), Value(run)); Assert.Same(span, run.Parent); Assert.Same(document, one.Document); Assert.Same(document, two.Document);
        }
        var box = Box("正文"); Style(box, "display:list-item;font-variant-east-asian:traditional"); Evaluate(box);
        var marker = Assert.Single(CssGeneratedContent.Markers(CssNode.Get(box))); Assert.Equal(CssEastAsianVariant.Traditional, Value(marker));
        Css.SetStyleSheet(box, "TextBlock::marker{font-variant-east-asian:full-width}");
        try { Evaluate(box); Assert.Equal(CssEastAsianVariant.FullWidth, Value(marker)); }
        finally { Css.SetStyleSheets(box, null); }
        Assert.Equal("日本語 ABC\n", document.GetText());
    }

    [Fact]
    public void DiscreteTransitionsAndKeyframesSwitchCompleteVariantGroupsAtHalfway()
    {
        var box = Box(); Style(box, "font-variant-east-asian:jis78"); var from = Value(box);
        Style(box, "font-variant-east-asian:traditional proportional-width ruby;transition:font-variant-east-asian 1s linear .2s allow-discrete"); var to = Value(box);
        var dp = CssFontEastAsian.VariantProperty;
        Assert.Null(CssTransitions.Create(box, dp, from, to, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, new("linear"), false)));
        var data = Assert.IsType<CssTransitionData>(box.GetValue(CssTransitions.DataProperty)).Find(box, dp)!;
        Assert.True(data.AllowDiscrete); Assert.Equal(TimeSpan.FromMilliseconds(200), data.Delay);
        var animation = Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(box, dp, from, to, data)); var clock = animation.CreateClock();
        foreach (double progress in new[] { 0, .49, .5, 1, .1 })
        {
            clock.SetProgressForSampling(progress); box.SetCssAnimatedValue(dp, animation.GetCurrentValue(from, to, clock));
            Assert.Equal(progress < .5 ? from : to, Value(box));
        }
        box.ClearCssAnimatedValue(dp); Assert.Equal(to, Value(box));
        Css.SetStyleSheet(box, "@keyframes variant{from{font-variant-east-asian:jis78}to{font-variant-east-asian:traditional proportional-width ruby}}");
        try
        {
            Evaluate(box); var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "variant")!);
            var sampler = new CssPropertySampler(dp, tracks[dp]);
            Assert.Equal(from, sampler.Sample(.49, new("linear"))); Assert.Equal(to, sampler.Sample(.5, new("linear")));
        }
        finally { Css.SetStyleSheets(box, null); }
    }

    [Theory]
    [InlineData("font-variant-east-asian", "ruby full-width jis04", "jis04 full-width ruby")]
    [InlineData("font-variant-numeric", "ordinal tabular-nums", "tabular-nums ordinal")]
    [InlineData("font-variant-ligatures", "no-contextual discretionary-ligatures", "discretionary-ligatures no-contextual")]
    [InlineData("font-kerning", "none", "none")]
    [InlineData("font-feature-settings", "'ruby' on,'jp04' 0", "'jp04' off,'ruby' 1")]
    public void StyleQueriesCompareCanonicalValuesAndRespondToInheritedChanges(string property, string authored, string equivalent)
    {
        var root = new StackPanel(); var parent = new StackPanel(); var child = Box(); root.Children.Add(parent); parent.Children.Add(child); Css.SetClass(child, "target");
        Css.SetStyleSheet(root, $"@container style({property}:{equivalent}){{.target{{opacity:.25}}}}");
        try
        {
            foreach (var item in new[] { (authored, .25), ("initial", 1d), (authored, .25) })
            {
                Style(root, property + ":" + item.Item1); Layout(root); Assert.Equal(item.Item2, child.Opacity, 3);
                var node = CssNode.Get(parent); var lengths = CssEngine.BuildLengthContext(node);
                Assert.Equal(item.Item2 == .25, CssOrdinaryStyleQuery.Matches(property, equivalent, node, lengths));
                Assert.Equal(item.Item2 == .25, CssOrdinaryStyleQuery.Matches(property, null, node, lengths));
                Assert.True(CssOrdinaryStyleQuery.Matches(property, "inherit", node, lengths));
                Assert.False(CssOrdinaryStyleQuery.Matches(property, "revert", node, lengths));
            }
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    private static TextBlock Box(string text = "") => new() { Text = text, FontFamily = "Hiragino Sans", FontSize = 32 };
    private static CssEastAsianVariant Value(DependencyObject owner) => (CssEastAsianVariant)owner.GetValue(CssFontEastAsian.VariantProperty)!;
    private static void Style(DependencyObject owner, string css) { Css.SetStyle(owner, css); Evaluate(owner); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { Evaluate(view); view.Measure(new(500, 240)); view.Arrange(new(0, 0, 500, 240)); view.UpdateLayout(); } }
    private static string Describe(FontFeature[] features) => string.Join(',', features.OrderBy(f => f.Tag).Select(f => new string([(char)(f.Tag >> 24), (char)(f.Tag >> 16 & 255), (char)(f.Tag >> 8 & 255), (char)(f.Tag & 255)]) + "=" + f.Value));
}
