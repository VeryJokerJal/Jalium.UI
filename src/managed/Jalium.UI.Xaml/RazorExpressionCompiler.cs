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
        if (!RazorScriptingFeature.IsSupported)
            return;

        var key = $"{plan.Expression}::{string.Join("|", plan.RootIdentifiers)}";
        _ = Cache.GetOrAdd(key, _ => Compile(plan));
    }

    public static object? Evaluate(RazorExpressionPlan plan, Func<string, object?> resolver)
    {
        // Prefer build-time pre-compiled evaluator (always available, AOT-safe).
        if (RazorExpressionRegistry.TryGetEvaluator(plan.Expression, out var preCompiled))
            return preCompiled(resolver);

        if (!RazorScriptingFeature.IsSupported)
            throw new PlatformNotSupportedException("Razor runtime scripting is not supported under NativeAOT. Pre-compile Razor expressions using the Jalium build task.");

        var key = $"{plan.Expression}::{string.Join("|", plan.RootIdentifiers)}";
        var compiled = Cache.GetOrAdd(key, _ => Compile(plan));
        var globals = new RazorScriptGlobals { Resolve = resolver };
        return compiled.Runner(globals).GetAwaiter().GetResult();
    }

    public static bool TryEvaluate(RazorExpressionPlan plan, Func<string, object?> resolver, out object? value)
    {
        if (!RazorScriptingFeature.IsSupported)
        {
            value = null;
            return false;
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
        if (!RazorScriptingFeature.IsSupported)
            return;

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

        // Prefer build-time pre-compiled template evaluator (AOT-safe).
        var key = BuildCacheKey(template);
        if (RazorExpressionRegistry.TryGetTemplateEvaluator(key, out var preCompiled))
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
