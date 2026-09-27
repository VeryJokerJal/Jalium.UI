namespace Jalium.UI.Styling;

internal static partial class CssSelectorParser
{
    private static void SkipComments(ReadOnlySpan<char> text,ref int position)
    {
        while(position+1<text.Length && text[position]=='/' && text[position+1]=='*') position=CssTokenReader.SkipComment(text,position);
    }

    private static bool ReadQualifiedName(ReadOnlySpan<char> text,ref int position,CssNamespaceContext? namespaces,bool allowLocalWildcard,
        out string name,out string? namespaceUri,out bool qualified)
    {
        name=string.Empty; namespaceUri=null; qualified=false;
        string prefix;
        if(position>=text.Length) return false;
        if(text[position]=='|') {prefix=string.Empty; qualified=true; position++;}
        else
        {
            if(text[position]=='*') {prefix="*"; position++;}
            else
            {
                if(!CssSyntax.ReadIdentifier(text,ref position,out var first)) return false;
                prefix=first.ToString();
            }
            var separator=position; SkipComments(text,ref separator);
            if(separator<text.Length && text[separator]=='|' && (separator+1==text.Length || text[separator+1] is not ('=' or '|')))
            {qualified=true; position=separator+1;}
        }
        if(!qualified) {name=prefix; return allowLocalWildcard || name!="*";}
        if(prefix.Length==0) namespaceUri=string.Empty;
        else if(prefix!="*")
        {
            if(namespaces is null || !namespaces.TryResolve(prefix,out var resolved)) {s_namespaceError=true; return false;}
            namespaceUri=resolved;
        }
        SkipComments(text,ref position);
        if(position<text.Length && text[position]=='*' && allowLocalWildcard) {position++; name="*"; return true;}
        if(!CssSyntax.ReadIdentifier(text,ref position,out var local)) return false;
        name=local.ToString(); return true;
    }
}
