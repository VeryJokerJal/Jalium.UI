using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssContentsParentTests : IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose()=>_context.Dispose();

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CollapsedFlexItemsSuppressVisibleDescendantsAcrossABoxlessParent(bool borderHost)
    {
        var group=new StackPanel();var leaf=new Border{Width=10,Height=10};var item=new Border{Child=leaf};group.Children.Add(item);
        FrameworkElement root;
        if(borderHost)root=new Border{Child=group};else{var panel=new StackPanel();panel.Children.Add(group);root=panel;}
        Css.SetStyle(root,"display:flex;width:200px;");Css.SetStyle(group,"display:contents;");Css.SetStyle(item,"display:block;width:60px;height:30px;visibility:collapse;");Css.SetStyle(leaf,"visibility:visible;");
        CssContentsTests.Layout(root);Assert.False(leaf.IsVisible);Assert.NotSame(leaf,root.InputHitTest(new(1,1)));
        Css.SetStyle(item,"display:block;width:60px;height:30px;visibility:visible;");CssContentsTests.Layout(root);Assert.True(leaf.IsVisible);Assert.Same(item,leaf.VisualParent);Assert.Same(group,item.VisualParent);
        Css.SetStyle(group,"display:contents;visibility:collapse;");CssContentsTests.Layout(root);Assert.True(leaf.IsVisible);
    }

    [Theory] [InlineData("auto",120)] [InlineData("calc-size(auto,size)",120)] [InlineData("calc-size(auto,size + 20px)",140)]
    public void StretchedGridItemsGivePercentDescendantsTheSameDefiniteHeight(string height,double expected)
    {
        var actual=GridItem(true,height);var reference=GridItem(false,height);CssContentsTests.Layout(actual.Root);CssContentsTests.Layout(reference.Root);
        Assert.Equal(expected,reference.Item.ActualHeight,4);Assert.Equal(expected/2,reference.Percent.ActualHeight,4);
        Assert.Equal(reference.Item.ActualHeight,actual.Item.ActualHeight,4);Assert.Equal(reference.Percent.ActualHeight,actual.Percent.ActualHeight,4);
        Assert.Same(actual.Group,actual.Item.VisualParent);
    }

    [Fact]
    public void RelativeOverconstraintUsesTheContainingBoxDirectionAndPreservesTextDirectionInheritance()
    {
        var root=new StackPanel();var group=new StackPanel();var item=new Border();root.Children.Add(group);group.Children.Add(item);
        Css.SetStyle(root,"display:block;direction:ltr;");Css.SetStyle(group,"display:contents;direction:rtl;");Css.SetStyle(item,"display:block;width:50px;height:20px;position:relative;left:10px;right:20px;");CssContentsTests.Layout(root);
        Assert.Equal(FlowDirection.RightToLeft,item.FlowDirection);Assert.Equal(10,item.TransformToAncestor(root).X,4);
    }

    [Fact]
    public void PromotedColumnsUseTheirGridAllocationForOverflowColumns()
    {
        (StackPanel Root,StackPanel Group,Border[] Items) Make(bool wrapped)
        {
            var root=new StackPanel();var group=new StackPanel();var column=new StackPanel();var items=Enumerable.Range(0,5).Select(_=>new Border()).ToArray();
            Css.SetStyle(root,"display:grid;width:200px;height:80px;grid-template-columns:200px;grid-template-rows:80px;");Css.SetStyle(group,"display:contents;");Css.SetStyle(column,"display:block;columns:2;column-gap:20px;column-fill:auto;");
            foreach(var item in items){Css.SetStyle(item,"display:block;height:40px;");column.Children.Add(item);}if(wrapped){root.Children.Add(group);group.Children.Add(column);}else root.Children.Add(column);return(root,group,items);
        }
        var actual=Make(true);var reference=Make(false);CssContentsTests.Layout(actual.Root);CssContentsTests.Layout(reference.Root);
        for(var i=0;i<5;i++){Assert.Equal(reference.Items[i].RenderSize,actual.Items[i].RenderSize);Assert.Equal(reference.Items[i].TransformToAncestor(reference.Root),actual.Items[i].TransformToAncestor(actual.Root));}
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NestedFlowSharesEscapedMarginsAndFloatsWithTheActualParent(bool floated)
    {
        (StackPanel Root,StackPanel Group,StackPanel Item,Border Next) Make(bool wrapped)
        {
            var root=new StackPanel();var group=new StackPanel();var item=new StackPanel();var child=new Border();var next=new Border();
            Css.SetStyle(root,"display:block;width:200px;");Css.SetStyle(group,"display:contents;");Css.SetStyle(item,"display:block;");
            Css.SetStyle(child,floated?"display:block;float:left;width:40px;height:60px;":"display:block;height:20px;margin:30px 0 15px;");item.Children.Add(child);
            if(floated){var normal=new Border();Css.SetStyle(normal,"display:block;height:20px;");item.Children.Add(normal);}
            Css.SetStyle(next,"display:block;height:10px;margin-top:5px;");if(wrapped){root.Children.Add(group);group.Children.Add(item);}else root.Children.Add(item);root.Children.Add(next);return(root,group,item,next);
        }
        var actual=Make(true);var reference=Make(false);CssContentsTests.Layout(actual.Root);CssContentsTests.Layout(reference.Root);
        Assert.Equal(reference.Root.DesiredSize,actual.Root.DesiredSize);Assert.Equal(reference.Item.RenderSize,actual.Item.RenderSize);Assert.Equal(reference.Next.TransformToAncestor(reference.Root),actual.Next.TransformToAncestor(actual.Root));
    }

    [Fact]
    public void NestedInlineLinesStillConsumeTheOuterClampBudget()
    {
        var root=new StackPanel();var group=new StackPanel();var item=new StackPanel();var first=new TextBlock{Text="ONE\nTWO"};var last=new TextBlock{Text="THREE"};
        root.Children.Add(group);group.Children.Add(item);item.Children.Add(first);root.Children.Add(last);
        Css.SetStyle(root,"display:block;line-clamp:2 no-ellipsis;white-space:pre;font-family:'Courier New';font-size:16px;line-height:24px;");Css.SetStyle(group,"display:contents;");Css.SetStyle(item,"display:block;");Css.SetStyle(first,"display:block;");Css.SetStyle(last,"display:block;");CssContentsTests.Layout(root);
        Assert.NotEmpty(first.DocumentTextBounds(4,3));Assert.Empty(last.GetSourceRangeSegments(0,5));Assert.Empty(last.DocumentTextBounds(0,5));
    }

    [Fact]
    public void ASpanningChildCanCrossABoxlessAncestorInsideColumns()
    {
        var root=new StackPanel();var group=new StackPanel();var before=new Border();var span=new Border();var after=new Border();root.Children.Add(before);root.Children.Add(group);group.Children.Add(span);root.Children.Add(after);
        Css.SetStyle(root,"display:block;width:200px;columns:2;column-gap:20px;");Css.SetStyle(group,"display:contents;");Css.SetStyle(before,"display:block;height:20px;");Css.SetStyle(span,"display:block;column-span:all;height:20px;");Css.SetStyle(after,"display:block;height:20px;");CssContentsTests.Layout(root);
        var reference=new StackPanel();var referenceBefore=new Border();var referenceSpan=new Border();var referenceAfter=new Border();reference.Children.Add(referenceBefore);reference.Children.Add(referenceSpan);reference.Children.Add(referenceAfter);
        Css.SetStyle(reference,Css.GetStyle(root));Css.SetStyle(referenceBefore,Css.GetStyle(before));Css.SetStyle(referenceSpan,Css.GetStyle(span));Css.SetStyle(referenceAfter,Css.GetStyle(after));CssContentsTests.Layout(reference);
        Assert.Equal(200,span.ActualWidth,4);Assert.Equal(referenceSpan.TransformToAncestor(reference),span.TransformToAncestor(root));
    }

    [Fact]
    public void LogicalClearUsesTheContainingBoxDirection()
    {
        var root=new StackPanel();var floating=new Border();var group=new StackPanel();var last=new Border();root.Children.Add(floating);root.Children.Add(group);group.Children.Add(last);
        Css.SetStyle(root,"display:block;width:200px;direction:ltr;");Css.SetStyle(floating,"display:block;float:left;width:40px;height:60px;");Css.SetStyle(group,"display:contents;direction:rtl;");Css.SetStyle(last,"display:block;height:20px;clear:inline-start;");CssContentsTests.Layout(root);
        Assert.Equal(FlowDirection.RightToLeft,last.FlowDirection);Assert.Equal(60,last.TransformToAncestor(root).Y,4);
    }

    private static (StackPanel Root,StackPanel Group,StackPanel Item,Border Percent) GridItem(bool wrapped,string height)
    {
        var root=new StackPanel();var group=new StackPanel();var item=new StackPanel();var percent=new Border{Child=new Border{Height=12}};item.Children.Add(percent);
        Css.SetStyle(root,"display:grid;width:200px;height:120px;grid-template-columns:200px;grid-template-rows:120px;");Css.SetStyle(group,"display:contents;");Css.SetStyle(item,$"display:block;height:{height};");Css.SetStyle(percent,"display:block;height:50%;");
        if(wrapped){root.Children.Add(group);group.Children.Add(item);}else root.Children.Add(item);return(root,group,item,percent);
    }
}
