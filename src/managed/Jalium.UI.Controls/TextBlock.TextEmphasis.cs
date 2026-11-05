using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private IEnumerable<(DependencyObject Source,int Start,int End)> EmphasisSources(int start,int length)
    {
        if(length<=0)yield break;
        if(_inlines is { Count:>0 })
        {
            foreach(var range in GetInlineTextRanges())
                if(range.End>start&&range.Start<start+length&&CssTextEmphasis.Style(range.Run).Mark.Length>0)
                    yield return(range.Run,Math.Max(start,range.Start),Math.Min(start+length,range.End));
        }
        else
        {
            var owner=AutospaceStyleSource(this);
            if(CssTextEmphasis.Style(owner).Mark.Length>0)yield return(owner,start,start+length);
        }
    }

    private IEnumerable<(DependencyObject Source,int Start,int End)> EmphasisCharacters(int start,int length)
    {
        var sources=EmphasisSources(start,length).ToArray();if(sources.Length==0)yield break;
        var boundaries=StringInfo.ParseCombiningCharacters(_displayText);
        for(var index=0;index<boundaries.Length;index++)
        {
            var first=boundaries[index];var end=index+1<boundaries.Length?boundaries[index+1]:_displayText.Length;
            if(first<start||first>=start+length)continue;
            var source=sources.FirstOrDefault(range=>range.Start<=first&&range.End>first).Source;
            if(source is not null&&CssTextEmphasis.ShouldMark(_displayText.AsSpan(first,end-first)))yield return(source,first,end);
        }
    }

    private bool HasCssTextEmphasis=>EmphasisCharacters(0,_displayText.Length).Any();

    private void EmphasisLineBounds(int start,int length,ref double above,ref double below)
    {
        foreach(var source in EmphasisCharacters(start,length).Select(range=>range.Source).Distinct())
        {
            if(CssTextEmphasisPainter.Create(source,FontSize,Foreground) is not { } mark)continue;
            if(mark.Under)below=Math.Max(below,mark.Descent+mark.Height);
            else above=Math.Max(above,mark.Ascent+mark.Height);
        }
    }

    private void DrawTextEmphasis(DrawingContext context,in TextLayoutLine line,double lineOriginX,double lineY)
    {
        var marks=new Dictionary<DependencyObject,CssTextEmphasisPainter.Mark?>();
        foreach(var (source,start,end) in EmphasisCharacters(line.StartIndex,line.Length))
        {
            if(!marks.TryGetValue(source,out var mark))marks[source]=mark=CssTextEmphasisPainter.Create(source,FontSize,Foreground);
            if(mark is null)continue;
            var capture=new CssSelectionPainter.GeometryCapture(context);DrawSelection(capture,start,end-start,Brushes.Black);
            // Retained source-range geometry handles bidi, ligatures, spacing,
            // shared paragraphs and font fallback without reshaping prefixes.
            var lineTop=TextLineTop(line,lineY);
            var rects=capture.Rectangles.Where(rect=>Math.Abs(rect.Top-lineTop)<.01).ToArray();
            if(rects.Length==0)
            {
                var a=GetInlineBoundaryX(line,lineOriginX,start);var b=GetInlineBoundaryX(line,lineOriginX,Math.Min(end,line.StartIndex+line.Length));
                CssTextEmphasisPainter.Draw(context,mark,(a+b)/2,lineY+line.Baseline);
            }
            else foreach(var rect in rects)CssTextEmphasisPainter.Draw(context,mark,rect.Left+rect.Width/2,lineY+line.Baseline);
        }
    }
}
