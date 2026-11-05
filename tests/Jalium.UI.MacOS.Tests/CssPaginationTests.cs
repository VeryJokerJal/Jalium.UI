using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPaginationTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("auto")] [InlineData("avoid")] [InlineData("avoid-page")] [InlineData("page")]
    [InlineData("left")] [InlineData("right")] [InlineData("recto")] [InlineData("verso")]
    [InlineData("column")] [InlineData("avoid-column")] [InlineData("region")] [InlineData("avoid-region")]
    public void ModernBreakGrammarIsSharedWithAuthoring(string value)
    {
        Assert.True(CssAuthoring.IsValueSupported("break-before", value)); Assert.True(CssAuthoring.IsValueSupported("break-after", value));
        Assert.Equal(value.StartsWith("avoid") || value == "auto", CssAuthoring.IsValueSupported("break-inside", value));
    }

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("1.5")] [InlineData("2e0")]
    [InlineData("2px")] [InlineData("100%")] [InlineData("auto")]
    public void InvalidLineCountsAreRejected(string value)
    { Assert.False(CssAuthoring.IsValueSupported("orphans", value)); Assert.False(CssAuthoring.IsValueSupported("widows", value)); }

    [Theory]
    [InlineData("1", 1)] [InlineData("calc(1.5)", 2)] [InlineData("calc(-4)", 1)] [InlineData("calc(1e20)", int.MaxValue)]
    public void IntegerMathUsesTheSharedRoundingAndRangeRules(string value, int expected)
    {
        var p = Para(3); var doc = Doc(p); Css.SetStyle(p, $"orphans:{value};widows:{value};"); Flush();
        Assert.Equal(expected, p.GetValue(CssFragmentationProperties.OrphansProperty)); Assert.Equal(expected, p.GetValue(CssFragmentationProperties.WidowsProperty));
    }

    [Theory]
    [InlineData("break-before:page", false)] [InlineData("break-after:page", true)]
    [InlineData("page-break-before:always", false)] [InlineData("page-break-after:always", true)]
    public void ForcedModernAndLegacyBreaksKeepOriginalSourceOffsets(string style, bool after)
    {
        var a = Para(1); var b = Para(1); var doc = Doc(a, b); Css.SetStyle(after ? a : b, style); Flush();
        Assert.Equal(2, doc.ViewerPaginator.PageCount); Assert.Equal(2, Page(doc, b));
        Assert.Equal(doc.GetDocumentOffset(b, 0), ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageStartOffset(2));
        Assert.Same(b, doc.Blocks[1]); Assert.Null(doc.Parent);
    }

    [Theory]
    [InlineData("left", false, 2)] [InlineData("right", false, 3)]
    [InlineData("recto", false, 3)] [InlineData("verso", false, 2)]
    [InlineData("left", true, 3)] [InlineData("right", true, 2)]
    [InlineData("recto", true, 3)] [InlineData("verso", true, 2)]
    public void PageSidesInsertOnlyTheRequiredBlankSpreadPage(string side, bool rtl, int expected)
    {
        var a = Para(1); var b = Para(1); var doc = Doc(a, b); doc.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Css.SetStyle(b, "break-before:" + side); Flush(); Assert.Equal(expected, doc.ViewerPaginator.PageCount); Assert.Equal(expected, Page(doc, b));
        if (expected == 3)
        {
            Assert.Equal(((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageStartOffset(2), ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageStartOffset(3));
            var visual = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual);
            Assert.Equal(0, visual.GetChildLayoutClip()!.Bounds.Height);
        }
    }

    [Fact]
    public void LaterBeforeSideWinsWhileForcedBreaksOverrideAvoidance()
    {
        var a = Para(1); var b = Para(1); var section = new Section(); section.Blocks.Add(a); section.Blocks.Add(b); var doc = Doc(section);
        Css.SetStyle(section, "break-inside:avoid;"); Css.SetStyle(a, "break-after:left;"); Css.SetStyle(b, "break-before:right;"); Flush();
        Assert.Equal(3, doc.ViewerPaginator.PageCount); Assert.Equal(3, Page(doc, b));
    }

    [Fact]
    public void FirstAndLastForcedBreaksDoNotCreateContentlessLeadingOrTrailingPages()
    {
        var a = Para(1); var b = Para(1); var doc = Doc(a, b);
        Css.SetStyle(a, "break-before:left;"); Css.SetStyle(b, "break-before:left;break-after:page;"); Flush();
        Assert.Equal(3, doc.ViewerPaginator.PageCount); Assert.Equal(1, Page(doc, a)); Assert.Equal(3, Page(doc, b));
        Css.SetStyle(b, "break-after:page;"); Flush(); Assert.Equal(1, doc.ViewerPaginator.PageCount);
    }

    [Theory]
    [InlineData("column")] [InlineData("region")] [InlineData("avoid-column")] [InlineData("avoid-region")]
    public void OtherFragmentationAxesDoNotForcePagesInAPageOnlyContext(string value)
    { var a = Para(1); var b = Para(1); var doc = Doc(a, b); Css.SetStyle(b, "break-before:" + value); Flush(); Assert.Equal(1, doc.ViewerPaginator.PageCount); }

    [Theory]
    [InlineData("avoid")] [InlineData("avoid-page")]
    public void NestedAvoidanceMovesAnEntireFittingSection(string value)
    {
        var prefix = Para(2); var a = Para(2); var b = Para(2); var section = new Section(); section.Blocks.Add(a); section.Blocks.Add(b); var doc = Doc(prefix, section);
        Css.SetStyle(section, "break-inside:" + value); Flush(); Assert.Equal(2, Page(doc, a)); Assert.Equal(2, Page(doc, b));
        Css.SetStyle(section, "break-inside:avoid-column;"); Flush(); Assert.Equal(1, Page(doc, a)); Assert.Equal(2, Page(doc, b));
    }

    [Fact]
    public void OrphansMoveAParagraphAndWidowsBacktrackTheLastFragment()
    {
        var prefix = Para(2); var body = Para(5); var doc = Doc(prefix, body); Css.SetStyle(body, "orphans:3;widows:2;"); Flush();
        Assert.Equal(2, Page(doc, body)); Assert.Equal(3, doc.ViewerPaginator.PageCount);
        var single = Para(5); var alone = Doc(single); Flush();
        Assert.Equal(LineStarts(single)[3], ((IFlowDocumentPageMetrics)alone.ViewerPaginator).GetPageStartOffset(2));
        Css.SetStyle(single, "orphans:1;widows:1;"); Flush();
        Assert.Equal(LineStarts(single)[4], ((IFlowDocumentPageMetrics)alone.ViewerPaginator).GetPageStartOffset(2));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KeepWithNextPreservesAHeadingWithFollowingContent(bool native)
    {
        var prefix = Para(1); var heading = Para(2); var body = Para(2); var doc = Doc(prefix, heading, body);
        if (native) heading.KeepWithNext = true; else Css.SetStyle(heading, "break-after:avoid;"); Flush();
        Assert.Equal(2, Page(doc, heading)); Assert.Equal(2, Page(doc, body));
    }

    [Fact]
    public void NativeKeepTogetherAndLineLimitsUseTheSamePlanner()
    {
        var prefix = Para(2); var body = Para(3); body.KeepTogether = true; var doc = Doc(prefix, body); Flush(); Assert.Equal(2, Page(doc, body));
        var p = Para(5); p.MinWidowLines = 3; p.MinOrphanLines = 2; var alone = Doc(p); Flush();
        Assert.Equal(LineStarts(p)[2], ((IFlowDocumentPageMetrics)alone.ViewerPaginator).GetPageStartOffset(2));
    }

    [Fact]
    public void ImpossibleAvoidanceAndHugeLineCountsRelaxWithoutLosingRowsOrLooping()
    {
        var p = Para(11); var doc = Doc(p); Css.SetStyle(p, "break-inside:avoid;orphans:2147483647;widows:2147483647;"); Flush();
        using var layout = Layout(doc); var plan = FlowDocumentPagination.Build(layout, 100, FlowDirection.LeftToRight);
        Assert.Equal(11, plan.Sum(fragment => fragment.EndLine - fragment.FirstLine)); Assert.All(plan, fragment => Assert.True(fragment.EndLine > fragment.FirstLine)); Assert.InRange(plan.Count, 2, 11);
    }

    [Fact]
    public void CountsInheritAndCssWideLegacyAliasesShareCascadeStorage()
    {
        var p = Para(5); var doc = Doc(p); Css.SetStyle(doc, "orphans:3;widows:4;"); Css.SetStyle(p, "break-before:page;page-break-before:initial;"); Flush();
        Assert.Equal(3, p.GetValue(CssFragmentationProperties.OrphansProperty)); Assert.Equal(4, p.GetValue(CssFragmentationProperties.WidowsProperty));
        Assert.Equal(CssFragmentBreak.Auto, p.GetValue(CssFragmentationProperties.BeforeProperty));
        Css.SetStyle(p, "orphans:initial;widows:unset;break-inside:avoid;page-break-inside:initial;"); Flush();
        Assert.Equal(2, p.GetValue(CssFragmentationProperties.OrphansProperty)); Assert.Equal(4, p.GetValue(CssFragmentationProperties.WidowsProperty)); Assert.Equal(CssFragmentBreak.Auto, p.GetValue(CssFragmentationProperties.InsideProperty));
        Css.SetStyle(doc, string.Empty); Flush(); Assert.Equal(2, p.GetValue(CssFragmentationProperties.WidowsProperty));
    }

    [Fact]
    public void InvalidLaterDeclarationsDoNotEraseEarlierValidValues()
    {
        var p = Para(1); var doc = Doc(p); Css.SetStyle(p, "orphans:3;orphans:0;widows:4;widows:1.5;break-before:right;page-break-before:recto;"); Flush();
        Assert.Equal(3, p.GetValue(CssFragmentationProperties.OrphansProperty)); Assert.Equal(4, p.GetValue(CssFragmentationProperties.WidowsProperty)); Assert.Equal(CssFragmentBreak.Right, p.GetValue(CssFragmentationProperties.BeforeProperty));
    }

    [Theory]
    [InlineData(false, 18d)] [InlineData(true, 28d)]
    public void ForcedBreaksPreserveFollowingMarginsAndBothKindsKeepPadding(bool forced, double expectedTop)
    {
        var prefix = Para(3); var body = Para(2); body.Margin = new(0, 10, 0, 0); body.Padding = new(0, 6, 0, 0); body.BorderThickness = new(0, 4, 0, 0);
        var doc = Doc(prefix, body); if (forced) Css.SetStyle(body, "break-before:page;"); Flush();
        var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual);
        Assert.Equal(expectedTop, page.Paragraphs[1].TransformToAncestor(page).Y, 3);
    }

    [Fact]
    public void FirstChildBreakPropagatesThroughItsSectionAndPreservesAncestorInsets()
    {
        var child = Para(1); child.Margin = new(0, 3, 0, 0); child.Padding = new(0, 2, 0, 0);
        var section = new Section { Margin = new(0, 7, 0, 0), Padding = new(0, 5, 0, 0) }; section.Blocks.Add(child);
        var doc = Doc(Para(1), section); Css.SetStyle(section, "break-before:right;"); Css.SetStyle(child, "break-before:left;"); Flush();
        Assert.Equal(2, doc.ViewerPaginator.PageCount); var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual);
        Assert.Equal(25, page.Paragraphs[1].TransformToAncestor(page).Y, 3);
    }

    [Fact]
    public void ListItemAvoidanceUsesTheOriginalItemAsItsContainingBox()
    {
        var body = Para(4); var item = new ListItem(); item.Blocks.Add(body); var list = new Jalium.UI.Documents.List(); list.ListItems.Add(item);
        var doc = Doc(Para(2), list); Css.SetStyle(item, "break-inside:avoid;"); Flush(); Assert.Equal(2, Page(doc, body));
    }

    [Fact]
    public void VariableChangesAndLocalCssCountsOverrideNativeHintsAndRepaginate()
    {
        var body = Para(5); var doc = Doc(Para(2), body); Css.SetStyle(doc, "--n:3;orphans:var(--n);widows:2;"); Flush(); Assert.Equal(2, Page(doc, body));
        Css.SetStyle(doc, "--n:1;orphans:var(--n);widows:1;"); Flush(); Assert.Equal(1, Page(doc, body));
        var single = Para(5); single.MinWidowLines = 3; var alone = Doc(single); Css.SetStyle(single, "widows:1;orphans:1;"); Flush();
        Assert.Equal(LineStarts(single)[4], ((IFlowDocumentPageMetrics)alone.ViewerPaginator).GetPageStartOffset(2));
    }

    [Fact]
    public void NativeSectionBreaksAndCssAutoUseOneEffectiveBoundary()
    {
        var child = Para(1); var section = new Section { BreakPageBefore = true }; section.Blocks.Add(child); var doc = Doc(Para(1), section);
        Assert.Equal(2, Page(doc, child)); Css.SetStyle(section, "break-before:auto;"); Flush(); Assert.Equal(1, Page(doc, child));
    }

    [Fact]
    public void AuthoringExposesTheSameDefaultsInheritanceAndLegacyVocabulary()
    {
        var lines = CssAuthoring.GetProperty("widows"); Assert.True(lines.IsInherited); Assert.Equal("2", lines.InitialValue); Assert.Contains("3", lines.Values);
        var before = CssAuthoring.GetProperty("break-before"); Assert.False(before.IsInherited); Assert.Equal("auto", before.InitialValue); Assert.Contains("recto", before.Values);
        var legacy = CssAuthoring.GetProperty("page-break-before"); Assert.True(legacy.IsShorthand); Assert.Contains("always", legacy.Values); Assert.DoesNotContain("recto", legacy.Values);
    }

    private static Paragraph Para(int lines)
    {
        var p = new Paragraph { Margin = new(0) };
        for (var line = 1; line <= lines; line++) { if (line > 1) p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run("第" + line + "行原文")); }
        return p;
    }
    private static FlowDocument Doc(params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PageWidth = 240, PageHeight = 116, PagePadding = new(8), Foreground = Brushes.Black };
        foreach (var block in blocks) doc.Blocks.Add(block); return doc;
    }
    private static FlowDocumentPresentation Layout(FlowDocument doc)
    { var layout = new FlowDocumentPresentation(observe: false); layout.SetDocument(doc); layout.EnsureDocumentLayout(doc.PageWidth - doc.PagePadding.Left - doc.PagePadding.Right); return layout; }
    private static int Page(FlowDocument doc, Paragraph p) => ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(p, 0));
    private static int[] LineStarts(Paragraph p)
    {
        var starts = new List<int>(); var offset = 0; foreach (var inline in p.Inlines) { if (inline is Run run) { starts.Add(offset); offset += run.Text.Length; } else if (inline is LineBreak) offset++; } return starts.ToArray();
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
}
