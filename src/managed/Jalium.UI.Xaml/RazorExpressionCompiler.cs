using System.Collections.Concurrent;
using System.Text;

namespace Jalium.UI.Markup;

internal static class RazorExpressionRuntimeCompiler
{
    internal sealed class CompiledExpressionWrapper
    {
        public required Func<RazorScriptGlobals, Task<object?>> Runner { get; init; }
    }

    private sealed class CompiledExpression
    {
        public required Func<RazorScriptGlobals, Task<object?>> Runner { get; init; }
    }

    private static readonly ConcurrentDictionary<string, CompiledExpression> Cache = new(StringComparer.Ordinal);

    public static void EnsureCompiled(RazorExpressionPlan plan)
    {
        // If a pre-compiled evaluator exists (from build-time), nothing to do.
        if (RazorExpressionRegistry.TryGetEvaluator(plan.Expression, out _))
            return;

        // The lightweight evaluator handles most expressions without Roslyn.
        // Defer Roslyn compilation to Evaluate() time — only invoked if the
        // lightweight path actually fails, avoiding the heavy upfront cost
        // of spinning up the Roslyn compiler pipeline for every expression.
    }

    public static object? Evaluate(RazorExpressionPlan plan, Func<string, object?> resolver)
    {
        // Build-time pre-compiled evaluators use 'dynamic' which requires Microsoft.CSharp.
        // Only use them when the runtime binder is available.
        if (IsDynamicSupported &&
            RazorExpressionRegistry.TryGetEvaluator(plan.Expression, out var preCompiled))
        {
            try { return preCompiled(resolver); }
            catch { /* fall through to lightweight */ }
        }

        // Lightweight AOT-safe evaluator (no Roslyn, no dynamic)
        return RazorLightweightExpressionEvaluator.Evaluate(plan.Expression, resolver);
    }

    internal static readonly bool IsDynamicSupported = CheckDynamicSupport();
    private static bool CheckDynamicSupport()
    {
        try { return Type.GetType("Microsoft.CSharp.RuntimeBinder.Binder, Microsoft.CSharp") != null; }
        catch { return false; }
    }

    public static bool TryEvaluate(RazorExpressionPlan plan, Func<string, object?> resolver, out object? value)
    {
        // Build-time pre-compiled evaluators use 'dynamic' — skip when not available
        if (IsDynamicSupported &&
            RazorExpressionRegistry.TryGetEvaluator(plan.Expression, out var preCompiledEval))
        {
            try { value = preCompiledEval(resolver); return true; }
            catch { /* fall through */ }
        }

        // Lightweight AOT-safe evaluator (no dynamic, no Roslyn)
        try
        {
            value = RazorLightweightExpressionEvaluator.Evaluate(plan.Expression, resolver);
            return true;
        }
        catch
        {
            if (!RazorScriptingFeature.IsSupported)
            {
                value = null;
                return false;
            }
        }

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
        var wrapper = RazorRoslynScriptCompiler.CompileExpression(plan);
        return new CompiledExpression { Runner = wrapper.Runner };
    }

    private static bool IsTransientNullBindingError(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
    {
        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class RazorTemplateRuntimeCompiler
{
    internal sealed class CompiledTemplateWrapper
    {
        public required Func<RazorScriptGlobals, Task<object?[]>> Runner { get; init; }
    }

    private sealed class CompiledTemplate
    {
        public required Func<RazorScriptGlobals, Task<object?[]>> Runner { get; init; }
    }

    private static readonly ConcurrentDictionary<string, CompiledTemplate> Cache = new(StringComparer.Ordinal);

    public static void EnsureCompiled(RazorTemplate template)
    {
        if (!template.HasCodeBlocks)
            return;

        var key = BuildCacheKey(template);
        if (RazorExpressionRegistry.TryGetTemplateEvaluator(key, out _))
            return;

        // Defer Roslyn compilation — the lightweight evaluator handles most
        // templates without Roslyn. Compile on-demand only when needed.
    }

    public static bool TryEvaluate(RazorTemplate template, Func<string, object?> resolver, out object? value)
    {
        if (RazorEvaluationGuards.ShouldShortCircuitMissingRoot(template) &&
            RazorEvaluationGuards.HasMissingRootValue(template, resolver))
        {
            value = null;
            return false;
        }

        // Build-time pre-compiled template evaluators use 'dynamic' — skip when not available
        var key = BuildCacheKey(template);
        if (RazorExpressionRuntimeCompiler.IsDynamicSupported &&
            RazorExpressionRegistry.TryGetTemplateEvaluator(key, out var preCompiled))
        {
            try
            {
                var parts = preCompiled(resolver);
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

        if (!RazorScriptingFeature.IsSupported)
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

    internal static string BuildCacheKey(RazorTemplate template)
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
        var wrapper = RazorRoslynScriptCompiler.CompileTemplate(template);
        return new CompiledTemplate { Runner = wrapper.Runner };
    }

    private static bool IsTransientNullBindingError(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
    {
        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
