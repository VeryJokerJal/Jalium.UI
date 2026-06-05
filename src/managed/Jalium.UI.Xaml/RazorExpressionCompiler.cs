using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Razor runtime expression evaluation is an opt-in feature. The called RazorLightweightExpressionEvaluator.Evaluate carries RequiresUnreferencedCode ('Razor expression evaluator dispatches to RazorExpressionParser, which may reflect on user types') — its preservation is the documented consumer responsibility: applications must register typed property/method accessors via RazorExpressionRegistry for any binding source they use under trimming, otherwise the evaluator falls back to reflection on the runtime source type.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Razor runtime expression evaluation is an opt-in feature. The called RazorLightweightExpressionEvaluator.Evaluate carries RequiresDynamicCode ('Razor expression evaluator dispatches to RazorExpressionParser, which may construct generic types/methods at runtime'). Consumers targeting AOT must register accessors via RazorExpressionRegistry; the reflective fallback is exercised only for unregistered runtime expressions, a documented prerequisite of the Razor binding surface, not a defect of this site.")]
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Razor runtime expression evaluation is an opt-in feature. The called RazorLightweightExpressionEvaluator.Evaluate carries RequiresUnreferencedCode ('Razor expression evaluator dispatches to RazorExpressionParser, which may reflect on user types') — its preservation is the documented consumer responsibility: applications must register typed property/method accessors via RazorExpressionRegistry for any binding source they use under trimming, otherwise the evaluator falls back to reflection on the runtime source type.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Razor runtime expression evaluation is an opt-in feature. The called RazorLightweightExpressionEvaluator.Evaluate carries RequiresDynamicCode ('Razor expression evaluator dispatches to RazorExpressionParser, which may construct generic types/methods at runtime'). Consumers targeting AOT must register accessors via RazorExpressionRegistry; the reflective fallback is exercised only for unregistered runtime expressions, a documented prerequisite of the Razor binding surface, not a defect of this site.")]
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
        catch (XamlParseException)
        {
            // Razor expression contained malformed C# (typical while the user is
            // mid-edit). TryEvaluate returns false rather than tearing down the
            // whole XAML load — see the matching catch in RazorTemplateRuntimeCompiler.
            value = null;
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Razor runtime expression evaluation is an opt-in feature. The called RazorLightweightExpressionCompiler.CompileExpression carries RequiresUnreferencedCode ('Compiled expression wrapper invokes the lightweight evaluator, which reflects on user types') — its preservation is the documented consumer responsibility: applications must register typed accessors via RazorExpressionRegistry for binding sources used under trimming, otherwise the wrapped evaluator falls back to reflection on the runtime source type.")]
    private static CompiledExpression Compile(RazorExpressionPlan plan)
    {
        var wrapper = RazorLightweightExpressionCompiler.CompileExpression(plan);
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
        catch (XamlParseException)
        {
            // Razor template body contained malformed C# (typical while the user is
            // mid-edit, e.g. a lone keyword like "is" inside @{ ... }). TryEvaluate's
            // contract is to return false on failure rather than tear down the whole
            // XAML load — propagate the bool, let the caller leave the property unset.
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Razor runtime template evaluation is an opt-in feature. The called RazorLightweightExpressionCompiler.CompileTemplate carries RequiresUnreferencedCode ('Compiled template wrapper invokes the lightweight expression/code-block evaluators, which reflect on user types') — its preservation is the documented consumer responsibility: applications must register typed accessors via RazorExpressionRegistry for binding sources used under trimming, otherwise the wrapped evaluators fall back to reflection on the runtime source type.")]
    private static CompiledTemplate Compile(RazorTemplate template)
    {
        var wrapper = RazorLightweightExpressionCompiler.CompileTemplate(template);
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
