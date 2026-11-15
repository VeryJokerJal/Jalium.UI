using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontNumericTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("normal", 0)]
    [InlineData("lining-nums", 1)] [InlineData("oldstyle-nums", 2)]
    [InlineData("proportional-nums", 4)] [InlineData("tabular-nums", 8)]
    [InlineData("diagonal-fractions", 16)] [InlineData("stacked-fractions", 32)]
    [InlineData("ordinal", 64)] [InlineData("slashed-zero", 128)]
    [InlineData("oldstyle-nums tabular-nums diagonal-fractions ordinal slashed-zero", 218)]
    [InlineData("slashed-zero ordinal stacked-fractions proportional-nums lining-nums", 229)]
    [InlineData("TaBuLaR-NuMs\n ordinal", 72)]
    [InlineData("ordinal/**/slashed-zero", 192)]
    [InlineData("tabular\\2d nums", 8)] [InlineData("o\\72 dinal", 64)]
    public void EveryKeywordAndExclusiveGroupIsAnAuthorableComputedValue(string value, int expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-numeric", value));
        var text = Box(); Apply(text, "font-variant-numeric:" + value + ";");
        Assert.Equal((CssNumericVariant)expected, Numeric(text));
    }

    [Theory]
    [InlineData("")] [InlineData("none")] [InlineData("normal ordinal")] [InlineData("ordinal normal")]
    [InlineData("normal normal")] [InlineData("ordinal ordinal")] [InlineData("slashed-zero slashed-zero")]
    [InlineData("lining-nums oldstyle-nums")] [InlineData("oldstyle-nums lining-nums")]
    [InlineData("tabular-nums proportional-nums")] [InlineData("proportional-nums tabular-nums")]
    [InlineData("diagonal-fractions stacked-fractions")] [InlineData("stacked-fractions diagonal-fractions")]
    [InlineData("tabular-nums tabular-nums")] [InlineData("tabular-nums,ordinal")]
    [InlineData("tabular-nums 1")] [InlineData("'tabular-nums'")] [InlineData("slashed-zero()")] [InlineData("inherit ordinal")]
    public void InvalidValuesDoNotReplaceAPreviousDeclaration(string invalid)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant-numeric", invalid));
        var text = Box(); Apply(text, "font-variant-numeric:tabular-nums;font-variant-numeric:" + invalid + ";");
        Assert.Equal(CssNumericVariant.Tabular, Numeric(text));
    }

    public static IEnumerable<object[]> LegalCombinations()
    {
        string[][] groups = [["", "lining-nums", "oldstyle-nums"], ["", "proportional-nums", "tabular-nums"],
            ["", "diagonal-fractions", "stacked-fractions"], ["", "ordinal"], ["", "slashed-zero"]];
        foreach (string figure in groups[0]) foreach (string spacing in groups[1]) foreach (string fraction in groups[2])
        foreach (string ordinal in groups[3]) foreach (string zero in groups[4])
        {
            var words = new[] { figure, spacing, fraction, ordinal, zero }.Where(s => s.Length > 0).ToArray();
            var text = words.Length == 0 ? "normal" : string.Join(' ', words);
            yield return [text];
            if (words.Length > 1) yield return [string.Join(' ', words.Reverse())];
        }
    }

    [Theory]
    [MemberData(nameof(LegalCombinations))]
    public void EveryLegalGroupCombinationIsAcceptedInEitherOrder(string value)
        => Assert.True(CssAuthoring.IsValueSupported("font-variant-numeric", value), value);

    [Theory]
    [InlineData("lining-nums", 1)] [InlineData("oldstyle-nums", 2)]
    [InlineData("proportional-nums", 4)] [InlineData("tabular-nums", 8)]
    [InlineData("diagonal-fractions", 16)] [InlineData("stacked-fractions", 32)]
    [InlineData("ordinal", 64)] [InlineData("slashed-zero", 128)]
    public void CompiledCssBuilderUsesTheSameNumericMapping(string keyword, int expected)
    {
        var text = Box(); var builder = new CssStyleSheetBuilder(1);
        var compound = builder.Compound("TextBlock", false, null, false, true, false, null, null, null, 0, null, null);
        builder.Rule([builder.Selector([compound], [], 1, 0, 0, false, false)],
            [builder.Declaration("font-variant-numeric", keyword, false)], -1, null, -1);
        Css.GetStyleSheets(text).Add(builder.Build()); EvaluateTree(text);
        Assert.Equal((CssNumericVariant)expected, Numeric(text));
    }

    [Fact]
    public void AuthoringDescribesTheLonghandAndItsCompleteKeywordSet()
    {
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("font-variant-numeric").Support);
        Assert.Contains("oldstyle-nums", CssAuthoring.GetValues("font-variant-numeric"));
        Assert.Contains("stacked-fractions", CssAuthoring.GetValues("font-variant-numeric"));
    }

    [Theory]
    [InlineData("inherit", 138)] [InlineData("unset", 138)] [InlineData("initial", 0)]
    [InlineData("normal", 0)] [InlineData("all:initial", 0)] [InlineData("all:unset", 138)]
    [InlineData("font:32px Seravek", 0)]
    public void InheritanceAndResetsFollowCssRatherThanRetainingOldFeatures(string value, int expected)
    {
        var child = Box(); var parent = new Border { Child = child };
        Apply(parent, "font-variant-numeric:oldstyle-nums tabular-nums slashed-zero;");
        Apply(child, value.Contains(':') ? value + ";" : "font-variant-numeric:" + value + ";");
        EvaluateTree(parent);
        Assert.Equal((CssNumericVariant)expected, Numeric(child));
    }

    [Fact]
    public void VariablesImportanceAndRemovalRestoreTheActualInheritedValue()
    {
        var child = Box(); var parent = new Border { Child = child };
        Apply(parent, "--numbers:oldstyle-nums tabular-nums;font-variant-numeric:lining-nums;");
        Apply(child, "font-variant-numeric:var(--numbers)!important;font-variant-numeric:ordinal;");
        Assert.Equal(CssNumericVariant.OldStyle | CssNumericVariant.Tabular, Numeric(child));
        Apply(child, ""); Assert.Equal(CssNumericVariant.Lining, Numeric(child));
        Apply(parent, "font-variant-numeric:proportional-nums;"); EvaluateTree(parent);
        Assert.Equal(CssNumericVariant.Proportional, Numeric(child));
    }

    [Theory]
    [InlineData("lining-nums", 0x6c6e756d)] [InlineData("oldstyle-nums", 0x6f6e756d)]
    [InlineData("proportional-nums", 0x706e756d)] [InlineData("tabular-nums", 0x746e756d)]
    [InlineData("diagonal-fractions", 0x66726163)] [InlineData("stacked-fractions", 0x61667263)]
    [InlineData("ordinal", 0x6f72646e)] [InlineData("slashed-zero", 0x7a65726f)]
    public void CssMeasurementMatchesAnIndependentlyConfiguredNativeFormat(string keyword, uint tag)
    {
        const string sample = "111 888 1234 1/2 0 1st";
        var text = Box(sample); Apply(text, "font-variant-numeric:" + keyword + ";");
        using var expected = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, expected.SetFontFeatures([new(tag, 1)]));
        var reference = expected.MeasureText(sample, 100000, 1000);
        var measured = Measure(text);
        Assert.Equal(reference.Width, measured.Width, 3); Assert.Equal("Seravek", text.FontFamily.Source);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(text.FontFamily.GetRenderingSource(text), out var features, out var family));
        Assert.Equal(new FontFeature(tag, 1), Assert.Single(features)); Assert.Equal("Seravek", family);
    }

    [Fact]
    public void FeatureChangesInvalidateExistingInlineMeasurementsAndResetToTheirOldWidth()
    {
        var run = new Run("111111"); var span = new Span(run); var text = Box(); text.Inlines.Add(span);
        var originalParent = span.Parent;
        Apply(span, "font-variant-numeric:proportional-nums;"); Layout(text); var normal = text.DesiredSize.Width;
        Apply(span, "font-variant-numeric:tabular-nums;"); Layout(text); var tabular = text.DesiredSize.Width;
        Assert.True(Math.Abs(tabular - normal) > 1, $"Widths did not change: {normal}/{tabular}");
        Apply(span, "font-variant-numeric:proportional-nums;"); Layout(text); Assert.Equal(normal, text.DesiredSize.Width, 3);
        Assert.Same(span, run.Parent); Assert.Same(originalParent, span.Parent); Assert.Same(span, Assert.Single(text.Inlines)); Assert.Equal("111111", run.Text);
    }

    [Fact]
    public void ASharedDocumentReceivesNumericUpdatesWithoutReplacingItsNodes()
    {
        var run = new Run("111111"); var span = new Span(run); var paragraph = new Paragraph(span);
        var document = new FlowDocument(paragraph) { PagePadding = new(0), FontFamily = "Seravek", FontSize = 32 };
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation();
        one.SetDocument(document); two.SetDocument(document);
        Apply(document, "font-variant-numeric:proportional-nums;"); Layout(one); Layout(two);
        var normal = MeasureRun(run).Width;
        Apply(document, "font-variant-numeric:tabular-nums;"); Layout(one); Layout(two);
        Assert.True(MeasureRun(run).Width > normal + 1); Assert.Equal(CssNumericVariant.Tabular, Numeric(run));
        Assert.Same(document, one.Document); Assert.Same(document, two.Document); Assert.Same(span, run.Parent);
        Assert.Equal("111111\n", document.GetText());
    }

    [Fact]
    public void MarkersHaveTabularUaDefaultsAndAuthorRulesCanOverrideOrRevertThem()
    {
        var item = Box("正文"); Apply(item, "display:list-item;list-style-type:decimal;font-variant-numeric:oldstyle-nums;");
        EvaluateTree(item); var marker = Assert.Single(CssGeneratedContent.Markers(CssNode.Get(item)));
        Assert.Equal(CssNumericVariant.Tabular, Numeric(marker));
        Css.SetStyleSheet(item, "TextBlock::marker{font-variant-numeric:oldstyle-nums proportional-nums;}"); EvaluateTree(item);
        Assert.Equal(CssNumericVariant.OldStyle | CssNumericVariant.Proportional, Numeric(marker));
        Css.SetStyleSheet(item, "TextBlock::marker{font-variant-numeric:revert;}"); EvaluateTree(item);
        Assert.Equal(CssNumericVariant.Tabular, Numeric(marker));
    }

    [Fact]
    public void WidthKeysAndBlockedFontPlansKeepTheirFeaturesAndPublicFamily()
    {
        var text = Box(); Apply(text, "font-variant-numeric:tabular-nums;font-stretch:75%;");
        var source = text.FontFamily.GetRenderingSource(text);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family));
        Assert.True(FontWidthRenderingSource.TryUnwrap(family, out var width, out _)); Assert.Equal(75, width);
        Assert.Equal(features, Unwrap(FontWidthRenderingSource.ForFormattedText(source, 3)));
        var blocked = FontFeatureRenderingSource.Wrap(CssFontFaces.BlockPrefix + "Seravek", features);
        Assert.True(CssFontFaces.IsBlocked(CssFontFaces.MaterializeSource(blocked, "123")));
        Assert.Equal("Seravek", text.FontFamily.Source);
        static FontFeature[] Unwrap(string key) { Assert.True(FontFeatureRenderingSource.TryUnwrap(key, out var result, out _)); return result; }
    }

    private static void EvaluateTree(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static CssNumericVariant Numeric(DependencyObject owner) => (CssNumericVariant)owner.GetValue(CssFontNumeric.VariantProperty)!;
    private static TextBlock Box(string text = "") => new() { Text = text, FontFamily = "Seravek", FontSize = 32 };
    private static void Apply(DependencyObject owner, string css) { Css.SetStyle(owner, css); CssEngine.EvaluateElement(CssNode.Get(owner)); }
    private static void Layout(FrameworkElement element) { element.Measure(new(500, 220)); element.Arrange(new(0, 0, 500, 220)); element.UpdateLayout(); }
    private static FormattedText Measure(TextBlock text) { var result = new FormattedText(text.Text, text.FontFamily.GetRenderingSource(text), text.FontSize); Assert.True(TextMeasurement.MeasureText(result)); return result; }
    private static FormattedText MeasureRun(Run run) { var result = new FormattedText(run.Text, run.FontFamily.GetRenderingSource(run), run.FontSize); Assert.True(TextMeasurement.MeasureText(result)); return result; }
}
