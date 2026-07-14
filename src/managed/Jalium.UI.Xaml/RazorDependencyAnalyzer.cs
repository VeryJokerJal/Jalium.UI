using Jalium.UI;
using Jalium.UI.Data;

namespace Jalium.UI.Markup;

internal static class RazorCodeBlockAnalyzer
{
    public static RazorCodeBlockPlan GetPlan(string code)
    {
        if (RazorScriptingFeature.IsSupported)
        {
            try
            {
                var analysis = RazorCSharpDependencyAnalyzer.AnalyzeCodeBlock(code);
                return new RazorCodeBlockPlan(
                    code,
                    analysis.Dependencies,
                    analysis.RootIdentifiers,
                    analysis.DeclaredIdentifiers);
            }
            catch (NotSupportedException) { /* Roslyn unavailable — fall through to lightweight */ }
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
            try
            {
                var analysis = RazorCSharpDependencyAnalyzer.AnalyzeExpression(expression);
                return new RazorExpressionPlan(
                    expression,
                    analysis.Dependencies,
                    analysis.RootIdentifiers);
            }
            catch (NotSupportedException) { /* Roslyn unavailable — fall through to lightweight */ }
        }

        // Lightweight fallback (AOT/single-file safe).
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
