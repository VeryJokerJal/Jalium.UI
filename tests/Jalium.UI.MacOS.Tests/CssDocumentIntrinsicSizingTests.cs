using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentIntrinsicSizingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    public static IEnumerable<object[]> WidthCases()
    {
        foreach (var target in new[] { "root", "paragraph", "section" })
        foreach (var (style, width) in new (string, string)[]
        {
            ("width:90px;","90"), ("width:min-content;","min"), ("width:max-content;","max"),
            ("width:fit-content;","fit"), ("width:fit-content(100px);","fit100"), ("width:stretch;","200"),
            ("width:calc-size(min-content,size * 2);","min*2"), ("width:calc-size(any,90px);","90"),
            ("width:calc-size(auto,size + 20px);","220"),
            ("width:calc-size(calc-size(min-content,size / 2),size + 20px);","min/2+20"),
            ("width:0;min-width:calc-size(min-content,size + 20px);","min+20"),
            ("width:200px;max-width:calc-size(max-content,size - 20px);","max-20"),
        }) yield return [target,style,width];
    }

    [Theory] [MemberData(nameof(WidthCases))]
    public void IntrinsicWidthsReachOriginalDocumentParagraphs(string target,string style,string expected)
    {
        using var f=new DocumentSizingFixture(target,style);f.Layout();
        Near(f.ExpectedWidth(expected),f.Text.ActualWidth);Assert.Same(f.Source.Inlines,f.Text.Inlines);
        Assert.Same(f.First,f.Source.Inlines[0]);Assert.Same(f.OriginalParent,f.Source.Parent);
    }

    public static IEnumerable<object[]> HeightCases()
    {
        foreach (var target in new[] { "root", "paragraph", "section" })
        foreach (var (style,height) in new (string,double)[]
        {
            ("height:80px;",80),("height:calc-size(auto,size + 24px);",48),
            ("height:calc-size(min-content,size * 2);",48),("height:calc-size(any,80px);",80),
            ("height:calc-size(calc-size(auto,size - 40px),size + 80px);",64),
            ("height:0;min-height:calc-size(max-content,size * 2);",48),
            ("height:200px;max-height:calc-size(min-content,size + 48px);",72),
            ("height:calc-size(50%,size / 2);",60),
        }) yield return [target,style,height];
    }

    [Theory] [MemberData(nameof(HeightCases))]
    public void CalculatedHeightsReserveFlowSpaceWithoutReplacingSourceNodes(string target,string style,double expected)
    {
        using var f=new DocumentSizingFixture(target,style);f.Layout();
        Near(expected,f.AfterY);Assert.Same(f.First,f.Source.Inlines[0]);
    }

    [Fact]
    public void ReferenceTextRetainsNaturalLineAndWordMetrics()
    {
        using var f=new DocumentSizingFixture("root","");f.Layout();
        Assert.True(f.ExpectedWidth("max")>f.ExpectedWidth("min"));Near(24,f.Text.ActualHeight);
    }

    [Theory] [InlineData("root")] [InlineData("paragraph")] [InlineData("section")]
    public void LiveTextNativeOverridesAndClearingRestoreTheCalculation(string target)
    {
        using var f=new DocumentSizingFixture(target,"height:calc-size(auto,size * 2);");f.Layout();Near(48,f.AfterY);
        f.Source.Inlines.Add(new LineBreak());f.Source.Inlines.Add(new Run("补充原文"));f.Layout();Near(96,f.AfterY);
        f.Target.SetValue(FrameworkElement.HeightProperty,70d);f.Layout();Near(70,f.AfterY);
        f.Target.SetAnimatedValue(FrameworkElement.HeightProperty,90d,false);f.Layout();Near(90,f.AfterY);
        f.Target.ClearAnimatedValue(FrameworkElement.HeightProperty);f.Target.ClearValue(FrameworkElement.HeightProperty);f.Layout();Near(96,f.AfterY);
    }

    [Theory] [InlineData("column-width")] [InlineData("columns")]
    public void AColumnLengthDoesNotAcceptTheSeparateSizingFunction(string property)
    {Assert.False(Css.Supports(property,"calc-size(any,80px)"));}

    [Theory]
    [InlineData("root","height:200px;max-height:calc-size(max-content,size * 2);")]
    [InlineData("section","height:200px;max-height:calc-size(max-content,size * 2);")]
    [InlineData("root","height:20px;min-height:calc-size(min-content,size * 2);")]
    [InlineData("section","height:20px;min-height:calc-size(min-content,size * 2);")]
    public void IntrinsicBoundsBreakThePercentageCycleBeforeSupplyingAChildBasis(string target,string style)
    {
        using var f=new DocumentSizingFixture(target,style);Css.SetStyle(f.Source,"height:50%;");
        for(var n=0;n<3;n++){f.Layout();Near(48,f.AfterY);f.Body.InvalidateMeasure();}
    }

    [Theory] [InlineData("root",false)] [InlineData("section",false)] [InlineData("root",true)] [InlineData("section",true)]
    public void MinimumAndMaximumIntrinsicBoundsKeepTheirDistinctPercentageRules(string target,bool minimum)
    {
        using var f=new DocumentSizingFixture(target,minimum?"height:20px;min-height:calc-size(min-content,size * 2);":"height:200px;max-height:calc-size(max-content,size * 2);");
        var inside=new Paragraph(new Run("内部后文"));
        if(f.Target is Section section)section.Blocks.Add(inside);else f.Document.Blocks.Add(inside);
        Css.SetStyle(f.Source,"height:10%;");
        for(var n=0;n<3;n++)
        {
            f.Layout();Near(minimum?9.6:24,f.Body.Paragraphs[1].TransformToAncestor(f.Body).Y);
            Near(96,target=="root"?f.Body.DesiredSize.Height:f.Body.Paragraphs[^1].TransformToAncestor(f.Body).Y);f.Body.InvalidateMeasure();
        }
    }

    [Theory] [InlineData("root")] [InlineData("section")]
    public void ColumnBalancingAndCalculatedBlockSizeShareTheSameSourceLines(string target)
    {
        using var f=new DocumentSizingFixture(target,"height:calc-size(auto,size + 24px);column-count:2;column-gap:20px;");
        Css.SetStyle(f.Source,"orphans:1;widows:1;");
        f.Layout();Near(48,f.AfterY);Assert.True(f.Body.HasColumns);Assert.Same(f.Source.Inlines,f.Text.Inlines);
    }

    [Fact]
    public void LiveCalculatedRootHeightInvalidatesTwoViewsAndCachedPages()
    {
        using var f=new DocumentSizingFixture("root","height:calc-size(auto,size * 4);");f.Document.PageHeight=48;
        using var second=new FlowDocumentPresentation();second.SetDocument(f.Document);f.Layout();DocumentSizingFixture.Layout(second,200,240);
        var pages=f.Document.ViewerPaginator;Assert.Equal(2,pages.PageCount);Near(96,f.Body.DesiredSize.Height);Near(96,second.DesiredSize.Height);
        f.Source.Inlines.Add(new LineBreak());f.Source.Inlines.Add(new Run("新增原文"));f.Layout();DocumentSizingFixture.Layout(second,200,240);
        Assert.Equal(4,pages.PageCount);Near(192,f.Body.DesiredSize.Height);Near(192,second.DesiredSize.Height);Assert.Same(f.First,f.Source.Inlines[0]);
    }

    [Theory] [InlineData("root")] [InlineData("paragraph")] [InlineData("section")]
    public void TypedKeyframesKeepTheOriginalNonvisualOwner(string target)
    {
        using var f=new DocumentSizingFixture(target,"height:calc-size(auto,size);");
        Css.GetStyleSheets(f.Document).Add(CssStyleSheet.Parse("@keyframes sizing{from{height:calc-size(auto,size);}to{height:calc-size(any,100px);}}"));f.Layout();
        var node=CssNode.Get(f.Target);var samplers=CssKeyframes.BuildSamplers(node,CssKeyframes.Find(node,"sizing")!);
        var track=samplers.Single(pair=>pair.Key.Name=="CssSizingHeight");f.Target.SetCssAnimatedValue(track.Key,track.Value.Sample(.5,new("linear")));f.Layout();Near(62,f.AfterY);
        f.Source.Inlines.Add(new LineBreak());f.Source.Inlines.Add(new Run("新增原文"));f.Layout();Near(74,f.AfterY);
        f.Target.ClearCssAnimatedValue(track.Key);f.Layout();Near(48,f.AfterY);
    }

    [Fact]
    public void NonvisualInheritanceRetainsTheComputedParentFontContext()
    {
        using var f=new DocumentSizingFixture("section","font-size:10px;width:calc-size(40px,size + 1em);");
        Css.SetStyle(f.Source,"font-size:20px;width:inherit;");f.Layout();Near(50,f.Text.ActualWidth);
        Css.SetStyle(f.Target,"font-size:30px;width:calc-size(40px,size + 1em);");f.Layout();Near(70,f.Text.ActualWidth);
        f.Target.SetValue(FrameworkElement.WidthProperty,90d);f.Layout();Near(90,f.Text.ActualWidth);
    }

    [Theory] [InlineData("root")] [InlineData("paragraph")] [InlineData("section")]
    public void ADeclaredPausedAnimationUsesTheNonvisualClockAndInheritedKeywordOptIn(string target)
    {
        using var f=new DocumentSizingFixture(target,"");
        Css.GetStyleSheets(f.Document).Add(CssStyleSheet.Parse("@keyframes open{from{height:auto;}to{height:100px;}}"));
        Css.SetStyle(f.Document,"interpolate-size:allow-keywords;"+(target=="root"?"":"height:240px;"));
        Css.SetStyle(f.Target,(target=="root"?"interpolate-size:allow-keywords;":"")+"height:auto;animation:open 1s linear -.5s both paused;");f.Layout();
        Assert.NotNull(CssNode.Get(f.Target).CssRuntimeState!.AnimationRunner);Near(62,f.AfterY);
        Css.SetStyle(f.Target,"height:auto;animation:none;");f.Layout();Near(24,f.AfterY);
    }

    [Theory] [InlineData("root")] [InlineData("paragraph")] [InlineData("section")]
    public void ComputedStyleQueriesObserveTheDocumentExpressionAndNativePriority(string target)
    {
        using var f=new DocumentSizingFixture(target,"width:calc-size(min-content,size + 20px);");f.Layout();
        var node=CssNode.Get(f.Target);var context=CssEngine.BuildLengthContext(node);
        Assert.True(CssSizeStyleQuery.Matches("width","calc-size(min-content,20px + size)",node,context));
        Assert.False(CssSizeStyleQuery.Matches("width","calc-size(max-content,size + 20px)",node,context));
        f.Target.SetValue(FrameworkElement.WidthProperty,90d);f.Layout();Assert.False(CssSizeStyleQuery.Matches("width","calc-size(min-content,size + 20px)",node,context));
        f.Target.ClearValue(FrameworkElement.WidthProperty);f.Layout();Assert.True(CssSizeStyleQuery.Matches("width","calc-size(min-content,size + 20px)",node,context));
    }

    [Theory] [InlineData("root",false)] [InlineData("paragraph",false)] [InlineData("section",false)]
    [InlineData("root",true)] [InlineData("paragraph",true)] [InlineData("section",true)]
    public void IntrinsicCalculationsUseTheAuthoredQuantityBox(string target,bool content)
    {
        using var f=new DocumentSizingFixture(target,"height:calc-size(auto,size * 2);box-sizing:"+(content?"content-box":"border-box")+";padding:4px;border:2px solid green;");
        f.Layout();Near(content?60:72,f.AfterY);
    }

    [Theory]
    [InlineData("width:min-content;","min")]
    [InlineData("width:max-content;","max")]
    [InlineData("width:calc-size(min-content,size * 2);","min*2")]
    [InlineData("width:calc-size(auto,size + 20px);","max+20")]
    [InlineData("width:calc-size(any,90px);","90")]
    public void ADocumentTableUsesTheResolvedPreferredWidth(string style,string expected)
    {using var f=new DocumentSizingFixture("table",style);f.Layout();Near(f.ExpectedWidth(expected),f.Text.ActualWidth);}

    [Theory] [InlineData("table")] [InlineData("cell")]
    public void DefiniteCalculatedTableHeightsParticipateInRowAllocation(string target)
    {using var f=new DocumentSizingFixture(target,"height:calc-size(any,80px);");f.Layout();Near(80,f.AfterY);}

    [Fact]
    public void ACalculatedCellWidthSuppliesAnActualTrackRequest()
    {using var f=new DocumentSizingFixture("cell","width:calc-size(any,180px);");f.Layout();Near(180,f.Text.ActualWidth);}

    [Theory] [InlineData("root")] [InlineData("section")]
    public void StretchUsesTheMarginBoxAndSuppliesTheRemainingContentHeight(string target)
    {
        using var f=new DocumentSizingFixture(target,"height:stretch;margin:10px;");var inside=new Paragraph(new Run("内部后文"));
        if(f.Target is Section section)section.Blocks.Add(inside);else f.Document.Blocks.Add(inside);
        Css.SetStyle(f.Source,"height:50%;");f.Layout();Near(120,f.Body.Paragraphs[1].TransformToAncestor(f.Body).Y);
        Near(240,target=="root"?f.Body.DesiredSize.Height:f.Body.Paragraphs[^1].TransformToAncestor(f.Body).Y);
    }

    [Theory] [InlineData("root")] [InlineData("section")]
    public void ThePublicScrollViewerKeepsTheDocumentAndItsLiveSizing(string target)
    {
        using var f=new DocumentSizingFixture(target,"height:calc-size(auto,size * 2);");
        var viewer=new FlowDocumentScrollViewer{Document=f.Document,IsToolBarVisible=false,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};
        viewer.Resources.MergedDictionaries.Add(MenuFlyoutTemplateTestHelper.Themes.Value);
        var root=new StackPanel();root.Children.Add(viewer);DocumentSizingFixture.Layout(root,200,240);
        var presented=Descendants(viewer).OfType<FlowDocumentPresentation>().Single();
        double Height()=>target=="root"?presented.ExtentHeight:presented.Paragraphs[1].TransformToAncestor(presented).Y;
        Near(48,Height());Assert.Same(f.Source.Inlines,presented.Paragraphs[0].Inlines);
        f.Source.Inlines.Add(new LineBreak());f.Source.Inlines.Add(new Run("新增原文"));DocumentSizingFixture.Layout(root,200,240);Near(96,Height());
        viewer.Document=null;root.Children.Clear();
    }

    [Theory]
    [InlineData("normal",48)] [InlineData("pre-wrap",48)]
    [InlineData("pre",24)] [InlineData("nowrap",24)]
    public void TheDocumentProxyUsesCssWrappingInsteadOfItsNativeFallback(string whiteSpace,double expected)
    {
        using var f=new DocumentSizingFixture("root","width:40px;white-space:"+whiteSpace+";");
        f.Layout();Near(expected,f.AfterY);Assert.Same(f.Source.Inlines,f.Text.Inlines);
    }

    [Theory]
    [InlineData("overflow-wrap:normal;",false)] [InlineData("overflow-wrap:break-word;",false)]
    [InlineData("overflow-wrap:anywhere;",true)] [InlineData("word-break:break-all;",true)]
    public void IntrinsicTextRespectsWordBoundariesAndTheEmergencyWrapDistinction(string wrapping,bool letters)
    {
        using var f=new DocumentSizingFixture("root","width:min-content;"+wrapping);f.Layout();
        var word=new TextBlock{Text=letters?"M":"MMMM",FontFamily="PingFang SC",FontSize=16};
        Css.SetStyle(word,"display:block;white-space:pre;width:max-content;");DocumentSizingFixture.Layout(word,200,240);
        Near(word.ActualWidth,f.Text.ActualWidth);Assert.True(f.Text.ActualWidth>0);
    }

    [Fact]
    public void ClearingCssWrappingRestoresTheNativeDocumentFallback()
    {
        using var f=new DocumentSizingFixture("root","width:40px;");
        Css.SetStyleSheet(f.Document,"Paragraph{margin:0;padding:0;border:0;}");f.Layout();var native=f.AfterY;Assert.True(native>48);
        Css.SetStyle(f.Document,"width:40px;white-space:pre-wrap;");f.Layout();Near(48,f.AfterY);
        Css.SetStyle(f.Document,"width:40px;");f.Layout();Near(native,f.AfterY);Assert.Same(f.First,f.Source.Inlines[0]);
    }

    [Theory]
    [InlineData("list","width:min-content;","min")]
    [InlineData("list","width:max-content;","max")]
    [InlineData("list","width:calc-size(min-content,size + 20px);","min+20")]
    [InlineData("list","width:calc-size(any,90px);","70")]
    [InlineData("item","width:min-content;","min")]
    [InlineData("item","width:max-content;","max")]
    [InlineData("item","width:calc-size(min-content,size + 20px);","min+20")]
    [InlineData("item","width:calc-size(any,90px);","90")]
    public void ListsKeepTheirExistingIndentOutsideTheItemContent(string target,string style,string expected)
    {using var f=new DocumentSizingFixture(target,style);f.Layout();Near(f.ExpectedWidth(expected),f.Text.ActualWidth);Assert.Same(f.Source.Inlines,f.Text.Inlines);}

    [Theory]
    [InlineData("list","height:calc-size(auto,size + 24px);",48)]
    [InlineData("item","height:calc-size(auto,size + 24px);",48)]
    [InlineData("list","height:calc-size(any,80px);",80)]
    [InlineData("item","height:calc-size(any,80px);",80)]
    public void ListsReserveTheCalculatedHeightBeforeFollowingBlocks(string target,string style,double expected)
    {using var f=new DocumentSizingFixture(target,style);f.Layout();Near(expected,f.AfterY);Assert.Same(f.First,f.Source.Inlines[0]);}

    [Theory]
    [InlineData("root",false,true)] [InlineData("section",false,true)]
    [InlineData("root",true,true)] [InlineData("section",true,true)]
    [InlineData("root",false,false)] [InlineData("section",false,false)]
    [InlineData("root",true,false)] [InlineData("section",true,false)]
    public void NativeMaximumOverridesTheIntrinsicPercentageBehaviorAndClearingRestoresIt(string target,bool animated,bool usesSize)
    {
        using var f=new DocumentSizingFixture(target,"height:200px;max-height:calc-size(max-content,"+(usesSize?"size * 2":"80px")+");");
        var inside=new Paragraph(new Run("内部后文"));
        if(f.Target is Section section)section.Blocks.Add(inside);else f.Document.Blocks.Add(inside);
        double End()=>target=="root"?f.Body.DesiredSize.Height:f.Body.Paragraphs[^1].TransformToAncestor(f.Body).Y;
        Css.SetStyle(f.Source,"height:50%;");f.Layout();Near(24,f.Body.Paragraphs[1].TransformToAncestor(f.Body).Y);Near(usesSize?96:80,End());
        if(animated)f.Target.SetAnimatedValue(FrameworkElement.MaxHeightProperty,80d,false);else f.Target.SetValue(FrameworkElement.MaxHeightProperty,80d);
        f.Layout();Near(40,f.Body.Paragraphs[1].TransformToAncestor(f.Body).Y);Near(80,End());
        if(animated)f.Target.ClearAnimatedValue(FrameworkElement.MaxHeightProperty);else f.Target.ClearValue(FrameworkElement.MaxHeightProperty);
        f.Layout();Near(24,f.Body.Paragraphs[1].TransformToAncestor(f.Body).Y);Near(usesSize?96:80,End());
    }

    private static IEnumerable<Visual> Descendants(Visual source)
    {
        for(var n=0;n<VisualTreeHelper.GetChildrenCount(source);n++)if(VisualTreeHelper.GetChild(source,n) is Visual child)
        {yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }

    private static void Near(double expected,double actual)=>Assert.InRange(actual,expected-.06,expected+.06);
}

internal sealed class DocumentSizingFixture : IDisposable
{
    internal readonly FlowDocument Document;
    internal readonly Paragraph Source,After=new(new Run("后续记录"));
    internal readonly Inline First;
    internal readonly DependencyObject Target;
    internal readonly DependencyObject? OriginalParent;
    internal readonly FlowDocumentPresentation Body=new();
    internal string Kind;
    internal TextBlock Text=>Body.Paragraphs[0];
    internal double AfterY=>Kind=="root"?Body.DesiredSize.Height:Body.Paragraphs[1].TransformToAncestor(Body).Y;
    internal DocumentSizingFixture(string kind,string style)
    {
        Kind=kind;Source=new();First=new Run("MMMM MMMM");Source.Inlines.Add(First);
        Document=new(){FontFamily="PingFang SC",FontSize=16,LineHeight=24,PagePadding=new(0),PageWidth=200,PageHeight=240};
        if(kind is "table" or "cell")
        {
            var table=new Table{CellSpacing=0};var row=new TableRow();var group=new TableRowGroup();var cell=new TableCell(Source){Padding=new(0),BorderThickness=new(0)};
            row.Cells.Add(cell);group.Rows.Add(row);table.RowGroups.Add(group);Document.Blocks.Add(table);Target=kind=="table"?table:cell;
            if(kind=="cell")Css.SetStyle(table,"width:auto;");
        }
        else if(kind is "list" or "item")
        {var item=new ListItem();item.Blocks.Add(Source);var list=new Jalium.UI.Documents.List();list.ListItems.Add(item);Document.Blocks.Add(list);Target=kind=="list"?list:item;}
        else if(kind=="section"){var section=new Section(Source);Document.Blocks.Add(section);Target=section;}
        else{Document.Blocks.Add(Source);Target=kind=="root"?Document:Source;}
        if(kind!="root")Document.Blocks.Add(After);
        OriginalParent=Source.Parent;
        Css.SetStyleSheet(Document,"FlowDocument{white-space:pre-wrap;}Paragraph,Section,List,ListItem{margin:0;padding:0;border:0;box-sizing:border-box;}");
        if(kind!="root")Css.SetStyle(Document,"height:240px;");
        Css.SetStyle(Target,style);Body.SetDocument(Document);
    }
    internal double ExpectedWidth(string value)
    {
        // The source fixture is two equal unbreakable words. Measure one whole
        // word without wrapping, independently of the document proxy's min probe.
        double Width(string keyword){var text=new TextBlock{Text=keyword=="min-content"?"MMMM":Source.Inlines.GetText(),FontFamily=Source.FontFamily,FontSize=Source.FontSize,FontWeight=Source.FontWeight,FontStyle=Source.FontStyle,FontStretch=Source.FontStretch};Css.SetStyle(text,"display:block;white-space:pre;width:max-content;");Layout(text,200,240);return text.ActualWidth;}
        var min=Width("min-content");var max=Width("max-content");
        return value switch {"min"=>min,"max"=>max,"fit"=>Math.Clamp(200,min,max),"fit100"=>Math.Clamp(100,min,max),"min*2"=>min*2,"min/2+20"=>min/2+20,"min+20"=>min+20,"max+20"=>max+20,"max-20"=>max-20,_=>double.Parse(value,System.Globalization.CultureInfo.InvariantCulture)};
    }
    internal void Layout()=>Layout(Body,200,240);
    internal static void Layout(FrameworkElement view,double width,double height)
    {
        for(var n=0;n<3;n++){Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            view.Measure(new(width,height));view.Arrange(new(0,0,width,height));view.UpdateLayout();}
    }
    public void Dispose()=>Body.Dispose();
}
