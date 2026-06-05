namespace Jalium.UI.Markup;

/// <summary>
/// AOT-safe expression compiler that provides lightweight alternatives to
/// <see cref="RazorRoslynScriptCompiler"/> for expression and template evaluation.
/// Uses <see cref="RazorLightweightExpressionEvaluator"/> (recursive-descent parser
/// with reflection-based evaluation) instead of Roslyn CSharpScript.
/// </summary>
internal static class RazorLightweightExpressionCompiler
{
    /// <summary>
    /// Compiles an expression plan into an evaluator wrapper without Roslyn.
    /// Drop-in replacement for <c>RazorRoslynScriptCompiler.CompileExpression()</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiled expression wrapper invokes the lightweight evaluator, which reflects on user types.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Runtime Razor expression evaluation is an opt-in reflective feature whose preservation is the documented consumer responsibility. RazorLightweightExpressionEvaluator.Evaluate carries RequiresDynamicCode (\"Razor expression evaluator dispatches to RazorExpressionParser, which may construct generic types/methods at runtime.\"); this site is reached only through that runtime-compilation surface, so the dynamic-code contract is already declared upstream rather than introduced here.")]
    public static RazorExpressionRuntimeCompiler.CompiledExpressionWrapper CompileExpression(RazorExpressionPlan plan)
    {
        var expression = plan.Expression;
        return new RazorExpressionRuntimeCompiler.CompiledExpressionWrapper
        {
            Runner = globals =>
            {
                object? Resolver(string name)
                {
                    // Resolve via the script globals (same mechanism as Roslyn path)
                    return globals.Resolve(name);
                }
                var result = RazorLightweightExpressionEvaluator.Evaluate(expression, Resolver);
                return System.Threading.Tasks.Task.FromResult(result);
            }
        };
    }

    /// <summary>
    /// Compiles a template (with code blocks) into an evaluator without Roslyn.
    /// Drop-in replacement for <c>RazorRoslynScriptCompiler.CompileTemplate()</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compiled template wrapper invokes the lightweight expression/code-block evaluators, which reflect on user types.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Runtime Razor template evaluation is an opt-in reflective feature whose preservation is the documented consumer responsibility. The evaluators invoked here both declare RequiresDynamicCode upstream — RazorLightweightExpressionEvaluator.Evaluate (\"Razor expression evaluator dispatches to RazorExpressionParser, which may construct generic types/methods at runtime.\") and RazorLightweightCodeBlockInterpreter.ExpandWithScope (\"Razor code-block interpreter may construct generic types/methods at runtime via the expression evaluator.\"). This site is reached only through that runtime-compilation surface, so the dynamic-code contract is already declared upstream rather than introduced here.")]
    public static RazorTemplateRuntimeCompiler.CompiledTemplateWrapper CompileTemplate(RazorTemplate template)
    {
        return new RazorTemplateRuntimeCompiler.CompiledTemplateWrapper
        {
            Runner = globals =>
            {
                // Start with the global resolver; code blocks may enrich it with local variables
                Func<string, object?> currentResolver = name => globals.Resolve(name);

                var parts = new System.Collections.Generic.List<object?>();
                foreach (var segment in template.Segments)
                {
                    switch (segment.Kind)
                    {
                        case RazorSegmentKind.Literal:
                            parts.Add(segment.Text);
                            break;
                        case RazorSegmentKind.Path:
                            parts.Add(currentResolver(segment.Text));
                            break;
                        case RazorSegmentKind.Expression:
                            parts.Add(RazorLightweightExpressionEvaluator.Evaluate(segment.Text, currentResolver));
                            break;
                        case RazorSegmentKind.Code:
                            // Execute code block with access to external variables,
                            // and capture its scope so subsequent segments can use
                            // variables defined in the code block.
                            var (codeOutput, codeResolver) = RazorLightweightCodeBlockInterpreter.ExpandWithScope(
                                segment.Text, currentResolver);
                            if (!string.IsNullOrEmpty(codeOutput))
                                parts.Add(codeOutput);
                            currentResolver = codeResolver;
                            break;
                    }
                }

                return System.Threading.Tasks.Task.FromResult(parts.ToArray());
            }
        };
    }
}
