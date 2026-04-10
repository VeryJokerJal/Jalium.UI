using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Jalium.UI.Markup;

/// <summary>
/// AOT-safe code block interpreter that replaces Roslyn-based <c>CSharpScript.EvaluateAsync</c>
/// for expanding <c>@{ }</c> code blocks mixed with XML markup.
/// Interprets C# statements (for, foreach, while, do-while, if/else, switch, try/catch,
/// variable declarations, assignments) and emits XML markup to a StringBuilder.
/// </summary>
internal static class RazorLightweightCodeBlockInterpreter
{
    /// <summary>
    /// Expands a code block by interpreting C# code and emitting XML markup.
    /// This is a drop-in replacement for the Roslyn-based <c>ExecuteScript</c>.
    /// </summary>
    public static string Expand(List<RazorCodeBlockPreprocessor.CodeSegment> segments)
    {
        // Merge all segments back into a single code string where markup
        // segments are preserved inline. This keeps for/while/if bodies intact
        // even when they contain XML elements.
        var mergedCode = new StringBuilder();
        foreach (var seg in segments)
            mergedCode.Append(seg.Text);

        var output = new StringBuilder();
        var scope = new InterpreterScope();
        InjectBuiltins(scope, output);
        InterpretMixedCode(mergedCode.ToString(), output, scope);
        return output.ToString();
    }

    /// <summary>
    /// Expands a code block with an external variable resolver and returns the scope
    /// so that variables defined in the code block are accessible to subsequent segments.
    /// </summary>
    public static (string Output, Func<string, object?> Resolver) ExpandWithScope(
        string code, Func<string, object?> externalResolver)
    {
        var output = new StringBuilder();
        var scope = new InterpreterScope(externalResolver);
        InjectBuiltins(scope, output);
        InterpretMixedCode(code, output, scope);
        return (output.ToString(), scope.Resolve);
    }

    /// <summary>
    /// Registers <c>Write()</c> and <c>WriteLiteral()</c> helper functions in the scope
    /// so code blocks can emit text output programmatically.
    /// </summary>
    private static void InjectBuiltins(InterpreterScope scope, StringBuilder output)
    {
        scope.Set("Write", new Func<object?[], object?>(args =>
        {
            foreach (var arg in args)
                output.Append(arg?.ToString() ?? "");
            return null;
        }));
        scope.Set("WriteLiteral", new Func<object?[], object?>(args =>
        {
            foreach (var arg in args)
                output.Append(arg?.ToString() ?? "");
            return null;
        }));
    }

    /// <summary>
    /// Interprets mixed C#/XML code where XML elements at statement level are emitted as output.
    /// Control flow (for, foreach, while, if, switch, etc.) is handled inline so that
    /// loop bodies containing XML elements work correctly.
    /// </summary>
    private static void InterpretMixedCode(string code, StringBuilder output, InterpreterScope scope)
    {
        var pos = 0;
        var flow = new FlowSignal();
        InterpretMixedCodeRange(code, ref pos, output, scope, flow);
    }

    private static void InterpretMixedCodeRange(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        while (pos < code.Length && flow.Kind == Signal.None)
        {
            SkipWhitespace(code, ref pos);
            if (pos >= code.Length) break;

            // XML element at statement level → emit as markup
            if (code[pos] == '<' && pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
            {
                var elementEnd = ReadXmlElement(code, pos);
                EmitMarkup(code[pos..elementEnd], output, scope);
                pos = elementEnd;
                continue;
            }

            // @{ inline code } inside mixed code (e.g. inside loop bodies from merged blocks)
            if (code[pos] == '@' && pos + 1 < code.Length && code[pos + 1] == '{')
            {
                var codeStart = pos + 2;
                var end = FindMatchingChar(code, codeStart, '{', '}');
                if (end >= 0)
                {
                    var inlineCode = code[codeStart..end];
                    ExecuteCode(inlineCode, output, scope);
                    pos = end + 1;
                    continue;
                }
            }

            // Try to match control flow keywords with block bodies
            if (TryMatchKeyword(code, pos, "for", out var kwEnd) && !TryMatchKeyword(code, pos, "foreach", out _))
            {
                pos = kwEnd;
                InterpretFor(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "foreach", out kwEnd))
            {
                pos = kwEnd;
                InterpretForeach(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "while", out kwEnd))
            {
                pos = kwEnd;
                InterpretWhile(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "do", out kwEnd))
            {
                pos = kwEnd;
                InterpretDoWhile(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "if", out kwEnd))
            {
                pos = kwEnd;
                InterpretIfMixed(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "switch", out kwEnd))
            {
                pos = kwEnd;
                InterpretSwitchMixed(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "try", out kwEnd))
            {
                pos = kwEnd;
                InterpretTryCatchMixed(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "using", out kwEnd))
            {
                pos = kwEnd;
                InterpretUsingMixed(code, ref pos, output, scope, flow);
                continue;
            }
            if (TryMatchKeyword(code, pos, "lock", out kwEnd))
            {
                pos = kwEnd;
                var lockObj = ReadParenthesized(code, ref pos);
                var lockVal = RazorLightweightExpressionEvaluator.Evaluate(lockObj.Trim(), scope.Resolve);
                var body = ReadBraceBody(code, ref pos);
                if (lockVal != null)
                {
                    lock (lockVal)
                    {
                        var bodyPos = 0;
                        InterpretMixedCodeRange(body, ref bodyPos, output, scope, flow);
                    }
                }
                else
                {
                    var bodyPos = 0;
                    InterpretMixedCodeRange(body, ref bodyPos, output, scope, flow);
                }
                continue;
            }
            if (TryMatchKeyword(code, pos, "section", out kwEnd))
            {
                pos = kwEnd;
                InterpretSection(code, ref pos, output, scope);
                continue;
            }
            if (TryMatchKeyword(code, pos, "break", out kwEnd))
            {
                pos = kwEnd; SkipSemicolon(code, ref pos);
                flow.Kind = Signal.Break;
                continue;
            }
            if (TryMatchKeyword(code, pos, "continue", out kwEnd))
            {
                pos = kwEnd; SkipSemicolon(code, ref pos);
                flow.Kind = Signal.Continue;
                continue;
            }
            // goto label; — skip to label
            if (TryMatchKeyword(code, pos, "goto", out kwEnd))
            {
                pos = kwEnd; SkipWhitespace(code, ref pos);
                var labelStart = pos;
                while (pos < code.Length && code[pos] != ';') pos++;
                var label = code[labelStart..pos].Trim();
                if (pos < code.Length && code[pos] == ';') pos++;
                // Search for label: in the current code block
                var labelTarget = code.IndexOf(label + ":", pos, StringComparison.Ordinal);
                if (labelTarget >= 0) pos = labelTarget + label.Length + 1;
                continue;
            }
            // checked { ... } / unchecked { ... } — execute body as-is
            if (TryMatchKeyword(code, pos, "checked", out kwEnd) || TryMatchKeyword(code, pos, "unchecked", out kwEnd))
            {
                pos = kwEnd; SkipWhitespace(code, ref pos);
                if (pos < code.Length && code[pos] == '{')
                {
                    var body = ReadBraceBody(code, ref pos);
                    var bodyPos = 0;
                    InterpretMixedCodeRange(body, ref bodyPos, output, scope, flow);
                }
                continue;
            }
            // await foreach / await using / await expr
            if (TryMatchKeyword(code, pos, "await", out kwEnd))
            {
                var afterAwait = kwEnd;
                SkipWhitespace(code, ref afterAwait);
                if (TryMatchKeyword(code, afterAwait, "foreach", out var foreachEnd))
                {
                    pos = foreachEnd;
                    InterpretAwaitForeach(code, ref pos, output, scope, flow);
                    continue;
                }
                if (TryMatchKeyword(code, afterAwait, "using", out var usingEnd))
                {
                    pos = usingEnd;
                    InterpretAwaitUsingMixed(code, ref pos, output, scope, flow);
                    continue;
                }
                // General await expression statement: await SomeTask();
                pos = kwEnd;
                SkipWhitespace(code, ref pos);
                var awaitStmtStart = pos;
                pos = ReadSimpleStatement(code, pos);
                var awaitExpr = code[awaitStmtStart..pos].TrimEnd(';').Trim();
                if (!string.IsNullOrEmpty(awaitExpr))
                {
                    var awaitResult = RazorLightweightExpressionEvaluator.Evaluate(awaitExpr, scope.Resolve);
                    RazorExpressionParser.UnwrapAwaitable(awaitResult);
                }
                continue;
            }
            // async local function: async Type Name(...) { ... }
            if (TryMatchKeyword(code, pos, "async", out kwEnd))
            {
                pos = kwEnd;
                SkipWhitespace(code, ref pos);
                // Read the function definition using the statement parser
                InterpretAsyncLocalFunction(code, ref pos, output, scope);
                continue;
            }

            // Label: identifier followed by : (e.g. "myLabel:")
            if (char.IsLetter(code[pos]) || code[pos] == '_')
            {
                var labelCheck = pos;
                while (labelCheck < code.Length && (char.IsLetterOrDigit(code[labelCheck]) || code[labelCheck] == '_'))
                    labelCheck++;
                while (labelCheck < code.Length && char.IsWhiteSpace(code[labelCheck])) labelCheck++;
                if (labelCheck < code.Length && code[labelCheck] == ':' && labelCheck + 1 < code.Length && code[labelCheck + 1] != ':')
                {
                    // It's a label — skip it
                    pos = labelCheck + 1;
                    continue;
                }
            }

            // Simple C# statement (no XML inside) — read until ;
            var stmtStart = pos;
            pos = ReadSimpleStatement(code, pos);
            var stmt = code[stmtStart..pos].Trim();
            if (!string.IsNullOrEmpty(stmt))
            {
                var tokens = new RazorTokenizer(stmt).Tokenize();
                var stmtFlow = new FlowSignal();
                ExecuteStatements(tokens, 0, output, scope, stmtFlow);
                if (stmtFlow.Kind != Signal.None) { flow.Kind = stmtFlow.Kind; flow.ReturnValue = stmtFlow.ReturnValue; }
            }
        }
    }

    private static bool TryMatchKeyword(string code, int pos, string keyword, out int afterKeyword)
    {
        afterKeyword = pos;
        if (pos + keyword.Length > code.Length) return false;
        if (!code.AsSpan(pos, keyword.Length).SequenceEqual(keyword)) return false;
        var after = pos + keyword.Length;
        if (after < code.Length && char.IsLetterOrDigit(code[after])) return false;
        afterKeyword = after;
        return true;
    }

    private static void SkipSemicolon(string code, ref int pos)
    {
        SkipWhitespace(code, ref pos);
        if (pos < code.Length && code[pos] == ';') pos++;
    }

    private static int ReadSimpleStatement(string code, int pos)
    {
        var inStr = false; var esc = false; char sq = '\0';
        while (pos < code.Length)
        {
            var c = code[pos];
            if (esc) { esc = false; pos++; continue; }
            if (inStr) { if (c == '\\') esc = true; else if (c == sq) inStr = false; pos++; continue; }
            if (c == '"' || c == '\'') { inStr = true; sq = c; pos++; continue; }
            if (c == ';') { pos++; return pos; }
            pos++;
        }
        return pos;
    }

    private static string ReadParenthesized(string code, ref int pos)
    {
        SkipWhitespace(code, ref pos);
        if (pos >= code.Length || code[pos] != '(') return "";
        var start = pos + 1;
        var end = FindMatchingChar(code, start, '(', ')');
        if (end < 0) { pos = code.Length; return ""; }
        var content = code[start..end];
        pos = end + 1;
        return content;
    }

    private static string ReadBraceBody(string code, ref int pos)
    {
        SkipWhitespace(code, ref pos);
        if (pos >= code.Length || code[pos] != '{') return "";
        var start = pos + 1;
        var end = FindMatchingChar(code, start, '{', '}');
        if (end < 0) { pos = code.Length; return ""; }
        var body = code[start..end];
        pos = end + 1;
        return body;
    }

    // ─── Control flow interpreters (mixed code/XML) ───

    private static void InterpretFor(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var header = ReadParenthesized(code, ref pos);
        var body = ReadBraceBody(code, ref pos);
        var parts = SplitForHeader(header);
        if (parts.Length != 3) return;

        var child = scope.CreateChild();
        var initCode = parts[0].Trim() + ";";
        var condExpr = parts[1].Trim();
        var incrCode = parts[2].Trim() + ";";

        ExecutePureCode(initCode, output, child);

        var limit = 10000;
        while (limit-- > 0)
        {
            var cond = RazorLightweightExpressionEvaluator.Evaluate(condExpr, child.Resolve);
            if (!RazorExpressionParser.IsTruthy(cond)) break;

            var bodyFlow = new FlowSignal();
            var bodyPos = 0;
            InterpretMixedCodeRange(body, ref bodyPos, output, child, bodyFlow);

            if (bodyFlow.Kind == Signal.Break) break;
            if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; break; }

            ExecutePureCode(incrCode, output, child);
        }
    }

    private static void InterpretForeach(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var header = ReadParenthesized(code, ref pos);
        var body = ReadBraceBody(code, ref pos);

        var inIdx = header.IndexOf(" in ", StringComparison.Ordinal);
        if (inIdx < 0) return;
        var varPart = header[..inIdx].Trim();
        var collExpr = header[(inIdx + 4)..].Trim();
        var collection = RazorLightweightExpressionEvaluator.Evaluate(collExpr, scope.Resolve);

        // Check for deconstruction: foreach (var (a, b) in collection)
        var isDeconstruct = varPart.Contains('(');
        List<string>? deconstructNames = null;
        string? varName = null;

        if (isDeconstruct)
        {
            // Extract names from (a, b) or (a, b, c)
            var parenStart = varPart.IndexOf('(');
            var parenEnd = varPart.LastIndexOf(')');
            if (parenStart >= 0 && parenEnd > parenStart)
            {
                deconstructNames = varPart[(parenStart + 1)..parenEnd]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim()).ToList();
            }
        }
        else
        {
            varName = varPart.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
        }

        if (collection is IEnumerable enumerable)
        {
            var child = scope.CreateChild();
            foreach (var item in enumerable)
            {
                if (deconstructNames != null)
                {
                    // Deconstruct tuple fields into named variables
                    var itemType = item?.GetType();
                    for (var i = 0; i < deconstructNames.Count; i++)
                    {
                        var field = itemType?.GetField($"Item{i + 1}");
                        child.Set(deconstructNames[i], field?.GetValue(item));
                    }
                }
                else
                {
                    child.Set(varName!, item);
                }
                var bodyFlow = new FlowSignal();
                var bodyPos = 0;
                InterpretMixedCodeRange(body, ref bodyPos, output, child, bodyFlow);
                if (bodyFlow.Kind == Signal.Break) break;
                if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; break; }
            }
        }
    }

    private static void InterpretWhile(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var condExpr = ReadParenthesized(code, ref pos).Trim();
        var body = ReadBraceBody(code, ref pos);

        var limit = 10000;
        while (limit-- > 0)
        {
            var cond = RazorLightweightExpressionEvaluator.Evaluate(condExpr, scope.Resolve);
            if (!RazorExpressionParser.IsTruthy(cond)) break;
            var bodyFlow = new FlowSignal();
            var bodyPos = 0;
            InterpretMixedCodeRange(body, ref bodyPos, output, scope, bodyFlow);
            if (bodyFlow.Kind == Signal.Break) break;
            if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; break; }
        }
    }

    private static void InterpretDoWhile(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var body = ReadBraceBody(code, ref pos);
        SkipWhitespace(code, ref pos);
        // skip 'while'
        if (TryMatchKeyword(code, pos, "while", out var kw)) pos = kw;
        var condExpr = ReadParenthesized(code, ref pos).Trim();
        SkipSemicolon(code, ref pos);

        var limit = 10000;
        do
        {
            var bodyFlow = new FlowSignal();
            var bodyPos = 0;
            InterpretMixedCodeRange(body, ref bodyPos, output, scope, bodyFlow);
            if (bodyFlow.Kind == Signal.Break) break;
            if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; break; }

            var cond = RazorLightweightExpressionEvaluator.Evaluate(condExpr, scope.Resolve);
            if (!RazorExpressionParser.IsTruthy(cond)) break;
        } while (limit-- > 0);
    }

    private static void InterpretIfMixed(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var condExpr = ReadParenthesized(code, ref pos);
        var body = ReadBraceBody(code, ref pos);
        var cond = RazorLightweightExpressionEvaluator.Evaluate(condExpr.Trim(), scope.Resolve);

        if (RazorExpressionParser.IsTruthy(cond))
        {
            var bodyPos = 0;
            InterpretMixedCodeRange(body, ref bodyPos, output, scope, flow);
            // Skip else branches
            SkipWhitespace(code, ref pos);
            while (TryMatchKeyword(code, pos, "else", out var elseEnd))
            {
                pos = elseEnd; SkipWhitespace(code, ref pos);
                if (TryMatchKeyword(code, pos, "if", out var ifEnd))
                {
                    pos = ifEnd;
                    ReadParenthesized(code, ref pos);
                }
                ReadBraceBody(code, ref pos);
                SkipWhitespace(code, ref pos);
            }
        }
        else
        {
            SkipWhitespace(code, ref pos);
            if (TryMatchKeyword(code, pos, "else", out var elseEnd))
            {
                pos = elseEnd; SkipWhitespace(code, ref pos);
                if (TryMatchKeyword(code, pos, "if", out var ifEnd))
                {
                    pos = ifEnd;
                    InterpretIfMixed(code, ref pos, output, scope, flow);
                }
                else
                {
                    var elseBody = ReadBraceBody(code, ref pos);
                    var bp = 0;
                    InterpretMixedCodeRange(elseBody, ref bp, output, scope, flow);
                }
            }
        }
    }

    private static void InterpretSwitchMixed(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var switchExpr = ReadParenthesized(code, ref pos);
        var switchVal = RazorLightweightExpressionEvaluator.Evaluate(switchExpr.Trim(), scope.Resolve);
        var body = ReadBraceBody(code, ref pos);

        // Parse case/default labels and find the matching branch
        var bodyPos = 0;
        var matched = false;
        var defaultBody = (string?)null;

        while (bodyPos < body.Length)
        {
            SkipWhitespace(body, ref bodyPos);
            if (bodyPos >= body.Length) break;

            if (TryMatchKeyword(body, bodyPos, "case", out var caseEnd))
            {
                bodyPos = caseEnd;
                SkipWhitespace(body, ref bodyPos);
                // Read case value until ':'
                var caseValStart = bodyPos;
                while (bodyPos < body.Length && body[bodyPos] != ':') bodyPos++;
                var caseValStr = body[caseValStart..bodyPos].Trim();
                if (bodyPos < body.Length) bodyPos++; // skip :

                // Read case body until next case/default/break
                var caseBodyStart = bodyPos;
                var caseBodyEnd = FindCaseEnd(body, bodyPos);
                var caseBody = body[caseBodyStart..caseBodyEnd].Trim();

                // Remove trailing 'break;'
                if (caseBody.EndsWith("break;")) caseBody = caseBody[..^6].Trim();
                else if (caseBody.EndsWith("break")) caseBody = caseBody[..^5].Trim();

                bodyPos = caseBodyEnd;
                // Skip 'break;' in body stream
                SkipWhitespace(body, ref bodyPos);
                if (TryMatchKeyword(body, bodyPos, "break", out var bk)) { bodyPos = bk; SkipSemicolon(body, ref bodyPos); }

                if (!matched)
                {
                    var caseVal = RazorLightweightExpressionEvaluator.Evaluate(caseValStr, scope.Resolve);
                    if (Equals(switchVal?.ToString(), caseVal?.ToString()))
                    {
                        matched = true;
                        var bp = 0;
                        InterpretMixedCodeRange(caseBody, ref bp, output, scope, flow);
                    }
                }
            }
            else if (TryMatchKeyword(body, bodyPos, "default", out var defEnd))
            {
                bodyPos = defEnd;
                SkipWhitespace(body, ref bodyPos);
                if (bodyPos < body.Length && body[bodyPos] == ':') bodyPos++;
                var defStart = bodyPos;
                var defBodyEnd = FindCaseEnd(body, bodyPos);
                defaultBody = body[defStart..defBodyEnd].Trim();
                if (defaultBody.EndsWith("break;")) defaultBody = defaultBody[..^6].Trim();
                else if (defaultBody.EndsWith("break")) defaultBody = defaultBody[..^5].Trim();
                bodyPos = defBodyEnd;
                SkipWhitespace(body, ref bodyPos);
                if (TryMatchKeyword(body, bodyPos, "break", out var bk2)) { bodyPos = bk2; SkipSemicolon(body, ref bodyPos); }
            }
            else bodyPos++;
        }

        if (!matched && defaultBody != null)
        {
            var bp = 0;
            InterpretMixedCodeRange(defaultBody, ref bp, output, scope, flow);
        }
    }

    private static int FindCaseEnd(string body, int pos)
    {
        var depth = 0;
        var inStr = false; var esc = false; char sq = '\0';
        while (pos < body.Length)
        {
            var c = body[pos];
            if (esc) { esc = false; pos++; continue; }
            if (inStr) { if (c == '\\') esc = true; else if (c == sq) inStr = false; pos++; continue; }
            if (c == '"' || c == '\'') { inStr = true; sq = c; pos++; continue; }
            if (c == '{') depth++;
            else if (c == '}') { if (depth == 0) return pos; depth--; }
            else if (depth == 0 && TryMatchKeyword(body, pos, "case", out _)) return pos;
            else if (depth == 0 && TryMatchKeyword(body, pos, "default", out _)) return pos;
            else if (depth == 0 && TryMatchKeyword(body, pos, "break", out _)) return pos;
            pos++;
        }
        return pos;
    }

    private static void InterpretTryCatchMixed(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var tryBody = ReadBraceBody(code, ref pos);

        // Collect all catch blocks and optional finally block
        var catches = new List<(string? typeName, string? varName, string? whenExpr, string body)>();
        string? finallyBody = null;

        SkipWhitespace(code, ref pos);
        while (TryMatchKeyword(code, pos, "catch", out var ce))
        {
            pos = ce; SkipWhitespace(code, ref pos);
            string? catchType = null, catchVar = null, whenExpr = null;
            if (pos < code.Length && code[pos] == '(')
            {
                var header = ReadParenthesized(code, ref pos);
                var parts = header.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) { catchType = parts[0]; catchVar = parts[^1]; }
                else if (parts.Length == 1) { catchType = parts[0]; catchVar = "ex"; }
            }
            // when filter
            SkipWhitespace(code, ref pos);
            if (TryMatchKeyword(code, pos, "when", out var whenEnd))
            {
                pos = whenEnd;
                whenExpr = ReadParenthesized(code, ref pos);
            }
            var catchBody = ReadBraceBody(code, ref pos);
            catches.Add((catchType, catchVar, whenExpr, catchBody));
            SkipWhitespace(code, ref pos);
        }
        if (TryMatchKeyword(code, pos, "finally", out var fe))
        {
            pos = fe;
            finallyBody = ReadBraceBody(code, ref pos);
        }

        try
        {
            var bp = 0;
            InterpretMixedCodeRange(tryBody, ref bp, output, scope, flow);
        }
        catch (Exception ex)
        {
            var handled = false;
            foreach (var (typeName, varName, whenExpr, catchBody) in catches)
            {
                // Check type match
                if (typeName != null)
                {
                    var catchType = RazorExpressionParser.ResolveWellKnownType(typeName);
                    if (catchType != null && !catchType.IsInstanceOfType(ex)) continue;
                }
                // Check when filter
                if (whenExpr != null)
                {
                    var whenScope = scope.CreateChild();
                    if (varName != null) whenScope.Set(varName, ex);
                    var whenResult = RazorLightweightExpressionEvaluator.Evaluate(whenExpr.Trim(), whenScope.Resolve);
                    if (!RazorExpressionParser.IsTruthy(whenResult)) continue;
                }
                // Execute matching catch block
                var catchScope = scope.CreateChild();
                if (varName != null) catchScope.Set(varName, ex);
                var cbp = 0;
                InterpretMixedCodeRange(catchBody, ref cbp, output, catchScope, flow);
                handled = true;
                break;
            }
            if (!handled) throw;
        }
        finally
        {
            if (finallyBody != null)
            {
                var fp = 0;
                var finallyFlow = new FlowSignal();
                InterpretMixedCodeRange(finallyBody, ref fp, output, scope, finallyFlow);
            }
        }
    }

    private static void InterpretUsingMixed(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var header = ReadParenthesized(code, ref pos);
        var body = ReadBraceBody(code, ref pos);

        // Parse "var name = expr" from the using header
        var child = scope.CreateChild();
        IDisposable? disposable = null;
        var eqIdx = header.IndexOf('=');
        if (eqIdx >= 0)
        {
            var left = header[..eqIdx].Trim();
            var varName = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            var initExpr = header[(eqIdx + 1)..].Trim();
            var initVal = RazorLightweightExpressionEvaluator.Evaluate(initExpr, child.Resolve);
            child.Set(varName, initVal);
            disposable = initVal as IDisposable;
        }

        try
        {
            var bodyPos = 0;
            InterpretMixedCodeRange(body, ref bodyPos, output, child, flow);
        }
        finally
        {
            disposable?.Dispose();
        }
    }

    private static void InterpretAwaitUsingMixed(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var header = ReadParenthesized(code, ref pos);
        var body = ReadBraceBody(code, ref pos);

        var child = scope.CreateChild();
        object? resource = null;
        var eqIdx = header.IndexOf('=');
        if (eqIdx >= 0)
        {
            var left = header[..eqIdx].Trim();
            var varName = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            var initExpr = header[(eqIdx + 1)..].Trim();
            var initVal = RazorLightweightExpressionEvaluator.Evaluate(initExpr, child.Resolve);
            // Unwrap if the initializer is a Task
            resource = RazorExpressionParser.UnwrapAwaitable(initVal) ?? initVal;
            child.Set(varName, resource);
        }

        try
        {
            var bodyPos = 0;
            InterpretMixedCodeRange(body, ref bodyPos, output, child, flow);
        }
        finally
        {
            // Dispose: IAsyncDisposable first, then IDisposable
            if (resource is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else if (resource is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static void InterpretAwaitForeach(string code, ref int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var header = ReadParenthesized(code, ref pos);
        var body = ReadBraceBody(code, ref pos);

        var inIdx = header.IndexOf(" in ", StringComparison.Ordinal);
        if (inIdx < 0) return;
        var varName = header[..inIdx].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
        var collExpr = header[(inIdx + 4)..].Trim();
        var collection = RazorLightweightExpressionEvaluator.Evaluate(collExpr, scope.Resolve);

        // Handle IAsyncEnumerable<T> by synchronously draining it
        if (collection != null)
        {
            var asyncEnumType = collection.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IAsyncEnumerable`1");

            if (asyncEnumType != null)
            {
                // Call GetAsyncEnumerator() and iterate
                var getEnumerator = asyncEnumType.GetMethod("GetAsyncEnumerator");
                if (getEnumerator != null)
                {
                    var enumerator = getEnumerator.Invoke(collection, new object[] { default(System.Threading.CancellationToken) });
                    if (enumerator != null)
                    {
                        var moveNextAsync = enumerator.GetType().GetMethod("MoveNextAsync");
                        var currentProp = enumerator.GetType().GetProperty("Current");
                        if (moveNextAsync != null && currentProp != null)
                        {
                            var child = scope.CreateChild();
                            while (true)
                            {
                                var moveResult = moveNextAsync.Invoke(enumerator, null);
                                bool hasNext;
                                if (moveResult is System.Threading.Tasks.ValueTask<bool> vt)
                                    hasNext = vt.AsTask().GetAwaiter().GetResult();
                                else if (moveResult is System.Threading.Tasks.Task<bool> t)
                                    hasNext = t.GetAwaiter().GetResult();
                                else break;

                                if (!hasNext) break;

                                child.Set(varName, currentProp.GetValue(enumerator));
                                var bodyFlow = new FlowSignal();
                                var bodyPos = 0;
                                InterpretMixedCodeRange(body, ref bodyPos, output, child, bodyFlow);
                                if (bodyFlow.Kind == Signal.Break) break;
                                if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; break; }
                            }

                            // Dispose if IAsyncDisposable
                            if (enumerator is IAsyncDisposable asyncDisposable)
                                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        }
                    }
                }
                return;
            }
        }

        // Fallback: treat as regular foreach for IEnumerable
        if (collection is System.Collections.IEnumerable enumerable)
        {
            var child = scope.CreateChild();
            foreach (var item in enumerable)
            {
                child.Set(varName, item);
                var bodyFlow = new FlowSignal();
                var bodyPos = 0;
                InterpretMixedCodeRange(body, ref bodyPos, output, child, bodyFlow);
                if (bodyFlow.Kind == Signal.Break) break;
                if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; break; }
            }
        }
    }

    private static void InterpretAsyncLocalFunction(string code, ref int pos, StringBuilder output, InterpreterScope scope)
    {
        // Parse: async ReturnType FunctionName(params) { body }
        // Skip return type (may have dots and generic args like System.Collections.Generic.IAsyncEnumerable<string>)
        var typeStart = pos;
        // Read until we find an identifier followed by (
        while (pos < code.Length)
        {
            SkipWhitespace(code, ref pos);
            if (pos >= code.Length) return;

            // Skip generic type parameters <...>
            if (code[pos] == '<')
            {
                var end = FindMatchingChar(code, pos + 1, '<', '>');
                if (end >= 0) { pos = end + 1; continue; }
            }

            // Check if this identifier is followed by (
            var idStart = pos;
            while (pos < code.Length && (char.IsLetterOrDigit(code[pos]) || code[pos] == '_' || code[pos] == '.'))
                pos++;
            var idLen = pos - idStart;
            if (idLen == 0) { pos++; continue; }

            SkipWhitespace(code, ref pos);
            if (pos < code.Length && code[pos] == '(')
            {
                // Found function name
                var funcName = code[idStart..(idStart + idLen)];
                // Read parameter list
                var paramHeader = ReadParenthesized(code, ref pos);
                var paramNames = new List<string>();
                if (!string.IsNullOrWhiteSpace(paramHeader))
                {
                    foreach (var param in paramHeader.Split(','))
                    {
                        var parts = param.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) paramNames.Add(parts[^1]);
                    }
                }

                SkipWhitespace(code, ref pos);
                if (pos < code.Length && code[pos] == '{')
                {
                    var funcBody = ReadBraceBody(code, ref pos);
                    var capturedScope = scope;

                    // Check if body contains yield return → IAsyncEnumerable
                    if (funcBody.Contains("yield return", StringComparison.Ordinal))
                    {
                        scope.Set(funcName, new Func<object?[], object?>(args =>
                        {
                            return CreateAsyncEnumerable(funcBody, paramNames, args, capturedScope);
                        }));
                    }
                    else
                    {
                        // Regular async function — interpret body synchronously
                        scope.Set(funcName, new Func<object?[], object?>(args =>
                        {
                            var fnScope = capturedScope.CreateChild();
                            for (var i = 0; i < Math.Min(paramNames.Count, args.Length); i++)
                                fnScope.Set(paramNames[i], args[i]);
                            var fnOutput = new StringBuilder();
                            var fnFlow = new FlowSignal();
                            InterpretMixedCode(funcBody, fnOutput, fnScope);
                            if (fnFlow.Kind == Signal.Return) return fnFlow.ReturnValue;
                            return fnOutput.Length > 0 ? fnOutput.ToString() : null;
                        }));
                    }
                }
                return;
            }
        }
    }

    private static object CreateAsyncEnumerable(string body, List<string> paramNames, object?[] args, InterpreterScope parentScope)
    {
        // Parse yield return statements from the body and collect values
        var values = new List<object?>();
        var scope = parentScope.CreateChild();
        for (var i = 0; i < Math.Min(paramNames.Count, args.Length); i++)
            scope.Set(paramNames[i], args[i]);

        // Simple yield return extraction: scan for "yield return expr;"
        var pos = 0;
        while (pos < body.Length)
        {
            SkipWhitespace(body, ref pos);
            if (pos >= body.Length) break;

            if (TryMatchKeyword(body, pos, "yield", out var kwEnd))
            {
                pos = kwEnd;
                SkipWhitespace(body, ref pos);
                if (TryMatchKeyword(body, pos, "break", out kwEnd))
                {
                    // yield break — stop producing values
                    pos = kwEnd;
                    SkipSemicolon(body, ref pos);
                    break;
                }
                if (TryMatchKeyword(body, pos, "return", out kwEnd))
                {
                    pos = kwEnd;
                    SkipWhitespace(body, ref pos);
                    var exprStart = pos;
                    pos = ReadSimpleStatement(body, pos);
                    var expr = body[exprStart..pos].TrimEnd(';').Trim();
                    if (!string.IsNullOrEmpty(expr))
                    {
                        var value = RazorLightweightExpressionEvaluator.Evaluate(expr, scope.Resolve);
                        values.Add(value);
                    }
                }
                continue;
            }

            // Skip other statements
            pos = ReadSimpleStatement(body, pos);
        }

        return new SyncAsyncEnumerable(values);
    }

    /// <summary>
    /// Wraps a list of values as an IAsyncEnumerable for use with await foreach.
    /// </summary>
    private sealed class SyncAsyncEnumerable(List<object?> values) : IAsyncEnumerable<object?>, IEnumerable<object?>
    {
        public IAsyncEnumerator<object?> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default)
            => new Enumerator(values);

        public IEnumerator<object?> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => values.GetEnumerator();

        private sealed class Enumerator(List<object?> items) : IAsyncEnumerator<object?>
        {
            private int _index = -1;
            public object? Current => _index >= 0 && _index < items.Count ? items[_index] : default;
            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(++_index < items.Count);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // ─── Section support ───

    private static void InterpretSection(string code, ref int pos, StringBuilder output, InterpreterScope scope)
    {
        SkipWhitespace(code, ref pos);

        // Read section name
        var nameStart = pos;
        while (pos < code.Length && (char.IsLetterOrDigit(code[pos]) || code[pos] == '_')) pos++;
        var sectionName = code[nameStart..pos];

        SkipWhitespace(code, ref pos);
        if (pos >= code.Length || code[pos] != '{') return;

        var body = ReadBraceBody(code, ref pos);

        // Store section content in scope for later RenderSection calls
        scope.Set("__section_" + sectionName, body);
    }

    /// <summary>
    /// Execute pure C# code (no XML elements) via tokenizer.
    /// </summary>
    private static void ExecutePureCode(string code, StringBuilder output, InterpreterScope scope)
    {
        code = code.Trim();
        if (string.IsNullOrEmpty(code)) return;
        var tokens = new RazorTokenizer(code).Tokenize();
        var flow = new FlowSignal();
        ExecuteStatements(tokens, 0, output, scope, flow);
    }

    private static void SkipWhitespace(string code, ref int pos)
    {
        while (pos < code.Length && char.IsWhiteSpace(code[pos])) pos++;
    }

    /// <summary>
    /// Reads a single C# statement, stopping before a statement-level XML element.
    /// Handles strings, comments, nested braces, and blocks.
    /// </summary>
    private static int ReadCSharpStatement(string code, int pos)
    {
        var braceDepth = 0;
        var inString = false;
        var inChar = false;
        var escaped = false;
        var verbatim = false;
        char stringQuote = '\0';

        while (pos < code.Length)
        {
            var c = code[pos];

            if (escaped) { escaped = false; pos++; continue; }

            if (inString)
            {
                if (!verbatim && c == '\\') { escaped = true; pos++; continue; }
                if (c == stringQuote)
                {
                    if (verbatim && pos + 1 < code.Length && code[pos + 1] == stringQuote) { pos += 2; continue; }
                    inString = false; verbatim = false;
                }
                pos++; continue;
            }

            if (inChar)
            {
                if (c == '\\') { escaped = true; pos++; continue; }
                if (c == '\'') inChar = false;
                pos++; continue;
            }

            // Comments
            if (c == '/' && pos + 1 < code.Length)
            {
                if (code[pos + 1] == '/') { while (pos < code.Length && code[pos] != '\n') pos++; continue; }
                if (code[pos + 1] == '*') { pos += 2; while (pos + 1 < code.Length && !(code[pos] == '*' && code[pos + 1] == '/')) pos++; pos += 2; continue; }
            }

            if (c == '\'') { inChar = true; pos++; continue; }
            if (c == '"')
            {
                inString = true;
                verbatim = pos > 0 && code[pos - 1] == '@';
                stringQuote = '"';
                pos++; continue;
            }

            if (c == '{') { braceDepth++; pos++; continue; }
            if (c == '}')
            {
                braceDepth--;
                pos++;
                if (braceDepth <= 0) return pos; // end of block
                continue;
            }

            // Statement-level XML element: stop before it ONLY at top level (outside any braces)
            if (braceDepth == 0 && c == '<' && pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
                return pos;

            // Semicolon at top level ends simple statement
            if (braceDepth == 0 && c == ';') { pos++; return pos; }

            pos++;
        }

        return pos;
    }

    /// <summary>
    /// Reads a complete XML element including nested children.
    /// </summary>
    private static int ReadXmlElement(string code, int start)
    {
        var pos = start + 1;
        while (pos < code.Length && !char.IsWhiteSpace(code[pos]) && code[pos] != '>' && code[pos] != '/')
            pos++;

        var inAttrVal = false;
        char aq = '\0';
        while (pos < code.Length)
        {
            if (inAttrVal) { if (code[pos] == aq) inAttrVal = false; pos++; continue; }
            if (code[pos] == '"' || code[pos] == '\'') { inAttrVal = true; aq = code[pos]; pos++; continue; }
            if (code[pos] == '/' && pos + 1 < code.Length && code[pos + 1] == '>') return pos + 2;
            if (code[pos] == '>') { pos++; break; }
            pos++;
        }

        var depth = 1;
        while (pos < code.Length && depth > 0)
        {
            if (code[pos] == '<')
            {
                if (pos + 1 < code.Length && code[pos + 1] == '/')
                {
                    var closeEnd = code.IndexOf('>', pos + 2);
                    if (closeEnd >= 0) { depth--; pos = closeEnd + 1; if (depth == 0) return pos; continue; }
                }
                else if (pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
                {
                    var selfClose = false;
                    var tagEnd = pos + 1;
                    var inAV = false; char avq = '\0';
                    while (tagEnd < code.Length)
                    {
                        if (inAV) { if (code[tagEnd] == avq) inAV = false; tagEnd++; continue; }
                        if (code[tagEnd] == '"' || code[tagEnd] == '\'') { inAV = true; avq = code[tagEnd]; tagEnd++; continue; }
                        if (code[tagEnd] == '/' && tagEnd + 1 < code.Length && code[tagEnd + 1] == '>') { selfClose = true; tagEnd += 2; break; }
                        if (code[tagEnd] == '>') { tagEnd++; break; }
                        tagEnd++;
                    }
                    if (!selfClose) depth++;
                    pos = tagEnd; continue;
                }
            }
            pos++;
        }
        return pos;
    }

    // ─────────────────────────── Scope ───────────────────────────

    private sealed class InterpreterScope
    {
        private readonly Dictionary<string, object?> _variables = new(StringComparer.Ordinal);
        private readonly InterpreterScope? _parent;
        private readonly Func<string, object?>? _externalResolver;

        public InterpreterScope(InterpreterScope? parent = null) => _parent = parent;

        public InterpreterScope(Func<string, object?> externalResolver)
        {
            _externalResolver = externalResolver;
        }

        public object? Get(string name)
        {
            if (_variables.TryGetValue(name, out var value)) return value;
            if (_parent != null) return _parent.Get(name);
            if (_externalResolver != null) return _externalResolver(name);
            return null;
        }

        public bool TryGet(string name, out object? value)
        {
            if (_variables.TryGetValue(name, out value)) return true;
            if (_parent != null) return _parent.TryGet(name, out value);
            if (_externalResolver != null) { value = _externalResolver(name); return value != null; }
            value = null;
            return false;
        }

        public void Set(string name, object? value) => _variables[name] = value;

        public void SetInScope(string name, object? value)
        {
            // Update in the scope where the variable was declared
            if (_variables.ContainsKey(name)) { _variables[name] = value; return; }
            if (_parent != null) { _parent.SetInScope(name, value); return; }
            _variables[name] = value;
        }

        public InterpreterScope CreateChild() => new(this);

        public object? Resolve(string name) => Get(name);
    }

    // ───────────────────── Control flow signals ─────────────────────

    private enum Signal { None, Break, Continue, Return }
    private sealed class FlowSignal { public Signal Kind; public object? ReturnValue; }

    // ───────────────────── Segment execution ─────────────────────

    private static void ExecuteSegments(List<RazorCodeBlockPreprocessor.CodeSegment> segments, StringBuilder output, InterpreterScope scope)
    {
        foreach (var segment in segments)
        {
            if (segment.IsMarkup)
                EmitMarkup(segment.Text, output, scope);
            else
                ExecuteCode(segment.Text, output, scope);
        }
    }

    // ───────────────────── Markup emission ─────────────────────

    private static void EmitMarkup(string markup, StringBuilder output, InterpreterScope scope)
    {
        var i = 0;
        while (i < markup.Length)
        {
            if (markup[i] == '@' && i + 1 < markup.Length)
            {
                if (markup[i + 1] == '@') { output.Append('@'); i += 2; continue; }

                if (markup[i + 1] == '(')
                {
                    var exprStart = i + 2;
                    var depth = 1;
                    var p = exprStart;
                    while (p < markup.Length && depth > 0)
                    {
                        if (markup[p] == '(') depth++;
                        else if (markup[p] == ')') { depth--; if (depth == 0) break; }
                        else if (markup[p] == '"') { p++; while (p < markup.Length && markup[p] != '"') { if (markup[p] == '\\') p++; p++; } }
                        else if (markup[p] == '\'') { p++; while (p < markup.Length && markup[p] != '\'') { if (markup[p] == '\\') p++; p++; } }
                        p++;
                    }
                    var expr = markup[exprStart..p];
                    var value = RazorLightweightExpressionEvaluator.Evaluate(expr, scope.Resolve);
                    output.Append(value?.ToString() ?? "");
                    i = p + 1;
                    continue;
                }

                // @{ inline code }
                if (markup[i + 1] == '{')
                {
                    var codeStart = i + 2;
                    var braceDepth = 1;
                    var p = codeStart;
                    while (p < markup.Length && braceDepth > 0)
                    {
                        if (markup[p] == '{') braceDepth++;
                        else if (markup[p] == '}') { braceDepth--; if (braceDepth == 0) break; }
                        else if (markup[p] == '"') { p++; while (p < markup.Length && markup[p] != '"') { if (markup[p] == '\\') p++; p++; } }
                        p++;
                    }
                    var code = markup[codeStart..p];
                    ExecuteCode(code, output, scope);
                    i = p + 1;
                    continue;
                }

                // @identifier
                if (char.IsLetter(markup[i + 1]) || markup[i + 1] == '_')
                {
                    // Check for block directive (@for, @foreach, etc.)
                    if (TryMatchAndExecuteBlockDirective(markup, i, output, scope, out var consumed))
                    {
                        i = consumed;
                        continue;
                    }

                    // @RenderSection("Name") — emit stored section content
                    if (TryRenderSection(markup, i, output, scope, out consumed))
                    {
                        i = consumed;
                        continue;
                    }

                    var idStart = i + 1;
                    var p = idStart;
                    while (p < markup.Length && (char.IsLetterOrDigit(markup[p]) || markup[p] == '_' || markup[p] == '.'))
                        p++;
                    var identifier = markup[idStart..p];
                    // Use the expression evaluator for dotted paths (e.g. @kv.Item2)
                    // so that member access is resolved correctly.
                    var value = identifier.Contains('.')
                        ? RazorLightweightExpressionEvaluator.Evaluate(identifier, scope.Resolve)
                        : scope.Resolve(identifier);
                    output.Append(value?.ToString() ?? "");
                    i = p;
                    continue;
                }
            }

            output.Append(markup[i]);
            i++;
        }
    }

    private static bool TryRenderSection(string markup, int atPos, StringBuilder output, InterpreterScope scope, out int consumed)
    {
        consumed = 0;
        var pos = atPos + 1;
        var remaining = markup.Length - pos;

        if (remaining < 13 || !markup.AsSpan(pos, 13).SequenceEqual("RenderSection"))
            return false;

        var p = pos + 13;
        while (p < markup.Length && char.IsWhiteSpace(markup[p])) p++;
        if (p >= markup.Length || markup[p] != '(') return false;

        var argsStart = p + 1;
        var parenEnd = FindMatchingChar(markup, argsStart, '(', ')');
        if (parenEnd < 0) return false;

        var args = markup[argsStart..parenEnd].Trim();

        // Extract quoted section name
        if (args.Length < 2) return false;
        var quote = args[0];
        if (quote != '"' && quote != '\'') return false;
        var endQuote = args.IndexOf(quote, 1);
        if (endQuote < 0) return false;
        var sectionName = args[1..endQuote];

        // Look up section content from scope
        var sectionKey = "__section_" + sectionName;
        if (scope.TryGet(sectionKey, out var sectionContent) && sectionContent is string content)
        {
            EmitMarkup(content, output, scope);
        }

        consumed = parenEnd + 1;
        return true;
    }

    private static bool TryMatchAndExecuteBlockDirective(string markup, int atPos, StringBuilder output, InterpreterScope scope, out int consumed)
    {
        consumed = 0;
        var pos = atPos + 1;
        var remaining = markup.Length - pos;

        // Match keyword
        string? keyword = null;
        int keywordLen = 0;
        if (remaining >= 7 && markup.AsSpan(pos, 7).SequenceEqual("foreach") && (remaining == 7 || !char.IsLetterOrDigit(markup[pos + 7])))
        { keyword = "foreach"; keywordLen = 7; }
        else if (remaining >= 5 && markup.AsSpan(pos, 5).SequenceEqual("while") && (remaining == 5 || !char.IsLetterOrDigit(markup[pos + 5])))
        { keyword = "while"; keywordLen = 5; }
        else if (remaining >= 3 && markup.AsSpan(pos, 3).SequenceEqual("for") && (remaining == 3 || !char.IsLetterOrDigit(markup[pos + 3])))
        { keyword = "for"; keywordLen = 3; }
        else if (remaining >= 2 && markup.AsSpan(pos, 2).SequenceEqual("if") && (remaining == 2 || !char.IsLetterOrDigit(markup[pos + 2])))
        { keyword = "if"; keywordLen = 2; }

        if (keyword == null) return false;

        var p = pos + keywordLen;
        while (p < markup.Length && char.IsWhiteSpace(markup[p])) p++;
        if (p >= markup.Length || markup[p] != '(') return false;

        // Find matching ) and {
        var afterParen = FindMatchingChar(markup, p + 1, '(', ')');
        if (afterParen < 0) return false;
        var condition = markup[(p + 1)..afterParen];
        p = afterParen + 1;

        while (p < markup.Length && char.IsWhiteSpace(markup[p])) p++;
        if (p >= markup.Length || markup[p] != '{') return false;

        var bodyEnd = FindMatchingChar(markup, p + 1, '{', '}');
        if (bodyEnd < 0) return false;
        var body = markup[(p + 1)..bodyEnd];
        consumed = bodyEnd + 1;

        // Execute the directive
        switch (keyword)
        {
            case "for":
                ExecuteForInMarkup(condition, body, output, scope);
                break;
            case "foreach":
                ExecuteForeachInMarkup(condition, body, output, scope);
                break;
            case "while":
                ExecuteWhileInMarkup(condition, body, output, scope);
                break;
            case "if":
                ExecuteIfInMarkup(condition, body, output, scope);
                break;
        }

        return true;
    }

    private static void ExecuteForInMarkup(string header, string body, StringBuilder output, InterpreterScope scope)
    {
        // Parse: init; condition; increment
        var parts = SplitForHeader(header);
        if (parts.Length != 3) return;

        var child = scope.CreateChild();
        ExecuteCode(parts[0] + ";", output, child);
        var iterLimit = 10000;
        while (iterLimit-- > 0)
        {
            var cond = RazorLightweightExpressionEvaluator.Evaluate(parts[1].Trim(), child.Resolve);
            if (!RazorExpressionParser.IsTruthy(cond)) break;
            EmitMarkup(body, output, child);
            ExecuteCode(parts[2].Trim() + ";", output, child);
        }
    }

    private static void ExecuteForeachInMarkup(string header, string body, StringBuilder output, InterpreterScope scope)
    {
        // Parse: var x in expr  or  Type x in expr
        var inIdx = header.IndexOf(" in ", StringComparison.Ordinal);
        if (inIdx < 0) return;
        var varPart = header[..inIdx].Trim();
        var exprPart = header[(inIdx + 4)..].Trim();

        var varName = varPart.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
        var collection = RazorLightweightExpressionEvaluator.Evaluate(exprPart, scope.Resolve);

        if (collection is IEnumerable enumerable)
        {
            var child = scope.CreateChild();
            foreach (var item in enumerable)
            {
                child.Set(varName, item);
                EmitMarkup(body, output, child);
            }
        }
    }

    private static void ExecuteWhileInMarkup(string condition, string body, StringBuilder output, InterpreterScope scope)
    {
        var iterLimit = 10000;
        while (iterLimit-- > 0)
        {
            var cond = RazorLightweightExpressionEvaluator.Evaluate(condition.Trim(), scope.Resolve);
            if (!RazorExpressionParser.IsTruthy(cond)) break;
            EmitMarkup(body, output, scope);
        }
    }

    private static void ExecuteIfInMarkup(string condition, string body, StringBuilder output, InterpreterScope scope)
    {
        var cond = RazorLightweightExpressionEvaluator.Evaluate(condition.Trim(), scope.Resolve);
        if (RazorExpressionParser.IsTruthy(cond))
            EmitMarkup(body, output, scope);
    }

    private static int FindMatchingChar(string text, int start, char open, char close)
    {
        var depth = 1;
        var inStr = false; var esc = false; char sq = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (esc) { esc = false; continue; }
            if (inStr) { if (c == '\\') esc = true; else if (c == sq) inStr = false; continue; }
            if (c == '"' || c == '\'') { inStr = true; sq = c; continue; }
            if (c == open) depth++;
            else if (c == close) { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    // ───────────────────── Code execution ─────────────────────

    private static void ExecuteCode(string code, StringBuilder output, InterpreterScope scope)
    {
        code = code.Trim();
        if (string.IsNullOrEmpty(code)) return;

        // If code contains XML elements, use the mixed interpreter
        // which handles statement-level <Element/> as markup output.
        if (ContainsXmlElement(code))
        {
            InterpretMixedCode(code, output, scope);
            return;
        }

        var tokens = new RazorTokenizer(code).Tokenize();
        var flow = new FlowSignal();
        ExecuteStatements(tokens, 0, output, scope, flow);
    }

    private static bool ContainsXmlElement(string code)
    {
        for (var i = 0; i < code.Length - 1; i++)
        {
            var c = code[i];
            // Skip strings
            if (c == '"') { i++; while (i < code.Length && code[i] != '"') { if (code[i] == '\\') i++; i++; } continue; }
            if (c == '\'') { i++; while (i < code.Length && code[i] != '\'') { if (code[i] == '\\') i++; i++; } continue; }
            // Check for <Letter (XML element start)
            if (c == '<' && i + 1 < code.Length && char.IsLetter(code[i + 1]))
                return true;
        }
        return false;
    }

    private static int ExecuteStatements(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Eof && flow.Kind == Signal.None)
        {
            pos = ExecuteStatement(tokens, pos, output, scope, flow);
        }
        return pos;
    }

    private static int ExecuteStatement(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        if (pos >= tokens.Count || tokens[pos].Kind == RazorTokenKind.Eof) return pos;

        var token = tokens[pos];

        switch (token.Kind)
        {
            case RazorTokenKind.Var:
            case RazorTokenKind.Int:
            case RazorTokenKind.Long:
            case RazorTokenKind.Double:
            case RazorTokenKind.Float:
            case RazorTokenKind.Decimal:
            case RazorTokenKind.Bool:
            case RazorTokenKind.String:
            case RazorTokenKind.Char:
            case RazorTokenKind.Object:
            case RazorTokenKind.Dynamic:
                return ExecuteVarDeclaration(tokens, pos, output, scope, flow);

            case RazorTokenKind.If:
                return ExecuteIf(tokens, pos, output, scope, flow);

            case RazorTokenKind.For:
                return ExecuteFor(tokens, pos, output, scope, flow);

            case RazorTokenKind.Foreach:
                return ExecuteForeach(tokens, pos, output, scope, flow);

            case RazorTokenKind.While:
                return ExecuteWhile(tokens, pos, output, scope, flow);

            case RazorTokenKind.Do:
                return ExecuteDoWhile(tokens, pos, output, scope, flow);

            case RazorTokenKind.Switch:
                return ExecuteSwitch(tokens, pos, output, scope, flow);

            case RazorTokenKind.Try:
                return ExecuteTryCatch(tokens, pos, output, scope, flow);

            case RazorTokenKind.Break:
                flow.Kind = Signal.Break;
                pos++;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                return pos;

            case RazorTokenKind.Continue:
                flow.Kind = Signal.Continue;
                pos++;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                return pos;

            case RazorTokenKind.Return:
                pos++;
                flow.Kind = Signal.Return;
                if (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Semicolon)
                {
                    var parser = new RazorExpressionParser(tokens, pos);
                    flow.ReturnValue = parser.EvalExpression(scope.Resolve);
                    pos = parser.Position;
                }
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                return pos;

            case RazorTokenKind.Throw:
                pos++;
                var throwParser = new RazorExpressionParser(tokens, pos);
                var throwVal = throwParser.EvalExpression(scope.Resolve);
                pos = throwParser.Position;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                if (throwVal is Exception ex) throw ex;
                throw new XamlParseException($"Thrown value: {throwVal}");

            case RazorTokenKind.Await:
                // await expression statement: await Task.Run(...)
                pos++;
                var awaitParser = new RazorExpressionParser(tokens, pos);
                var awaitVal = awaitParser.EvalExpression(scope.Resolve);
                pos = awaitParser.Position;
                RazorExpressionParser.UnwrapAwaitable(awaitVal);
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                return pos;

            case RazorTokenKind.Async:
                // async local function via token path
                return ExecuteVarDeclaration(tokens, pos, output, scope, flow);

            case RazorTokenKind.Semicolon:
                return pos + 1;

            case RazorTokenKind.OpenBrace:
                return ExecuteBlock(tokens, pos, output, scope, flow);

            case RazorTokenKind.Identifier:
                // static local function: static Type Name(...)
                if (tokens[pos].Value == "static" && pos + 1 < tokens.Count)
                    return ExecuteVarDeclaration(tokens, pos, output, scope, flow);
                // Check for assignment or expression statement
                if (pos + 1 < tokens.Count)
                {
                    var next = tokens[pos + 1].Kind;
                    if (next is RazorTokenKind.Assign or RazorTokenKind.PlusAssign or RazorTokenKind.MinusAssign
                        or RazorTokenKind.StarAssign or RazorTokenKind.SlashAssign or RazorTokenKind.PercentAssign
                        or RazorTokenKind.BitwiseAndAssign or RazorTokenKind.BitwiseOrAssign or RazorTokenKind.BitwiseXorAssign
                        or RazorTokenKind.LeftShiftAssign or RazorTokenKind.RightShiftAssign
                        or RazorTokenKind.QuestionQuestionAssign)
                        return ExecuteAssignment(tokens, pos, output, scope, flow);
                    if (next == RazorTokenKind.PlusPlus || next == RazorTokenKind.MinusMinus)
                        return ExecuteIncrementDecrement(tokens, pos, scope);
                    // Dotted assignment: x.Y.Z = value
                    if (next == RazorTokenKind.Dot)
                    {
                        var lookAhead = pos + 1;
                        while (lookAhead < tokens.Count && tokens[lookAhead].Kind == RazorTokenKind.Dot)
                        {
                            lookAhead++; // skip .
                            if (lookAhead < tokens.Count && tokens[lookAhead].Kind == RazorTokenKind.Identifier) lookAhead++; // skip member
                        }
                        if (lookAhead < tokens.Count && tokens[lookAhead].Kind is RazorTokenKind.Assign or RazorTokenKind.PlusAssign or RazorTokenKind.MinusAssign)
                            return ExecuteDottedAssignment(tokens, pos, scope);
                    }
                }
                // Indexer assignment: x[key] = value
                if (pos + 1 < tokens.Count && tokens[pos + 1].Kind == RazorTokenKind.OpenBracket)
                {
                    var saved = pos;
                    if (TryExecuteIndexerAssignment(tokens, ref pos, scope))
                        return pos;
                    pos = saved;
                }
                // Dotted assignment or expression statement
                return ExecuteExpressionStatement(tokens, pos, output, scope, flow);

            case RazorTokenKind.PlusPlus:
            case RazorTokenKind.MinusMinus:
                // ++x or --x
                var isInc = token.Kind == RazorTokenKind.PlusPlus;
                pos++;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Identifier)
                {
                    var name = tokens[pos].Value;
                    pos++;
                    var cur = scope.Get(name);
                    scope.SetInScope(name, isInc ? Increment(cur) : Decrement(cur));
                }
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                return pos;

            default:
                // Expression statement
                return ExecuteExpressionStatement(tokens, pos, output, scope, flow);
        }
    }

    private static int ExecuteVarDeclaration(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip type/var
        // Handle possible generic type or array type
        while (pos < tokens.Count && tokens[pos].Kind is RazorTokenKind.Less or RazorTokenKind.OpenBracket or RazorTokenKind.CloseBracket or RazorTokenKind.Greater or RazorTokenKind.Comma or RazorTokenKind.Question)
            pos++;

        // Tuple deconstruction: var (a, b) = expr;
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen)
        {
            pos++; // skip (
            var names = new List<string>();
            while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseParen)
            {
                if (tokens[pos].Kind == RazorTokenKind.Identifier) names.Add(tokens[pos].Value);
                pos++;
            }
            if (pos < tokens.Count) pos++; // skip )
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Assign)
            {
                pos++;
                var parser = new RazorExpressionParser(tokens, pos);
                var tupleVal = parser.EvalExpression(scope.Resolve);
                pos = parser.Position;
                // Extract tuple items via reflection (ValueTuple fields: Item1, Item2, ...)
                if (tupleVal != null)
                {
                    var tupleType = tupleVal.GetType();
                    for (var i = 0; i < names.Count; i++)
                    {
                        var field = tupleType.GetField($"Item{i + 1}");
                        scope.Set(names[i], field?.GetValue(tupleVal));
                    }
                }
            }
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
            return pos;
        }

        if (pos >= tokens.Count || tokens[pos].Kind != RazorTokenKind.Identifier)
        {
            // Might be a type used as expression (e.g., string.Join)
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) return pos + 1;
            return pos;
        }

        var name = tokens[pos].Value;
        pos++;

        // Local function: Type Name(...) => expr; or Type Name(...) { body }
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen)
        {
            // Skip parameter list
            var parenDepth = 1;
            pos++; // skip (
            var paramNames = new List<string>();
            while (pos < tokens.Count && parenDepth > 0)
            {
                if (tokens[pos].Kind == RazorTokenKind.OpenParen) parenDepth++;
                else if (tokens[pos].Kind == RazorTokenKind.CloseParen) { parenDepth--; if (parenDepth == 0) break; }
                // Capture parameter names (last identifier before , or ))
                if (tokens[pos].Kind == RazorTokenKind.Identifier &&
                    pos + 1 < tokens.Count && (tokens[pos + 1].Kind is RazorTokenKind.Comma or RazorTokenKind.CloseParen))
                    paramNames.Add(tokens[pos].Value);
                pos++;
            }
            if (pos < tokens.Count) pos++; // skip )

            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Arrow)
            {
                // Expression-bodied: Name(...) => expr;
                pos++; // skip =>
                // Collect remaining tokens as the body expression
                var bodyStart = pos;
                while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Semicolon) pos++;
                var bodyTokens = tokens.GetRange(bodyStart, pos - bodyStart);
                var bodyExpr = string.Join(" ", bodyTokens.Select(t => t.Kind switch
                {
                    RazorTokenKind.StringLiteral => "\"" + t.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
                    RazorTokenKind.CharLiteral => "'" + t.Value + "'",
                    _ => t.Value
                }));
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;

                // Store as a callable: when invoked, evaluate the expression in current scope
                var capturedScope = scope;
                scope.Set(name, new Func<object?[], object?>(args =>
                {
                    var fnScope = capturedScope.CreateChild();
                    for (var i = 0; i < Math.Min(paramNames.Count, args.Length); i++)
                        fnScope.Set(paramNames[i], args[i]);
                    return RazorLightweightExpressionEvaluator.Evaluate(bodyExpr, fnScope.Resolve);
                }));
                return pos;
            }

            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenBrace)
            {
                // Block-bodied local function: capture the token range and
                // interpret the body when called at runtime.
                var bodyStartPos = pos;
                var depth = 1; pos++;
                while (pos < tokens.Count && depth > 0)
                {
                    if (tokens[pos].Kind == RazorTokenKind.OpenBrace) depth++;
                    else if (tokens[pos].Kind == RazorTokenKind.CloseBrace) depth--;
                    pos++;
                }
                // Capture the body tokens (between { and })
                var bodyTokens = tokens.GetRange(bodyStartPos + 1, pos - bodyStartPos - 2);
                bodyTokens.Add(new RazorToken(RazorTokenKind.Eof, "", 0));
                var capturedScope = scope;
                scope.Set(name, new Func<object?[], object?>(args =>
                {
                    var fnScope = capturedScope.CreateChild();
                    for (var i = 0; i < Math.Min(paramNames.Count, args.Length); i++)
                        fnScope.Set(paramNames[i], args[i]);
                    var fnOutput = new StringBuilder();
                    var fnFlow = new FlowSignal();
                    ExecuteStatements(bodyTokens, 0, fnOutput, fnScope, fnFlow);
                    if (fnFlow.Kind == Signal.Return)
                        return fnFlow.ReturnValue;
                    return fnOutput.Length > 0 ? fnOutput.ToString() : null;
                }));
                return pos;
            }

            // Not a function, something else — skip to semicolon
            while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Semicolon) pos++;
            if (pos < tokens.Count) pos++;
            scope.Set(name, null);
            return pos;
        }

        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Assign)
        {
            pos++;
            var parser = new RazorExpressionParser(tokens, pos);
            var value = parser.EvalExpression(scope.Resolve);
            pos = parser.Position;
            scope.Set(name, value);
        }
        else
        {
            scope.Set(name, null);
        }

        // Multiple variable declarations: int a = 1, b = 2, c;
        while (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Comma)
        {
            pos++; // skip ,
            if (pos >= tokens.Count || tokens[pos].Kind != RazorTokenKind.Identifier) break;
            var extraName = tokens[pos].Value;
            pos++;
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Assign)
            {
                pos++;
                var parser = new RazorExpressionParser(tokens, pos);
                var extraVal = parser.EvalExpression(scope.Resolve);
                pos = parser.Position;
                scope.Set(extraName, extraVal);
            }
            else
            {
                scope.Set(extraName, null);
            }
        }

        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
        return pos;
    }

    private static int ExecuteAssignment(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var name = tokens[pos].Value;
        pos++;
        var op = tokens[pos].Kind;
        pos++;

        var parser = new RazorExpressionParser(tokens, pos);
        var value = parser.EvalExpression(scope.Resolve);
        pos = parser.Position;

        switch (op)
        {
            case RazorTokenKind.Assign:
                scope.SetInScope(name, value);
                break;
            case RazorTokenKind.PlusAssign:
                var cur = scope.Get(name);
                if (cur is string s) scope.SetInScope(name, s + value?.ToString());
                else scope.SetInScope(name, AddValues(cur, value));
                break;
            case RazorTokenKind.MinusAssign:
                scope.SetInScope(name, SubtractValues(scope.Get(name), value));
                break;
            case RazorTokenKind.StarAssign:
                scope.SetInScope(name, MultiplyValues(scope.Get(name), value));
                break;
            case RazorTokenKind.SlashAssign:
                scope.SetInScope(name, DivideValues(scope.Get(name), value));
                break;
            case RazorTokenKind.PercentAssign:
                scope.SetInScope(name, RazorExpressionParser.ArithmeticOp(scope.Get(name), value, '%'));
                break;
            case RazorTokenKind.BitwiseAndAssign:
                scope.SetInScope(name, RazorExpressionParser.BitwiseOp(scope.Get(name), value, '&'));
                break;
            case RazorTokenKind.BitwiseOrAssign:
                scope.SetInScope(name, RazorExpressionParser.BitwiseOp(scope.Get(name), value, '|'));
                break;
            case RazorTokenKind.BitwiseXorAssign:
                scope.SetInScope(name, RazorExpressionParser.BitwiseOp(scope.Get(name), value, '^'));
                break;
            case RazorTokenKind.LeftShiftAssign:
                scope.SetInScope(name, RazorExpressionParser.BitwiseOp(scope.Get(name), value, '<'));
                break;
            case RazorTokenKind.RightShiftAssign:
                scope.SetInScope(name, RazorExpressionParser.BitwiseOp(scope.Get(name), value, '>'));
                break;
            case RazorTokenKind.QuestionQuestionAssign:
                var curVal = scope.Get(name);
                if (curVal == null) scope.SetInScope(name, value);
                break;
        }

        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
        return pos;
    }

    private static int ExecuteDottedAssignment(List<RazorToken> tokens, int pos, InterpreterScope scope)
    {
        // Navigate dotted path: x.Y.Z = value
        var rootName = tokens[pos].Value;
        var target = scope.Get(rootName);
        pos++; // skip root identifier

        // Navigate to the parent object
        while (pos + 2 < tokens.Count && tokens[pos].Kind == RazorTokenKind.Dot)
        {
            pos++; // skip .
            var member = tokens[pos].Value;
            pos++; // skip member name
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Dot)
            {
                // More dots ahead — navigate deeper
                target = RazorExpressionParser.GetMember(target, member);
            }
            else if (pos < tokens.Count && tokens[pos].Kind is RazorTokenKind.Assign or RazorTokenKind.PlusAssign or RazorTokenKind.MinusAssign)
            {
                // Final assignment
                var op = tokens[pos].Kind;
                pos++;
                var parser = new RazorExpressionParser(tokens, pos);
                var value = parser.EvalExpression(scope.Resolve);
                pos = parser.Position;

                if (target != null)
                {
                    var prop = target.GetType().GetProperty(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        if (op == RazorTokenKind.Assign)
                            prop.SetValue(target, value);
                        else if (op == RazorTokenKind.PlusAssign)
                        {
                            var cur = prop.GetValue(target);
                            prop.SetValue(target, cur is string s ? s + value?.ToString() : RazorExpressionParser.ArithmeticOp(cur, value, '+'));
                        }
                    }
                    else
                    {
                        var field = target.GetType().GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (field != null) field.SetValue(target, value);
                    }
                }
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                return pos;
            }
        }
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
        return pos;
    }

    private static int ExecuteIncrementDecrement(List<RazorToken> tokens, int pos, InterpreterScope scope)
    {
        var name = tokens[pos].Value;
        pos++;
        var isInc = tokens[pos].Kind == RazorTokenKind.PlusPlus;
        pos++;
        scope.SetInScope(name, isInc ? Increment(scope.Get(name)) : Decrement(scope.Get(name)));
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
        return pos;
    }

    private static bool TryExecuteIndexerAssignment(List<RazorToken> tokens, ref int pos, InterpreterScope scope)
    {
        var targetName = tokens[pos].Value;
        var target = scope.Get(targetName);
        pos++; // skip identifier
        pos++; // skip [
        var parser = new RazorExpressionParser(tokens, pos);
        var index = parser.EvalExpression(scope.Resolve);
        pos = parser.Position;
        if (pos >= tokens.Count || tokens[pos].Kind != RazorTokenKind.CloseBracket) return false;
        pos++; // skip ]
        if (pos >= tokens.Count || tokens[pos].Kind != RazorTokenKind.Assign) return false;
        pos++; // skip =
        var valueParser = new RazorExpressionParser(tokens, pos);
        var value = valueParser.EvalExpression(scope.Resolve);
        pos = valueParser.Position;
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;

        // Set via indexer
        if (target is System.Collections.IList list && index is int idx)
            list[idx] = value;
        else if (target is System.Collections.IDictionary dict)
            dict[index!] = value;
        else
        {
            // Try reflection-based indexer
            var indexerProp = target?.GetType().GetProperties()
                .FirstOrDefault(p => p.GetIndexParameters().Length == 1);
            if (indexerProp != null)
            {
                try
                {
                    var convertedIndex = Convert.ChangeType(index, indexerProp.GetIndexParameters()[0].ParameterType, System.Globalization.CultureInfo.InvariantCulture);
                    indexerProp.SetValue(target, value, new[] { convertedIndex });
                }
                catch { return false; }
            }
            else return false;
        }
        return true;
    }

    private static int ExecuteExpressionStatement(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        var parser = new RazorExpressionParser(tokens, pos);
        parser.EvalExpression(scope.Resolve);
        pos = parser.Position;
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
        return pos;
    }

    private static int ExecuteBlock(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip {
        var child = scope.CreateChild();
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseBrace && tokens[pos].Kind != RazorTokenKind.Eof && flow.Kind == Signal.None)
        {
            pos = ExecuteStatement(tokens, pos, output, child, flow);
        }
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.CloseBrace) pos++;
        return pos;
    }

    private static int ExecuteIf(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'if'
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen) pos++;
        var parser = new RazorExpressionParser(tokens, pos);
        var cond = parser.EvalExpression(scope.Resolve);
        pos = parser.Position;
        pos++; // skip )

        if (RazorExpressionParser.IsTruthy(cond))
        {
            pos = ExecuteBlock(tokens, pos, output, scope, flow);
            // Skip else branches
            while (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Else)
            {
                pos++;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.If)
                {
                    pos++; // skip 'if'
                    pos = SkipParenthesized(tokens, pos);
                }
                pos = SkipBlock(tokens, pos);
            }
        }
        else
        {
            pos = SkipBlock(tokens, pos);
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Else)
            {
                pos++;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.If)
                    pos = ExecuteIf(tokens, pos, output, scope, flow);
                else
                    pos = ExecuteBlock(tokens, pos, output, scope, flow);
            }
        }
        return pos;
    }

    private static int ExecuteFor(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'for'
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen) pos++;

        var child = scope.CreateChild();

        // Initializer
        var initStart = pos;
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Semicolon) pos++;
        var initTokens = tokens.GetRange(initStart, pos - initStart);
        initTokens.Add(new RazorToken(RazorTokenKind.Semicolon, ";", 0));
        initTokens.Add(new RazorToken(RazorTokenKind.Eof, "", 0));
        ExecuteStatements(initTokens, 0, output, child, new FlowSignal());
        pos++; // skip ;

        // Condition
        var condStart = pos;
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Semicolon) pos++;
        var condExpr = string.Join(" ", tokens.GetRange(condStart, pos - condStart).Select(t => t.Value));
        pos++; // skip ;

        // Increment
        var incrStart = pos;
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseParen) pos++;
        var incrTokens = tokens.GetRange(incrStart, pos - incrStart);
        incrTokens.Add(new RazorToken(RazorTokenKind.Semicolon, ";", 0));
        incrTokens.Add(new RazorToken(RazorTokenKind.Eof, "", 0));
        pos++; // skip )

        // Body
        var bodyStart = pos;
        var bodyEnd = FindBlockEnd(tokens, pos);

        var iterLimit = 10000;
        while (iterLimit-- > 0)
        {
            var condVal = RazorLightweightExpressionEvaluator.Evaluate(condExpr, child.Resolve);
            if (!RazorExpressionParser.IsTruthy(condVal)) break;

            var bodyFlow = new FlowSignal();
            ExecuteBlock(tokens, bodyStart, output, child, bodyFlow);

            if (bodyFlow.Kind == Signal.Break) break;
            if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; flow.ReturnValue = bodyFlow.ReturnValue; break; }
            // Continue: just proceed to increment

            ExecuteStatements(incrTokens, 0, output, child, new FlowSignal());
        }

        return bodyEnd;
    }

    private static int ExecuteForeach(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'foreach'
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen) pos++;

        // Skip type keyword(s)
        if (pos < tokens.Count) pos++; // var/type
        while (pos < tokens.Count && tokens[pos].Kind is not RazorTokenKind.Identifier and not RazorTokenKind.In) pos++;

        if (pos >= tokens.Count) return pos;
        var varName = tokens[pos].Value;
        pos++;

        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.In) pos++;

        var parser = new RazorExpressionParser(tokens, pos);
        var collection = parser.EvalExpression(scope.Resolve);
        pos = parser.Position;
        pos++; // skip )

        var bodyStart = pos;
        var bodyEnd = FindBlockEnd(tokens, pos);

        if (collection is IEnumerable enumerable)
        {
            var child = scope.CreateChild();
            foreach (var item in enumerable)
            {
                child.Set(varName, item);
                var bodyFlow = new FlowSignal();
                ExecuteBlock(tokens, bodyStart, output, child, bodyFlow);
                if (bodyFlow.Kind == Signal.Break) break;
                if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; flow.ReturnValue = bodyFlow.ReturnValue; break; }
            }
        }

        return bodyEnd;
    }

    private static int ExecuteWhile(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'while'
        pos++; // skip (

        var condStart = pos;
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseParen) pos++;
        var condExpr = string.Join(" ", tokens.GetRange(condStart, pos - condStart).Select(t => t.Value));
        pos++; // skip )

        var bodyStart = pos;
        var bodyEnd = FindBlockEnd(tokens, pos);

        var iterLimit = 10000;
        while (iterLimit-- > 0)
        {
            var condVal = RazorLightweightExpressionEvaluator.Evaluate(condExpr, scope.Resolve);
            if (!RazorExpressionParser.IsTruthy(condVal)) break;

            var bodyFlow = new FlowSignal();
            ExecuteBlock(tokens, bodyStart, output, scope, bodyFlow);
            if (bodyFlow.Kind == Signal.Break) break;
            if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; flow.ReturnValue = bodyFlow.ReturnValue; break; }
        }

        return bodyEnd;
    }

    private static int ExecuteDoWhile(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'do'

        var bodyStart = pos;
        var bodyEnd = FindBlockEnd(tokens, pos);

        pos = bodyEnd;
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.While) pos++;
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen) pos++;

        var condStart = pos;
        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseParen) pos++;
        var condExpr = string.Join(" ", tokens.GetRange(condStart, pos - condStart).Select(t => t.Value));
        pos++; // skip )
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;

        var iterLimit = 10000;
        do
        {
            var bodyFlow = new FlowSignal();
            ExecuteBlock(tokens, bodyStart, output, scope, bodyFlow);
            if (bodyFlow.Kind == Signal.Break) break;
            if (bodyFlow.Kind == Signal.Return) { flow.Kind = Signal.Return; flow.ReturnValue = bodyFlow.ReturnValue; break; }

            var condVal = RazorLightweightExpressionEvaluator.Evaluate(condExpr, scope.Resolve);
            if (!RazorExpressionParser.IsTruthy(condVal)) break;
        } while (iterLimit-- > 0);

        return pos;
    }

    private static int ExecuteSwitch(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'switch'
        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen) pos++;

        var parser = new RazorExpressionParser(tokens, pos);
        var switchVal = parser.EvalExpression(scope.Resolve);
        pos = parser.Position;
        pos++; // skip )
        pos++; // skip {

        var matched = false;
        var defaultStart = -1;

        while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseBrace)
        {
            if (tokens[pos].Kind == RazorTokenKind.Case)
            {
                pos++; // skip 'case'
                var caseParser = new RazorExpressionParser(tokens, pos);
                var caseVal = caseParser.EvalExpression(scope.Resolve);
                pos = caseParser.Position;
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Colon) pos++;

                if (!matched && Equals(switchVal?.ToString(), caseVal?.ToString()))
                {
                    matched = true;
                    while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.Break
                        && tokens[pos].Kind != RazorTokenKind.CloseBrace && flow.Kind == Signal.None)
                    {
                        pos = ExecuteStatement(tokens, pos, output, scope, flow);
                    }
                    if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Break)
                    {
                        pos++;
                        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Semicolon) pos++;
                    }
                }
                else
                {
                    pos = SkipUntilCaseOrBrace(tokens, pos);
                }
            }
            else if (tokens[pos].Kind == RazorTokenKind.Default)
            {
                pos++; // skip 'default'
                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Colon) pos++;
                defaultStart = pos;
                pos = SkipUntilCaseOrBrace(tokens, pos);
            }
            else pos++;
        }

        if (!matched && defaultStart >= 0)
        {
            var defPos = defaultStart;
            while (defPos < tokens.Count && tokens[defPos].Kind != RazorTokenKind.Break
                && tokens[defPos].Kind != RazorTokenKind.CloseBrace && flow.Kind == Signal.None)
            {
                defPos = ExecuteStatement(tokens, defPos, output, scope, flow);
            }
        }

        if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.CloseBrace) pos++;
        return pos;
    }

    private static int ExecuteTryCatch(List<RazorToken> tokens, int pos, StringBuilder output, InterpreterScope scope, FlowSignal flow)
    {
        pos++; // skip 'try'

        var tryStart = pos;
        var tryEnd = FindBlockEnd(tokens, pos);

        try
        {
            ExecuteBlock(tokens, tryStart, output, scope, flow);
        }
        catch (Exception ex)
        {
            pos = tryEnd;
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.Catch)
            {
                pos++; // skip 'catch'
                var catchScope = scope.CreateChild();

                if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen)
                {
                    pos++; // skip (
                    // Skip exception type
                    while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseParen)
                    {
                        if (tokens[pos].Kind == RazorTokenKind.Identifier)
                            catchScope.Set(tokens[pos].Value, ex);
                        pos++;
                    }
                    pos++; // skip )
                }

                ExecuteBlock(tokens, pos, output, catchScope, flow);
                pos = FindBlockEnd(tokens, pos);
            }
        }

        pos = tryEnd;
        // Skip to after catch/finally blocks
        while (pos < tokens.Count && (tokens[pos].Kind == RazorTokenKind.Catch || tokens[pos].Kind == RazorTokenKind.Finally))
        {
            pos++;
            if (pos < tokens.Count && tokens[pos].Kind == RazorTokenKind.OpenParen)
            {
                while (pos < tokens.Count && tokens[pos].Kind != RazorTokenKind.CloseParen) pos++;
                pos++;
            }
            pos = FindBlockEnd(tokens, pos);
        }

        return pos;
    }

    // ─────────────────────── Helpers ───────────────────────

    private static int SkipBlock(List<RazorToken> tokens, int pos) => FindBlockEnd(tokens, pos);

    private static int FindBlockEnd(List<RazorToken> tokens, int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Kind != RazorTokenKind.OpenBrace) return pos;
        var depth = 1;
        pos++;
        while (pos < tokens.Count && depth > 0)
        {
            if (tokens[pos].Kind == RazorTokenKind.OpenBrace) depth++;
            else if (tokens[pos].Kind == RazorTokenKind.CloseBrace) depth--;
            pos++;
        }
        return pos;
    }

    private static int SkipParenthesized(List<RazorToken> tokens, int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Kind != RazorTokenKind.OpenParen) return pos;
        var depth = 1;
        pos++;
        while (pos < tokens.Count && depth > 0)
        {
            if (tokens[pos].Kind == RazorTokenKind.OpenParen) depth++;
            else if (tokens[pos].Kind == RazorTokenKind.CloseParen) depth--;
            pos++;
        }
        return pos;
    }

    private static int SkipUntilCaseOrBrace(List<RazorToken> tokens, int pos)
    {
        var depth = 0;
        while (pos < tokens.Count)
        {
            if (tokens[pos].Kind == RazorTokenKind.OpenBrace) depth++;
            else if (tokens[pos].Kind == RazorTokenKind.CloseBrace) { if (depth == 0) break; depth--; }
            else if (depth == 0 && (tokens[pos].Kind == RazorTokenKind.Case || tokens[pos].Kind == RazorTokenKind.Default)) break;
            pos++;
        }
        return pos;
    }

    private static string[] SplitForHeader(string header)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] == '(' || header[i] == '[') depth++;
            else if (header[i] == ')' || header[i] == ']') depth--;
            else if (header[i] == ';' && depth == 0)
            {
                parts.Add(header[start..i]);
                start = i + 1;
            }
        }
        parts.Add(header[start..]);
        return parts.ToArray();
    }

    private static object? Increment(object? val) => val switch
    {
        int i => i + 1, long l => l + 1, double d => d + 1, float f => f + 1, decimal m => m + 1, _ => val
    };

    private static object? Decrement(object? val) => val switch
    {
        int i => i - 1, long l => l - 1, double d => d - 1, float f => f - 1, decimal m => m - 1, _ => val
    };

    private static object? AddValues(object? a, object? b)
    {
        if (a is int ai && b is int bi) return ai + bi;
        if (a is long al && b is long bl) return al + bl;
        return Convert.ToDouble(a ?? 0, CultureInfo.InvariantCulture) + Convert.ToDouble(b ?? 0, CultureInfo.InvariantCulture);
    }

    private static object? SubtractValues(object? a, object? b)
    {
        if (a is int ai && b is int bi) return ai - bi;
        return Convert.ToDouble(a ?? 0, CultureInfo.InvariantCulture) - Convert.ToDouble(b ?? 0, CultureInfo.InvariantCulture);
    }

    private static object? MultiplyValues(object? a, object? b)
    {
        if (a is int ai && b is int bi) return ai * bi;
        return Convert.ToDouble(a ?? 0, CultureInfo.InvariantCulture) * Convert.ToDouble(b ?? 0, CultureInfo.InvariantCulture);
    }

    private static object? DivideValues(object? a, object? b)
    {
        if (a is int ai && b is int bi) return bi != 0 ? ai / bi : 0;
        var divisor = Convert.ToDouble(b ?? 1, CultureInfo.InvariantCulture);
        if (divisor == 0) return 0;
        return Convert.ToDouble(a ?? 0, CultureInfo.InvariantCulture) / divisor;
    }
}
