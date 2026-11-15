using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontAlternatesTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private readonly CssNativeFontResource _font;
    private string Family => _font.Family;
    private const string Definitions = "@font-feature-values 'FontWithFeaturesOTF'{@stylistic{fancy:1;Fancy:3;}@styleset{sets:1 3;other:2;}@character-variant{first:1 2;last:1 3;single:2;}@swash{flow:2;}@ornaments{ornate:3;}@annotation{circled:1;}}";
    public CssFontAlternatesTests()
    {
        _font = CssNativeFontResource.Create(File.ReadAllBytes(MacOSTestAssets.PathTo("FontNumeric/FontWithFancyFeatures.otf")))!;
        Assert.NotNull(_font);
    }
    public void Dispose() { _font.Dispose(); _context.Dispose(); }

    [Theory]
    [InlineData("normal", "")]
    [InlineData("historical-forms", "hist=1")]
    [InlineData("stylistic(fancy)", "salt=1")]
    [InlineData("stylistic(Fancy)", "salt=3")]
    [InlineData("styleset(sets,other)", "ss01=1,ss02=1,ss03=1")]
    [InlineData("character-variant(first,last)", "cv01=3")]
    [InlineData("character-variant(last,first,single)", "cv01=2,cv02=1")]
    [InlineData("swash(flow)", "cswh=2,swsh=2")]
    [InlineData("ornaments(ornate)", "ornm=3")]
    [InlineData("annotation(circled)", "nalt=1")]
    [InlineData("STYLISTIC(\\46 ancy)", "salt=3")]
    [InlineData("stylistic(unknown)", "")]
    [InlineData("styleset(unknown,sets) historical-forms swash(flow)", "cswh=2,hist=1,ss01=1,ss03=1,swsh=2")]
    public void NamedValuesResolveForTheActualFontAndKeepSourceText(string value, string expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-alternates", value));
        var box = Box(); Sheet(box, Definitions); Style(box, "font-family:'" + Family + "';font-variant-alternates:" + value);
        Assert.Equal(expected, Features(box)); Assert.Equal(Family, box.FontFamily.Source); Assert.Equal("Xnophijklmqrstuvwxyz", box.Text);
        box.SelectAll(); Assert.Equal(box.Text, box.SelectedText);
        Css.SetStyleSheets(box, null);
    }

    [Theory]
    [InlineData("")] [InlineData("none")] [InlineData("normal historical-forms")] [InlineData("historical-forms normal")]
    [InlineData("historical-forms historical-forms")] [InlineData("stylistic()")]
    [InlineData("stylistic(a,b)")] [InlineData("swash(a,b)")] [InlineData("ornaments(a,b)")]
    [InlineData("annotation(a,b)")] [InlineData("styleset(a,)")] [InlineData("styleset(,a)")]
    [InlineData("character-variant('a')")] [InlineData("stylistic(1)")] [InlineData("swash(a) swash(b)")]
    [InlineData("styleset(a) styleset(b)")] [InlineData("stylistic(a),swash(b)")]
    [InlineData("historical-forms()")] [InlineData("unknown(a)")] [InlineData("stylistic(a) 1")]
    public void InvalidGrammarCannotReplaceAValidDeclaration(string invalid)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant-alternates", invalid));
        var box = Box(); Style(box, "font-variant-alternates:historical-forms;font-variant-alternates:" + invalid);
        Assert.True(Variant(box).Historical);
    }

    [Fact]
    public void AllIndependentFunctionGroupsCanBeCombinedInAnyOrder()
    {
        string[] groups = ["stylistic(a)", "historical-forms", "styleset(a,b)", "character-variant(a,b)", "swash(a)", "ornaments(a)", "annotation(a)"];
        for (int mask = 1; mask < 128; mask++)
        {
            var words = groups.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            Assert.True(CssAuthoring.IsValueSupported("font-variant-alternates", string.Join(' ', words)));
            Assert.True(CssAuthoring.IsValueSupported("font-variant-alternates", string.Join(' ', words.Reverse())));
        }
        var box = Box(); Sheet(box, "@font-feature-values 'FontWithFeaturesOTF'{@stylistic{inherit:2;}}}");
        Style(box, "font-family:'" + Family + "';font-variant-alternates:stylistic(inherit)"); Assert.Equal("salt=2", Features(box));
        Css.SetStyleSheets(box, null);
    }

    [Theory]
    [InlineData("inherit", true)] [InlineData("unset", true)] [InlineData("initial", false)] [InlineData("normal", false)]
    [InlineData("all:initial", false)] [InlineData("all:unset", true)] [InlineData("font:32px Seravek", false)]
    [InlineData("font:inherit", true)] [InlineData("font:unset", true)]
    public void WholeComputedValuesInheritAndFontOrAllResetThem(string value, bool inherited)
    {
        var child = Box(); var root = new Border { Child = child }; Style(root, "font-variant-alternates:historical-forms styleset(sets)");
        Style(child, value.Contains(':') ? value : "font-variant-alternates:" + value); Evaluate(root);
        Assert.Equal(inherited, Variant(child).Historical); Assert.Equal(inherited, Variant(child).HasNamedValues);
    }

    [Fact]
    public void DuplicateDefinitionsMergeByKindNameLayerAndSourceOrderWithCaseSensitivity()
    {
        var box = Box(); Sheet(box, "@layer low,high;@layer high{@font-feature-values 'FontWithFeaturesOTF'{@styleset{a:1;}@stylistic{a:2;A:3;}}}@layer low{@font-feature-values 'FontWithFeaturesOTF'{@styleset{a:2;b:3;}@stylistic{a:1;}}}@font-feature-values 'FONTWITHFEATURESOTF'{@styleset{a:2;a:1;}@styleset{a:3;}}}");
        Style(box, "font-family:'" + Family + "';font-variant-alternates:styleset(a,b) stylistic(A)");
        Assert.Equal("salt=3,ss03=1", Features(box));
        Style(box, "font-family:'" + Family + "';font-variant-alternates:styleset(b) stylistic(a)"); Assert.Equal("salt=2,ss03=1", Features(box));
        Css.SetStyleSheets(box, null);
    }

    [Fact]
    public void ErrorRecoveryKeepsValidDefinitionsAndIgnoresUnknownOrMalformedItems()
    {
        var sheet = Parse("@font-feature-values 'FontWithFeaturesOTF'{ @swash{good:1;bad:-1;good:2!important;wide:1 2;} annotation{bad:2;} @unknown{@styleset{x:1}} @ornaments; @styleset{Set:1 3;set:2;} @character-variant{ok:1;pair:2 3;bad:1 2 3;} @swash{next:3;} font-display:swap; font-display:wrong; }");
        var rule = Assert.Single(sheet.FontFeatureValues); Assert.Equal("swap", rule.Display);
        Assert.Equal(new[] { "good", "Set", "set", "ok", "pair", "next" }, rule.Values.Select(v => v.Name));
        Assert.Equal(new[] { 1, 3 }, rule.Values.Single(v => v.Name == "Set").Indices);
        var box = Box(); Css.GetStyleSheets(box).Add(sheet); Style(box, "font-family:'" + Family + "';font-variant-alternates:swash(next) character-variant(pair)");
        Assert.Equal("cswh=3,cv02=3,swsh=3", Features(box)); Css.SetStyleSheets(box, null);
    }

    [Theory]
    [InlineData("serif")] [InlineData("ui-serif")] [InlineData("system-ui")] [InlineData("caption")]
    [InlineData("F,monospace")] [InlineData("'F',")] [InlineData("generic(kai)")] [InlineData("''")]
    [InlineData("inherit")] [InlineData("F serif")]
    public void InvalidFamilyListsDiscardTheDefinitionRatherThanAffectGenericFonts(string family)
    {
        var sheet = Parse("@font-feature-values " + family + "{@stylistic{a:1}}TextBlock{opacity:.5}");
        Assert.Empty(sheet.FontFeatureValues); Assert.Single(sheet.Rules);
        Assert.Single(Parse("@font-feature-values 'serif'{@stylistic{a:1}}" ).FontFeatureValues);
    }

    [Fact]
    public void OutOfRangeIndicesAreRetainedButDoNotEnableUnrelatedTags()
    {
        var box = Box(); Sheet(box, "@font-feature-values 'FontWithFeaturesOTF'{@styleset{a:0 100 21 1;}@character-variant{a:100 3;b:0;c:3 0;}}}");
        Style(box, "font-family:'" + Family + "';font-variant-alternates:styleset(a) character-variant(a,b,c)");
        Assert.Equal("cv03=0,ss01=1,ss21=1", Features(box)); Css.SetStyleSheets(box, null);
    }

    [Fact]
    public void UnknownNamesRemainInComputedValuesAndQueriesEvenWithoutGlyphFeatures()
    {
        var box = Box(); Style(box, "font-variant-alternates:styleset(missing,Missing)");
        Assert.True(Variant(box).HasNamedValues); Assert.Equal("", Features(box));
        var node = CssNode.Get(box); var lengths = CssEngine.BuildLengthContext(node);
        Assert.True(CssOrdinaryStyleQuery.Matches("font-variant-alternates", "styleset(missing,Missing)", node, lengths));
        Assert.False(CssOrdinaryStyleQuery.Matches("font-variant-alternates", "styleset(Missing,missing)", node, lengths));
        Assert.True(CssOrdinaryStyleQuery.Matches("font-variant-alternates", null, node, lengths));
    }

    [Fact]
    public void NativeFontValuesKeepWeightAndIgnoreCssFaceAliasesWhileResolvingTheirOwnNames()
    {
        var box = Box(); box.FontFamily = Family; box.FontWeight = FontWeights.Bold;
        Sheet(box, Definitions + "@font-face{font-family:'FontWithFeaturesOTF';src:url(never-load.otf)}");
        Style(box, "font-variant-alternates:stylistic(Fancy)"); var source = box.FontFamily.GetRenderingSource(box);
        Assert.True(CssFontRenderingPlan.TryDecode(source, out var plan)); Assert.Equal(700, plan.Faces[0].Weight);
        Assert.Equal(Family, plan.Faces[0].Family); Assert.Equal("salt=3", Features(box)); Assert.Empty(Css.GetFontLoadErrors(box));
        Css.SetStyleSheets(box, null);
    }

    [Fact]
    public void FontSpecificSettingsDoNotLeakIntoFallbackFacesAndExplicitPropertiesWin()
    {
        var box = Box(); Sheet(box, Definitions); Style(box, "font-family:'FontWithFeaturesOTF',Seravek;font-variant-alternates:stylistic(fancy) historical-forms;font-feature-settings:'salt' 0");
        Assert.True(CssFontRenderingPlan.TryDecode(box.FontFamily.GetRenderingSource(box), out var plan));
        Assert.Equal("salt=1", Describe(plan.Faces[0].Features)); Assert.Empty(plan.Faces[1].Features);
        Assert.Equal("hist=1,salt=0", Features(box));
        Css.SetStyleSheets(box, null);
    }

    [Fact]
    public void DefinitionReplacementAndConditionalRulesUpdateExistingSources()
    {
        var box = Box(); Style(box, "font-family:'" + Family + "';font-variant-alternates:stylistic(fancy)");
        foreach (int value in new[] { 1, 3, 0, 2, 1 })
        {
            Sheet(box, "@font-feature-values 'FontWithFeaturesOTF'{@stylistic{fancy:" + value + "}}@supports (unknown-font-property:yes){@font-feature-values 'FontWithFeaturesOTF'{@stylistic{fancy:9}}}");
            Assert.Equal("salt=" + value, Features(box));
        }
        Css.SetStyleSheets(box, null); Assert.Equal("", Features(box));
    }

    [Fact]
    public void VariablesImportanceAndCanonicalStyleQueriesFollowInheritedChanges()
    {
        var root = new StackPanel(); var box = Box(); root.Children.Add(box); Css.SetClass(box, "target");
        Sheet(root, "@container style(font-variant-alternates:styleset(sets) historical-forms){.target{opacity:.25}}");
        foreach (string value in new[] { "historical-forms styleset(sets)", "normal", "styleset(sets) historical-forms" })
        {
            Style(root, "--alternate:" + value + ";font-variant-alternates:var(--alternate)!important;font-variant-alternates:normal"); Layout(root);
            Assert.Equal(value == "normal" ? 1 : .25, box.Opacity, 3);
        }
        Css.SetStyleSheets(root, null);
    }

    [Fact]
    public void DiscreteTransitionsAndKeyframesUseCompleteUnresolvedValues()
    {
        var box = Box(); Style(box, "font-variant-alternates:historical-forms"); var from = Variant(box);
        Style(box, "font-variant-alternates:stylistic(Fancy) styleset(sets);transition:font-variant-alternates 1s linear .2s allow-discrete"); var to = Variant(box); var dp = CssFontAlternates.VariantProperty;
        Assert.Null(CssTransitions.Create(box, dp, from, to, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, new("linear"), false)));
        var data = Assert.IsType<CssTransitionData>(box.GetValue(CssTransitions.DataProperty)).Find(box, dp)!;
        var animation = Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(box, dp, from, to, data)); var clock = animation.CreateClock();
        Assert.Equal(TimeSpan.FromMilliseconds(200), animation.BeginTime);
        foreach (double progress in new[] { .0, .49, .5, 1, .1 })
        {
            clock.SetProgressForSampling(progress); box.SetCssAnimatedValue(dp, animation.GetCurrentValue(from, to, clock));
            Assert.Equal(progress < .5 ? from : to, Variant(box));
        }
        box.ClearCssAnimatedValue(dp);
        Sheet(box, "@keyframes alt{from{font-variant-alternates:historical-forms}to{font-variant-alternates:stylistic(Fancy) styleset(sets)}}");
        var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "alt")!); var sampler = new CssPropertySampler(dp, tracks[dp]);
        Assert.Equal(from, sampler.Sample(.49, new("linear"))); Assert.Equal(to, sampler.Sample(.5, new("linear"))); Css.SetStyleSheets(box, null);
    }

    [Theory]
    [InlineData(null, "swap", false)] [InlineData("auto", "swap", true)] [InlineData("swap", "block", false)]
    [InlineData(null, "block", true)] [InlineData("bad", "swap", false)]
    public void FamilyDisplayDefaultsApplyOnlyWhenTheFaceDescriptorWasOmitted(string? faceDisplay, string familyDisplay, bool blocked)
    {
        var box = Box("X"); var resolver = new PendingResolver();
        string css = "@font-face{font-family:PendingAlt;src:url(font.otf);" + (faceDisplay is null ? "" : "font-display:" + faceDisplay) + "}@font-feature-values PendingAlt{font-display:" + familyDisplay + ";}";
        Css.GetStyleSheets(box).Add(Parse(css, null, new Uri("https://font.test/"), resolver));
        Style(box, "font-family:PendingAlt,Seravek");
        try { Assert.Equal(blocked, CssFontFaces.IsBlocked(CssFontFaces.MaterializeSource(box.FontFamily.GetRenderingSource(box), "X"))); }
        finally { Css.SetStyleSheets(box, null); Evaluate(box); }
    }

    [Fact]
    public void BuilderPreservesDefinitionsAndOmittedDisplayWithoutReparsingAStylesheet()
    {
        var builder = new CssStyleSheetBuilder(1); builder.FontFeatureValues([Family], ["stylistic"], ["Fancy"], [[3]], "swap", 0, -1, null);
        builder.FontFace("PendingAlt", ["https://font.test/f.otf"], [false], [null], [[]], 400, 400, 0, 100, 100, [0, 0x10ffff], "auto", 1, -1, null, "normal", false);
        var sheet = builder.Build(); Assert.False(Assert.Single(sheet.FontFaces).DisplaySpecified); Assert.Equal("Fancy", Assert.Single(sheet.FontFeatureValues).Values[0].Name);
        var box = Box(); Css.GetStyleSheets(box).Add(sheet); Style(box, "font-family:'" + Family + "';font-variant-alternates:stylistic(Fancy)");
        Assert.Equal("salt=3", Features(box)); Css.SetStyleSheets(box, null);
    }

    [Fact]
    public async Task ImportedDefinitionsPreserveLayerOrderConditionsAndCase()
    {
        var resolver = new SheetResolver(new Dictionary<string, string>
        {
            ["/main.css"] = Template("@import 'definitions.css' layer(base);@font-feature-values 'FontWithFeaturesOTF'{@stylistic{Fancy:3}}"),
            ["/definitions.css"] = Template("@font-feature-values 'FontWithFeaturesOTF'{@styleset{sets:1 3}@stylistic{Fancy:1}}@supports (unknown-font-property:yes){@font-feature-values 'FontWithFeaturesOTF'{@stylistic{Fancy:9}}}"),
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://font.test/main.css"), resolver);
        Assert.Equal(3, sheet.FontFeatureValues.Length); Assert.Contains(sheet.FontFeatureValues, rule => rule.LayerName == "base");
        var box = Box(); Css.GetStyleSheets(box).Add(sheet); Style(box, "font-family:'" + Family + "';font-variant-alternates:styleset(sets) stylistic(Fancy)");
        Assert.Equal("salt=3,ss01=1,ss03=1", Features(box)); Css.SetStyleSheets(box, null);
    }

    [Fact]
    public void DocumentProxyUsesOriginalFontScopeForSpacesBaselinesAndWrapping()
    {
        var paragraph = new Paragraph(new Run("B B B B B B B B B")); var document = new FlowDocument(paragraph) { PagePadding = new(0) };
        Sheet(document, "@font-face{font-family:DocumentAlias;src:local('" + Family + "')}Paragraph{margin:0}");
        Style(document, "font-family:DocumentAlias;font-size:32px;line-height:44px;white-space:pre-wrap;text-autospace:no-autospace");
        using var view = new FlowDocumentPresentation(); view.SetDocument(document); Layout(view);
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(document).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(document.Dispatcher); Layout(view); var proxy = Assert.Single(view.Paragraphs);
        var reference = Box("B B B B B B B B B"); Style(reference, "font-family:'" + Family + "';font-size:32px;line-height:44px;white-space:pre-wrap;text-autospace:no-autospace"); Layout(reference);
        Assert.Same(paragraph, proxy.DocumentFontSource);
        Assert.True(CssFontRenderingPlan.TryDecode(proxy.FontFamily.GetRenderingSource(proxy), out var plan) ||
            proxy.FontFamily.GetRenderingSource(proxy).Contains(Family, StringComparison.Ordinal));
        for (int index = 0; index < reference.Text.Length; index++) Assert.Equal(reference.GetSourceRangeSegments(index, 1), proxy.GetSourceRangeSegments(index, 1));
        Css.SetStyleSheets(document, null); Evaluate(document);
    }

    [Fact]
    public void CopiedCssFamiliesKeepResolvingAliasesInTheirConsumerScope()
    {
        string css = "@font-face{font-family:CopiedAlias;src:local('" + Family + "')}";
        var original = Box(); Sheet(original, css); Style(original, "font-family:CopiedAlias");
        var proxy = Box(); proxy.FontFamily = original.FontFamily; Sheet(proxy, css); Layout(proxy);
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(proxy).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(proxy.Dispatcher); Layout(proxy);
        string source = CssFontFaces.MaterializeSource(proxy.FontFamily.GetRenderingSource(proxy), proxy.Text);
        Assert.True(source.Contains(Family, StringComparison.Ordinal) || CssFontRenderingPlan.TryDecode(source, out var plan) && plan.Faces.Any(face => face.Family == Family));
        var expected = Box(); expected.FontFamily = Family; Layout(expected); Assert.Equal(expected.DesiredSize.Width, proxy.DesiredSize.Width, 3);
        Assert.Equal("CopiedAlias", proxy.FontFamily.Source); Css.SetStyleSheets(original, null); Css.SetStyleSheets(proxy, null); Evaluate(proxy);
    }

    [Fact]
    public void ADelayedMarkerFontInvalidatesTheViewersCachedIntrinsicWidth()
    {
        const string content = "X n o p h i j k l m q r s t u v w x y z";
        FlowDocument Document() => new(new Jalium.UI.Documents.List(new ListItem(new Paragraph(new Run("B"))))) { PagePadding = new(0) };
        var document = Document(); var resolver = new BytesResolver();
        string css = "@font-face{font-family:DelayedMarker;src:url(marker.otf);font-display:swap}ListItem::marker{font-family:DelayedMarker;content:'" + content + "'}Paragraph,List,ListItem{margin:0;padding:0;border:0}";
        Css.GetStyleSheets(document).Add(Parse(css, null, new Uri("https://font.test/"), resolver));
        Style(document, "font-family:'" + Family + "';font-size:32px;line-height:44px;white-space:pre-wrap;text-autospace:no-autospace");
        using var view = new FlowDocumentPresentation(); view.SetDocument(document); Layout(view); Assert.True(view.IsMeasureValid);
        resolver.Bytes.TrySetResult(File.ReadAllBytes(MacOSTestAssets.PathTo("FontNumeric/FontWithFancyFeatures.otf")));
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(document).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(document.Dispatcher); Assert.False(view.IsMeasureValid); Layout(view);
        var reference = Document(); Sheet(reference, "ListItem::marker{font-family:'" + Family + "';content:'" + content + "'}Paragraph,List,ListItem{margin:0;padding:0;border:0}");
        Style(reference, "font-family:'" + Family + "';font-size:32px;line-height:44px;white-space:pre-wrap;text-autospace:no-autospace");
        using var expected = new FlowDocumentPresentation(); expected.SetDocument(reference); Layout(expected);
        Assert.Equal(expected.DesiredSize.Width, view.DesiredSize.Width, 3);
        Css.SetStyleSheets(document, null); Css.SetStyleSheets(reference, null); Evaluate(document); Evaluate(reference);
    }

    private static TextBlock Box(string sample = "Xnophijklmqrstuvwxyz") => new() { Text = sample, FontSize = 32, IsTextSelectionEnabled = true };
    private static CssFontAlternates Variant(DependencyObject owner) => CssFontAlternates.For(owner);
    private void Style(DependencyObject owner, string value) { Css.SetStyle(owner, Template(value)); Evaluate(owner); }
    private void Sheet(DependencyObject owner, string css) { Css.SetStyleSheet(owner, Template(css)); Evaluate(owner); }
    private string Template(string css) => css.Replace("FontWithFeaturesOTF", Family).Replace("FONTWITHFEATURESOTF", Family.ToUpperInvariant());
    private CssStyleSheet Parse(string css) => CssStyleSheet.Parse(Template(css));
    private CssStyleSheet Parse(string css, string? label, Uri uri, ICssResourceResolver resolver) => CssStyleSheet.Parse(Template(css), label, uri, resolver);
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { Evaluate(view); view.Measure(new(500, 240)); view.Arrange(new(0, 0, 500, 240)); view.UpdateLayout(); } }
    private static string Features(TextBlock box)
    {
        string source = CssFontFaces.MaterializeSource(box.FontFamily.GetRenderingSource(box), box.Text); FontFeatureRenderingSource.TryUnwrap(source, out var global, out _);
        return Describe(CssFontRenderingPlan.TryDecode(source, out var plan) && plan.Faces.Length > 0 ? FontFeatureRenderingSource.Merge(plan.Faces[0].Features, global) : global);
    }
    private static string Describe(FontFeature[] features) => string.Join(',', features.OrderBy(f => f.Tag).Select(f => new string([(char)(f.Tag >> 24), (char)(f.Tag >> 16 & 255), (char)(f.Tag >> 8 & 255), (char)(f.Tag & 255)]) + "=" + f.Value));
    private sealed class PendingResolver : ICssResourceResolver
    {
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken token = default)
        { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Cancelled pending fixture returned"); }
    }
    private sealed class SheetResolver(Dictionary<string, string> sources) : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken token = default)
            => ValueTask.FromResult(new CssResource(uri, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sources[uri.AbsolutePath])), "text/css"));
    }
    private sealed class BytesResolver : ICssResourceResolver
    {
        internal readonly TaskCompletionSource<byte[]> Bytes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken token = default)
            => new(uri, new MemoryStream(await Bytes.Task.WaitAsync(token)), "font/otf");
    }
}
