using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInlineBlockDescendantTests
{
    private const string LongText = "原始文字在行内祖先中的块级内容前后保持来源并继续换行";

    [Fact]
    public void ABlockDescendantSplitsTheWholeOuterParagraphWithoutReparenting()
    {
        var lead=Text("外层");var before=Text("前段");var after=Text("后段");var tail=Text("尾声");var block=Block(40);
        var content=InlinePanel(before,block,after);var frame=InlineBorder(content,"padding: 0 7px; border: 1px solid green;");var root=Root(lead,frame,tail);Layout(root,180);
        Assert.Equal(180,block.ActualWidth,4);Assert.Equal(0,Bounds(block,root).X,4);
        Assert.Equal(Lines(lead,root)[0].Baseline,Lines(before,root)[0].Baseline,4);Assert.Equal(Lines(after,root)[0].Baseline,Lines(tail,root)[0].Baseline,4);
        Assert.True(Bounds(block,root).Y>=Lines(before,root)[^1].Baseline);Assert.True(Lines(after,root)[0].Baseline>Bounds(block,root).Bottom);
        Assert.Same(block,content.Children[1]);Assert.Same(content,block.VisualParent);Assert.Same(content,frame.Child);Assert.Equal(3,root.Children.Count);
        CheckPoints(root,lead,before,after,tail);
    }

    [Fact]
    public void NestedAncestorsSplitAroundBlocksWithoutPaintingAcrossTheBlock()
    {
        var before=Text("前段文字");var after=Text("后段文字");var block=Block(40);var inner=InlineBorder(InlinePanel(before,block,after),"padding: 0 3px; border: 1px solid blue;");
        var frame=InlineBorder(inner,"padding: 0 5px; border: 1px solid red;");frame.Background=Brushes.Green;var root=Root(frame);Layout(root,180);
        var center=new Point(90,Bounds(block,root).Y+20);var painted=Paint(root);
        Assert.DoesNotContain(painted.Fills,fill=>ReferenceEquals(fill.Brush,Brushes.Green)&&fill.Rect.Contains(center));
        Assert.Equal(180,block.ActualWidth,4);Assert.Equal(0,Bounds(block,root).X,4);CheckPoints(root,before,after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABlockOnlyInlineHasOnlyItsActualEmptySideBoxes(bool decorated)
    {
        var block=Block(40);var frame=InlineBorder(InlinePanel(block),decorated?"border: 1px solid green; padding: 0 5px;":"");var root=Root(frame);Layout(root,160);
        if(decorated){Assert.True(Bounds(block,root).Y>0);Assert.True(root.DesiredSize.Height>40);}
        else{Assert.Equal(0,Bounds(block,root).Y,4);Assert.Equal(40,root.DesiredSize.Height,4);}
        Assert.Equal(160,block.ActualWidth,4);
    }

    [Fact]
    public void ConsecutiveBlocksCollapseMarginsAcrossWhitespaceWithoutAnInterveningLine()
    {
        var a=Block(20,"margin-bottom: 30px;");var b=Block(25,"margin-top: 20px;");var content=InlinePanel(a,Text("   "),b);var frame=InlineBorder(content,"border: 1px solid green;");var root=Root(frame);Layout(root,180);
        Assert.Equal(Bounds(a,root).Bottom+30,Bounds(b,root).Y,4);Assert.Equal(3,content.Children.Count);
    }

    [Theory]
    [InlineData("20px","-7px",13)]
    [InlineData("-15px","-7px",-15)]
    public void AdjoiningPositiveAndNegativeMarginsUseTheNativeBlockStrut(string bottom,string top,double expected)
    {
        var a=Block(30,"margin-bottom: "+bottom+";");var b=Block(30,"margin-top: "+top+";");var root=Root(InlinePanel(a,b));Layout(root,180);
        Assert.Equal(expected,Bounds(b,root).Y-Bounds(a,root).Bottom,4);
    }

    [Fact]
    public void AnEmptyThroughBlockMergesItsMarginsWithBothNeighbours()
    {
        var a=Block(20,"margin-bottom: 12px;");var empty=Block(0,"margin: 24px 0 35px;");var b=Block(20,"margin-top: 18px;");var root=Root(InlinePanel(a,empty,b));Layout(root,180);
        Assert.Equal(35,Bounds(b,root).Y-Bounds(a,root).Bottom,4);
    }

    [Fact]
    public void UndecoratedInlineSidesPermitMarginsToEscapeANormalBlock()
    {
        var nested=Block(10,"margin: 20px 0 30px;");var normal=new StackPanel();normal.Children.Add(InlinePanel(nested));Css.SetStyle(normal,"display: block;");
        var tail=Block(10,"margin-top: 5px;");var root=Root(normal,tail);Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,180);
        Assert.Equal(20,Bounds(normal,root).Y,4);Assert.Equal(Bounds(normal,root).Y,Bounds(nested,root).Y,4);Assert.Equal(10,normal.ActualHeight,4);Assert.Equal(60,Bounds(tail,root).Y,4);
    }

    [Fact]
    public void FlowRootContainsTheMarginsOfAnUndecoratedSplitInline()
    {
        var block=Block(10,"margin: 20px 0 30px;");var root=Root(InlinePanel(block));Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,180);
        Assert.Equal(20,Bounds(block,root).Y,4);Assert.Equal(60,root.DesiredSize.Height,4);
    }

    [Fact]
    public void InlineDecorationPreventsTheNestedBlockMarginsFromEscaping()
    {
        var block=Block(10,"margin: 20px 0 30px;");var frame=InlineBorder(InlinePanel(block),"border: 1px solid green;");var normal=new StackPanel();normal.Children.Add(frame);Css.SetStyle(normal,"display: block;");var root=Root(normal);Layout(root,180);
        Assert.Equal(0,Bounds(normal,root).Y,4);Assert.True(Bounds(block,root).Y>20);Assert.True(normal.ActualHeight>60);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void ANormalNestedBlockBorrowsTheOuterFloatExclusions(string side)
    {
        var floating=Floating(side);var body=Text(LongText);var block=new Border{Child=body};Css.SetStyle(block,"display: block;");var frame=InlinePanel(block);var root=Root(floating,frame,Text("外层尾注"));Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,160);
        Assert.Equal(160,block.ActualWidth,4);NoOverlap(root,floating,body);CheckPoints(root,body);
        var original=Lines(body,root).ToArray();new CssLayoutBox(root,0).MeasureIntrinsicWidth();Layout(root,240);Layout(root,160);Assert.Equal(original,Lines(body,root));
    }

    [Fact]
    public void AFlowRootNestedBlockFitsBesideTheOuterFloat()
    {
        var floating=Floating("left");var body=Text(LongText);var block=new Border{Child=body};Css.SetStyle(block,"display: flow-root;");var root=Root(floating,InlinePanel(block));Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,160);
        Assert.Equal(40,Bounds(block,root).X,4);Assert.Equal(120,block.ActualWidth,4);NoOverlap(root,floating,body);CheckPoints(root,body);
    }

    [Fact]
    public void AFloatExportedByANestedNormalBlockAlsoExcludesTheFollowingOuterText()
    {
        var floating=Floating("left");var exporter=new Border{Child=floating};Css.SetStyle(exporter,"display: block;");var body=Text(LongText);var tail=Text("外部尾注继续");var root=Root(InlinePanel(exporter,body),tail);Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,160);
        Assert.Equal(0,exporter.ActualHeight,4);Assert.True(Lines(body,root)[0].StartX>=40);NoOverlap(root,floating,body,tail);CheckPoints(root,body,tail);Assert.True(root.DesiredSize.Height>=Bounds(floating,root).Bottom);
    }

    [Fact]
    public void ClearOnAZeroHeightNestedBlockPreservesTheFollowingMargin()
    {
        var floating=Floating("left");var block=Block(0,"clear: both; margin-bottom: 10px;");var root=Root(floating,InlinePanel(block));Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,160);
        Assert.Equal(50,Bounds(block,root).Y,4);Assert.Equal(60,root.DesiredSize.Height,4);
    }

    [Fact]
    public void AThroughOnlySplitRunPreservesAnEarlierClearanceBoundary()
    {
        var floating=Floating("left");var clear=Block(0,"clear: both; margin-bottom: 10px;");var through=Block(0,"margin-bottom: 5px;");var normal=new StackPanel();normal.Children.Add(clear);normal.Children.Add(InlinePanel(through));Css.SetStyle(normal,"display: block;");var root=Root(floating,normal);Css.SetStyle(root,"display: flow-root; font-size: 16px;");Layout(root,180);
        Assert.Equal(60,normal.ActualHeight,4);Assert.Equal(60,root.DesiredSize.Height,4);
    }

    [Fact]
    public void PercentageDimensionsAndAutoMarginsUseTheOuterBlockContainingSize()
    {
        var block=Block(0,"width: 50%; height: 50%; margin: 0 auto;");var frame=InlineBorder(InlinePanel(block),"padding: 0 9px; width: 900px; height: 900px;");var root=Root(frame);Css.SetStyle(root,"display: flow-root; height: 200px; font-size: 16px;");Layout(root,200);
        Assert.Equal(100,block.ActualWidth,4);Assert.Equal(100,block.ActualHeight,4);Assert.Equal(50,Bounds(block,root).X,4);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("flex")]
    public void NativeGridAndFlexBlockContentsKeepTheirIndependentFormatting(string mode)
    {
        var a=Text("第一列");var b=Text("第二列");var block=new StackPanel();block.Children.Add(a);block.Children.Add(b);Css.SetStyle(block,"display: "+mode+"; grid-template-columns: 1fr 1fr;");
        var before=Text("前");var after=Text("后");var root=Root(InlinePanel(before,block,after));Layout(root,200);
        Assert.Equal(200,block.ActualWidth,4);Assert.True(Bounds(block,root).Y>=Lines(before,root)[0].Baseline);Assert.True(Lines(after,root)[0].Baseline>Bounds(block,root).Bottom);CheckPoints(root,a,b);
    }

    [Fact]
    public void RelativeInlineAncestorsMoveTheirBlockDescendantsWithoutChangingFollowingFlow()
    {
        var block=Block(30);var before=Text("前段");var after=Text("后段");var frame=InlineBorder(InlinePanel(before,block,after));var tail=Block(10);var root=Root(frame,tail);Layout(root,180);
        var original=Bounds(block,root);var oldTail=Bounds(tail,root);var oldText=Lines(after,root)[0];
        Css.SetStyle(frame,"display: inline; position: relative; left: 7px; top: 5px;");Layout(root,180);
        Assert.Equal(original.X+7,Bounds(block,root).X,4);Assert.Equal(original.Y+5,Bounds(block,root).Y,4);Assert.Equal(oldTail,Bounds(tail,root));Assert.Equal(oldText.Baseline+5,Lines(after,root)[0].Baseline,4);CheckPoints(root,before,after);
    }

    [Fact]
    public void SwitchingTheDescendantBetweenBlockInlineAndNoneRestoresTheCorrectParagraphs()
    {
        var before=Text("前");var body=Text("正文");Css.SetStyle(body,"display: block;");var after=Text("后");var frame=InlinePanel(before,body,after);var root=Root(frame);Layout(root,200);
        Assert.True(Lines(body,root)[0].Baseline>Lines(before,root)[0].Baseline);
        Css.SetStyle(body,"display: inline;");Layout(root,200);Assert.Equal(Lines(before,root)[0].Baseline,Lines(body,root)[0].Baseline,4);Assert.Equal(Lines(body,root)[0].Baseline,Lines(after,root)[0].Baseline,4);
        Css.SetStyle(body,"display: none;");Layout(root,200);Assert.Equal("前后",Draw(root));
        Css.SetStyle(body,"display: block;");Layout(root,200);Assert.True(Lines(after,root)[0].Baseline>Lines(body,root)[0].Baseline);CheckPoints(root,before,body,after);
    }

    [Fact]
    public void ReparentingAndIntrinsicProbesKeepTheOriginalRichTextAndSelection()
    {
        var body=Text("");var run=new Run(LongText){FontSize=18};body.Inlines.Add(run);var block=new Border{Child=body};Css.SetStyle(block,"display: block;");var frame=InlineBorder(InlinePanel(Text("前"),block,Text("后")),"border: 1px solid green;");var root=Root(frame);Layout(root,120);
        body.Select(2,5);var selected=body.SelectedText;new CssLayoutBox(root,0).MeasureIntrinsicWidth();Layout(root,210);Layout(root,120);Assert.Same(run,Assert.Single(body.Inlines));Assert.Equal(selected,body.SelectedText);CheckPoints(root,body);
        root.Children.Remove(frame);var other=Root(Text("另一个父级"),frame);Layout(other,180);Assert.Same(body,block.Child);CheckPoints(other,body);
        run.Text="更新以后继续保留原始富文本实例";Layout(other,180);Assert.Same(run,Assert.Single(body.Inlines));Assert.Contains(run.Text,Draw(other));
    }

    [Fact]
    public void ATextBlockWithABlockPseudoCanJoinAnOuterInlineTree()
    {
        var body=Text(LongText);body.Name="body";var lead=Text("外层前段");var tail=Text("外层尾段");var root=Root(lead,InlineBorder(body),tail);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("#body::before { content: '块级标签'; display: block; height: 26px; background: #e2f0e8; } #body::after { content: '结尾'; display: inline; }"));Layout(root,160);
        var pseudo=CssGeneratedContent.Get(body)!.Before!;Assert.Equal(160,pseudo.ActualWidth,4);Assert.True(Bounds(pseudo,root).Y>Lines(lead,root)[0].Baseline);Assert.True(Lines(body,root)[0].Baseline>Bounds(pseudo,root).Bottom);
        Assert.Equal(lead.Text+"块级标签"+body.Text+"结尾"+tail.Text,Draw(root));CheckPoints(root,lead,body,tail);
    }

    [Fact]
    public void PreservedHardBreaksAndSoftHyphensDoNotCrossABlockBoundary()
    {
        var before=Text("ab\u00ad");var block=Block(15);var after=Text("甲\n乙");Css.SetStyle(after,"display: inline; white-space: pre-wrap;");var root=Root(InlinePanel(before,block,after));Layout(root,200);
        Assert.Equal("ab甲乙",Draw(root));Assert.Equal(2,Lines(after,root).Count);Assert.True(Lines(after,root)[0].Baseline>Bounds(block,root).Bottom);CheckPoints(root,before,after);
    }

    [Fact]
    public void EmptySplitMarkersPreserveNaturalCjkBreaksWithoutSplittingLatinWords()
    {
        var lead=Text("甲");var before=Text("乙丙丁戊");var block=Block(10);var root=Root(lead,InlinePanel(before,block));Layout(root,32);
        Assert.True(Lines(before,root).Count>=2);Assert.Equal(Lines(lead,root)[0].Baseline,Lines(before,root)[0].Baseline,4);Assert.All(Lines(before,root),line=>Assert.True(line.EndX<=32.01));
        lead.Text="Hel";before.Text="lo";Layout(root,1000);var whole=Lines(before,root)[0].EndX;Layout(root,whole*.75);Assert.Single(Lines(before,root));Assert.Equal(Lines(lead,root)[0].Baseline,Lines(before,root)[0].Baseline,4);
    }

    [Fact]
    public void StructuralSelectorsStillSeeTheUnsplitNativeChildren()
    {
        var before=Text("前");var block=Block(10);Css.SetClass(block,"block");var after=Text("后");var frame=InlinePanel(before,block,after);Css.SetClass(frame,"frame");var root=Root(frame);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".frame > .block:nth-child(2) { width: 90px; }"));Layout(root,180);Assert.Equal(90,block.ActualWidth,4);Assert.Equal(3,frame.Children.Count);Assert.Same(frame,block.VisualParent);
    }

    [Fact]
    public void GroupAlignmentIncludesTheTextInsideLiftedBlockDescendants()
    {
        var before=Text("前");var body=Text("最长的块级正文");var after=Text("后");var block=new Border{Child=body};Css.SetStyle(block,"display: block;");var root=Root(InlinePanel(before,block,after));Css.SetStyle(root,"display: flow-root; font-size: 16px; text-group-align: center;");Layout(root,200);
        var width=Lines(body,root)[0].EndX-Lines(body,root)[0].StartX;var expected=(200-width)/2;
        Assert.Equal(expected,Lines(before,root)[0].StartX,4);Assert.Equal(expected,Lines(body,root)[0].StartX,4);Assert.Equal(expected,Lines(after,root)[0].StartX,4);CheckPoints(root,before,body,after);
    }

    [Fact]
    public void RightToLeftBlockPlacementStillUsesTheOuterContainingBlock()
    {
        var before=Text("前段");var after=Text("后段");var block=Block(25,"width: 80px; margin-left: 20px; margin-right: 5px;");var frame=InlineBorder(InlinePanel(before,block,after),"padding: 0 7px; border: 1px solid green;");var root=Root(frame);Css.SetStyle(root,"display: flow-root; direction: rtl; font-size: 16px;");Layout(root,200);
        Assert.Equal(115,Bounds(block,root).X,4);Assert.Equal(80,block.ActualWidth,4);CheckPoints(root,before,after);
    }

    [Fact]
    public void AbsoluteChildrenBetweenBlocksDoNotCreateAnAnonymousTextLine()
    {
        var a=Block(20,"margin-bottom: 30px;");var b=Block(20,"margin-top: 20px;");var absolute=Block(8,"position: absolute; width: 8px; left: 0; top: 0;");var frame=InlinePanel(a,absolute,Text("  "),b);var root=Root(frame);Layout(root,180);
        Assert.Equal(30,Bounds(b,root).Y-Bounds(a,root).Bottom,4);Assert.Equal(8,absolute.ActualWidth,4);Assert.Same(frame,absolute.VisualParent);
    }

    [Fact]
    public void BlockDescendantsKeepTabOrderFocusAndAutomationNames()
    {
        Keyboard.Initialize();Keyboard.ClearFocus();var a=Text("第一段");var b=Text(LongText);var c=Text("第三段");foreach(var t in new[]{a,b,c}){t.Focusable=true;KeyboardNavigation.SetIsTabStop(t,true);}
        var block=new Border{Child=b};Css.SetStyle(block,"display: block;");var frame=InlinePanel(a,block,c);var window=new DisplayedTestWindow{Content=Root(frame),Width=180,Height=400,TitleBarStyle=WindowTitleBarStyle.Native};
        try{Layout(window,180);Assert.True(a.Focus());Assert.True(KeyboardNavigation.MoveFocus(a));Assert.Same(b,Keyboard.FocusedElement);Assert.Equal(b.Text,b.GetAutomationPeer()!.GetName());
            Css.SetStyle(frame,"display: inline; padding: 0 5px; border: 1px solid green;");Layout(window,180);Assert.Same(b,Keyboard.FocusedElement);Assert.True(KeyboardNavigation.MoveFocus(b));Assert.Same(c,Keyboard.FocusedElement);Assert.True(KeyboardNavigation.MoveFocus(c,reverse:true));Assert.Same(b,Keyboard.FocusedElement);
        }finally{Keyboard.ClearFocus();window.Close();}
    }

    private static Border Block(double height,string style="")
    {var block=new Border{Background=Brushes.White};Css.SetStyle(block,"display: block; height: "+height+"px; "+style);return block;}
    private static Border Floating(string side)
    {var block=new Border{Background=Brushes.Green};Css.SetStyle(block,"float: "+side+"; width: 40px; height: 50px;");return block;}
    private static Rect Bounds(FrameworkElement element,FrameworkElement root)
    {var p=element.TransformToAncestor(root);return new(p.X,p.Y,element.ActualWidth,element.ActualHeight);}
    private static void NoOverlap(FrameworkElement root,FrameworkElement floating,params TextBlock[] text)
    {var rect=Bounds(floating,root);foreach(var source in text)foreach(var line in Lines(source,root))if(line.Baseline>rect.Top&&line.Baseline-14<rect.Bottom)Assert.True(line.EndX<=rect.Left+.01||line.StartX>=rect.Right-.01);}
    private static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, FontSize = 16, Foreground = Brushes.Black, IsTextSelectionEnabled = true };
        Css.SetStyle(text, "display: inline;"); return text;
    }
    private static Border InlineBorder(UIElement child, string style = "")
    {
        var border = new Border { Child = child }; Css.SetStyle(border, "display: inline; " + style); return border;
    }
    private static StackPanel InlinePanel(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); Css.SetStyle(panel, "display: inline;"); return panel;
    }
    private static StackPanel Root(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); Css.SetStyle(panel, "display: block; font-size: 16px;"); return panel;
    }
    private static void Layout(FrameworkElement root, double width)
    {
        for (var i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width, 1000)); root.Arrange(new(0, 0, width, Math.Max(1, root.DesiredSize.Height))); root.UpdateLayout(); }
    }
    private static IReadOnlyList<(double StartX, double EndX, double Baseline)> Lines(TextBlock text, FrameworkElement root)
    {
        var offset = text.TransformToAncestor(root);
        return text.GetSourceRangeSegments(0, text.Text.Length).Select(line => (line.StartX + offset.X, line.EndX + offset.X, line.Baseline + offset.Y)).ToArray();
    }
    private static void CheckPoints(FrameworkElement root, params TextBlock[] sources)
    {
        foreach (var text in sources)
        {
            var offset = text.TransformToAncestor(root);
            foreach (var line in Lines(text, root))
            {
                if (line.EndX - line.StartX < .1) continue;
                var point = new Point((line.StartX + line.EndX) / 2, line.Baseline - 2);
                var hit = root.InputHitTest(point);
                Assert.True(ReferenceEquals(text, hit), $"Expected {text.Text}, hit {(hit as TextBlock)?.Text} at {point}; lines={string.Join(';', Lines(text, root))}");
                Assert.Same(text, root.HitTest(point)?.VisualHit);
                var local = new Point(point.X - offset.X, point.Y - offset.Y);
                Assert.Same(text, text.HitTest(local)?.VisualHit);
                var index = text.ContentStart.GetOffsetToPosition(text.GetPositionFromPoint(local, true)!);
                Assert.InRange(index, 0, text.Text.Length);
                var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text.Text);
                var start = boundaries.Last(boundary => boundary <= Math.Min(index, text.Text.Length - 1));
                var end = boundaries.FirstOrDefault(boundary => boundary > start, text.Text.Length);
                text.Select(start, end - start); Assert.Equal(text.Text.Substring(start, end - start), text.SelectedText);
            }
        }
    }
    private static Capture Paint(FrameworkElement root) { var dc = new Capture(); root.Render(dc); return dc; }
    private static string Draw(FrameworkElement root) => string.Concat(Paint(root).Text);
    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<string> Text = [];
        internal readonly List<Rect> Geometries = [];
        internal readonly List<Rect> Clips = [];
        internal readonly List<(Brush? Brush,Rect Rect)> Fills = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { Geometries.Add(geometry.Bounds); var b=geometry.Bounds; Fills.Add((brush,new Rect(b.X+Offset.X,b.Y+Offset.Y,b.Width,b.Height))); }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void Close() { }
        public override void PushClip(Geometry clip) => Clips.Add(clip.Bounds);
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
    }
}
