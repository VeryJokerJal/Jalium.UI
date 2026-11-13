using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssContentsTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Fact]
    public void ContentsIsATypedDisplayValueAndSupportsQueriesUseTheSameGrammar()
    {
        Assert.True(Css.Supports("display", "contents"));
        Assert.True(CssAuthoring.IsValueSupported("display", "contents"));
        foreach (var invalid in new[] { "inline contents", "contents flow", "contents contents", "contents list-item" })
            Assert.False(Css.Supports("display", invalid));
        var root = new StackPanel(); var child = new StackPanel(); root.Children.Add(child);
        Css.SetStyle(child, "display:contents;"); Layout(root);
        Assert.Equal("Contents", CssDisplayProperties.Computed(CssNode.Get(child)).Mode.ToString());
    }

    [Theory]
    [InlineData("block")] [InlineData("flow-root")] [InlineData("flex")] [InlineData("grid")] [InlineData("table")]
    public void ChildrenParticipateInTheNearestBoxLayoutWithoutChangingSourceParents(string mode)
    {
        using var actual = new Fixture(mode, true); using var reference = new Fixture(mode, false);
        actual.Layout(); reference.Layout(); SameGeometry(actual, reference);
        Assert.Same(actual.Wrapper, actual.Items[1].VisualParent); Assert.Same(actual.Wrapper, actual.Items[2].VisualParent);
        Assert.Equal(3, actual.Root.Children.Count); Assert.Equal(2, actual.Wrapper.Children.Count);
        Assert.Contains(actual.Wrapper,Enumerable.Range(0,VisualTreeHelper.GetChildrenCount(actual.Root)).Select(i=>VisualTreeHelper.GetChild(actual.Root,i)));
        Assert.Equal(default, actual.Wrapper.RenderSize); Assert.Equal(default, actual.Wrapper.DesiredSize);
    }

    [Theory] [InlineData("block")] [InlineData("flex")] [InlineData("grid")] [InlineData("table")]
    public void TogglingContentsRestoresAuthorBoxesAndChildEditsInvalidateTheFormattingAncestor(string mode)
    {
        using var actual = new Fixture(mode, true); using var reference = new Fixture(mode, false);
        actual.Layout(); reference.Layout(); SameGeometry(actual, reference);
        Css.SetStyle(actual.Wrapper, "display:block;padding:13px;border:2px solid red;"); actual.Layout();
        Assert.True(actual.Wrapper.ActualHeight > 0);
        actual.Contents(); actual.Layout(); SameGeometry(actual, reference);
        Css.SetStyle(actual.Items[1], actual.ItemStyle + "width:100px;height:55px;");
        Css.SetStyle(reference.Items[1], reference.ItemStyle + "width:100px;height:55px;");
        actual.Layout(); reference.Layout(); SameGeometry(actual, reference);
        Css.SetStyle(actual.Wrapper, "display:none;"); actual.Layout(); Assert.False(actual.Items[1].IsVisible);
        actual.Contents(); actual.Layout(); SameGeometry(actual, reference);
    }

    [Fact]
    public void NestedContentsKeepsInheritanceSelectorsAndGeneratedContentOnTheOriginalTree()
    {
        using var actual = new Fixture("flex", true); using var reference = new Fixture("flex", false);
        var nested = new StackPanel(); actual.Wrapper.Children.Remove(actual.Items[2]); nested.Children.Add(actual.Items[2]); actual.Wrapper.Children.Add(nested);
        Css.SetStyle(nested, "display:contents;position:absolute;float:left;width:900px;padding:90px;");
        Css.SetClass(actual.Wrapper, "elided"); Css.SetClass(nested, "nested");
        Css.SetStyleSheet(actual.Root, ".elided > .nested > Border {color:rgb(12,34,56);} .elided::before{content:'';display:block;flex:none;width:11px;height:9px;}");
        var generated = new Border(); Css.SetStyle(generated, "display:block;flex:none;width:11px;height:9px;"); reference.Root.Children.Insert(1,generated);
        actual.Layout(); reference.Layout();
        for(var i=0;i<actual.Items.Length;i++) Assert.Equal(reference.Items[i].TransformToAncestor(reference.Root), actual.Items[i].TransformToAncestor(actual.Root));
        Assert.Same(nested,actual.Items[2].VisualParent); Assert.Equal(default, nested.RenderSize);
    }

    [Fact]
    public void RootContentsComputesToBlockAndReplacedImageContentsComputesToNone()
    {
        var root = new StackPanel(); var image = new Image { Width=70, Height=80 }; root.Children.Add(image);
        Css.SetStyle(root,"display:contents;"); Css.SetStyle(image,"display:contents;"); Layout(root);
        Assert.Equal(CssDisplayMode.Block, CssDisplayProperties.Computed(CssNode.Get(root)).Mode);
        Assert.True(CssDisplayProperties.Computed(CssNode.Get(image)).None); Assert.Equal(default,image.DesiredSize);
        Assert.Equal(0,root.DesiredSize.Height);
    }

    [Fact]
    public void ElidedOverflowTransformOpacityAndDecorationDoNotClipOrMovePromotedChildren()
    {
        using var actual=new Fixture("block",true); using var reference=new Fixture("block",false);
        Css.SetStyle(actual.Wrapper, Fixture.ContentsStyle+"overflow:hidden;transform:translate(80px,70px);opacity:0;clip-path:inset(100%);");
        actual.Layout();reference.Layout();SameGeometry(actual,reference);
        var item=actual.Items[1];var p=item.TransformToAncestor(actual.Root);Assert.Same(item,actual.Root.InputHitTest(new(p.X+1,p.Y+1)));
        Assert.Null(actual.Wrapper.GetLayoutClip());
    }

    [Theory] [InlineData("float:left;")] [InlineData("position:absolute;left:25%;top:9px;")] [InlineData("position:relative;left:13px;")]
    public void ChildFloatAndPositionUseTheFormattingAncestor(string rule)
    {
        using var actual=new Fixture("block",true);using var reference=new Fixture("block",false);
        foreach(var f in new[]{actual,reference}){Css.SetStyle(f.Items[1],f.ItemStyle+"width:60px;height:30px;"+rule);Css.SetStyle(f.Items[2],f.ItemStyle+"width:80px;height:40px;clear:both;");}
        actual.Layout();reference.Layout();SameGeometry(actual,reference);
    }

    [Theory] [InlineData("block")] [InlineData("flex")] [InlineData("grid")]
    public void StructuralAndStyleEditsReflowWithoutManuallyInvalidatingTheRoot(string mode)
    {
        using var actual=new Fixture(mode,true);using var reference=new Fixture(mode,false);actual.Layout();reference.Layout();
        var a=new Border();var b=new Border();Css.SetStyle(a,actual.ItemStyle+"width:30px;height:65px;");Css.SetStyle(b,reference.ItemStyle+"width:30px;height:65px;");
        actual.Wrapper.Children.Insert(1,a);reference.Root.Children.Insert(2,b);actual.Layout();reference.Layout();SameGeometry(actual,reference);
        Assert.Equal(b.TransformToAncestor(reference.Root),a.TransformToAncestor(actual.Root));
        actual.Wrapper.Children.Remove(a);reference.Root.Children.Remove(b);actual.Layout();reference.Layout();SameGeometry(actual,reference);
    }

    [Fact]
    public void DisplayQueriesBlockifyItemsAcrossContentsButKeepTheContentsValue()
    {
        var root=new StackPanel();var group=new StackPanel();var text=new TextBlock{Text="原文"};root.Children.Add(group);group.Children.Add(text);
        Css.SetStyle(root,"display:flex;");Css.SetStyle(group,"display:contents;");Css.SetStyle(text,"display:inline;");Layout(root);
        Assert.True(CssDisplayStyleQuery.Matches("contents",CssNode.Get(group),CssLengthContext.Default));
        Assert.True(CssDisplayStyleQuery.Matches("block",CssNode.Get(text),CssLengthContext.Default));
        Assert.Equal(CssDisplayMode.Block,CssDisplayProperties.Computed(CssNode.Get(text)).Mode);
        Assert.True(CssDisplayStyleQuery.Matches("inline",CssNode.Get(text),CssLengthContext.Default));
        Css.SetStyle(root,"display:contents;");Layout(root);
        Assert.True(CssDisplayStyleQuery.Matches("block",CssNode.Get(root),CssLengthContext.Default));
        Assert.True(CssDisplayStyleQuery.Matches("inline",CssNode.Get(text),CssLengthContext.Default));
    }

    [Fact]
    public void PromotedSubgridBorrowsItsActualParentTracks()
    {
        var root=new StackPanel();var group=new StackPanel();var subgrid=new StackPanel();var first=new Border();var second=new Border();
        root.Children.Add(group);group.Children.Add(subgrid);subgrid.Children.Add(first);subgrid.Children.Add(second);
        Css.SetStyle(root,"display:grid;grid-template-columns:60px 120px;gap:7px;align-items:start;");Css.SetStyle(group,"display:contents;");
        Css.SetStyle(subgrid,"display:grid;grid-template-columns:subgrid;grid-column:span 2;");foreach(var item in new[]{first,second})Css.SetStyle(item,"display:block;height:20px;min-width:0;");Layout(root);
        Assert.Equal(60,first.ActualWidth,4);Assert.Equal(120,second.ActualWidth,4);Assert.Equal(67,second.TransformToAncestor(subgrid).X,4);
        Assert.Same(group,subgrid.VisualParent);
    }

    [Fact]
    public void ColumnsFragmentPromotedBoxesWithoutApplyingTheElidedBreakRules()
    {
        using var actual=new Fixture("block",true);using var reference=new Fixture("block",false);
        foreach(var f in new[]{actual,reference})Css.SetStyle(f.Root,"display:block;columns:2;column-gap:20px;height:80px;column-fill:auto;");
        Css.SetStyle(actual.Wrapper,Fixture.ContentsStyle+"break-inside:avoid;break-before:column;column-span:all;");actual.Layout();reference.Layout();SameGeometry(actual,reference);
        Assert.Same(actual.Wrapper,actual.Items[2].VisualParent);
    }

    [Fact]
    public void ContentsTextJoinsTheSharedLineAndRetainsOriginalTextAndGeometry()
    {
        var root=new StackPanel();var first=new TextBlock{Text="AB"};var text=new TextBlock{Text="CD"};var last=new TextBlock{Text="EF"};
        root.Children.Add(first);root.Children.Add(text);root.Children.Add(last);Css.SetStyle(root,"display:block;font-family:'Courier New';font-size:16px;line-height:24px;");
        foreach(var t in new[]{first,last})Css.SetStyle(t,"display:inline;");Css.SetStyle(text,"display:contents;margin:40px;padding:30px;border:5px solid red;");Layout(root);
        var a=first.DocumentTextBounds(0,2).Single();var b=text.DocumentTextBounds(0,2).Single();var c=last.DocumentTextBounds(0,2).Single();
        Assert.Equal(a.Right,b.Left,4);Assert.Equal(b.Right,c.Left+last.VisualBounds.X,4);Assert.Equal("CD",text.Text);Assert.Equal(default,text.RenderSize);
        Assert.Equal("CD",text.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void FocusSkipsTheBoxlessElementAndKeepsSourceOrderedDescendants()
    {
        Keyboard.Initialize();using var f=new Fixture("flex",true);f.Wrapper.Focusable=true;
        foreach(var item in f.Items)item.Focusable=true;f.Layout();
        try{Assert.False(f.Wrapper.Focus());Assert.True(f.Items[0].Focus());Assert.True(f.Items[0].MoveFocus(FocusNavigationDirection.Next));Assert.True(f.Items[1].IsFocused);Assert.True(f.Items[1].MoveFocus(FocusNavigationDirection.Next));Assert.True(f.Items[2].IsFocused);}
        finally{Keyboard.ClearFocus();}
    }

    [Theory] [InlineData("block")] [InlineData("flex")] [InlineData("grid")]
    public void NativeHiddenReservesThePromotedLayoutAndCollapsedRemovesIt(string mode)
    {
        using var actual=new Fixture(mode,true);using var reference=new Fixture(mode,false);
        actual.Wrapper.Visibility=Visibility.Hidden;actual.Layout();reference.Layout();SameGeometry(actual,reference);
        actual.Wrapper.Visibility=Visibility.Collapsed;actual.Layout();Assert.False(actual.Items[1].IsVisible);
        actual.Wrapper.Visibility=Visibility.Visible;actual.Layout();SameGeometry(actual,reference);
    }

    [Fact]
    public void InheritanceAndChildSelectorsStillFollowTheBoxlessSourceElement()
    {
        var root=new StackPanel();var group=new StackPanel();var text=new TextBlock{Text="原文"};root.Children.Add(group);group.Children.Add(text);
        Css.SetClass(group,"group");Css.SetStyle(root,"display:block;");Css.SetStyle(group,"display:contents;color:rgb(12,34,56);font-size:23px;");
        Css.SetStyleSheet(root,".group > TextBlock{line-height:31px;} StackPanel > StackPanel > TextBlock{letter-spacing:2px;}");Layout(root);
        Assert.Equal(23,text.FontSize);Assert.Equal(31,text.LineHeight);Assert.Equal(Color.FromRgb(12,34,56),Assert.IsType<SolidColorBrush>(text.Foreground).Color);
        Assert.Same(group,text.VisualParent);Assert.Equal("原文",text.Text);
    }

    [Fact]
    public void RootContentsUsesBlockClampingWhileNonRootContentsHasNoClampBox()
    {
        var root=new StackPanel();var group=new StackPanel();var text=new TextBlock{Text="ONE\nTWO\nTHREE"};root.Children.Add(group);group.Children.Add(text);
        Css.SetStyle(root,"display:contents;font-family:'Courier New';font-size:16px;line-height:24px;white-space:pre;line-clamp:1 no-ellipsis;");
        Css.SetStyle(group,"display:contents;line-clamp:1 no-ellipsis;");Css.SetStyle(text,"display:block;");Layout(root);
        Assert.NotEmpty(text.DocumentTextBounds(0,3));Assert.Empty(text.DocumentTextBounds(4,3));
        Css.SetStyle(root,"display:contents;font-family:'Courier New';font-size:16px;line-height:24px;white-space:pre;");Layout(root);
        Assert.NotEmpty(text.DocumentTextBounds(4,3));Assert.NotEmpty(text.DocumentTextBounds(8,5));
    }

    [Fact]
    public void DiscreteDisplayAnimationRebuildsTheBoxProjectionAndRestoresItsBase()
    {
        using var actual=new Fixture("block",true);using var reference=new Fixture("block",false);actual.Layout();reference.Layout();
        var contents=(CssDisplaySpecification)actual.Wrapper.GetValue(CssDisplayProperties.SpecificationProperty)!;
        Css.SetStyle(actual.Wrapper,"display:block;padding:13px;");actual.Layout();var block=(CssDisplaySpecification)actual.Wrapper.GetValue(CssDisplayProperties.SpecificationProperty)!;
        var animation=new CssDisplayAnimation{From=block,To=contents};var clock=animation.CreateClock();
        try
        {
            clock.SetProgressForSampling(.25);actual.Wrapper.SetCssAnimatedValue(CssDisplayProperties.SpecificationProperty,animation.GetCurrentValue(block,contents,clock));actual.Layout();Assert.True(actual.Wrapper.ActualHeight>0);
            clock.SetProgressForSampling(.75);actual.Wrapper.SetCssAnimatedValue(CssDisplayProperties.SpecificationProperty,animation.GetCurrentValue(block,contents,clock));actual.Layout();SameGeometry(actual,reference);
        }
        finally{actual.Wrapper.ClearCssAnimatedValue(CssDisplayProperties.SpecificationProperty);}
        actual.Layout();Assert.True(actual.Wrapper.ActualHeight>0);Assert.Same(actual.Wrapper,actual.Items[1].VisualParent);
    }

    private static void SameGeometry(Fixture actual,Fixture reference)
    {
        Assert.Equal(reference.Root.DesiredSize,actual.Root.DesiredSize);
        for(var i=0;i<actual.Items.Length;i++)
        {
            Assert.Equal(reference.Items[i].RenderSize,actual.Items[i].RenderSize);
            Assert.Equal(reference.Items[i].TransformToAncestor(reference.Root),actual.Items[i].TransformToAncestor(actual.Root));
            Assert.True(actual.Items[i].IsMeasureValid); Assert.True(actual.Items[i].IsArrangeValid);
        }
    }
    internal static void Layout(FrameworkElement root,double width=300)
    {
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);root.Measure(new(width,200));root.Arrange(new Rect(0,0,width,200));root.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);root.Measure(new(width,200));root.Arrange(new Rect(0,0,width,200));root.UpdateLayout();
    }
    private sealed class Fixture : IDisposable
    {
        internal const string ContentsStyle="display:contents;width:900px;height:800px;min-width:700px;margin:21px;padding:19px;border:7px solid red;background:red;";
        internal StackPanel Root=new(),Wrapper=new(); internal Border[] Items=[new(),new(),new(),new()];
        internal string ItemStyle;
        internal Fixture(string mode,bool wrapped)
        {
            ItemStyle=(mode=="table"?"display:table-cell;":"display:block;")+"flex:none;min-width:0;min-height:0;background:green;";
            Css.SetStyle(Root,$"display:{mode};gap:7px;grid-template-columns:80px 80px;border-spacing:0;align-items:start;");
            for(var i=0;i<Items.Length;i++) Css.SetStyle(Items[i],ItemStyle+$"width:{40+i*20}px;height:{20+i*10}px;");
            Root.Children.Add(Items[0]);
            if(wrapped){Root.Children.Add(Wrapper);Wrapper.Children.Add(Items[1]);Wrapper.Children.Add(Items[2]);Contents();}
            else {Root.Children.Add(Items[1]);Root.Children.Add(Items[2]);}
            Root.Children.Add(Items[3]);
        }
        internal void Contents()=>Css.SetStyle(Wrapper,ContentsStyle);
        internal void Layout()=>CssContentsTests.Layout(Root);
        public void Dispose(){Css.SetStyleSheet(Root,"");Root.Children.Clear();Wrapper.Children.Clear();}
    }
}
