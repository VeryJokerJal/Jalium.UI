namespace Jalium.UI.Styling;

/// <summary>
/// Parses a selector group ("Button.primary:hover, .card &gt; .title"). Per the CSS error
/// model, any invalid selector in a comma group invalidates the whole group (the caller
/// then drops the entire rule).
/// </summary>
internal static class CssSelectorParser
{
    public static List<CssSelector>? ParseGroup(ReadOnlySpan<char> text, out string? error)
    {
        error = null;
        var selectors = new List<CssSelector>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
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

            var selector = ParseSingle(segment, out error);
            if (selector is null)
            {
                return null;
            }

            selectors.Add(selector);
        }

        return selectors.Count > 0 ? selectors : null;
    }

    private static CssSelector? ParseSingle(ReadOnlySpan<char> text, out string? error)
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

        while (true)
        {
            SkipWhitespace(text, ref pos);
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
                    error = $"unsupported combinator '{c}'";
                    return null;
                }

                // Whitespace already consumed; the next compound implies a descendant combinator.
                pendingCombinator = CssCombinator.Descendant;
                expectCompound = true;
                continue;
            }

            var compound = ParseCompound(text, ref pos, ref ids, ref classesAndPseudos, ref types, out error);
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

        compounds.Reverse();
        combinators.Reverse();

        var selector = new CssSelector
        {
            Compounds = compounds.ToArray(),
            Combinators = combinators.ToArray(),
            Specificity = CssSelector.PackSpecificity(ids, classesAndPseudos, types),
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
        out string? error)
    {
        var compound = new CssCompound();
        var sawAnything = false;

        if (pos < text.Length && text[pos] == '*')
        {
            compound.Universal = true;
            pos++;
            sawAnything = true;
        }
        else if (pos < text.Length && IsIdentStart(text, pos))
        {
            compound.TypeName = ReadIdent(text, ref pos).ToString();
            types++;
            sawAnything = true;
        }

        List<string>? classes = null;
        List<CssPseudoClass>? pseudos = null;

        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '#')
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

                var pseudoName = ReadIdent(text, ref pos);
                if (pos < text.Length && text[pos] == '(')
                {
                    error = $"functional pseudo-class ':{pseudoName}()' is not supported";
                    return null;
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
        error = null;
        return compound;
    }

    private static bool TryMapPseudo(ReadOnlySpan<char> name, out CssPseudoClass pseudo)
    {
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

    private static void SkipWhitespace(ReadOnlySpan<char> text, ref int pos)
    {
        while (pos < text.Length && CssTokenReader.IsCssWhitespace(text[pos]))
        {
            pos++;
        }
    }

    private static ReadOnlySpan<char> ReadIdent(ReadOnlySpan<char> text, ref int pos)
    {
        var start = pos;
        while (pos < text.Length && IsIdentChar(text[pos]))
        {
            pos++;
        }

        return text.Slice(start, pos - start);
    }

    private static bool IsIdentStart(ReadOnlySpan<char> text, int pos)
    {
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
        => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c > 0x7F;
}
