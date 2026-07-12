using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Markup;

internal static class RazorValueResolver
{
    public static object? Resolve(
        object targetObject,
        object? codeBehind,
        string path)
    {
        // $.path — self-reference: resolve against the target element itself
        if (path.Length >= 2 && path[0] == '$' && path[1] == '.')
        {
            var selfPath = path[2..];
            if (TryResolvePath(targetObject, selfPath, out _, out var selfValue))
                return selfValue;
            return null;
        }

        if (path.Length == 1 && path[0] == '$')
            return targetObject;

        // #.path — data model reference: resolve against DataContext only, no codeBehind fallback
        if (path.Length >= 2 && path[0] == '#' && path[1] == '.')
        {
            var dataPath = path[2..];
            if (TryResolveDataContext(targetObject, dataPath, out var foundInData, out var dataValue) && foundInData)
                return dataValue;
            return null;
        }

        if (path.Length == 1 && path[0] == '#')
        {
            if (targetObject is FrameworkElement fe)
                return fe.DataContext;
            return null;
        }

        // 1. Try DataContext on the target element or its visual ancestors
        if (TryResolveDataContext(targetObject, path, out var foundInDataContext, out var valueFromDataContext) && foundInDataContext)
        {
            if (!RazorEvaluationGuards.IsUnavailableBindingValue(valueFromDataContext))
                return valueFromDataContext;
        }

        // 2. If codeBehind has a DataContext, try resolving on that first
        if (codeBehind is FrameworkElement cbElement)
        {
            var cbDc = cbElement.GetValue(FrameworkElement.DataContextProperty);
            if (cbDc != null && TryResolvePath(cbDc, path, out var foundInCbDc, out var valueFromCbDc) && foundInCbDc)
                return valueFromCbDc;
        }

        // 3. Fall back to codeBehind itself (Page properties)
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Calls RazorValueResolver.TryReadMember, whose RUC contract is the documented Razor opt-in: \"Razor binding falls back to reflection when no PropertyAccessor is registered for the source type. Register accessors via RazorExpressionRegistry.RegisterPropertyAccessor for AOT-safe usage.\" The same contract is declared upstream at the public Razor/XAML surface (XamlReader.Load is annotated RequiresUnreferencedCode, and the SG-emitted bindings reach this resolver only through the XamlBuilder.*Impl delegates whose ModuleInitializer registration carries IL2026). Reflection here is the path/member resolver for the runtime data source; preserving those members is the consumer's responsibility per that message.")]
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

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Razor binding falls back to reflection when no PropertyAccessor is registered for the source type. Register accessors via RazorExpressionRegistry.RegisterPropertyAccessor for AOT-safe usage.")]
    private static bool TryReadMember(object source, string memberName, out object? value)
    {
        // AOT-safe: try pre-registered accessor first (no reflection)
        var sourceType = source.GetType();
        if (RazorExpressionRegistry.TryGetPropertyAccessor(sourceType, memberName, out var accessor))
        {
            value = accessor(source);
            return true;
        }

        // DependencyProperty path uses the AOT-safe registry (no field reflection).
        if (source is DependencyObject dependencyObject)
        {
            var dependencyProperty = DependencyProperty.FromName(sourceType, memberName);
            if (dependencyProperty != null)
            {
                value = dependencyObject.GetValue(dependencyProperty);
                return true;
            }
        }

        // CLR property/field fallback — requires reflection on the runtime source type.
        // Mark via [RequiresUnreferencedCode] above so the analyzer flags any caller that
        // hasn't supplied trim-safe registrations for its data sources.
        var property = sourceType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            value = property.GetValue(source);
            return true;
        }

        var field = sourceType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
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

    /// <summary>
    /// SourceGenerator-lowered Razor <c>@if</c>. The parser lifted a simple
    /// <c>@if(cond) { ... }</c> block into a synthetic wrapper, flattened it away, and the
    /// SG emitted one call here per conditional child immediately after adding it to its
    /// parent. This is the exact <see cref="TryApplyIfVisibility"/> binding the streaming
    /// parser's <c>ShouldIncludeConditionalChild</c> applies — same
    /// <c>RazorConditionalVisibilityConverter</c>, same DataContext trigger — but reached
    /// from straight-line generated C# instead of a runtime document re-parse
    /// (<c>XamlReader.LoadComponentFromString</c> is no longer involved for these blocks).
    /// <para>
    /// <paramref name="dependencies"/> is the SG-computed identifier set for
    /// <paramref name="condition"/>; pre-registering it lets
    /// <see cref="RazorExpressionAnalyzer.GetPlan"/> skip its reflection/Roslyn walk (the
    /// documented SG fast path). The set is a superset of the precise dependencies so the
    /// binding never misses an update.
    /// </para>
    /// </summary>
    internal static void ApplySgLoweredIfVisibility(
        object child,
        string condition,
        string[] dependencies,
        XamlParserContext context)
    {
        if (dependencies is { Length: > 0 })
            RazorExpressionRegistry.RegisterMetadata(condition, condition, dependencies);

        // For a lifted simple @if the child is always a UIElement and the condition is
        // never blank (the parser refuses to lift text-only / bodyless @if), so this
        // applies the visibility binding exactly as the streaming parser would. The
        // non-UIElement branch ShouldIncludeConditionalChild has (EvaluateConditionOnce)
        // is unreachable here by construction, so it is intentionally not duplicated.
        TryApplyIfVisibility(child, condition, context);
    }

    /// <summary>
    /// SourceGenerator-lowered Razor value-expression on a named property. The SG
    /// pre-computed <paramref name="dependencies"/> from the expression text at codegen
    /// time so we skip the reflection walk in <see cref="RazorExpressionAnalyzer"/>.
    /// Mirrors the runtime <see cref="TryApplyRazorValue"/> path otherwise.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "GetProperty is invoked on the runtime type of the SG-lowered Razor binding target. The source is object.GetType() and cannot carry DynamicallyAccessedMembers. The target type is reachable from XAML and is preserved via XamlTypeRegistry / DAM annotations on the XAML surface; the public Razor/XAML entry point (XamlReader.Load) already declares the RequiresUnreferencedCode contract that covers this reflective property lookup, and these SG-emitted bindings reach here only through the XamlBuilder.*Impl delegates whose ModuleInitializer registration carries the IL2026 suppression.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Falls back to XamlReader.BuilderSetProperty, whose RUC message is \"XamlBuilder.SetProperty forwards to the runtime XamlReader.SetProperty path which uses reflection / type converters / markup extensions.\" That contract is already declared at the public XAML surface (XamlReader.Load is RequiresUnreferencedCode) and re-asserted by the XamlBuilder.SetPropertyImpl ModuleInitializer registration (IL2026 suppressed there). This forwarder is invoked only via the SG-emitted XamlBuilder.SetRazorBindingImpl delegate, never as an independent public API.")]
    internal static void ApplySgLoweredExpression(
        object target,
        string propertyName,
        string expression,
        string[] dependencies,
        XamlParserContext context)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
        {
            // Fall back to runtime SetProperty pipeline — it knows about attached property
            // and the message will surface the missing-member error consistently.
            XamlReader.BuilderSetProperty(target, propertyName, expression, context);
            return;
        }

        var template = RazorTemplateParser.Parse(expression);
        if (!template.HasDynamicSegments)
        {
            var collapsed = EvaluateTemplateOncePublic(template, target, context.CodeBehindInstance);
            if (TryFindDependencyProperty(target.GetType(), propertyName, out var literalDp) && target is DependencyObject literalTarget)
            {
                literalTarget.SetValue(literalDp, ConvertOnceValuePublic(collapsed, property.PropertyType));
                return;
            }
            TryAssignClrPropertyPublic(target, property, collapsed);
            return;
        }

        if (!TryFindDependencyProperty(target.GetType(), propertyName, out var dependencyProperty) || target is not DependencyObject dependencyObject)
        {
            // CLR-only property — best-effort one-time eval.
            var resolved = EvaluateTemplateOncePublic(template, target, context.CodeBehindInstance);
            TryAssignClrPropertyPublic(target, property, resolved);
            return;
        }

        var binding = CreateBindingPublic(template, target, context.CodeBehindInstance);
        dependencyObject.SetBinding(dependencyProperty, binding);
    }

    /// <summary>
    /// SourceGenerator-lowered Razor value-expression on the element's content property
    /// (resolved via <see cref="System.Windows.Markup.ContentPropertyAttribute"/>). Used by
    /// SG-emitted <see cref="XamlBuilder.SetContentRazorBinding"/> calls for text-nodes like
    /// <c>&lt;TextBlock&gt;Count: @Items.Count&lt;/TextBlock&gt;</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Calls ResolveContentPropertyName, whose RUC message is \"Reads ContentPropertyAttribute via reflection on the runtime type.\" Mirrors XamlReader's annotated content-property resolution (\"Reads ContentPropertyAttribute and the matching property via reflection on the runtime type.\"); the RUC contract is declared upstream at the public XAML/Razor surface (XamlReader.Load) and this forwarder is reached only through the SG-emitted XamlBuilder.SetContentRazorBindingImpl delegate whose ModuleInitializer registration carries IL2026.")]
    internal static void ApplySgLoweredContentExpression(
        object target,
        string expression,
        string[] dependencies,
        XamlParserContext context)
    {
        var contentPropertyName = ResolveContentPropertyName(target.GetType()) ?? "Content";
        ApplySgLoweredExpression(target, contentPropertyName, expression, dependencies, context);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reads ContentPropertyAttribute via reflection on the runtime type.")]
    private static string? ResolveContentPropertyName(Type type)
    {
        if (typeof(TextBlock).IsAssignableFrom(type))
        {
            return nameof(TextBlock.Text);
        }

        return type.GetCustomAttribute<ContentPropertyAttribute>(inherit: true)?.Name
            ?? type.GetCustomAttribute<Jalium.UI.ContentPropertyAttribute>(inherit: true)?.Name;
    }

    // Internal accessors so the SG-facing methods above can reuse the private helpers
    // without bumping their visibility. Keep these one-line forwarders inline so future
    // refactors of the private helpers don't drift out of sync.
    private static object? EvaluateTemplateOncePublic(RazorTemplate template, object target, object? codeBehind)
        => EvaluateTemplateOnce(template, target, codeBehind);
    private static object? ConvertOnceValuePublic(object? value, Type targetType)
        => ConvertOnceValue(value, targetType);
    private static bool TryAssignClrPropertyPublic(object instance, PropertyInfo property, object? value)
        => TryAssignClrProperty(instance, property, value);
    private static BindingBase CreateBindingPublic(RazorTemplate template, object targetObject, object? codeBehind)
        => CreateBinding(template, targetObject, codeBehind);

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
                return TryAssignClrProperty(instance, property, collapsedLiteral);
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
            return TryAssignClrProperty(instance, property, resolved);
        }

        var binding = CreateBinding(template, instance, context.CodeBehindInstance);
        dependencyObject.SetBinding(dependencyProperty, binding);
        return true;
    }

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="property"/> on <paramref name="instance"/>.
    /// When the property is read-only (typical for collection content properties such as
    /// <c>Panel.Children</c> or <c>ItemsControl.Items</c>), the value is appended to the
    /// underlying <see cref="System.Collections.IList"/> if the element type matches.
    /// Returns <c>true</c> when the value was applied; <c>false</c> when the caller should
    /// fall back to other handling (e.g. emitting raw text). The method never throws
    /// <see cref="ArgumentException"/> for missing setters — that would turn an in-progress
    /// editor edit (e.g. <c>@{ i }</c> inside a Grid) into a hard parse failure.
    /// </summary>
    private static bool TryAssignClrProperty(object instance, PropertyInfo property, object? value)
    {
        if (property.CanWrite)
        {
            property.SetValue(instance, ConvertOnceValue(value, property.PropertyType));
            return true;
        }

        // Read-only property: only collection content properties make sense here.
        object? existing;
        try
        {
            existing = property.GetValue(instance);
        }
        catch
        {
            return false;
        }

        if (existing is not System.Collections.IList list)
            return false;

        var converted = ConvertOnceValue(value, property.PropertyType);
        if (converted == null)
            return false;

        try
        {
            list.Add(converted);
            return true;
        }
        catch
        {
            // Element type mismatch (e.g. adding a string to IList<UIElement>) — let the
            // caller decide whether to emit fallback text. Don't crash the parser.
            return false;
        }
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
        // $.path — self-reference: bind to the element's own property via RelativeSource.Self
        if (path.Length >= 2 && path[0] == '$' && path[1] == '.')
        {
            return new Binding(path[2..])
            {
                RelativeSource = RelativeSource.Self,
                FallbackValue = DependencyProperty.UnsetValue
            };
        }

        // #.path — data model: bind to DataContext only, no codeBehind fallback
        if (path.Length >= 2 && path[0] == '#' && path[1] == '.')
        {
            return new Binding(path[2..])
            {
                FallbackValue = DependencyProperty.UnsetValue
            };
        }

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
        var dp = DependencyProperty.FromName(type, propertyName);
        if (dp != null)
        {
            dependencyProperty = dp;
            return true;
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

    // ── Namespace type registry (populated by build-time generated code) ──

    private static readonly ConcurrentDictionary<string, Type> RegisteredNamespaceTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a type by its simple name so that Razor expressions can resolve it
    /// without a fully qualified name. Called from build-time generated module initializers
    /// based on @using directives found in .jalxaml files.
    /// </summary>
    public static void RegisterNamespaceType(string simpleName, Type type)
    {
        if (!string.IsNullOrWhiteSpace(simpleName) && type != null)
            RegisteredNamespaceTypes.TryAdd(simpleName, type);
    }

    internal static Type? TryResolveRegisteredType(string name)
    {
        return RegisteredNamespaceTypes.TryGetValue(name, out var type) ? type : null;
    }

    // ── Global section registry (cross-file @section / @RenderSection) ──

    private static readonly ConcurrentDictionary<string, string> GlobalSections = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a named section's XAML content globally so it can be
    /// referenced via <c>@RenderSection("Name")</c> from any JALXAML file.
    /// </summary>
    /// <summary>Raised when a section is registered or updated.</summary>
    public static event Action<string>? SectionRegistered;

    /// <summary>Raised when a section is unregistered.</summary>
    public static event Action<string>? SectionUnregistered;

    public static void RegisterSection(string name, string xamlContent)
    {
        if (!string.IsNullOrWhiteSpace(name) && xamlContent != null)
        {
            GlobalSections[name] = xamlContent;
            SectionRegistered?.Invoke(name);
        }
    }

    /// <summary>
    /// Removes a globally registered section.
    /// </summary>
    public static void UnregisterSection(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            GlobalSections.TryRemove(name, out _);
            SectionUnregistered?.Invoke(name);
        }
    }

    internal static bool TryGetGlobalSection(string name, out string content)
    {
        return GlobalSections.TryGetValue(name, out content!);
    }

    // ── Compiled section factories (SourceGenerator-lowered @section) ──
    //
    // The runtime preprocessor path registers a section's raw XAML *string*
    // (RegisterSection above); RazorSectionHost then re-parses it via
    // XamlReader.Parse on render. The SG instead compiles the @section body to a
    // straight-line element factory and registers the delegate here, so the host
    // can invoke it directly with no document re-parse. Both stores share the
    // SectionRegistered/SectionUnregistered events and the same name space; the
    // host prefers a factory and falls back to the string store, so SG-compiled
    // and runtime-parsed documents interoperate.

    private static readonly ConcurrentDictionary<string, Func<object?>> SectionFactories = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a compiled <c>@section</c> body factory. Emitted by the
    /// SourceGenerator into the defining component's <c>InitializeComponent</c>.
    /// Each invocation produces a fresh visual tree (factory semantics, matching
    /// the runtime which re-parses the section XAML per host). Fires
    /// <see cref="SectionRegistered"/> so any already-loaded
    /// <see cref="RazorSectionHost"/> for this name re-renders.
    /// </summary>
    public static void RegisterSectionFactory(string name, Func<object?> factory)
    {
        if (!string.IsNullOrWhiteSpace(name) && factory != null)
        {
            SectionFactories[name] = factory;
            SectionRegistered?.Invoke(name);
        }
    }

    internal static bool TryGetSectionFactory(string name, out Func<object?> factory)
    {
        return SectionFactories.TryGetValue(name, out factory!);
    }

    // ── AOT-safe property accessor registry ──

    private static readonly ConcurrentDictionary<(Type, string), Func<object, object?>> PropertyAccessors = new();

    /// <summary>
    /// Registers a direct property accessor for a type+property combination.
    /// Called from build-time generated code to avoid reflection in AOT.
    /// </summary>
    public static void RegisterPropertyAccessor(Type type, string propertyName, Func<object, object?> accessor)
    {
        if (type != null && !string.IsNullOrWhiteSpace(propertyName) && accessor != null)
        {
            PropertyAccessors[(type, propertyName)] = accessor;
            PropertyAccessorRegistry.Register(type, propertyName, accessor);
        }
    }

    /// <summary>
    /// Registers property accessors for all properties on a type using a factory.
    /// </summary>
    public static void RegisterPropertyAccessors(Type type, IEnumerable<(string Name, Func<object, object?> Accessor)> accessors)
    {
        if (type == null || accessors == null) return;
        foreach (var (name, accessor) in accessors)
        {
            PropertyAccessors[(type, name)] = accessor;
            PropertyAccessorRegistry.Register(type, name, accessor);
        }
    }

    internal static bool TryGetPropertyAccessor(Type type, string propertyName, out Func<object, object?> accessor)
    {
        return PropertyAccessors.TryGetValue((type, propertyName), out accessor!);
    }

    internal sealed record ExpressionMetadata(string ExpressionId, string Expression, IReadOnlyList<string> Dependencies);
}
