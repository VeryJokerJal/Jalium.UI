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
