using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentDisplayTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    internal static readonly string[] Scopes = ["paragraph", "section", "list", "item", "table", "group", "row", "cell", "root"];
    public static IEnumerable<object[]> Cases()
    {
        foreach (var scope in Scopes)
        foreach (var columns in new[] { false, true }) yield return [scope, columns];
    }

    [Theory] [MemberData(nameof(Cases))]
    public void NoneRemovesTheWholeOriginalSubtreeAndRestoresItWithoutReparenting(string scope, bool columns)
    {
        using var f = new DocumentDisplayFixture(scope); var original = f.Doc.GetText(); var pointer = f.Doc.ContentEnd; var proxies = f.Body.Paragraphs.ToArray();
        if (columns) Css.SetStyle(f.Doc, DocumentDisplayFixture.Font + "columns:2;column-gap:20px;orphans:1;widows:1;");
        f.Style("display:none;margin:19px;padding:13px;border:3px solid red;height:200px;"); f.Layout();
        Assert.Empty(f.Body.Paragraphs[1].DocumentTextBounds(0, 6)); Assert.False(f.Body.Paragraphs[1].IsVisible);
        Assert.Empty(f.Body.Paragraphs[1].GetDocumentLines(180)); Assert.DoesNotContain("MASKED", f.Provider.DocumentRange.GetText(-1));
        if (scope == "root") { Assert.Equal(0, f.Body.ExtentHeight); Assert.Equal("", f.Provider.DocumentRange.GetText(-1)); }
        else { Assert.InRange(f.Body.ExtentHeight, 23.9, 48.1); Assert.Contains("BEFORE", f.Provider.DocumentRange.GetText(-1)); Assert.Contains("AFTER", f.Provider.DocumentRange.GetText(-1)); }
        Assert.Equal(original, f.Doc.GetText()); Assert.Equal(proxies, f.Body.Paragraphs); Assert.Same(f.Source.Inlines, f.Body.Paragraphs[1].Inlines);
        Assert.Equal(0, pointer.CompareTo(f.Doc.ContentEnd));
        f.Style(""); f.Layout(); Assert.NotEmpty(f.Body.Paragraphs[1].DocumentTextBounds(0, 6)); Assert.Contains("MASKED", f.Provider.DocumentRange.GetText(-1)); Assert.Equal(original, f.Doc.GetText());
    }

    [Theory]
    [InlineData("section")] [InlineData("item")] [InlineData("group")] [InlineData("row")] [InlineData("cell")] [InlineData("root")]
    public void AVisibleDescendantCannotRecreateBoxesUnderNone(string scope)
    {
        using var f = new DocumentDisplayFixture(scope); f.Style("display:none;"); Css.SetStyle(f.Source, "display:block;visibility:visible;height:300px;"); f.Layout();
        Assert.Empty(f.Body.Paragraphs[1].DocumentTextBounds(0, 6)); Assert.DoesNotContain("MASKED", f.Provider.DocumentRange.GetText(-1));
        f.Style(""); Css.SetStyle(f.Source, ""); f.Layout(); Assert.NotEmpty(f.Body.Paragraphs[1].DocumentTextBounds(0, 6));
    }

    [Theory]
    [InlineData("paragraph")] [InlineData("section")] [InlineData("cell")] [InlineData("root")]
    public void SharedViewsPaginationAndSelectionUseTheirOwnMappedVisibleOffsets(string scope)
    {
        using var f = new DocumentDisplayFixture(scope); using var second = DocumentDisplayFixture.Present(f.Doc); f.Doc.PageHeight = 24;
        var pages = ((IDocumentPaginatorSource)f.Doc).DocumentPaginator; var before = pages.PageCount; f.Style("display:none;"); f.Layout(); DocumentDisplayFixture.Layout(second);
        Assert.Equal(scope == "root" ? 1 : 2, pages.PageCount); Assert.DoesNotContain("MASKED", DocumentDisplayFixture.ProviderFor(second).DocumentRange.GetText(-1));
        if (scope != "root")
        {
            var range = f.Provider.DocumentRange.FindText("AFTER", false, false)!; range.Select(); Assert.Equal("AFTER", f.Body.Selection!.Text);
            var rect = range.GetBoundingRectangles(); Assert.NotEmpty(rect); Assert.InRange(f.Body.OffsetFromPoint(new(rect[0]+1, rect[1]+12)), f.Doc.GetDocumentOffset(f.After,0), f.Doc.GetDocumentOffset(f.After,0)+1);
        }
        f.Style(""); f.Layout(); DocumentDisplayFixture.Layout(second); Assert.Equal(before,pages.PageCount); Assert.Contains("MASKED", f.Provider.DocumentRange.GetText(-1));
    }

    [Fact]
    public void NoneDoesNotConsumeTheOuterClampBudgetOrIntrinsicWidth()
    {
        using var f = new DocumentDisplayFixture("section"); f.Run.Text = new string('M',100); f.Style("display:none;width:900px;");
        Css.SetStyle(f.Doc,DocumentDisplayFixture.Font+"width:min-content;line-clamp:2 no-ellipsis;"); f.Layout();
        Assert.NotEmpty(f.Body.Paragraphs[2].DocumentTextBounds(0,5)); Assert.True(f.Body.Paragraphs[0].ActualWidth<100);
        Assert.DoesNotContain(new string('M',10),f.Provider.DocumentRange.GetText(-1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HiddenInlineTextHasNoMetricsOrForcedBreaksButKeepsOriginalSourceOffsets(bool nested)
    {
        var before=new Run("AB"); var hidden=new Run("MASKED"){FontSize=96}; var after=new Run("CD"); var paragraph=new Paragraph(before);
        TextElement target=hidden;
        if(nested){var span=new Span(hidden);span.Inlines.Add(new LineBreak());paragraph.Inlines.Add(span);target=span;}else paragraph.Inlines.Add(hidden);
        paragraph.Inlines.Add(after);var doc=DocumentDisplayFixture.Document(paragraph);Css.SetStyle(target,"display:none;");
        using var body=DocumentDisplayFixture.Present(doc);var text=body.Paragraphs[0];
        Assert.Single(text.GetDocumentLines(180));Assert.InRange(body.ExtentHeight,23.9,24.1);
        Assert.Empty(text.DocumentTextBounds(2,6));Assert.Equal("ABCD\n",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
        var range=DocumentDisplayFixture.ProviderFor(body).DocumentRange.FindText("CD",false,false)!;range.Select();Assert.Equal("CD",body.Selection!.Text);Assert.Same(hidden,nested?((Span)paragraph.Inlines[1]).Inlines[0]:paragraph.Inlines[1]);
        Css.SetStyle(target,"");DocumentDisplayFixture.Layout(body);Assert.Contains("MASKED",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.True(body.ExtentHeight>24);
    }

    [Fact]
    public void RemovingAMiddleCellRecomputesItsAnonymousColumnWithoutMutatingSourceCollections()
    {
        var a=new TableCell(new Paragraph(new Run("A")));var b=new TableCell(new Paragraph(new Run("MASKED")));var c=new TableCell(new Paragraph(new Run("C")));
        var row=new TableRow();row.Cells.Add(a);row.Cells.Add(b);row.Cells.Add(c);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);
        var doc=DocumentDisplayFixture.Document(table);using var body=DocumentDisplayFixture.Present(doc);Css.SetStyle(b,"display:none;width:300px;padding:20px;");DocumentDisplayFixture.Layout(body);
        Assert.Equal(2,body.TableCells.Count);Assert.Equal(1,body.TableCells.Single(v=>ReferenceEquals(v.Source,c)).Column);Assert.Empty(body.Paragraphs[1].DocumentTextBounds(0,6));
        Assert.Equal(new[]{a,b,c},row.Cells);Assert.DoesNotContain("MASKED",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
        Css.SetStyle(b,"");DocumentDisplayFixture.Layout(body);Assert.Equal(3,body.TableCells.Count);Assert.NotEmpty(body.Paragraphs[1].DocumentTextBounds(0,6));
    }

    [Theory]
    [InlineData("AB", "CD")] [InlineData("甲乙", "丙丁戊己")] [InlineData("e\u0301", "e\u0301 e\u0301")]
    public void HiddenInlineNamesAndLineBreaksMatchAParagraphWithoutThatSource(string first,string last)
    {
        var a=new Run(first);var hidden=new Run("秘密👨‍👩‍👧‍👦"){FontSize=120};var b=new Run(last);var span=new Span(hidden);span.Inlines.Add(new LineBreak());
        var source=new Paragraph(a);source.Inlines.Add(span);source.Inlines.Add(b);var doc=DocumentDisplayFixture.Document(source);
        Css.SetStyle(doc,DocumentDisplayFixture.Font+"width:40px;");Css.SetStyle(span,"display:none;background:red;text-decoration:underline;line-height:300px;");
        using var body=DocumentDisplayFixture.Present(doc);var reference=DocumentDisplayFixture.Document(new Paragraph(new Run(first+last)));Css.SetStyle(reference,DocumentDisplayFixture.Font+"width:40px;");using var expected=DocumentDisplayFixture.Present(reference);
        var text=body.Paragraphs[0];var actual=text.GetDocumentLines(40);var lines=expected.Paragraphs[0].GetDocumentLines(40);Assert.Equal(lines.Count,actual.Count);Assert.Equal(expected.ExtentHeight,body.ExtentHeight,3);
        Assert.Equal(first+last,UIElementAutomationPeer.CreatePeerForElement(text)!.GetName());Assert.Empty(text.DocumentTextBounds(first.Length,hidden.Text.Length));Assert.Equal(first+last+"\n",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void HiddenRowsAndGroupsRebuildSpansAndCollapsedEdges(bool collapsed)
    {
        var spanning=new TableCell(new Paragraph(new Run("SPAN"))){RowSpan=2};var first=new TableRow();first.Cells.Add(spanning);first.Cells.Add(new TableCell(new Paragraph(new Run("A"))));
        var hidden=new TableRow();hidden.Cells.Add(new TableCell(new Paragraph(new Run("MASKED"))));var last=new TableRow();last.Cells.Add(new TableCell(new Paragraph(new Run("B"))));
        var group=new TableRowGroup();group.Rows.Add(first);group.Rows.Add(hidden);group.Rows.Add(last);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);
        Css.SetStyle(table,collapsed?"border-collapse:collapse;border:2px solid green;":"border-spacing:0;");Css.SetStyle(hidden,"display:none;height:300px;border:20px solid red;");
        using var body=DocumentDisplayFixture.Present(doc);Assert.Equal(3,body.TableCells.Count);Assert.Equal(2,body.TableCells.Single(v=>ReferenceEquals(v.Source,spanning)).RowSpan);Assert.Equal(1,body.TableCells.Single(v=>ReferenceEquals(v.Source,last.Cells[0])).Row);
        Assert.DoesNotContain("MASKED",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Equal(3,group.Rows.Count);Assert.Equal(2,spanning.RowSpan);
        Css.SetStyle(group,"display:none;");DocumentDisplayFixture.Layout(body);Assert.Empty(body.TableCells);Assert.Equal("",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
        Css.SetStyle(group,"");Css.SetStyle(hidden,"");DocumentDisplayFixture.Layout(body);Assert.Equal(4,body.TableCells.Count);
    }

    [Fact]
    public void AHiddenColumnRemovesItsAuthoredWidthAndPaintButKeepsTheCells()
    {
        var row=new TableRow();row.Cells.Add(new TableCell(new Paragraph(new Run("A"))));row.Cells.Add(new TableCell(new Paragraph(new Run("B"))));
        var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var column=new TableColumn{Width=new GridLength(150),Background=Brushes.Red};table.Columns.Add(column);
        var doc=DocumentDisplayFixture.Document(table);using var body=DocumentDisplayFixture.Present(doc);var width=body.TableCells[0].Bounds.Width;Css.SetStyle(column,"display:none;");DocumentDisplayFixture.Layout(body);
        Assert.Equal(2,body.TableCells.Count);Assert.True(body.TableCells[0].Bounds.Width<width);Assert.Contains("A",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Same(column,table.Columns[0]);
        Css.SetStyle(column,"");DocumentDisplayFixture.Layout(body);Assert.Equal(width,body.TableCells[0].Bounds.Width,3);
    }

    [Fact]
    public void MultipleHiddenIntervalsMapRangeEndpointsAndCaretHitsAcrossOriginalGaps()
    {
        var lead=new Run("HIDDEN");var middle=new Run("秘密😀");var tail=new Run("TRAILING");var paragraph=new Paragraph(lead);
        paragraph.Inlines.Add(new Run("甲乙"));paragraph.Inlines.Add(middle);paragraph.Inlines.Add(new Run("丙丁"));paragraph.Inlines.Add(tail);
        var doc=DocumentDisplayFixture.Document(paragraph);foreach(var run in new[]{lead,middle,tail})Css.SetStyle(run,"display:none;");using var body=DocumentDisplayFixture.Present(doc);
        var provider=DocumentDisplayFixture.ProviderFor(body);Assert.Equal("甲乙丙丁\n",provider.DocumentRange.GetText(-1));
        foreach(var value in new[]{"甲乙","丙丁","甲乙丙丁"})
        {
            var range=provider.DocumentRange.FindText(value,false,false)!;range.Select();Assert.Equal(value,provider.GetSelection().Single().GetText(-1));
            if(value!="甲乙丙丁")Assert.Equal(value,body.Selection!.Text);
            var rect=range.GetBoundingRectangles();Assert.NotEmpty(rect);var hit=provider.RangeFromPoint(new(rect[0]+1,rect[1]+12));Assert.NotNull(hit);Assert.InRange(hit.CompareEndpoints(TextPatternRangeEndpoint.Start,range,TextPatternRangeEndpoint.Start),0,1);
        }
        Assert.DoesNotContain("HIDDEN",UIElementAutomationPeer.CreatePeerForElement(body.Paragraphs[0])!.GetName());
        foreach(var run in new[]{lead,middle,tail})Css.SetStyle(run,"");DocumentDisplayFixture.Layout(body);Assert.Contains("秘密😀",provider.DocumentRange.GetText(-1));
    }

    [Fact]
    public void EditingAHiddenSourceAndTogglingAClassPreservesTheOriginalObjectsAndUpdatesExposure()
    {
        using var f=new DocumentDisplayFixture("section");Css.SetStyleSheet(f.Doc,"Paragraph,Section{margin:0;padding:0;border:0;}.off{display:none;}.off Paragraph{display:block;visibility:visible;}");
        for(var pass=0;pass<3;pass++)
        {
            Css.SetClass(f.Target,"off");f.Run.Text="MASKED "+pass;f.Layout();Assert.DoesNotContain("MASKED",f.Provider.DocumentRange.GetText(-1));Assert.InRange(f.Body.ExtentHeight,47.9,48.1);
            Css.SetClass(f.Target,"");f.Layout();Assert.Contains("MASKED "+pass,f.Provider.DocumentRange.GetText(-1));Assert.Same(f.Run,f.Source.Inlines[0]);
        }
    }
}

internal sealed class DocumentDisplayFixture : IDisposable
{
    internal const string Font="font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:180px;text-align:start;";
    internal readonly FlowDocument Doc;
    internal readonly Paragraph Source;
    internal readonly Paragraph After=new(new Run("AFTER"));
    internal readonly Run Run=new("MASKED");
    internal readonly DependencyObject Target;
    internal readonly FlowDocumentPresentation Body;
    internal ITextProvider Provider=>ProviderFor(Body);
    private readonly string _scope;
    internal DocumentDisplayFixture(string scope)
    {
        _scope=scope;Source=new(Run);Block child=Source;DependencyObject target=Source;
        if(scope=="section"){var section=new Section(Source);child=section;target=section;}
        if(scope is "list" or "item"){var item=new ListItem(Source);var list=new DocList(item);child=list;target=scope=="list"?list:item;}
        if(scope is "table" or "group" or "row" or "cell")
        {var cell=new TableCell(Source);var row=new TableRow();row.Cells.Add(cell);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);child=table;target=scope=="table"?table:scope=="group"?group:scope=="row"?row:cell;}
        Doc=Document(new Paragraph(new Run("BEFORE")),child,After);Target=scope=="root"?Doc:target;Body=Present(Doc);
    }
    internal void Style(string value)=>Css.SetStyle(Target,(_scope=="root"?Font:"")+value);
    internal void Layout()=>Layout(Body);
    internal static FlowDocument Document(params Block[] blocks)
    {
        var doc=new FlowDocument{PagePadding=new(0),PageWidth=180,PageHeight=96,FontFamily="Courier New",FontSize=16,LineHeight=24,Background=Brushes.White};
        foreach(var block in blocks)doc.Blocks.Add(block);Css.SetStyle(doc,Font);Css.SetStyleSheet(doc,"Paragraph,Section,List,ListItem,Table,TableCell{margin:0;padding:0;border:0;}Table{border-spacing:0;}");return doc;
    }
    internal static FlowDocumentPresentation Present(FlowDocument doc){var body=new FlowDocumentPresentation();body.SetDocument(doc);Layout(body);return body;}
    internal static void Layout(FlowDocumentPresentation body)
    {for(var n=0;n<3;n++){Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);body.Measure(new(180,double.PositiveInfinity));body.Arrange(new(0,0,180,body.DesiredSize.Height));}}
    internal static ITextProvider ProviderFor(FlowDocumentPresentation body)=>(ITextProvider)UIElementAutomationPeer.CreatePeerForElement(body)!.GetPattern(PatternInterface.Text)!;
    public void Dispose()=>Body.Dispose();
}
