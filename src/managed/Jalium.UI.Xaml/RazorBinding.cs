using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using Jalium.UI;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Jalium.UI.Markup;

internal enum RazorSegmentKind
{
    Literal,
    Path,
    Expression
}

internal sealed record RazorExpressionPlan(
    string Expression,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers);

internal sealed record RazorSegment(
    RazorSegmentKind Kind,
    string Text,
    RazorExpressionPlan? ExpressionPlan = null);

internal sealed class RazorTemplate
{
    public RazorTemplate(IReadOnlyList<RazorSegment> segments)
    {
        Segments = segments;

        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (segment.Kind == RazorSegmentKind.Path)
            {
                dependencies.Add(segment.Text);
            }
            else if (segment.Kind == RazorSegmentKind.Expression && segment.ExpressionPlan != null)
            {
                foreach (var dependency in segment.ExpressionPlan.Dependencies)
                {
                    dependencies.Add(dependency);
                }
            }
        }

        Dependencies = dependencies.ToArray();
    }

    public IReadOnlyList<RazorSegment> Segments { get; }

    public IReadOnlyList<string> Dependencies { get; }

    public bool HasDynamicSegments => Segments.Any(static s => s.Kind != RazorSegmentKind.Literal);

    public bool IsSinglePath =>
        Segments.Count == 1 && Segments[0].Kind == RazorSegmentKind.Path;

    public bool IsSingleExpression =>
        Segments.Count == 1 && Segments[0].Kind == RazorSegmentKind.Expression;

    public bool IsMixed =>
        Segments.Count > 1 && HasDynamicSegments;

    public string? SinglePath => IsSinglePath ? Segments[0].Text : null;

    public RazorExpressionPlan? SingleExpressionPlan =>
        IsSingleExpression ? Segments[0].ExpressionPlan : null;
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
        var pos = i + 2;
        var depth = 1;
        var inString = false;
        char stringQuote = '\0';
        var escaped = false;
        var start = pos;

        while (pos < input.Length)
        {
            var c = input[pos];

            if (escaped)
            {
                escaped = false;
                pos++;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == stringQuote)
                {
                    inString = false;
                    stringQuote = '\0';
                }

                pos++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                stringQuote = c;
                pos++;
                continue;
            }

            if (c == '(')
            {
                depth++;
                pos++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    var expression = input[start..pos].Trim();
                    i = pos + 1;
                    return expression;
                }

                pos++;
                continue;
            }

            pos++;
        }

        throw new XamlParseException("Unclosed Razor expression. Expected closing ')'.");
    }
}

internal static class RazorExpressionAnalyzer
{
    public static RazorExpressionPlan GetPlan(string expression)
    {
        if (RazorExpressionRegistry.TryGetMetadata(expression, out var metadata))
        {
            var rootsFromMetadata = metadata.Dependencies
                .Select(GetRootIdentifier)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new RazorExpressionPlan(
                expression,
                metadata.Dependencies,
                rootsFromMetadata);
        }

        var dependencies = ExtractDependencies(expression);
        var roots = dependencies
            .Select(GetRootIdentifier)
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new RazorExpressionPlan(
            expression,
            dependencies,
            roots);
    }

    private static string[] ExtractDependencies(string expression)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var span = expression.AsSpan();
        var i = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';

        while (i < span.Length)
        {
            var c = span[i];
            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == quote)
                {
                    inString = false;
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                quote = c;
                i++;
                continue;
            }

            if (!IsIdentifierStart(c))
            {
                i++;
                continue;
            }

            if (i > 0 && (span[i - 1] == '.' || span[i - 1] == ':'))
            {
                i++;
                while (i < span.Length && IsIdentifierPart(span[i]))
                {
                    i++;
                }

                continue;
            }

            var start = i;
            i++;
            while (i < span.Length && IsIdentifierPart(span[i]))
            {
                i++;
            }

            var token = span[start..i].ToString();
            if (IsReservedIdentifier(token))
                continue;

            var end = i;
            while (end < span.Length && span[end] == '.')
            {
                var nextStart = end + 1;
                if (nextStart >= span.Length || !IsIdentifierStart(span[nextStart]))
                    break;

                end = nextStart + 1;
                while (end < span.Length && IsIdentifierPart(span[end]))
                {
                    end++;
                }
            }

            var path = span[start..end].ToString();
            var after = end;
            while (after < span.Length && char.IsWhiteSpace(span[after]))
            {
                after++;
            }

            var isInvocation = after < span.Length && span[after] == '(';
            AddDependency(dependencies, path, isInvocation);
            i = end;
        }

        return dependencies.ToArray();
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

    private static bool IsIdentifierStart(char c) =>
        c == '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) =>
        c == '_' || char.IsLetterOrDigit(c);

    private static bool IsReservedIdentifier(string identifier) =>
        identifier is "true" or "false" or "null" or "new" or "global" or "this" or "base";

    private static string GetRootIdentifier(string path)
    {
        var dotIndex = path.IndexOf('.');
        return dotIndex < 0 ? path : path[..dotIndex];
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
        if (value == null)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType == typeof(object) || underlyingType.IsInstanceOfType(value))
            return value;

        if (value is string stringValue && underlyingType != typeof(string))
        {
            return TypeConverterRegistry.ConvertValue(stringValue, underlyingType);
        }

        try
        {
            return System.Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
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
            }
        }

        return sb.ToString();
    }

    private static object? ConvertOnceValue(object? value, Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (value == null)
            return null;

        if (targetType == typeof(object) || targetType.IsInstanceOfType(value))
            return value;

        if (value is string str && targetType != typeof(string))
        {
            return TypeConverterRegistry.ConvertValue(str, targetType);
        }

        try
        {
            return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
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
        foreach (var segment in template.Segments)
        {
            if (segment.Kind == RazorSegmentKind.Expression && segment.ExpressionPlan != null)
            {
                RazorExpressionRuntimeCompiler.EnsureCompiled(segment.ExpressionPlan);
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
