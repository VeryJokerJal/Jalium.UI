namespace Jalium.UI.Styling;

/// <summary>Namespace bindings belong to one parsed sheet; selectors retain expanded names, not prefixes.</summary>
internal sealed class CssNamespaceContext
{
    private readonly Dictionary<string,string> _prefixes = new(StringComparer.Ordinal);
    internal string? DefaultNamespace { get; private set; }
    internal bool TryResolve(string prefix,out string namespaceUri) => _prefixes.TryGetValue(prefix,out namespaceUri!);

    internal bool Declare(ReadOnlySpan<char> text)
    {
        text=text.Trim();
        if(text.IsEmpty || text[^1]!=';') return false;
        var reader=new CssTokenReader(text[..^1]);
        string? prefix=null;
        if(reader.TryReadIdent(out var identifier)) prefix=identifier.ToString();
        string value;
        if(reader.TryReadString(out var quoted)) value=quoted;
        else if(reader.TryReadFunction(out var function,out var arguments) && function.Equals("url",StringComparison.OrdinalIgnoreCase))
        {
            if(arguments.TryReadString(out quoted)) {if(!arguments.AtEnd) return false; value=quoted;}
            else
            {
                var raw=arguments.Remaining.Trim(); var decoded=new System.Text.StringBuilder();
                for(var position=0;position<raw.Length;)
                {
                    if(raw[position]=='\\') {if(!CssSyntax.ReadEscape(raw,ref position,out var escape)) return false; decoded.Append(escape); continue;}
                    var c=raw[position++];
                    if(CssTokenReader.IsCssWhitespace(c) || c<0x20 || c is '(' or ')' or '\'' or '"') return false;
                    decoded.Append(c);
                }
                value=decoded.ToString();
            }
        }
        else return false;
        if(!reader.AtEnd) return false;
        if(prefix is null) DefaultNamespace=value; else _prefixes[prefix]=value;
        return true;
    }
}
