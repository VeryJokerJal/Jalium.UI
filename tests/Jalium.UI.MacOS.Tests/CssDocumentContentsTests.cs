using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList=Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentContentsTests : IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose()=>_context.Dispose();
    internal const string Contents="display:contents;width:900px;height:800px;min-height:700px;margin:30px;padding:20px;border:10px solid red;background:red;columns:2;column-gap:90px;break-before:page;";

    [Theory] [InlineData("AB","CD",180)] [InlineData("甲乙","丙丁",40)] [InlineData("e\u0301","👨‍👩‍👧‍👦 e\u0301",60)] [InlineData("AB AB AB AB ","CD CD CD CD",70)]
    public void BoxlessParagraphsShareAnonymousLinesAndKeepOriginalSources(string a,string b,double width)
    {
        var one=new Paragraph(new Run(a));var two=new Paragraph(new Run(b));var doc=DocumentDisplayFixture.Document(one,two);var raw=doc.GetText();var pointer=two.ContentStart;
        Css.SetStyle(one,Contents);Css.SetStyle(two,Contents);Css.SetStyle(doc,DocumentDisplayFixture.Font+$"width:{width}px;");
        using var body=DocumentDisplayFixture.Present(doc);var reference=DocumentDisplayFixture.Document(new Paragraph(new Run(a+b)));Css.SetStyle(reference,DocumentDisplayFixture.Font+$"width:{width}px;");using var expected=DocumentDisplayFixture.Present(reference);
        Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);Assert.Equal(expected.Lines.Count,body.Lines.Count);
        Assert.Equal(a+b,DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Equal(raw,doc.GetText());Assert.Same(one.Inlines,body.Paragraphs[0].Inlines);Assert.Same(two.Inlines,body.Paragraphs[1].Inlines);Assert.Equal(0,pointer.CompareTo(two.ContentStart));
        var first=body.Paragraphs[0].DocumentTextBounds(0,a.Length);var second=body.Paragraphs[1].DocumentTextBounds(0,b.Length);Assert.NotEmpty(first);Assert.NotEmpty(second);
    }

    [Theory] [InlineData("section")] [InlineData("list")] [InlineData("item")]
    public void StructuralContentsRemovesItsBoxAndKeepsDescendantBoxes(string scope)
    {
        var p=new Paragraph(new Run("BEFORE"));var q=new Paragraph(new Run("AFTER"));TextElement target;Block block;
        if(scope=="section"){var section=new Section(p);section.Blocks.Add(q);target=section;block=section;}
        else{var item=new ListItem(p);item.Blocks.Add(q);var list=new DocList(item);target=scope=="list"?list:item;block=list;}
        var doc=DocumentDisplayFixture.Document(block);Css.SetStyle(target,Contents);using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(48,body.ExtentHeight,4);Assert.Equal(scope=="list"?20:0,body.Paragraphs[0].TransformToAncestor(body).X,4);
        Assert.Contains("BEFORE",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Same(p.Inlines,body.Paragraphs[0].Inlines);
    }

    [Fact]
    public void NestedBoxlessSectionsDoNotBreakTheAnonymousParagraph()
    {
        var a=new Paragraph(new Run("AB"));var b=new Paragraph(new Run("CD"));var section=new Section(b);var doc=DocumentDisplayFixture.Document(a,section);
        foreach(var source in new TextElement[]{a,b,section})Css.SetStyle(source,Contents);using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(24,body.ExtentHeight,4);Assert.Single(body.Lines);Assert.Equal("ABCD",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
    }

    [Fact]
    public void EmptyContentsParagraphsDoNotCreateAFontStrutOrExposedNewline()
    {
        var a=new Paragraph();var b=new Paragraph();var doc=DocumentDisplayFixture.Document(a,b);Css.SetStyle(a,Contents);Css.SetStyle(b,Contents);using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(0,body.ExtentHeight);Assert.Empty(body.Lines);Assert.Equal("",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Equal("\n\n",doc.GetText());
    }

    [Fact]
    public void VisibleRangesCrossTheOriginalParagraphSeamAndSelectOriginalPointers()
    {
        var a=new Paragraph(new Run("AB"));var b=new Paragraph(new Run("CD"));var doc=DocumentDisplayFixture.Document(a,b);Css.SetStyle(a,Contents);Css.SetStyle(b,Contents);using var body=DocumentDisplayFixture.Present(doc);
        var provider=DocumentDisplayFixture.ProviderFor(body);var range=provider.DocumentRange.FindText("BC",false,false);Assert.NotNull(range);Assert.Equal("BC",range.GetText(-1));range.Select();Assert.Equal("BC",provider.GetSelection().Single().GetText(-1));
        var rect=provider.DocumentRange.FindText("C",false,false)!.GetBoundingRectangles();Assert.NotEmpty(rect);Assert.Equal(doc.GetDocumentOffset(b,0),body.OffsetFromPoint(new(rect[0]+1,rect[1]+12)));
    }

    [Fact]
    public void EditingAndSwitchingDisplayRestoresTheSameParagraphProxies()
    {
        var run=new Run("AB");var a=new Paragraph(run);var b=new Paragraph(new Run("CD"));var doc=DocumentDisplayFixture.Document(a,b);Css.SetStyle(a,Contents);Css.SetStyle(b,Contents);using var body=DocumentDisplayFixture.Present(doc);var views=body.Paragraphs.ToArray();
        Assert.Equal(24,body.ExtentHeight,4);Css.SetStyle(a,"");DocumentDisplayFixture.Layout(body);Assert.Equal(48,body.ExtentHeight,4);
        run.Text="甲乙丙丁";Css.SetStyle(a,Contents);DocumentDisplayFixture.Layout(body);Assert.Equal("甲乙丙丁CD",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));Assert.Equal(views,body.Paragraphs);Assert.Same(run,a.Inlines[0]);
    }

    [Theory] [InlineData("min-content")] [InlineData("max-content")] [InlineData("fit-content(100px)")] [InlineData("calc-size(max-content,size + 20px)")]
    public void AnonymousIntrinsicWidthsIncludeWordsAcrossSourceSeams(string sizing)
    {
        var a=new Paragraph(new Run("AB"));var b=new Paragraph(new Run("CD EF"));var after=new Paragraph(new Run("X"));
        var doc=DocumentDisplayFixture.Document(a,b,after);Css.SetStyle(a,Contents);Css.SetStyle(b,Contents);
        var reference=DocumentDisplayFixture.Document(new Paragraph(new Run("ABCD EF")),new Paragraph(new Run("X")));
        Css.SetStyle(doc,DocumentDisplayFixture.Font+$"width:{sizing};");Css.SetStyle(reference,DocumentDisplayFixture.Font+$"width:{sizing};");
        using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(reference);
        Assert.Equal(expected.Paragraphs[^1].ActualWidth,body.Paragraphs[^1].ActualWidth,4);Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);
    }

    [Fact]
    public void DisplayNoneDoesNotSplitAnAnonymousRunAndRealBlocksDo()
    {
        var a=new Paragraph(new Run("AB"));var hidden=new Paragraph(new Run("MASKED"));var b=new Paragraph(new Run("CD"));var block=new Paragraph(new Run("BLOCK"));var c=new Paragraph(new Run("EF"));
        var doc=DocumentDisplayFixture.Document(a,hidden,b,block,c);foreach(var p in new[]{a,b,c})Css.SetStyle(p,Contents);Css.SetStyle(hidden,"display:none;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(72,body.ExtentHeight,4);Assert.Equal(3,body.Lines.Count);Assert.Equal("ABCDBLOCK\nEF",DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
        Assert.Empty(body.Paragraphs[1].DocumentTextBounds(0,6));Assert.Equal(24,body.Paragraphs[3].TransformToAncestor(body).Y,4);
    }

    [Theory] [InlineData("normal")] [InlineData("pre-wrap")] [InlineData("break-spaces")]
    public void WhitespaceCollapsingAndWrappingContinueAcrossParagraphSources(string whitespace)
    {
        var a=new Paragraph(new Run("A  "));var b=new Paragraph(new Run("  B  C  "));var doc=DocumentDisplayFixture.Document(a,b);Css.SetStyle(a,Contents);Css.SetStyle(b,Contents);
        var reference=DocumentDisplayFixture.Document(new Paragraph(new Run("A    B  C  ")));
        foreach(var d in new[]{doc,reference})Css.SetStyle(d,DocumentDisplayFixture.Font+$"width:45px;white-space:{whitespace};");
        using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(reference);Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);Assert.Equal(expected.Lines.Count,body.Lines.Count);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ColumnsAndPaginationConsumePhysicalLinesAcrossTheElidedParagraphs(bool columns)
    {
        const string a="AA BB CC DD ",b="EE FF GG HH II JJ KK LL MM NN OO PP";
        var p=new Paragraph(new Run(a));var q=new Paragraph(new Run(b));var wrapper=new Section(p);wrapper.Blocks.Add(q);var doc=DocumentDisplayFixture.Document(wrapper);
        foreach(var source in new TextElement[]{p,q,wrapper})Css.SetStyle(source,Contents+"orphans:8;widows:8;break-inside:avoid;column-span:all;");
        var reference=DocumentDisplayFixture.Document(new Paragraph(new Run(a+b)));
        foreach(var d in new[]{doc,reference})Css.SetStyle(d,DocumentDisplayFixture.Font+"width:100px;orphans:1;widows:1;"+(columns?"columns:2;column-gap:10px;":""));
        using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(reference);
        Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);Assert.Equal(expected.Lines.Count,body.Lines.Count);
        var pages=FlowDocumentPagination.Build(body,72,FlowDirection.LeftToRight);var expectedPages=FlowDocumentPagination.Build(expected,72,FlowDirection.LeftToRight);
        Assert.Equal(expectedPages.Select(page=>(page.FirstLine,page.EndLine,page.Top,page.Bottom)),pages.Select(page=>(page.FirstLine,page.EndLine,page.Top,page.Bottom)));
        Assert.NotEmpty(DocumentDisplayFixture.ProviderFor(body).DocumentRange.FindText("PP",false,false)!.GetBoundingRectangles());
    }

    [Theory] [InlineData(1,"no-ellipsis","auto")] [InlineData(2,"no-ellipsis","auto")] [InlineData(2,"ellipsis","auto")] [InlineData(2,"'… 更多'","balance")]
    public void OuterClampingCountsAnonymousLinesAndRestoresAllOriginalSources(int count,string marker,string wrap)
    {
        const string a="AA BB CC DD ",b="EE FF GG HH II JJ KK LL MM NN OO PP";
        var p=new Paragraph(new Run(a));var q=new Paragraph(new Run(b));var wrapper=new Section(p);wrapper.Blocks.Add(q);var doc=DocumentDisplayFixture.Document(wrapper);
        foreach(var source in new TextElement[]{p,q,wrapper})Css.SetStyle(source,Contents+"overflow:hidden;");
        Css.SetStyle(doc,DocumentDisplayFixture.Font+$"width:90px;line-clamp:{count} {marker};text-wrap-style:{wrap};");using var body=DocumentDisplayFixture.Present(doc);var views=body.Paragraphs.ToArray();
        Assert.Equal(count,body.Lines.Count);Assert.Equal(count*24,body.ExtentHeight,4);Assert.Empty(body.Paragraphs[1].DocumentTextBounds(b.Length-2,2));Assert.Equal(a+b,DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetText(-1));
        Css.SetStyle(doc,DocumentDisplayFixture.Font+"width:90px;");DocumentDisplayFixture.Layout(body);Assert.True(body.Lines.Count>count);Assert.NotEmpty(body.Paragraphs[1].DocumentTextBounds(b.Length-2,2));Assert.Equal(views,body.Paragraphs);
    }

    [Fact]
    public void ABoxlessRootStillGeneratesTheRootBoxAndClampsItsContent()
    {
        var doc=DocumentDisplayFixture.Document(new Paragraph(new Run("FIRST")),new Paragraph(new Run("SECOND")));
        Css.SetStyle(doc,DocumentDisplayFixture.Font+"display:contents;padding:5px;border:2px solid green;line-clamp:1 no-ellipsis;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(38,body.ExtentHeight,4);Assert.Single(body.Lines,line=>!line.IsSpace);Assert.Empty(body.Paragraphs[1].DocumentTextBounds(0,6));Assert.Equal(7,body.Paragraphs[0].TransformToAncestor(body).X,4);
    }

    [Fact]
    public void TwoViewsReflowAndEditTheirOwnAnonymousFragmentsWithoutReparenting()
    {
        var run=new Run("AA BB CC DD ");var p=new Paragraph(run);var q=new Paragraph(new Run("EE FF GG HH"));var doc=DocumentDisplayFixture.Document(p,q);Css.SetStyle(p,Contents);Css.SetStyle(q,Contents);
        using var first=DocumentDisplayFixture.Present(doc);using var second=DocumentDisplayFixture.Present(doc);var one=first.Paragraphs.ToArray();var two=second.Paragraphs.ToArray();
        Css.SetStyle(doc,DocumentDisplayFixture.Font+"width:60px;");DocumentDisplayFixture.Layout(first);DocumentDisplayFixture.Layout(second);Assert.Equal(first.Lines.Count,second.Lines.Count);Assert.True(first.Lines.Count>1);
        run.Text="修改原文 ";DocumentDisplayFixture.Layout(first);DocumentDisplayFixture.Layout(second);Assert.Equal("修改原文 EE FF GG HH",DocumentDisplayFixture.ProviderFor(second).DocumentRange.GetText(-1));Assert.Equal(one,first.Paragraphs);Assert.Equal(two,second.Paragraphs);Assert.NotSame(one[0],two[0]);
        Assert.Same(run,p.Inlines[0]);Assert.Same(p,doc.Blocks[0]);Assert.Same(q,doc.Blocks[1]);
    }

    [Theory] [InlineData("甲乙e\u0301 ","丙丁👨‍👩‍👧‍👦 戊己")] [InlineData("甲乙AB ","丙丁")]
    public void MixedScriptSourceSeamsRetainTheInternalSpaceAdvance(string a,string b)
    {
        var p=new Paragraph(new Run(a));var q=new Paragraph(new Run(b));var doc=DocumentDisplayFixture.Document(p,q);Css.SetStyle(p,Contents);Css.SetStyle(q,Contents);
        var reference=DocumentDisplayFixture.Document(new Paragraph(new Run(a+b)));foreach(var d in new[]{doc,reference})Css.SetStyle(d,DocumentDisplayFixture.Font+"width:80px;");
        using var body=DocumentDisplayFixture.Present(doc);using var expected=DocumentDisplayFixture.Present(reference);
        var actual=body.Paragraphs[1].DocumentTextBounds(0,1).Single();var origin=body.Paragraphs[1].TransformToAncestor(body);var wanted=expected.Paragraphs[0].DocumentTextBounds(a.Length,1).Single();
        Assert.Equal(wanted.X,actual.X+origin.X,4);Assert.Equal(wanted.Y,actual.Y+origin.Y,4);Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);
    }

    [Fact]
    public void NativeTableCellsUseTheAnonymousRunsIntrinsicWidth()
    {
        FlowDocument Make(bool contents)
        {
            var p=new Paragraph(new Run("AB"));var q=new Paragraph(new Run("CD EF"));var cell=new TableCell(p);
            if(contents){cell.Blocks.Add(q);Css.SetStyle(p,Contents);Css.SetStyle(q,Contents);}else ((Run)p.Inlines[0]).Text="ABCD EF";
            var row=new TableRow();row.Cells.Add(cell);row.Cells.Add(new TableCell(new Paragraph(new Run("CELL"))));var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);var doc=DocumentDisplayFixture.Document(table);Css.SetStyle(table,"border-spacing:0;border:1px solid green;");return doc;
        }
        using var body=DocumentDisplayFixture.Present(Make(true));using var expected=DocumentDisplayFixture.Present(Make(false));
        Assert.Equal(expected.TableCells.Select(cell=>cell.Bounds),body.TableCells.Select(cell=>cell.Bounds));Assert.Equal(expected.ExtentHeight,body.ExtentHeight,4);
        Assert.NotEmpty(DocumentDisplayFixture.ProviderFor(body).DocumentRange.FindText("CELL",false,false)!.GetBoundingRectangles());
    }

    [Fact]
    public void DoubleClickSelectsAWordAcrossTheElidedParagraphBoundary()
    {
        var p=new Paragraph(new Run("AB"));var q=new Paragraph(new Run("CD"));var doc=DocumentDisplayFixture.Document(p,q);Css.SetStyle(p,Contents);Css.SetStyle(q,Contents);
        using var body=DocumentDisplayFixture.Present(doc);var provider=DocumentDisplayFixture.ProviderFor(body);var bounds=provider.DocumentRange.FindText("C",false,false)!.GetBoundingRectangles();
        var down=new MouseButtonEventArgs(UIElement.PreviewMouseDownEvent,new(bounds[0]+1,bounds[1]+12),MouseButton.Left,MouseButtonState.Pressed,2,
            MouseButtonState.Pressed,MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,ModifierKeys.None,0);
        try{body.RaiseEvent(down);Assert.True(down.Handled);Assert.Equal("ABCD",provider.GetSelection().Single().GetText(-1));Assert.Equal(0,body.Selection!.Start.DocumentOffset);Assert.Equal(doc.GetDocumentOffset(q,2),body.Selection.End.DocumentOffset);}
        finally{body.ReleaseMouseCapture();}
    }

    [Fact]
    public void ASharedEllipsisWithoutRetainedGlyphsStillHitsItsOriginalBoundary()
    {
        var header=new Paragraph(new Run("HEADER"));var p=new Paragraph(new Run("AA BB CC DD "));var q=new Paragraph(new Run("EE FF GG HH II JJ KK LL"));
        var doc=DocumentDisplayFixture.Document(header,p,q);Css.SetStyle(p,Contents);Css.SetStyle(q,Contents);
        Css.SetStyle(doc,DocumentDisplayFixture.Font+"width:90px;line-clamp:2 'THIS MARKER DOES NOT FIT';");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(48,body.ExtentHeight,4);Assert.Empty(body.Paragraphs[1].DocumentTextBounds(0,12));Assert.Empty(body.Paragraphs[2].DocumentTextBounds(0,21));
        Assert.Equal(doc.GetDocumentOffset(p,0),body.OffsetFromPoint(new(1,36)));
    }
}
