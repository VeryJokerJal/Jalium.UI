using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Data;

namespace Jalium.UI;

/// <summary>
/// Contains property setters that can be shared between instances of a type.
/// </summary>
[ContentProperty("Setters")]
public sealed class Style
{
    private readonly List<Setter> _setters = new();
    private readonly List<EventSetter> _eventSetters = new();
    private readonly List<TriggerBase> _triggers = new();
    private bool _isSealed;

    /// <summary>
    /// Gets or sets the type for which this style is intended.
    /// </summary>
    public Type? TargetType { get; set; }

    /// <summary>
    /// Gets or sets a style that is the basis of the current style.
    /// </summary>
    public Style? BasedOn { get; set; }

    /// <summary>
    /// Gets the collection of property setters.
    /// </summary>
    public IList<Setter> Setters => _setters;

    /// <summary>
    /// Gets the collection of event setters.
    /// </summary>
    public IList<EventSetter> EventSetters => _eventSetters;

    /// <summary>
    /// Gets the collection of triggers.
    /// </summary>
    public IList<TriggerBase> Triggers => _triggers;

    /// <summary>
    /// Gets a value that indicates whether the style is read-only.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class.
    /// </summary>
    public Style()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class with the specified target type.
    /// </summary>
    /// <param name="targetType">The type for which this style is intended.</param>
    public Style(Type targetType)
    {
        TargetType = targetType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class with the specified target type and base style.
    /// </summary>
    /// <param name="targetType">The type for which this style is intended.</param>
    /// <param name="basedOn">A style that is the basis of the current style.</param>
    public Style(Type targetType, Style? basedOn)
    {
        TargetType = targetType;
        BasedOn = basedOn;
    }

    /// <summary>
    /// Seals the style so that it can no longer be modified.
    /// </summary>
    public void Seal()
    {
        _isSealed = true;
    }

    /// <summary>
    /// Applies this style to the specified framework element.
    /// </summary>
    /// <param name="element">The element to apply the style to.</param>
    internal void Apply(FrameworkElement element)
    {
        Apply(element, null);
    }

    private void Apply(FrameworkElement element, HashSet<Style>? visited)
    {
        // Apply base style first (with cycle detection)
        if (BasedOn != null)
        {
            visited ??= new HashSet<Style>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(BasedOn))
                return; // Cycle detected, stop recursion
            BasedOn.Apply(element, visited);
        }

        // Apply setters
        foreach (var setter in _setters)
        {
            setter.Apply(element);
        }

        // Apply event setters
        foreach (var eventSetter in _eventSetters)
        {
            eventSetter.Apply(element);
        }

        // Apply triggers
        foreach (var trigger in _triggers)
        {
            trigger.ParentStyle = this;
            trigger.Attach(element);
        }
    }

    /// <summary>
    /// Removes this style from the specified framework element.
    /// </summary>
    /// <param name="element">The element to remove the style from.</param>
    internal void Remove(FrameworkElement element)
    {
        Remove(element, null);
    }

    private void Remove(FrameworkElement element, HashSet<Style>? visited)
    {
        // Remove triggers
        foreach (var trigger in _triggers)
        {
            trigger.Detach(element);
        }

        // Remove event setters
        foreach (var eventSetter in _eventSetters)
        {
            eventSetter.Remove(element);
        }

        // Remove setters (in reverse order)
        for (int i = _setters.Count - 1; i >= 0; i--)
        {
            _setters[i].Remove(element);
        }

        // Remove base style (with cycle detection)
        if (BasedOn != null)
        {
            visited ??= new HashSet<Style>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(BasedOn))
                return; // Cycle detected, stop recursion
            BasedOn.Remove(element, visited);
        }
    }

    /// <summary>
    /// 上层（典型为 Jalium.UI.Xaml 的 TypeConverterRegistry）注入的字符串值转换器。
    /// Setter / TriggerBase 内部 <c>ConvertValueIfNeeded</c> 的 hardcoded fast-path 命中
    /// 不到目标类型时会回落到这里，从而把 jalxaml 里写出的字符串值（例如
    /// <c>Cursor="Hand"</c>、自定义 Brush 名等）正确转成目标 DP 类型，而不是把原始字符串
    /// 塞进 layer 让渲染层强转崩溃。
    /// </summary>
    internal static Func<string, Type, object?>? StringValueConverter { get; set; }
}

/// <summary>
/// Represents a setter that sets a property value.
/// </summary>
[ContentProperty("Value")]
public sealed class Setter
{
    /// <summary>
    /// Gets or sets the property to set.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the value to set.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets the name of the element to apply the setter to.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// Gets or sets the unresolved property name for deferred resolution.
    /// When a Setter has a TargetName pointing to a different element type than the Style's TargetType,
    /// the DependencyProperty cannot be resolved at parse time. The property name is stored here
    /// and resolved at runtime against the actual target element type.
    /// </summary>
    public string? PropertyName { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Setter"/> class.
    /// </summary>
    public Setter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Setter"/> class with the specified property and value.
    /// </summary>
    /// <param name="property">The property to set.</param>
    /// <param name="value">The value to set.</param>
    public Setter(DependencyProperty property, object? value)
    {
        Property = property;
        Value = value;
    }

    /// <summary>
    /// Applies this setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element)
    {
        var target = GetTarget(element);
        if (target == null)
            return;

        // Resolve the property - may need deferred resolution for TargetName setters
        var resolvedProperty = Property;
        if (resolvedProperty == null && PropertyName != null)
        {
            resolvedProperty = ResolveDependencyPropertyByName(PropertyName, target.GetType());
        }
        if (resolvedProperty == null)
            return;

        // Resolve the actual property on the target type
        // This handles the case where the property was resolved against the Style's TargetType
        // but the setter targets a different element type via TargetName
        var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
        if (actualProperty == null)
            return;

        // Don't override local values - WPF style behavior
        // Local values have higher precedence than style values
        if (target.HasLocalValue(actualProperty))
            return;

        if (Value is IDynamicResourceReference dynamicReference)
        {
            DynamicResourceBindingOperations.SetDynamicResource(
                target,
                actualProperty,
                dynamicReference.ResourceKey,
                DependencyObject.LayerValueSource.StyleSetter);
            return;
        }

        // Setter.Value 可以是一个 BindingBase（典型场景：jalxaml 里写
        // <Setter Property="Foo" Value="{TemplateBinding Bar}" /> 或 RelativeSource Binding）。
        // 之前会把整个 BindingBase 当成属性值塞进 layer，OnRender 时强转成 Brush 等
        // 目标类型直接抛 InvalidCastException — 控件渲染崩溃。这里改为标准 WPF 行为：
        // 让 binding 在目标 DP 上建立连接，由 BindingExpression 自行把 source 值流到 layer。
        if (Value is BindingBase binding)
        {
            target.SetBinding(actualProperty, binding);
            return;
        }

        // Convert value to the correct type if needed
        var valueToSet = ConvertValueIfNeeded(Value, actualProperty.PropertyType);

        // Defense (WPF StyleHelper parity): if the post-conversion value cannot legally inhabit the
        // target DP — an unconvertible string / type-mismatched object into a value-type property, or a
        // null into a non-nullable value type — do NOT pin it into the StyleSetter layer; the getter
        // would throw InvalidCastException / NullReferenceException at layout. Degrade to default by
        // clearing this layer's contribution, mirroring the TemplateBinding transfer guards.
        if (!actualProperty.IsValidType(valueToSet))
        {
            target.ClearLayerValue(actualProperty, DependencyObject.LayerValueSource.StyleSetter);
            return;
        }

        target.SetLayerValue(actualProperty, valueToSet, DependencyObject.LayerValueSource.StyleSetter);
    }

    /// <summary>
    /// Converts a value to the target type if needed.
    /// </summary>
    private static object? ConvertValueIfNeeded(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Handle nullable types - get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;

        // String to target type conversion
        if (value is string stringValue)
        {
            // Numeric / bool / enum string conversions are best-effort: on an
            // unparseable value we fall through to `return value` (below), so the
            // caller's IsValidType guard (Setter.Apply / ApplyTriggerSetters) skips
            // the setter instead of this throwing and tearing down the whole style
            // application. (Real bug this caught: a default StatusBar-family trigger
            // setting a double property to "Auto" — WPF's spelling of double.NaN —
            // used to throw FormatException out of double.Parse and crash startup.)
            if (actualType == typeof(double))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return double.NaN;
                return double.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble)
                    ? parsedDouble
                    : value;
            }
            if (actualType == typeof(float))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return float.NaN;
                return float.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedFloat)
                    ? parsedFloat
                    : value;
            }
            if (actualType == typeof(int))
                return int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedInt)
                    ? parsedInt
                    : value;
            if (actualType == typeof(bool))
                return bool.TryParse(stringValue, out var parsedBool) ? parsedBool : value;
            if (actualType.IsEnum)
                return Enum.TryParse(actualType, stringValue, ignoreCase: true, out var parsedEnum) ? parsedEnum : value;
            if (actualType == typeof(CornerRadius))
                return ParseCornerRadius(stringValue);
            if (actualType == typeof(Thickness))
                return ParseThickness(stringValue);
            if (actualType == typeof(Cursor))
                return ParseCursor(stringValue);

            // 没命中 hardcoded 列表 — 走 Style 提供的字符串值转换器钩子。
            // 详细说明见 TriggerBase.ConvertValueIfNeeded 的同名 fallback。
            var converter = Style.StringValueConverter;
            if (converter != null)
            {
                try
                {
                    var converted = converter(stringValue, actualType);
                    if (converted != null && actualType.IsInstanceOfType(converted))
                        return converted;
                }
                catch
                {
                    // 转换失败不抛 — 让上层防御逻辑处理。
                }
            }
        }

        return value;
    }

    private static Cursor? ParseCursor(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (Enum.TryParse<CursorType>(trimmed, ignoreCase: true, out var cursorType))
            return new Cursor(cursorType);

        return null;
    }

    /// <summary>
    /// Parses a CornerRadius from a string value.
    /// </summary>
    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new CornerRadius(0)
        };
    }

    /// <summary>
    /// Parses a Thickness from a string value.
    /// </summary>
    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Removes this setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element)
    {
        var target = GetTarget(element);
        if (target == null) return;

        // Resolve the property - may need deferred resolution for TargetName setters
        var resolvedProperty = Property;
        if (resolvedProperty == null && PropertyName != null)
        {
            resolvedProperty = ResolveDependencyPropertyByName(PropertyName, target.GetType());
        }
        if (resolvedProperty == null) return;

        // Resolve the actual property on the target type
        var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
        if (actualProperty == null) return;

        DynamicResourceBindingOperations.ClearDynamicResource(target, actualProperty);
        target.ClearLayerValue(actualProperty, DependencyObject.LayerValueSource.StyleSetter);
    }

    /// <summary>
    /// Resolves the actual DependencyProperty for the target element type.
    /// This handles the case where the property was resolved against the Style's TargetType
    /// but the setter targets a different element type via TargetName.
    /// </summary>
    private static DependencyProperty? ResolvePropertyForTarget(DependencyProperty originalProperty, FrameworkElement target)
    {
        var targetType = target.GetType();

        // If the property is already from this type or an ancestor, use it directly
        if (originalProperty.OwnerType.IsAssignableFrom(targetType))
        {
            return originalProperty;
        }

        // Resolve via the AOT-safe DependencyProperty registry (no reflection).
        return DependencyProperty.FromName(targetType, originalProperty.Name) ?? originalProperty;
    }

    /// <summary>
    /// Resolves a DependencyProperty by name on the target type.
    /// Used for deferred resolution when the property couldn't be resolved at parse time.
    /// </summary>
    internal static DependencyProperty? ResolveDependencyPropertyByName(string propertyName, Type targetType)
    {
        // AOT-safe lookup via the DependencyProperty registry.
        return DependencyProperty.FromName(targetType, propertyName);
    }

    private FrameworkElement? GetTarget(FrameworkElement element)
    {
        if (string.IsNullOrEmpty(TargetName))
            return element;

        // Look up named element in the template scope
        // First, try using the element's FindName method
        if (element.FindName(TargetName) is FrameworkElement found)
        {
            return found;
        }

        // If that fails, search the visual tree starting from the element
        int visitedNodes = 0;
        var result = SearchVisualTreeForName(element, TargetName, ref visitedNodes);
        return result;
    }

    /// <summary>
    /// Recursively searches the visual tree for an element with the specified name.
    /// </summary>
    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string name, ref int visitedNodes)
    {
        if (visual == null) return null;
        visitedNodes++;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == name)
        {
            return fe;
        }

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var result = SearchVisualTreeForName(child, name, ref visitedNodes);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>
/// Base class for triggers that conditionally apply property values.
/// </summary>
[ContentProperty("Setters")]
public abstract class TriggerBase
{
    /// <summary>
    /// Gets the collection of setters to apply when the trigger is active.
    /// </summary>
    public IList<Setter> Setters { get; } = new List<Setter>();

    /// <summary>
    /// Tracks which properties this trigger has set for each element.
    /// Used to properly decrement the active count in shared storage.
    /// </summary>
    private readonly HashSet<(FrameworkElement, DependencyProperty)> _activeSetters = new();

    /// <summary>
    /// Gets or sets the parent style that contains this trigger.
    /// </summary>
    internal Style? ParentStyle { get; set; }

    /// <summary>
    /// Gets or sets the parent template triggers collection.
    /// This is set when the trigger is attached as part of a ControlTemplate.
    /// </summary>
    internal IList<TriggerBase>? ParentTemplateTriggers { get; set; }

    /// <summary>
    /// Attaches this trigger to the specified element.
    /// </summary>
    internal abstract void Attach(FrameworkElement element);

    /// <summary>
    /// Detaches this trigger from the specified element.
    /// </summary>
    internal abstract void Detach(FrameworkElement element);

    /// <summary>
    /// Gets whether this trigger is currently active for the specified element.
    /// </summary>
    internal abstract bool IsActiveForElement(FrameworkElement element);

    /// <summary>
    /// Applies the trigger's setters. WPF semantics: trigger order in the declaring
    /// collection (Style.Triggers / ControlTemplate.Triggers) determines precedence —
    /// later triggers win over earlier ones for the same target property. We honor that
    /// here by SKIPPING setters whose (target, dp) is *already owned by a later sibling
    /// trigger that is also active*. Otherwise an earlier-defined trigger activating
    /// AFTER a later one (e.g. user hovers a RadioButton that's already IsChecked=true)
    /// would clobber the later trigger's value just because the layer dictionary uses
    /// last-write-wins — which is exactly the "hover residue on a checked button" bug.
    /// </summary>
    protected void ApplyTriggerSetters(FrameworkElement element)
    {
        var layerSource = ParentTemplateTriggers != null
            ? DependencyObject.LayerValueSource.TemplateTrigger
            : DependencyObject.LayerValueSource.StyleTrigger;

        // Build the set of (target, dp) keys owned by any *later* active sibling.
        // If a key is in this set, our setter for it is suppressed — the later sibling
        // already wrote (or will write) its own value and must remain authoritative.
        var laterSiblingOwned = ComputeLaterSiblingOwnedKeys(element);

        foreach (var setter in Setters)
        {
            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null)
                continue;

            var resolvedProperty = setter.Property;
            if (resolvedProperty == null && setter.PropertyName != null)
                resolvedProperty = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
            if (resolvedProperty == null)
                continue;

            var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
            if (actualProperty == null)
                continue;

            var key = (target, actualProperty);

            // Track that this trigger conceptually owns this property — even if we end up
            // skipping the write below, we still need to remember the ownership so that
            // RemoveTriggerSetters can later re-apply ours when the later sibling deactivates.
            _activeSetters.Add(key);

            // Honor WPF precedence: defer to any active later sibling.
            if (laterSiblingOwned.Contains(key))
            {
                EnsureSetterInvalidation(target, actualProperty);
                continue;
            }

            if (setter.Value is IDynamicResourceReference dynamicReference)
            {
                DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, dynamicReference.ResourceKey, layerSource);
                EnsureSetterInvalidation(target, actualProperty);
                continue;
            }

            // 触发器内的 Setter.Value 也可能是 BindingBase（{TemplateBinding ...} /
            // {Binding ..., RelativeSource={RelativeSource TemplatedParent}} 等）。
            // 直接走 SetLayerValue 会把 BindingBase 当成属性值灌进 DP，触发渲染时
            // 类型强转崩溃。WPF 行为：让 binding 在 trigger 激活期间建立连接，
            // 失活时由 RemoveTriggerSetters 调用 ClearBinding 还原。
            if (setter.Value is BindingBase binding)
            {
                target.SetBinding(actualProperty, binding);
                EnsureSetterInvalidation(target, actualProperty);
                continue;
            }

            // Trigger 走动态状态切换路径，必须参与自动过渡 — 这是 hover/pressed
            // 反馈动画的入口；Style.Setter (baseline) 走 allowAutoTransition=false。
            var valueToSet = ConvertValueIfNeeded(setter.Value, actualProperty.PropertyType);

            // 防御（WPF StyleHelper 对齐）：转换后若值仍无法合法存入目标 DP —— 类型不匹配的对象、
            // 没命中转换器的字符串，或 null 写入非 nullable 值类型 —— 跳过本 setter，而不是把脏值塞进
            // layer（否则渲染时从该 DP 强转目标类型会抛 InvalidCastException / NullReferenceException）。
            // 这取代了之前“转换前判断 + 排除 string”的守卫——后者漏掉了无法转换的字符串这一情形。
            if (!actualProperty.IsValidType(valueToSet))
            {
                continue;
            }

            target.SetLayerValue(actualProperty, valueToSet, layerSource, allowAutoTransition: true);
            EnsureSetterInvalidation(target, actualProperty);
        }
    }

    /// <summary>
    /// Returns the set of (target, dp) keys that any *later* sibling trigger (in the
    /// declaring collection) currently owns for the given element. Used by
    /// ApplyTriggerSetters to defer to later-defined triggers (WPF precedence).
    /// </summary>
    private HashSet<(FrameworkElement, DependencyProperty)> ComputeLaterSiblingOwnedKeys(FrameworkElement element)
    {
        var result = new HashSet<(FrameworkElement, DependencyProperty)>();

        IList<TriggerBase>? siblings = ParentStyle?.Triggers as IList<TriggerBase> ?? ParentTemplateTriggers;
        if (siblings == null)
            return result;

        var myIndex = siblings.IndexOf(this);
        if (myIndex < 0)
            return result;

        for (int i = myIndex + 1; i < siblings.Count; i++)
        {
            var later = siblings[i];
            if (!later.IsActiveForElement(element))
                continue;

            foreach (var setter in later.Setters)
            {
                var target = GetSetterTarget(element, setter.TargetName);
                if (target == null) continue;

                var resolvedProp = setter.Property;
                if (resolvedProp == null && setter.PropertyName != null)
                    resolvedProp = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
                if (resolvedProp == null) continue;

                var actualProperty = ResolvePropertyForTarget(resolvedProp, target);
                if (actualProperty == null) continue;

                result.Add((target, actualProperty));
            }
        }

        return result;
    }

    /// <summary>
    /// 当 setter 改的 DP 没有 metadata-level invalidation callback 时（例如纯数据 DP），
    /// 显式给 target 安排一次 visual 失效，否则改完没人通知 window 重绘。
    /// 已注册 callback 的 DP（OpacityProperty / RenderTransformProperty / Border.Background
    /// 等）由 SetLayerValue → MutateValue → OnPropertyChanged → metadata callback
    /// 链路自然 invalidate，**不**走这条兜底——避免对每个 setter 都 paranoid
    /// 整体 InvalidateVisual。compositionOnly DP 路由到 InvalidateComposition。
    /// </summary>
    private static void EnsureSetterInvalidation(FrameworkElement target, DependencyProperty dp)
    {
        var metadata = dp.GetMetadata(target.GetType());
        if (metadata.PropertyChangedCallback != null)
            return; // metadata callback 已经处理了 invalidation

        if (metadata is FrameworkPropertyMetadata fpm && fpm.AffectsCompositionOnly)
        {
            target.InvalidateComposition();
        }
        else
        {
            target.InvalidateVisual();
        }
    }

    /// <summary>
    /// Resolves the actual DependencyProperty for the target element type.
    /// This handles the case where the property was resolved against the Style's TargetType
    /// but the setter targets a different element type via TargetName.
    /// </summary>
    private static DependencyProperty? ResolvePropertyForTarget(DependencyProperty originalProperty, FrameworkElement target)
    {
        var targetType = target.GetType();

        // If the property is already from this type or an ancestor, use it directly
        if (originalProperty.OwnerType.IsAssignableFrom(targetType))
        {
            return originalProperty;
        }

        // Resolve via the AOT-safe DependencyProperty registry (no reflection).
        return DependencyProperty.FromName(targetType, originalProperty.Name) ?? originalProperty;
    }

    /// <summary>
    /// Converts a value to the target type if needed.
    /// </summary>
    protected static object? ConvertValueIfNeeded(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Handle nullable types - get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;

        // String to target type conversion
        if (value is string stringValue)
        {
            // Numeric / bool / enum string conversions are best-effort: on an
            // unparseable value we fall through to `return value` (below), so the
            // caller's IsValidType guard (Setter.Apply / ApplyTriggerSetters) skips
            // the setter instead of this throwing and tearing down the whole style
            // application. (Real bug this caught: a default StatusBar-family trigger
            // setting a double property to "Auto" — WPF's spelling of double.NaN —
            // used to throw FormatException out of double.Parse and crash startup.)
            if (actualType == typeof(double))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return double.NaN;
                return double.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble)
                    ? parsedDouble
                    : value;
            }
            if (actualType == typeof(float))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return float.NaN;
                return float.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedFloat)
                    ? parsedFloat
                    : value;
            }
            if (actualType == typeof(int))
                return int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedInt)
                    ? parsedInt
                    : value;
            if (actualType == typeof(bool))
                return bool.TryParse(stringValue, out var parsedBool) ? parsedBool : value;
            if (actualType.IsEnum)
                return Enum.TryParse(actualType, stringValue, ignoreCase: true, out var parsedEnum) ? parsedEnum : value;
            if (actualType == typeof(CornerRadius))
                return ParseCornerRadius(stringValue);
            if (actualType == typeof(Thickness))
                return ParseThickness(stringValue);
            if (actualType == typeof(Cursor))
                return ParseCursor(stringValue);

            // 没命中 hardcoded 列表 — 走 Style 提供的字符串值转换器钩子（典型为 Xaml 的
            // TypeConverterRegistry 在 ModuleInitializer 里注入）。这避免了把 Cursor /
            // Brush / GridLength 等框架内置类型在 Core 层重复实现一遍。
            var converter = Style.StringValueConverter;
            if (converter != null)
            {
                try
                {
                    var converted = converter(stringValue, actualType);
                    if (converted != null && actualType.IsInstanceOfType(converted))
                        return converted;
                }
                catch
                {
                    // 转换失败不抛 — 留给后续防御逻辑（如 ApplyTriggerSetters 的类型不匹配跳过）
                    // 处理；否则一个错配的 Setter 会拖垮整个 Style 加载。
                }
            }
        }

        return value;
    }

    /// <summary>
    /// 把字符串解析成 <see cref="Cursor"/>。Core 自己处理这条路径是因为 Cursor 类型也在
    /// Core 程序集里，但完整的 CursorConverter 在 Jalium.UI.Input — Style.cs 不能反向引用。
    /// </summary>
    private static Cursor? ParseCursor(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (Enum.TryParse<CursorType>(trimmed, ignoreCase: true, out var cursorType))
            return new Cursor(cursorType);

        return null;
    }

    /// <summary>
    /// Parses a CornerRadius from a string value.
    /// </summary>
    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new CornerRadius(0)
        };
    }

    /// <summary>
    /// Parses a Thickness from a string value.
    /// </summary>
    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Removes the trigger's setters when this trigger deactivates. Mirrors WPF precedence
    /// (later sibling wins) by recomputing what the layer value should be for each owned
    /// (target, dp) key:
    ///   – If any other sibling is still active and owns the key, write the LATEST
    ///     (declaration-order) sibling's value to the layer in one mutation. This avoids
    ///     a Clear→Set sequence that would fire two transitions back-to-back and flash
    ///     baseline (the visible source of "hover residue" on a still-checked button).
    ///   – If no sibling takes over, ClearLayerValue so the property falls back to baseline.
    /// Skips the layer write entirely when this trigger had been suppressed by a later
    /// sibling during ApplyTriggerSetters (layer was never ours — leaving it alone is
    /// correct, and any redundant SetLayerValue here would needlessly restart transitions).
    /// </summary>
    protected void RemoveTriggerSetters(FrameworkElement element)
    {
        var layerSource = ParentTemplateTriggers != null
            ? DependencyObject.LayerValueSource.TemplateTrigger
            : DependencyObject.LayerValueSource.StyleTrigger;

        // (1) Resolve own setters: setter list → (target, dp, setter) entries that we previously
        //     claimed via _activeSetters in ApplyTriggerSetters (whether we actually wrote the
        //     layer or were suppressed by a later sibling).
        var ownSetters = new List<(FrameworkElement Target, DependencyProperty Property, Setter Setter)>();
        var ownKeys = new HashSet<(FrameworkElement, DependencyProperty)>();

        foreach (var setter in Setters)
        {
            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null) continue;

            var resolvedProperty = setter.Property;
            if (resolvedProperty == null && setter.PropertyName != null)
                resolvedProperty = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
            if (resolvedProperty == null) continue;

            var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
            if (actualProperty == null) continue;

            var key = (target, actualProperty);
            if (!_activeSetters.Contains(key)) continue;

            ownSetters.Add((target, actualProperty, setter));
            ownKeys.Add(key);
        }

        if (ownSetters.Count == 0)
            return;

        // (2) For each owned key, walk siblings in declaration order to find:
        //     – the latest still-active sibling owning the key (takeover)
        //     – whether any later sibling currently owns the key (we were suppressed)
        var takeoverSetter = new Dictionary<(FrameworkElement, DependencyProperty), Setter>();
        var suppressedByLater = new HashSet<(FrameworkElement, DependencyProperty)>();

        IList<TriggerBase>? siblings = ParentStyle?.Triggers as IList<TriggerBase> ?? ParentTemplateTriggers;
        if (siblings != null)
        {
            int myIndex = siblings.IndexOf(this);
            for (int i = 0; i < siblings.Count; i++)
            {
                var sibling = siblings[i];
                if (sibling == this) continue;
                if (!sibling.IsActiveForElement(element)) continue;

                foreach (var setter in sibling.Setters)
                {
                    var target = GetSetterTarget(element, setter.TargetName);
                    if (target == null) continue;

                    var resolvedProp = setter.Property;
                    if (resolvedProp == null && setter.PropertyName != null)
                        resolvedProp = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
                    if (resolvedProp == null) continue;

                    var actualProperty = ResolvePropertyForTarget(resolvedProp, target);
                    if (actualProperty == null) continue;

                    var key = (target, actualProperty);
                    if (!ownKeys.Contains(key)) continue;

                    // Later-defined active sibling overwrites earlier — keep updating so the
                    // last write wins. Also note whether this sibling is past our own index:
                    // if any later sibling owns the key, our SetLayerValue was suppressed,
                    // so RemoveTriggerSetters must leave the layer alone (layer is theirs).
                    takeoverSetter[key] = setter;
                    if (myIndex >= 0 && i > myIndex)
                        suppressedByLater.Add(key);
                }
            }
        }

        // (3) Per owned key: either layer is already someone else's (suppressed-by-later),
        //     a take-over sibling needs to claim it, or we ClearLayerValue back to baseline.
        foreach (var (target, actualProperty, setter) in ownSetters)
        {
            var key = (target, actualProperty);
            _activeSetters.Remove(key);

            // Tear down any binding/dynamic-resource that *we* established.
            // ApplyTriggerSetters skips SetBinding/SetDynamicResource when suppressed by a
            // later sibling, so there's nothing of ours to tear down in that case.
            bool weActuallyWroteLayer = !suppressedByLater.Contains(key);
            if (weActuallyWroteLayer)
            {
                if (setter.Value is IDynamicResourceReference)
                    DynamicResourceBindingOperations.ClearDynamicResource(target, actualProperty);
                else if (setter.Value is BindingBase)
                    target.ClearBinding(actualProperty);
            }

            if (suppressedByLater.Contains(key))
            {
                // Layer belongs to a later sibling that's still active — leave it untouched.
                continue;
            }

            if (takeoverSetter.TryGetValue(key, out var reapplySetter))
            {
                if (reapplySetter.Value is IDynamicResourceReference dynamicReference)
                {
                    DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, dynamicReference.ResourceKey, layerSource);
                }
                else if (reapplySetter.Value is BindingBase reapplyBinding)
                {
                    target.SetBinding(actualProperty, reapplyBinding);
                }
                else
                {
                    var valueToSet = ConvertValueIfNeeded(reapplySetter.Value, actualProperty.PropertyType);
                    target.SetLayerValue(actualProperty, valueToSet, layerSource, allowAutoTransition: true);
                }
            }
            else
            {
                target.ClearLayerValue(actualProperty, layerSource, allowAutoTransition: true);
            }

            EnsureSetterInvalidation(target, actualProperty);
        }
    }

    /// <summary>
    /// Gets the target element for a setter, handling TargetName lookup.
    /// Also reused by <see cref="Trigger"/> / <see cref="MultiTrigger"/> for SourceName
    /// (same logic: FindName 优先，失败则从 templated parent 起做一次 visual-tree 搜索）。
    /// </summary>
    protected static FrameworkElement? GetSetterTarget(FrameworkElement element, string? targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return element;

        // Look up named element in the template scope
        if (element.FindName(targetName) is FrameworkElement found)
            return found;

        // If that fails, search the visual tree starting from the element
        int visitedNodes = 0;
        var result = SearchVisualTreeForName(element, targetName, ref visitedNodes);
        return result;
    }

    /// <summary>
    /// Recursively searches the visual tree for an element with the specified name.
    /// </summary>
    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string name, ref int visitedNodes)
    {
        if (visual == null) return null;
        visitedNodes++;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == name)
            return fe;

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var result = SearchVisualTreeForName(child, name, ref visitedNodes);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Clears all stored pre-trigger values for an element.
    /// </summary>
    protected void ClearPreTriggerValues(FrameworkElement element)
    {
        // Clear this trigger's tracking
        var keysToRemove = _activeSetters.Where(k => k.Item1 == element).ToList();
        foreach (var key in keysToRemove)
        {
            _activeSetters.Remove(key);
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when a property value equals a specified value.
/// </summary>
public sealed class Trigger : TriggerBase
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        /// <summary>
        /// 实际被订阅的"源"元素：SourceName=null 时就是 templated parent；
        /// SourceName 指向命名模板部件时是该部件。Detach 时按此还原订阅。
        /// </summary>
        public FrameworkElement? SourceElement;
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    /// <summary>
    /// Gets or sets the property that activates the trigger.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the value that activates the trigger.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 可选——监听具名模板部件而非 templated parent 本身的 <see cref="Property"/>。
    /// 典型用法 <c>&lt;Trigger SourceName="PART_Btn" Property="IsMouseOver"&gt;</c>：
    /// 按钮自身 hover 触发 trigger，Setter 可改 sibling 命名部件的属性，从而把按钮 hover
    /// 反馈传染到模板里其它视觉（例如图标 Fill 切换）。
    /// 解析时机：trigger Attach 时通过 <see cref="TriggerBase.GetSetterTarget"/> 在 templated
    /// parent 的 NameScope 里查找；ControlTemplate.Triggers 在模板应用之后才 Attach，命名部件
    /// 此刻已存在。SourceName 解不到（空模板 / 拼写错误）时 trigger 静默不挂——不抛、不崩。
    /// </summary>
    public string? SourceName { get; set; }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        if (Property == null)
            return;

        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // 解析 SourceName：null/空 → 监听 templated parent 自己；否则去命名部件。
        // 解析失败（命名部件未找到）→ 整条 trigger 不挂，避免无声错位。
        var source = GetSetterTarget(element, SourceName);
        if (source == null)
            return;
        state.SourceElement = source;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            if (dp == Property)
            {
                EvaluateTriggerForElement(element, newValue);
            }
        };

        // Subscribe to property changes on the resolved source
        source.PropertyChangedInternal += state.Handler;

        // Check initial state
        var currentValue = source.GetValue(Property);
        EvaluateTriggerForElement(element, currentValue);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes on the source we subscribed to
            if (state.Handler != null && state.SourceElement != null)
            {
                state.SourceElement.PropertyChangedInternal -= state.Handler;
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (Property == null) return;
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // Convert Value to the property's type for proper comparison
        var triggerValue = ConvertValueIfNeeded(Value, Property.PropertyType);
        var shouldBeActive = Equals(currentValue, triggerValue);

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Represents a condition for a MultiTrigger.
/// </summary>
public sealed class Condition
{
    /// <summary>
    /// Gets or sets the property to evaluate.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the value that the property must equal for the condition to be true.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 可选——按命名模板部件取 <see cref="Property"/> 值（而非 templated parent 自身）。
    /// 用于跨子部件组合条件：例如 MultiTrigger 同时检查"按钮被按下"+"父控件已选中"。
    /// 解析逻辑与 <see cref="Trigger.SourceName"/> 一致，由 <see cref="MultiTrigger.Attach"/>
    /// 在挂载时一次性缓存到 condition-source 数组。
    /// </summary>
    public string? SourceName { get; set; }
}

/// <summary>
/// Represents a condition based on a data binding for a MultiDataTrigger.
/// </summary>
public sealed class BindingCondition
{
    /// <summary>
    /// Gets or sets the binding to evaluate.
    /// </summary>
    public Binding? Binding { get; set; }

    /// <summary>
    /// Gets or sets the value that the binding must equal for the condition to be true.
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// Represents a trigger that applies property values when multiple conditions are all true.
/// </summary>
[ContentProperty("Setters")]
public sealed class MultiTrigger : TriggerBase
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        /// <summary>
        /// 每条 Condition 解析到的源元素，与 <see cref="MultiTrigger.Conditions"/> 平行索引；
        /// 元素为 null 表示该 condition 的 SourceName 解析失败（命名部件不存在）。
        /// EvaluateTriggerForElement 按此数组逐条取值，避免每次重新走 visual-tree 搜索。
        /// </summary>
        public FrameworkElement?[] ConditionSources = Array.Empty<FrameworkElement?>();
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    /// <summary>
    /// Gets the collection of conditions that must all be true for the trigger to activate.
    /// </summary>
    public IList<Condition> Conditions { get; } = new List<Condition>();

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // 解析每条 Condition 的 source（SourceName=null → templated parent 自己）。
        // 收集去重后的源元素集合，handler 订阅到每个唯一源——任何一个源的相关 DP 变化
        // 都会触发整组重评估（"all must be true" 语义）。
        state.ConditionSources = new FrameworkElement?[Conditions.Count];
        var uniqueSources = new HashSet<FrameworkElement>();
        for (int i = 0; i < Conditions.Count; i++)
        {
            var source = GetSetterTarget(element, Conditions[i].SourceName);
            state.ConditionSources[i] = source;
            if (source != null) uniqueSources.Add(source);
        }

        // 没有任何可订阅的源：所有 SourceName 都没解到——直接放弃，避免无效订阅与误激活。
        if (uniqueSources.Count == 0)
            return;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            // Check if this property is one of our conditions
            bool shouldEvaluate = false;
            foreach (var condition in Conditions)
            {
                // Compare by property reference or by name as fallback
                if (dp == condition.Property ||
                    (condition.Property != null && dp.Name == condition.Property.Name))
                {
                    shouldEvaluate = true;
                    break;
                }
            }

            if (shouldEvaluate)
            {
                EvaluateTriggerForElement(element);
            }
        };

        // Subscribe handler to every distinct source so any of them firing PropertyChanged
        // wakes this MultiTrigger.
        foreach (var source in uniqueSources)
        {
            source.PropertyChangedInternal += state.Handler;
        }

        // Check initial state
        EvaluateTriggerForElement(element);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe handler from every distinct source we subscribed to in Attach.
            if (state.Handler != null && state.ConditionSources.Length > 0)
            {
                var unsubscribed = new HashSet<FrameworkElement>();
                foreach (var source in state.ConditionSources)
                {
                    if (source == null) continue;
                    if (!unsubscribed.Add(source)) continue;
                    source.PropertyChangedInternal -= state.Handler;
                }
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // All conditions must be true
        var shouldBeActive = true;

        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (condition.Property == null)
            {
                shouldBeActive = false;
                break;
            }

            // 用 Attach 时缓存的 source（与 Conditions 平行）取值。SourceName 解析失败的
            // condition（source==null）视为不满足——MultiTrigger 整体不激活。
            var source = i < state.ConditionSources.Length ? state.ConditionSources[i] : null;
            if (source == null)
            {
                shouldBeActive = false;
                break;
            }
            var currentValue = source.GetValue(condition.Property);
            var conditionValue = ConvertValueIfNeeded(condition.Value, condition.Property.PropertyType);

            if (!Equals(currentValue, conditionValue))
            {
                shouldBeActive = false;
                break;
            }
        }

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when data equals a specified value.
/// </summary>
public sealed class DataTrigger : TriggerBase
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        public BindingExpressionBase? BindingExpression;
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    // Shadow property for receiving binding updates. ONE UNIQUE attached property per DataTrigger
    // INSTANCE (lazily registered). A single property shared across all instances made two
    // DataTriggers on the SAME element clobber each other's binding — the later Attach's
    // SetBinding overwrote the earlier trigger's binding, so the earlier one never saw its value
    // and never fired (e.g. a hover DataTrigger + a selected DataTrigger on one border: hover died).
    // Distinct instances now use distinct properties; per-element attached values keep multiple
    // elements sharing one style independent. Mirrors MultiDataTrigger's per-condition shadow props.
    private static int _propertyCounter;
    private DependencyProperty? _shadowProperty;

    private DependencyProperty ShadowProperty =>
        _shadowProperty ??= DependencyProperty.RegisterAttached(
            $"_DataTriggerValue{Interlocked.Increment(ref _propertyCounter)}",
            typeof(object), typeof(DataTrigger), new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the binding that produces the value to compare with Value.
    /// </summary>
    public Binding? Binding { get; set; }

    /// <summary>
    /// Gets or sets the value that activates the trigger.
    /// </summary>
    public object? Value { get; set; }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        var shadowProp = ShadowProperty;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            if (dp == shadowProp)
            {
                EvaluateTriggerForElement(element, newValue);
            }
        };

        // Subscribe to property changes for the shadow property
        element.PropertyChangedInternal += state.Handler;

        // Set up binding to the shadow property
        if (Binding != null)
        {
            state.BindingExpression = BindingOperations.SetBinding(element, shadowProp, Binding);
        }

        // Check initial state
        var currentValue = element.GetValue(shadowProp);
        EvaluateTriggerForElement(element, currentValue);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            // Clear binding
            if (state.BindingExpression != null)
            {
                BindingOperations.ClearBinding(element, ShadowProperty);
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // Coerce the (typically XAML-authored string) Value to the binding result's RUNTIME type
        // before comparing. The property-based Trigger converts Value to Property.PropertyType; a
        // DataTrigger has no static target type, so convert against currentValue's runtime type.
        // Without this a XAML Value="True" (string) never equals a bool binding result (e.g.
        // IsMouseOver) and the trigger silently never activates.
        var triggerValue = currentValue != null ? ConvertValueIfNeeded(Value, currentValue.GetType()) : Value;
        var shouldBeActive = Equals(currentValue, triggerValue);

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when an event occurs.
/// </summary>
public sealed class EventTrigger : TriggerBase
{
    private FrameworkElement? _attachedElement;

    /// <summary>
    /// Gets or sets the name of the event that activates the trigger.
    /// </summary>
    public RoutedEvent? RoutedEvent { get; set; }

    /// <summary>
    /// Gets the collection of actions to perform when the event occurs.
    /// </summary>
    public IList<TriggerAction> Actions { get; } = new List<TriggerAction>();

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        _attachedElement = element;

        if (RoutedEvent != null)
        {
            element.AddHandler(RoutedEvent, new RoutedEventHandler(OnEventRaised));
        }
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (RoutedEvent != null)
        {
            element.RemoveHandler(RoutedEvent, new RoutedEventHandler(OnEventRaised));
        }

        _attachedElement = null;
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        // EventTriggers don't have a persistent active state - they fire on events
        return false;
    }

    private void OnEventRaised(object sender, RoutedEventArgs e)
    {
        foreach (var action in Actions)
        {
            action.Invoke(_attachedElement);
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when multiple data binding conditions are all true.
/// </summary>
[ContentProperty("Setters")]
public sealed class MultiDataTrigger : TriggerBase
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        public List<BindingExpressionBase> BindingExpressions = new();
        public List<DependencyProperty> ShadowProperties = new();
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    // Counter for generating unique shadow property names
    private static int _propertyCounter;

    /// <summary>
    /// Gets the collection of binding conditions that must all be true for the trigger to activate.
    /// </summary>
    public IList<BindingCondition> Conditions { get; } = new List<BindingCondition>();

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // Create shadow properties for each condition
        foreach (var condition in Conditions)
        {
            var propId = Interlocked.Increment(ref _propertyCounter);
            var shadowProp = DependencyProperty.RegisterAttached(
                $"_MultiDataTriggerValue{propId}",
                typeof(object),
                typeof(MultiDataTrigger),
                new PropertyMetadata(null));
            state.ShadowProperties.Add(shadowProp);
        }

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            // Check if this property is one of our shadow properties
            if (state.ShadowProperties.Contains(dp))
            {
                EvaluateTriggerForElement(element);
            }
        };

        // Subscribe to property changes
        element.PropertyChangedInternal += state.Handler;

        // Set up bindings to shadow properties
        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (condition.Binding != null)
            {
                var bindingExpr = BindingOperations.SetBinding(element, state.ShadowProperties[i], condition.Binding);
                state.BindingExpressions.Add(bindingExpr);
            }
        }

        // Check initial state
        EvaluateTriggerForElement(element);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            // Clear all bindings
            foreach (var shadowProp in state.ShadowProperties)
            {
                BindingOperations.ClearBinding(element, shadowProp);
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // All conditions must be true
        var shouldBeActive = true;

        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (i >= state.ShadowProperties.Count)
            {
                shouldBeActive = false;
                break;
            }

            var currentValue = element.GetValue(state.ShadowProperties[i]);
            // Coerce condition.Value (typically a XAML string) to the binding result's runtime type
            // before comparing — same reason as DataTrigger.EvaluateTriggerForElement: a string
            // "True" must match a bool binding result, etc.
            var conditionValue = currentValue != null
                ? ConvertValueIfNeeded(condition.Value, currentValue.GetType())
                : condition.Value;
            if (!Equals(currentValue, conditionValue))
            {
                shouldBeActive = false;
                break;
            }
        }

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Base class for actions that can be invoked by event triggers.
/// </summary>
public abstract class TriggerAction
{
    /// <summary>
    /// Invokes the action on the specified element.
    /// </summary>
    internal abstract void Invoke(FrameworkElement? element);
}

/// <summary>
/// Represents a setter that applies an event handler in a Style.
/// </summary>
public sealed class EventSetter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventSetter"/> class.
    /// </summary>
    public EventSetter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSetter"/> class with the specified event and handler.
    /// </summary>
    /// <param name="routedEvent">The routed event that this EventSetter responds to.</param>
    /// <param name="handler">The handler to assign.</param>
    public EventSetter(RoutedEvent routedEvent, Delegate handler)
    {
        Event = routedEvent;
        Handler = handler;
    }

    /// <summary>
    /// Gets or sets the particular routed event that this EventSetter responds to.
    /// </summary>
    public RoutedEvent? Event { get; set; }

    /// <summary>
    /// Gets or sets the handler to assign in this setter.
    /// </summary>
    public Delegate? Handler { get; set; }

    /// <summary>
    /// Gets or sets whether the handler should be invoked even if the event is marked as handled.
    /// </summary>
    public bool HandledEventsToo { get; set; }

    /// <summary>
    /// Applies this event setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element)
    {
        if (Event == null || Handler == null)
            return;

        element.AddHandler(Event, Handler, HandledEventsToo);
    }

    /// <summary>
    /// Removes this event setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element)
    {
        if (Event == null || Handler == null)
            return;

        element.RemoveHandler(Event, Handler);
    }
}
