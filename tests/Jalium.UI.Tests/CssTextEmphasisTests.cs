using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTextEmphasisTests
{
    [Theory]
    [InlineData("text-emphasis", "filled dot green")]
    [InlineData("text-emphasis", "color-mix(in srgb,currentColor 50%,red 50%) open sesame")]
    [InlineData("text-emphasis", "red")]
    [InlineData("text-emphasis", "none blue")]
    [InlineData("text-emphasis", "\"※\" currentColor")]
    [InlineData("text-emphasis-style", "dot open")]
    [InlineData("text-emphasis-style", "filled")]
    [InlineData("text-emphasis-style", "\"A\\301\"")]
    [InlineData("text-emphasis-style", "\"\"")]
    [InlineData("text-emphasis-color", "currentColor")]
    [InlineData("text-emphasis-position", "under")]
    [InlineData("text-emphasis-position", "left over")]
    [InlineData("text-emphasis-position", "under right")]
    public void AcceptsTheEmphasisGrammar(string property,string value)
        => Assert.True(Css.Supports(property,value),property+": "+value);

    [Theory]
    [InlineData("text-emphasis", "filled open")]
    [InlineData("text-emphasis", "circle dot")]
    [InlineData("text-emphasis", "red blue")]
    [InlineData("text-emphasis", "none dot")]
    [InlineData("text-emphasis", "dot under")]
    [InlineData("text-emphasis-style", "none open")]
    [InlineData("text-emphasis-style", "\"x\" filled")]
    [InlineData("text-emphasis-style", "sesame sesame")]
    [InlineData("text-emphasis-style", "3px")]
    [InlineData("text-emphasis-position", "left")]
    [InlineData("text-emphasis-position", "over under")]
    [InlineData("text-emphasis-position", "under left right")]
    [InlineData("text-emphasis-color", "auto")]
    public void RejectsDuplicateAndMisplacedComponents(string property,string value)
        => Assert.False(Css.Supports(property,value),property+": "+value);

    [Theory]
    [InlineData("filled dot","•")]
    [InlineData("open dot","◦")]
    [InlineData("filled circle","●")]
    [InlineData("open circle","○")]
    [InlineData("filled double-circle","◉")]
    [InlineData("open double-circle","◎")]
    [InlineData("filled triangle","▲")]
    [InlineData("open triangle","△")]
    [InlineData("filled sesame","﹅")]
    [InlineData("open sesame","﹆")]
    [InlineData("filled","●")]
    [InlineData("open","○")]
    [InlineData("\"A\\301\"","Á")]
    [InlineData("\"😀x\"","😀")]
    public void DrawsOneHalfSizeMarkPerTypographicCharacter(string style,string mark)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("汉字 A\u0308 \u200B😀","text-emphasis:"+style+" #146342;");
        var original=text.Text;var paint=Draw(root);var marks=paint.Text.Where(row=>row.Value==mark).ToArray();
        Assert.Equal(4,marks.Length);Assert.All(marks,row=>{Assert.Equal(12,row.Size,6);Assert.Equal(Color.FromRgb(20,99,66),row.Color);});
        Assert.Equal(original,text.Text);Assert.Equal(original,text.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void MarksReserveLineSpaceAndSwitchSidesWithoutChangingSourceOrWidth()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("AB","text-emphasis:none;");var ordinarySize=text.DesiredSize;
        Css.SetStyle(text,"text-emphasis:filled dot;text-emphasis-position:over;");Layout(root);var above=Draw(root);var aboveSize=text.DesiredSize;
        Assert.True(aboveSize.Height>ordinarySize.Height);Assert.Equal(ordinarySize.Width,aboveSize.Width,5);
        var body=above.Text.First(row=>row.Value=="AB");Assert.All(above.Text.Where(row=>row.Value=="•"),row=>Assert.True(row.Origin.Y<body.Origin.Y));
        Css.SetStyle(text,"text-emphasis:filled dot;text-emphasis-position:under left;");Layout(root);var below=Draw(root);body=below.Text.First(row=>row.Value=="AB");
        Assert.All(below.Text.Where(row=>row.Value=="•"),row=>Assert.True(row.Origin.Y>body.Origin.Y));Assert.Equal(aboveSize,text.DesiredSize);
        Css.SetStyle(text,"text-emphasis:none;");Layout(root);Assert.DoesNotContain(Draw(root).Text,row=>row.Value=="•");Assert.Equal(ordinarySize,text.DesiredSize);
    }

    [Fact]
    public void CrossRunCombiningMarksUseTheirBaseCharacterAndKeepOriginalNodes()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("","text-emphasis:dot;");var first=new Jalium.UI.Documents.Run("A");var second=new Jalium.UI.Documents.Run("\u0308B");text.Inlines.Add(first);text.Inlines.Add(second);Layout(root);
        Assert.Equal(2,Draw(root).Text.Count(row=>row.Value=="•"));Assert.Same(first,text.Inlines[0]);Assert.Same(second,text.Inlines[1]);Assert.Equal("A",first.Text);Assert.Equal("\u0308B",second.Text);
    }

    [Theory]
    [InlineData("A ,、。#%&@§¶⁋⁓〽️",10)]
    [InlineData(" \u0308A\u00A0\u202F\u200B\u0378",2)]
    public void UsesTheUnicodeSeparatorAndPunctuationRules(string source,int expected)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,_)=Scene(source,"text-emphasis:dot;");Assert.Equal(expected,Draw(root).Text.Count(row=>row.Value=="•"));
    }

    [Fact]
    public void ShorthandResetsStyleAndColorAndKeepsTheChosenPosition()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,_)=Scene("AB","text-emphasis-position:under left;text-emphasis:filled red;text-emphasis:none;text-emphasis-style:dot;");
        var paint=Draw(root);var body=paint.Text.First(row=>row.Value=="AB");var marks=paint.Text.Where(row=>row.Value=="•").ToArray();
        Assert.Equal(2,marks.Length);Assert.All(marks,row=>{Assert.Equal(body.Color,row.Color);Assert.True(row.Origin.Y>body.Origin.Y);});
    }

    [Fact]
    public void InheritedCurrentColorTracksTheChildsOwnTextAndNativeBrushChanges()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("AB","color:blue;");Css.SetStyle(root,"display:block;font-family:Georgia;font-size:24px;line-height:normal;color:red;text-emphasis:dot;");Layout(root);
        Assert.All(Draw(root).Text.Where(row=>row.Value=="•"),row=>Assert.Equal(Colors.Blue,row.Color));
        Css.SetStyle(text,"color:green;");Layout(root);Assert.All(Draw(root).Text.Where(row=>row.Value=="•"),row=>Assert.Equal(Colors.Green,row.Color));
        Assert.Equal(2,Draw(root).Text.Count(row=>row.Value=="•"));
        Css.SetStyle(text,"");var brush=new SolidColorBrush(Colors.Blue);text.Foreground=brush;Layout(root);
        Assert.All(Draw(root).Text.Where(row=>row.Value=="•"),row=>Assert.Equal(Colors.Blue,row.Color));
        brush.Color=Colors.Green;Layout(root);Assert.All(Draw(root).Text.Where(row=>row.Value=="•"),row=>Assert.Equal(Colors.Green,row.Color));
    }

    [Fact]
    public void ExplicitInheritanceRefreshesWhenTheParentSwitchesBetweenLiteralAndCurrentColor()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("AB","color:blue;text-emphasis-color:inherit;");
        const string basis="display:block;font-family:Georgia;font-size:24px;line-height:normal;color:red;";
        foreach(var color in new[]{"red","currentColor","red","currentColor"})
        {
            Css.SetStyle(root,basis+"text-emphasis:dot "+color+";");Layout(root);var marks=Draw(root).Text.Where(row=>row.Value=="•").ToArray();
            Assert.Equal(2,marks.Length);Assert.All(marks,row=>Assert.Equal(color=="red"?Colors.Red:Colors.Blue,row.Color));
        }
    }

    [Theory]
    [InlineData("::first-line","AB\nCD",2)]
    [InlineData("::first-letter","AB",1)]
    public void TypographicPseudosCanIntroduceEmphasis(string pseudo,string source,int expected)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene(source,"white-space:pre-wrap;");Css.SetClass(text,"subject");Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject"+pseudo+"{text-emphasis:dot #146342;}"));Layout(root);
        Assert.Equal(expected,Draw(root).Text.Count(row=>row.Value=="•"));Assert.Equal(source,text.Text);
    }

    [Fact]
    public void SelectedMarksUseTheSelectionForegroundWithoutChangingTheirLayout()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root,text)=Scene("AB","text-emphasis:dot green;");var size=text.DesiredSize;Css.SetClass(text,"subject");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::selection{color:blue;background:yellow;}"));Layout(root);text.SelectAll();
        var marks=Draw(root).Text.Where(row=>row.Value=="•").ToArray();Assert.Equal(2,marks.Length);Assert.All(marks,row=>Assert.Equal(Colors.Blue,row.Color));
        Assert.Equal(size,text.DesiredSize);Assert.Equal("AB",text.SelectedText);
    }

    [Fact]
    public void ZeroSizeRunsDoNotAcquireParentSizedMarks()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var(root,text)=Scene("","text-emphasis:dot;");var run=new Jalium.UI.Documents.Run("AB"){FontSize=0};text.Inlines.Add(run);Layout(root);
        Assert.DoesNotContain(Draw(root).Text,row=>row.Value=="•");Assert.Equal(0,run.FontSize);Assert.Equal("AB",run.Text);
    }

    private static (StackPanel Root,TextBlock Text) Scene(string value,string style)
    {
        var root=new StackPanel();Css.SetStyle(root,"display:block;font-family:Georgia;font-size:24px;line-height:normal;color:#233c32;");
        var text=new TextBlock{Text=value,IsTextSelectionEnabled=true};root.Children.Add(text);Css.SetStyle(text,style);Layout(root);return(root,text);
    }
    private static void Layout(FrameworkElement root)
    {
        for(var pass=0;pass<6;pass++){CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);root.Measure(new(320,240));root.Arrange(new(0,0,320,240));root.UpdateLayout();}
    }
    private static Capture Draw(FrameworkElement root){var result=new Capture();root.Render(result);return result;}
    private sealed class Capture:DrawingContextAdapter,IOffsetDrawingContext
    {
        public Point Offset{get;set;}
        internal readonly List<(string Value,double Size,Color Color,Point Origin)> Text=[];
        private Rect? _clip;private readonly Stack<Rect?> _states=[];
        private readonly List<(Geometry Shape,Point Origin)> _clips=[];private readonly Stack<int> _clipCounts=[];
        public override void DrawText(FormattedText value,Point origin)
        {
            var point=new Point(origin.X+Offset.X,origin.Y+Offset.Y);
            if(_clip is { } clip&&value.IsMeasured&&Rect.Intersect(clip,new Rect(point,new Size(value.WidthIncludingTrailingWhitespace,value.Height))).IsEmpty)return;
            if(value.IsMeasured&&_clips.Any(clip=>!clip.Shape.FillContains(new Point(point.X+value.WidthIncludingTrailingWhitespace/2-clip.Origin.X,point.Y+value.Height/2-clip.Origin.Y))))return;
            if(value.Foreground is SolidColorBrush b)Text.Add((value.Text,value.FontSize,b.Color,point));
        }
        public override void DrawRectangle(Brush? b,Pen? p,Rect r){}public override void DrawGeometry(Brush? b,Pen? p,Geometry g){}public override void DrawLine(Pen p,Point a,Point b){}
        public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y){}public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y){}
        public override void DrawImage(ImageSource i,Rect r){}public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c){}
        private void Push(){_states.Push(_clip);_clipCounts.Push(_clips.Count);}
        public override void PushClip(Geometry g){Push();_clips.Add((g,Offset));var rect=g.Bounds;rect=new(rect.X+Offset.X,rect.Y+Offset.Y,rect.Width,rect.Height);_clip=_clip is { } previous?Rect.Intersect(previous,rect):rect;}
        public override void PushTransform(Transform t)=>Push();public override void PushOpacity(double d)=>Push();public override void Pop(){_clip=_states.Pop();var count=_clipCounts.Pop();_clips.RemoveRange(count,_clips.Count-count);}public override void Close(){}
    }
}
