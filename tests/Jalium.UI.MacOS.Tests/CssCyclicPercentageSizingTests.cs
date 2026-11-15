using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCyclicPercentageSizingTests:IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose()=>_context.Dispose();
    public static IEnumerable<object[]> GeometryCases(){foreach(var c in Cases)yield return [c.Name];}
    [Theory] [MemberData(nameof(GeometryCases))]
    public void ContributionsAndUsedPercentagesRespectTheirOwnContainingBlock(string name)
    {
        using var f=new CycleFixture(Cases.Single(c=>c.Name==name));f.Layout();
        var actual=new Dictionary<string,double>{{"width",f.Box.ActualWidth},{"height",f.Box.ActualHeight},{"itemWidth",f.Item.ActualWidth},{"itemHeight",f.Item.ActualHeight}};
        if(f.Percent is {} percent)actual["percentHeight"]=percent.ActualHeight;
        foreach(var pair in f.Case.Expected!)Near(pair.Value,actual[pair.Key]);
        Assert.Same(f.Item,f.Box.Children[0]);if(f.Item is Border item)Assert.Same(f.Percent??f.Leaf,item.Child);
    }
    [Fact]
    public void NativeAndAnimatedWidthsOverrideCyclicCSSAndRestoreItsContribution()
    {
        using var f=new CycleFixture(Cases.Single(c=>c.Name=="min-half"));f.Layout();Near(80,f.Box.ActualWidth);Near(40,f.Item.ActualWidth);
        f.Item.Width=160;f.Layout();Near(160,f.Box.ActualWidth);Near(160,f.Item.ActualWidth);
        f.Item.ClearValue(FrameworkElement.WidthProperty);f.Layout();Near(80,f.Box.ActualWidth);Near(40,f.Item.ActualWidth);
        f.Item.Width=double.NaN;f.Layout();Near(80,f.Box.ActualWidth);Near(80,f.Item.ActualWidth);
        f.Item.ClearValue(FrameworkElement.WidthProperty);f.Layout();Near(40,f.Item.ActualWidth);
        f.Item.SetAnimatedValue(FrameworkElement.WidthProperty,60d,false);f.Layout();Near(60,f.Box.ActualWidth);Near(60,f.Item.ActualWidth);
        f.Item.ClearAnimatedValue(FrameworkElement.WidthProperty);f.Layout();Near(80,f.Box.ActualWidth);Near(40,f.Item.ActualWidth);
    }
    [Fact]
    public void ContentAndDefiniteContainerChangesInvalidateContributionCaches()
    {
        using var f=new CycleFixture(Cases.Single(c=>c.Name=="min-calc"));f.Layout();Near(80,f.Box.ActualWidth);Near(50,f.Item.ActualWidth);
        f.Leaf.Width=120;f.Layout();Near(120,f.Box.ActualWidth);Near(70,f.Item.ActualWidth);
        Css.SetStyle(f.Box,"display:block;width:200px;");f.Layout();Near(200,f.Box.ActualWidth);Near(110,f.Item.ActualWidth);
        Css.SetStyle(f.Box,f.Case.BoxStyle);f.Layout();Near(120,f.Box.ActualWidth);Near(70,f.Item.ActualWidth);
        f.Leaf.Width=80;f.Layout();Near(80,f.Box.ActualWidth);Near(50,f.Item.ActualWidth);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ADefiniteNestedWidthBreaksThePercentageCycle(bool percentage)
    {
        var root=new StackPanel();var box=new StackPanel();var leaf=new Border{Width=80,Height=40};var inner=new Border{Child=leaf};var item=new Border{Child=inner};root.Children.Add(box);box.Children.Add(item);
        Css.SetStyle(root,"display:block;width:400px;");Css.SetStyle(box,"display:block;width:min-content;");
        Css.SetStyle(item,percentage?"display:block;width:100%;":"display:block;width:160px;");Css.SetStyle(inner,"display:block;width:50%;");
        Layout(root);Near(percentage?80:160,box.ActualWidth);Near(percentage?40:80,inner.ActualWidth);
        box.Children.Clear();root.Children.Clear();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void AReplacedMinimumContributionTransfersTheOppositeMinimumThroughItsRatio(bool padded)
    {
        using var f=new CycleFixture(Cases.Single(c=>c.Name=="image-min-calc"));
        Css.SetStyle(f.Item,"display:block;min-height:20px;width:calc(50% + 10px);"+(padded?"box-sizing:content-box;padding:4px;border:2px solid green;":""));
        f.Layout();Near(padded?52:40,f.Box.ActualWidth);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeBoxEdgesRemainDefiniteDuringIntrinsicQueries(bool margin)
    {
        using var f=new CycleFixture(Cases.Single(c=>c.Name=="min-half"));
        Css.SetStyle(f.Item,"display:block;width:50%;"+(margin?"margin:10%;":"padding:10%;"));
        if(margin)f.Item.Margin=new Thickness(10);else ((Border)f.Item).Padding=new Thickness(10);
        f.Layout();Near(100,f.Box.ActualWidth);Near(margin?50:50,f.Item.ActualWidth);
        if(margin)f.Item.ClearValue(FrameworkElement.MarginProperty);else f.Item.ClearValue(Border.PaddingProperty);
        f.Layout();Near(80,f.Box.ActualWidth);Near(40,f.Item.ActualWidth);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void DefiniteAndIntrinsicColumnBasesProvideDifferentPercentageHeights(bool native)
    {
        var root=new StackPanel();Panel flex=native?new FlexPanel():new StackPanel();var leaf=new Border{Width=80,Height=40};var percent=new Border{Child=leaf};var item=new Border{Child=percent};root.Children.Add(flex);flex.Children.Add(item);
        Css.SetStyle(root,"display:block;width:400px;");Css.SetStyle(flex,"display:flex;flex-direction:column;align-items:flex-start;width:150px;");Css.SetStyle(percent,"display:block;height:50%;");
        Css.SetStyle(item,"display:block;min-height:0;flex:0 0 content;");Layout(root);Near(40,item.ActualHeight);Near(40,percent.ActualHeight);
        Css.SetStyle(item,"display:block;min-height:0;flex:0 0 80px;");Layout(root);Near(80,item.ActualHeight);Near(40,percent.ActualHeight);
        Css.SetStyle(item,"display:block;min-height:0;flex:0 0 min-content;");Layout(root);Near(40,item.ActualHeight);Near(40,percent.ActualHeight);
        FlexPanel.SetBasis(item,60);Layout(root);Near(60,item.ActualHeight);Near(30,percent.ActualHeight);
        item.ClearValue(FlexPanel.BasisProperty);Layout(root);Near(40,item.ActualHeight);Near(40,percent.ActualHeight);
        flex.Children.Clear();root.Children.Clear();
    }
    [Fact]
    public void AnExceptionalIntrinsicQueryRestoresEveryDescendantContext()
    {
        var root=new StackPanel();var box=new StackPanel();var child=new ThrowOnZero();box.Children.Add(child);root.Children.Add(box);
        Css.SetStyle(root,"display:block;width:400px;");Css.SetStyle(box,"display:block;width:min-content;");Css.SetStyle(child,"display:block;width:50%;");
        Dispatcher.CurrentDispatcher.ProcessQueue();Assert.Throws<InvalidOperationException>(()=>root.Measure(new(400,300)));
        child.Throw=false;child.InvalidateMeasure();Layout(root);Near(80,box.ActualWidth);Near(40,child.ActualWidth);
        Css.SetStyle(box,"display:block;width:200px;");Layout(root);Near(100,child.ActualWidth);
        box.Children.Clear();root.Children.Clear();
    }
    private sealed class ThrowOnZero:FrameworkElement
    {public bool Throw=true;protected override Size MeasureOverride(Size size){if(Throw&&size.Width==0)throw new InvalidOperationException("intrinsic");return new(80,40);}}
    private static void Layout(FrameworkElement root){for(var i=0;i<4;i++){Dispatcher.CurrentDispatcher.ProcessQueue();root.Measure(new(400,300));root.Arrange(new(0,0,400,300));root.UpdateLayout();}}
    private static void Near(double expected,double actual)=>Assert.InRange(actual,expected-.06,expected+.06);
    internal static readonly CycleCase[] Cases =
    [
        new("min-half", "min", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-calc", "min", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-zero-term", "min", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-max", "min", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-max-calc", "min", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-min", "min", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-min-calc", "min", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-fixed-max", "min", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-percent-min-fixed", "min", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("min-stretch", "min", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("max-half", "max", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-calc", "max", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-zero-term", "max", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-max", "max", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-max-calc", "max", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-min", "max", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-min-calc", "max", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-fixed-max", "max", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-percent-min-fixed", "max", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("max-stretch", "max", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("fit-half", "fit", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-calc", "fit", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-zero-term", "fit", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-max", "fit", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-max-calc", "fit", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-min", "fit", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-min-calc", "fit", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-fixed-max", "fit", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-percent-min-fixed", "fit", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("fit-stretch", "fit", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:fit-content;"),
        new("inline-half", "inline", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-calc", "inline", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-zero-term", "inline", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-max", "inline", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-max-calc", "inline", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-min", "inline", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-min-calc", "inline", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-fixed-max", "inline", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-percent-min-fixed", "inline", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("inline-stretch", "inline", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("float-half", "float", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-calc", "float", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-zero-term", "float", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-max", "float", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-max-calc", "float", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-min", "float", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-min-calc", "float", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-fixed-max", "float", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-percent-min-fixed", "float", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("float-stretch", "float", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("grid-min-half", "grid-min", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-calc", "grid-min", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-zero-term", "grid-min", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-max", "grid-min", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-max-calc", "grid-min", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-min", "grid-min", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-min-calc", "grid-min", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-fixed-max", "grid-min", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-percent-min-fixed", "grid-min", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-min-stretch", "grid-min", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-half", "grid-max", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-calc", "grid-max", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-zero-term", "grid-max", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-max", "grid-max", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-max-calc", "grid-max", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-min", "grid-max", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-min-calc", "grid-max", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-fixed-max", "grid-max", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-percent-min-fixed", "grid-max", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("grid-max-stretch", "grid-max", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("inline-flex-half", "inline-flex", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-calc", "inline-flex", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-zero-term", "inline-flex", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-max", "inline-flex", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-max-calc", "inline-flex", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-min", "inline-flex", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-min-calc", "inline-flex", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-fixed-max", "inline-flex", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-percent-min-fixed", "inline-flex", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-flex-stretch", "inline-flex", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-flex;"),
        new("inline-grid-half", "inline-grid", "", "width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-calc", "inline-grid", "", "width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-zero-term", "inline-grid", "", "width:calc(10px + 0%);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-max", "inline-grid", "", "max-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-max-calc", "inline-grid", "", "max-width:calc(50% + 10px);", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-min", "inline-grid", "", "min-width:50%;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-min-calc", "inline-grid", "", "min-width:calc(50% + 100px);", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-fixed-max", "inline-grid", "", "width:20px;max-width:50%;", "block", 80, 40, 0, new() { {"width", 20}, {"itemWidth", 10} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-percent-min-fixed", "inline-grid", "", "width:50%;min-width:100px;", "block", 80, 40, 0, new() { {"width", 100}, {"itemWidth", 100} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("inline-grid-stretch", "inline-grid", "", "width:stretch;", "block", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-grid;"),
        new("image-min-half", "min", "", "width:50%;", "image", 80, 40, 0, new() { {"width", 0}, {"itemWidth", 0} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("image-min-calc", "min", "", "width:calc(50% + 10px);", "image", 80, 40, 0, new() { {"width", 10}, {"itemWidth", 15} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("image-min-max", "min", "", "max-width:50%;", "image", 80, 40, 0, new() { {"width", 0}, {"itemWidth", 0} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("image-max-half", "max", "", "width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("image-max-calc", "max", "", "width:calc(50% + 10px);", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("image-max-max", "max", "", "max-width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("image-inline-half", "inline", "", "width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("image-inline-calc", "inline", "", "width:calc(50% + 10px);", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("image-inline-max", "inline", "", "max-width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:inline-block;"),
        new("image-float-half", "float", "", "width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("image-float-calc", "float", "", "width:calc(50% + 10px);", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("image-float-max", "float", "", "max-width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("image-grid-min-half", "grid-min", "", "width:50%;", "image", 80, 40, 0, new() { {"width", 0}, {"itemWidth", 0} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("image-grid-min-calc", "grid-min", "", "width:calc(50% + 10px);", "image", 80, 40, 0, new() { {"width", 10}, {"itemWidth", 15} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("image-grid-min-max", "grid-min", "", "max-width:50%;", "image", 80, 40, 0, new() { {"width", 0}, {"itemWidth", 0} }, "width:400px;height:300px;display:grid;grid-template-columns:min-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("image-grid-max-half", "grid-max", "", "width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("image-grid-max-calc", "grid-max", "", "width:calc(50% + 10px);", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 50} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("image-grid-max-max", "grid-max", "", "max-width:50%;", "image", 80, 40, 0, new() { {"width", 80}, {"itemWidth", 40} }, "width:400px;height:300px;display:grid;grid-template-columns:max-content;align-items:start;", "box-sizing:border-box;display:block;"),
        new("edges-108", "min", "", "padding:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 80}, {"itemHeight", 56} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("edges-109", "max", "", "padding:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 80}, {"itemHeight", 56} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("edges-110", "float", "", "padding:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 80}, {"itemHeight", 56} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("edges-111", "min", "", "margin:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 40}, {"itemWidth", 64}, {"itemHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("edges-112", "max", "", "margin:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 40}, {"itemWidth", 64}, {"itemHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("edges-113", "float", "", "margin:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 64}, {"itemHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("edges-114", "min", "", "padding:calc(10px + 10%);", "block", 80, 40, 0, new() { {"width", 100}, {"height", 80}, {"itemWidth", 100}, {"itemHeight", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("edges-115", "max", "", "padding:calc(10px + 10%);", "block", 80, 40, 0, new() { {"width", 100}, {"height", 80}, {"itemWidth", 100}, {"itemHeight", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("edges-116", "float", "", "padding:calc(10px + 10%);", "block", 80, 40, 0, new() { {"width", 100}, {"height", 80}, {"itemWidth", 100}, {"itemHeight", 80} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("edges-117", "min", "", "width:50%;padding:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 40}, {"itemHeight", 56} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("edges-118", "max", "", "width:50%;padding:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 40}, {"itemHeight", 56} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("edges-119", "float", "", "width:50%;padding:10%;", "block", 80, 40, 0, new() { {"width", 80}, {"height", 56}, {"itemWidth", 40}, {"itemHeight", 56} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("edges-120", "min", "", "min-width:calc(50% + 100px);padding:10%;", "block", 80, 40, 0, new() { {"width", 100}, {"height", 60}, {"itemWidth", 150}, {"itemHeight", 60} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:min-content;"),
        new("edges-121", "max", "", "min-width:calc(50% + 100px);padding:10%;", "block", 80, 40, 0, new() { {"width", 100}, {"height", 60}, {"itemWidth", 150}, {"itemHeight", 60} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;"),
        new("edges-122", "float", "", "min-width:calc(50% + 100px);padding:10%;", "block", 80, 40, 0, new() { {"width", 100}, {"height", 60}, {"itemWidth", 150}, {"itemHeight", 60} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;float:left;"),
        new("height-min-content", "max", "width:150px;height:min-content;", "height:50%;", "block", 80, 150, 30, new() { {"width", 150}, {"height", 180}, {"itemHeight", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;width:150px;height:min-content;"),
        new("height-max-content", "max", "width:150px;height:max-content;", "height:50%;", "block", 80, 150, 30, new() { {"width", 150}, {"height", 180}, {"itemHeight", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;width:150px;height:max-content;"),
        new("height-fit-content", "max", "width:150px;height:fit-content;", "height:50%;", "block", 80, 150, 30, new() { {"width", 150}, {"height", 180}, {"itemHeight", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;width:150px;height:fit-content;"),
        new("height-auto", "max", "width:150px;height:auto;", "height:50%;", "block", 80, 150, 30, new() { {"width", 150}, {"height", 180}, {"itemHeight", 150} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;width:150px;height:auto;"),
        new("height-definite-min", "max", "width:150px;height:100px;min-height:min-content;", "height:50%;", "block", 80, 150, 30, new() { {"height", 180}, {"itemHeight", 90} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;width:150px;height:100px;min-height:min-content;"),
        new("height-definite-min-overflow", "max", "width:150px;height:100px;min-height:min-content;", "height:200%;", "block", 80, 150, 30, new() { {"height", 180}, {"itemHeight", 360} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:block;width:max-content;width:150px;height:100px;min-height:min-content;"),
        new("row-auto", "host-row-auto", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 40}, {"itemWidth", 80}, {"itemHeight", 40}, {"percentHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:flex;align-items:flex-start;width:150px;"),
        new("row-fixed", "host-row-fixed", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 100}, {"itemWidth", 80}, {"itemHeight", 40}, {"percentHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:flex;align-items:flex-start;height:100px;width:150px;"),
        new("row-stretch-auto", "host-row-stretch-auto", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 40}, {"itemWidth", 80}, {"itemHeight", 40}, {"percentHeight", 20} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:flex;align-items:stretch;width:150px;"),
        new("row-stretch-fixed", "host-row-stretch-fixed", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 100}, {"itemWidth", 80}, {"itemHeight", 100}, {"percentHeight", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:flex;align-items:stretch;height:100px;width:150px;"),
        new("column-auto", "host-column-auto", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 40}, {"itemWidth", 80}, {"itemHeight", 40}, {"percentHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:flex;flex-direction:column;align-items:flex-start;width:150px;"),
        new("column-fixed", "host-column-fixed", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 100}, {"itemWidth", 80}, {"itemHeight", 40}, {"percentHeight", 20} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:flex;flex-direction:column;align-items:flex-start;height:100px;width:150px;"),
        new("grid-auto", "host-grid-auto", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 40}, {"itemWidth", 150}, {"itemHeight", 40}, {"percentHeight", 20} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:grid;align-items:stretch;width:150px;"),
        new("grid-fixed", "host-grid-fixed", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 100}, {"itemWidth", 150}, {"itemHeight", 100}, {"percentHeight", 50} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:grid;align-items:stretch;height:100px;width:150px;"),
        new("grid-start-auto", "host-grid-start-auto", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 40}, {"itemWidth", 150}, {"itemHeight", 40}, {"percentHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:grid;align-items:flex-start;width:150px;"),
        new("grid-start-fixed", "host-grid-start-fixed", "width:150px;", "", "block", 80, 40, 0, new() { {"width", 150}, {"height", 100}, {"itemWidth", 150}, {"itemHeight", 40}, {"percentHeight", 40} }, "width:400px;height:300px;display:flow-root;", "box-sizing:border-box;display:grid;align-items:flex-start;height:100px;width:150px;"),
    ];
}

sealed record CycleCase(string Name,string Mode,string Box,string Item,string Kind,int Leafw,int Leafh,int Tail,
    Dictionary<string,double>? Expected,string RootStyle,string BoxStyle);
sealed class CycleFixture:IDisposable
{
    public readonly CycleCase Case;
    public readonly StackPanel Root=new(),Box=new();
    public readonly FrameworkElement Item;
    public readonly Border Leaf;
    public readonly Border? Percent;
    public CycleFixture(CycleCase c)
    {
        Case=c;Root.Children.Add(Box);Leaf=new(){Width=c.Leafw,Height=c.Leafh};
        Percent=c.Mode.StartsWith("host-")?new Border{Child=Leaf}:null;
        Item=c.Kind=="image"?new Image{Source=new DrawingImage(new GeometryDrawing(Brushes.Green,null,new RectangleGeometry(new Rect(0,0,80,40))))}:new Border{Child=Percent??Leaf};
        Box.Children.Add(Item);if(c.Tail>0){var tail=new Border{Width=20,Height=c.Tail};Box.Children.Add(tail);Css.SetStyle(tail,"display:block;");}
        Css.SetStyle(Root,c.RootStyle);Css.SetStyle(Box,c.BoxStyle);Css.SetStyle(Item,"display:block;box-sizing:border-box;"+c.Item);
        if(Percent is not null)Css.SetStyle(Percent,"display:block;box-sizing:border-box;height:50%;");
    }
    public void Layout()
    {
        for(var i=0;i<4;i++)
        {
            typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);
            Root.Measure(new(400,300));Root.Arrange(new(0,0,400,300));Root.UpdateLayout();
        }
    }
    public void Dispose(){Box.Children.Clear();Root.Children.Clear();}
}
