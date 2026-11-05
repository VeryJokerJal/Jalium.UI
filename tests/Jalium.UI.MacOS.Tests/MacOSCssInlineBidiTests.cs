using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSCssInlineBidiTests
{
    public static IEnumerable<object[]> SharedBidiCases()
    {
        foreach(bool rtl in new[]{false,true})
        {
            foreach(string mode in new[]{"normal","embed","isolate","bidi-override","isolate-override","plaintext"})
                foreach(string scene in new[]{"plain","mixed"})yield return [rtl,mode,scene];
            foreach(string scene in new[]{"nested-override","nested-isolate","nested-plaintext","tracking","fonts","gradient"})
                yield return [rtl,"normal",scene];
        }
    }

    [Theory] [MemberData(nameof(SharedBidiCases))]
    public void SharedSourcesMatchIndependentParagraphControls(bool rtl,string mode,string scene)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string first=scene is "mixed" or "gradient-mixed"?"אב AB":scene is "nested-isolate" or "nested-plaintext"?"AA אב":"AB";
        string second=scene is "nested-isolate" or "nested-plaintext"?" 12":"CD";
        var root=new StackPanel();var a=new TextBlock{Text=first,IsTextSelectionEnabled=true};var b=new TextBlock{Text=second,IsTextSelectionEnabled=true};
        root.Children.Add(a);root.Children.Add(b);
        string style=$"display:block;width:300px;height:160px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:nowrap;text-align:start;direction:{(rtl?"rtl":"ltr")};unicode-bidi:{mode};color:#d83253;font-kerning:normal";
        if(scene=="tracking")style+=";letter-spacing:2px";
        Css.SetStyle(root,style);Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");
        string innerOpen=scene=="nested-override"?"\u202E":scene=="nested-isolate"?"\u2067":scene=="nested-plaintext"?"\u2068":"";
        string innerClose=scene=="nested-override"?"\u202C":innerOpen.Length>0?"\u2069":"";
        if(innerOpen.Length>0)Css.SetStyle(a,"display:inline;direction:rtl;unicode-bidi:"+(scene=="nested-override"?"bidi-override":scene[7..]));
        if(scene=="fonts")Css.SetStyle(b,"display:inline;font-family:DejaVu Serif;font-size:32px");
        if(scene is "gradient" or "gradient-mixed")
        {
            a.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
            b.Foreground=new LinearGradientBrush(Colors.Green,Colors.Purple,new(0,0),new(1,0)){Opacity=.75};
        }
        Layout(root);
        string open=mode is "bidi-override" or "isolate-override"?(rtl?"\u202E":"\u202D"):"",close=open.Length>0?"\u202C":"";
        string text=open+innerOpen+first+innerClose+second+close;
        int firstNative=open.Length+innerOpen.Length,secondNative=firstNative+first.Length+innerClose.Length;
        string familyA=a.FontFamily.GetRenderingSource(a),familyB=b.FontFamily.GetRenderingSource(b);
        var spans=new[]{new NativeTextParagraph.Span(text[..secondNative],familyA,a.FontSize,400,0,Colors.Black),
            new NativeTextParagraph.Span(text[secondNative..],familyB,b.FontSize,400,0,Colors.Black)};
        using var reference=scene=="tracking"
            ?NativeTextParagraph.TryCreateAdvanced(spans,familyA,28,10000000,1,root.FlowDirection,new CssInputTextSpacing(2,0).Units(text),contextPaint:true,naturalDirection:mode=="plaintext",textOptionsOwner:a)
            :NativeTextParagraph.TryCreate(spans,familyA,28,10000000,1,TextAlignment.Left,root.FlowDirection,naturalDirection:mode=="plaintext",noWrap:true,textOptionsOwner:a);
        Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        bool direction=mode=="plaintext"?scene=="mixed":rtl;double x=direction?292-native.Metrics.Line.Width:8;
        var firstSegments=a.GetSourceRangeSegments(0,first.Length);Assert.NotEmpty(firstSegments);
        double baseline=a.TransformToAncestor(root).Y+firstSegments[0].Baseline;
        var expected=Pixels(dc=>
        {
            foreach(var (source,start,length)in new[]{(a,firstNative,first.Length),(b,secondNative,second.Length)})
            {
                var brush=source.Foreground!;
                dc.DrawText(Options(new FormattedText(text,source.FontFamily.GetRenderingSource(source),source.FontSize){Foreground=brush,
                    PlatformTextLine=native.ForSource(start,length,brush is SolidColorBrush solid?solid.Color:Colors.White,brush.Opacity)
                        .WithBrushLineBox(native.Metrics.Baseline-(baseline-8),40)},source),
                    new(x-native.Metrics.Line.X,baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(root,context));
        foreach(var (source,start)in new[]{(a,firstNative),(b,secondNative)})
        {
            var transform=source.TransformToAncestor(root);
            for(int index=0;index<source.Text.Length;index++)
            {
                var bounds=source.GetSourceRangeSegments(index,1);var rects=reference.Selection(0,start+index,1);
                Assert.NotEmpty(bounds);Assert.NotEmpty(rects);
                Assert.Equal(x+rects.Min(r=>r.X)-native.Metrics.Line.X,transform.X+bounds.Min(r=>Math.Min(r.StartX,r.EndX)),3);
                foreach(var range in bounds.Where(range=>Math.Abs(range.EndX-range.StartX)>1))
                {
                    var hit=Assert.IsType<Documents.TextPointer>(source.GetPositionFromPoint(new((range.StartX+range.EndX)/2,range.Baseline-2),false));
                    Assert.InRange(source.ContentStart.GetOffsetToPosition(hit),index,index+1);
                }
            }
            source.SelectAll();Assert.Equal(source.Text,source.SelectedText);source.ClearSelection();
            var row=Assert.Single(source.GetDocumentLines(300));Assert.Equal(0,row.Start);Assert.Equal(source.Text.Length,row.End);
        }
        Assert.Same(root,a.Parent);Assert.Same(root,b.Parent);Assert.Equal(first,a.Text);Assert.Equal(second,b.Text);
        Assert.Equal(rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,root.FlowDirection);
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();root.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        a.Text=first.Replace('A','Z').Replace('א','ת');b.Text=second.Replace('C','Y');Layout(root);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(root,context));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SplitGradientSourcesRetainOneOriginalBrushSpace(bool rtl)
        =>SharedSourcesMatchIndependentParagraphControls(rtl,"normal","gradient-mixed");

    [Theory]
    [InlineData(false,false,false)] [InlineData(false,true,false)]
    [InlineData(true,false,false)] [InlineData(true,true,false)]
    [InlineData(false,false,true)] [InlineData(false,true,true)]
    [InlineData(true,false,true)] [InlineData(true,true,true)]
    public void NestedFramesFollowVisualSourceGroups(bool rtl,bool clone,bool nested)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text="אב AB",IsTextSelectionEnabled=true};
        var b=new TextBlock{Text=" CD",IsTextSelectionEnabled=true};var frame=new Border{Child=a};
        Border? outer=nested?new Border{Child=frame}:null;root.Children.Add(outer??frame);root.Children.Add(b);
        Css.SetStyle(root,$"display:block;width:300px;height:160px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre;direction:{(rtl?"rtl":"ltr")};font-kerning:normal;color:black");
        Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");
        string box=$"display:inline;padding-left:5px;padding-right:9px;border-left:1px solid green;border-right:2px solid red;box-decoration-break:{(clone?"clone":"slice")};background:#fff2cc";
        Css.SetStyle(frame,box);if(outer is not null)Css.SetStyle(outer,box);
        Layout(root);
        using var reference=Paragraph(a.Text+b.Text,a,rtl);Assert.NotNull(reference);
        var native=Assert.Single(reference.Lines);int depth=nested?2:1;
        var expectedCells=new List<(TextBlock Source,int Index,double Left,double Right)>();
        foreach(var (source,start)in new[]{(a,0),(b,a.Text.Length)})for(int i=0;i<source.Text.Length;i++)
        {
            var selection=reference.Selection(0,start+i,1);Assert.NotEmpty(selection);
            expectedCells.Add((source,i,selection.Min(r=>r.X),selection.Max(r=>r.X+r.Width)));
        }
        expectedCells=expectedCells.OrderBy(c=>c.Left).ToList();
        var groups=new List<(int First,int Last)>();
        for(int i=0;i<expectedCells.Count;)
        {
            if(!ReferenceEquals(expectedCells[i].Source,a)){i++;continue;}int first=i++;
            while(i<expectedCells.Count&&ReferenceEquals(expectedCells[i].Source,a))i++;
            groups.Add((first,i-1));
        }
        Assert.Equal(rtl?2:1,groups.Count);
        double total=native.Metrics.Line.Width+depth*(clone?17*groups.Count:17),x=rtl?292-total:8,advance=0;
        var expectedBoxes=new List<Rect>();var paintCells=new List<(int Raw,double X)>();
        for(int i=0;i<expectedCells.Count;i++)
        {
            var cell=expectedCells[i];int group=groups.FindIndex(g=>g.First<=i&&g.Last>=i);
            bool left=group>=0&&i==groups[group].First&&(clone||group==0);
            bool right=group>=0&&i==groups[group].Last&&(clone||group==groups.Count-1);
            double before=left?6*depth:0,after=right?11*depth:0;
            double expectedLeft=x+cell.Left+advance+before;
            paintCells.Add((ReferenceEquals(cell.Source,a)?cell.Index:a.Text.Length+cell.Index,expectedLeft-cell.Left));
            var offset=cell.Source.TransformToAncestor(root);var actual=cell.Source.GetSourceRangeSegments(cell.Index,1);
            Assert.NotEmpty(actual);Assert.Equal(expectedLeft,offset.X+actual.Min(r=>Math.Min(r.StartX,r.EndX)),3);
            if(group>=0&&i==groups[group].First)expectedBoxes.Add(new(x+cell.Left+advance,8,0,40));
            advance+=before+after;
            if(group>=0&&i==groups[group].Last)
            {
                var old=expectedBoxes[^1];expectedBoxes[^1]=new(old.X,8,x+cell.Right+advance-old.X,40);
            }
            var segment=actual.First(r=>Math.Abs(r.EndX-r.StartX)>1);
            Assert.Same(cell.Source,root.InputHitTest(new(offset.X+(segment.StartX+segment.EndX)/2,offset.Y+segment.Baseline-2)));
        }
        var allocation=Assert.IsType<CssInlineBoxAllocation>((outer??frame).CssInlineBox);
        var frameOffset=(outer??frame).TransformToAncestor(root);
        var fragments=allocation.Fragments.OrderBy(f=>f.Rect.X).ToArray();Assert.Equal(groups.Count,fragments.Length);
        for(int i=0;i<fragments.Length;i++)
        {
            Assert.Equal(expectedBoxes[i].X,frameOffset.X+fragments[i].Rect.X,3);
            Assert.Equal(expectedBoxes[i].Width,fragments[i].Rect.Width,3);
            Assert.Equal(clone||i==0,fragments[i].LeftEdge);Assert.Equal(clone||i==fragments.Length-1,fragments[i].RightEdge);
        }
        double baseline=a.TransformToAncestor(root).Y+a.GetSourceRangeSegments(0,1)[0].Baseline;
        var expected=Pixels(dc=>
        {
            for(int level=0;level<depth;level++)Boxes(level,true,false);
            foreach(var cell in paintCells)
                dc.DrawText(Options(new FormattedText(a.Text+b.Text,a.FontFamily.GetRenderingSource(a),28){Foreground=Brushes.Black,
                    PlatformTextLine=native.ForSource(cell.Raw,1,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-(baseline-8),40)},a),
                    new(cell.X,baseline-native.Metrics.Baseline));
            // Border paints its stroke after its children, including the
            // antialias fringe of a nested background at a fractional edge.
            for(int level=depth-1;level>=0;level--)Boxes(level,false,true);
            void Boxes(int level,bool background,bool stroke)
            {
                for(int i=0;i<expectedBoxes.Count;i++)
                {
                    bool left=clone||i==0,right=clone||i==expectedBoxes.Count-1;var rect=expectedBoxes[i];
                    double insetLeft=left?6*level:0,insetRight=right?11*level:0;
                    ReferenceBox(dc,new(rect.X+insetLeft,rect.Y,rect.Width-insetLeft-insetRight,rect.Height),left,right,background,stroke);
                }
            }
        },context);
        Assert.Equal(expected,Pixels(root,context));
        Assert.Same(a,frame.Child);if(outer is not null)Assert.Same(frame,outer.Child);
        foreach(var source in new[]{a,b}){source.SelectAll();Assert.Equal(source.Text,source.SelectedText);source.ClearSelection();}
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RightToLeftCjkFramesKeepSourceAndPhysicalEdges(bool clone)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var lead=new TextBlock{Text="前",IsTextSelectionEnabled=true};
        var body=new TextBlock{Text="嵌套行内容器中的原始文字继续换行以后仍然可以选择和更新",IsTextSelectionEnabled=true};
        var tail=new TextBlock{Text="后",IsTextSelectionEnabled=true};var frame=new Border{Child=body};
        root.Children.Add(lead);root.Children.Add(frame);root.Children.Add(tail);
        Css.SetStyle(root,"display:block;width:125px;font-size:16px;direction:rtl");
        foreach(var source in new[]{lead,body,tail})Css.SetStyle(source,"display:inline");
        Css.SetStyle(frame,$"display:inline;padding:0 6px;border:1px solid green;box-decoration-break:{(clone?"clone":"slice")}");
        Layout(root,125);
        var offset=body.TransformToAncestor(root);var rows=body.GetSourceRangeSegments(0,body.Text.Length);
        Assert.True(rows.Count>2);var leadOffset=lead.TransformToAncestor(root);var first=Assert.Single(lead.GetSourceRangeSegments(0,1));
        // CJK has bidi class L: a RTL paragraph retains its logical character
        // order, while text-align:start still anchors each row at the right.
        Assert.True(leadOffset.X+first.EndX<=offset.X+rows[0].StartX+.001);
        Assert.Equal(125-(clone?7:0),offset.X+rows[1].EndX,3);
        foreach(var source in new[]{lead,body,tail})
        {
            var transform=source.TransformToAncestor(root);
            foreach(var row in source.GetSourceRangeSegments(0,source.Text.Length))
                Assert.Same(source,root.InputHitTest(new(transform.X+(row.StartX+row.EndX)/2,transform.Y+row.Baseline-2)));
            source.SelectAll();Assert.Equal(source.Text,source.SelectedText);source.ClearSelection();
        }
    }

    [Theory]
    [InlineData(false,false,false)] [InlineData(true,false,false)]
    [InlineData(false,true,false)] [InlineData(true,true,false)]
    [InlineData(false,false,true)] [InlineData(true,false,true)]
    [InlineData(false,true,true)] [InlineData(true,true,true)]
    public void PhysicalEdgesStopShapingWithoutEndingTheBidiParagraph(bool rtl,bool arabic,bool nested)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text=arabic?"مر":"A"};var b=new TextBlock{Text=arabic?"حبا":"V"};
        var frame=new Border{Child=a};root.Children.Add(nested?frame:a);root.Children.Add(b);
        Css.SetStyle(root,$"display:block;width:300px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:nowrap;direction:{(rtl?"rtl":"ltr")};font-kerning:normal");
        Css.SetStyle(a,"display:inline"+(nested?"":arabic?";padding-left:6px":";padding-right:6px"));Css.SetStyle(b,"display:inline");
        if(nested)Css.SetStyle(frame,"display:inline;"+(arabic?"padding-left:6px":"padding-right:6px"));Layout(root);
        using var separate=Paragraph(a.Text,a,rtl);using var second=Paragraph(b.Text,b,rtl);
        Assert.NotNull(separate);Assert.NotNull(second);
        var bounds=a.GetSourceRangeSegments(0,a.Text.Length);
        Assert.Equal(separate.Lines[0].Metrics.Line.Width,bounds.Max(r=>r.EndX)-bounds.Min(r=>r.StartX),3);
        var boxes=new[]{a,b}.SelectMany(s=>s.GetSourceRangeSegments(0,s.Text.Length).Select(r=>(Left:s.TransformToAncestor(root).X+r.StartX,Right:s.TransformToAncestor(root).X+r.EndX))).ToArray();
        Assert.Equal(separate.Lines[0].Metrics.Line.Width+second.Lines[0].Metrics.Line.Width+6,boxes.Max(r=>r.Right)-boxes.Min(r=>r.Left),3);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AtomicInlineObjectsUseTheResolvedNeutralSlot(bool rtl)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text="אב AB",IsTextSelectionEnabled=true};var b=new TextBlock{Text=" CD",IsTextSelectionEnabled=true};var atom=new InlineAtom();
        root.Children.Add(a);root.Children.Add(atom);root.Children.Add(b);
        Css.SetStyle(root,$"display:block;width:300px;height:160px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre;direction:{(rtl?"rtl":"ltr")};color:#d83253;font-kerning:normal");
        Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");Css.SetStyle(atom,"display:inline-block;width:24px;height:24px");Layout(root);
        string text=a.Text+"\uFFFC"+b.Text;var units=new List<NativeMethods.ParagraphAdvanceUnit>();
        for(int i=0;i<text.Length;i++)units.Add(new(){TextPosition=(uint)i,Length=1,Trailing=i==a.Text.Length?24:0});
        string family=a.FontFamily.GetRenderingSource(a);
        using var reference=NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,root.FlowDirection,units.ToArray(),contextPaint:true,textOptionsOwner:a);
        Assert.NotNull(reference);var native=Assert.Single(reference.Lines);var objectRange=Assert.Single(reference.Selection(0,a.Text.Length,1));
        Assert.Equal(24,objectRange.Width,3);double x=rtl?292-native.Metrics.Line.Width:8;
        var objectOffset=atom.TransformToAncestor(root);Assert.Equal(x+objectRange.X,objectOffset.X,3);Assert.Equal(24,atom.ActualWidth,3);
        double baseline=a.TransformToAncestor(root).Y+a.GetSourceRangeSegments(0,a.Text.Length)[0].Baseline;
        var expected=Pixels(dc=>
        {
            var brush=(SolidColorBrush)a.Foreground!;
            foreach(var(start,length)in new[]{(0,a.Text.Length),(a.Text.Length+1,b.Text.Length)})
                dc.DrawText(Options(new FormattedText(text,family,28){Foreground=brush,PlatformTextLine=native.ForSource(start,length,brush.Color,1).WithBrushLineBox(native.Metrics.Baseline-(baseline-8),40)},a),new(x,baseline-native.Metrics.Baseline));
            dc.DrawRectangle(Brushes.Blue,null,new(x+objectRange.X,baseline-24,24,24));
        },context);
        Assert.Equal(expected,Pixels(root,context));Assert.Same(atom,root.InputHitTest(new(objectOffset.X+12,objectOffset.Y+12)));
        Assert.Same(root,a.Parent);Assert.Same(root,b.Parent);Assert.Same(root,atom.Parent);
    }

    private sealed class InlineAtom:FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)=>new(24,24);
        protected override void OnRender(DrawingContext context)=>context.DrawRectangle(Brushes.Blue,null,new(0,0,24,24));
    }

    public static IEnumerable<object[]> ForcedRows()
    {
        foreach(bool rtl in new[]{false,true})foreach(bool plaintext in new[]{false,true})
            foreach(string separator in new[]{"\n","\r\n","\u2028","\u2029","\u0085"})yield return[rtl,plaintext,separator];
    }

    [Fact]
    public void ExistingSharedTabRulersKeepTheirAdvanceWhenDirectionIsAuthored()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text="AB\t",IsTextSelectionEnabled=true};var b=new TextBlock{Text="CD",IsTextSelectionEnabled=true};
        root.Children.Add(a);root.Children.Add(b);
        Css.SetStyle(root,"display:block;width:300px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre;direction:ltr;tab-size:3;font-kerning:normal");
        Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");Layout(root);
        using var prefix=Paragraph("AB",a,false);using var space=Paragraph(" ",a,false);using var zero=Paragraph("0",a,false);
        Assert.NotNull(prefix);Assert.NotNull(space);Assert.NotNull(zero);
        double step=3*space.Lines[0].Metrics.Line.Width,before=prefix.Lines[0].Metrics.Line.Width;
        double tick=(Math.Floor(before/step)+1)*step;if(tick-before<zero.Lines[0].Metrics.Line.Width/2)tick+=step;
        var bounds=Assert.Single(b.GetSourceRangeSegments(0,2));Assert.Equal(8+tick,b.TransformToAncestor(root).X+bounds.StartX,3);
        foreach(var source in new[]{a,b}){source.SelectAll();Assert.Equal(source.Text,source.SelectedText);source.ClearSelection();}
    }

    [Theory]
    [InlineData(false,false,false)] [InlineData(true,false,false)]
    [InlineData(false,true,false)] [InlineData(true,true,false)]
    [InlineData(false,false,true)] [InlineData(true,false,true)]
    [InlineData(false,true,true)] [InlineData(true,true,true)]
    public void SoftRowsRetainBidiContextAcrossSourcesAndResize(bool rtl,bool plaintext,bool tracking)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text="אב AB CD",IsTextSelectionEnabled=true};
        var b=new TextBlock{Text=" EF 12 GH",IsTextSelectionEnabled=true};root.Children.Add(a);root.Children.Add(b);
        string style=$"display:block;width:126px;height:160px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:normal;direction:{(rtl?"rtl":"ltr")};unicode-bidi:{(plaintext?"plaintext":"normal")};color:#d83253;font-kerning:normal;letter-spacing:{(tracking?2:0)}px";
        Css.SetStyle(root,style);Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");Layout(root,126);
        const string referenceText="אב AB \u2028CD EF \u202812 GH";
        var units=new List<NativeMethods.ParagraphAdvanceUnit>();
        for(int i=0;i<referenceText.Length;i++)
        {
            bool ignored=referenceText[i]=='\u2028'||i+1<referenceText.Length&&referenceText[i]==' '&&referenceText[i+1]=='\u2028';
            units.Add(new(){TextPosition=(uint)i,Length=1,Spacing=tracking?2:0,Flags=ignored?1u:referenceText[i]==' '?4u:0u});
        }
        string family=a.FontFamily.GetRenderingSource(a);
        using var reference=NativeTextParagraph.TryCreateAdvanced([new(referenceText,family,28,400,0,Colors.Black)],family,28,10000000,1,
            root.FlowDirection,units.ToArray(),contextPaint:true,naturalDirection:plaintext,textOptionsOwner:a);
        Assert.NotNull(reference);Assert.Equal(3,reference.Lines.Length);Assert.All(reference.Lines,line=>Assert.InRange(line.Metrics.Line.Width,50,110));
        var brush=(SolidColorBrush)a.Foreground!;double baseline=a.TransformToAncestor(root).Y+a.GetSourceRangeSegments(0,1)[0].Baseline;
        double Left(int row)=>((plaintext||rtl)?118-reference.Lines[row].Metrics.Line.Width:8)-reference.Lines[row].Metrics.Line.X;
        var expected=Pixels(dc=>
        {
            for(int row=0;row<3;row++)
            {
                var line=reference.Lines[row];
                int first=(int)line.Metrics.Line.TextPosition,last=first+(int)line.Metrics.Line.Length;
                while(last>first&&referenceText[last-1] is '\u2028' or ' ')last--;
                dc.DrawText(Options(new FormattedText(referenceText,family,28){Foreground=brush,PlatformTextLine=line.ForSource(first,last-first,brush.Color,1)
                    .WithBrushLineBox(line.Metrics.Baseline-(baseline-8),40)},a),new(Left(row),baseline+row*40-line.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(root,context));
        foreach(var(source,start)in new[]{(a,0),(b,a.Text.Length)})
        {
            var offset=source.TransformToAncestor(root);
            for(int i=0;i<source.Text.Length;i++)
            {
                if(source.Text[i]==' ')continue;int raw=start+i,row=raw<6?0:raw<12?1:2,index=raw+(raw>=6?1:0)+(raw>=12?1:0);
                var native=reference.Selection((uint)row,index,1);var actual=source.GetSourceRangeSegments(i,1);Assert.NotEmpty(native);Assert.NotEmpty(actual);
                Assert.Equal(Left(row)+native.Min(r=>r.X),offset.X+actual.Min(r=>Math.Min(r.StartX,r.EndX)),3);
                Assert.Equal(baseline+row*40,offset.Y+actual[0].Baseline,3);
            }
            source.SelectAll();Assert.Equal(source.Text,source.SelectedText);source.ClearSelection();
        }
        Css.SetStyle(root,style.Replace("126px","280px"));Layout(root,280);
        Assert.Single(a.GetSourceRangeSegments(0,2));Assert.NotEqual(expected,Pixels(root,context));
        Css.SetStyle(root,style);Layout(root,126);Assert.Equal(expected,Pixels(root,context));
    }

    [Theory] [MemberData(nameof(ForcedRows))]
    public void ForcedRowsKeepOneSharedParagraphAndOriginalSources(bool rtl,bool plaintext,string separator)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=new TextBlock{Text="אב AB"+separator+"12 CD"+separator,IsTextSelectionEnabled=true};
        var b=new TextBlock{Text="EF אב"+separator,IsTextSelectionEnabled=true};root.Children.Add(a);root.Children.Add(b);
        Css.SetStyle(root,$"display:block;width:300px;height:160px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre;direction:{(rtl?"rtl":"ltr")};unicode-bidi:{(plaintext?"plaintext":"normal")};color:#d83253;font-kerning:normal");
        Css.SetStyle(a,"display:inline");Css.SetStyle(b,"display:inline");Layout(root);
        string text=a.Text+b.Text,family=a.FontFamily.GetRenderingSource(a);
        using var reference=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,
            TextAlignment.Left,root.FlowDirection,naturalDirection:plaintext,noWrap:true,textOptionsOwner:a);
        Assert.NotNull(reference);Assert.InRange(reference.Lines.Length,3,4);
        double baseline=a.TransformToAncestor(root).Y+a.GetSourceRangeSegments(0,1)[0].Baseline;
        var brush=(SolidColorBrush)a.Foreground!;
        double Left(int row)=>((plaintext?separator=="\u2028"||row==0:rtl)?292-reference.Lines[row].Metrics.Line.Width:8)-reference.Lines[row].Metrics.Line.X;
        var expected=Pixels(dc=>
        {
            for(int row=0;row<3;row++)
            {
                var line=reference.Lines[row];
                int first=(int)line.Metrics.Line.TextPosition,last=first+(int)line.Metrics.Line.Length;
                while(last>first&&text[last-1] is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')last--;
                dc.DrawText(Options(new FormattedText(text,family,28){Foreground=brush,PlatformTextLine=line.ForSource(first,last-first,brush.Color,1)
                    .WithBrushLineBox(line.Metrics.Baseline-(baseline-8),40)},a),new(Left(row),baseline+row*40-line.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(root,context));
        foreach(var(source,start)in new[]{(a,0),(b,a.Text.Length)})
        {
            var offset=source.TransformToAncestor(root);
            for(int i=0;i<source.Text.Length;i++)
            {
                if(char.IsWhiteSpace(source.Text[i]))continue;
                int raw=start+i,row=Array.FindIndex(reference.Lines,line=>raw>=line.Metrics.Line.TextPosition&&raw<line.Metrics.Line.TextPosition+line.Metrics.Line.Length);
                Assert.True(row>=0);var native=reference.Selection((uint)row,raw,1);var actual=source.GetSourceRangeSegments(i,1);Assert.NotEmpty(native);Assert.NotEmpty(actual);
                Assert.Equal(Left(row)+native.Min(r=>r.X),offset.X+actual.Min(r=>Math.Min(r.StartX,r.EndX)),3);
                Assert.Equal(baseline+row*40,offset.Y+actual[0].Baseline,3);
            }
            source.SelectAll();Assert.Equal(source.Text,source.SelectedText);source.ClearSelection();
        }
        var caret=Assert.Single(b.DocumentTextBounds(b.Text.Length,0));var endOffset=b.TransformToAncestor(root);
        Assert.False(caret.IsEmpty);Assert.True(caret.Height>0);
        Assert.Equal((plaintext?separator=="\u2028":rtl)?292:8,endOffset.X+caret.X,3);
        Assert.InRange(endOffset.Y+caret.Y,baseline+80,baseline+120);
    }

    private static NativeTextParagraph? Paragraph(string text,TextBlock source,bool rtl)=>NativeTextParagraph.TryCreate(
        [new(text,source.FontFamily.GetRenderingSource(source),source.FontSize,400,0,Colors.Black)],source.FontFamily.GetRenderingSource(source),source.FontSize,
        10000000,1,TextAlignment.Left,rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:source);

    private static FormattedText Options(FormattedText text,DependencyObject owner){text.ApplyTextOptionsFrom(owner);return text;}

    private static void ReferenceBox(DrawingContext context,Rect rect,bool left,bool right,bool background,bool stroke)
    {
        // An independently positioned ordinary block paints the expected
        // background and physical borders, without shared-inline allocation.
        var box=new Border();Css.SetStyle(box,FormattableString.Invariant($"display:block;box-sizing:border-box;width:{rect.Width:R}px;height:{rect.Height:R}px;{(background?"background:#fff2cc;":"")}border-radius:0;border-left:{(stroke&&left?1:0)}px solid green;border-right:{(stroke&&right?2:0)}px solid red"));
        for(int i=0;i<2;i++){Dispatcher.GetForCurrentThread().ProcessQueue();box.Measure(rect.Size);box.Arrange(new(0,0,rect.Width,rect.Height));box.UpdateLayout();}
        var translated=Assert.IsAssignableFrom<IOffsetDrawingContext>(context);var saved=translated.Offset;
        translated.Offset=new(saved.X+rect.X,saved.Y+rect.Y);try{box.Render(context);}finally{translated.Offset=saved;}
    }

    private static void Layout(FrameworkElement root,int width=300)
    {
        for(int i=0;i<3;i++){Dispatcher.GetForCurrentThread().ProcessQueue();root.Measure(new(width,160));root.Arrange(new(0,0,width,160));root.UpdateLayout();}
    }
    private static byte[] Pixels(UIElement root,RenderContext context)=>Pixels(dc=>root.Render(dc),context);
    private static byte[] Pixels(Action<DrawingContext> paint,RenderContext context)
    {
        using var target=context.CreateOffscreenRenderTarget(400,180);target.RequestReadback();target.BeginDraw();target.Clear(0,0,0,0);
        using(var dc=new RenderTargetDrawingContext(target,context)){paint(dc);dc.Close();}target.EndDraw();var pixels=new byte[400*180*4];
        Assert.Equal(JaliumResult.Ok,target.FetchReadback(pixels,400*4,out _,out _));return pixels;
    }
}
