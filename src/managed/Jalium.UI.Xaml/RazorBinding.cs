using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using Jalium.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Jalium.UI.Markup;

internal enum RazorSegmentKind
{
    Literal,
    Path,
    Expression,
    Code
}

internal sealed record RazorExpressionPlan(
    string Expression,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers);

internal sealed record RazorCodeBlockPlan(
    string Code,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers,
    IReadOnlyList<string> DeclaredIdentifiers);

internal sealed record RazorSegment(
    RazorSegmentKind Kind,
    string Text,
    RazorExpressionPlan? ExpressionPlan = null,
    RazorCodeBlockPlan? CodeBlockPlan = null);

internal sealed class RazorTemplate
{
    public RazorTemplate(IReadOnlyList<RazorSegment> segments)
    {
        Segments = segments;

        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var rootIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var knownLocals = new HashSet<string>(StringComparer.Ordinal);
        var outputSegments = new List<RazorSegment>();
        var renderedSegmentCount = 0;
        var hasRenderableLiteral = false;
        var hasCodeBlocks = false;

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case RazorSegmentKind.Literal:
                    if (!string.IsNullOrEmpty(segment.Text))
                    {
                        renderedSegmentCount++;
                        hasRenderableLiteral = true;
                    }
                    break;

                case RazorSegmentKind.Path:
                    renderedSegmentCount++;
                    outputSegments.Add(segment);
                    AddExternalDependency(segment.Text, knownLocals, dependencies, rootIdentifiers);
                    break;

                case RazorSegmentKind.Expression:
                    renderedSegmentCount++;
                    outputSegments.Add(segment);
                    if (segment.ExpressionPlan != null)
                    {
                        AddExternalDependencies(segment.ExpressionPlan.Dependencies, knownLocals, dependencies, rootIdentifiers);
                    }
                    break;

                case RazorSegmentKind.Code:
                    hasCodeBlocks = true;
                    if (segment.CodeBlockPlan != null)
                    {
                        AddExternalDependencies(segment.CodeBlockPlan.Dependencies, knownLocals, dependencies, rootIdentifiers);
                        foreach (var declaredIdentifier in segment.CodeBlockPlan.DeclaredIdentifiers)
                        {
                            if (!string.IsNullOrWhiteSpace(declaredIdentifier))
                            {
                                knownLocals.Add(declaredIdentifier);
                            }
                        }
                    }
                    break;
            }
        }

        Dependencies = dependencies.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        RootIdentifiers = rootIdentifiers.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        HasCodeBlocks = hasCodeBlocks;
        HasRenderableLiteral = hasRenderableLiteral;
        RenderedSegmentCount = renderedSegmentCount;
        SingleOutputSegment = !hasRenderableLiteral && outputSegments.Count == 1 ? outputSegments[0] : null;
    }

    public IReadOnlyList<RazorSegment> Segments { get; }

    public IReadOnlyList<string> Dependencies { get; }

    public IReadOnlyList<string> RootIdentifiers { get; }

    public bool HasCodeBlocks { get; }

    public bool HasRenderableLiteral { get; }

    public int RenderedSegmentCount { get; }

    public RazorSegment? SingleOutputSegment { get; }

    public bool HasDynamicSegments => Segments.Any(static s => s.Kind != RazorSegmentKind.Literal);

    public bool IsSinglePath =>
        Segments.Count == 1 && Segments[0].Kind == RazorSegmentKind.Path;

    public bool IsSingleExpression =>
        Segments.Count == 1 && Segments[0].Kind == RazorSegmentKind.Expression;

    public bool IsMixed =>
        HasDynamicSegments && RenderedSegmentCount > 1;

    public bool IsSingleComputedValue =>
        !HasRenderableLiteral && SingleOutputSegment != null;

    public string? SinglePath => IsSinglePath ? Segments[0].Text : null;

    public RazorExpressionPlan? SingleExpressionPlan =>
        IsSingleExpression ? Segments[0].ExpressionPlan : null;

    private static void AddExternalDependencies(
        IEnumerable<string> candidateDependencies,
        HashSet<string> knownLocals,
        HashSet<string> dependencies,
        HashSet<string> rootIdentifiers)
    {
        foreach (var candidate in candidateDependencies)
        {
            AddExternalDependency(candidate, knownLocals, dependencies, rootIdentifiers);
        }
    }

    private static void AddExternalDependency(
        string candidateDependency,
        HashSet<string> knownLocals,
        HashSet<string> dependencies,
        HashSet<string> rootIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(candidateDependency))
            return;

        var rootIdentifier = GetRootIdentifier(candidateDependency);
        if (string.IsNullOrWhiteSpace(rootIdentifier) || knownLocals.Contains(rootIdentifier))
            return;

        dependencies.Add(candidateDependency);
        rootIdentifiers.Add(rootIdentifier);
    }

    internal static string GetRootIdentifier(string path)
    {
        var dotIndex = path.IndexOf('.');
        var bracketIndex = path.IndexOf('[');
        if (dotIndex < 0)
            return bracketIndex < 0 ? path : path[..bracketIndex];

        if (bracketIndex < 0)
            return path[..dotIndex];

        var endIndex = Math.Min(dotIndex, bracketIndex);
        return path[..endIndex];
    }
}

internal static class RazorTemplateParser
{
    private static readonly HashSet<char> PathChars = new(
    [
        '.', '_', '[', ']', '$'
    ]);

    public static RazorTemplate Parse(string value)
    {
        var segments = new List<RazorSegment>();
        var literal = new StringBuilder();
        var i = 0;

        while (i < value.Length)
        {
            var current = value[i];

            if (current == '\\' && i + 1 < value.Length && value[i + 1] == '@')
            {
                literal.Append('@');
                i += 2;
                continue;
            }

            if (current == '@')
            {
                if (i + 1 < value.Length && value[i + 1] == '@')
                {
                    literal.Append('@');
                    i += 2;
                    continue;
                }

                FlushLiteral(segments, literal);

                if (i + 1 < value.Length && value[i + 1] == '(')
                {
                    var expression = ParseExpression(value, ref i);
                    var plan = RazorExpressionAnalyzer.GetPlan(expression);
                    segments.Add(new RazorSegment(RazorSegmentKind.Expression, expression, plan));
                    continue;
                }

                if (i + 1 < value.Length && value[i + 1] == '{')
                {
                    var code = ParseCodeBlock(value, ref i);
                    var plan = RazorCodeBlockAnalyzer.GetPlan(code);
                    segments.Add(new RazorSegment(RazorSegmentKind.Code, code, null, plan));
                    continue;
                }

                var path = ParsePath(value, ref i);
                if (string.IsNullOrWhiteSpace(path))
                {
                    literal.Append('@');
                    i++;
                    continue;
                }

                segments.Add(new RazorSegment(RazorSegmentKind.Path, path));
                continue;
            }

            literal.Append(current);
            i++;
        }

        FlushLiteral(segments, literal);
        return new RazorTemplate(segments);
    }

    private static void FlushLiteral(List<RazorSegment> segments, StringBuilder literal)
    {
        if (literal.Length == 0)
            return;

        segments.Add(new RazorSegment(RazorSegmentKind.Literal, literal.ToString()));
        literal.Clear();
    }

    private static string ParsePath(string input, ref int i)
    {
        var start = i + 1;
        if (start >= input.Length || !IsPathStart(input[start]))
            return string.Empty;

        var pos = start + 1;

        while (pos < input.Length)
        {
            var c = input[pos];
            if (IsPathPart(c))
            {
                pos++;
                continue;
            }

            break;
        }

        i = pos;
        return input[start..pos];
    }

    private static bool IsPathStart(char c) =>
        c == '_' || c == '$' || char.IsLetter(c);

    private static bool IsPathPart(char c) =>
        char.IsLetterOrDigit(c) || PathChars.Contains(c);

    private static string ParseExpression(string input, ref int i)
    {
        return ParseDelimitedCSharp(input, ref i, i + 2, '(', ')', "Unclosed Razor expression. Expected closing ')'.");
    }

    private static string ParseCodeBlock(string input, ref int i)
    {
        return ParseDelimitedCSharp(input, ref i, i + 2, '{', '}', "Unclosed Razor code block. Expected closing '}'.");
    }

    private static string ParseDelimitedCSharp(
        string input,
        ref int i,
        int start,
        char openChar,
        char closeChar,
        string errorMessage)
    {
        var pos = start;
        var depth = 1;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;
        var verbatimString = false;
        char stringQuote = '\0';

        while (pos < input.Length)
        {
            var current = input[pos];
            var next = pos + 1 < input.Length ? input[pos + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                }

                pos++;
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    pos += 2;
                    continue;
                }

                pos++;
                continue;
            }

            if (inString)
            {
                if (!verbatimString && escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (!verbatimString && current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == stringQuote)
                {
                    if (verbatimString && next == stringQuote)
                    {
                        pos += 2;
                        continue;
                    }

                    inString = false;
                    verbatimString = false;
                    stringQuote = '\0';
                }

                pos++;
                continue;
            }

            if (inChar)
            {
                if (escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == '\'')
                {
                    inChar = false;
                }

                pos++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                pos += 2;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                pos += 2;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                pos++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                verbatimString =
                    (pos > 0 && input[pos - 1] == '@')
                    || (pos > 1 && ((input[pos - 1] == '$' && input[pos - 2] == '@') || (input[pos - 1] == '@' && input[pos - 2] == '$')));
                stringQuote = current;
                pos++;
                continue;
            }

            if (current == openChar)
            {
                depth++;
                pos++;
                continue;
            }

            if (current == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    var result = input[start..pos].Trim();
                    i = pos + 1;
                    return result;
                }

                pos++;
                continue;
            }

            pos++;
        }

        throw new XamlParseException(errorMessage);
    }
}

internal static class RazorCodeBlockAnalyzer
{
    public static RazorCodeBlockPlan GetPlan(string code)
    {
        var analysis = RazorCSharpDependencyAnalyzer.AnalyzeCodeBlock(code);
        return new RazorCodeBlockPlan(
            code,
            analysis.Dependencies,
            analysis.RootIdentifiers,
            analysis.DeclaredIdentifiers);
    }
}

internal static class RazorExpressionAnalyzer
{
    public static RazorExpressionPlan GetPlan(string expression)
    {
        if (RazorExpressionRegistry.TryGetMetadata(expression, out var metadata))
        {
            var rootsFromMetadata = metadata.Dependencies
                .Select(RazorCSharpDependencyAnalyzer.GetRootIdentifier)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new RazorExpressionPlan(
                expression,
                metadata.Dependencies,
                rootsFromMetadata);
        }

        var analysis = RazorCSharpDependencyAnalyzer.AnalyzeExpression(expression);

        return new RazorExpressionPlan(
            expression,
            analysis.Dependencies,
            analysis.RootIdentifiers);
    }
}

internal sealed record RazorCSharpDependencyAnalysis(
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers,
    IReadOnlyList<string> DeclaredIdentifiers);

internal static class RazorCSharpDependencyAnalyzer
{
    private static readonly CSharpParseOptions ScriptParseOptions = CSharpParseOptions.Default.WithKind(SourceCodeKind.Script);

    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
        "dynamic", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float",
        "for", "foreach", "global", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "not", "null", "object", "operator", "or", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "var", "virtual", "void", "volatile", "when", "while", "with"
    };

    public static RazorCSharpDependencyAnalysis AnalyzeExpression(string expression)
    {
        var syntax = SyntaxFactory.ParseExpression(expression);
        return AnalyzeCore(syntax);
    }

    public static RazorCSharpDependencyAnalysis AnalyzeCodeBlock(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, ScriptParseOptions);
        return AnalyzeCore(tree.GetRoot());
    }

    private static RazorCSharpDependencyAnalysis AnalyzeCore(SyntaxNode root)
    {
        var declaredIdentifiers = CollectDeclaredIdentifiers(root);
        var dependencies = CollectDependencies(root, declaredIdentifiers);
        var rootIdentifiers = dependencies
            .Select(GetRootIdentifier)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        return new RazorCSharpDependencyAnalysis(
            dependencies.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            rootIdentifiers,
            declaredIdentifiers.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
    }

    private static HashSet<string> CollectDeclaredIdentifiers(SyntaxNode root)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declarator in root.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
        {
            if (!declarator.Identifier.IsMissing && !string.IsNullOrWhiteSpace(declarator.Identifier.ValueText))
            {
                declared.Add(declarator.Identifier.ValueText);
            }
        }

        foreach (var parameter in root.DescendantNodesAndSelf().OfType<ParameterSyntax>())
        {
            if (!parameter.Identifier.IsMissing && !string.IsNullOrWhiteSpace(parameter.Identifier.ValueText))
            {
                declared.Add(parameter.Identifier.ValueText);
            }
        }

        foreach (var foreachStatement in root.DescendantNodesAndSelf().OfType<ForEachStatementSyntax>())
        {
            if (!foreachStatement.Identifier.IsMissing && !string.IsNullOrWhiteSpace(foreachStatement.Identifier.ValueText))
            {
                declared.Add(foreachStatement.Identifier.ValueText);
            }
        }

        foreach (var catchDeclaration in root.DescendantNodesAndSelf().OfType<CatchDeclarationSyntax>())
        {
            if (!catchDeclaration.Identifier.IsMissing && !string.IsNullOrWhiteSpace(catchDeclaration.Identifier.ValueText))
            {
                declared.Add(catchDeclaration.Identifier.ValueText);
            }
        }

        foreach (var localFunction in root.DescendantNodesAndSelf().OfType<LocalFunctionStatementSyntax>())
        {
            if (!localFunction.Identifier.IsMissing && !string.IsNullOrWhiteSpace(localFunction.Identifier.ValueText))
            {
                declared.Add(localFunction.Identifier.ValueText);
            }
        }

        foreach (var designation in root.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
        {
            if (!designation.Identifier.IsMissing && !string.IsNullOrWhiteSpace(designation.Identifier.ValueText))
            {
                declared.Add(designation.Identifier.ValueText);
            }
        }

        return declared;
    }

    private static HashSet<string> CollectDependencies(SyntaxNode root, HashSet<string> declaredIdentifiers)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var memberAccess in root.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Parent is MemberAccessExpressionSyntax parentMemberAccess && parentMemberAccess.Expression == memberAccess)
                continue;

            if (!TryGetMemberAccessPath(memberAccess, declaredIdentifiers, out var path))
                continue;

            var isInvocation = memberAccess.Parent is InvocationExpressionSyntax invocation && invocation.Expression == memberAccess;
            AddDependency(dependencies, path, isInvocation);
        }

        foreach (var identifier in root.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Parent is MemberAccessExpressionSyntax)
                continue;

            if (ShouldSkipIdentifier(identifier, declaredIdentifiers))
                continue;

            var isInvocation = identifier.Parent is InvocationExpressionSyntax invocation && invocation.Expression == identifier;
            AddDependency(dependencies, identifier.Identifier.ValueText, isInvocation);
        }

        return dependencies;
    }

    private static bool TryGetMemberAccessPath(
        ExpressionSyntax expression,
        HashSet<string> declaredIdentifiers,
        out string path)
    {
        var parts = new Stack<string>();
        ExpressionSyntax? current = expression;

        while (current != null)
        {
            switch (current)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    parts.Push(memberAccess.Name.Identifier.ValueText);
                    current = memberAccess.Expression;
                    continue;

                case ElementAccessExpressionSyntax elementAccess:
                    current = elementAccess.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case IdentifierNameSyntax identifier:
                    if (ShouldSkipIdentifier(identifier, declaredIdentifiers))
                    {
                        path = string.Empty;
                        return false;
                    }

                    parts.Push(identifier.Identifier.ValueText);
                    path = string.Join(".", parts);
                    return true;

                default:
                    path = string.Empty;
                    return false;
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool ShouldSkipIdentifier(IdentifierNameSyntax identifier, HashSet<string> declaredIdentifiers)
    {
        var value = identifier.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(value) || declaredIdentifiers.Contains(value) || ReservedIdentifiers.Contains(value))
            return true;

        if (identifier.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NameColonSyntax)
            return true;

        if (IsInTypeContext(identifier))
            return true;

        if (IsInsideNameOf(identifier))
            return true;

        return false;
    }

    private static bool IsInTypeContext(IdentifierNameSyntax identifier)
    {
        return identifier.Parent switch
        {
            VariableDeclarationSyntax variableDeclaration => variableDeclaration.Type == identifier,
            ParameterSyntax parameter => parameter.Type == identifier,
            ForEachStatementSyntax foreachStatement => foreachStatement.Type == identifier,
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Type == identifier,
            CastExpressionSyntax castExpression => castExpression.Type == identifier,
            TypeOfExpressionSyntax typeOfExpression => typeOfExpression.Type == identifier,
            DefaultExpressionSyntax defaultExpression => defaultExpression.Type == identifier,
            RefTypeSyntax refType => refType.Type == identifier,
            NullableTypeSyntax nullableType => nullableType.ElementType == identifier,
            _ => false
        };
    }

    private static bool IsInsideNameOf(IdentifierNameSyntax identifier)
    {
        if (identifier.Parent is not ArgumentSyntax argument || argument.Parent is not ArgumentListSyntax argumentList || argumentList.Parent is not InvocationExpressionSyntax invocation)
            return false;

        return invocation.Expression is IdentifierNameSyntax invokedName
            && string.Equals(invokedName.Identifier.ValueText, "nameof", StringComparison.Ordinal);
    }

    private static void AddDependency(HashSet<string> dependencies, string path, bool isInvocation)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (path.Contains("::", StringComparison.Ordinal))
            return;

        if (isInvocation)
        {
            var lastDot = path.LastIndexOf('.');
            if (lastDot > 0)
            {
                path = path[..lastDot];
            }
            else
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            dependencies.Add(path);
        }
    }

    internal static string GetRootIdentifier(string path)
    {
        var dotIndex = path.IndexOf('.');
        var bracketIndex = path.IndexOf('[');
        if (dotIndex < 0)
            return bracketIndex < 0 ? path : path[..bracketIndex];

        if (bracketIndex < 0)
            return path[..dotIndex];

        var endIndex = Math.Min(dotIndex, bracketIndex);
        return path[..endIndex];
    }
}

public sealed class RazorScriptGlobals
{
    public required Func<string, object?> Resolve { get; init; }
}

internal static class RazorEvaluationGuards
{
    public static bool ShouldShortCircuitMissingRoot(RazorExpressionPlan plan)
    {
        // Expressions that explicitly handle null should still be evaluated.
        return plan.Expression.IndexOf("null", StringComparison.OrdinalIgnoreCase) < 0;
    }

    public static bool ShouldShortCircuitMissingRoot(RazorTemplate template)
    {
        foreach (var segment in template.Segments)
        {
            if (segment.Text.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        return true;
    }

    public static bool IsUnavailableBindingValue(object? value)
    {
        return ReferenceEquals(value, DependencyProperty.UnsetValue)
            || ReferenceEquals(value, Binding.UnsetValue)
            || ReferenceEquals(value, Binding.DoNothing);
    }

    public static bool IsMissingRootValue(object? value)
    {
        return value == null || IsUnavailableBindingValue(value);
    }

    public static bool HasMissingRootValue(RazorExpressionPlan plan, Func<string, object?> resolver)
    {
        foreach (var root in plan.RootIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var value = resolver(root);
            if (IsMissingRootValue(value))
                return true;
        }

        return false;
    }

    public static bool HasMissingRootValue(RazorTemplate template, Func<string, object?> resolver)
    {
        foreach (var root in template.RootIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var value = resolver(root);
            if (IsMissingRootValue(value))
                return true;
        }

        return false;
    }
}

internal static class RazorExpressionRuntimeCompiler
{
    private sealed class CompiledExpression
    {
        public required ScriptRunner<object?> Runner { get; init; }
    }

    private static readonly ConcurrentDictionary<string, CompiledExpression> Cache = new(StringComparer.Ordinal);

    public static void EnsureCompiled(RazorExpressionPlan plan)
    {
        var key = $"{plan.Expression}::{string.Join("|", plan.RootIdentifiers)}";
        _ = Cache.GetOrAdd(key, _ => Compile(plan));
    }

    public static object? Evaluate(RazorExpressionPlan plan, Func<string, object?> resolver)
    {
        var key = $"{plan.Expression}::{string.Join("|", plan.RootIdentifiers)}";
        var compiled = Cache.GetOrAdd(key, _ => Compile(plan));
        var globals = new RazorScriptGlobals { Resolve = resolver };
        return compiled.Runner(globals).GetAwaiter().GetResult();
    }

    public static bool TryEvaluate(RazorExpressionPlan plan, Func<string, object?> resolver, out object? value)
    {
        if (RazorEvaluationGuards.ShouldShortCircuitMissingRoot(plan) &&
            RazorEvaluationGuards.HasMissingRootValue(plan, resolver))
        {
            value = null;
            return false;
        }

        try
        {
            value = Evaluate(plan, resolver);
            return true;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex) when (IsTransientNullBindingError(ex))
        {
            value = null;
            return false;
        }
        catch (NullReferenceException)
        {
            value = null;
            return false;
        }
    }

    private static CompiledExpression Compile(RazorExpressionPlan plan)
    {
        var scriptBody = BuildScriptBody(plan);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Cast<Assembly>()
            .ToList();

        var csharpAssembly = TryLoadAssembly("Microsoft.CSharp");
        if (csharpAssembly != null)
        {
            references.Add(csharpAssembly);
        }

        var distinctReferences = references
            .GroupBy(static a => a.FullName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToArray();

        var options = ScriptOptions.Default
            .WithReferences(distinctReferences)
            .WithImports("System", "System.Linq", "System.Collections.Generic");

        try
        {
            var script = CSharpScript.Create<object?>(scriptBody, options, typeof(RazorScriptGlobals));
            var diagnostics = script.Compile();
            var errors = diagnostics.Where(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
            {
                var message = string.Join(Environment.NewLine, errors.Select(static d => d.ToString()));
                throw new XamlParseException($"Razor expression compile failed: {message}");
            }

            return new CompiledExpression
            {
                Runner = script.CreateDelegate()
            };
        }
        catch (CompilationErrorException ex)
        {
            throw new XamlParseException($"Razor expression compile failed: {string.Join(Environment.NewLine, ex.Diagnostics)}", ex);
        }
    }

    private static Assembly? TryLoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(assemblyName);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildScriptBody(RazorExpressionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("var __resolver = Resolve;");
        foreach (var root in plan.RootIdentifiers.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (!IsValidIdentifier(root))
                continue;

            sb.Append("dynamic ").Append(root).Append(" = __resolver(\"")
                .Append(root.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
                .AppendLine("\");");
        }

        sb.Append("return (object?)(").Append(plan.Expression).AppendLine(");");
        return sb.ToString();
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!(value[0] == '_' || char.IsLetter(value[0])))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }

    private static bool IsTransientNullBindingError(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
    {
        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // DataContext can be null during early binding evaluation.
        return message.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class RazorTemplateRuntimeCompiler
{
    private sealed class CompiledTemplate
    {
        public required ScriptRunner<object?[]> Runner { get; init; }
    }

    private static readonly ConcurrentDictionary<string, CompiledTemplate> Cache = new(StringComparer.Ordinal);

    public static void EnsureCompiled(RazorTemplate template)
    {
        if (!template.HasCodeBlocks)
            return;

        var key = BuildCacheKey(template);
        _ = Cache.GetOrAdd(key, _ => Compile(template));
    }

    public static bool TryEvaluate(RazorTemplate template, Func<string, object?> resolver, out object? value)
    {
        if (RazorEvaluationGuards.ShouldShortCircuitMissingRoot(template) &&
            RazorEvaluationGuards.HasMissingRootValue(template, resolver))
        {
            value = null;
            return false;
        }

        try
        {
            var parts = EvaluateParts(template, resolver);
            value = CollapseResult(template, parts);
            return true;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex) when (IsTransientNullBindingError(ex))
        {
            value = null;
            return false;
        }
        catch (NullReferenceException)
        {
            value = null;
            return false;
        }
    }

    private static object?[] EvaluateParts(RazorTemplate template, Func<string, object?> resolver)
    {
        var key = BuildCacheKey(template);
        var compiled = Cache.GetOrAdd(key, _ => Compile(template));
        var globals = new RazorScriptGlobals { Resolve = resolver };
        return compiled.Runner(globals).GetAwaiter().GetResult();
    }

    private static object? CollapseResult(RazorTemplate template, object?[] parts)
    {
        if (template.IsSingleComputedValue)
            return parts.Length == 0 ? null : parts[0];

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part?.ToString() ?? string.Empty);
        }

        return sb.ToString();
    }

    private static string BuildCacheKey(RazorTemplate template)
    {
        var sb = new StringBuilder();
        foreach (var root in template.RootIdentifiers)
        {
            sb.Append(root).Append('|');
        }

        sb.Append("::");

        foreach (var segment in template.Segments)
        {
            sb.Append((int)segment.Kind).Append(':').Append(segment.Text).Append("||");
        }

        return sb.ToString();
    }

    private static CompiledTemplate Compile(RazorTemplate template)
    {
        var scriptBody = BuildScriptBody(template);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Cast<Assembly>()
            .ToList();

        TryAddReference(references, "Microsoft.CSharp");

        var distinctReferences = references
            .GroupBy(static a => a.FullName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToArray();

        var options = ScriptOptions.Default
            .WithReferences(distinctReferences)
            .WithImports("System", "System.Linq", "System.Collections.Generic");

        try
        {
            var script = CSharpScript.Create<object?[]>(scriptBody, options, typeof(RazorScriptGlobals));
            var diagnostics = script.Compile();
            var errors = diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
            {
                var message = string.Join(Environment.NewLine, errors.Select(static d => d.ToString()));
                throw new XamlParseException($"Razor template compile failed: {message}");
            }

            return new CompiledTemplate
            {
                Runner = script.CreateDelegate()
            };
        }
        catch (CompilationErrorException ex)
        {
            throw new XamlParseException($"Razor template compile failed: {string.Join(Environment.NewLine, ex.Diagnostics)}", ex);
        }
    }

    private static string BuildScriptBody(RazorTemplate template)
    {
        var sb = new StringBuilder();
        sb.AppendLine("var __resolver = Resolve;");
        foreach (var root in template.RootIdentifiers.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(root) || !IsValidIdentifier(root))
                continue;

            sb.Append("dynamic ").Append(root).Append(" = __resolver(\"")
                .Append(root.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
                .AppendLine("\");");
        }

        sb.AppendLine("var __parts = new System.Collections.Generic.List<object?>();");
        sb.AppendLine("void Write(object? value) => __parts.Add(value);");
        sb.AppendLine("void WriteLiteral(string value) => __parts.Add(value);");

        foreach (var segment in template.Segments)
        {
            switch (segment.Kind)
            {
                case RazorSegmentKind.Literal:
                    sb.Append("WriteLiteral(").Append(ToStringLiteral(segment.Text)).AppendLine(");");
                    break;

                case RazorSegmentKind.Path:
                case RazorSegmentKind.Expression:
                    sb.Append("Write((object?)(").Append(segment.Text).AppendLine("));");
                    break;

                case RazorSegmentKind.Code:
                    sb.AppendLine(segment.Text);
                    break;
            }
        }

        sb.AppendLine("return __parts.ToArray();");
        return sb.ToString();
    }

    private static void TryAddReference(List<Assembly> references, string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            references.Add(assembly);
        }
        catch
        {
            // Ignore optional references that are unavailable in the current context.
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!(value[0] == '_' || char.IsLetter(value[0])))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }

    private static string ToStringLiteral(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }

    private static bool IsTransientNullBindingError(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
    {
        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class RazorValueResolver
{
    public static object? Resolve(
        object targetObject,
        object? codeBehind,
        string path)
    {
        if (TryResolveDataContext(targetObject, path, out var foundInDataContext, out var valueFromDataContext) && foundInDataContext)
        {
            if (!RazorEvaluationGuards.IsUnavailableBindingValue(valueFromDataContext))
                return valueFromDataContext;
        }

        if (codeBehind != null && TryResolvePath(codeBehind, path, out var foundInCodeBehind, out var valueFromCodeBehind) && foundInCodeBehind)
        {
            return valueFromCodeBehind;
        }

        return null;
    }

    private static bool TryResolveDataContext(object targetObject, string path, out bool found, out object? value)
    {
        if (targetObject is FrameworkElement fe)
        {
            FrameworkElement? current = fe;
            while (current != null)
            {
                if (current.DataContext != null)
                {
                    return TryResolvePath(current.DataContext, path, out found, out value);
                }

                current = current.VisualParent as FrameworkElement;
            }
        }

        found = false;
        value = null;
        return false;
    }

    private static bool TryResolvePath(object? source, string path, out bool found, out object? value)
    {
        found = false;
        value = null;

        if (source == null)
            return false;

        if (string.IsNullOrWhiteSpace(path))
        {
            found = true;
            value = source;
            return true;
        }

        object? current = source;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            if (current == null)
            {
                found = true;
                value = null;
                return true;
            }

            if (!TryReadMember(current, segment, out var memberValue))
            {
                found = false;
                value = null;
                return false;
            }

            current = memberValue;
            found = true;
        }

        value = current;
        return true;
    }

    private static bool TryReadMember(object source, string memberName, out object? value)
    {
        if (source is DependencyObject dependencyObject)
        {
            var dpField = source.GetType().GetField(
                memberName + "Property",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty dependencyProperty)
            {
                value = dependencyObject.GetValue(dependencyProperty);
                return true;
            }
        }

        var property = source.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            value = property.GetValue(source);
            return true;
        }

        var field = source.GetType().GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            value = field.GetValue(source);
            return true;
        }

        value = null;
        return false;
    }
}

internal sealed class RazorTemplateConverter : IMultiValueConverter
{
    private readonly RazorTemplate _template;
    private readonly WeakReference<object> _targetRef;
    private readonly object? _codeBehind;

    public RazorTemplateConverter(RazorTemplate template, object targetObject, object? codeBehind)
    {
        _template = template;
        _targetRef = new WeakReference<object>(targetObject);
        _codeBehind = codeBehind;
    }

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!_targetRef.TryGetTarget(out var target))
            return null;

        object? result;
        if (_template.HasCodeBlocks)
        {
            result = EvaluateScriptTemplate(target);
            return ConvertToTargetType(result, targetType);
        }

        if (_template.IsSingleExpression && _template.SingleExpressionPlan != null)
        {
            result = EvaluateExpression(_template.SingleExpressionPlan, target);
            return ConvertToTargetType(result, targetType);
        }

        if (_template.IsSinglePath && _template.SinglePath != null)
        {
            result = RazorValueResolver.Resolve(target, _codeBehind, _template.SinglePath);
            return ConvertToTargetType(result, targetType);
        }

        var sb = new StringBuilder();
        foreach (var segment in _template.Segments)
        {
            switch (segment.Kind)
            {
                case RazorSegmentKind.Literal:
                    sb.Append(segment.Text);
                    break;
                case RazorSegmentKind.Path:
                    var resolvedPath = RazorValueResolver.Resolve(target, _codeBehind, segment.Text);
                    sb.Append(resolvedPath?.ToString() ?? string.Empty);
                    break;
                case RazorSegmentKind.Expression:
                    if (segment.ExpressionPlan != null)
                    {
                        var evaluated = EvaluateExpression(segment.ExpressionPlan, target);
                        sb.Append(evaluated?.ToString() ?? string.Empty);
                    }
                    break;

                case RazorSegmentKind.Code:
                    break;
            }
        }

        result = sb.ToString();
        return ConvertToTargetType(result, targetType);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        var result = new object?[targetTypes.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = DependencyProperty.UnsetValue;
        }

        return result;
    }

    private object? EvaluateExpression(RazorExpressionPlan plan, object target)
    {
        if (ShouldShortCircuitMissingRoot(plan) && HasMissingRootValue(plan, target))
            return null;

        if (!RazorExpressionRuntimeCompiler.TryEvaluate(
                plan,
                path => RazorValueResolver.Resolve(target, _codeBehind, path),
                out var value))
        {
            return null;
        }

        return value;
    }

    private object? EvaluateScriptTemplate(object target)
    {
        if (!RazorTemplateRuntimeCompiler.TryEvaluate(
                _template,
                path => RazorValueResolver.Resolve(target, _codeBehind, path),
                out var value))
        {
            return null;
        }

        return value;
    }

    private bool HasMissingRootValue(RazorExpressionPlan plan, object target)
    {
        foreach (var root in plan.RootIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var value = RazorValueResolver.Resolve(target, _codeBehind, root);
            if (RazorEvaluationGuards.IsMissingRootValue(value))
                return true;
        }

        return false;
    }

    private static bool ShouldShortCircuitMissingRoot(RazorExpressionPlan plan)
    {
        return RazorEvaluationGuards.ShouldShortCircuitMissingRoot(plan);
    }

    private static object? ConvertToTargetType(object? value, Type targetType)
    {
        var coerced = BindingValueCoercion.Coerce(value, targetType, CultureInfo.InvariantCulture);
        if (coerced is string stringValue)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlyingType != typeof(string) && !underlyingType.IsInstanceOfType(coerced))
            {
                return TypeConverterRegistry.ConvertValue(stringValue, underlyingType) ?? coerced;
            }
        }

        return coerced;
    }
}

internal sealed class RazorConditionalVisibilityConverter : IMultiValueConverter
{
    private readonly RazorExpressionPlan _plan;
    private readonly WeakReference<object> _targetRef;
    private readonly object? _codeBehind;

    public RazorConditionalVisibilityConverter(RazorExpressionPlan plan, object targetObject, object? codeBehind)
    {
        _plan = plan;
        _targetRef = new WeakReference<object>(targetObject);
        _codeBehind = codeBehind;
    }

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!_targetRef.TryGetTarget(out var target))
            return Visibility.Collapsed;

        if (ShouldShortCircuitMissingRoot(_plan) && HasMissingRootValue(target))
            return Visibility.Collapsed;

        if (!RazorExpressionRuntimeCompiler.TryEvaluate(
                _plan,
                path => RazorValueResolver.Resolve(target, _codeBehind, path),
                out var raw))
        {
            return Visibility.Collapsed;
        }

        return CoerceToBoolean(raw) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        var result = new object?[targetTypes.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = DependencyProperty.UnsetValue;
        }

        return result;
    }

    private static bool CoerceToBoolean(object? value)
    {
        if (value == null)
            return false;

        if (value is bool b)
            return b;

        if (value is string s)
        {
            return bool.TryParse(s, out var parsed) && parsed;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                return convertible.ToDouble(CultureInfo.InvariantCulture) != 0d;
            }
            catch
            {
                // Fall through to default truthy check.
            }
        }

        return true;
    }

    private bool HasMissingRootValue(object target)
    {
        foreach (var root in _plan.RootIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var value = RazorValueResolver.Resolve(target, _codeBehind, root);
            if (RazorEvaluationGuards.IsMissingRootValue(value))
                return true;
        }

        return false;
    }

    private static bool ShouldShortCircuitMissingRoot(RazorExpressionPlan plan)
    {
        return RazorEvaluationGuards.ShouldShortCircuitMissingRoot(plan);
    }
}

internal static class RazorBindingEngine
{
    internal static bool TryApplyIfVisibility(
        object child,
        string conditionExpression,
        XamlParserContext context)
    {
        if (child is not UIElement uiElement || string.IsNullOrWhiteSpace(conditionExpression))
            return false;

        var plan = RazorExpressionAnalyzer.GetPlan(conditionExpression);
        RazorExpressionRuntimeCompiler.EnsureCompiled(plan);

        var binding = new MultiBinding
        {
            Converter = new RazorConditionalVisibilityConverter(plan, uiElement, context.CodeBehindInstance)
        };

        foreach (var dependency in plan.Dependencies)
        {
            binding.Bindings.Add(CreatePreferredPathBinding(dependency, context.CodeBehindInstance));
        }

        TryAddDataContextTriggerBinding(binding, uiElement);
        uiElement.SetBinding(UIElement.VisibilityProperty, binding);
        return true;
    }

    internal static bool EvaluateConditionOnce(
        object targetObject,
        object? codeBehind,
        string conditionExpression)
    {
        if (string.IsNullOrWhiteSpace(conditionExpression))
            return false;

        var plan = RazorExpressionAnalyzer.GetPlan(conditionExpression);
        RazorExpressionRuntimeCompiler.EnsureCompiled(plan);
        if (!RazorExpressionRuntimeCompiler.TryEvaluate(
                plan,
                path => RazorValueResolver.Resolve(targetObject, codeBehind, path),
                out var value))
        {
            return false;
        }

        if (value is bool b)
            return b;

        if (value is string s)
            return bool.TryParse(s, out var parsed) && parsed;

        if (value is IConvertible convertible)
        {
            try
            {
                return convertible.ToDouble(CultureInfo.InvariantCulture) != 0d;
            }
            catch
            {
                return value != null;
            }
        }

        return value != null;
    }

    public static bool TryApplyRazorValue(
        object instance,
        PropertyInfo property,
        string rawValue,
        XamlParserContext context,
        XmlReader? reader)
    {
        if (string.IsNullOrEmpty(rawValue) || !rawValue.Contains('@', StringComparison.Ordinal))
            return false;

        var template = RazorTemplateParser.Parse(rawValue);
        if (!template.HasDynamicSegments)
        {
            var collapsedLiteral = EvaluateTemplateOnce(template, instance, context.CodeBehindInstance);
            if (collapsedLiteral is string collapsedString &&
                string.Equals(collapsedString, rawValue, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryFindDependencyProperty(instance.GetType(), property.Name, out var literalProperty) || instance is not DependencyObject literalTarget)
            {
                property.SetValue(instance, ConvertOnceValue(collapsedLiteral, property.PropertyType));
                return true;
            }

            literalTarget.SetValue(literalProperty, ConvertOnceValue(collapsedLiteral, property.PropertyType));
            return true;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (template.IsMixed && targetType != typeof(string) && targetType != typeof(object))
        {
            throw CreateMixedValueException(property.Name, rawValue, reader);
        }

        if (!TryFindDependencyProperty(instance.GetType(), property.Name, out var dependencyProperty) || instance is not DependencyObject dependencyObject)
        {
            // CLR-only property fallback: evaluate once and assign.
            var resolved = EvaluateTemplateOnce(template, instance, context.CodeBehindInstance);
            property.SetValue(instance, ConvertOnceValue(resolved, property.PropertyType));
            return true;
        }

        var binding = CreateBinding(template, instance, context.CodeBehindInstance);
        dependencyObject.SetBinding(dependencyProperty, binding);
        return true;
    }

    private static object? EvaluateTemplateOnce(RazorTemplate template, object target, object? codeBehind)
    {
        if (template.HasCodeBlocks)
        {
            if (!RazorTemplateRuntimeCompiler.TryEvaluate(
                    template,
                    path => RazorValueResolver.Resolve(target, codeBehind, path),
                    out var scriptValue))
            {
                return null;
            }

            return scriptValue;
        }

        if (template.IsSinglePath && template.SinglePath != null)
            return RazorValueResolver.Resolve(target, codeBehind, template.SinglePath);

        if (template.IsSingleExpression && template.SingleExpressionPlan != null)
        {
            if (ShouldShortCircuitMissingRoot(template.SingleExpressionPlan) &&
                HasMissingRootValue(template.SingleExpressionPlan, target, codeBehind))
            {
                return null;
            }

            if (!RazorExpressionRuntimeCompiler.TryEvaluate(
                    template.SingleExpressionPlan,
                    path => RazorValueResolver.Resolve(target, codeBehind, path),
                    out var expressionValue))
            {
                return null;
            }

            return expressionValue;
        }

        var sb = new StringBuilder();
        foreach (var segment in template.Segments)
        {
            switch (segment.Kind)
            {
                case RazorSegmentKind.Literal:
                    sb.Append(segment.Text);
                    break;
                case RazorSegmentKind.Path:
                    sb.Append(RazorValueResolver.Resolve(target, codeBehind, segment.Text)?.ToString() ?? string.Empty);
                    break;
                case RazorSegmentKind.Expression:
                    if (segment.ExpressionPlan != null)
                    {
                        if (ShouldShortCircuitMissingRoot(segment.ExpressionPlan) &&
                            HasMissingRootValue(segment.ExpressionPlan, target, codeBehind))
                        {
                            break;
                        }

                        if (!RazorExpressionRuntimeCompiler.TryEvaluate(
                                segment.ExpressionPlan,
                                path => RazorValueResolver.Resolve(target, codeBehind, path),
                                out var value))
                        {
                            break;
                        }

                        sb.Append(value?.ToString() ?? string.Empty);
                    }
                    break;

                case RazorSegmentKind.Code:
                    break;
            }
        }

        return sb.ToString();
    }

    private static object? ConvertOnceValue(object? value, Type propertyType)
    {
        var coerced = BindingValueCoercion.Coerce(value, propertyType, CultureInfo.InvariantCulture);
        if (coerced is string stringValue)
        {
            var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetType != typeof(string) && !targetType.IsInstanceOfType(coerced))
            {
                return TypeConverterRegistry.ConvertValue(stringValue, targetType) ?? coerced;
            }
        }

        return coerced;
    }

    private static bool HasMissingRootValue(RazorExpressionPlan plan, object target, object? codeBehind)
    {
        foreach (var root in plan.RootIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var value = RazorValueResolver.Resolve(target, codeBehind, root);
            if (RazorEvaluationGuards.IsMissingRootValue(value))
                return true;
        }

        return false;
    }

    private static bool ShouldShortCircuitMissingRoot(RazorExpressionPlan plan)
    {
        return RazorEvaluationGuards.ShouldShortCircuitMissingRoot(plan);
    }

    private static BindingBase CreateBinding(RazorTemplate template, object targetObject, object? codeBehind)
    {
        if (template.HasCodeBlocks)
        {
            RazorTemplateRuntimeCompiler.EnsureCompiled(template);
        }
        else
        {
            foreach (var segment in template.Segments)
            {
                if (segment.Kind == RazorSegmentKind.Expression && segment.ExpressionPlan != null)
                {
                    RazorExpressionRuntimeCompiler.EnsureCompiled(segment.ExpressionPlan);
                }
            }
        }

        if (template.IsSinglePath && template.SinglePath != null)
        {
            var singlePathBinding = new MultiBinding
            {
                Converter = new RazorTemplateConverter(template, targetObject, codeBehind)
            };

            singlePathBinding.Bindings.Add(CreatePreferredPathBinding(template.SinglePath, codeBehind));
            TryAddDataContextTriggerBinding(singlePathBinding, targetObject);
            return singlePathBinding;
        }

        var multiBinding = new MultiBinding
        {
            Converter = new RazorTemplateConverter(template, targetObject, codeBehind)
        };

        foreach (var dependency in template.Dependencies)
        {
            multiBinding.Bindings.Add(CreatePreferredPathBinding(dependency, codeBehind));
        }

        TryAddDataContextTriggerBinding(multiBinding, targetObject);
        return multiBinding;
    }

    private static void TryAddDataContextTriggerBinding(MultiBinding multiBinding, object targetObject)
    {
        if (targetObject is not FrameworkElement)
            return;

        multiBinding.Bindings.Add(new Binding(nameof(FrameworkElement.DataContext))
        {
            RelativeSource = RelativeSource.Self,
            FallbackValue = DependencyProperty.UnsetValue
        });
    }

    private static BindingBase CreatePreferredPathBinding(string path, object? codeBehind)
    {
        var dataContextBinding = new Binding(path)
        {
            FallbackValue = DependencyProperty.UnsetValue
        };

        if (codeBehind == null)
            return dataContextBinding;

        var codeBehindBinding = new Binding(path)
        {
            Source = codeBehind,
            FallbackValue = DependencyProperty.UnsetValue
        };

        var priorityBinding = new PriorityBinding();
        priorityBinding.Bindings.Add(dataContextBinding);
        priorityBinding.Bindings.Add(codeBehindBinding);
        return priorityBinding;
    }

    private static bool TryFindDependencyProperty(Type type, string propertyName, out DependencyProperty dependencyProperty)
    {
        var current = type;
        var fieldName = propertyName + "Property";
        while (current != null)
        {
            var field = current.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty dp)
            {
                dependencyProperty = dp;
                return true;
            }

            current = current.BaseType;
        }

        dependencyProperty = null!;
        return false;
    }

    private static XamlParseException CreateMixedValueException(string propertyName, string rawValue, XmlReader? reader)
    {
        var suffix = string.Empty;
        if (reader is IXmlLineInfo info && info.HasLineInfo())
        {
            suffix = $" Line={info.LineNumber}, Position={info.LinePosition}.";
        }

        return new XamlParseException(
            $"Razor mixed template is not allowed on non-string property '{propertyName}'. Value='{rawValue}'.{suffix}");
    }
}

/// <summary>
/// Global registry used by build-time generated code to pre-register Razor expression metadata.
/// </summary>
public static class RazorExpressionRegistry
{
    private static readonly ConcurrentDictionary<string, ExpressionMetadata> MetadataByExpression = new(StringComparer.Ordinal);

    public static void RegisterMetadata(string expressionId, string expression, string[] dependencies)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;

        var safeDependencies = dependencies ?? Array.Empty<string>();
        MetadataByExpression[expression] = new ExpressionMetadata(expressionId, expression, safeDependencies);
    }

    internal static bool TryGetMetadata(string expression, out ExpressionMetadata metadata)
    {
        return MetadataByExpression.TryGetValue(expression, out metadata!);
    }

    internal sealed record ExpressionMetadata(string ExpressionId, string Expression, IReadOnlyList<string> Dependencies);
}
