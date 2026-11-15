using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssIntrinsicSizeValueTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> GeometryCases() { foreach (var c in Cases) yield return [c.Name]; }
    [Theory] [MemberData(nameof(GeometryCases))]
    public void UsedSizesMatchSizingSpecificationAcrossFormattingContexts(string name)
    {
        using var f = new SizingFixture(Cases.Single(c => c.Name == name)); f.Layout();
        Near(f.Case.Width, f.Subject.ActualWidth);
        if (f.Case.Height is { } height) Near(height, f.Subject.ActualHeight);
        Assert.Same(f.First, f.Content.Children[0]); Assert.Same(f.Second, f.Content.Children[1]);
    }
    [Fact]
    public void ContentResizeAndNativeSourceTransitionsInvalidateIntrinsicProbes()
    {
        using var f = new SizingFixture(new("sources", "width:min-content;",80)); f.Layout(); Near(80,f.Subject.ActualWidth);
        f.Second.Width=110; f.Layout(); Near(110,f.Subject.ActualWidth);
        f.Subject.Width=double.NaN; f.Layout(); Near(300,f.Subject.ActualWidth);
        f.Subject.ClearValue(FrameworkElement.WidthProperty); f.Layout(); Near(110,f.Subject.ActualWidth);
        f.Subject.SetAnimatedValue(FrameworkElement.WidthProperty,55d,false); f.Layout(); Near(55,f.Subject.ActualWidth);
        f.Subject.ClearAnimatedValue(FrameworkElement.WidthProperty); f.Layout(); Near(110,f.Subject.ActualWidth);
        Css.SetStyle(f.Subject,f.BaseStyle+"width:max-content;");f.Layout();Near(150,f.Subject.ActualWidth);
        Css.SetStyle(f.Subject,f.BaseStyle+"width:fit-content(90px);");f.Layout();Near(110,f.Subject.ActualWidth);
        f.Second.Width=80;f.Layout();Near(90,f.Subject.ActualWidth);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void NativeEqualSentinelAndAnimatedBoundsOverrideAndRestoreKeywords(bool minimum)
    {
        var dp=minimum?FrameworkElement.MinWidthProperty:FrameworkElement.MaxWidthProperty;
        using var f=new SizingFixture(new("bounds",minimum?"width:0;min-width:min-content;":"width:250px;max-width:max-content;",minimum?80:120));
        f.Layout();Near(minimum?80:120,f.Subject.ActualWidth);
        f.Subject.SetValue(dp,minimum?0d:double.PositiveInfinity);f.Layout();Near(minimum?0:250,f.Subject.ActualWidth);
        f.Subject.ClearValue(dp);f.Layout();Near(minimum?80:120,f.Subject.ActualWidth);
        f.Subject.SetAnimatedValue(dp,minimum?100d:60d,false);f.Layout();Near(minimum?100:60,f.Subject.ActualWidth);
        f.Subject.ClearAnimatedValue(dp);f.Layout();Near(minimum?80:120,f.Subject.ActualWidth);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void FlexBasisKeywordsRespectNativeAndAnimatedOverrides(bool native)
    {
        var root=new StackPanel();Panel flex=native?new FlexPanel():new StackPanel();var item=new Border {Child=new Border {Width=80,Height=50}};
        root.Children.Add(flex);flex.Children.Add(item);Css.SetStyle(root,"display:block;width:300px;");Css.SetStyle(flex,"display:flex;align-items:flex-start;");
        Css.SetStyle(item,"width:160px;min-width:0;flex:0 0 min-content;");Layout(root);Near(80,item.ActualWidth);
        FlexPanel.SetBasis(item,double.NaN);Layout(root);Near(160,item.ActualWidth);
        item.ClearValue(FlexPanel.BasisProperty);Layout(root);Near(80,item.ActualWidth);
        item.SetAnimatedValue(FlexPanel.BasisProperty,60d,false);Layout(root);Near(60,item.ActualWidth);
        item.ClearAnimatedValue(FlexPanel.BasisProperty);Layout(root);Near(80,item.ActualWidth);
        flex.Children.Clear();root.Children.Clear();
    }
    [Fact]
    public void ExplicitInheritedFlexBasisKeepsTheComputedValueAndTracksNativeSources()
    {
        var root=new StackPanel();var flex=new StackPanel();var item=new Border {Child=new Border {Width=80,Height=30}};
        root.Children.Add(flex);flex.Children.Add(item);Css.SetStyle(root,"display:block;width:300px;");
        Css.SetStyle(flex,"display:flex;align-items:flex-start;flex-basis:min-content;");Css.SetStyle(item,"width:160px;min-width:0;flex:0 0 auto;flex-basis:inherit;");
        Layout(root);Near(80,item.ActualWidth);
        Css.SetStyle(flex,"display:flex;align-items:flex-start;flex-basis:90px;");Layout(root);Near(90,item.ActualWidth);
        Css.SetStyle(flex,"display:flex;align-items:flex-start;flex-basis:fit-content(100px);");Layout(root);Near(80,item.ActualWidth);
        FlexPanel.SetBasis(flex,110);Layout(root);Near(110,item.ActualWidth);
        FlexPanel.SetBasis(flex,double.NaN);Layout(root);Near(160,item.ActualWidth);
        flex.ClearValue(FlexPanel.BasisProperty);Layout(root);Near(80,item.ActualWidth);
        Css.SetStyle(flex,"display:flex;align-items:flex-start;flex-basis:content;");Layout(root);Near(80,item.ActualWidth);
        flex.Children.Clear();root.Children.Clear();
    }
    [Theory] [InlineData("min-content")] [InlineData("max-content")] [InlineData("fit-content")] [InlineData("stretch")] [InlineData("fit-content(calc(50% + 10px))")]
    public void VariablesSupportsAndInheritancePreserveComputedSizing(string value)
    {
        using var f=new SizingFixture(new("inherit","",0));
        Css.SetClass(f.Subject,"subject");Css.SetClass(f.Content,"content");
        Css.SetStyleSheet(f.Root,".subject{--size:"+value+";}@supports (width:"+value+"){.subject{width:var(--size);}.content{width:inherit;}}");
        f.Layout();Assert.True(double.IsFinite(f.Subject.ActualWidth));Assert.True(f.Subject.ActualWidth>=80);
        // Inherited keywords are re-evaluated against the child's own contents and containing block.
        Near(value.StartsWith("fit-content(")?80:f.Subject.ActualWidth,f.Content.ActualWidth);
    }
    [Theory] [InlineData("fit-content()")] [InlineData("fit-content(auto)")] [InlineData("fit-content(-1px)")] [InlineData("fit-content(10px 20px)")] [InlineData("min-content max-content")] [InlineData("fit-content(10)")]
    public void InvalidValuesDoNotEraseAnEarlierValidDeclaration(string value)
    {
        using var f=new SizingFixture(new("invalid","width:90px;width:"+value+";",90));f.Layout();Near(90,f.Subject.ActualWidth);
    }
    [Fact]
    public void AuthoringSupportUsesTheSameStrictSizingGrammar()
    {
        foreach(var property in new[]{"width","min-width","max-height","inline-size","flex-basis","flex"})
        {
            foreach(var value in new[]{"min-content","max-content","fit-content","stretch","fit-content(0)","fit-content(10px)","fit-content(50%)","fit-content(calc(10px - 20px))"})
                Assert.True(CssAuthoring.IsValueSupported(property,property=="flex"?"0 0 "+value:value),$"{property}: {value}");
            foreach(var value in new[]{"fit-content()","fit-content(auto)","fit-content(10)","fit-content(-1px)","fit-content(10px 20px)","fit-content(calc(2))"})
                Assert.False(CssAuthoring.IsValueSupported(property,property=="flex"?"0 0 "+value:value),$"{property}: {value}");
        }
    }
    [Theory] [InlineData("min-content")] [InlineData("max-content")] [InlineData("fit-content")] [InlineData("stretch")] [InlineData("fit-content(calc(50% + 10px))")]
    public void ContainerQueriesCompareComputedKeywordsRatherThanUsedPixels(string value)
    {
        using var f=new SizingFixture(new("query","width:"+value+";container-name:sized;",0));
        Css.SetClass(f.Content,"content");Css.SetStyleSheet(f.Root,".content{opacity:.25;}@container sized style(width:"+value+"){.content{opacity:.75;}}");
        f.Layout();Near(.75,f.Content.Opacity);
        f.Subject.Width=55;f.Layout();Near(.25,f.Content.Opacity);
        f.Subject.ClearValue(FrameworkElement.WidthProperty);f.Layout();Near(.75,f.Content.Opacity);
    }
    [Theory] [InlineData("min-content")] [InlineData("max-content")] [InlineData("fit-content")]
    public void RealTextContentCanBeMeasuredAndReflowedWithoutReplacingItsRuns(string value)
    {
        var root=new StackPanel();var text=new TextBlock {FontFamily="PingFang SC",FontSize=16,LineHeight=24};
        var run=new Jalium.UI.Documents.Run("MMMM MMMM");text.Inlines.Add(run);root.Children.Add(text);
        Css.SetStyle(root,"display:block;width:300px;");Css.SetStyle(text,"display:block;white-space:normal;width:"+value+";");
        Layout(root);Assert.InRange(text.ActualWidth,20,250);var initial=text.ActualWidth;
        run.Text="MMMM MMMM MMMM";Layout(root);Assert.True(text.ActualWidth>=initial-.06);Assert.Same(run,text.Inlines[0]);
        root.Children.Clear();
    }
    [Fact]
    public void IntrinsicSizesApplyANativeLayoutTransformOnce()
    {
        using var f=new SizingFixture(new("transform","width:min-content;",80));
        f.Subject.LayoutTransform=new ScaleTransform(2,1);f.Layout();Near(80,f.Subject.ActualWidth);Near(160,f.Subject.DesiredSize.Width);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void AProbeExceptionDoesNotLeaveAnIntrinsicOverrideActive(bool nested)
    {
        var root=new StackPanel();var content=new ThrowOnProbe();FrameworkElement item=nested?new Border{Child=content}:content;
        root.Children.Add(item);Css.SetStyle(root,"display:block;width:300px;");Css.SetStyle(item,"width:min-content;");
        Dispatcher.CurrentDispatcher.ProcessQueue();Assert.Throws<InvalidOperationException>(()=>root.Measure(new(300,200)));
        content.Throw=false;content.InvalidateMeasure();Layout(root);Near(80,item.ActualWidth);
        item.Width=50;Layout(root);Near(50,item.ActualWidth);item.ClearValue(FrameworkElement.WidthProperty);Layout(root);Near(80,item.ActualWidth);
        root.Children.Clear();
    }
    [Theory] [InlineData(Stretch.Uniform)] [InlineData(Stretch.UniformToFill)] [InlineData(Stretch.Fill)] [InlineData(Stretch.None)]
    public void AnUnboundedNativeImageRetainsItsNaturalSize(Stretch stretch)
    {
        var image=new Image{Stretch=stretch,Source=new DrawingImage(new GeometryDrawing(Brushes.Green,null,new RectangleGeometry(new Rect(0,0,80,40))))};
        image.Measure(new(double.PositiveInfinity,double.PositiveInfinity));Near(80,image.DesiredSize.Width);Near(40,image.DesiredSize.Height);
    }
    [Fact]
    public void FitContentAndPercentagePaddingTrackContainingBlockResizes()
    {
        using var f=new SizingFixture(new("resize","width:fit-content;",120));
        foreach(var width in new[]{300,50,100,300})
        {Css.SetStyle(f.Root,$"display:flow-root;width:{width}px;height:200px;");f.Layout();Near(Math.Clamp(width,80,120),f.Subject.ActualWidth);}
        Css.SetStyle(f.Subject,f.BaseStyle+"width:min-content;padding:10%;border:2px solid green;");
        f.Layout();Near(144,f.Subject.ActualWidth);
        Css.SetStyle(f.Root,"display:flow-root;width:200px;height:200px;");f.Layout();Near(124,f.Subject.ActualWidth);
    }
    [Theory] [InlineData("block")] [InlineData("grid")] [InlineData("flex")]
    public void ADefiniteStretchHeightProvidesAPercentageBasis(string kind)
    {
        using var f=new SizingFixture(new("stretch-percent","width:150px;height:stretch;",150,200,Kind:kind,Layout:"percent-block"));
        f.Layout();Near(200,f.Subject.ActualHeight);Near(200,f.Content.ActualHeight);
    }
    [Theory] [InlineData("height:min-content;")] [InlineData("height:max-content;")] [InlineData("height:fit-content;")]
    [InlineData("height:0;min-height:max-content;")] [InlineData("height:160px;max-height:min-content;")]
    public void TableIntrinsicHeightAddsAnOutsideCaptionOnce(string style)
    {
        var root=new StackPanel();var table=new StackPanel();var row=new StackPanel();var cell=new Border{Child=new Border{Width=40,Height=50}};
        var caption=new Border{Child=new Border{Width=40,Height=20}};root.Children.Add(table);table.Children.Add(caption);table.Children.Add(row);row.Children.Add(cell);
        Css.SetStyle(root,"display:block;width:300px;");Css.SetStyle(table,"display:table;width:150px;border-spacing:0;padding:0;border:0;"+style);
        Css.SetStyle(row,"display:table-row;");Css.SetStyle(cell,"display:table-cell;padding:0;border:0;");Css.SetStyle(caption,"display:table-caption;padding:0;border:0;");
        for(var i=0;i<3;i++){Layout(root);Near(70,table.ActualHeight);Near(50,cell.ActualHeight);Near(20,row.TransformToAncestor(table).Y);table.InvalidateMeasure();}
        row.Children.Clear();table.Children.Clear();root.Children.Clear();
    }
    private sealed class ThrowOnProbe:FrameworkElement
    {
        public bool Throw=true;
        protected override Size MeasureOverride(Size available) {if(Throw&&available.Width==0)throw new InvalidOperationException("probe");return new(80,20);}
    }
    private static void Layout(FrameworkElement root) {for(var i=0;i<4;i++){Dispatcher.CurrentDispatcher.ProcessQueue();root.Measure(new(300,200));root.Arrange(new(0,0,300,200));root.UpdateLayout();}}
    private static void Near(double expected,double actual)=>Assert.InRange(actual,expected-.06,expected+.06);
    internal static readonly SizingCase[] Cases =
    [
        new("flow-min-content", "width:min-content;", 80, null, 300, "flow", "block", "inline", ""),
        new("flow-max-content", "width:max-content;", 120, null, 300, "flow", "block", "inline", ""),
        new("flow-fit-content", "width:fit-content;", 100, null, 100, "flow", "block", "inline", ""),
        new("flow-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "flow", "block", "inline", ""),
        new("grid-min-content", "width:min-content;", 80, null, 300, "grid", "block", "inline", ""),
        new("grid-max-content", "width:max-content;", 120, null, 300, "grid", "block", "inline", ""),
        new("grid-fit-content", "width:fit-content;", 100, null, 100, "grid", "block", "inline", ""),
        new("grid-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "grid", "block", "inline", ""),
        new("flex-row-min-content", "width:min-content;", 80, null, 300, "flex-row", "block", "inline", ""),
        new("flex-row-max-content", "width:max-content;", 120, null, 300, "flex-row", "block", "inline", ""),
        new("flex-row-fit-content", "width:fit-content;", 100, null, 100, "flex-row", "block", "inline", ""),
        new("flex-row-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "flex-row", "block", "inline", ""),
        new("flex-column-min-content", "width:min-content;", 80, null, 300, "flex-column", "block", "inline", ""),
        new("flex-column-max-content", "width:max-content;", 120, null, 300, "flex-column", "block", "inline", ""),
        new("flex-column-fit-content", "width:fit-content;", 100, null, 100, "flex-column", "block", "inline", ""),
        new("flex-column-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "flex-column", "block", "inline", ""),
        new("float-min-content", "width:min-content;", 80, null, 300, "float", "block", "inline", ""),
        new("float-max-content", "width:max-content;", 120, null, 300, "float", "block", "inline", ""),
        new("float-fit-content", "width:fit-content;", 100, null, 100, "float", "block", "inline", ""),
        new("float-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "float", "block", "inline", ""),
        new("inline-min-content", "width:min-content;", 80, null, 300, "inline", "block", "inline", ""),
        new("inline-max-content", "width:max-content;", 120, null, 300, "inline", "block", "inline", ""),
        new("inline-fit-content", "width:fit-content;", 100, null, 100, "inline", "block", "inline", ""),
        new("inline-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "inline", "block", "inline", ""),
        new("absolute-min-content", "width:min-content;", 80, null, 300, "absolute", "block", "inline", ""),
        new("absolute-max-content", "width:max-content;", 120, null, 300, "absolute", "block", "inline", ""),
        new("absolute-fit-content", "width:fit-content;", 90, null, 100, "absolute", "block", "inline", ""),
        new("absolute-fit-content(90px)", "width:fit-content(90px);", 90, null, 300, "absolute", "block", "inline", ""),
        new("native-min", "width:min-content;", 80, null, 300, "flow", "native", "inline", ""),
        new("native-max", "width:max-content;", 120, null, 300, "flow", "native", "inline", ""),
        new("native-fit", "width:fit-content;", 100, null, 100, "flow", "native", "inline", ""),
        new("flex-min", "width:min-content;", 80, null, 300, "flow", "flex", "inline", ""),
        new("flex-max", "width:max-content;", 120, null, 300, "flow", "flex", "inline", ""),
        new("flex-fit", "width:fit-content;", 100, null, 100, "flow", "flex", "inline", ""),
        new("grid-min", "width:min-content;", 80, null, 300, "flow", "grid", "inline", ""),
        new("grid-max", "width:max-content;", 120, null, 300, "flow", "grid", "inline", ""),
        new("grid-fit", "width:fit-content;", 100, null, 100, "flow", "grid", "inline", ""),
        new("table-min", "width:min-content;", 80, null, 300, "flow", "table", "inline", ""),
        new("table-max", "width:max-content;", 120, null, 300, "flow", "table", "inline", ""),
        new("table-fit", "width:fit-content;", 100, null, 100, "flow", "table", "inline", ""),
        new("fit-below-min", "width:fit-content;", 80, null, 50, "flow", "block", "inline", ""),
        new("fit-function-below-min", "width:fit-content(10px);", 80, null, 300, "flow", "block", "inline", ""),
        new("fit-function-over-max", "width:fit-content(500px);", 120, null, 300, "flow", "block", "inline", ""),
        new("fit-function-percent", "width:fit-content(50%);", 100, null, 200, "flow", "block", "inline", ""),
        new("fit-function-calc", "width:fit-content(calc(50% + 10px));", 110, null, 200, "flow", "block", "inline", ""),
        new("fit-function-negative-math", "width:fit-content(calc(10px - 20px));", 80, null, 300, "flow", "block", "inline", ""),
        new("min-bound", "width:0px;min-width:min-content;", 80, null, 300, "flow", "block", "inline", ""),
        new("max-bound", "width:250px;max-width:max-content;", 120, null, 300, "flow", "block", "inline", ""),
        new("intrinsic-min-over-max", "width:0px;min-width:max-content;max-width:min-content;", 120, null, 300, "flow", "block", "inline", ""),
        new("logical-width", "inline-size:min-content;", 80, null, 300, "flow", "block", "inline", ""),
        new("logical-min", "width:0;min-inline-size:max-content;", 120, null, 300, "flow", "block", "inline", ""),
        new("logical-max", "width:250px;max-inline-size:min-content;", 80, null, 300, "flow", "block", "inline", ""),
        new("invalid-function", "width:90px;width:fit-content(-1px);", 90, null, 300, "flow", "block", "inline", ""),
        new("rtl-grid", "width:max-content;", 120, null, 300, "grid", "block", "inline", "direction:rtl;"),
        new("border-box-min", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:min-content;", 104, null, 300, "flow", "block", "inline", ""),
        new("border-box-max", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:max-content;", 144, null, 300, "flow", "block", "inline", ""),
        new("border-box-fit-function", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:fit-content(100px);", 104, null, 300, "flow", "block", "inline", ""),
        new("border-box-min-bound", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:0;min-width:min-content;", 104, null, 300, "flow", "block", "inline", ""),
        new("border-box-max-bound", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:300px;max-width:max-content;", 144, null, 300, "flow", "block", "inline", ""),
        new("border-box-stretch", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:stretch;margin:0 20px 0 10px;", 270, null, 300, "flow", "block", "inline", ""),
        new("border-box-stretch-floor", "box-sizing:border-box;padding:10px;border:2px solid #146342;width:stretch;margin:0 20px 0 10px;", 24, null, 10, "flow", "block", "inline", ""),
        new("content-box-min", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:min-content;", 104, null, 300, "flow", "block", "inline", ""),
        new("content-box-max", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:max-content;", 144, null, 300, "flow", "block", "inline", ""),
        new("content-box-fit-function", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:fit-content(100px);", 124, null, 300, "flow", "block", "inline", ""),
        new("content-box-min-bound", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:0;min-width:min-content;", 104, null, 300, "flow", "block", "inline", ""),
        new("content-box-max-bound", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:300px;max-width:max-content;", 144, null, 300, "flow", "block", "inline", ""),
        new("content-box-stretch", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:stretch;margin:0 20px 0 10px;", 270, null, 300, "flow", "block", "inline", ""),
        new("content-box-stretch-floor", "box-sizing:content-box;padding:10px;border:2px solid #146342;width:stretch;margin:0 20px 0 10px;", 24, null, 10, "flow", "block", "inline", ""),
        new("height-min", "width:150px;height:min-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-max", "width:150px;height:max-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-fit", "width:150px;height:fit-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-fit-function", "width:150px;height:fit-content(10px);", 150, 50, 300, "flow", "block", "block", ""),
        new("height-min-bound", "width:150px;height:0;min-height:max-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-max-bound", "width:150px;height:160px;max-height:min-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-logical", "width:150px;block-size:min-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-logical-min", "width:150px;height:0;min-block-size:max-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-logical-max", "width:150px;height:160px;max-block-size:min-content;", 150, 50, 300, "flow", "block", "block", ""),
        new("height-stretch", "width:150px;height:stretch;", 150, 200, 300, "flow", "block", "block", ""),
        new("height-padded-min-bound", "width:150px;height:0;min-height:max-content;padding:10px;border:2px solid #146342;", 150, 74, 300, "flow", "block", "block", ""),
        new("height-padded-max-bound", "width:150px;height:160px;max-height:min-content;padding:10px;border:2px solid #146342;", 150, 74, 300, "flow", "block", "block", ""),
        new("height-indefinite-percent-child", "width:150px;height:max-content;", 150, 50, 300, "flow", "block", "percent-block", ""),
        new("native-height", "width:150px;height:max-content;", 150, 50, 300, "flow", "native", "block", ""),
        new("table-height", "width:150px;height:max-content;", 150, 50, 300, "flow", "table", "block", ""),
        new("flex-height", "width:150px;height:max-content;", 150, 50, 300, "flow", "flex", "block", ""),
        new("grid-height", "width:150px;height:max-content;", 150, 50, 300, "flow", "grid", "block", ""),
        new("basis-long-min-content", "width:160px;flex:none;flex-basis:min-content;", 80, null, 300, "flex-row", "block", "inline", ""),
        new("basis-short-min-content", "width:160px;flex:0 0 min-content;", 80, null, 300, "flex-row", "block", "inline", ""),
        new("basis-long-max-content", "width:160px;flex:none;flex-basis:max-content;", 120, null, 300, "flex-row", "block", "inline", ""),
        new("basis-short-max-content", "width:160px;flex:0 0 max-content;", 120, null, 300, "flex-row", "block", "inline", ""),
        new("basis-long-fit-content", "width:160px;flex:none;flex-basis:fit-content;", 100, null, 100, "flex-row", "block", "inline", ""),
        new("basis-short-fit-content", "width:160px;flex:0 0 fit-content;", 100, null, 100, "flex-row", "block", "inline", ""),
        new("basis-long-fit-content(90px)", "width:160px;flex:none;flex-basis:fit-content(90px);", 90, null, 300, "flex-row", "block", "inline", ""),
        new("basis-short-fit-content(90px)", "width:160px;flex:0 0 fit-content(90px);", 90, null, 300, "flex-row", "block", "inline", ""),
        new("basis-long-stretch", "width:160px;flex:none;flex-basis:stretch;", 300, null, 300, "flex-row", "block", "inline", ""),
        new("basis-short-stretch", "width:160px;flex:0 0 stretch;", 300, null, 300, "flex-row", "block", "inline", ""),
        new("basis-content-box", "width:160px;box-sizing:content-box;padding:10px;border:2px solid #146342;flex:0 0 min-content;", 104, null, 300, "flex-row", "block", "inline", ""),
        new("basis-function-content-box", "width:160px;box-sizing:content-box;padding:10px;border:2px solid #146342;flex:0 0 fit-content(100px);", 124, null, 300, "flex-row", "block", "inline", ""),
        new("basis-function-percent", "width:160px;flex:0 0 fit-content(50%);", 100, null, 200, "flex-row", "block", "inline", ""),
        new("basis-function-em", "font-size:20px;width:160px;flex:0 0 fit-content(5em);", 100, null, 300, "flex-row", "block", "inline", ""),
        new("basis-invalid-order", "width:160px;flex:0 0 90px;flex:0 min-content 0;", 90, null, 300, "flex-row", "block", "inline", ""),
        new("column-basis-min-content", "width:150px;height:160px;flex:0 0 min-content;", 150, 50, 300, "flex-column", "block", "block", ""),
        new("column-basis-max-content", "width:150px;height:160px;flex:0 0 max-content;", 150, 50, 300, "flex-column", "block", "block", ""),
        new("column-basis-fit-content", "width:150px;height:160px;flex:0 0 fit-content;", 150, 50, 300, "flex-column", "block", "block", ""),
        new("column-basis-fit-content(10px)", "width:150px;height:160px;flex:0 0 fit-content(10px);", 150, 50, 300, "flex-column", "block", "block", ""),
        new("ratio-content-box-min-content", "box-sizing:content-box;width:min-content;height:40px;padding:10px;border:2px solid #146342;aspect-ratio:2;", 104, 64, 300, "flow", "block", "inline", ""),
        new("ratio-content-box-max-content", "box-sizing:content-box;width:max-content;height:40px;padding:10px;border:2px solid #146342;aspect-ratio:2;", 104, 64, 300, "flow", "block", "inline", ""),
        new("ratio-content-box-fit-content", "box-sizing:content-box;width:fit-content;height:40px;padding:10px;border:2px solid #146342;aspect-ratio:2;", 104, 64, 300, "flow", "block", "inline", ""),
        new("ratio-border-box-min-content", "box-sizing:border-box;width:min-content;height:40px;padding:10px;border:2px solid #146342;aspect-ratio:2;", 80, 40, 300, "flow", "block", "inline", ""),
        new("ratio-border-box-max-content", "box-sizing:border-box;width:max-content;height:40px;padding:10px;border:2px solid #146342;aspect-ratio:2;", 80, 40, 300, "flow", "block", "inline", ""),
        new("ratio-border-box-fit-content", "box-sizing:border-box;width:fit-content;height:40px;padding:10px;border:2px solid #146342;aspect-ratio:2;", 80, 40, 300, "flow", "block", "inline", ""),
        new("image-auto-min-content", "height:auto;width:min-content;", 80, 40, 300, "flow", "image", "inline", ""),
        new("image-auto-max-content", "height:auto;width:max-content;", 80, 40, 300, "flow", "image", "inline", ""),
        new("image-auto-fit-content", "height:auto;width:fit-content;", 80, 40, 300, "flow", "image", "inline", ""),
        new("image-20px-min-content", "height:20px;width:min-content;", 40, 20, 300, "flow", "image", "inline", ""),
        new("image-20px-max-content", "height:20px;width:max-content;", 40, 20, 300, "flow", "image", "inline", ""),
        new("image-20px-fit-content", "height:20px;width:fit-content;", 40, 20, 300, "flow", "image", "inline", ""),
    ];
}

sealed record SizingCase(string Name, string Style, double Width, double? Height = null, int Cb = 300,
    string Mode = "flow", string Kind = "block", string Layout = "inline", string Root = "");

sealed class SizingFixture : IDisposable
{
    public readonly SizingCase Case;
    public readonly StackPanel Root = new();
    public readonly FrameworkElement Subject;
    public readonly StackPanel Content = new();
    public readonly Border First = new() { Width = 40, Height = 20 };
    public readonly Border Second;
    public readonly string BaseStyle;
    public SizingFixture(SizingCase c)
    {
        Case = c; Second = new() { Width = 80, Height = c.Layout == "inline" ? 20 : 30 };
        Content.Children.Add(First); Content.Children.Add(Second);
        Css.SetStyle(Content, "display:block;font-size:1px;line-height:0;" + (c.Layout == "percent-block" ? "height:100%;" : ""));
        foreach (var part in new[] { First, Second }) Css.SetStyle(part,
            "display:" + (c.Layout == "inline" ? "inline-block" : "block") + ";vertical-align:top;");
        Subject = c.Kind == "image" ? new Image {Source = new Jalium.UI.Media.DrawingImage(new Jalium.UI.Media.GeometryDrawing(Jalium.UI.Media.Brushes.Green,null,new Jalium.UI.Media.RectangleGeometry(new Rect(0,0,80,40))))} : c.Kind == "native" ? new Border { Child = Content } : new StackPanel();
        if (Subject is StackPanel panel)
        {
            if (c.Kind == "table")
            {
                var row = new StackPanel(); var cell = new Border { Child = Content };
                row.Children.Add(cell); panel.Children.Add(row);
                Css.SetStyle(row, "display:table-row;"); Css.SetStyle(cell, "display:table-cell;padding:0;border:0;");
            }
            else panel.Children.Add(Content);
        }
        Root.Children.Add(Subject);
        var rootDisplay = c.Mode == "grid" ? "display:grid;grid-template-columns:1fr;align-items:start;" :
            c.Mode.StartsWith("flex-") ? "display:flex;align-items:flex-start;flex-direction:" + (c.Mode == "flex-column" ? "column" : "row") + ";" : "display:flow-root;";
        Css.SetStyle(Root, $"width:{c.Cb}px;height:200px;position:relative;{rootDisplay}{c.Root}");
        BaseStyle = (c.Kind == "native" ? "" : "display:" + (c.Kind is "flex" or "grid" or "table" ? c.Kind : "block") + ";") +
            "box-sizing:border-box;min-width:0;min-height:0;" + (c.Kind == "table" ? "border-spacing:0;" : "") +
            (c.Mode.StartsWith("flex-") ? "flex:none;" : "") + (c.Mode == "float" ? "float:left;" : "") +
            (c.Mode == "inline" ? "display:inline-block;" : "") + (c.Mode == "absolute" ? "position:absolute;left:10px;top:8px;" : "");
        Css.SetStyle(Subject, BaseStyle + c.Style);
    }
    public void Layout()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null);
            Root.Measure(new(Case.Cb, 200)); Root.Arrange(new(0, 0, Case.Cb, 200)); Root.UpdateLayout();
        }
    }
    public void Dispose() { Root.Children.Clear(); Content.Children.Clear(); }
}
