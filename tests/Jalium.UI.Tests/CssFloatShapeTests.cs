using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFloatShapeTests
{
    private const string LongText = "浮动轮廓决定文字每一行的可用空间中文段落继续环绕并保持原始文本与选择范围";

    [Theory]
    [InlineData("inset(10px)", 90)]
    [InlineData("xywh(10px 10px 60px 70px)", 70)]
    [InlineData("rect(10px 70px 80px 10px)", 70)]
    [InlineData("polygon(0 0,100% 100%,0 100%)", 40)]
    [InlineData("path('M0 0 L100 100 L0 100 Z')", 40)]
    [InlineData("shape(from 0 0,line to 100% 100%,line to 0 100%,close)", 40)]
    public void BasicShapesUseTheWholeLineStrip(string shape, double right)
    { var value = Shape(shape); Assert.Equal(right, value.Band(20, 20).Right, 4); }

    [Fact]
    public void CircleAndEllipseUseTheirContoursRatherThanTheirBoundingBoxes()
    {
        var circle = Shape("circle(50px at 50px 50px)");
        Assert.InRange(circle.Band(0, 10).Right, 79.98, 80.02);
        Assert.InRange(circle.Band(40, 20).Right, 99.98, 100);
        var ellipse = Shape("ellipse(40px 20px at 50px 50px)");
        Assert.False(ellipse.Band(0, 20).Active);
        Assert.InRange(ellipse.Band(30, 10).Right, 84.62, 84.66);
    }

    [Theory]
    [InlineData("inset(0 50%)", true, 50)]
    [InlineData("inset(0 150% 0 50%)", true, 25)]
    [InlineData("inset(50% 0)", false, 0)]
    public void DegenerateVerticalEdgesStillExcludeButHorizontalLinesDoNot(string shape, bool active, double right)
    { var band = Shape(shape).Band(20, 60); Assert.Equal(active, band.Active); if (active) Assert.Equal(right, band.Right, 4); }

    [Fact]
    public void ShapeMarginIsACircularOffsetIncludingDegeneratePointsAndLines()
    {
        var triangle = Shape("polygon(0 0,100% 100%,0 100%)", "shape-margin: 10px;");
        Assert.InRange(triangle.Band(40, 10).Right, 64.12, 64.16);
        var line = Shape("inset(50% 0)", "shape-margin: 10px;");
        Assert.False(line.Band(0, 30).Active); Assert.InRange(line.Band(40, 5).Right, 99.98, 100);
        var point = Shape("circle(0 at 50px 50px)", "shape-margin: 10px;");
        Assert.InRange(point.Band(45, 10).Right, 59.98, 60.02);
    }

    [Fact]
    public void ShapeAndMarginAreClippedAfterExpansionToTheFloatMarginBox()
    {
        var enclosing = Shape("polygon(-100% -100%,200% -100%,200% 200%,-100% 200%)");
        Assert.Equal((0d,100d,true), enclosing.Band(20, 20));
        var outside = Shape("inset(0 -100px 0 200px)", "shape-margin: 30px;");
        Assert.False(outside.Band(20, 20).Active);
        var circle = Shape("circle(80px)", "shape-margin: 30px;");
        Assert.Equal((0d,100d,true), circle.Band(0, 100));
    }

    [Fact]
    public void FilledHolesOutsideTheClippingDomainDoNotBecomeFilledRectangles()
    {
        var hole = Shape("path(evenodd,'M-100 -100 H200 V200 H-100 Z M-50 -50 H150 V150 H-50 Z')");
        Assert.False(hole.Band(0, 100).Active);
        var nonzero = Shape("path(nonzero,'M-100 -100 H200 V200 H-100 Z M-50 -50 H150 V150 H-50 Z')");
        Assert.Equal((0d,100d,true), nonzero.Band(0,100));
    }

    [Theory]
    [InlineData("inset(25%)",30,90)]
    [InlineData("inset(25%) border-box",35,85)]
    [InlineData("padding-box inset(25%)",37.5,82.5)]
    [InlineData("inset(25%) content-box",42.5,77.5)]
    public void BasicShapeDefaultsToMarginBoxAndAcceptsEitherBoxOrder(string shape,double left,double right)
    {
        var value=Shape(shape,"margin:10px;border:5px solid green;padding:10px;",containingWidth:300);
        var band=value.Band(50,10); Assert.Equal(left,band.Left,4);Assert.Equal(right,band.Right,4);
    }

    [Fact]
    public void ShapeBoxCornersIncludeNativeRadiiAndNonlinearMarginAdjustment()
    {
        var floating=new Border { CornerRadius=new CornerRadius(10) };Css.SetStyle(floating,"shape-outside:margin-box;margin:20px;");Flush();
        var value=Resolve(floating,100,100,300);var expectedRadius=10+20*(1+Math.Pow(.5-1,3));
        var right=value.Band(0,5).Right;var expected=140-expectedRadius+Math.Sqrt(expectedRadius*expectedRadius-Math.Pow(expectedRadius-5,2));
        Assert.InRange(right,expected-.03,expected+.03);
        floating.CornerRadius=default;var square=Resolve(floating,100,100,300);Assert.Equal(140,square.Band(0,5).Right,4);
    }

    [Fact]
    public void ShapeMarginPercentageUsesTheContainingBlockWidthAndMathClampsAtUse()
    {
        Assert.InRange(Shape("inset(30px)","shape-margin:10%;",containingWidth:200).Band(40,10).Right,89.98,90.02);
        Assert.Equal(70,Shape("inset(30px)","shape-margin:calc(-10px);").Band(40,10).Right,4);
        Assert.Equal(75,Shape("inset(30px)","shape-margin:calc(10% - 15px);",containingWidth:200).Band(40,10).Right,2);
    }

    [Theory]
    [InlineData("url('shape.png') border-box")]
    [InlineData("circle(-1px)")]
    [InlineData("inset(0) border-box content-box")]
    [InlineData("circle() garbage")]
    public void UnsupportedOrInvalidShapesDoNotInstallAPartialExclusion(string shape)
    { var floating=new Border();Css.SetStyle(floating,$"shape-outside:{shape};");Flush();Assert.Null(floating.GetValue(CssShapeOutsideProperties.ValueProperty)); }

    [Theory]
    [InlineData("-1px")]
    [InlineData("auto")]
    [InlineData("5deg")]
    public void InvalidShapeMarginsAreRejected(string margin)
    { var floating=new Border();Css.SetStyle(floating,$"shape-margin:{margin};");Flush();Assert.Equal(CssLayoutLength.Px(0),floating.GetValue(CssShapeOutsideProperties.MarginProperty)); }

    [Fact]
    public void AnUnbreakableWordFindsTheEarliestFitAlongANarrowingContour()
    {
        var shape=Shape("polygon(0 0,100% 0,0 100%)");var space=new CssFloatSpace(CssFloatEnvironment.Empty);
        space.Add(new(CssFloatSide.Left,0,0,100,100,shape));
        Assert.Equal(40,space.NextFit(150,0,20,90),3);
        Assert.Equal(100,space.NextFit(150,0,20,150),3);
        Assert.Equal(100,space.NextFit(150,0,20,200),3);
        Assert.Equal(100,space.Band(150,0,20).Left,4);
        Assert.Equal(100,space.ClearBottom(CssClearSide.Both),4);
    }

    [Fact]
    public void OpposingConcaveShapesFindANarrowOpeningWithoutAssumingMonotonicity()
    {
        var left=Shape("polygon(0 0,100% 0,20% 49.9%,100% 50.1%,0 100%)");
        var right=Shape("polygon(100% 0,0 0,80% 49.9%,0 50.1%,100% 100%)");
        var space=new CssFloatSpace(CssFloatEnvironment.Empty);space.Add(new(CssFloatSide.Left,0,0,100,100,left));space.Add(new(CssFloatSide.Right,100,0,100,100,right));
        var next=space.NextFit(200,0,.01,159);
        Assert.InRange(next,49.58,49.61);var band=space.Band(200,next,.01,useShapes:true);Assert.True(band.Right-band.Left>=158.999);
    }

    [Fact]
    public void ShapeRegionsKeepValueEqualityAcrossRemeasureAndCoordinateOffsets()
    {
        var a=Shape("circle(40px)","shape-margin:5px;");var b=Shape("circle(40px)","shape-margin:5px;");Assert.Equal(a,b);
        var first=new CssFloatEnvironment([new(CssFloatSide.Left,5,10,100,100,a)]);var second=new CssFloatEnvironment([new(CssFloatSide.Left,5,10,100,100,b)]);
        Assert.Equal(first,second);Assert.Equal(first.GetHashCode(),second.GetHashCode());Assert.Equal(first,first.Offset(7,11).Offset(-7,-11));
    }

    [Fact]
    public void ActualSharedTextFollowsTheTriangleAndKeepsItsSelectionAndTree()
    {
        var floating=Floating("left","polygon(0 0,100% 100%,0 100%)");var text=Text(LongText);var run=new Run(LongText);text.Text="";text.Inlines.Add(run);
        var root=Root(floating,text);Layout(root,180);text.Select(2,6);var lines=Lines(text,root);
        Assert.InRange(lines[0].StartX,19.98,20.02);Assert.True(lines.Count>1);
        foreach(var line in lines){var y=Top(text,line.Baseline);if(y<100)Assert.True(line.StartX>=Math.Min(100,y+20)-.02);}
        Assert.Same(root,text.VisualParent);Assert.Same(run,text.Inlines[0]);Assert.Equal(LongText.Substring(2,6),text.SelectedText);
        foreach(var line in lines)Assert.Same(text,root.InputHitTest(new Point((line.StartX+line.EndX)/2,line.Baseline-4)));
        Assert.Same(floating,floating.HitTest(new Point(90,5))?.VisualHit);
    }

    [Fact]
    public void TallInlineContentRechecksTheEntireShapeStrip()
    {
        var floating=Floating("left","polygon(0 0,100% 100%,0 100%)");var a=Text("甲");var b=Text("乙");b.FontSize=32;b.LineHeight=40;
        var root=Root(floating,a,b);Layout(root,220);var line=Lines(a,root)[0];Assert.True(line.StartX>=39.98);Assert.Equal(line.Baseline,Lines(b,root)[0].Baseline,4);
    }

    [Fact]
    public void FloatsIndependentBlocksAndClearUseRectanglesEvenWhenTheContourIsTiny()
    {
        var floating=Floating("left","inset(0 90% 0 0)");var next=Floating("right","circle(1px)");next.Width=80;next.Height=20;
        var block=new Border();Css.SetStyle(block,"display:flow-root;width:80px;height:20px;");var clear=new Border();Css.SetStyle(clear,"clear:both;height:5px;");
        var root=Root(floating,next,block,clear);Layout(root,150);
        Assert.Equal(100,next.VisualBounds.Y,4);Assert.True(block.VisualBounds.Y>=120-.01);Assert.True(clear.VisualBounds.Y>=120-.01);Assert.True(root.ActualHeight>=145-.01);
    }

    [Fact]
    public void BorrowedShapeExclusionsSurviveANormalBlockBoundaryAndClearStillUsesFullHeight()
    {
        var floating=Floating("left","polygon(0 0,100% 100%,0 100%)");var host=new StackPanel();Css.SetStyle(host,"display:block;");host.Children.Add(floating);
        var text=Text(LongText);var root=Root(host,text);Layout(root,180);
        Assert.Equal(0,host.ActualHeight,4);Assert.NotNull(CssDisplayLayout.FlowFor(host).ExportedFloats[0].Shape);Assert.InRange(Lines(text,root)[0].StartX,19.98,20.02);
    }

    [Fact]
    public void DynamicShapeMarginBoxRadiusAndRemovalReflowAtTheSameWidth()
    {
        var floating=Floating("left","circle(50px)");var text=Text(LongText);var root=Root(floating,text);Layout(root,180);var before=Lines(text,root)[0].StartX;
        Css.SetStyle(floating,"float:left;width:100px;height:100px;shape-outside:inset(0 30px);shape-margin:10%;");Layout(root,180);
        Assert.InRange(Lines(text,root)[0].StartX,87.98,88.02);Assert.NotEqual(before,Lines(text,root)[0].StartX);
        Css.SetStyle(floating,"float:left;width:100px;height:100px;shape-outside:border-box;");floating.CornerRadius=new(50);Layout(root,180);var rounded=Lines(text,root)[0].StartX;
        floating.CornerRadius=default;Assert.False(root.IsMeasureValid);Layout(root,180);Assert.Equal(100,Lines(text,root)[0].StartX,4);Assert.True(rounded<100);
        Css.SetStyle(floating,"float:left;width:100px;height:100px;shape-outside:none;shape-margin:50px;");Layout(root,180);Assert.Equal(100,Lines(text,root)[0].StartX,4);
        Css.SetStyle(floating,"display:block;width:100px;height:100px;shape-outside:circle(50px);");Layout(root,180);Assert.Equal(CssFloatSide.None,CssFloatProperties.ActiveSide(floating));Assert.Equal(100,text.VisualBounds.Y,4);
    }

    [Fact]
    public void ResponsivePercentageFloatAndMarginResolveAgainAfterWidthReplay()
    {
        var floating=Floating("left","polygon(0 0,100% 100%,0 100%)");Css.SetStyle(floating,"float:left;width:40%;height:100px;shape-outside:polygon(0 0,100% 100%,0 100%) border-box;shape-margin:10%;");
        var text=Text(LongText);var root=Root(floating,text);
        foreach(var width in new[]{200d,300d,200d})
        {
            Layout(root,width);var slope=width*.004;var expected=20*slope+width*.1*Math.Sqrt(1+slope*slope);
            Assert.InRange(Lines(text,root)[0].StartX,expected-.04,expected+.04);Assert.Equal(width*.4,floating.ActualWidth,4);
        }
    }

    [Fact]
    public void TextBlockAnonymousBodyAndGeneratedTailShareAShapedPseudoFloat()
    {
        var text=Text(LongText);Css.SetClass(text,"text");var sheet=CssStyleSheet.Parse(".text {display:block;} .text::before {content:'';float:left;width:100px;height:100px;shape-outside:polygon(0 0,100% 100%,0 100%);} .text::after {content:'尾注';}");
        Assert.Empty(sheet.Diagnostics);Css.GetStyleSheets(text).Add(sheet);Layout(text,180);var slots=CssGeneratedContent.Get(text)!;
        Assert.NotNull(slots.Before);
        var body=text.GetSourceRangeSegments(0,text.Text.Length);Assert.Equal(body[1].Baseline-body[0].Baseline,body[0].StartX,3);
        var tail=slots.After!.GetSourceRangeSegments(0,1)[0];var at=slots.After.TransformToAncestor(text);
        var fits=body[^1].EndX+tail.EndX-tail.StartX<=text.ActualWidth+.00001;
        var nextBaseline=text.DocumentTextBounds(text.Text.Length-1,1)[0].Bottom+
            Math.Max(CssFlowProperties.LineMetrics(text).Baseline,tail.Baseline-slots.After.DocumentTextBounds(0,1)[0].Y);
        Assert.Equal(fits?body[^1].Baseline:nextBaseline,tail.Baseline+at.Y,4);
        if(fits)Assert.Equal(body[^1].EndX,tail.StartX+at.X,4);
        text.Select(2,6);Assert.Equal(LongText.Substring(2,6),text.SelectedText);Assert.False(slots.Before!.Focusable);
    }

    [Fact]
    public void AVeryLargeShapeMarginReducesToTheClippedMarginBox()
    {
        var shape=Shape("circle(0 at 50% 50%)","shape-margin:1000000000px;");Assert.Equal((0d,100d,true),shape.Band(0,100));Assert.Equal(2,shape.Edges.Length);
    }

    [Fact]
    public void AShallowFinitePolygonDoesNotProduceNaNFromItsSlope()
    {
        var path=new PathGeometry();var figure=new PathFigure {StartPoint=new(0,0),IsClosed=true,IsFilled=true};
        figure.Segments.Add(new LineSegment(new(100,1e-310),true));figure.Segments.Add(new LineSegment(new(0,1e-310),true));path.Figures.Add(figure);
        var band=CssFloatShape.Create(path,0,0,100,100,0).Band(0,20);Assert.Equal((0d,100d,true),band);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARightFloatAfterTextUsesTheShapedSpaceAlreadyAvailableToItsLeft(bool atomic)
    {
        var first=Floating("left","inset(0 90% 0 0)");var next=Floating("right","inset(0)");next.Width=50;next.Height=20;
        FrameworkElement prefix=atomic?new Border {Width=32,Height=20}:Text("甲乙");if(atomic)Css.SetStyle(prefix,"display:inline-block;");
        var root=Root(first,prefix,next,Text("丙丁戊己"));Layout(root,150);
        Assert.Equal(0,next.VisualBounds.Y,4);Assert.Equal(100,next.VisualBounds.X,4);
    }

    [Fact]
    public void AShapedFloatAfterTextCanRemainOnItsSourceLineWhenTheTextFitsItsContour()
    {
        var prefix=Text("甲乙丙丁");var floating=Floating("left","inset(0 90% 0 0)");var root=Root(prefix,floating);Layout(root,100);
        Assert.Equal(0,floating.VisualBounds.Y,4);Assert.Equal(10,Lines(prefix,root)[0].StartX,4);
    }

    [Fact]
    public void ShapeReflowRetainsKeyboardOrderFocusAndAccessibleText()
    {
        Keyboard.Initialize();Keyboard.ClearFocus();var a=Text("第一段");var b=Text(LongText);var c=Text("第三段");
        foreach(var t in new[]{a,b,c}){t.Focusable=true;KeyboardNavigation.SetIsTabStop(t,true);}
        var floating=Floating("left","circle(50px)");var window=new DisplayedTestWindow{Content=Root(floating,a,b,c),Width=180,Height=400,TitleBarStyle=WindowTitleBarStyle.Native};
        try
        {
            Layout(window,180);Assert.True(a.Focus());Assert.True(KeyboardNavigation.MoveFocus(a));Assert.Same(b,Keyboard.FocusedElement);Assert.Equal(b.Text,b.GetAutomationPeer()!.GetName());
            Css.SetStyle(floating,"float:right;width:100px;height:100px;shape-outside:polygon(0 0,100% 100%,100% 0);shape-margin:8px;");Layout(window,180);
            Assert.Same(b,Keyboard.FocusedElement);Assert.True(KeyboardNavigation.MoveFocus(b));Assert.Same(c,Keyboard.FocusedElement);Assert.True(KeyboardNavigation.MoveFocus(c,reverse:true));Assert.Same(b,Keyboard.FocusedElement);
        }
        finally{Keyboard.ClearFocus();window.Close();}
    }

    private static CssFloatShape Shape(string shape,string extra="",double containingWidth=200)
    { var element=new Border();Css.SetStyle(element,$"shape-outside:{shape};{extra}");Flush();return Resolve(element,100,100,containingWidth); }
    private static CssFloatShape Resolve(FrameworkElement element,double width,double height,double containingWidth)
        => CssShapeOutsideProperties.Resolve(element,new(width,height),CssBoxMetrics.Margin(element,containingWidth),containingWidth)!;
    private static TextBlock Text(string value)
    { var text=new TextBlock { Text=value,FontSize=16,LineHeight=20,IsTextSelectionEnabled=true,Foreground=Brushes.Black,VerticalAlignment=VerticalAlignment.Top };Css.SetStyle(text,"display:inline;");return text; }
    private static Border Floating(string side,string shape)
    { var element=new Border { Background=Brushes.LightGreen };Css.SetStyle(element,$"float:{side};width:100px;height:100px;shape-outside:{shape};");return element; }
    private static StackPanel Root(params UIElement[] children)
    { var root=new StackPanel();foreach(var child in children)root.Children.Add(child);Css.SetStyle(root,"display:flow-root;font-size:16px;");return root; }
    private static void Flush()=>CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
    private static void Layout(FrameworkElement root,double width)
    { for(var i=0;i<5;i++){Flush();root.Measure(new(width,1200));root.Arrange(new(0,0,width,Math.Max(1,root.DesiredSize.Height)));root.UpdateLayout();} }
    private static IReadOnlyList<(double StartX,double EndX,double Baseline)> Lines(TextBlock text,FrameworkElement root)
    { var p=text.TransformToAncestor(root);return text.GetSourceRangeSegments(0,text.Text.Length==0?text.Inlines.OfType<Run>().Sum(r=>r.Text.Length):text.Text.Length).Select(l=>(l.StartX+p.X,l.EndX+p.X,l.Baseline+p.Y)).ToArray(); }
    private static double Top(TextBlock text,double baseline)=>baseline-CssFlowProperties.LineMetrics(text).Baseline;
}
