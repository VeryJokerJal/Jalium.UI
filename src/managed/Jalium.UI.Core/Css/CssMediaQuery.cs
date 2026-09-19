namespace Jalium.UI.Styling;

/// <summary>Media-query lists retain unknown feature values under negation and resolve against the host viewport.</summary>
internal sealed class CssMediaQuery(CssBooleanQuery[] queries)
{
    internal static CssMediaQuery Parse(string text)
    {
        var reader = new CssTokenReader(CssParser.StripComments(text));
        if (reader.AtEnd) return new([new CssBooleanQuery.Constant(CssQueryResult.True)]);
        var queries = new List<CssBooleanQuery>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var part)) {queries.Add(new CssBooleanQuery.Constant(CssQueryResult.False)); break;}
            queries.Add(ParseOne(part.ToString()) ?? new CssBooleanQuery.Constant(CssQueryResult.False));
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) break;
            if (reader.AtEnd) queries.Add(new CssBooleanQuery.Constant(CssQueryResult.False));
        } while (!reader.AtEnd);
        return new(queries.ToArray());
    }

    private static CssBooleanQuery? ParseOne(string text)
    {
        var reader = new CssTokenReader(text);
        var negate = false;
        if (reader.TryReadIdent(out var type))
        {
            if (type.Equals("not",StringComparison.OrdinalIgnoreCase) || type.Equals("only",StringComparison.OrdinalIgnoreCase))
            {
                negate = type.Equals("not",StringComparison.OrdinalIgnoreCase);
                if (!reader.TryReadIdent(out type)) return negate ? CssBooleanQuery.Parse(text) : null;
            }
            var media = type.ToString().ToLowerInvariant();
            if (media is "and" or "or" or "not" or "only" or "layer") return null;
            CssBooleanQuery result = new CssBooleanQuery.Constant(media is "all" or "screen" ? CssQueryResult.True : CssQueryResult.False);
            if (!reader.AtEnd)
            {
                if (!reader.TryReadIdent(out var and) || !and.Equals("and",StringComparison.OrdinalIgnoreCase)) return null;
                var condition = CssBooleanQuery.Parse(reader.Remaining.ToString());
                if (condition is null || condition is CssBooleanQuery.Operation { Name:"or" }) return null;
                result = new CssBooleanQuery.Operation("and",result,condition);
            }
            return negate ? new CssBooleanQuery.Operation("not",result) : result;
        }
        return CssBooleanQuery.Parse(text);
    }

    internal bool Evaluate(CssNode element)
    {
        CssLengthContext? context = null;
        CssQueryResult Feature(string raw)
        {
            context ??= CssEngine.BuildLengthContext(element);
            return EvaluateFeature(raw,context.Value);
        }
        return queries.Any(query => query.Evaluate(Feature) == CssQueryResult.True);
    }

    private static CssQueryResult EvaluateFeature(string text, CssLengthContext context)
    {
        var parts = new List<string>(); var operations = new List<string>();
        var start = 0; var depth = 0;
        for (var i=0;i<text.Length;i++)
        {
            var c = text[i];
            if (c is '\'' or '"') { i=CssTokenReader.SkipString(text,i)-1; continue; }
            if (c=='\\') { if (!CssSyntax.ReadEscape(text,ref i,out _)) return CssQueryResult.Unknown; i--; continue; }
            if (c=='(') depth++;
            else if (c==')') depth--;
            else if (depth==0 && c is ':' or '<' or '>' or '=')
            {
                parts.Add(text[start..i].Trim()); var op=c.ToString();
                if (c is '<' or '>' && i+1<text.Length && text[i+1]=='=') {op+='='; i++;}
                operations.Add(op); start=i+1;
            }
        }
        parts.Add(text[start..].Trim());
        static string? Identifier(string text)
        {var reader=new CssTokenReader(text); return reader.TryReadIdent(out var name) && reader.AtEnd ? name.ToString().ToLowerInvariant() : null;}
        var name=Identifier(parts[0]); var reversed=false;
        if (name is null && parts.Count>1) {name=Identifier(parts[1]); reversed=true;}
        if (name is null) return CssQueryResult.Unknown;
        var colonSyntax=operations.Count==1 && operations[0]==":" && !reversed;
        if (colonSyntax)
        {
            operations[0]="=";
            if (name.StartsWith("min-",StringComparison.Ordinal)) {name=name[4..]; operations[0]=">=";}
            else if (name.StartsWith("max-",StringComparison.Ordinal)) {name=name[4..]; operations[0]="<=";}
        }
        if (name is not ("width" or "height" or "aspect-ratio" or "orientation")) return CssQueryResult.Unknown;
        if (name=="orientation")
        {
            if (operations.Count==0) return CssQueryResult.True;
            if (!colonSyntax || operations[0]!="=") return CssQueryResult.Unknown;
            var orientation=Identifier(parts[1]);
            return orientation is not ("portrait" or "landscape") ? CssQueryResult.Unknown :
                (orientation=="landscape" ? context.ViewportWidth>context.ViewportHeight : context.ViewportHeight>=context.ViewportWidth) ? CssQueryResult.True : CssQueryResult.False;
        }
        var actual=name=="width" ? context.ViewportWidth : name=="height" ? context.ViewportHeight
            : context.ViewportHeight==0 ? context.ViewportWidth==0 ? 1 : double.PositiveInfinity : context.ViewportWidth/context.ViewportHeight;
        if (operations.Count==0) return actual==0 ? CssQueryResult.False : CssQueryResult.True;
        if (operations.Count>2 || operations.Contains(":") || operations.Count==2 && (!reversed || operations[0][0]!=operations[1][0] || operations[0]=="=")) return CssQueryResult.Unknown;
        var first=parts[reversed ? 0 : 1];
        if (!Target(first,name,context,out var target)) return CssQueryResult.Unknown;
        var matches=Compare(actual,reversed ? Reverse(operations[0]) : operations[0],target);
        if (operations.Count==2)
        {
            if (!Target(parts[2],name,context,out target)) return CssQueryResult.Unknown;
            matches &= Compare(actual,operations[1],target);
        }
        return matches ? CssQueryResult.True : CssQueryResult.False;
    }

    private static bool Target(string text,string name,CssLengthContext context,out double value)
    {
        value=0;
        var reader=new CssTokenReader(text);
        if (name=="aspect-ratio")
        {
            if (!reader.TryReadNumber(out var numerator,out var unit) || unit!=CssUnit.None || numerator<0) return false;
            var denominator=1d;
            if (!reader.AtEnd && (!reader.TryReadSlash() || !reader.TryReadNumber(out denominator,out unit) || unit!=CssUnit.None || denominator<0)) return false;
            value=denominator==0 ? numerator==0 ? 1 : double.PositiveInfinity : numerator/denominator;
            return reader.AtEnd;
        }
        var lengths=new CssLengthContext(CssLengthContext.DefaultFontSize,CssLengthContext.DefaultFontSize,CssLengthContext.DefaultFontSize,
            context.ViewportWidth,context.ViewportHeight, fonts: CssFontContext.Initial with
            { Dependency = context.Fonts?.Dependency, MetricsEpoch = context.Fonts?.MetricsEpoch ?? 0 }, viewports: context.Viewports);
        return reader.TryReadLength(out var length) && reader.AtEnd && !length.UsesPercent &&
            (length.Unit!=CssUnit.None || length.Value==0) && length.TryResolve(lengths,CssPercentBasis.NotSupported,out value);
    }
    private static string Reverse(string op) => op switch { ">"=>"<", ">="=>"<=", "<"=>">", "<="=>">=", _=>op };
    private static bool Compare(double actual,string op,double target) => op switch
    { ">"=>actual>target, ">="=>actual>=target, "<"=>actual<target, "<="=>actual<=target, "="=>actual==target || Math.Abs(actual-target)<1e-9, _=>false };
}
