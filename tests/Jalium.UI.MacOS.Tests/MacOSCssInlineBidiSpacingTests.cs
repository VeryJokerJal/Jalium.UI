using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSCssInlineBidiSpacingTests
{
    public static IEnumerable<object[]> TabCases()
    {
        foreach(bool rtl in new[]{false,true})
        {
            foreach(string mode in new[]{"normal","embed","isolate","bidi-override","isolate-override","plaintext"})
                foreach(string size in new[]{"0","4","80px"})yield return[rtl,mode,"",size];
            foreach(string inner in new[]{"isolate","bidi-override"})
                foreach(string size in new[]{"0","80px"})yield return[rtl,"normal",inner,size];
        }
    }

    [Theory] [MemberData(nameof(TabCases))]
    public void TabRulerUsesTheScopedParagraph(bool rtl,string mode,string inner,string tabSize)
        =>Check(rtl,mode,inner,tabSize,false,false,null);

    [Theory]
    [InlineData(false,"normal",false)] [InlineData(true,"normal",false)]
    [InlineData(false,"bidi-override",false)] [InlineData(true,"bidi-override",false)]
    [InlineData(false,"plaintext",false)] [InlineData(true,"plaintext",false)]
    [InlineData(false,"normal",true)] [InlineData(true,"normal",true)]
    [InlineData(false,"bidi-override",true)] [InlineData(true,"bidi-override",true)]
    [InlineData(false,"plaintext",true)] [InlineData(true,"plaintext",true)]
    public void VisualAutospaceRetainsAncestorOwnership(bool rtl,string mode,bool replace)
        =>Check(rtl,mode,"",null,true,replace,null);

    [Theory]
    [InlineData(false,"normal",false)] [InlineData(true,"normal",false)]
    [InlineData(false,"bidi-override",false)] [InlineData(true,"bidi-override",false)]
    [InlineData(false,"plaintext",false)] [InlineData(true,"plaintext",false)]
    [InlineData(false,"normal",true)] [InlineData(true,"normal",true)]
    [InlineData(false,"bidi-override",true)] [InlineData(true,"bidi-override",true)]
    [InlineData(false,"plaintext",true)] [InlineData(true,"plaintext",true)]
    public void AutospaceStaysOutsideSourceGradientBoxes(bool rtl,string mode,bool replace)
        =>Check(rtl,mode,"",null,true,replace,null,gradient:true);

    [Theory]
    [InlineData(false,"0",false)] [InlineData(true,"0",false)]
    [InlineData(false,"4",false)] [InlineData(true,"4",false)]
    [InlineData(false,"80px",false)] [InlineData(true,"80px",false)]
    [InlineData(false,"0",true)] [InlineData(true,"0",true)]
    [InlineData(false,"4",true)] [InlineData(true,"4",true)]
    [InlineData(false,"80px",true)] [InlineData(true,"80px",true)]
    public void InlinePhysicalEdgesRemainInTheBlockTabRuler(bool rtl,string size,bool justify)
        =>Check(rtl,"bidi-override","",size,false,false,justify?"inter-character":null,physicalEdges:true);

    public static IEnumerable<object[]> JustificationCases()
    {
        foreach(bool rtl in new[]{false,true})foreach(string mode in new[]{"normal","bidi-override","plaintext"})
            foreach(bool tabs in new[]{false,true})foreach(string method in new[]{"inter-word","inter-character"})
                yield return[rtl,mode,tabs,method];
    }

    [Theory] [MemberData(nameof(JustificationCases))]
    public void JustificationPreservesScopedGlyphsAndTheTabAnchor(bool rtl,string mode,bool tabs,string method)
        =>Check(rtl,mode,"",tabs?"80px":null,false,false,method);

    private static void Check(bool rtl,string mode,string inner,string? tabSize,bool autospace,bool replace,string? justify,bool gradient=false,bool physicalEdges=false)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string first=autospace?"中":tabSize is not null?"אב A\t":"אב A ";
        string second=autospace?(replace?" AB文":"AB文"):justify is not null?"BC D E":"BC";
        var root=new StackPanel();var a=new TextBlock{Text=first,IsTextSelectionEnabled=true};
        var b=new TextBlock{Text=second,IsTextSelectionEnabled=true};root.Children.Add(a);root.Children.Add(b);
        string style=$"display:block;width:300px;height:160px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre;text-align:start;text-indent:11px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:{mode};color:#d83253;font-kerning:normal";
        if(autospace)style+=";text-autospace:ideograph-alpha "+(replace?"replace":"insert");
        if(justify is not null)style+=$";text-align:justify;text-align-last:justify;text-justify:{justify};font-variant-ligatures:none";
        Css.SetStyle(root,style);Css.SetStyle(a,"display:inline"+(tabSize is not null?";font-size:18px;tab-size:"+tabSize:""));
        Css.SetStyle(b,"display:inline");
        if(physicalEdges)
        {
            Css.SetStyle(a,"display:inline;font-size:18px;tab-size:"+tabSize+";padding:0 5px 0 3px;margin:0 3px 0 2px");
            Css.SetStyle(b,"display:inline;padding:0 11px 0 7px;margin:0 9px 0 4px");
        }
        if(inner.Length>0)Css.SetStyle(a,"display:inline;font-size:18px;tab-size:"+tabSize+";direction:rtl;unicode-bidi:"+inner);
        if(autospace){a.Background=new SolidColorBrush(Color.FromRgb(255,242,204));b.Background=new SolidColorBrush(Color.FromRgb(216,238,255));}
        if(gradient)
        {
            a.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
            b.Foreground=new LinearGradientBrush(Colors.Green,Colors.Purple,new(0,0),new(1,0)){Opacity=.75};
        }
        Layout(root);
        string open=mode is "bidi-override" or "isolate-override"?(rtl?"\u202E":"\u202D"):"";
        string innerOpen=inner=="isolate"?"\u2067":inner=="bidi-override"?"\u202E":"";
        string innerClose=inner=="isolate"?"\u2069":inner.Length>0?"\u202C":"";
        string text=open+innerOpen+first+innerClose+second+(open.Length>0?"\u202C":"");
        int firstNative=open.Length+innerOpen.Length,secondNative=firstNative+first.Length+innerClose.Length;
        var spans=new[]{new NativeTextParagraph.Span(text[..secondNative],a.FontFamily.GetRenderingSource(a),a.FontSize,400,0,Colors.Black),
            new NativeTextParagraph.Span(text[secondNative..],b.FontFamily.GetRenderingSource(b),b.FontSize,400,0,Colors.Black)};
        var units=Enumerable.Range(0,text.Length).Select(i=>new NativeMethods.ParagraphAdvanceUnit{
            TextPosition=(uint)i,Length=1,Flags=char.GetUnicodeCategory(text[i])==UnicodeCategory.Format?1u:0u}).ToArray();
        NativeTextParagraph? Make()=>NativeTextParagraph.TryCreateAdvanced(spans,a.FontFamily.GetRenderingSource(a),a.FontSize,
            10000000,1,root.FlowDirection,units,contextPaint:true,naturalDirection:mode=="plaintext",textOptionsOwner:a);
        using var natural=Make();Assert.NotNull(natural);Assert.Single(natural.Lines);
        bool direction=mode=="plaintext"?!autospace:rtl;
        var external=new Dictionary<int,(double Left,double Right)>();
        (double Left,double Right,bool Rtl) Cell(NativeTextParagraph paragraph,int index)
        {
            double x=paragraph.Caret(0,index,false).X,y=paragraph.Caret(0,index+1,true).X;
            return(Math.Min(x,y),Math.Max(x,y),y<x);
        }
        void Gap(int aIndex,int bIndex,double width,bool ancestor)
        {
            var ac=Cell(natural,aIndex);var bc=Cell(natural,bIndex);
            int left=ac.Left<bc.Left?aIndex:bIndex,right=left==aIndex?bIndex:aIndex;
            if(Cell(natural,left).Rtl)units[left].Leading+=(float)(width/2);else units[left].Trailing+=(float)(width/2);
            if(Cell(natural,right).Rtl)units[right].Trailing+=(float)(width/2);else units[right].Leading+=(float)(width/2);
            if(ancestor){external[left]=(0,width/2);external[right]=(width/2,0);}
        }
        if(tabSize is not null)
        {
            int tab=text.IndexOf('\t');var tabCell=Cell(natural,tab);
            using var blockFormat=TextMeasurement.CreateTextFormatFromFamilyList(context,a.FontFamily.GetRenderingSource(root),28,400,0);
            using var localFormat=TextMeasurement.CreateTextFormatFromFamilyList(context,a.FontFamily.GetRenderingSource(a),(float)a.FontSize,400,0);
            Assert.NotNull(blockFormat);Assert.NotNull(localFormat);
            double step=tabSize=="0"?0:tabSize=="4"?4*blockFormat.MeasureText(" ",10000,100).WidthIncludingTrailingWhitespace:80;
            double prefix=direction?natural.Lines[0].Metrics.Line.Width-tabCell.Right:tabCell.Left;
            double physical=physicalEdges?(direction?33:24):0;
            double position=11+prefix+physical,advance=step<=0?0:(Math.Floor(position/step)+1)*step-position;
            if(step>0&&advance<localFormat.MeasureText("0",10000,100).WidthIncludingTrailingWhitespace/2)advance+=step;
            // One declared ruler stop, independent of the production planner.
            // The tab's own UAX9 direction determines which logical side grows.
            units[tab].Trailing=(float)(advance-(tabCell.Right-tabCell.Left));
        }
        if(autospace)
        {
            using var format=TextMeasurement.CreateTextFormatFromFamilyList(context,a.FontFamily.GetRenderingSource(root),28,400,0);Assert.NotNull(format);
            double gap=format.GetFontUnitMetrics().IdeographicAdvance/8;
            Assert.True(gap>0);
            if(replace)
            {
                int space=secondNative;var cell=Cell(natural,space);
                units[space].Leading=units[space].Trailing=(float)((gap-(cell.Right-cell.Left))/2);
                external[space]=(gap/2,gap/2);
            }
            else Gap(firstNative,secondNative,gap,true);
            Gap(secondNative+second.Length-2,secondNative+second.Length-1,gap,false);
        }
        using var positioned=Make();Assert.NotNull(positioned);
        if(justify is not null)
        {
            var visible=Enumerable.Range(0,text.Length).Where(i=>(units[i].Flags&1)==0)
                .Select(i=>(Index:i,Geometry:Cell(positioned,i))).OrderBy(c=>c.Geometry.Left)
                // A zero-size tab shares a caret with an adjacent glyph; the
                // natural paragraph disambiguates their visual order.
                .ThenBy(c=>Cell(natural,c.Index).Left).ToArray();
            int tab=Array.FindIndex(visible,c=>text[c.Index]=='\t');
            var boundaries=new List<(int Left,int Right)>();
            if(justify=="inter-word")
                boundaries.AddRange(visible.Where((c,i)=>text[c.Index]==' '&&i>0&&i+1<visible.Length&&
                    (tab<0||(direction?i<tab:i>tab))).Select(c=>(c.Index,c.Index)));
            else
                for(int i=1;i<visible.Length;i++)
                    if(text[visible[i-1].Index]!='\t'&&text[visible[i].Index]!='\t'&&
                        (tab<0||(direction?i<tab:i-1>tab)))boundaries.Add((visible[i-1].Index,visible[i].Index));
            Assert.NotEmpty(boundaries);
            double extra=(273-(physicalEdges?44:0)-positioned.Lines[0].Metrics.Line.Width)/boundaries.Count;Assert.True(extra>0);
            foreach(var boundary in boundaries)
            {
                if(boundary.Left==boundary.Right){units[boundary.Left].Leading+=(float)(extra/2);units[boundary.Left].Trailing+=(float)(extra/2);}
                else if(tabSize is not null)
                {
                    // Public tab caret geometry assigns the expansion to the
                    // preceding character in block reading order.
                    int index=direction?boundary.Right:boundary.Left;
                    bool nativeRtl=Cell(natural,index).Rtl;
                    if(direction?nativeRtl:!nativeRtl)units[index].Trailing+=(float)extra;
                    else units[index].Leading+=(float)extra;
                }
                else Gap(boundary.Left,boundary.Right,extra,false);
            }
        }
        using var reference=Make();Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        if(justify is not null)Assert.Equal(273-(physicalEdges?44:0),native.Metrics.Line.Width,3);
        double x=(direction?281-native.Metrics.Line.Width-(physicalEdges?44:0):19)-native.Metrics.Line.X;
        double Shift(TextBlock source)=>!physicalEdges?0:direction?(ReferenceEquals(source,a)?36:11):(ReferenceEquals(source,a)?5:24);
        double baseline=a.TransformToAncestor(root).Y+a.GetSourceRangeSegments(0,1)[0].Baseline;
        var sources=new[]{(Source:a,Start:firstNative),(Source:b,Start:secondNative)};
        var expected=Pixels(dc=>
        {
            if(autospace)
                foreach(var source in sources)
                {
                    var boxes=Enumerable.Range(0,source.Source.Text.Length).Select(i=>
                    {
                        int raw=source.Start+i;var cell=Cell(reference,raw);var trim=external.GetValueOrDefault(raw);
                        return(Left:cell.Left+trim.Left,Right:cell.Right-trim.Right);
                    }).Where(box=>box.Right>box.Left+.000001).OrderBy(box=>box.Left).ToArray();
                    if(boxes.Length>0)dc.DrawRectangle(source.Source.Background!,null,new(x+boxes.Min(box=>box.Left),8,
                        boxes.Max(box=>box.Right)-boxes.Min(box=>box.Left),40));
                }
            foreach(var source in sources)
            {
                var brush=source.Source.Foreground!;
                var paint=native.ForSource(source.Start,source.Source.Text.Length,brush is SolidColorBrush solid?solid.Color:Colors.White,brush.Opacity)
                    .WithBrushLineBox(native.Metrics.Baseline-(baseline-8),40);
                if(gradient)
                {
                    var boxes=Enumerable.Range(source.Start,source.Source.Text.Length).Select(raw=>
                    {
                        var cell=Cell(reference,raw);var trim=external.GetValueOrDefault(raw);
                        return(Left:cell.Left+trim.Left,Right:cell.Right-trim.Right);
                    }).Where(box=>box.Right>box.Left+.000001).ToArray();
                    double left=boxes.Min(box=>box.Left),right=boxes.Max(box=>box.Right);
                    paint=paint.WithBrushHorizontalBox(left,right-left);
                }
                var formatted=new FormattedText(text,source.Source.FontFamily.GetRenderingSource(source.Source),source.Source.FontSize){Foreground=brush,
                    PlatformTextLine=paint};
                formatted.ApplyTextOptionsFrom(source.Source);dc.DrawText(formatted,new(x+Shift(source.Source),baseline-native.Metrics.Baseline));
            }
        },context);
        foreach(var source in sources)
        {
            var offset=source.Source.TransformToAncestor(root);
            for(int i=0;i<source.Source.Text.Length;i++)
            {
                var cell=Cell(reference,source.Start+i);var trim=external.GetValueOrDefault(source.Start+i);
                var ranges=source.Source.GetSourceRangeSegments(i,1);
                if(cell.Right-cell.Left<.000001)continue;
                Assert.NotEmpty(ranges);
                Assert.Equal(x+Shift(source.Source)+cell.Left+trim.Left,offset.X+ranges.Min(r=>Math.Min(r.StartX,r.EndX)),3);
                Assert.Equal(x+Shift(source.Source)+cell.Right-trim.Right,offset.X+ranges.Max(r=>Math.Max(r.StartX,r.EndX)),3);
                foreach(var range in ranges.Where(range=>range.EndX-range.StartX>1))
                {
                    var hit=Assert.IsType<Documents.TextPointer>(source.Source.GetPositionFromPoint(new((range.StartX+range.EndX)/2,range.Baseline-2),false));
                    Assert.InRange(source.Source.ContentStart.GetOffsetToPosition(hit),i,i+1);
                }
            }
            source.Source.SelectAll();Assert.Equal(source.Source.Text,source.Source.SelectedText);source.Source.ClearSelection();
            Assert.Same(root,source.Source.Parent);
        }
        Assert.Equal(expected,Pixels(dc=>root.Render(dc),context));
        Assert.Equal(rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,root.FlowDirection);
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();root.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        a.Text=a.Text.Replace('א','ת').Replace('中','国');b.Text=b.Text.Replace('B','X');Layout(root);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(dc=>root.Render(dc),context));
    }

    [Theory]
    [InlineData(false,0,false)] [InlineData(true,0,false)]
    [InlineData(false,20,false)] [InlineData(true,20,false)]
    [InlineData(false,0,true)] [InlineData(true,0,true)]
    [InlineData(false,20,true)] [InlineData(true,20,true)]
    public void ConsecutiveAtomsRetainTheirBoxesAndTheFollowingAnchor(bool rtl,int width,bool tabs)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var first=new TextBlock{Text="M",IsTextSelectionEnabled=true};
        var last=new TextBlock{Text=tabs?"\tM":"M",IsTextSelectionEnabled=true};
        var a=new SpacingAtom(width);var b=new SpacingAtom(width);
        root.Children.Add(first);root.Children.Add(a);root.Children.Add(b);root.Children.Add(last);
        Css.SetStyle(root,$"display:block;width:160px;font-family:'Courier New';font-size:16px;line-height:40px;white-space:pre;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:{(tabs?"start":"justify")};text-align-last:{(tabs?"start":"justify")};text-justify:inter-character;tab-size:80px;color:#d83253;font-kerning:normal;font-variant-ligatures:none");
        foreach(var textSource in new[]{first,last})Css.SetStyle(textSource,"display:inline");
        foreach(var box in new[]{a,b})Css.SetStyle(box,$"display:inline-block;width:{width}px;height:20px");
        Layout(root);
        string family=first.FontFamily.GetRenderingSource(first);
        using var format=TextMeasurement.CreateTextFormatFromFamilyList(context,family,16,400,0);
        double m=format.MeasureText("M",10000,100).WidthIncludingTrailingWhitespace;
        double extra=tabs?0:(160-2*m-2*width)/2;
        double ax=rtl?160-m-extra-width:m+extra,bx=rtl?ax-width:ax+width;
        Assert.Equal(width,a.ActualWidth,3);Assert.Equal(width,b.ActualWidth,3);
        Assert.Equal(ax,a.TransformToAncestor(root).X,3);Assert.Equal(bx,b.TransformToAncestor(root).X,3);
        var end=Assert.Single(last.GetSourceRangeSegments(tabs?1:0,1));var offset=last.TransformToAncestor(root);
        Assert.Equal(tabs?(rtl?80:80+m):rtl?m:160,offset.X+Math.Max(end.StartX,end.EndX),3);
        string raw=(rtl?"\u202e":"\u202d")+"M\ufffc\ufffc"+last.Text+"\u202c";
        var units=Enumerable.Range(0,raw.Length).Select(i=>new NativeMethods.ParagraphAdvanceUnit{TextPosition=(uint)i,Length=1,
            Flags=char.GetUnicodeCategory(raw[i])==UnicodeCategory.Format?1u:0u}).ToArray();
        NativeTextParagraph? Make()=>NativeTextParagraph.TryCreateAdvanced([new(raw,family,16,400,0,Colors.Black)],family,16,
            10000000,1,root.FlowDirection,units,contextPaint:true,textOptionsOwner:first);
        using var natural=Make();Assert.NotNull(natural);
        double Advance(int index)=>Math.Abs(natural.Caret(0,index+1,true).X-natural.Caret(0,index,false).X);
        foreach(int index in new[]{2,3})units[index].Trailing=(float)(width-Advance(index));
        if(tabs)units[4].Trailing=(float)(80-m-2*width-Advance(4));
        else {units[1].Trailing+=(float)extra;units[3].Trailing+=(float)extra;}
        using var reference=Make();Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        double x=(rtl?160-native.Metrics.Line.Width:0)-native.Metrics.Line.X;
        double baseline=first.TransformToAncestor(root).Y+first.GetSourceRangeSegments(0,1)[0].Baseline;
        var expected=Pixels(dc=>
        {
            // Preserve author paint order at shared fractional pixel edges.
            Glyph(1);
            foreach(double left in new[]{ax,bx})if(width>0)dc.DrawRectangle(Brushes.Green,null,new(left,baseline-20,width,20));
            Glyph(raw.Length-2);
            void Glyph(int start)
            {
                var brush=(SolidColorBrush)first.Foreground!;
                var text=new FormattedText("M",family,16){Foreground=brush,PlatformTextLine=native.ForSource(start,1,brush.Color,brush.Opacity)
                    .WithBrushLineBox(native.Metrics.Baseline-baseline,40)};
                text.ApplyTextOptionsFrom(first);dc.DrawText(text,new(x,baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(dc=>root.Render(dc),context));
        foreach(var text in new[]{first,last}){text.SelectAll();Assert.Equal(text.Text,text.SelectedText);text.ClearSelection();}
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();root.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        first.Text="N";last.Text=tabs?"\tW":"W";Layout(root);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(dc=>root.Render(dc),context));
    }

    private sealed class SpacingAtom(int width):FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)=>new(width,20);
        protected override void OnRender(DrawingContext context)=>context.DrawRectangle(Brushes.Green,null,new(0,0,width,20));
    }

    [Fact]
    public void AnEmptyGeneratedTailDoesNotAddATerminalSoftBreak()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text=new TextBlock{Text="\u0301👩‍💻甲乙",IsTextSelectionEnabled=true};
        Css.SetClass(text,"tail");
        Css.SetStyle(text,"display:block;width:4px;font-size:28px;line-height:40px;white-space:normal;overflow-wrap:anywhere;direction:ltr;unicode-bidi:isolate");
        var sheet=CssStyleSheet.Parse(".tail::before{content:'e';}.tail::after{content:'';}");
        Assert.Empty(sheet.Diagnostics);Css.GetStyleSheets(text).Add(sheet);Layout(text);
        var before=CssGeneratedContent.Get(text)!.Before!;
        var first=Assert.Single(before.GetSourceRangeSegments(0,1));
        foreach(var mark in text.GetSourceRangeSegments(0,1))
            Assert.Equal(first.Baseline+before.VisualBounds.Y,mark.Baseline,3);
        var emoji=Assert.Single(text.GetSourceRangeSegments(1,5));Assert.True(emoji.EndX>emoji.StartX);
        Assert.True(Assert.Single(text.GetSourceRangeSegments(6,1)).Baseline>emoji.Baseline);
        text.SelectAll();Assert.Equal("\u0301👩‍💻甲乙",text.SelectedText);text.ClearSelection();
        Css.SetStyle(text,"display:block;width:300px;font-size:28px;line-height:40px;white-space:normal;overflow-wrap:anywhere;direction:ltr;unicode-bidi:isolate");
        Layout(text);Assert.Single(text.GetSourceRangeSegments(1,5));
    }

    private static void Layout(FrameworkElement root)
    {
        for(int i=0;i<3;i++){Dispatcher.GetForCurrentThread().ProcessQueue();root.Measure(new(300,160));root.Arrange(new(0,0,300,160));root.UpdateLayout();}
    }
    private static byte[] Pixels(Action<DrawingContext> paint,RenderContext context)
    {
        using var target=context.CreateOffscreenRenderTarget(400,180);target.RequestReadback();target.BeginDraw();target.Clear(0,0,0,0);
        using(var dc=new RenderTargetDrawingContext(target,context)){paint(dc);dc.Close();}target.EndDraw();var pixels=new byte[400*180*4];
        Assert.Equal(JaliumResult.Ok,target.FetchReadback(pixels,400*4,out _,out _));return pixels;
    }
}
