using System.Globalization;

namespace Jalium.UI.Styling;

internal static partial class CssSelectorParser
{
    [ThreadStatic] private static int s_functionDepth;

    private static bool TryParseAttribute(ReadOnlySpan<char> text, ref int position, out CssAttributeSelector? attribute,CssNamespaceContext? namespaces)
    {
        attribute = null;
        var start = ++position;
        while (position < text.Length && text[position] != ']')
        {
            if(text[position]=='/' && position+1<text.Length && text[position+1]=='*') {position=CssTokenReader.SkipComment(text,position); continue;}
            if (text[position] == '\\' && CssSyntax.ReadEscape(text, ref position, out _)) continue;
            if (text[position] is '\'' or '"') position = CssTokenReader.SkipString(text, position);
            else position++;
        }
        if (position == text.Length) return false;
        var valueText=text[start..position++]; var cursor=0;
        SkipWhitespace(valueText,ref cursor);
        if(!ReadQualifiedName(valueText,ref cursor,namespaces,false,out var name,out var namespaceUri,out var qualified)) return false;
        if(!qualified) namespaceUri=string.Empty;
        var reader = new CssTokenReader(valueText[cursor..]);
        if (reader.AtEnd) { attribute = new(name, "", "", false,namespaceUri); return true; }
        var rest = reader.Remaining;
        var count = rest[0] == '=' ? 1 : rest.Length > 1 && rest[1] == '=' && rest[0] is '~' or '|' or '^' or '$' or '*' ? 2 : 0;
        if (count == 0) return false;
        var operation = rest[..count].ToString();
        reader = new CssTokenReader(rest[count..]);
        string value;
        if (reader.TryReadString(out var quoted)) value = quoted;
        else if (reader.TryReadIdent(out var identifier)) value = identifier.ToString();
        else return false;
        var insensitive = false;
        if (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var flag) || !reader.AtEnd) return false;
            if (flag.Equals("i", StringComparison.OrdinalIgnoreCase)) insensitive = true;
            else if (!flag.Equals("s", StringComparison.OrdinalIgnoreCase)) return false;
        }
        attribute = new(name, operation, value, insensitive,namespaceUri);
        return true;
    }

    private static bool TryParseFunctional(string name, ReadOnlySpan<char> arguments,
        out CssFunctionalPseudo? function, out int specificity, CssNestingContext? nesting,CssNamespaceContext? namespaces)
    {
        function = null;
        specificity = 0;
        if (s_functionDepth >= 64) return false;
        s_functionDepth++;
        try
        {
            name = name.ToLowerInvariant();
            if (name is "is" or "where" or "not" or "has")
            {
                var groups = SplitArguments(arguments.ToString());
                var selectors = new List<CssSelector>();
                foreach (var group in groups)
                {
                    var parsed = ParseGroup(group, out _, nesting, name == "has" ? CssRelativeSelectorMode.Has : CssRelativeSelectorMode.None,
                        namespaces,ignoreDefaultOnSubject:name is "is" or "where" or "not");
                    if (parsed is null)
                    {
                        if (!s_strictSupport && name is "is" or "where") continue;
                        return false;
                    }
                    selectors.AddRange(parsed);
                }
                if (selectors.Count > 0 && name != "where")
                    specificity = selectors.Max(s => s.Specificity);
                function = new(name, selectors.ToArray());
                return true;
            }
            if (name is "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type")
            {
                var raw = arguments.ToString().Trim();
                var of = raw.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
                CssSelector[] selectors = [];
                if (of >= 0)
                {
                    if (name is "nth-of-type" or "nth-last-of-type") return false;
                    var parsed = ParseGroup(raw.AsSpan(of + 4), out _, nesting,namespaces:namespaces);
                    if (parsed is null) return false;
                    selectors = parsed.ToArray();
                    raw = raw[..of];
                }
                raw = string.Concat(CssParser.StripComments(raw).ToString().Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
                int a, b;
                if (raw == "odd") { a = 2; b = 1; }
                else if (raw == "even") { a = 2; b = 0; }
                else
                {
                    var n = raw.IndexOf('n');
                    if (n < 0) { a = 0; if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) return false; }
                    else
                    {
                        var coefficient = raw[..n];
                        a = coefficient is "" or "+" ? 1 : coefficient == "-" ? -1 : 0;
                        if (a == 0 && !int.TryParse(coefficient, NumberStyles.Integer, CultureInfo.InvariantCulture, out a)) return false;
                        var offset = raw[(n + 1)..];
                        b = 0;
                        if (offset.Length > 0 && (offset[0] is not ('+' or '-') ||
                            !int.TryParse(offset, NumberStyles.Integer, CultureInfo.InvariantCulture, out b))) return false;
                    }
                }
                specificity = (1 << 10) + (selectors.Length == 0 ? 0 : selectors.Max(s => s.Specificity));
                function = new(name, selectors, a, b);
                return true;
            }
            return false;
        }
        finally { s_functionDepth--; }
    }

    private static IEnumerable<string> SplitArguments(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if(text[i]=='/' && i+1<text.Length && text[i+1]=='*') {i=CssTokenReader.SkipComment(text,i)-1; continue;}
            if (text[i] == '\\')
            {
                var escaped = i;
                if (CssSyntax.ReadEscape(text, ref escaped, out _)) { i = escaped - 1; continue; }
            }
            if (text[i] is '\'' or '"') { i = CssTokenReader.SkipString(text, i) - 1; continue; }
            if (text[i] is '(' or '[') depth++;
            if (text[i] is ')' or ']') depth--;
            if (depth == 0 && text[i] == ',') { yield return text[start..i].Trim(); start = i + 1; }
        }
        yield return text[start..].Trim();
    }
}
