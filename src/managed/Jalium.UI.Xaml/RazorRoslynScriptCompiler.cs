using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Jalium.UI.Markup;

/// <summary>
/// Roslyn-based C# syntax dependency analyzer. This partial class contains all Roslyn-dependent
/// code; the other partial in RazorDependencyAnalyzer.cs has only the <c>GetRootIdentifier</c> helper.
/// ILC trims this entire class when <c>Jalium.UI.Markup.RazorScripting</c> is false.
/// </summary>
internal static partial class RazorCSharpDependencyAnalyzer
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
                declared.Add(declarator.Identifier.ValueText);
        }

        foreach (var parameter in root.DescendantNodesAndSelf().OfType<ParameterSyntax>())
        {
            if (!parameter.Identifier.IsMissing && !string.IsNullOrWhiteSpace(parameter.Identifier.ValueText))
                declared.Add(parameter.Identifier.ValueText);
        }

        foreach (var foreachStatement in root.DescendantNodesAndSelf().OfType<ForEachStatementSyntax>())
        {
            if (!foreachStatement.Identifier.IsMissing && !string.IsNullOrWhiteSpace(foreachStatement.Identifier.ValueText))
                declared.Add(foreachStatement.Identifier.ValueText);
        }

        foreach (var catchDeclaration in root.DescendantNodesAndSelf().OfType<CatchDeclarationSyntax>())
        {
            if (!catchDeclaration.Identifier.IsMissing && !string.IsNullOrWhiteSpace(catchDeclaration.Identifier.ValueText))
                declared.Add(catchDeclaration.Identifier.ValueText);
        }

        foreach (var localFunction in root.DescendantNodesAndSelf().OfType<LocalFunctionStatementSyntax>())
        {
            if (!localFunction.Identifier.IsMissing && !string.IsNullOrWhiteSpace(localFunction.Identifier.ValueText))
                declared.Add(localFunction.Identifier.ValueText);
        }

        foreach (var designation in root.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
        {
            if (!designation.Identifier.IsMissing && !string.IsNullOrWhiteSpace(designation.Identifier.ValueText))
                declared.Add(designation.Identifier.ValueText);
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
}
