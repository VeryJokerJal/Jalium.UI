using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterTextEmphasis()
    {
        RegisterDecorationLonghand("text-emphasis-style",CssTextEmphasis.StyleProperty,
            static(ref CssTokenReader reader,CssCompileContext _)=>TryReadEmphasisStyle(ref reader,out var style)&&reader.AtEnd
                ?new CssImmediateValue(CssTextEmphasis.StyleProperty,style):null);
        RegisterDecorationLonghand("text-emphasis-color",CssTextEmphasis.ColorProperty,
            static(ref CssTokenReader reader,CssCompileContext _)=>TryReadEmphasisColor(ref reader,out var color)&&reader.AtEnd?color:null);
        RegisterDecorationLonghand("text-emphasis-position",CssTextEmphasis.PositionProperty,
            static(ref CssTokenReader reader,CssCompileContext _)=>TryReadEmphasisPosition(ref reader,out var position)
                ?new CssImmediateValue(CssTextEmphasis.PositionProperty,position):null);
        RegisterShorthand("text-emphasis",static(ref CssTokenReader reader,CssCompileContext context,List<CssCompiledDeclaration> output)=>
        {
            CssEmphasisStyle style=default;CssCompiledValue? color=null;var seenStyle=false;
            while(!reader.AtEnd)
            {
                var probe=reader;
                if(!seenStyle&&TryReadEmphasisStyle(ref probe,out var candidate)){style=candidate;seenStyle=true;reader=probe;continue;}
                probe=reader;
                if(color is null&&TryReadEmphasisColor(ref probe,out color)){reader=probe;continue;}
                return false;
            }
            if(!seenStyle&&color is null)return false;
            color??=new CssContextualColorValue("text-emphasis-color","currentColor");
            output.Add(new CssCompiledDeclaration("text-emphasis-style",new CssImmediateValue(CssTextEmphasis.StyleProperty,style),false));
            output.Add(new CssCompiledDeclaration("text-emphasis-color",color,false));return true;
        });
    }
    private static bool TryReadEmphasisStyle(ref CssTokenReader reader,out CssEmphasisStyle style)
    {
        style=default;var probe=reader;
        if(probe.TryReadString(out var custom))
        {
            style=new(CssEmphasisShape.Custom,Custom:custom.Length==0?"":StringInfo.GetNextTextElement(custom));reader=probe;return true;
        }
        var seen=false;var seenShape=false;var seenFill=false;var open=false;var shape=CssEmphasisShape.Circle;
        while(true)
        {
            var next=probe;if(!next.TryReadIdent(out var word))break;
            if(word.Equals("none",StringComparison.OrdinalIgnoreCase))
            {
                if(seen)return false;reader=next;return true;
            }
            var value=word.ToString().ToLowerInvariant();
            if(value is "filled" or "open")
            {if(seenFill)return false;seenFill=true;open=value=="open";}
            else
            {
                var found=value switch{"dot"=>CssEmphasisShape.Dot,"circle"=>CssEmphasisShape.Circle,"double-circle"=>CssEmphasisShape.DoubleCircle,
                    "triangle"=>CssEmphasisShape.Triangle,"sesame"=>CssEmphasisShape.Sesame,_=>(CssEmphasisShape?)null};
                if(found is null)break;if(seenShape)return false;seenShape=true;shape=found.Value;
            }
            seen=true;probe=next;
        }
        if(!seen)return false;style=new(shape,open);reader=probe;return true;
    }
    private static bool TryReadEmphasisColor(ref CssTokenReader reader,out CssCompiledValue? value)
    {
        value=null;var probe=reader;
        if(!CssColorParser.TryParseContextual(ref probe,out var color,out var current,out var deferred))return false;
        value=deferred is not null?new CssContextualColorValue("text-emphasis-color",deferred):current?
            new CssContextualColorValue("text-emphasis-color","currentColor"):new EmphasisAbsoluteColorValue((Brush)FreezeIfPossible(new SolidColorBrush(color)));
        reader=probe;return true;
    }
    private sealed class EmphasisAbsoluteColorValue(Brush brush):CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context,ICssSetterSink sink)
        {sink.Set(CssTextEmphasis.ColorProperty,brush);sink.Set(CssTextEmphasis.ColorExpressionProperty,null);return true;}
    }
    private static bool TryReadEmphasisPosition(ref CssTokenReader reader,out CssEmphasisPosition position)
    {
        position=CssEmphasisPosition.OverRight;var side=false;var direction=false;
        while(!reader.AtEnd)
        {
            if(!reader.TryReadIdent(out var word))return false;
            switch(word.ToString().ToLowerInvariant())
            {
                case "over":if(side)return false;side=true;break;
                case "under":if(side)return false;side=true;position|=CssEmphasisPosition.Under;break;
                case "left":if(direction)return false;direction=true;position|=CssEmphasisPosition.Left;break;
                case "right":if(direction)return false;direction=true;break;
                default:return false;
            }
        }
        return side;
    }
}
