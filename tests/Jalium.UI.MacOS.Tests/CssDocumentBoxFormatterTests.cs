using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentBoxFormatterTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory] [InlineData("root","flex")] [InlineData("section","flex")] [InlineData("root","grid")] [InlineData("section","grid")]
    public void DocumentContainersUseTheSharedCssFormatter(string scope,string mode)
    {
        var first=new Paragraph(new Run("A"));var second=new Paragraph(new Run("B"));var section=scope=="section"?new Section(first):null;section?.Blocks.Add(second);
        var doc=section is null?DocumentDisplayFixture.Document(first,second):DocumentDisplayFixture.Document(section);var source=section is null?(DependencyObject)doc:section;
        var style=$"display:{mode};gap:10px;grid-template-columns:60px 110px;align-items:start;";Css.SetStyle(source,(scope=="root"?DocumentDisplayFixture.Font:"")+style);
        var raw=doc.GetText();using var body=DocumentDisplayFixture.Present(doc);
        var root=new StackPanel();Css.SetStyle(root,DocumentDisplayFixture.Font+style);var a=new TextBlock{Text="A"};var b=new TextBlock{Text="B"};root.Children.Add(a);root.Children.Add(b);foreach(var text in new[]{a,b})Css.SetStyle(text,"display:block;margin:0;padding:0;border:0;");
        for(var pass=0;pass<4;pass++){Dispatcher.CurrentDispatcher.ProcessQueue();root.Measure(new(180,double.PositiveInfinity));root.Arrange(new(0,0,180,root.DesiredSize.Height));}
        Assert.Equal(root.DesiredSize.Height,body.ExtentHeight,4);
        for(var i=0;i<2;i++)
        {
            var actual=body.Paragraphs[i].DocumentTextBounds(0,1).Single();var expected=(i==0?a:b).DocumentTextBounds(0,1).Single();var at=body.Paragraphs[i].TransformToAncestor(body);var reference=(i==0?a:b).TransformToAncestor(root);
            Assert.Equal(expected.X+reference.X,actual.X+at.X,4);Assert.Equal(expected.Y+reference.Y,actual.Y+at.Y,4);Assert.Equal(expected.Width,actual.Width,4);Assert.Equal(expected.Height,actual.Height,4);
        }
        Assert.Equal(raw,doc.GetText());Assert.Same(first.Inlines,body.Paragraphs[0].Inlines);Assert.Same(second.Inlines,body.Paragraphs[1].Inlines);
    }

    public static IEnumerable<object[]> Conversions()
    {
        foreach(var scope in new[]{"root","section","list","item","table","group","row","cell","paragraph"})
        foreach(var mode in new[]{"block","flex","grid","inline","inline-block","flow-root","table","table-row-group","table-row","table-cell","table-caption"})yield return[scope,mode];
    }

    [Theory] [MemberData(nameof(Conversions))]
    public void NativeBoxConversionsMatchOrdinaryCssWithoutChangingTheSourceTree(string scope,string mode)
    {
        using var f=new ConversionFixture(scope);f.Style($"display:{mode};gap:10px;grid-template-columns:60px 110px;align-items:start;");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Theory] [InlineData("flex-direction:row-reverse;justify-content:space-between;align-items:flex-end;","flex")]
    [InlineData("flex-flow:row wrap;justify-content:center;align-items:center;","flex")]
    [InlineData("grid-template-columns:60px 110px;justify-items:end;align-items:end;","grid")]
    public void ContainerPropertiesUseTheOriginalComputedStyles(string properties,string mode)
    {
        using var f=new ConversionFixture("section");f.Style($"display:{mode};height:120px;gap:10px;{properties}");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Fact]
    public void RepeatedModeChangesKeepOriginalProxiesAndSelectionsForBothViews()
    {
        using var f=new ConversionFixture("section");using var second=DocumentDisplayFixture.Present(f.Document);var proxies=f.Body.Paragraphs.ToArray();var other=second.Paragraphs.ToArray();
        foreach(var mode in new[]{"grid","flex","inline","block","inline-block","grid","block"})
        {
            f.Style($"display:{mode};gap:10px;grid-template-columns:60px 110px;");f.Layout();DocumentDisplayFixture.Layout(second);f.SameGeometry();
            Assert.Equal(proxies,f.Body.Paragraphs);Assert.Equal(other,second.Paragraphs);f.OriginalSource();
            var provider=DocumentDisplayFixture.ProviderFor(f.Body);var found=provider.DocumentRange.FindText("BB",false,false)!;found.Select();Assert.Equal("BB",provider.GetSelection().Single().GetText(-1));
            var bounds=found.GetBoundingRectangles();Assert.NotEmpty(bounds);Assert.Equal(f.Document.GetDocumentOffset(f.Paragraphs[1],0),f.Body.OffsetFromPoint(new(bounds[0]+1,bounds[1]+12)));
        }
    }

    [Fact]
    public void DisplayChangesInvalidateAllFormattingAncestorsWithoutAWindow()
    {
        var root=new StackPanel();var middle=new StackPanel();var changed=new StackPanel();root.Children.Add(middle);middle.Children.Add(changed);
        Css.SetStyle(root,DocumentDisplayFixture.Font+"display:block;");Css.SetStyle(middle,"display:block;");Css.SetStyle(changed,"display:block;gap:10px;grid-template-columns:60px 110px;");
        for(var i=0;i<3;i++){var child=new TextBlock{Text="AA"};Css.SetStyle(child,"display:block;");changed.Children.Add(child);}
        void Layout(){for(var i=0;i<4;i++){Dispatcher.CurrentDispatcher.ProcessQueue();root.Measure(new(180,double.PositiveInfinity));root.Arrange(new(0,0,180,root.DesiredSize.Height));}}
        Layout();Assert.Equal(72,root.DesiredSize.Height,4);Css.SetStyle(changed,"display:grid;gap:10px;grid-template-columns:60px 110px;");Layout();Assert.Equal(58,root.DesiredSize.Height,4);
        Css.SetStyle(changed,"display:block;");Layout();Assert.Equal(72,root.DesiredSize.Height,4);
    }

    [Theory] [InlineData("content-box")] [InlineData("border-box")]
    public void ProjectedSizingPreservesCssLayersInsetsAndPercentages(string sizing)
    {
        using var f=new ConversionFixture("section");f.Style($"display:grid;width:140px;height:100px;box-sizing:{sizing};padding:10px;border:4px solid green;grid-template-columns:50% 50%;");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Fact]
    public void NativeDimensionsRetainTheirPrecedenceOverProjectedCss()
    {
        using var f=new ConversionFixture("section");f.NativeWidth(130);f.Style("display:grid;width:60px;grid-template-columns:50% 50%;");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Fact]
    public void CssVisibilityCanBeOverriddenByAnOriginalDescendant()
    {
        using var f=new ConversionFixture("section");f.Style("display:grid;grid-template-columns:60px 110px;visibility:hidden;",f.Selector(f.Paragraphs[1])+"{visibility:visible;}");f.Layout();f.SameGeometry();f.SameVisibility();f.OriginalSource();
        Assert.Equal(new[]{false,true,false,false},f.Body.Paragraphs.Select(text=>text.IsVisible));
    }

    [Theory] [InlineData("flex")] [InlineData("grid")]
    public void AnonymousCssTextItemsUseLineBoxesWithoutNativeInkAllowance(string mode)
    {
        var text=new TextBlock{Text="AA"};Css.SetStyle(text,DocumentDisplayFixture.Font+$"display:{mode};");
        for(var i=0;i<3;i++){Dispatcher.CurrentDispatcher.ProcessQueue();text.Measure(new(180,double.PositiveInfinity));text.Arrange(new(0,0,180,text.DesiredSize.Height));}
        Assert.Equal(24,text.DesiredSize.Height,4);
    }

    [Fact]
    public void CssAncestorClipsRestrictRangesHitsAndPageBands()
    {
        using var f=new ConversionFixture("section");f.Style("display:grid;grid-template-columns:180px;height:36px;overflow:hidden;");f.Layout();f.SameGeometry();
        var provider=DocumentDisplayFixture.ProviderFor(f.Body);Assert.Empty(provider.DocumentRange.FindText("DD",false,false)!.GetBoundingRectangles());
        var partial=provider.DocumentRange.FindText("BB",false,false)!.GetBoundingRectangles();Assert.NotEmpty(partial);Assert.Equal(12,partial[3],4);
        Assert.Single(FlowDocumentPagination.Build(f.Body,40,FlowDirection.LeftToRight));
        Assert.True(f.Body.OffsetFromPoint(new(1,100))<f.Document.GetDocumentOffset(f.Paragraphs[2],0));f.OriginalSource();
    }

    [Fact]
    public void EmptyProjectedHeightRemainsFragmentableDocumentSpace()
    {
        var section=new Section();var doc=DocumentDisplayFixture.Document(section);Css.SetStyle(section,"display:grid;height:120px;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(120,body.ExtentHeight,4);var pages=FlowDocumentPagination.Build(body,48,FlowDirection.LeftToRight);Assert.Equal(3,pages.Count);
        Assert.Empty(DocumentDisplayFixture.ProviderFor(body).DocumentRange.GetBoundingRectangles());Assert.Empty(section.Blocks);
    }

    [Fact]
    public void OriginalTableCellGeometryRemainsAvailableAroundConvertedContent()
    {
        using var f=new ConversionFixture("table");f.Style("display:table;",f.Selector(f.Paragraphs[0])+"{display:grid;}"+f.Selector(f.Paragraphs[1].Parent!)+"{visibility:hidden;}");f.Layout();f.SameGeometry();
        Assert.Equal(4,f.Body.TableCells.Count);Assert.Equal(f.Paragraphs.Select(paragraph=>(TableCell)paragraph.Parent!),f.Body.TableCells.Select(cell=>cell.Source));
        Assert.All(f.Body.TableCells,cell=>Assert.Equal(90,cell.Bounds.Width,4));f.OriginalSource();
    }

    [Fact]
    public void NativeTextAlignmentSurvivesItsProjectedFormattingParent()
    {
        using var f=new ConversionFixture("section");f.NativeAlignment(TextAlignment.Right);f.Style("display:grid;grid-template-columns:60px 110px;");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Theory] [InlineData("root")] [InlineData("group")] [InlineData("row")]
    public void ConvertedNonvisualBoxEdgesUseTheVisualPresentationProperties(string scope)
    {
        using var f=new ConversionFixture(scope);f.Style("display:grid;grid-template-columns:60px 110px;padding:10px;border:4px solid green;");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Theory] [InlineData("root")] [InlineData("section")]
    public void ConvertedColumnsSplitLongSourceParagraphsAndPreserveEveryRange(string scope)
    {
        using var f=new ConversionFixture(scope);
        f.Words(["AA BB CC DD EE FF GG HH II JJ KK LL MM NN OO PP QQ RR SS TT UU VV WW XX ","YY ","ZZ ","END"]);
        var raw=f.Document.GetText();
        f.Style("display:inline-block;width:180px;height:72px;columns:2;column-gap:12px;",string.Concat(f.Paragraphs.Select(p=>f.Selector(p)+"{display:inline;}")));
        f.Layout();f.EntireGeometry();f.SourceIdentity();Assert.Equal(raw,f.Document.GetText());
        var at=f.Body.Paragraphs[0].TransformToAncestor(f.Body);
        for(var line=0;line<8;line++)
        {
            var bounds=f.Body.Paragraphs[0].DocumentTextBounds(line*9,2).Single();
            Assert.Equal(bounds.Top+at.Y,f.Body.TopForOffset(f.Document.GetDocumentOffset(f.Paragraphs[0],line*9)),4);
        }
    }

    [Fact]
    public void RtlUsesCssStartAlignmentWithoutAnAuthoredNativeOverride()
    {
        using var f=new ConversionFixture("section");f.Typography(DocumentDisplayFixture.Font.Replace("text-align:start;",""));
        f.Style("display:grid;direction:rtl;grid-template-columns:60px 110px;gap:10px;");f.Layout();f.SameGeometry();f.OriginalSource();
    }

    [Fact]
    public void StructuralSelectorsAndInheritanceStillUseOriginalDocumentNodes()
    {
        var first=new Paragraph(new Run("AA"));var second=new Paragraph(new Run("BB"));var section=new Section(first);section.Blocks.Add(second);var doc=DocumentDisplayFixture.Document(section);Css.SetClass(section,"cards");
        Css.GetStyleSheets(doc).Add(CssStyleSheet.Parse("Section.cards>Paragraph:nth-child(2){font-size:20px;line-height:28px;font-weight:bold;}"));Css.SetStyle(section,"display:grid;grid-template-columns:60px 110px;");using var body=DocumentDisplayFixture.Present(doc);
        Assert.Equal(20,second.FontSize);Assert.Equal(FontWeights.Bold,((Run)second.Inlines[0]).FontWeight);Assert.Equal(28,body.ExtentHeight,4);
        var inserted=new Paragraph(new Run("XX"));section.Blocks.Insert(1,inserted);DocumentDisplayFixture.Layout(body);Assert.Equal(20,inserted.FontSize);Assert.Equal(16,second.FontSize);Assert.Equal(52,body.ExtentHeight,4);
        Assert.Same(section,second.Parent);Assert.Same(second.Inlines,body.Paragraphs[2].Inlines);
    }

    [Fact]
    public void SharedDocumentsKeepIndependentBoxConstraintsAtDifferentWidths()
    {
        var paragraphs=Enumerable.Range(0,4).Select(i=>new Paragraph(new Run(new string((char)('A'+i),2)))).ToArray();var section=new Section();foreach(var paragraph in paragraphs)section.Blocks.Add(paragraph);var doc=DocumentDisplayFixture.Document(section);
        Css.SetStyle(doc,"font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;text-align:start;");Css.SetStyle(section,"display:grid;grid-template-columns:repeat(auto-fit,minmax(80px,1fr));gap:10px;");
        using var narrow=new FlowDocumentPresentation();narrow.SetDocument(doc);using var wide=new FlowDocumentPresentation();wide.SetDocument(doc);
        void Layout(FlowDocumentPresentation body,double width){for(var pass=0;pass<3;pass++){Dispatcher.CurrentDispatcher.ProcessQueue();body.Measure(new(width,double.PositiveInfinity));body.Arrange(new(0,0,width,body.DesiredSize.Height));}}
        for(var pass=0;pass<3;pass++)
        {
            if(pass==1)((Run)paragraphs[2].Inlines[0]).Text="CC+";Layout(narrow,180);Layout(wide,270);
            var one=narrow.Paragraphs[2].TransformToAncestor(narrow);var two=wide.Paragraphs[2].TransformToAncestor(wide);Assert.Equal(34,one.Y,4);Assert.Equal(0,two.Y,4);Assert.True(two.X>170);
            Assert.Equal(58,narrow.ExtentHeight,4);Assert.Equal(58,wide.ExtentHeight,4);Assert.Same(paragraphs[2].Inlines,narrow.Paragraphs[2].Inlines);Assert.Same(paragraphs[2].Inlines,wide.Paragraphs[2].Inlines);
        }
    }

    private sealed class ConversionFixture : IDisposable
    {
        internal readonly FlowDocument Document;
        internal readonly FlowDocumentPresentation Body;
        internal readonly Paragraph[] Paragraphs=Enumerable.Range(0,4).Select(i=>new Paragraph(new Run(new string((char)('A'+i),2)))).ToArray();
        private readonly StackPanel _reference=new();
        private readonly System.Collections.Generic.List<TextBlock> _texts=[];
        private readonly Dictionary<DependencyObject,string> _classes=[];
        private readonly Dictionary<DependencyObject,DependencyObject?> _parents=[];
        private readonly Dictionary<DependencyObject,FrameworkElement> _referenceBoxes=[];
        private readonly DependencyObject _target;
        private readonly string _baseSheet;
        private readonly string _original;
        internal ConversionFixture(string scope)
        {
            var item=new ListItem(Paragraphs[0]);item.Blocks.Add(Paragraphs[1]);var list=new Jalium.UI.Documents.List(item);list.ListItems.Add(new ListItem(Paragraphs[2]));list.ListItems.Add(new ListItem(Paragraphs[3]));
            var section=new Section();var table=new Table();var group=new TableRowGroup();var rows=new[]{new TableRow(),new TableRow()};TableCell? cell=null;
            if(scope is "table" or "group" or "row" or "cell")
            {
                // The unused list only owns these paragraphs until this fixture chooses its source shape.
                foreach(var entry in list.ListItems)entry.Blocks.Clear();
                foreach(var row in rows)group.Rows.Add(row);table.RowGroups.Add(group);
                for(var i=0;i<4;i++){var next=new TableCell(Paragraphs[i]);rows[i/2].Cells.Add(next);cell??=next;}section.Blocks.Add(table);
            }
            else if(scope is "list" or "item")section.Blocks.Add(list);
            else
            {
                foreach(var entry in list.ListItems)entry.Blocks.Clear();
                foreach(var paragraph in Paragraphs)section.Blocks.Add(paragraph);
            }
            Document=DocumentDisplayFixture.Document(section);
            _target=scope switch{"root"=>Document,"list"=>list,"item"=>item,"table"=>table,"group"=>group,"row"=>rows[0],"cell"=>cell!,"paragraph"=>Paragraphs[0],_=>section};
            var rules=new System.Collections.Generic.List<string>();
            FrameworkElement Reference(DependencyObject source)
            {
                var name="n"+_classes.Count;_classes[source]=name;_parents[source]=(source as FrameworkContentElement)?.Parent;Css.SetClass(source,name);
                var role=source switch{Table=>"table",TableRowGroup=>"table-row-group",TableRow=>"table-row",TableCell=>"table-cell",_=>"block"};
                rules.Add($".{name}{{display:{role};margin:0;padding:0;border:0;border-spacing:0;{(source is Table?"width:180px;":"")}}}");
                FrameworkElement result;
                if(source is Paragraph paragraph){var text=new TextBlock{Text=((Run)paragraph.Inlines[0]).Text};_texts.Add(text);result=text;}
                else
                {
                    var panel=ReferenceEquals(source,Document)?_reference:new StackPanel();
                    IEnumerable<DependencyObject> children=source switch{FlowDocument d=>d.Blocks,Section s=>s.Blocks,Jalium.UI.Documents.List l=>l.ListItems,ListItem i=>i.Blocks,Table t=>t.RowGroups,TableRowGroup g=>g.Rows,TableRow r=>r.Cells,TableCell c=>c.Blocks,_=>[]};
                    foreach(var child in children)panel.Children.Add(Reference(child));result=panel;
                }
                Css.SetClass(result,name);_referenceBoxes[source]=result;return result;
            }
            Reference(Document);_baseSheet=string.Concat(rules);Css.SetStyle(_reference,DocumentDisplayFixture.Font);_original=Document.GetText();Body=DocumentDisplayFixture.Present(Document);Style("");Layout();
        }
        internal string Selector(DependencyObject source)=>"."+_classes[source];
        internal void NativeWidth(double width){_target.SetValue(FrameworkElement.WidthProperty,width);_referenceBoxes[_target].Width=width;}
        internal void Typography(string css){Css.SetStyle(Document,css);Css.SetStyle(_reference,css);}
        internal void Words(string[] words){for(var i=0;i<words.Length;i++){((Run)Paragraphs[i].Inlines[0]).Text=words[i];_texts[i].Text=words[i];}}
        internal void NativeAlignment(TextAlignment alignment){_target.SetValue(Block.TextAlignmentProperty,alignment);foreach(var text in _texts)text.TextAlignment=alignment;}
        internal void Style(string css,string extra="")
        {var sheet=_baseSheet+$".{_classes[_target]}{{{css}}}"+extra;Css.SetStyleSheet(Document,sheet);Css.SetStyleSheet(_reference,sheet);}
        internal void Layout()
        {
            DocumentDisplayFixture.Layout(Body);
            for(var pass=0;pass<4;pass++){Dispatcher.CurrentDispatcher.ProcessQueue();_reference.Measure(new(180,double.PositiveInfinity));_reference.Arrange(new(0,0,180,_reference.DesiredSize.Height));}
        }
        internal void SameGeometry()
            => CompareGeometry(2);
        internal void EntireGeometry()
            => CompareGeometry(int.MaxValue);
        private void CompareGeometry(int length)
        {
            Assert.Equal(_reference.DesiredSize.Height,Body.ExtentHeight,4);
            for(var i=0;i<_texts.Count;i++)
            {
                var one=Body.Paragraphs[i].DocumentTextBounds(0,length);var two=_texts[i].DocumentTextBounds(0,length);Assert.Equal(two.Count,one.Count);var at=Body.Paragraphs[i].TransformToAncestor(Body);var expected=_texts[i].TransformToAncestor(_reference);
                for(var j=0;j<one.Count;j++){Assert.Equal(two[j].X+expected.X,one[j].X+at.X,4);Assert.Equal(two[j].Y+expected.Y,one[j].Y+at.Y,4);Assert.Equal(two[j].Width,one[j].Width,4);Assert.Equal(two[j].Height,one[j].Height,4);}
            }
        }
        internal void OriginalSource()
        {Assert.Equal(_original,Document.GetText());SourceIdentity();}
        internal void SourceIdentity()
        {foreach(var (source,parent) in _parents)Assert.Same(parent,(source as FrameworkContentElement)?.Parent);for(var i=0;i<Paragraphs.Length;i++)Assert.Same(Paragraphs[i].Inlines,Body.Paragraphs[i].Inlines);}
        internal void SameVisibility()=>Assert.Equal(_texts.Select(text=>text.IsVisible),Body.Paragraphs.Select(text=>text.IsVisible));
        public void Dispose()=>Body.Dispose();
    }
}
