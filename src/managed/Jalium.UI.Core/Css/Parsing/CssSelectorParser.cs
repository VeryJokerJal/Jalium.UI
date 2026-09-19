namespace Jalium.UI.Styling;

/// <summary>
/// Parses a selector group ("Button.primary:hover, .card &gt; .title"). Per the CSS error
/// model, any invalid selector in a comma group invalidates the whole group (the caller
/// then drops the entire rule).
/// </summary>
internal static partial class CssSelectorParser
{
    [ThreadStatic] private static bool s_strictSupport;
    [ThreadStatic] private static bool s_namespaceError;
    internal static bool IsSupported(ReadOnlySpan<char> text,CssNamespaceContext? namespaces,out bool namespaceError)
    {
        var previous=s_strictSupport; var previousError=s_namespaceError;
        s_strictSupport=true; s_namespaceError=false;
        try
        {
            var supported=ParseGroup(text,out _,namespaces:namespaces) is {Count:1};
            namespaceError=s_namespaceError; return supported;
        }
        finally {s_strictSupport=previous; s_namespaceError=previousError;}
    }
    public static List<CssSelector>? ParseGroup(ReadOnlySpan<char> text, out string? error,
        CssNestingContext? nesting = null, CssRelativeSelectorMode relative = CssRelativeSelectorMode.None,
        CssNamespaceContext? namespaces = null, bool ignoreDefaultOnSubject = false)
    {
        error = null;
        var selectors = new List<CssSelector>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if(i<text.Length && text[i]=='/' && i+1<text.Length && text[i+1]=='*') {i=CssTokenReader.SkipComment(text,i)-1; continue;}
            if (i < text.Length && text[i] == '\\')
            {
                var escaped = i;
                if (CssSyntax.ReadEscape(text, ref escaped, out _)) { i = escaped - 1; continue; }
            }
            if (i < text.Length && text[i] is '\'' or '"')
            {
                i = CssTokenReader.SkipString(text, i) - 1;
                continue;
            }
            if (i < text.Length && text[i] is '(' or '[') depth++;
            else if (i < text.Length && text[i] is ')' or ']') depth--;
            if (depth < 0 || depth > 64) { error = "invalid selector nesting"; return null; }
            if (i == text.Length && depth != 0) { error = "unclosed selector argument"; return null; }
            if (depth != 0) continue;
            if (i < text.Length && text[i] != ',')
            {
                continue;
            }

            var segment = text.Slice(start, i - start).Trim();
            start = i + 1;
            if (segment.IsEmpty)
            {
                error = "empty selector in group";
                return null;
            }

            var selector = ParseSingle(segment, out error, nesting, relative, namespaces,ignoreDefaultOnSubject);
            if (selector is null)
            {
                return null;
            }

            selectors.Add(selector);
        }

        return selectors.Count > 0 ? selectors : null;
    }

    private static CssSelector? ParseSingle(ReadOnlySpan<char> text, out string? error, CssNestingContext? nesting, CssRelativeSelectorMode relative,
        CssNamespaceContext? namespaces,bool ignoreDefaultOnSubject)
    {
        // Parsed left-to-right, then reversed so Compounds[0] is the rightmost subject.
        var compounds = new List<CssCompound>();
        var combinators = new List<CssCombinator>();

        var pos = 0;
        var ids = 0;
        var classesAndPseudos = 0;
        var types = 0;
        var pendingCombinator = CssCombinator.Descendant;
        var expectCompound = true;
        SkipWhitespace(text,ref pos);
        var leading = relative != CssRelativeSelectorMode.None && pos<text.Length && text[pos] is '>' or '+' or '~';
        if (leading)
        {
            compounds.Add(NestingCompound(nesting, relative == CssRelativeSelectorMode.Has, ref ids, ref classesAndPseudos, ref types));
            expectCompound = false;
        }

        while (true)
        {
            var hadWhitespace = SkipWhitespace(text, ref pos);
            if (pos >= text.Length)
            {
                break;
            }

            var c = text[pos];
            if (!expectCompound)
            {
                if (c == '>')
                {
                    pendingCombinator = CssCombinator.Child;
                    pos++;
                    expectCompound = true;
                    continue;
                }

                if (c is '~' or '+')
                {
                    pendingCombinator = c == '+' ? CssCombinator.AdjacentSibling : CssCombinator.GeneralSibling;
                    pos++;
                    expectCompound = true;
                    continue;
                }

                // Whitespace already consumed; the next compound implies a descendant combinator.
                if (!hadWhitespace)
                { error = "missing combinator between selectors"; return null; }
                pendingCombinator = CssCombinator.Descendant;
                expectCompound = true;
                continue;
            }

            var compound = ParseCompound(text, ref pos, ref ids, ref classesAndPseudos, ref types, out error, nesting,namespaces);
            if (compound is null)
            {
                return null;
            }

            if (compounds.Count > 0)
            {
                combinators.Add(pendingCombinator);
            }

            compounds.Add(compound);
            expectCompound = false;
        }

        if (compounds.Count == 0)
        {
            error = "empty selector";
            return null;
        }

        if (expectCompound)
        {
            // Trailing explicit combinator such as "A >".
            error = "selector ended after combinator";
            return null;
        }

        var containsNesting = compounds.Any(c => c.Pseudos?.Contains(CssPseudoClass.NestingScope) == true ||
            c.Functions?.Any(f => f.Name == "nesting" || f.Selectors.Any(s => s.ContainsNesting)) == true);
        var containsScope = compounds.Any(c => c.Pseudos?.Contains(CssPseudoClass.Scope) == true ||
            c.Functions?.Any(f => f.Selectors.Any(s => s.ContainsScope)) == true);
        if (!leading && (relative == CssRelativeSelectorMode.Has || !containsNesting &&
            (relative == CssRelativeSelectorMode.Nesting || relative == CssRelativeSelectorMode.Scope && !containsScope)))
        {
            compounds.Insert(0, NestingCompound(nesting, relative == CssRelativeSelectorMode.Has, ref ids, ref classesAndPseudos, ref types));
            combinators.Insert(0, CssCombinator.Descendant);
            containsNesting |= relative != CssRelativeSelectorMode.Has;
        }
        compounds.Reverse();
        combinators.Reverse();
        if(ignoreDefaultOnSubject && compounds[0].DefaultNamespaceApplied && !compounds[0].ExplicitTypeSelector)
        {
            compounds[0].NamespaceUri=null;
            compounds[0].DefaultNamespaceApplied=false;
        }

        var selector = new CssSelector
        {
            Compounds = compounds.ToArray(),
            Combinators = combinators.ToArray(),
            Specificity = CssSelector.PackSpecificity(ids, classesAndPseudos, types),
            ContainsNesting = containsNesting, ContainsScope = containsScope,
        };

        selector.RightmostStates = selector.Compounds[0].StateMask;
        for (var i = 1; i < selector.Compounds.Length; i++)
        {
            selector.AncestorStates |= selector.Compounds[i].StateMask;
        }

        error = null;
        return selector;
    }

    private static CssCompound? ParseCompound(
        ReadOnlySpan<char> text,
        ref int pos,
        ref int ids,
        ref int classesAndPseudos,
        ref int types,
        out string? error, CssNestingContext? nesting,CssNamespaceContext? namespaces)
    {
        var compound = new CssCompound {NamespaceUri=namespaces?.DefaultNamespace,DefaultNamespaceApplied=namespaces?.DefaultNamespace is not null};
        var sawAnything = false;

        if(pos<text.Length && (text[pos] is '*' or '|' || IsIdentStart(text,pos)))
        {
            if(!ReadQualifiedName(text,ref pos,namespaces,true,out var name,out var namespaceUri,out var qualified))
            {error="invalid namespace-qualified type selector"; return null;}
            compound.Universal=name=="*";
            compound.TypeName=compound.Universal ? null : name;
            compound.ExplicitTypeSelector=true;
            if(qualified) {compound.NamespaceUri=namespaceUri; compound.DefaultNamespaceApplied=false;}
            compound.ExpandedTypeName=qualified || namespaces?.DefaultNamespace is not null;
            if(!compound.Universal) types++;
            sawAnything=true;
        }

        List<string>? classes = null;
        List<CssPseudoClass>? pseudos = null;
        List<CssAttributeSelector>? attributes = null;
        List<CssFunctionalPseudo>? functions = null;

        while (pos < text.Length)
        {
            var c = text[pos];
            if(c=='/' && pos+1<text.Length && text[pos+1]=='*') {pos=CssTokenReader.SkipComment(text,pos); continue;}
            if (c == '&')
            {
                var anchor = NestingCompound(nesting, false, ref ids, ref classesAndPseudos, ref types);
                if (anchor.Pseudos is { } anchors) (pseudos ??= []).AddRange(anchors);
                if (anchor.Functions is { } nested) (functions ??= []).AddRange(nested);
                compound.StateMask |= anchor.StateMask;
                pos++; sawAnything = true;
            }
            else if (c == '[')
            {
                if (!TryParseAttribute(text, ref pos, out var attribute,namespaces))
                { error = "invalid attribute selector"; return null; }
                (attributes ??= []).Add(attribute!);
                classesAndPseudos++;
                sawAnything = true;
            }
            else if (c == '#')
            {
                pos++;
                if (pos >= text.Length || !IsIdentChar(text[pos]))
                {
                    error = "expected identifier after '#'";
                    return null;
                }

                compound.Id = ReadIdent(text, ref pos).ToString();
                ids++;
                sawAnything = true;
            }
            else if (c == '.')
            {
                pos++;
                if (pos >= text.Length || !IsIdentStart(text, pos))
                {
                    error = "expected identifier after '.'";
                    return null;
                }

                (classes ??= new List<string>()).Add(ReadIdent(text, ref pos).ToString());
                classesAndPseudos++;
                sawAnything = true;
            }
            else if (c == ':')
            {
                pos++;
                if (pos < text.Length && text[pos] == ':')
                {
                    error = "pseudo-elements are not supported";
                    return null;
                }

                if (pos >= text.Length || !IsIdentStart(text, pos))
                {
                    error = "expected identifier after ':'";
                    return null;
                }

                var pseudoStart = pos;
                var pseudoName = ReadIdent(text, ref pos);
                if (pos < text.Length && text[pos] == '(')
                {
                    var reader = new CssTokenReader(text[pseudoStart..]);
                    if (!reader.TryReadFunction(out _, out var args) ||
                        !TryParseFunctional(pseudoName.ToString(), args.Remaining, out var function, out var specificity, nesting,namespaces))
                    { error = "invalid functional pseudo-class"; return null; }
                    pos = pseudoStart + reader.Position;
                    (functions ??= []).Add(function!);
                    ids += specificity >> 20;
                    classesAndPseudos += (specificity >> 10) & 1023;
                    types += specificity & 1023;
                    foreach (var nested in function!.Selectors)
                        compound.StateMask |= nested.RightmostStates | nested.AncestorStates;
                    sawAnything = true;
                    continue;
                }

                if (!TryMapPseudo(pseudoName, out var pseudo))
                {
                    error = $"unknown pseudo-class ':{pseudoName}'";
                    return null;
                }

                (pseudos ??= new List<CssPseudoClass>()).Add(pseudo);
                compound.StateMask |= CssCompound.ToStateMask(pseudo);
                classesAndPseudos++;
                sawAnything = true;
            }
            else
            {
                break;
            }
        }

        if (!sawAnything)
        {
            error = $"unexpected character '{text[pos]}' in selector";
            return null;
        }

        compound.Classes = classes?.ToArray();
        compound.Pseudos = pseudos?.ToArray();
        compound.Attributes = attributes?.ToArray();
        compound.Functions = functions?.ToArray();
        error = null;
        return compound;
    }

    private static CssCompound NestingCompound(CssNestingContext? nesting, bool relativeAnchor, ref int ids, ref int classes, ref int types)
    {
        if (relativeAnchor) return new() { Pseudos = [CssPseudoClass.RelativeAnchor] };
        if (nesting is null) return new() { Pseudos = [CssPseudoClass.NestingScope] };
        var specificity = nesting.Selectors.Length == 0 ? 0 : nesting.Selectors.Max(s => s.Specificity);
        ids += specificity >> 20; classes += (specificity >> 10) & 1023; types += specificity & 1023;
        return new()
        {
            Functions = [new("nesting", nesting.Selectors, NestingScope: nesting.Scope)],
            StateMask = nesting.Selectors.Aggregate(CssStateMask.None, (mask, selector) => mask | selector.RightmostStates | selector.AncestorStates),
        };
    }

    private static bool TryMapPseudo(ReadOnlySpan<char> name, out CssPseudoClass pseudo)
    {
        if (Eq(name, "root")) { pseudo = CssPseudoClass.Root; return true; }
        if (Eq(name, "scope")) { pseudo = CssPseudoClass.Scope; return true; }
        if (Eq(name, "empty")) { pseudo = CssPseudoClass.Empty; return true; }
        if (Eq(name, "first-child")) { pseudo = CssPseudoClass.FirstChild; return true; }
        if (Eq(name, "last-child")) { pseudo = CssPseudoClass.LastChild; return true; }
        if (Eq(name, "only-child")) { pseudo = CssPseudoClass.OnlyChild; return true; }
        if (Eq(name, "first-of-type")) { pseudo = CssPseudoClass.FirstOfType; return true; }
        if (Eq(name, "last-of-type")) { pseudo = CssPseudoClass.LastOfType; return true; }
        if (Eq(name, "only-of-type")) { pseudo = CssPseudoClass.OnlyOfType; return true; }
        if (Eq(name, "hover")) { pseudo = CssPseudoClass.Hover; return true; }
        if (Eq(name, "active")) { pseudo = CssPseudoClass.Active; return true; }
        if (Eq(name, "focus")) { pseudo = CssPseudoClass.Focus; return true; }
        if (Eq(name, "focus-visible")) { pseudo = CssPseudoClass.FocusVisible; return true; }
        if (Eq(name, "focus-within")) { pseudo = CssPseudoClass.FocusWithin; return true; }
        if (Eq(name, "disabled")) { pseudo = CssPseudoClass.Disabled; return true; }
        if (Eq(name, "enabled")) { pseudo = CssPseudoClass.Enabled; return true; }
        if (Eq(name, "checked")) { pseudo = CssPseudoClass.Checked; return true; }
        if (Eq(name, "indeterminate")) { pseudo = CssPseudoClass.Indeterminate; return true; }

        pseudo = default;
        return false;
    }

    private static bool Eq(ReadOnlySpan<char> text, string candidate)
        => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);

    private static bool SkipWhitespace(ReadOnlySpan<char> text, ref int pos)
    {
        var whitespace=false;
        while(pos<text.Length)
        {
            if(CssTokenReader.IsCssWhitespace(text[pos])) {pos++; whitespace=true;}
            else if(text[pos]=='/' && pos+1<text.Length && text[pos+1]=='*') pos=CssTokenReader.SkipComment(text,pos);
            else break;
        }
        return whitespace;
    }

    private static ReadOnlySpan<char> ReadIdent(ReadOnlySpan<char> text, ref int pos)
    {
        CssSyntax.ReadIdentifier(text, ref pos, out var name, hash: true);
        return name;
    }

    private static bool IsIdentStart(ReadOnlySpan<char> text, int pos)
    {
        if (text[pos] == '\\') return CssSyntax.IsNameStart(text, pos);
        var c = text[pos];
        if (char.IsAsciiLetter(c) || c == '_' || c > 0x7F)
        {
            return true;
        }

        if (c == '-')
        {
            var next = pos + 1;
            return next < text.Length && IsIdentChar(text[next]) && !char.IsAsciiDigit(text[next]);
        }

        return false;
    }

    private static bool IsIdentChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '\\' || c > 0x7F;
}
