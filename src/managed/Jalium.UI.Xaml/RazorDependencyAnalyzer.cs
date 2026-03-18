using Jalium.UI;

namespace Jalium.UI.Markup;

internal static class RazorCodeBlockAnalyzer
{
    public static RazorCodeBlockPlan GetPlan(string code)
    {
        if (RazorScriptingFeature.IsSupported)
        {
            var analysis = RazorCSharpDependencyAnalyzer.AnalyzeCodeBlock(code);
            return new RazorCodeBlockPlan(
                code,
                analysis.Dependencies,
                analysis.RootIdentifiers,
                analysis.DeclaredIdentifiers);
        }

        var fallback = RazorLightweightDependencyAnalyzer.AnalyzeCodeBlock(code);
        return new RazorCodeBlockPlan(
            code,
            fallback.Dependencies,
            fallback.RootIdentifiers,
            fallback.DeclaredIdentifiers);
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

        if (RazorScriptingFeature.IsSupported)
        {
            var analysis = RazorCSharpDependencyAnalyzer.AnalyzeExpression(expression);
            return new RazorExpressionPlan(
                expression,
                analysis.Dependencies,
                analysis.RootIdentifiers);
        }

        // Lightweight fallback when Roslyn is trimmed (NativeAOT).
        var fallback = RazorLightweightDependencyAnalyzer.AnalyzeExpression(expression);
        return new RazorExpressionPlan(
            expression,
            fallback.Dependencies,
            fallback.RootIdentifiers);
    }
}

internal sealed record RazorCSharpDependencyAnalysis(
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers,
    IReadOnlyList<string> DeclaredIdentifiers);

/// <summary>
/// Roslyn-based dependency analyzer. Only referenced when <see cref="RazorScriptingFeature.IsSupported"/>
/// is true — ILC will trim this entire class (and transitively all Roslyn types) when the feature
/// switch is set to false.
/// The actual implementation lives in <c>RazorRoslynScriptCompiler.cs</c> to isolate Roslyn usings.
/// This partial declaration exposes the <see cref="GetRootIdentifier"/> helper which does NOT
/// depend on Roslyn and is used by non-Roslyn code paths.
/// </summary>
internal static partial class RazorCSharpDependencyAnalyzer
{
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
