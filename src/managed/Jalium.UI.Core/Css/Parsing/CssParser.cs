namespace Jalium.UI.Styling;

/// <summary>
/// Parses style-sheet text into rules following the CSS error-recovery model: a malformed
/// declaration is skipped up to the next ';' at the same nesting level, a malformed
/// selector drops the whole rule, and at-rules are skipped as a balanced block. Never throws.
/// </summary>
internal static partial class CssParser
{
    internal static bool SupportsAtRule(string name) => name.ToLowerInvariant() is "import" or "namespace" or "layer" or "media" or "supports" or "container" or "scope" or "property";
    public static CssStyleSheet Parse(string cssText, string? sourceLabel, int depth = 0, string? parentLayer = null,
        CssNestingContext? nesting = null, CssScopeRule? scope = null, CssSelector[]? declarationSelectors = null, bool styleBlock = false,
        CssNamespaceContext? namespaces = null)
    {
        if (depth > 64)
            return new CssStyleSheet([], [new CssParseDiagnostic(CssDiagnosticSeverity.Warning, "CSS rule nesting limit exceeded", 1)], sourceLabel);
        var diagnostics = new List<CssParseDiagnostic>();
        var rules = new List<CssRule>();
        var imports = new List<CssImport>();
        var layers = new List<CssLayerDeclaration>();
        var properties = new List<CssPropertyRegistration>();
        var importsAllowed = true;
        var hasImports = false;
        var namespacesAllowed=depth==0;
        var hasNamespaces=false;
        namespaces??=new CssNamespaceContext();
        var text = cssText.AsSpan();
        var pos = 0;
        var line = 1;
        var declarationsPending = new List<CssDeclaration>();
        void FlushDeclarations()
        {
            if (declarationSelectors is null || declarationsPending.Count == 0) return;
            rules.Add(new CssRule { Selectors = declarationSelectors, Declarations = declarationsPending.ToArray(), RuleIndex = rules.Count, LayerName = parentLayer, Scope = scope });
            declarationsPending.Clear();
        }

        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos, ref line);
            if (pos >= text.Length)
            {
                FlushDeclarations();
                break;
            }

            var c = text[pos];
            if (c == ';') { pos++; continue; }
            if (declarationSelectors is not null && TryReadNestedDeclaration(text, ref pos, ref line, diagnostics, out var declarations))
            {
                declarationsPending.AddRange(declarations);
                continue;
            }
            FlushDeclarations();
            if (c == '@')
            {
                var atLine = line;
                var atStart = pos;
                var name = SkipAtRule(text, ref pos, ref line, out var headerStart);
                if(name.Equals("namespace",StringComparison.OrdinalIgnoreCase))
                {
                    if(!namespacesAllowed || !namespaces.Declare(text[headerStart..pos]))
                        diagnostics.Add(new(CssDiagnosticSeverity.Warning,"invalid or misplaced @namespace rule",atLine));
                    else {importsAllowed=false; hasNamespaces=true;}
                    continue;
                }
                if (name.Equals("scope", StringComparison.OrdinalIgnoreCase))
                {
                    var brace = headerStart; var bodyLine = atLine;
                    ScanToTopLevelOpenBrace(text, ref brace, ref bodyLine);
                    var end = brace < pos && text[pos - 1] == '}' ? pos - 1 : pos;
                    var definition = brace < end ? CssScopeRule.Parse(text[headerStart..brace].ToString(), nesting, scope,namespaces) : null;
                    if (definition is null)
                    {
                        diagnostics.Add(new(CssDiagnosticSeverity.Warning, "invalid @scope rule", atLine));
                        continue;
                    }
                    importsAllowed = false;
                    namespacesAllowed=false;
                    var direct = CssSelectorParser.ParseGroup("&", out _)!.ToArray();
                    var nested = Parse(text[(brace + 1)..end].ToString(), sourceLabel, depth + 1, parentLayer, scope: definition, declarationSelectors: direct,namespaces:namespaces);
                    foreach (var rule in nested.Rules) { rule.RuleIndex = rules.Count; rules.Add(rule); }
                    layers.AddRange(nested.Layers.Select(entry => entry with { Offset = entry.Offset + brace + 1 }));
                    properties.AddRange(nested.Properties.Select(entry => entry with { Offset = entry.Offset + brace + 1 }));
                    foreach (var diagnostic in nested.Diagnostics) diagnostics.Add(new(diagnostic.Severity, diagnostic.Message, bodyLine + diagnostic.Line - 1));
                    continue;
                }
                if (name.Equals("property", StringComparison.OrdinalIgnoreCase))
                {
                    if (styleBlock) { diagnostics.Add(new(CssDiagnosticSeverity.Warning, "@property cannot be nested directly in a style rule", atLine)); continue; }
                    var brace = headerStart; var bodyLine = atLine;
                    ScanToTopLevelOpenBrace(text, ref brace, ref bodyLine);
                    var end = pos > brace && text[pos - 1] == '}' ? pos - 1 : pos;
                    var property = brace < end ? CssPropertyRegistration.Parse(StripComments(text[headerStart..brace]), text[(brace + 1)..end], atStart) : null;
                    if (property is null) diagnostics.Add(new(CssDiagnosticSeverity.Warning, "invalid @property registration", atLine));
                    else { properties.Add(property with {LayerName=parentLayer}); importsAllowed = false; namespacesAllowed=false; }
                    continue;
                }
                if (name.Equals("layer", StringComparison.OrdinalIgnoreCase))
                {
                    var layerStart = headerStart;
                    if (pos > atStart && text[pos - 1] == ';')
                    {
                        if (styleBlock) { diagnostics.Add(new(CssDiagnosticSeverity.Warning, "a nested @layer requires a block", atLine)); continue; }
                        var nameReader = new CssTokenReader(StripComments(text[layerStart..(pos - 1)]));
                        var names = new List<string>();
                        var validNames = true;
                        do
                        {
                            if (!nameReader.TryReadUntilTopLevelComma(out var value) || CssLayerOrder.NormalizeName(value.ToString()) is not { } parsedName)
                            { validNames = false; break; }
                            names.Add(parsedName);
                            if (nameReader.AtEnd) break;
                            if (!nameReader.TryReadComma() || nameReader.AtEnd) { validNames = false; break; }
                        } while (true);
                        if (validNames && names.Count > 0)
                        {
                            foreach (var layer in names) layers.Add(new(parentLayer is null ? layer : parentLayer + "." + layer, atStart));
                            if (hasImports) importsAllowed = false;
                            if(hasImports || hasNamespaces) namespacesAllowed=false;
                            continue;
                        }
                    }
                    else
                    {
                        var brace = layerStart;
                        var bodyLine = atLine;
                        ScanToTopLevelOpenBrace(text, ref brace, ref bodyLine);
                        if (brace < pos)
                        {
                            var layerText = StripComments(text[layerStart..brace]).Trim().ToString();
                            var anonymous = layerText.Length == 0;
                            var layer = anonymous ? CssLayerOrder.AnonymousName() : CssLayerOrder.NormalizeName(layerText);
                            if (layer is not null)
                            {
                                importsAllowed = false;
                                namespacesAllowed=false;
                                layer = parentLayer is null ? layer : parentLayer + "." + layer;
                                layers.Add(new(layer, atStart, Anonymous: anonymous));
                                var end = text[pos - 1] == '}' ? pos - 1 : pos;
                                var nested = Parse(text[(brace + 1)..end].ToString(), sourceLabel, depth + 1, layer, nesting, scope, declarationSelectors,namespaces:namespaces);
                                foreach (var nestedRule in nested.Rules) { nestedRule.RuleIndex = rules.Count; rules.Add(nestedRule); }
                                layers.AddRange(nested.Layers.Select(entry => entry with { Offset = entry.Offset + brace + 1 }));
                                properties.AddRange(nested.Properties.Select(entry => entry with { Offset = entry.Offset + brace + 1 }));
                                foreach (var diagnostic in nested.Diagnostics)
                                    diagnostics.Add(new CssParseDiagnostic(diagnostic.Severity, diagnostic.Message, bodyLine + diagnostic.Line - 1));
                                continue;
                            }
                        }
                    }
                    diagnostics.Add(new CssParseDiagnostic(CssDiagnosticSeverity.Warning, "invalid @layer rule", atLine));
                    continue;
                }
                if (name.Equals("media", StringComparison.OrdinalIgnoreCase) || name.Equals("supports", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("container", StringComparison.OrdinalIgnoreCase))
                {
                    var brace = atStart;
                    var bodyLine = atLine;
                    ScanToTopLevelOpenBrace(text, ref brace, ref bodyLine);
                    if (brace < pos && brace < text.Length)
                    {
                        var header = text[headerStart..brace].Trim().ToString();
                        if (name.Equals("supports",StringComparison.OrdinalIgnoreCase) && CssSupportsQuery.Parse(header,namespaces:namespaces) is null)
                        {
                            diagnostics.Add(new(CssDiagnosticSeverity.Warning,"invalid @supports condition",atLine));
                            continue;
                        }
                        var container = name.Equals("container", StringComparison.OrdinalIgnoreCase) ? CssContainerQuery.Parse(header) : null;
                        if (name.Equals("container", StringComparison.OrdinalIgnoreCase) && container is null)
                        {
                            diagnostics.Add(new CssParseDiagnostic(CssDiagnosticSeverity.Warning, "invalid @container condition", atLine));
                            continue;
                        }
                        var bodyEnd = pos > brace && text[pos - 1] == '}' ? pos - 1 : pos;
                        importsAllowed = false;
                        namespacesAllowed=false;
                        var nested = Parse(text[(brace + 1)..bodyEnd].ToString(), sourceLabel, depth + 1, parentLayer, nesting, scope, declarationSelectors,namespaces:namespaces);
                        var condition = new CssCondition(name.ToLowerInvariant(), header,Namespaces:namespaces) { ContainerQuery = container };
                        properties.AddRange(nested.Properties.Select(entry => entry with
                        {
                            Offset = entry.Offset + brace + 1,
                            // Name-defining rules are not constrained by an element's container condition.
                            Condition = container is not null ? entry.Condition : entry.Condition is null ? condition : new CssCondition("and", "", condition, entry.Condition),
                        }));
                        layers.AddRange(nested.Layers.Select(entry => entry with
                        {
                            Offset = entry.Offset + brace + 1,
                            Condition = container is not null ? entry.Condition : entry.Condition is null ? condition : new CssCondition("and", "", condition, entry.Condition),
                        }));
                        foreach (var nestedRule in nested.Rules)
                        {
                            nestedRule.Condition = nestedRule.Condition is null ? condition : new CssCondition("and", "", condition, nestedRule.Condition);
                            nestedRule.RuleIndex = rules.Count;
                            rules.Add(nestedRule);
                        }
                        foreach (var diagnostic in nested.Diagnostics)
                            diagnostics.Add(new CssParseDiagnostic(diagnostic.Severity, diagnostic.Message, bodyLine + diagnostic.Line - 1));
                        continue;
                    }
                }
                if (name.Equals("import", StringComparison.OrdinalIgnoreCase) && (!importsAllowed || depth > 0))
                {
                    diagnostics.Add(new(CssDiagnosticSeverity.Warning, "@import must precede style rules and block at-rules", atLine));
                    continue;
                }
                if (name.Equals("import", StringComparison.OrdinalIgnoreCase) && importsAllowed && rules.Count == 0 &&
                    CssImport.TryParse(text[headerStart..pos], atLine, out var import,namespaces))
                {
                    imports.Add(import! with { Offset = atStart });
                    hasImports = true;
                    diagnostics.Add(new CssParseDiagnostic(CssDiagnosticSeverity.Info,
                        "@import is deferred; use CssStyleSheet.LoadAsync to load stylesheet resources", atLine));
                    continue;
                }
                if (name.Equals("import", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new(CssDiagnosticSeverity.Warning,"invalid @import rule",atLine));
                    continue;
                }
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Info, $"at-rule '@{name}' is not supported and was skipped", atLine));
                continue;
            }

            if (c == '}')
            {
                diagnostics.Add(new CssParseDiagnostic(CssDiagnosticSeverity.Warning, "unexpected '}'", line));
                pos++;
                continue;
            }

            var selectorLine = line;
            var selectorStart = pos;
            ScanNestedItem(text, ref pos, ref line, allowCurlyValue: false);
            if (pos >= text.Length || text[pos] != '{')
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning, "expected '{' after a style rule selector", selectorLine));
                if (pos < text.Length) { pos++; continue; }
                break;
            }

            var selectorText = text.Slice(selectorStart, pos - selectorStart).Trim();
            pos++; // consume '{'

            var relative = nesting is not null ? CssRelativeSelectorMode.Nesting : scope is not null ? CssRelativeSelectorMode.Scope : CssRelativeSelectorMode.None;
            var selectors = CssSelectorParser.ParseGroup(selectorText, out var selectorError, nesting, relative,namespaces);
            if (selectors is null)
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning,
                    $"invalid selector '{selectorText.ToString()}' ({selectorError}); rule dropped", selectorLine));
                SkipBlockRemainder(text, ref pos, ref line);
                continue;
            }

            var bodyStart = pos; var startLine = line;
            SkipBlockRemainder(text, ref pos, ref line);
            var styleEnd = pos > bodyStart && text[pos - 1] == '}' ? pos - 1 : pos;
            var group = selectors.ToArray();
            var children = Parse(text[bodyStart..styleEnd].ToString(), sourceLabel, depth + 1, parentLayer,
                new CssNestingContext(group, scope), scope, group, styleBlock: true,namespaces:namespaces);
            foreach (var rule in children.Rules) { rule.RuleIndex = rules.Count; rules.Add(rule); }
            layers.AddRange(children.Layers.Select(entry => entry with { Offset = entry.Offset + bodyStart }));
            properties.AddRange(children.Properties.Select(entry => entry with { Offset = entry.Offset + bodyStart }));
            foreach (var diagnostic in children.Diagnostics) diagnostics.Add(new(diagnostic.Severity, diagnostic.Message, startLine + diagnostic.Line - 1));
            if (pos == text.Length && (pos == 0 || text[pos - 1] != '}')) diagnostics.Add(new(CssDiagnosticSeverity.Warning, "unterminated declaration block", selectorLine));
            importsAllowed = false;
            namespacesAllowed=false;
        }

        return new CssStyleSheet(rules.ToArray(), diagnostics.ToArray(), sourceLabel, imports.ToArray(), layers.ToArray(), properties.ToArray());
    }

    /// <summary>Parses a bare declaration list (the inline Css.Style channel).</summary>
    public static List<CssDeclaration> ParseInlineDeclarations(string text, List<CssParseDiagnostic>? diagnostics)
    {
        var span = text.AsSpan();
        var pos = 0;
        var line = 1;
        var collected = diagnostics ?? new List<CssParseDiagnostic>();
        return ParseDeclarations(span, ref pos, ref line, collected, stopAtCloseBrace: false);
    }

    private static List<CssDeclaration> ParseDeclarationBlock(
        ReadOnlySpan<char> text, ref int pos, ref int line, List<CssParseDiagnostic> diagnostics)
        => ParseDeclarations(text, ref pos, ref line, diagnostics, stopAtCloseBrace: true);

    private static List<CssDeclaration> ParseDeclarations(
        ReadOnlySpan<char> text, ref int pos, ref int line, List<CssParseDiagnostic> diagnostics, bool stopAtCloseBrace)
    {
        var declarations = new List<CssDeclaration>();
        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos, ref line);
            if (pos >= text.Length)
            {
                if (stopAtCloseBrace)
                {
                    diagnostics.Add(new CssParseDiagnostic(
                        CssDiagnosticSeverity.Warning, "unterminated declaration block", line));
                }

                break;
            }

            var c = text[pos];
            if (c == '}' && stopAtCloseBrace)
            {
                pos++;
                break;
            }

            if (c == ';')
            {
                pos++;
                continue;
            }

            var declLine = line;
            CssSyntax.ReadIdentifier(text, ref pos, out var name);
            SkipWhitespaceAndComments(text, ref pos, ref line);
            if (name.IsEmpty || pos >= text.Length || text[pos] != ':')
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning, "malformed declaration; skipped", declLine));
                SkipToDeclarationBoundary(text, ref pos, ref line, stopAtCloseBrace);
                continue;
            }

            pos++; // consume ':'
            SkipWhitespaceAndComments(text, ref pos, ref line);
            var valueStart = pos;
            ScanValue(text, ref pos, ref line, stopAtCloseBrace);
            var rawValue = StripComments(text.Slice(valueStart, pos - valueStart)).Trim();

            var important = false;
            if (TryStripImportant(rawValue, out var stripped))
            {
                important = true;
                rawValue = stripped;
            }

            if (rawValue.IsEmpty && !name.StartsWith("--", StringComparison.Ordinal))
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning, $"declaration '{name.ToString()}' has an empty value; skipped", declLine));
                continue;
            }

            declarations.Add(new CssDeclaration
            {
                PropertyName = name.StartsWith("--", StringComparison.Ordinal)
                    ? name.ToString() : name.ToString().ToLowerInvariant(),
                RawValue = rawValue.ToString(),
                Important = important,
            });
        }

        return declarations;
    }

    /// <summary>Scans a declaration value up to the next same-level ';' or the block's closing '}'.</summary>
    private static void ScanValue(ReadOnlySpan<char> text, ref int pos, ref int line, bool stopAtCloseBrace)
    {
        var depth = 0;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\\' && CssSyntax.ReadEscape(text, ref pos, out _)) continue;
            if (c == '\n')
            {
                line++;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' || c == '}' && depth > 0)
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }
            else if (depth == 0 && (c == ';' || (stopAtCloseBrace && c == '}')))
            {
                return;
            }

            pos++;
        }
    }

    private static void SkipToDeclarationBoundary(ReadOnlySpan<char> text, ref int pos, ref int line, bool stopAtCloseBrace)
    {
        var depth = 0;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
            }
            else if (c == '(' || c == '{' && !stopAtCloseBrace)
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (depth == 0)
            {
                if (c == ';')
                {
                    pos++;
                    return;
                }

                if (stopAtCloseBrace && c == '}')
                {
                    return;
                }
            }

            pos++;
        }
    }

    /// <summary>Replaces comments with a single space (string-aware). Returns the input span when comment-free.</summary>
    internal static ReadOnlySpan<char> StripComments(ReadOnlySpan<char> text)
    {
        if (text.IndexOf("/*", StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = i + 2;
                while (end < text.Length && !(text[end] == '*' && end + 1 < text.Length && text[end + 1] == '/'))
                {
                    end++;
                }

                i = end < text.Length ? end + 2 : text.Length;
                sb.Append(' ');
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var end = CssTokenReader.SkipString(text, i);
                sb.Append(text.Slice(i, end - i));
                i = end;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    internal static bool TryStripImportant(ReadOnlySpan<char> value, out ReadOnlySpan<char> stripped)
    {
        var bang = value.LastIndexOf('!');
        if (bang >= 0 && value.Slice(bang + 1).Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            stripped = value.Slice(0, bang).TrimEnd();
            return true;
        }

        stripped = value;
        return false;
    }

    private static void ScanToTopLevelOpenBrace(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\\' && CssSyntax.ReadEscape(text, ref pos, out _)) continue;
            if (c == '{')
            {
                return;
            }

            if (c == '\n')
            {
                line++;
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }

            pos++;
        }
    }

    /// <summary>Consumes the remainder of a rule's block after its '{' (used when a rule is dropped).</summary>
    private static void SkipBlockRemainder(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        var depth = 1;
        while (pos < text.Length && depth > 0)
        {
            var c = text[pos];
            if (c == '\\' && CssSyntax.ReadEscape(text, ref pos, out _)) continue;
            if (c == '\n')
            {
                line++;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }

            pos++;
        }
    }

    /// <summary>Skips an at-rule: either up to a top-level ';' (block-less) or over its balanced { } block.</summary>
    private static string SkipAtRule(ReadOnlySpan<char> text, ref int pos, ref int line, out int headerStart)
    {
        pos++; // consume '@'
        var nameStart = pos;
        CssSyntax.ReadIdentifier(text, ref pos, out var identifier);
        headerStart = pos;
        var name = identifier.ToString();
        var parentheses = 0;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
            }
            else if (c == '(') parentheses++;
            else if (c == ')') parentheses = Math.Max(0, parentheses - 1);
            else if (c == '\\' && CssSyntax.ReadEscape(text, ref pos, out _)) continue;
            else if (c == ';' && parentheses == 0)
            {
                pos++;
                return name;
            }
            else if (c == '{' && parentheses == 0)
            {
                pos++;
                SkipBlockRemainder(text, ref pos, ref line);
                return name;
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }

            pos++;
        }

        return name;
    }

    private static void SkipWhitespaceAndComments(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
                pos++;
            }
            else if (CssTokenReader.IsCssWhitespace(c))
            {
                pos++;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
            }
            else
            {
                break;
            }
        }
    }

    private static void SkipComment(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        pos += 2;
        while (pos < text.Length)
        {
            if (text[pos] == '\n')
            {
                line++;
            }
            else if (text[pos] == '*' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                pos += 2;
                return;
            }

            pos++;
        }
    }

    private static bool IsIdentChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c > 0x7F;
}
