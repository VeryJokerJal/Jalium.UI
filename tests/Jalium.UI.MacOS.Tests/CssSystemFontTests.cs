using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSystemFontTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private const string Text = "System font 中文 AV 0123";
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("caption")] [InlineData("icon")] [InlineData("menu")]
    [InlineData("message-box")] [InlineData("small-caption")] [InlineData("status-bar")]
    public void SystemRolesApplyACompleteFontAndKeepSourceText(string role)
    {
        Assert.True(CssAuthoring.IsValueSupported("font", role)); Assert.Contains(role, CssAuthoring.GetValues("font"));
        var box = new TextBlock { Text = Text, IsTextSelectionEnabled = true };
        Apply(box, "font:" + role); Layout(box); var expected = SystemFonts.CssFont(role);
        Assert.Equal(expected.Family.Source, box.FontFamily.Source); Assert.Equal(expected.Size, box.FontSize);
        Assert.Equal(expected.Style, box.FontStyle); Assert.Equal(expected.Weight, box.FontWeight);
        Assert.Equal(100, CssFontStretchValue.Computed(CssNode.Get(box)));
        Assert.Equal(CssLineHeightKind.Normal, CssComputedLineHeightValue.Inherited(CssNode.Get(box)).Kind);
        Assert.True(CssOrdinaryStyleQuery.Matches("font-size", Number(expected.Size) + "px", CssNode.Get(box), CssEngine.BuildLengthContext(CssNode.Get(box))));
        box.SelectAll(); Assert.Equal(Text, box.SelectedText);
        using var reference = _context.CreateTextFormat(expected.Family.Source, (float)expected.Size, expected.Weight.ToOpenTypeWeight(), expected.Style.ToOpenTypeStyle());
        Assert.True(reference.TrySetFontVariations([new(0x6f70737a, expected.Size)]));
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, box.FontFamily.GetRenderingSource(box), (float)box.FontSize, box.FontWeight.ToOpenTypeWeight(), box.FontStyle.ToOpenTypeStyle());
        Assert.Equal(reference.MeasureText(Text, 10000, 1000).Width, actual.MeasureText(Text, 10000, 1000).Width, 3);
    }

    [Theory]
    [InlineData("CaPtIoN", "caption")] [InlineData("MENU", "menu")]
    [InlineData("m\\65 nu", "menu")] [InlineData("message\\2d box", "message-box")]
    [InlineData("/**/small-caption/**/", "small-caption")]
    public void SystemKeywordsFollowCssIdentifierAndCommentRules(string value, string role)
    {
        Assert.True(CssAuthoring.IsValueSupported("font", value)); var box = new TextBlock(); Apply(box, "font:" + value);
        Assert.Equal(SystemFonts.CssFont(role).Size, box.FontSize);
    }

    [Theory]
    [InlineData("menu 14px serif")] [InlineData("italic menu")] [InlineData("menu/1.5")]
    [InlineData("menu,serif")] [InlineData("'menu'")] [InlineData("menu()")]
    [InlineData("caption bold")] [InlineData("small-caption 20px")] [InlineData("status-bar normal")]
    [InlineData("system-ui")] [InlineData("default")] [InlineData("message-box menu")]
    public void InvalidSystemShorthandDoesNotResetValidEarlierLonghands(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("font", value)); var box = new TextBlock();
        Apply(box, "font-family:Seravek;font-size:31px;font-weight:700;font-width:83.2%;font:" + value);
        Assert.Equal("Seravek", box.FontFamily.Source); Assert.Equal(31, box.FontSize); Assert.Equal(700, box.FontWeight.ToOpenTypeWeight()); Assert.Equal(83.2, CssFontStretchValue.Computed(CssNode.Get(box)));
    }

    [Theory]
    [InlineData("caption")] [InlineData("icon")] [InlineData("menu")]
    [InlineData("message-box")] [InlineData("small-caption")] [InlineData("status-bar")]
    public void SystemNamesAfterAnExplicitSizeAreOrdinaryFamilyNames(string role)
    {
        Assert.True(CssAuthoring.IsValueSupported("font", "31px " + role));
        Assert.False(CssAuthoring.IsValueSupported("font-family", role));
        Assert.True(CssAuthoring.IsValueSupported("font-family", "'" + role + "'"));
        var box = new TextBlock(); Apply(box, "font:31px " + role); Assert.Equal(31, box.FontSize); Assert.Equal(role, box.FontFamily.Source);
    }

    [Theory] [InlineData("caption")] [InlineData("small-caption")] [InlineData("menu")]
    public void SystemFontResetsTheFontGroupWhilePreservingIndependentTextProperties(string role)
    {
        var box = new TextBlock();
        Apply(box, "font:italic bold condensed 42px/2 Seravek;font-variant-numeric:tabular-nums;font-variant-east-asian:ruby;font-variant-alternates:historical-forms;font-variant-ligatures:none;font-kerning:none;font-feature-settings:'salt' 2;color:red;letter-spacing:2px;word-spacing:3px;text-decoration:underline;font:" + role);
        Assert.DoesNotContain(CssFontFeatureSettings.ForOwner(box), f => f.Tag != FontFeature.TagOf("liga") && f.Tag != FontFeature.TagOf("clig") && f.Tag != FontFeature.TagOf("dlig") && f.Tag != FontFeature.TagOf("hlig"));
        Assert.Equal(CssNumericVariant.Normal, box.GetValue(CssFontNumeric.VariantProperty));
        Assert.Equal(CssEastAsianVariant.Normal, box.GetValue(CssFontEastAsian.VariantProperty));
        Assert.Equal(CssFontAlternates.Normal, box.GetValue(CssFontAlternates.VariantProperty));
        Assert.Equal(CssLigatureVariant.Normal, box.GetValue(CssFontLigatures.VariantProperty));
        Assert.Equal(CssFontKerning.Auto, box.GetValue(CssFontLigatures.KerningProperty));
        Assert.Equal(CssFontFeatureSettings.Normal, box.GetValue(CssFontFeatureSettings.SettingsProperty));
        Assert.Equal(2, CssTextSpacing.Letter(box)); Assert.Equal(CssLayoutLength.Px(3), box.GetValue(CssFlowProperties.WordSpacingProperty));
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(box.Foreground).Color);
        Assert.NotEmpty(box.TextDecorations);
    }

    [Fact]
    public void VariablesImportanceLayersAndLaterLonghandsUseNormalCascade()
    {
        var root = new StackPanel(); var box = new TextBlock(); root.Children.Add(box);
        Apply(root, "--ui:menu"); Apply(box, "font:var(--ui)!important;font-size:99px"); Assert.Equal(SystemFonts.CssFont("menu").Size, box.FontSize);
        Apply(root, "--ui:small-caption"); Evaluate(root); Assert.Equal(SystemFonts.CssFont("small-caption").Size, box.FontSize);
        Apply(box, "font:menu;font-size:31px;font-weight:700"); Assert.Equal(31, box.FontSize); Assert.Equal(700, box.FontWeight.ToOpenTypeWeight());
        Apply(box, ""); Css.SetStyleSheet(root, "@layer base,top;@layer base{TextBlock{font:menu}}@layer top{TextBlock{font:small-caption;font:revert-layer}}");
        try { Evaluate(root); Assert.Equal(SystemFonts.CssFont("menu").Size, box.FontSize); }
        finally { Css.SetStyleSheets(root, null); root.Children.Clear(); }
    }

    [Fact]
    public void FontRelativeUnitsUseTheSystemFontSelectedInTheSameDeclarationBlock()
    {
        var box = new TextBlock { Text = Text };
        foreach (string role in new[] { "menu", "icon", "small-caption", "menu" })
        {
            Apply(box, "width:8ch;padding:1em;font:" + role); Layout(box); var expected = SystemFonts.CssFont(role);
            using var reference = _context.CreateTextFormat(expected.Family.Source, (float)expected.Size, expected.Weight.ToOpenTypeWeight(), expected.Style.ToOpenTypeStyle());
            Assert.True(reference.TrySetFontVariations([new(0x6f70737a, expected.Size)]));
            Assert.Equal(8 * reference.GetFontUnitMetrics().ZeroAdvance, box.Width, 3);
            Assert.Equal(expected.Size, box.Padding.Left, 7);
        }
    }

    [Fact]
    public void NativeLocalValuesKeepPrecedenceOverTheSystemFontShorthand()
    {
        var box = new TextBlock { FontFamily = "Seravek", FontSize = 31, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
        Apply(box, "font:menu"); Assert.Equal("Seravek", box.FontFamily.Source); Assert.Equal(31, box.FontSize); Assert.Equal(FontWeights.Bold, box.FontWeight); Assert.Equal(FontStyles.Italic, box.FontStyle);
        box.ClearValue(TextBlock.FontFamilyProperty); box.ClearValue(TextBlock.FontSizeProperty); box.ClearValue(TextBlock.FontWeightProperty); box.ClearValue(TextBlock.FontStyleProperty); Evaluate(box);
        Assert.Equal(SystemFonts.CssFont("menu").Family.Source, box.FontFamily.Source); Assert.Equal(SystemFonts.CssFont("menu").Size, box.FontSize);
    }

    [Theory] [InlineData("inherit")] [InlineData("unset")] [InlineData("var(--missing)")]
    public void InheritanceCopiesComputedSystemLonghands(string value)
    {
        var root = new StackPanel(); var box = new TextBlock(); root.Children.Add(box); Apply(root, "font:small-caption"); Apply(box, "font:" + value);
        Assert.Equal(SystemFonts.CssFont("small-caption").Size, box.FontSize); Assert.Equal(SystemFonts.CssFont("small-caption").Family.Source, box.FontFamily.Source); root.Children.Clear();
    }

    [Theory] [InlineData("root")] [InlineData("run")] [InlineData("span")] [InlineData("paragraph")]
    public void SharedDocumentReadersObserveTheOriginalSystemFontScope(string kind)
    {
        var run = new Run(Text); var span = new Span(run); var paragraph = new Paragraph(span); var document = new FlowDocument(paragraph) { PagePadding = new(0) };
        using var first = new FlowDocumentPresentation(); using var second = new FlowDocumentPresentation(); first.SetDocument(document); second.SetDocument(document);
        DependencyObject owner = kind switch { "run" => run, "span" => span, "paragraph" => paragraph, _ => document };
        foreach (string role in new[] { "menu", "small-caption", "icon", "menu" })
        {
            Apply(owner, "font:" + role); Layout(first); Layout(second);
            Assert.Equal(SystemFonts.CssFont(role).Size, run.FontSize); Assert.Equal(SystemFonts.CssFont(role).Family.Source, run.FontFamily.Source);
            Assert.Equal(first.DesiredSize, second.DesiredSize); Assert.Same(span, run.Parent); Assert.Equal(Text + "\n", document.GetText());
        }
    }

    [Fact]
    public void RuntimeRefreshInvalidatesDependentsAndKeepsEachApplySnapshotConsistent()
    {
        var fields = new[] { "s_fonts", "s_cssFonts" }.Select(n => typeof(SystemFonts).GetField(n, BindingFlags.Static | BindingFlags.NonPublic)!).ToArray();
        _ = SystemFonts.CssFont("menu"); var originals = fields.Select(f => f.GetValue(null)).ToArray();
        var box = new TextBlock { Text = Text }; Apply(box, "font:menu"); Layout(box); Assert.True(box.IsMeasureValid);
        var node = CssNode.Get(box); var slots = new CssSlotAccumulator(); var context = new CssApplyContext(node, CssEngine.BuildLengthContext(node), slots);
        Assert.True(new CssSystemFontValue("menu", "font-family").TryApply(context, new CssEngine.CssSetterCollector())); var captured = slots.SystemFonts!["menu"].Family;
        var otherRole = SystemFonts.CssFont("small-caption").Family;
        try
        {
            SystemFonts.InvalidateCache(); Assert.False(box.IsMeasureValid);
            Assert.True(new CssSystemFontValue("menu", "font-size").TryApply(context, new CssEngine.CssSetterCollector())); Assert.Same(captured, slots.SystemFonts["menu"].Family);
            Assert.True(new CssSystemFontValue("small-caption", "font-style").TryApply(context, new CssEngine.CssSetterCollector())); Assert.Same(otherRole, slots.SystemFonts["small-caption"].Family);
            var next = new CssSlotAccumulator(); var fresh = new CssApplyContext(node, CssEngine.BuildLengthContext(node), next);
            Assert.True(new CssSystemFontValue("menu", "font-family").TryApply(fresh, new CssEngine.CssSetterCollector())); Assert.NotSame(captured, next.SystemFonts!["menu"].Family);
        }
        finally { for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, originals[i]); Apply(box, ""); }
    }

    [Fact]
    public void SystemFontKeyframesRetainNumericSizeEndpointsAndAllResetProperties()
    {
        var box = new TextBlock(); Apply(box, "font:menu"); Css.SetStyleSheet(box, "@keyframes ui{from{font:menu}to{font:small-caption}}");
        try
        {
            Evaluate(box); var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "ui")!);
            var sampler = new CssPropertySampler(TextBlock.FontSizeProperty, tracks[TextBlock.FontSizeProperty]);
            Assert.Equal(SystemFonts.CssFont("menu").Size, sampler.Sample(0, new("linear"))); Assert.Equal(SystemFonts.CssFont("small-caption").Size, sampler.Sample(1, new("linear")));
            Assert.Equal((SystemFonts.CssFont("menu").Size + SystemFonts.CssFont("small-caption").Size) / 2, sampler.Sample(.5, new("linear")));
            Assert.Contains(CssFontFeatureSettings.SettingsProperty, tracks.Keys); Assert.Contains(CssFontStretchValue.PercentageProperty, tracks.Keys);
        }
        finally { Css.SetStyleSheets(box, null); }
    }

    private static string Number(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private static void Apply(DependencyObject target, string text) { Css.SetStyle(target, text); Evaluate(target); }
    private static void Evaluate(DependencyObject target) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(target)); CssEvaluationScheduler.FlushIfPending(target.Dispatcher); }
    private static void Layout(FrameworkElement target) { for (int i = 0; i < 3; i++) { Evaluate(target); target.Measure(new(500, 240)); target.Arrange(new(0, 0, 500, 240)); target.UpdateLayout(); } }
}
