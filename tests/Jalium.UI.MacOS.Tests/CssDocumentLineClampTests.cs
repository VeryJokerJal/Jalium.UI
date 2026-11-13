using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentLineClampTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    internal const string Words = "MMMM MMMM MMMM MMMM MMMM MMMM M";
    internal static readonly string[] Scopes = ["paragraph", "section", "list", "item", "cell", "root"];
    public static IEnumerable<object[]> Cases()
    {
        foreach (var scope in Scopes)
        foreach (var count in new[] { 1, 2 })
        foreach (var marker in new[] { "no-ellipsis", "ellipsis", "'… 阅读更多'" })
        foreach (var wrap in new[] { "auto", "balance" }) yield return [scope, count, marker, wrap];
    }

    [Theory] [MemberData(nameof(Cases))]
    public void NativeDocumentScopesClampOriginalParagraphsAndRestoreTheirFullLayout(string scope, int count, string marker, string wrap)
    {
        using var f = new DocumentClampFixture(scope); f.Layout(); var original = f.Ranges().Count; var proxies = f.Body.Paragraphs.ToArray();
        f.Style($"line-clamp:{count} {marker};text-wrap-style:{wrap};"); f.Layout();
        Assert.Equal(count, f.Ranges().Count); Assert.InRange(f.Body.ExtentHeight, count * 24 - .1, count * 24 + 2.1);
        f.AssertOriginal(); Assert.Equal(proxies, f.Body.Paragraphs);
        f.Style(""); f.Layout(); Assert.Equal(original, f.Ranges().Count); Assert.Equal(proxies, f.Body.Paragraphs); f.AssertOriginal();
    }

    public static IEnumerable<object[]> ScopeCases() => Scopes.Select(scope => new object[] { scope });

    [Theory] [MemberData(nameof(ScopeCases))]
    public void NativeHeightAndInsetsConstrainAutomaticClamping(string scope)
    {
        using var f = new DocumentClampFixture(scope);
        f.Style("line-clamp:auto;max-height:52px;padding:2px;"); f.Layout(); Assert.Equal(2, f.Ranges().Count);
        Assert.InRange(f.Body.ExtentHeight, 51.9, 54.1); f.AssertOriginal();
        f.Style("line-clamp:auto;max-height:76px;padding:2px;"); f.Layout(); Assert.True(f.Ranges().Count >= 3); f.AssertOriginal();
    }

    [Theory] [MemberData(nameof(ScopeCases))]
    public void LonghandsAndLegacyGatingUseTheSameNativeDocumentPath(string scope)
    {
        using var f = new DocumentClampFixture(scope); f.Layout(); var full = f.Ranges().Count;
        f.Style("max-lines:2;block-ellipsis:ellipsis;continue:collapse;"); f.Layout(); Assert.Equal(2, f.Ranges().Count);
        f.Style("-webkit-line-clamp:1;"); f.Layout(); Assert.Equal(full, f.Ranges().Count);
        f.Style("display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:1;"); f.Layout(); Assert.Single(f.Ranges());
        f.Style("line-clamp:1;line-clamp:initial;"); f.Layout(); Assert.Equal(full, f.Ranges().Count); f.AssertOriginal();
    }

    [Fact]
    public void OneBudgetAcrossNestedBlocksSuppressesOnlyTheFollowingNativeContent()
    {
        var first = new Paragraph(new Run("M")); var nested = new Section(new Paragraph(new Run(Words))); var after = new Paragraph(new Run("FOLLOWING"));
        var section = new Section(first); section.Blocks.Add(nested); section.Blocks.Add(after);
        var outside = new Paragraph(new Run("OUTSIDE")); var doc = DocumentClampFixture.Document(section, outside);
        Css.SetStyle(section, "line-clamp:2;"); using var body = DocumentClampFixture.Present(doc);
        Assert.Single(body.Paragraphs[0].GetSourceRangeSegments(0, 1)); Assert.Single(body.Paragraphs[1].GetSourceRangeSegments(0, Words.Length));
        Assert.Empty(body.Paragraphs[2].GetSourceRangeSegments(0, 9)); Assert.Single(body.Paragraphs[3].GetSourceRangeSegments(0, 7));
        Assert.InRange(body.ExtentHeight, 71.9, 74.1);
        var provider = DocumentClampFixture.Provider(body); var hidden = provider.DocumentRange.FindText("FOLLOWING", false, false)!;
        Assert.Empty(hidden.GetBoundingRectangles()); Assert.Equal(doc.GetText(), provider.DocumentRange.GetText(-1)); hidden.Select(); Assert.Equal("FOLLOWING", body.Selection!.Text);
        Css.SetStyle(section, "line-clamp:none;"); DocumentClampFixture.Layout(body);
        Assert.NotEmpty(hidden.GetBoundingRectangles()); Assert.Same(nested, section.Blocks[1]); Assert.Same(first, section.Blocks[0]);
    }

    [Fact]
    public void SharedViewsAndThePaginatorInvalidateWhileKeepingOriginalPointers()
    {
        using var f = new DocumentClampFixture("root"); using var second = DocumentClampFixture.Present(f.Doc);
        var original = f.Doc.Blocks[0]; var pointer = f.Doc.ContentEnd; var pages = ((IDocumentPaginatorSource)f.Doc).DocumentPaginator;
        f.Doc.PageHeight = 24; pages.PageSize = new Size(150, 24); var before = pages.PageCount;
        f.Style("line-clamp:1;"); f.Layout(); DocumentClampFixture.Layout(second);
        Assert.Single(f.Ranges()); Assert.Single(second.Paragraphs[0].GetSourceRangeSegments(0, Words.Length)); Assert.Equal(1, pages.PageCount);
        Assert.True(before > 1); Assert.Same(original, f.Doc.Blocks[0]); Assert.Equal(0, f.Doc.ContentEnd.CompareTo(pointer));
        f.Style(""); f.Layout(); DocumentClampFixture.Layout(second); Assert.Equal(before, pages.PageCount); Assert.True(f.Ranges().Count > 1); f.AssertOriginal();
    }

    [Theory]
    [InlineData("M", 1)] [InlineData("M\nM", 2)] [InlineData("M\n\nM", 3)]
    public void ExactFitsAndExplicitEmptyLinesDoNotCreateAnEllipsis(string value, int lines)
    {
        var source = new Paragraph(new Run(value)); var doc = DocumentClampFixture.Document(source);
        Css.SetStyle(source, $"white-space:pre-wrap;line-clamp:{lines};"); using var body = DocumentClampFixture.Present(doc);
        Assert.Equal(lines, body.Paragraphs[0].GetDocumentLines(150).Count);
        Assert.Null(typeof(TextBlock).GetField("_cssClampMarker", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(body.Paragraphs[0]));
        Assert.Equal(value + "\n", doc.GetText());
        if (lines == 1) return;
        Css.SetStyle(source, $"white-space:pre-wrap;line-clamp:{lines - 1} no-ellipsis;"); DocumentClampFixture.Layout(body);
        Assert.Equal(lines - 1, body.Paragraphs[0].GetDocumentLines(150).Count);
    }

    [Theory] [MemberData(nameof(ScopeCases))]
    public void ZeroHeightAndConflictingBoundsUseTheNativeBoxSizingRules(string scope)
    {
        using var f = new DocumentClampFixture(scope);
        f.Style("line-clamp:auto no-ellipsis;height:0;padding:2px;"); f.Layout(); Assert.Empty(f.Ranges());
        Assert.InRange(f.Body.ExtentHeight, 3.9, 4.1);
        f.Style("line-clamp:auto no-ellipsis;min-height:48px;max-height:24px;box-sizing:border-box;"); f.Layout();
        Assert.Equal(2, f.Ranges().Count); Assert.InRange(f.Body.ExtentHeight, 47.9, 48.1); f.AssertOriginal();
        f.Style("line-clamp:auto 1 no-ellipsis;max-height:72px;"); f.Layout(); Assert.Single(f.Ranges());
    }

    [Theory]
    [InlineData("display:flow-root;", false)] [InlineData("overflow:hidden;", false)]
    [InlineData("overflow:auto;", false)] [InlineData("columns:2;column-gap:10px;line-clamp:1;", false)]
    [InlineData("line-clamp:2 no-ellipsis;", true)]
    public void IndependentNativeContextsKeepTheirOwnLinesOutsideTheOuterBudget(string innerStyle, bool innerClamp)
    {
        var text = new Paragraph(new Run(Words)); var inner = new Section(text);
        var next = new Paragraph(new Run(Words)); var tail = new Paragraph(new Run("TAIL"));
        var outer = new Section(inner); outer.Blocks.Add(next); outer.Blocks.Add(tail);
        var doc = DocumentClampFixture.Document(outer); Css.SetStyle(inner, innerStyle); Css.SetStyle(outer, "line-clamp:1 no-ellipsis;");
        using var body = DocumentClampFixture.Present(doc);
        Assert.True(body.Paragraphs[0].GetDocumentLines(body.Paragraphs[0].ActualWidth).Count >= (innerClamp ? 2 : 3));
        if (innerClamp) Assert.Equal(2, body.Paragraphs[0].GetDocumentLines(body.Paragraphs[0].ActualWidth).Count);
        Assert.Single(body.Paragraphs[1].GetDocumentLines(150)); Assert.Empty(body.Paragraphs[2].GetDocumentLines(150));
        Css.SetStyle(outer, "line-clamp:none;"); DocumentClampFixture.Layout(body);
        Assert.True(body.Paragraphs[1].GetDocumentLines(150).Count > 1); Assert.Single(body.Paragraphs[2].GetDocumentLines(150));
        Assert.Same(inner, outer.Blocks[0]); Assert.Same(text.Inlines, body.Paragraphs[0].Inlines);
    }

    [Fact]
    public void OverflowClipDoesNotStartAnIndependentNativeLineBudget()
    {
        var text = new Paragraph(new Run(Words)); var inner = new Section(text); var tail = new Paragraph(new Run("TAIL"));
        var outer = new Section(inner); outer.Blocks.Add(tail); var doc = DocumentClampFixture.Document(outer);
        Css.SetStyle(inner, "overflow:clip;"); Css.SetStyle(outer, "line-clamp:1 no-ellipsis;"); using var body = DocumentClampFixture.Present(doc);
        Assert.Single(body.Paragraphs[0].GetDocumentLines(150)); Assert.Empty(body.Paragraphs[1].GetDocumentLines(150));
    }

    [Fact]
    public void EditingMovingAndReplacingAClampedListItemInvalidatesOnlyItsCurrentDocument()
    {
        using var f = new DocumentClampFixture("item"); var item = (ListItem)f.Target; var list = (DocList)item.Parent!;
        f.Style("line-clamp:1 no-ellipsis;"); f.Layout(); var proxy = f.Body.Paragraphs[0];
        f.Runs[0].Text = "M"; f.Runs[1].Text = ""; f.Layout(); Assert.Single(f.Ranges()); Assert.Same(proxy, f.Body.Paragraphs[0]);
        f.Runs[0].Text = Words; f.Layout(); Assert.Single(f.Ranges()); Assert.Same(proxy, f.Body.Paragraphs[0]);
        var other = new ListItem(new Paragraph(new Run(Words))); list.ListItems[0] = other; f.Layout();
        Assert.True(f.Body.Paragraphs[0].GetDocumentLines(130).Count > 1); Assert.Null(item.Parent);
        Css.SetStyle(other, "line-clamp:2 no-ellipsis;"); f.Layout(); Assert.Equal(2, f.Body.Paragraphs[0].GetDocumentLines(130).Count);
        list.ListItems.Remove(other); list.ListItems.Add(item); f.Layout(); Assert.Single(f.Ranges());
        f.Style("line-clamp:none;"); f.Layout(); Assert.True(f.Ranges().Count > 1); Assert.Same(f.Paragraph.Inlines, f.Body.Paragraphs[0].Inlines);
    }

    [Theory]
    [InlineData("paragraph")] [InlineData("section")] [InlineData("root")]
    public void ClampingRetainsTheFullIntrinsicInlineSize(string scope)
    {
        using var f = new DocumentClampFixture(scope); f.Style("width:max-content;"); f.Layout(); var full = f.Body.Paragraphs[0].ActualWidth;
        f.Style("width:max-content;line-clamp:1;"); f.Layout(); Assert.Equal(full, f.Body.Paragraphs[0].ActualWidth, 3);
        f.Style("width:min-content;"); f.Layout(); var minimum = f.Body.Paragraphs[0].ActualWidth;
        f.Style("width:min-content;line-clamp:1;"); f.Layout(); Assert.Equal(minimum, f.Body.Paragraphs[0].ActualWidth, 3); f.AssertOriginal();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParagraphClampsBeforeNativeAndCssColumnFragmentation(bool native)
    {
        using var f = new DocumentClampFixture("paragraph");
        if (native) { f.Doc.ColumnWidth = 65; f.Doc.ColumnGap = 20; }
        else Css.SetStyle(f.Doc, "font-family:'Courier New';font-size:16px;line-height:24px;width:150px;columns:2;column-gap:20px;");
        f.Style("line-clamp:2 no-ellipsis;orphans:1;widows:1;"); f.Layout();
        var rectangles = f.Body.Paragraphs[0].DocumentTextBounds(0, Words.Length); Assert.Equal(2, rectangles.Count);
        Assert.True(rectangles[1].X > rectangles[0].X + 40); Assert.Equal(rectangles[0].Y, rectangles[1].Y, 3); f.AssertOriginal();
        f.Style("line-clamp:none;"); f.Layout(); Assert.True(f.Body.Paragraphs[0].DocumentTextBounds(0, Words.Length).Count > 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RootClampingIsIgnoredWhenItEstablishesColumns(bool native)
    {
        using var f = new DocumentClampFixture("root");
        if (native) { f.Doc.ColumnWidth = 65; f.Doc.ColumnGap = 20; f.Style("line-clamp:1;"); }
        else f.Style("columns:2;column-gap:20px;line-clamp:1;");
        f.Layout(); Assert.True(f.Body.Paragraphs[0].DocumentTextBounds(0, Words.Length).Count > 2); f.AssertOriginal();
    }

    [Fact]
    public void AClampedCellKeepsTableTracksAndFollowingContentOutOfTheOuterLineBudget()
    {
        var first = new Paragraph(new Run("FIRST")); var source = new Paragraph(new Run(Words)); var cell = new TableCell(source);
        var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table(); table.RowGroups.Add(group);
        var after = new Paragraph(new Run(Words)); var tail = new Paragraph(new Run("TAIL")); var outer = new Section(first);
        outer.Blocks.Add(table); outer.Blocks.Add(after); outer.Blocks.Add(tail); var doc = DocumentClampFixture.Document(outer);
        Css.SetStyle(outer, "line-clamp:2 no-ellipsis;"); Css.SetStyle(cell, "line-clamp:1 no-ellipsis;"); using var body = DocumentClampFixture.Present(doc);
        Assert.Single(body.Paragraphs[0].GetDocumentLines(150)); Assert.Single(body.Paragraphs[1].GetDocumentLines(150));
        Assert.Single(body.Paragraphs[2].GetDocumentLines(150)); Assert.Empty(body.Paragraphs[3].GetDocumentLines(150)); Assert.InRange(body.ExtentHeight, 71.9, 72.1);
        Css.SetStyle(cell, "line-clamp:2 no-ellipsis;"); DocumentClampFixture.Layout(body); Assert.InRange(body.ExtentHeight, 95.9, 96.1);
        Assert.Equal(2, body.Paragraphs[1].GetDocumentLines(150).Count); Assert.Single(body.Paragraphs[2].GetDocumentLines(150));
    }

    [Fact]
    public void SharedViewsAtDifferentWidthsRetainTheirOwnEndpointsAndRestoreAfterRepeatedChanges()
    {
        using var f = new DocumentClampFixture("paragraph"); using var second = DocumentClampFixture.Present(f.Doc);
        Css.SetStyle(f.Doc, "font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            f.Style("line-clamp:2;"); f.Layout(); LayoutWidth(second, 95);
            var wide = f.Body.Paragraphs[0].GetDocumentLines(150); var narrow = second.Paragraphs[0].GetDocumentLines(95);
            Assert.Equal(150, f.Body.Paragraphs[0].ActualWidth, 3); Assert.Equal(95, second.Paragraphs[0].ActualWidth, 3);
            Assert.Equal(2, wide.Count); Assert.Equal(2, narrow.Count); Assert.True(wide[^1].End > narrow[^1].End);
            Assert.Equal(wide[^1].End, f.Body.Paragraphs[0].GetDocumentLines(150)[^1].End);
            f.Style(""); f.Layout(); LayoutWidth(second, 95); Assert.True(f.Ranges().Count > 2); Assert.True(second.Paragraphs[0].GetDocumentLines(95).Count > 2);
        }
        f.AssertOriginal();
    }

    private static void LayoutWidth(FlowDocumentPresentation body, double width)
    {
        for (var pass = 0; pass < 3; pass++)
        { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(width, double.PositiveInfinity)); body.Arrange(new(0, 0, width, body.DesiredSize.Height)); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ListCollectionInterfacesPreserveOwnershipAndDetachContentNotifications(bool nonGeneric)
    {
        var list = new DocList(); var first = new ListItem(new Paragraph(new Run("FIRST"))); var second = new ListItem(new Paragraph(new Run(Words)));
        var doc = DocumentClampFixture.Document(list); using var body = DocumentClampFixture.Present(doc);
        if (nonGeneric) { var items = (System.Collections.IList)list.ListItems; items.Add(first); items.Insert(1, second); }
        else { var items = (IList<ListItem>)list.ListItems; items.Add(first); items.Insert(1, second); }
        DocumentClampFixture.Layout(body); Assert.Equal(2, body.Paragraphs.Count); Assert.Same(list, second.Parent); Assert.Same(first, second.PreviousListItem);
        Css.SetStyle(second, "line-clamp:1 no-ellipsis;"); DocumentClampFixture.Layout(body); Assert.Single(body.Paragraphs[1].GetDocumentLines(130));
        var replacement = new ListItem(new Paragraph(new Run(Words)));
        if (nonGeneric) ((System.Collections.IList)list.ListItems)[1] = replacement; else ((IList<ListItem>)list.ListItems)[1] = replacement;
        DocumentClampFixture.Layout(body); Assert.Null(second.Parent); Assert.Same(first, replacement.PreviousListItem); Assert.Same(replacement, first.NextListItem);
        Assert.True(body.Paragraphs[1].GetDocumentLines(130).Count > 1);
        var changes = 0; list.TextContentChanged += (_, _) => changes++;
        Css.SetStyle(second, "line-clamp:2;"); Assert.Equal(0, changes);
        if (nonGeneric) ((System.Collections.IList)list.ListItems).Clear(); else ((IList<ListItem>)list.ListItems).Clear();
        DocumentClampFixture.Layout(body); Assert.Empty(body.Paragraphs); Assert.Null(first.Parent); Assert.Null(replacement.Parent); Assert.Null(first.NextListItem);
    }

    [Fact]
    public void AutomaticClampingIncludesNestedMinimumHeightsAndAncestorInsets()
    {
        var source = new Paragraph(new Run(Words + " " + Words)); var child = new Section(source); var after = new Paragraph(new Run("AFTER"));
        var outer = new Section(child); outer.Blocks.Add(after); var doc = DocumentClampFixture.Document(outer);
        Css.SetStyle(child, "min-height:72px;"); Css.SetStyle(outer, "line-clamp:auto no-ellipsis;max-height:80px;padding:2px;border:2px solid red;");
        using var body = DocumentClampFixture.Present(doc); Assert.Equal(3, body.Paragraphs[0].GetDocumentLines(142).Count);
        Assert.Empty(body.Paragraphs[1].GetDocumentLines(142)); Assert.InRange(body.ExtentHeight, 79.9, 80.1);
        Css.SetStyle(child, "min-height:96px;"); DocumentClampFixture.Layout(body); Assert.Empty(body.Paragraphs[0].GetDocumentLines(142));
        Assert.InRange(body.ExtentHeight, 7.9, 8.1);
    }

    [Theory]
    [InlineData("中文段落逐行保留，隐藏内容仍然属于原始文档，切换以后完整恢复。")]
    [InlineData("e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301 e\u0301")]
    [InlineData("👨‍👩‍👧‍👦 👨‍👩‍👧‍👦 👨‍👩‍👧‍👦 👨‍👩‍👧‍👦 👨‍👩‍👧‍👦 👨‍👩‍👧‍👦 👨‍👩‍👧‍👦 👨‍👩‍👧‍👦")]
    public void NativeDocumentEndpointsRemainOnOriginalGraphemeBoundaries(string value)
    {
        var run = new Run(value); var source = new Paragraph(run); var doc = DocumentClampFixture.Document(source);
        Css.SetStyle(source, "line-clamp:1;"); using var body = DocumentClampFixture.Present(doc); var line = Assert.Single(body.Paragraphs[0].GetDocumentLines(150));
        Assert.Contains(line.End, System.Globalization.StringInfo.ParseCombiningCharacters(value).Append(value.Length));
        Assert.Equal(value + "\n", DocumentClampFixture.Provider(body).DocumentRange.GetText(-1)); Assert.Same(run, Assert.Single(source.Inlines));
        var at = body.OffsetFromPoint(new(149, 12)); Assert.InRange(at, 0, line.End);
        Css.SetStyle(source, "line-clamp:none;"); DocumentClampFixture.Layout(body); Assert.True(body.Paragraphs[0].GetDocumentLines(150).Count > 1);
    }

    [Fact]
    public void AFixedHeightChildKeepsItsVisibleOverflowWhileLaterSiblingsAreSuppressed()
    {
        var source = new Paragraph(new Run(Words + " " + Words)); var child = new Section(source); var after = new Paragraph(new Run("AFTER"));
        var outer = new Section(child); outer.Blocks.Add(after); var doc = DocumentClampFixture.Document(outer);
        Css.SetStyle(child, "height:72px;"); Css.SetStyle(outer, "line-clamp:auto no-ellipsis;max-height:80px;padding:2px;border:2px solid red;");
        using var body = DocumentClampFixture.Present(doc); var full = body.Paragraphs[0].GetDocumentLines(142).Count;
        Assert.True(full > 3); Assert.Empty(body.Paragraphs[1].GetDocumentLines(142)); Assert.True(body.ExtentHeight > 80);
        Css.SetStyle(outer, "line-clamp:none;padding:2px;border:2px solid red;"); DocumentClampFixture.Layout(body);
        Assert.Equal(full, body.Paragraphs[0].GetDocumentLines(142).Count); Assert.Single(body.Paragraphs[1].GetDocumentLines(142));
    }

    [Fact]
    public void ACellHeightPercentageReflowKeepsTheRetainedEndpointAndRowHeight()
    {
        using var f = new DocumentClampFixture("cell"); Css.SetStyle(f.Paragraph, "height:50%;"); f.Style("height:96px;line-clamp:1 no-ellipsis;"); f.Layout();
        Assert.Single(f.Ranges()); Assert.InRange(f.Body.ExtentHeight, 95.9, 96.1);
        for (var pass = 0; pass < 3; pass++) { f.Body.InvalidateMeasure(); f.Layout(); Assert.Single(f.Ranges()); }
        f.Style("height:96px;line-clamp:none;"); f.Layout(); Assert.True(f.Ranges().Count > 1); f.AssertOriginal();
    }

    [Fact]
    public void ALongDocumentKeepsOriginalSourcesWhenMostParagraphsAreSuppressed()
    {
        var sources = Enumerable.Range(0, 400).Select(index => new Paragraph(new Run($"第 {index} 段原始内容"))).ToArray();
        var doc = DocumentClampFixture.Document(sources); var original = doc.GetText(); Css.SetStyle(doc, "font-size:16px;line-height:24px;width:150px;line-clamp:1 no-ellipsis;");
        using var body = DocumentClampFixture.Present(doc); Assert.Equal(400, body.Paragraphs.Count); Assert.True(body.Paragraphs[0].IsVisible); Assert.False(body.Paragraphs[^1].IsVisible);
        Assert.Equal(original, DocumentClampFixture.Provider(body).DocumentRange.GetText(-1)); Assert.InRange(body.ExtentHeight, 23.9, 24.1);
        Css.SetStyle(doc, "font-size:16px;line-height:24px;width:150px;line-clamp:none;"); DocumentClampFixture.Layout(body);
        Assert.True(body.Paragraphs[^1].IsVisible); Assert.Equal(sources, doc.Blocks.Cast<Paragraph>()); Assert.Same(sources[^1].Inlines, body.Paragraphs[^1].Inlines);
    }
}

internal sealed class DocumentClampFixture : IDisposable
{
    private const string Words = CssDocumentLineClampTests.Words;
    private const string RootStyle = "font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:150px;text-align:start;";
    internal readonly FlowDocument Doc;
    internal readonly Paragraph Paragraph;
    internal readonly Run[] Runs;
    internal readonly DependencyObject Target;
    internal readonly FlowDocumentPresentation Body;
    internal readonly string Scope;
    private readonly string _full;
    internal DocumentClampFixture(string scope)
    {
        Scope = scope; Runs = [new(Words[..7]), new(Words[7..])]; Paragraph = new(); foreach (var run in Runs) Paragraph.Inlines.Add(run);
        Block child = Paragraph; DependencyObject target = Paragraph;
        if (scope == "section") { var section = new Section(Paragraph); child = section; target = section; }
        if (scope is "list" or "item")
        { var item = new ListItem(Paragraph); var list = new DocList(); list.ListItems.Add(item); child = list; target = scope == "list" ? list : item; }
        if (scope == "cell")
        { var cell = new TableCell(Paragraph); var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table(); table.RowGroups.Add(group); child = table; target = cell; }
        Doc = Document(child); Target = scope == "root" ? Doc : target; _full = Doc.GetText(); Body = Present(Doc);
    }
    internal void Style(string value) => Css.SetStyle(Target, (ReferenceEquals(Target, Doc) ? RootStyle : "") + value);
    internal void Layout() => Layout(Body);
    internal IReadOnlyList<(double StartX, double EndX, double Baseline)> Ranges() => Body.Paragraphs[0].GetSourceRangeSegments(0, Words.Length);
    internal void AssertOriginal()
    {
        Assert.Equal(_full, Doc.GetText()); Assert.Equal(Runs, Paragraph.Inlines.Cast<Run>()); Assert.Same(Paragraph.Inlines, Body.Paragraphs[0].Inlines);
        var provider = Provider(Body); Assert.Equal(_full, provider.DocumentRange.GetText(-1)); provider.DocumentRange.Select(); Assert.Equal(_full, Body.Selection!.Text);
    }
    internal static FlowDocument Document(params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "Courier New", FontSize = 16, LineHeight = 24, PagePadding = new(0), PageWidth = 150, PageHeight = 96, Background = Brushes.White };
        foreach (var block in blocks) doc.Blocks.Add(block);
        Css.SetStyle(doc, RootStyle); Css.SetStyleSheet(doc, "Paragraph,Section,List,ListItem,Table,TableCell{margin:0;padding:0;border:0;}Table{border-spacing:0;}"); return doc;
    }
    internal static FlowDocumentPresentation Present(FlowDocument doc) { var result = new FlowDocumentPresentation(); result.SetDocument(doc); Layout(result); return result; }
    internal static void Layout(FlowDocumentPresentation body)
    {
        for (var pass = 0; pass < 3; pass++)
        { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(150, double.PositiveInfinity)); body.Arrange(new(0, 0, 150, body.DesiredSize.Height)); }
    }
    internal static ITextProvider Provider(FlowDocumentPresentation body) => (ITextProvider)UIElementAutomationPeer.CreatePeerForElement(body)!.GetPattern(PatternInterface.Text)!;
    public void Dispose() => Body.Dispose();
}
