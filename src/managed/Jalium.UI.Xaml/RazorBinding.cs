using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using Jalium.UI;

namespace Jalium.UI.Markup;

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
    private static readonly ConcurrentDictionary<string, Func<Func<string, object?>, object?>> CompiledEvaluators = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Func<Func<string, object?>, object?[]>> CompiledTemplateEvaluators = new(StringComparer.Ordinal);

    public static void RegisterMetadata(string expressionId, string expression, string[] dependencies)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;

        var safeDependencies = dependencies ?? Array.Empty<string>();
        MetadataByExpression[expression] = new ExpressionMetadata(expressionId, expression, safeDependencies);
    }

    /// <summary>
    /// Registers a pre-compiled expression evaluator generated at build time.
    /// When available, the runtime skips Roslyn scripting entirely.
    /// </summary>
    public static void RegisterEvaluator(string expression, Func<Func<string, object?>, object?> evaluator)
    {
        if (!string.IsNullOrWhiteSpace(expression) && evaluator != null)
            CompiledEvaluators[expression] = evaluator;
    }

    /// <summary>
    /// Registers a pre-compiled template evaluator for templates with code blocks.
    /// </summary>
    public static void RegisterTemplateEvaluator(string templateKey, Func<Func<string, object?>, object?[]> evaluator)
    {
        if (!string.IsNullOrWhiteSpace(templateKey) && evaluator != null)
            CompiledTemplateEvaluators[templateKey] = evaluator;
    }

    internal static bool TryGetMetadata(string expression, out ExpressionMetadata metadata)
    {
        return MetadataByExpression.TryGetValue(expression, out metadata!);
    }

    internal static bool TryGetEvaluator(string expression, out Func<Func<string, object?>, object?> evaluator)
    {
        return CompiledEvaluators.TryGetValue(expression, out evaluator!);
    }

    internal static bool TryGetTemplateEvaluator(string templateKey, out Func<Func<string, object?>, object?[]> evaluator)
    {
        return CompiledTemplateEvaluators.TryGetValue(templateKey, out evaluator!);
    }

    internal sealed record ExpressionMetadata(string ExpressionId, string Expression, IReadOnlyList<string> Dependencies);
}
