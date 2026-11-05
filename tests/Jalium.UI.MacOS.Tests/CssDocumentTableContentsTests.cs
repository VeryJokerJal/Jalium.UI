using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentTableContentsTests : IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose()=>_context.Dispose();
    internal const string Contents="display:contents;width:900px;height:800px;min-height:700px;margin:30px;padding:20px;border:10px solid red;background:red;columns:2;column-gap:90px;break-before:page;";
    public static IEnumerable<object[]> Cases()=>new[]{"table","group","row","cell"}.SelectMany(scope=>new[]{false,true}.Select(collapsed=>new object[]{scope,collapsed}));

    [Theory] [MemberData(nameof(Cases))]
    public void AnonymousFixupsReplaceOnlyTheElidedTableBoxes(string scope,bool collapsed)
    {
        var cells=Enumerable.Range(0,4).Select(i=>new TableCell(new Paragraph(new Run(((char)('A'+i)).ToString())))).ToArray();
        var first=new TableRow();first.Cells.Add(cells[0]);first.Cells.Add(cells[1]);var second=new TableRow();second.Cells.Add(cells[2]);second.Cells.Add(cells[3]);
        var group=new TableRowGroup();group.Rows.Add(first);group.Rows.Add(second);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);var raw=doc.GetText();
        Css.SetStyle(table,collapsed?"border-collapse:collapse;border:2px solid green;border-spacing:0;":"border-spacing:0;");
        foreach(var target in scope switch{"table"=>new TextElement[]{table},"group"=>[group],"row"=>[first,second],_=>cells})Css.SetStyle(target,Contents+(scope=="table"&&collapsed?"border-collapse:collapse;":""));
        var expectedTable=Reference(scope);var reference=DocumentDisplayFixture.Document(expectedTable);
        Css.SetStyle(expectedTable,scope=="table"?"width:auto;border-spacing:0;":collapsed?"border-collapse:collapse;border:2px solid green;border-spacing:0;":"border-spacing:0;");
        using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(reference);
        Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);Assert.Equal(expected.TableCells.Select(c=>c.Bounds),body.TableCells.Select(c=>c.Bounds));
        Assert.Equal(expected.TableCells.Select(c=>(c.Row,c.Column,c.RowSpan,c.ColumnSpan)),body.TableCells.Select(c=>(c.Row,c.Column,c.RowSpan,c.ColumnSpan)));
        Assert.Equal(raw,doc.GetText());Assert.Equal(new[]{first,second},group.Rows);Assert.Equal(cells.Take(2),first.Cells);Assert.Equal(cells.Skip(2),second.Cells);
        for(var i=0;i<4;i++)Assert.Same(((Paragraph)cells[i].Blocks[0]).Inlines,body.Paragraphs[i].Inlines);
        if(scope=="cell")Assert.DoesNotContain(body.TableCells,c=>cells.Contains(c.Source));
    }

    [Fact]
    public void RowspansCrossElidedGroupsAndStopAtRemainingGroupBoxes()
    {
        var a=new TableCell(new Paragraph(new Run("A"))){RowSpan=2};var b=new TableCell(new Paragraph(new Run("B")));var c=new TableCell(new Paragraph(new Run("C")));
        var first=new TableRow();first.Cells.Add(a);first.Cells.Add(b);var second=new TableRow();second.Cells.Add(c);var one=new TableRowGroup();one.Rows.Add(first);var two=new TableRowGroup();two.Rows.Add(second);
        var table=new Table();table.RowGroups.Add(one);table.RowGroups.Add(two);var doc=DocumentDisplayFixture.Document(table);Css.SetStyle(one,"display:contents;");Css.SetStyle(two,"display:contents;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(2,body.TableCells.Single(cell=>ReferenceEquals(cell.Source,a)).RowSpan);Assert.Equal(1,body.TableCells.Single(cell=>ReferenceEquals(cell.Source,c)).Column);
        Css.SetStyle(two,"");DocumentDisplayFixture.Layout(body);Assert.Equal(1,body.TableCells.Single(cell=>ReferenceEquals(cell.Source,a)).RowSpan);Assert.Equal(0,body.TableCells.Single(cell=>ReferenceEquals(cell.Source,c)).Column);Assert.Equal(2,a.RowSpan);
    }

    [Fact]
    public void ElidedCellSpansDoNotTransferToItsAnonymousReplacement()
    {
        var a=new TableCell(new Paragraph(new Run("A"))){ColumnSpan=3,RowSpan=3};var b=new TableCell(new Paragraph(new Run("B")));var row=new TableRow();row.Cells.Add(a);row.Cells.Add(b);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);Css.SetStyle(a,"display:contents;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(1,body.TableCells.Single(cell=>ReferenceEquals(cell.Source,b)).Column);Assert.DoesNotContain(body.TableCells,cell=>ReferenceEquals(cell.Source,a));Assert.All(body.TableCells,cell=>Assert.Equal(1,cell.ColumnSpan));Assert.Equal(3,a.ColumnSpan);Assert.Equal(3,a.RowSpan);
    }

    [Fact]
    public void HiddenCellsDoNotSeparateConsecutiveAnonymousCellContent()
    {
        var a=new TableCell(new Paragraph(new Run("A")));var hidden=new TableCell(new Paragraph(new Run("MASKED")));var c=new TableCell(new Paragraph(new Run("C")));var row=new TableRow();foreach(var cell in new[]{a,hidden,c})row.Cells.Add(cell);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);
        Css.SetStyle(a,"display:contents;");Css.SetStyle(c,"display:contents;");Css.SetStyle(hidden,"display:none;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Single(body.TableCells);Assert.Equal(48,body.ExtentHeight,4);Assert.Empty(body.Paragraphs[1].DocumentTextBounds(0,6));Assert.DoesNotContain("MASKED",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Equal(3,row.Cells.Count);
    }

    [Fact]
    public void ElidingAllTableBoxesPromotesTheOriginalBlocksIntoDocumentFlow()
    {
        var p=new Paragraph(new Run("AB"));var q=new Paragraph(new Run("CD"));var first=new TableCell(p);var second=new TableCell(q);var row=new TableRow();row.Cells.Add(first);row.Cells.Add(second);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);
        foreach(var source in new TextElement[]{table,group,row,first,second})Css.SetStyle(source,Contents);using var body=DocumentDisplayFixture.Present(doc);
        Assert.Empty(body.TableCells);Assert.Equal(48,body.ExtentHeight,4);Assert.Equal(0,body.Paragraphs[0].TransformToAncestor(body).X,4);Assert.Equal(24,body.Paragraphs[1].TransformToAncestor(body).Y,4);Assert.Same(p,first.Blocks[0]);
    }

    [Fact]
    public void CellAndParagraphSeamsMapVisibleRangesBackToTheOriginalPointers()
    {
        var p=new Paragraph(new Run("AB"));var q=new Paragraph(new Run("CD"));var a=new TableCell(p);var b=new TableCell(q);var row=new TableRow();row.Cells.Add(a);row.Cells.Add(b);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);var raw=doc.GetText();
        foreach(var source in new TextElement[]{a,b,p,q})Css.SetStyle(source,"display:contents;");using var body=DocumentDisplayFixture.Present(doc);var provider=DocumentDisplayFixture.ProviderFor(body);var range=provider.DocumentRange.FindText("BC",false,false);Assert.NotNull(range);range.Select();Assert.Equal("BC",provider.GetSelection().Single().GetText(-1));
        var bounds=provider.DocumentRange.FindText("C",false,false)!.GetBoundingRectangles();Assert.NotEmpty(bounds);Assert.Equal(doc.GetDocumentOffset(q,0),body.OffsetFromPoint(new(bounds[0]+1,bounds[1]+12)));Assert.Equal(raw,doc.GetText());Assert.Same(q,b.Blocks[0]);
    }

    [Fact]
    public void ElidedHeadersLoseTheirBoxAndTheNextHeaderIsPromoted()
    {
        TableRowGroup Group(string value){var row=new TableRow();row.Cells.Add(new TableCell(new Paragraph(new Run(value))));var group=new TableRowGroup();group.Rows.Add(row);return group;}
        var one=Group("FIRST");var two=Group("SECOND");var bodyGroup=Group("BODY");var table=new Table();table.RowGroups.Add(one);table.RowGroups.Add(two);table.RowGroups.Add(bodyGroup);var doc=DocumentDisplayFixture.Document(table);
        Css.SetStyle(one,Contents);Css.SetStyle(two,"display:table-header-group;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(72,body.ExtentHeight,4);Assert.Equal(0,body.Paragraphs[1].TransformToAncestor(body).Y,4);Assert.Equal(24,body.Paragraphs[0].TransformToAncestor(body).Y,4);Assert.Equal(new[]{one,two,bodyGroup},table.RowGroups);
    }

    [Fact]
    public void ConsecutiveElidedTablesShareOneAnonymousTableAndItsColumnDefinitions()
    {
        Table Make(string value,double width){var row=new TableRow();row.Cells.Add(new TableCell(new Paragraph(new Run(value))));var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);table.Columns.Add(new TableColumn{Width=new GridLength(width)});Css.SetStyle(table,Contents);return table;}
        var one=Make("A",80);var two=Make("B",100);var doc=DocumentDisplayFixture.Document(one,two);using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(new[]{0,1},body.TableCells.Select(cell=>cell.Row));Assert.All(body.TableCells,cell=>Assert.Equal(80,cell.Bounds.Width,4));Assert.Equal(48,body.ExtentHeight,4);Assert.Same(one,doc.Blocks[0]);Assert.Same(two,doc.Blocks[1]);
    }

    [Theory] [InlineData("normal",48)] [InlineData("pre-wrap",96)]
    public void AnonymousTableFixupsDiscardOnlyCollapsibleWhitespace(string whitespace,double height)
    {
        var before=new Paragraph(new Run(" \t "));var after=new Paragraph(new Run(" \t "));var table=Reference("group");var doc=DocumentDisplayFixture.Document(before,table,after);
        Css.SetStyle(before,"display:contents;");Css.SetStyle(after,"display:contents;");Css.SetStyle(table,"display:contents;");Css.SetStyle(doc,DocumentDisplayFixture.Font+$"white-space:{whitespace};");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(height,body.ExtentHeight,4);Assert.All(body.TableCells,cell=>Assert.True(cell.Bounds.Width<30));Assert.Equal(whitespace!="normal",body.Paragraphs[0].IsVisible);Assert.Equal(whitespace!="normal",body.Paragraphs[^1].IsVisible);
        var exposed=DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1);if(whitespace=="normal")Assert.DoesNotContain(" \t ",exposed);else Assert.Contains(" \t ",exposed);
    }

    [Theory] [InlineData("normal",false)] [InlineData("pre-line",false)] [InlineData("pre-wrap",true)] [InlineData("break-spaces",true)]
    public void ConsecutiveWhitespaceIsDiscardedAsASequenceAcrossHiddenSources(string whitespace,bool preserved)
    {
        var before=new Paragraph(new Run(" "));var another=new Paragraph(new Run("\t "));var hidden=new Paragraph(new Run("MASKED"));var after=new Paragraph(new Run(" "));var last=new Paragraph(new Run(" "));var table=Reference("group");
        var doc=DocumentDisplayFixture.Document(before,hidden,another,table,after,last);var raw=doc.GetText();
        foreach(var source in new TextElement[]{before,another,table,after,last})Css.SetStyle(source,"display:contents;");Css.SetStyle(hidden,"display:none;");Css.SetStyle(doc,DocumentDisplayFixture.Font+$"white-space:{whitespace};");
        using var body=DocumentDisplayFixture.Present(doc);Assert.Equal(preserved?96:48,body.ExtentHeight,4);
        foreach(var index in new[]{0,2,body.Paragraphs.Count-2,body.Paragraphs.Count-1})Assert.Equal(preserved,body.Paragraphs[index].IsVisible);
        var exposed=DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1);Assert.DoesNotContain("MASKED",exposed);Assert.Equal(preserved,exposed.StartsWith(" \t ",StringComparison.Ordinal));Assert.Equal(raw,doc.GetText());
    }

    [Fact]
    public void AnonymousCellsRebuildForTwoViewsWithoutChangingAnyOriginalCollection()
    {
        var run=new Run("A");var p=new Paragraph(run);var q=new Paragraph(new Run("B"));var a=new TableCell(p);var b=new TableCell(q);var row=new TableRow();row.Cells.Add(a);row.Cells.Add(b);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);using var one=DocumentDisplayFixture.Present(doc);using var two=DocumentDisplayFixture.Present(doc);var first=one.Paragraphs.ToArray();var second=two.Paragraphs.ToArray();
        foreach(var cell in new[]{a,b})Css.SetStyle(cell,"display:contents;");DocumentDisplayFixture.Layout(one);DocumentDisplayFixture.Layout(two);Assert.Single(one.TableCells);Assert.Single(two.TableCells);
        run.Text="修改原文";Css.SetStyle(a,"");DocumentDisplayFixture.Layout(one);DocumentDisplayFixture.Layout(two);Assert.Equal(2,one.TableCells.Count);Assert.Equal(2,two.TableCells.Count);Assert.Equal(first,one.Paragraphs);Assert.Equal(second,two.Paragraphs);Assert.Same(run,p.Inlines[0]);Assert.Equal(new[]{a,b},row.Cells);Assert.Contains("修改原文",DocumentDisplayFixture.ProviderFor(two).DocumentRange.GetText(-1));
    }

    [Fact]
    public void OriginalSelectorsAndInheritedTextStylesSurviveAnonymousCellRepair()
    {
        var run=new Run("A");var a=new TableCell(new Paragraph(run));var b=new TableCell(new Paragraph(new Run("B")));var row=new TableRow();row.Cells.Add(a);row.Cells.Add(b);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);
        foreach(var cell in new[]{a,b}){Css.SetClass(cell,"removed");Css.SetStyle(cell,"display:contents;font-size:22px;line-height:30px;color:#146342;background:red;");}
        Css.GetStyleSheets(doc).Add(CssStyleSheet.Parse("TableCell.removed > Paragraph > Run{font-weight:bold;}"));using var body=DocumentDisplayFixture.Present(doc);
        Assert.Single(body.TableCells);Assert.Equal(60,body.ExtentHeight,4);Assert.Equal(22,run.FontSize);Assert.Equal(FontWeights.Bold,run.FontWeight);Assert.Same(a,((Paragraph)run.Parent!).Parent);Assert.Same(row,a.Parent);
    }

    [Fact]
    public void ContentsColumnsRemoveTheirOwnWidthAndBackgroundButKeepOriginalDefinitions()
    {
        var table=Reference("group");var column=new TableColumn{Width=new GridLength(150),Background=Brushes.Red};table.Columns.Add(column);var doc=DocumentDisplayFixture.Document(table);Css.SetStyle(column,"display:contents;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.All(body.TableCells,cell=>Assert.Equal(90,cell.Bounds.Width,4));Assert.Same(column,table.Columns[0]);Assert.Equal(150,column.Width.Value);
    }

    [Theory] [InlineData("row")] [InlineData("group")]
    public void ElidedCollapsingPartsAllowExplicitlyVisibleDescendantRowsAndCells(string scope)
    {
        var table=Reference("group");var group=table.RowGroups[0];var row=group.Rows[0];var doc=DocumentDisplayFixture.Document(table);
        Css.SetStyle(scope=="row"?row:group,"display:contents;visibility:collapse;");
        foreach(var source in scope=="row"?row.Cells.Cast<TextElement>():group.Rows.Cast<TextElement>())Css.SetStyle(source,"visibility:visible;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.NotEmpty(body.Paragraphs[0].DocumentTextBounds(0,1));Assert.Equal(48,body.ExtentHeight,4);
    }

    public static IEnumerable<object[]> BoxCombinations()=>Enumerable.Range(0,32).Select(mask=>new object[]{mask});

    [Theory] [MemberData(nameof(BoxCombinations))]
    public void EveryTableContentsCombinationMatchesTheOrdinaryCssBoxFormatter(int mask)
    {
        var table=Reference("group");var group=table.RowGroups[0];var doc=DocumentDisplayFixture.Document(table);
        var root=new StackPanel();var ordinaryTable=new StackPanel();var ordinaryGroup=new StackPanel();root.Children.Add(ordinaryTable);ordinaryTable.Children.Add(ordinaryGroup);
        Css.SetStyle(root,DocumentDisplayFixture.Font+"display:block;");var labels=new System.Collections.Generic.List<TextBlock>();
        void Style(DependencyObject native,DependencyObject ordinary,int bit,string display)
        {
            var style=$"display:{((mask&(1<<bit))!=0?"contents":display)};margin:0;padding:0;border:0;"+(bit==0?"width:180px;border-spacing:0;":"");
            Css.SetStyle(native,style);Css.SetStyle(ordinary,style);
        }
        Style(table,ordinaryTable,0,"table");Style(group,ordinaryGroup,1,"table-row-group");
        foreach(var row in group.Rows)
        {
            var ordinaryRow=new StackPanel();ordinaryGroup.Children.Add(ordinaryRow);Style(row,ordinaryRow,2,"table-row");
            foreach(var cell in row.Cells)
            {
                var ordinaryCell=new StackPanel();ordinaryRow.Children.Add(ordinaryCell);Style(cell,ordinaryCell,3,"table-cell");
                var paragraph=(Paragraph)cell.Blocks[0];var text=new TextBlock{Text=((Run)paragraph.Inlines[0]).Text};ordinaryCell.Children.Add(text);labels.Add(text);Style(paragraph,text,4,"block");
            }
        }
        using var body=DocumentDisplayFixture.Present(doc);
        for(var pass=0;pass<4;pass++){Dispatcher.CurrentDispatcher.ProcessQueue();root.Measure(new(180,double.PositiveInfinity));root.Arrange(new(0,0,180,root.DesiredSize.Height));}
        Assert.Equal(root.DesiredSize.Height,body.ExtentHeight,4);
        for(var i=0;i<labels.Count;i++)
        {
            var actual=body.Paragraphs[i].DocumentTextBounds(0,1).Single();var expected=labels[i].DocumentTextBounds(0,1).Single();
            var at=body.Paragraphs[i].TransformToAncestor(body);var reference=labels[i].TransformToAncestor(root);
            Assert.Equal(expected.X+reference.X,actual.X+at.X,4);Assert.Equal(expected.Y+reference.Y,actual.Y+at.Y,4);
            Assert.Equal(expected.Width,actual.Width,4);Assert.Equal(expected.Height,actual.Height,4);
        }
        Assert.Equal(2,group.Rows.Count);Assert.All(group.Rows,row=>Assert.Equal(2,row.Cells.Count));
    }

    [Theory] [MemberData(nameof(Cases))]
    public void ColumnsAndPageRangesUseRepairedTableBoxes(string scope,bool columns)
    {
        var table=Reference("group");var doc=DocumentDisplayFixture.Document(new Paragraph(new Run("HEAD")),table,new Paragraph(new Run("TAIL")));
        IEnumerable<TextElement> targets=scope switch{"table"=>[table],"group"=>table.RowGroups,"row"=>table.RowGroups.SelectMany(group=>group.Rows),_=>table.RowGroups.SelectMany(group=>group.Rows).SelectMany(row=>row.Cells)};
        foreach(var target in targets)Css.SetStyle(target,Contents);
        var expectedTable=Reference(scope);var reference=DocumentDisplayFixture.Document(new Paragraph(new Run("HEAD")),expectedTable,new Paragraph(new Run("TAIL")));if(scope=="table")Css.SetStyle(expectedTable,"width:auto;");
        foreach(var document in new[]{doc,reference})Css.SetStyle(document,DocumentDisplayFixture.Font+"orphans:1;widows:1;"+(columns?"columns:2;column-gap:10px;":""));
        using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(reference);Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);
        var pages=FlowDocumentPagination.Build(body,48,FlowDirection.LeftToRight);var expectedPages=FlowDocumentPagination.Build(expected,48,FlowDirection.LeftToRight);
        Assert.Equal(expectedPages.Select(page=>(page.FirstLine,page.EndLine,page.Top,page.Bottom)),pages.Select(page=>(page.FirstLine,page.EndLine,page.Top,page.Bottom)));
        foreach(var word in new[]{"HEAD","A","B","C","D","TAIL"})Assert.Equal(DocumentDisplayFixture.ProviderFor(expected).DocumentRange.FindText(word,false,false)!.GetBoundingRectangles(),DocumentDisplayFixture.ProviderFor(body).DocumentRange.FindText(word,false,false)!.GetBoundingRectangles());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void OuterClampingTreatsTheAnonymousTableAsOneAtomicBoxAndRestoresIt(bool automatic)
    {
        Section Section(bool actual)
        {
            var table=Reference("group");Css.SetStyle(table,actual?Contents:"width:auto;");var section=new Section(new Paragraph(new Run("FIRST")));section.Blocks.Add(table);section.Blocks.Add(new Paragraph(new Run("AFTER")));section.Blocks.Add(new Paragraph(new Run("TAIL")));return section;
        }
        var source=Section(true);var reference=Section(false);var doc=DocumentDisplayFixture.Document(source);var expectedDoc=DocumentDisplayFixture.Document(reference);var raw=doc.GetText();
        var style=automatic?"line-clamp:auto no-ellipsis;max-height:80px;padding:2px;border:2px solid green;":"line-clamp:2 no-ellipsis;padding:2px;border:2px solid green;";
        foreach(var section in new[]{source,reference})Css.SetStyle(section,style);using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(expectedDoc);
        for(var pass=0;pass<3;pass++)
        {
            Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);Assert.Equal(expected.Paragraphs.Select(text=>text.DocumentTextBounds(0,1).Count),body.Paragraphs.Select(text=>text.DocumentTextBounds(0,1).Count));
            var next=pass==0?"padding:2px;border:2px solid green;":style;foreach(var section in new[]{source,reference})Css.SetStyle(section,next);DocumentDisplayFixture.Layout(body);DocumentDisplayFixture.Layout(expected);
        }
        Assert.Equal(raw,doc.GetText());Assert.Equal(4,body.TableCells.Count);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PercentageCellRelayoutPreservesSharedTextAndNestedColumnGeometry(bool columns)
    {
        const string a="AA BB CC DD ",b="EE FF GG HH II JJ KK LL MM NN OO PP";
        (FlowDocument Doc,Section Section,Paragraph First) Document(bool actual)
        {
            var p=new Paragraph(new Run(a));var section=new Section(p);var cell=new TableCell(section);
            if(actual){var q=new Paragraph(new Run(b));section.Blocks.Add(q);Css.SetStyle(cell,"display:contents;");Css.SetStyle(p,"display:contents;");Css.SetStyle(q,"display:contents;");}
            else p.Inlines.Add(new Run(b));
            Css.SetStyle(section,"height:50%;"+(columns?"columns:2;column-gap:10px;column-fill:auto;":""));
            var row=new TableRow();row.Cells.Add(cell);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);
            var doc=DocumentDisplayFixture.Document(table);Css.SetStyle(table,"height:144px;");return(doc,section,p);
        }
        var actual=Document(true);var reference=Document(false);using var body=DocumentDisplayFixture.Present(actual.Doc);using var expected=DocumentDisplayFixture.Present(reference.Doc);var proxies=body.Paragraphs.ToArray();
        foreach(var height in new[]{144,192,144})
        {
            Css.SetStyle(actual.Doc.Blocks[0],$"height:{height}px;");Css.SetStyle(reference.Doc.Blocks[0],$"height:{height}px;");DocumentDisplayFixture.Layout(body);DocumentDisplayFixture.Layout(expected);
            Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);
            foreach(var word in new[]{"AA","DD","EE","PP"})Assert.Equal(DocumentDisplayFixture.ProviderFor(expected).DocumentRange.FindText(word,false,false)!.GetBoundingRectangles(),DocumentDisplayFixture.ProviderFor(body).DocumentRange.FindText(word,false,false)!.GetBoundingRectangles());
        }
        Assert.Equal(proxies,body.Paragraphs);Assert.Same(actual.First,actual.Section.Blocks[0]);
    }

    internal static Table Reference(string scope)
    {
        var table=new Table();var group=new TableRowGroup();table.RowGroups.Add(group);
        if(scope=="row")
        {var row=new TableRow();foreach(var value in new[]{"A","B","C","D"})row.Cells.Add(new TableCell(new Paragraph(new Run(value))));group.Rows.Add(row);}
        else foreach(var pair in new[]{new[]{"A","B"},new[]{"C","D"}})
        {
            var row=new TableRow();group.Rows.Add(row);
            if(scope=="cell"){var cell=new TableCell();foreach(var value in pair)cell.Blocks.Add(new Paragraph(new Run(value)));row.Cells.Add(cell);}
            else foreach(var value in pair)row.Cells.Add(new TableCell(new Paragraph(new Run(value))));
        }
        return table;
    }
}
