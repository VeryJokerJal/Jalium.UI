using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool[]? _displayHiddenUnits;
    internal string DisplayExposedText
    {
        get
        {
            if (DocumentDisplaySuppressed || CssRubyVisibility.Suppressed(this) || CssContainmentProperties.HidesContentsFromUserAgent(this)) return string.Empty;
            if (InlineDisplayMask() is not { } mask) return _displayText;
            var result = new System.Text.StringBuilder(_displayText.Length);
            for (var i=0;i<mask.Length;i++) if(!mask[i]) result.Append(_displayText[i]);
            return result.ToString();
        }
    }
    private bool[]? InlineDisplayMask()
    {
        bool[]? mask=null;var offset=0;
        void Walk(Inline inline)
        {
            var length=TextElement.GetContentLength(inline);
            if(CssDisplayProperties.SuppressesBoxTree(inline))
            {
                mask??=new bool[_displayText.Length];
                for(var i=offset;i<Math.Min(offset+length,mask.Length);i++)mask[i]=true;
                offset+=length;return;
            }
            if(inline is Span span)foreach(var child in span.Inlines)Walk(child);
            else offset+=length;
        }
        if(_inlines is not null)foreach(var inline in _inlines)Walk(inline);
        return _displayHiddenUnits=mask;
    }
    private bool HasDisplayText(int start,int length)
    {
        if(_displayHiddenUnits is not { } mask)return true;
        if(length==0)return start<mask.Length&&!mask[Math.Max(0,start)] || start>0&&!mask[Math.Min(start-1,mask.Length-1)];
        for(var i=Math.Max(0,start);i<Math.Min((long)start+length,mask.Length);i++)if(!mask[i])return true;
        return false;
    }
}
